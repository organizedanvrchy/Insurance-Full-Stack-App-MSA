using Insurance.Billing.DTOs;
using Insurance.Billing.Enums;
using Insurance.Billing.Exceptions;
using Insurance.Billing.Gateway;
using Insurance.Billing.Models;
using Insurance.Billing.Repositories;
using Insurance.BuildingBlocks.Exceptions;
using Insurance.Enrollments.Contracts;
using Microsoft.Extensions.Logging;

namespace Insurance.Billing.Services;

public interface ICheckoutService
{
    Task<CheckoutResultDto> PayAsync(int paymentId, int userId, CheckoutRequest request);
}

/// <summary>
/// Paying an installment, the way a real integration with a payment processor works:
///   1. A retried request (same idempotency key) gets the first attempt's result: never a second charge.
///   2. Every rule is checked BEFORE any money moves (ownership, active policy, already paid, pay in order).
///   3. The attempt is recorded (Processing), then the gateway charges the token.
///   4. Declined: recorded, and the customer is told why (HTTP 200: a decline is an outcome, not an error).
///   5. Approved: the installment is marked paid and the attempt Succeeded in one database transaction.
///      If that fails anyway (another payment won a race), the charge is refunded and recorded as Refunded.
///   6. "Save this card": the processor turns the token into a reusable reference, stored with brand and last 4.
/// The logs (ILogger) describe each attempt without the token: only brand and last 4, which are safe to show.
/// </summary>
public class CheckoutService : ICheckoutService
{
    private const string Currency = "USD";

    private readonly IPaymentRepository _payments;
    private readonly ITransactionRepository _transactions;
    private readonly ISavedMethodRepository _savedMethods;
    private readonly IEnrollmentLookup _enrollments;
    private readonly IPaymentGateway _gateway;
    private readonly ILogger<CheckoutService> _logger;

