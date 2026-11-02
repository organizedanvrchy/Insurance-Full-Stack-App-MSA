using Insurance.Billing.Enums;
using Insurance.Billing.Exceptions;
using Insurance.Billing.Models;
using Insurance.Billing.Services;
using Insurance.Enrollments.Contracts;
using Xunit;

namespace Insurance.Billing.Tests;

// The rules that need another service's data (ownership, enrollment status) live in PaymentService.
// "Pay in order" and the double-click guard stay in usp_Payment_Pay, where the row is locked.

public class PaymentServiceTests
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly FakePaymentRepository _repo = new();
    private readonly FakeEnrollmentLookup _enrollments = new();
    private readonly PaymentService _sut;

    public PaymentServiceTests()
    {
        _sut = new PaymentService(_repo, _enrollments);

        // Customer 1 holds enrollment 10, with payment 100 due today.
        SetEnrollment(EnrollmentStatus.Active);
        _repo.Payments.Add(new Payment { PaymentId = 100, EnrollmentId = 10, DueDate = Today, Amount = 50m, Status = PaymentStatus.Pending });
    }

    private void SetEnrollment(EnrollmentStatus status) =>
        _enrollments.Enrollments[10] = new EnrollmentInfo(10, UserId: 1, PolicyId: 1, Today.AddMonths(-1), Today.AddMonths(11), status);

    [Fact]
    public async Task The_owner_can_pay_an_installment()
    {
        var dto = await _sut.PayAsync(100, userId: 1);

        Assert.Equal(PaymentStatus.Paid, dto.Status);
        Assert.Equal(100, _repo.LastPaid);
    }

    [Theory]
    [InlineData(999, 1)]   // no such payment
    [InlineData(100, 2)]   // someone else's
    public async Task Missing_and_foreign_payments_look_the_same(int paymentId, int userId)
    {
        await Assert.ThrowsAsync<PaymentNotFoundException>(() => _sut.PayAsync(paymentId, userId));
        Assert.Null(_repo.LastPaid);
    }

    [Fact]
    public async Task A_paid_installment_cannot_be_paid_again()
    {
        _repo.Payments.Single().PaidDate = Today;

        var ex = await Assert.ThrowsAsync<PaymentConflictException>(() => _sut.PayAsync(100, userId: 1));
        Assert.Contains("already been paid", ex.Message);
    }

    [Fact]
    public async Task A_cancelled_policys_unpaid_installments_are_gone_even_before_billing_hears_about_it()
    {
        // EnrollmentCancelled may not have been processed yet, so the row can still exist here.
        SetEnrollment(EnrollmentStatus.Cancelled);

        await Assert.ThrowsAsync<PaymentNotFoundException>(() => _sut.PayAsync(100, userId: 1));
        Assert.Empty((await _sut.GetScheduleAsync(10, userId: 1, isAdmin: false))!);
    }

    [Fact]
    public async Task A_cancelled_policy_keeps_its_paid_installments_as_history()
    {
        _repo.Payments.Single().PaidDate = Today;
        SetEnrollment(EnrollmentStatus.Cancelled);

        Assert.Single((await _sut.GetScheduleAsync(10, userId: 1, isAdmin: false))!);
    }

    [Fact]
    public async Task A_policy_still_being_set_up_takes_no_payments()
    {
        SetEnrollment(EnrollmentStatus.Pending);

        await Assert.ThrowsAsync<PaymentConflictException>(() => _sut.PayAsync(100, userId: 1));
        Assert.Null(_repo.LastPaid);
    }

    [Fact]
    public async Task An_expired_policy_can_still_settle_its_last_installments()
    {
        SetEnrollment(EnrollmentStatus.Expired);

        await _sut.PayAsync(100, userId: 1);

        Assert.Equal(100, _repo.LastPaid);
    }

    [Theory]
    [InlineData(1, false, true)]    // owner
    [InlineData(2, false, false)]   // another customer
    [InlineData(2, true, true)]     // admin
    public async Task Who_can_see_the_schedule(int userId, bool isAdmin, bool visible)
    {
        var schedule = await _sut.GetScheduleAsync(10, userId, isAdmin);

        Assert.Equal(visible, schedule is not null);
    }

    [Fact]
    public async Task Paying_sends_the_owner_and_plan_along_for_the_emailed_receipt()
    {
        await _sut.PayAsync(100, userId: 1);

        Assert.Equal((100, 1, 1), Assert.Single(_repo.Receipts));
    }

    [Fact]
    public void Receipt_numbers_are_padded_payment_ids()
    {
        Assert.Equal("PAY-00000042", Insurance.Billing.Repositories.PaymentRepository.ReceiptNumber(42));
    }
}
