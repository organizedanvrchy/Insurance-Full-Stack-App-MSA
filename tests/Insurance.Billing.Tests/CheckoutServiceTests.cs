using Insurance.Billing.DTOs;
using Insurance.Billing.Enums;
using Insurance.Billing.Exceptions;
using Insurance.Billing.Gateway;
using Insurance.Billing.Models;
using Insurance.Billing.Services;
using Insurance.BuildingBlocks.Exceptions;
using Insurance.Enrollments.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Insurance.Billing.Tests;

// Paying through the (simulated) payment gateway: checks before charging, declines, idempotent retries,
// refunds when an approved charge can't be settled, and saved methods.

public class CheckoutServiceTests
{
    private const string Visa = "tok_mock_card_visa_4242_1230";            // approved (expires 12/30)
    private const string DeclinedCard = "tok_mock_card_visa_0002_1230";    // card_declined
    private const string ProcessorError = "tok_mock_card_visa_0119_1230";  // processing_error (retryable)

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly FakePaymentRepository _payments = new();
    private readonly FakeTransactionRepository _transactions;
    private readonly FakeSavedMethodRepository _saved = new();
    private readonly FakeEnrollmentLookup _enrollments = new();
    private readonly CountingGateway _gateway = new();
    private readonly CheckoutService _sut;

    public CheckoutServiceTests()
    {
        _transactions = new FakeTransactionRepository(_payments);
        _sut = new CheckoutService(_payments, _transactions, _saved, _enrollments, _gateway, NullLogger<CheckoutService>.Instance);

        // Customer 1 holds enrollment 10: installment 100 due today, then 101 next month.
        SetEnrollment(EnrollmentStatus.Active);
        _payments.Payments.Add(new Payment { PaymentId = 100, EnrollmentId = 10, DueDate = Today, Amount = 50m, Status = PaymentStatus.Pending });
        _payments.Payments.Add(new Payment { PaymentId = 101, EnrollmentId = 10, DueDate = Today.AddMonths(1), Amount = 50m, Status = PaymentStatus.Pending });
    }

    private void SetEnrollment(EnrollmentStatus status) =>
        _enrollments.Enrollments[10] = new EnrollmentInfo(10, UserId: 1, PolicyId: 1, Today.AddMonths(-1), Today.AddMonths(11), status);

    private static CheckoutRequest With(string token, string key = "attempt-0001", bool save = false) =>
        new() { PaymentMethodToken = token, IdempotencyKey = key, SaveMethod = save, BillingName = "Cal Tester", BillingPostalCode = "06902" };

    private Payment Installment(int id) => _payments.Payments.Single(p => p.PaymentId == id);

    // Approved and declined

    [Fact]
    public async Task An_approved_card_pays_the_installment_and_logs_a_receipt()
    {
        var result = await _sut.PayAsync(100, userId: 1, With(Visa));

        Assert.Equal(CheckoutOutcome.Succeeded, result.Outcome);
        Assert.Equal(PaymentStatus.Paid, result.Payment.Status);
        var t = result.Transaction;
        Assert.Equal((TransactionStatus.Succeeded, "visa", "4242", 50m), (t.Status, t.CardBrand, t.CardLast4, t.Amount));
        Assert.StartsWith("ch_mock_", t.ProviderChargeId);
        Assert.Equal("TXN-000001", t.Reference);
    }

    [Fact]
    public async Task A_declined_card_is_logged_and_explained_and_the_installment_stays_unpaid()
    {
        var result = await _sut.PayAsync(100, userId: 1, With(DeclinedCard));

        Assert.Equal(CheckoutOutcome.Declined, result.Outcome);
        Assert.Equal("Your card was declined.", result.Message);
        Assert.Equal((TransactionStatus.Declined, "card_declined"), (result.Transaction.Status, result.Transaction.DeclineCode));
        Assert.Null(Installment(100).PaidDate);
    }

    [Fact]
    public async Task A_processor_error_is_logged_as_failed_so_the_customer_can_retry()
    {
        var result = await _sut.PayAsync(100, userId: 1, With(ProcessorError));

        Assert.Equal(CheckoutOutcome.Declined, result.Outcome);
        Assert.Equal(TransactionStatus.Failed, result.Transaction.Status);
    }

    [Fact]
    public async Task A_wallet_payment_is_approved()
    {
        var result = await _sut.PayAsync(100, userId: 1, With("tok_mock_paypal"));

        Assert.Equal(CheckoutOutcome.Succeeded, result.Outcome);
        Assert.Equal(PaymentMethodType.PayPal, result.Transaction.MethodType);
        Assert.Null(result.Transaction.CardLast4);
    }

    // Idempotency: a retry never charges twice

    [Fact]
    public async Task Retrying_a_successful_attempt_replays_the_receipt_without_charging_again()
    {
        var first = await _sut.PayAsync(100, userId: 1, With(Visa, key: "attempt-0001"));
        var retry = await _sut.PayAsync(100, userId: 1, With(Visa, key: "attempt-0001"));

        Assert.Equal(1, _gateway.Charges);
        Assert.Single(_transactions.Transactions);
        Assert.Equal(CheckoutOutcome.Succeeded, retry.Outcome);
        Assert.Equal(first.Transaction.TransactionId, retry.Transaction.TransactionId);
    }