    public CheckoutService(IPaymentRepository payments, ITransactionRepository transactions, ISavedMethodRepository savedMethods,
        IEnrollmentLookup enrollments, IPaymentGateway gateway, ILogger<CheckoutService> logger)
    {
        _payments = payments;
        _transactions = transactions;
        _savedMethods = savedMethods;
        _enrollments = enrollments;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<CheckoutResultDto> PayAsync(int paymentId, int userId, CheckoutRequest request)
    {
        if ((request.PaymentMethodToken is null) == (request.SavedPaymentMethodId is null))
            throw new BusinessRuleException("Choose one payment method.");

        // 1. A retry of an attempt we already handled?
        var earlier = await _transactions.GetByKeyAsync(userId, request.IdempotencyKey);
        if (earlier is not null)
            return await ReplayAsync(earlier, paymentId);

        // 2. Can this installment be paid by this customer right now?
        var payment = await CheckPayableAsync(paymentId, userId);

        // 3. What is being charged?
        var (token, method, holderName) = await ResolveMethodAsync(request, userId);

        var (attempt, existing) = await _transactions.BeginAsync(new PaymentTransaction
        {
            PaymentId = payment.PaymentId,
            EnrollmentId = payment.EnrollmentId,
            UserId = userId,
            Amount = payment.Amount,
            Currency = Currency,
            MethodType = method.Type,
            CardBrand = method.Brand,
            CardLast4 = method.Last4,
            Provider = _gateway.Name,
            IdempotencyKey = request.IdempotencyKey,
            BillingName = request.BillingName?.Trim() ?? holderName,
            BillingPostalCode = request.BillingPostalCode?.Trim()
        });
        if (existing)   // the same key arrived twice at the same moment; the other request is handling it
            return await ReplayAsync(attempt, paymentId);

        var charge = await _gateway.ChargeAsync(new ChargeRequest(
            payment.Amount, Currency, token, request.IdempotencyKey,
            $"Insurance premium, policy {payment.EnrollmentId}, installment due {payment.DueDate:yyyy-MM-dd}",
            $"customer-{userId}", attempt.BillingPostalCode));

        // 4. Declined or failed: record it and tell the customer.
        if (!charge.Succeeded)
        {
            var status = charge.Retryable ? TransactionStatus.Failed : TransactionStatus.Declined;
            attempt = await _transactions.CompleteAsync(attempt.TransactionId, status, charge.ChargeId, charge.DeclineCode, charge.Message);
            _logger.LogWarning("Payment {Reference} for installment {PaymentId} {Status} ({DeclineCode}) via {Provider} {Method}",
                Reference(attempt), payment.PaymentId, status, charge.DeclineCode, _gateway.Name, Describe(method));
            return Result(CheckoutOutcome.Declined, charge.Message ?? "The payment was not approved.", attempt, payment);
        }

        // 5. Approved: mark the installment paid, or give the money back.
        Payment paid;
        try
        {
            paid = await _transactions.SettleAsync(payment.PaymentId, attempt.TransactionId, charge.ChargeId!, "Payment received.");
        }
        catch (Exception ex) when (ex is PaymentConflictException or PaymentNotFoundException)
        {
            var refund = await _gateway.RefundAsync(charge.ChargeId!, payment.Amount, request.IdempotencyKey + ":refund");
            await _transactions.CompleteAsync(attempt.TransactionId, TransactionStatus.Refunded, charge.ChargeId,
                "settlement_failed", $"Refunded automatically: {ex.Message}");
            _logger.LogError("Payment {Reference} was charged ({ChargeId}) but installment {PaymentId} could not be marked paid: {Reason}. Refund {RefundId}",
                Reference(attempt), charge.ChargeId, payment.PaymentId, ex.Message, refund.RefundId);
            throw;   // the customer gets the same 409 as before, and was not charged
        }

        // 6. Keep the method for next time, if asked. This must never undo a payment that went through.
        if (request.SaveMethod && request.PaymentMethodToken is not null)
            await TrySaveMethodAsync(userId, token, method, attempt.BillingName);

        attempt = (await _transactions.GetByIdAsync(attempt.TransactionId))!;
        _logger.LogInformation("Payment {Reference} for installment {PaymentId} succeeded: {Amount} {Currency} via {Provider} {Method}, charge {ChargeId}",
            Reference(attempt), paid.PaymentId, attempt.Amount, attempt.Currency, _gateway.Name, Describe(method), charge.ChargeId);
        return Result(CheckoutOutcome.Succeeded, "Payment received. Thank you!", attempt, paid);
    }

    // Same checks as before simulated payments existed, plus "pay in order", all BEFORE charging anything.
    private async Task<Payment> CheckPayableAsync(int paymentId, int userId)
    {
        var payment = await _payments.GetByIdAsync(paymentId);
        var enrollment = payment is null ? null : await _enrollments.GetAsync(payment.EnrollmentId);

        // Same answer for "doesn't exist" and "not yours", so ids can't be probed.
        if (payment is null || enrollment is null || enrollment.UserId != userId || !PaymentService.IsStillDue(payment, enrollment))
            throw new PaymentNotFoundException(paymentId);

        if (payment.PaidDate is not null)
            throw new PaymentConflictException("This installment has already been paid.");

        // An Active policy past its end date reads as Expired; its last installments can still be paid.
        if (enrollment.Status is not (EnrollmentStatus.Active or EnrollmentStatus.Expired))
            throw new PaymentConflictException("Payments can only be made on an active enrollment.");

        var schedule = await _payments.GetByEnrollmentAsync(payment.EnrollmentId);
        if (schedule.Any(p => p.PaidDate is null && p.DueDate < payment.DueDate))
            throw new PaymentConflictException("Please pay your earlier installments first.");

        return payment;
    }

    private async Task<(string Token, PaymentMethodDetails Method, string? HolderName)> ResolveMethodAsync(CheckoutRequest request, int userId)
    {
        if (request.SavedPaymentMethodId is { } savedId)
        {
            var saved = await _savedMethods.GetByIdAsync(savedId);
            if (saved is null || saved.UserId != userId || saved.Provider != _gateway.Name)
                throw new NotFoundException("That saved payment method was not found.");

            return (saved.ProviderMethodId,
                new PaymentMethodDetails(saved.MethodType, saved.CardBrand, saved.CardLast4, saved.ExpMonth, saved.ExpYear),
                saved.HolderName);
        }

        var token = request.PaymentMethodToken!;
        return (token, await _gateway.DescribeAsync(token), null);
    }

    private async Task TrySaveMethodAsync(int userId, string token, PaymentMethodDetails method, string? holderName)
    {
        try
        {
            var reusable = await _gateway.SaveForReuseAsync(token, $"customer-{userId}");
            await _savedMethods.AddAsync(new SavedPaymentMethod
            {
                UserId = userId,
                MethodType = method.Type,
                CardBrand = method.Brand,
                CardLast4 = method.Last4,
                ExpMonth = method.ExpMonth,
                ExpYear = method.ExpYear,
                HolderName = holderName,
                Provider = _gateway.Name,
                ProviderMethodId = reusable
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The payment succeeded but the payment method could not be saved for customer {UserId}", userId);
        }
    }

    // The answer to a retry: whatever happened the first time.
    private async Task<CheckoutResultDto> ReplayAsync(PaymentTransaction earlier, int paymentId)
    {
        if (earlier.PaymentId != paymentId)
            throw new ConflictException("This payment attempt was already used for another installment.");
        if (earlier.Status == TransactionStatus.Processing)
            throw new ConflictException("This payment is still being processed. Please wait a moment and refresh.");

        var payment = (await _payments.GetByIdAsync(earlier.PaymentId))!;
        var outcome = earlier.Status == TransactionStatus.Succeeded ? CheckoutOutcome.Succeeded : CheckoutOutcome.Declined;
        return Result(outcome, earlier.Message ?? "", earlier, payment);
    }

    private static CheckoutResultDto Result(CheckoutOutcome outcome, string message, PaymentTransaction t, Payment p) => new()
    {
        Outcome = outcome,
        Message = message,
        Transaction = PaymentService.MapToDto(t),
        Payment = PaymentService.MapToDto(p)
    };

    private static string Reference(PaymentTransaction t) => PaymentService.Reference(t.TransactionId);

    // What the logs show: "visa ****4242" or "PayPal". Never the token.
    private static string Describe(PaymentMethodDetails m) =>
        m.Type == PaymentMethodType.Card ? $"{m.Brand} ****{m.Last4}" : m.Type.ToString();
}
