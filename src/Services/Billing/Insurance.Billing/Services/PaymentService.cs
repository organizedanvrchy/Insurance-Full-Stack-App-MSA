using Insurance.Billing.DTOs;
using Insurance.Billing.Enums;
using Insurance.Billing.Models;
using Insurance.Billing.Repositories;
using Insurance.Enrollments.Contracts;
using Insurance.Identity.Contracts;

namespace Insurance.Billing.Services;

// Reading schedules and the payment log. Paying is CheckoutService.
public class PaymentService : IPaymentService
{
    private readonly IPaymentRepository _payments;
    private readonly ITransactionRepository _transactions;
    private readonly IEnrollmentLookup _enrollments;
    private readonly IUserDirectory _users;

    public PaymentService(IPaymentRepository payments, ITransactionRepository transactions, IEnrollmentLookup enrollments,
        IUserDirectory users)
    {
        _payments = payments;
        _transactions = transactions;
        _enrollments = enrollments;
        _users = users;
    }

    public async Task<List<PaymentDto>?> GetScheduleAsync(int enrollmentId, int userId, bool isAdmin)
    {
        var enrollment = await _enrollments.GetAsync(enrollmentId);

        // Same null for "doesn't exist" and "not yours".
        if (enrollment is null || (enrollment.UserId != userId && !isAdmin))
            return null;

        var payments = await _payments.GetByEnrollmentAsync(enrollmentId);
        return payments.Where(p => IsStillDue(p, enrollment)).Select(MapToDto).ToList();
    }

    // The payment log. With an enrollment: that policy's attempts (owner or admin). Without one: a customer sees
    // their own attempts, an admin sees everyone's, with names from the Identity service.
    public async Task<List<TransactionDto>?> GetTransactionsAsync(int userId, bool isAdmin, int? enrollmentId, TransactionStatus? status)
    {
        if (enrollmentId is { } id)
        {
            var enrollment = await _enrollments.GetAsync(id);
            if (enrollment is null || (enrollment.UserId != userId && !isAdmin))
                return null;
        }

        var list = await _transactions.SearchAsync(enrollmentId, isAdmin ? null : userId, status);
        var dtos = list.Select(MapToDto).ToList();
        if (isAdmin) await AddCustomerNamesAsync(dtos, list);
        return dtos;
    }

    // A receipt. Same null for "doesn't exist" and "not yours".
    public async Task<TransactionDto?> GetTransactionAsync(int transactionId, int userId, bool isAdmin)
    {
        var t = await _transactions.GetByIdAsync(transactionId);
        if (t is null || (t.UserId != userId && !isAdmin))
            return null;

        var dto = MapToDto(t);
        if (isAdmin) await AddCustomerNamesAsync(new List<TransactionDto> { dto }, new List<PaymentTransaction> { t });
        return dto;
    }

    private async Task AddCustomerNamesAsync(List<TransactionDto> dtos, List<PaymentTransaction> source)
    {
        var users = await _users.GetManyAsync(source.Select(t => t.UserId));
        for (var i = 0; i < dtos.Count; i++)
        {
            var user = users.GetValueOrDefault(source[i].UserId);
            dtos[i].UserId = source[i].UserId;
            dtos[i].CustomerName = user?.FullName;
            dtos[i].CustomerEmail = user?.Email;
        }
    }

    // Cancelling removes the unpaid installments, but that happens when this service receives EnrollmentCancelled,
    // a moment after the cancellation itself. Until then they are treated as already gone, so the customer sees
    // the same thing either way. Paid installments stay as history.
    internal static bool IsStillDue(Payment p, EnrollmentInfo enrollment) =>
        p.PaidDate is not null || enrollment.Status != EnrollmentStatus.Cancelled;

    // What the customer quotes to support: TXN-000042.
    internal static string Reference(int transactionId) => $"TXN-{transactionId:D6}";

    internal static PaymentDto MapToDto(Payment p) => new()
    {
        PaymentId = p.PaymentId,
        EnrollmentId = p.EnrollmentId,
        DueDate = p.DueDate,
        Amount = p.Amount,
        PaidDate = p.PaidDate,
        Status = p.Status
    };

    internal static TransactionDto MapToDto(PaymentTransaction t) => new()
    {
        TransactionId = t.TransactionId,
        Reference = Reference(t.TransactionId),
        PaymentId = t.PaymentId,
        EnrollmentId = t.EnrollmentId,
        Amount = t.Amount,
        Currency = t.Currency,
        MethodType = t.MethodType,
        CardBrand = t.CardBrand,
        CardLast4 = t.CardLast4,
        Provider = t.Provider,
        ProviderChargeId = t.ProviderChargeId,
        Status = t.Status,
        DeclineCode = t.DeclineCode,
        Message = t.Message,
        BillingName = t.BillingName,
        CreatedAt = t.CreatedAt,
        CompletedAt = t.CompletedAt
    };
}