    [Fact]
    public async Task Retrying_a_declined_attempt_replays_the_decline_and_a_new_attempt_can_succeed()
    {
        await _sut.PayAsync(100, userId: 1, With(DeclinedCard, key: "attempt-0001"));
        var retry = await _sut.PayAsync(100, userId: 1, With(DeclinedCard, key: "attempt-0001"));
        var newCard = await _sut.PayAsync(100, userId: 1, With(Visa, key: "attempt-0002"));

        Assert.Equal(CheckoutOutcome.Declined, retry.Outcome);
        Assert.Equal(CheckoutOutcome.Succeeded, newCard.Outcome);
        Assert.Equal(2, _gateway.Charges);
    }

    // Every rule is checked before any money moves

    [Theory]
    [InlineData(999, 1)]   // no such installment
    [InlineData(100, 2)]   // someone else's
    public async Task Missing_and_foreign_installments_look_the_same_and_nothing_is_charged(int paymentId, int userId)
    {
        await Assert.ThrowsAsync<PaymentNotFoundException>(() => _sut.PayAsync(paymentId, userId, With(Visa)));
        Assert.Equal(0, _gateway.Charges);
        Assert.Empty(_transactions.Transactions);
    }

    [Fact]
    public async Task Installments_must_be_paid_in_order()
    {
        var ex = await Assert.ThrowsAsync<PaymentConflictException>(() => _sut.PayAsync(101, userId: 1, With(Visa)));

        Assert.Contains("earlier installments", ex.Message);
        Assert.Equal(0, _gateway.Charges);
    }

    [Fact]
    public async Task A_paid_installment_cannot_be_paid_again()
    {
        await _sut.PayAsync(100, userId: 1, With(Visa, key: "attempt-0001"));

        await Assert.ThrowsAsync<PaymentConflictException>(() => _sut.PayAsync(100, userId: 1, With(Visa, key: "attempt-0002")));
        Assert.Equal(1, _gateway.Charges);
    }

    [Fact]
    public async Task A_cancelled_policys_unpaid_installments_are_gone()
    {
        SetEnrollment(EnrollmentStatus.Cancelled);

        await Assert.ThrowsAsync<PaymentNotFoundException>(() => _sut.PayAsync(100, userId: 1, With(Visa)));
    }

    [Fact]
    public async Task A_policy_still_being_set_up_takes_no_payments()
    {
        SetEnrollment(EnrollmentStatus.Pending);

        await Assert.ThrowsAsync<PaymentConflictException>(() => _sut.PayAsync(100, userId: 1, With(Visa)));
        Assert.Equal(0, _gateway.Charges);
    }

    [Fact]
    public async Task An_expired_policy_can_still_settle_its_last_installments()
    {
        SetEnrollment(EnrollmentStatus.Expired);

        Assert.Equal(CheckoutOutcome.Succeeded, (await _sut.PayAsync(100, userId: 1, With(Visa))).Outcome);
    }

    [Theory]
    [InlineData(null, null)]          // no method
    [InlineData(Visa, 1)]             // two methods
    public async Task Exactly_one_payment_method_is_required(string? token, int? savedId)
    {
        var request = new CheckoutRequest { PaymentMethodToken = token, SavedPaymentMethodId = savedId, IdempotencyKey = "attempt-0001" };

        await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.PayAsync(100, userId: 1, request));
    }

    [Fact]
    public async Task An_unreadable_token_is_rejected_before_anything_is_recorded()
    {
        await Assert.ThrowsAsync<PaymentMethodInvalidException>(() => _sut.PayAsync(100, userId: 1, With("4242424242424242")));
        Assert.Empty(_transactions.Transactions);
    }

    // Charged, but the installment can't be marked paid

    [Fact]
    public async Task If_settling_fails_after_the_charge_the_money_is_refunded()
    {
        _transactions.SettleThrows = new PaymentConflictException("This installment has already been paid.");

        await Assert.ThrowsAsync<PaymentConflictException>(() => _sut.PayAsync(100, userId: 1, With(Visa)));

        var t = _transactions.Transactions.Single();
        Assert.Equal(TransactionStatus.Refunded, t.Status);
        Assert.Equal(new[] { t.ProviderChargeId }, _gateway.Refunds);
    }

    // Saved methods

    [Fact]
    public async Task Saving_a_card_keeps_a_reusable_reference_and_never_the_token()
    {
        await _sut.PayAsync(100, userId: 1, With(Visa, save: true));

        var saved = _saved.Methods.Single();
        Assert.Equal(("visa", "4242", 12, 2030, "Cal Tester"), (saved.CardBrand, saved.CardLast4, saved.ExpMonth, saved.ExpYear, saved.HolderName));
        Assert.StartsWith("pm_mock_", saved.ProviderMethodId);
        Assert.NotEqual(Visa, saved.ProviderMethodId);
    }

    [Fact]
    public async Task A_saved_card_can_pay_the_next_installment()
    {
        await _sut.PayAsync(100, userId: 1, With(Visa, key: "attempt-0001", save: true));
        var request = new CheckoutRequest { SavedPaymentMethodId = _saved.Methods.Single().PaymentMethodId, IdempotencyKey = "attempt-0002" };

        var result = await _sut.PayAsync(101, userId: 1, request);

        Assert.Equal(CheckoutOutcome.Succeeded, result.Outcome);
        Assert.Equal("4242", result.Transaction.CardLast4);
    }

    [Fact]
    public async Task Nobody_can_pay_with_someone_elses_saved_card()
    {
        _saved.Methods.Add(new SavedPaymentMethod { PaymentMethodId = 7, UserId = 2, Provider = "Mock", ProviderMethodId = "pm_mock_card_visa_4242_1230_abc" });
        var request = new CheckoutRequest { SavedPaymentMethodId = 7, IdempotencyKey = "attempt-0001" };

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.PayAsync(100, userId: 1, request));
        Assert.Equal(0, _gateway.Charges);
    }
}
