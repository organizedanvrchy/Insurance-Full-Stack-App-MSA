using Insurance.Billing.Enums;
using Insurance.Billing.Models;
using Insurance.Billing.Services;
using Insurance.Enrollments.Contracts;
using Xunit;

namespace Insurance.Billing.Tests;

// Reading schedules and the payment log. Paying is covered by CheckoutServiceTests.

public class PaymentServiceTests
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly FakePaymentRepository _repo = new();
    private readonly FakeTransactionRepository _transactions;
    private readonly FakeEnrollmentLookup _enrollments = new();
    private readonly PaymentService _sut;

    public PaymentServiceTests()
    {
        _transactions = new FakeTransactionRepository(_repo);
        _sut = new PaymentService(_repo, _transactions, _enrollments, new FakeUserDirectory());

        // Customer 1 holds enrollment 10, with payment 100 due today.
        SetEnrollment(EnrollmentStatus.Active);
        _repo.Payments.Add(new Payment { PaymentId = 100, EnrollmentId = 10, DueDate = Today, Amount = 50m, Status = PaymentStatus.Pending });
    }

    private void SetEnrollment(EnrollmentStatus status) =>
        _enrollments.Enrollments[10] = new EnrollmentInfo(10, UserId: 1, PolicyId: 1, Today.AddMonths(-1), Today.AddMonths(11), status);

    [Fact]
    public async Task A_cancelled_policys_unpaid_installments_are_gone_even_before_billing_hears_about_it()
    {
        // EnrollmentCancelled may not have been processed yet, so the row can still exist here.
        SetEnrollment(EnrollmentStatus.Cancelled);

        Assert.Empty((await _sut.GetScheduleAsync(10, userId: 1, isAdmin: false))!);
    }

    [Fact]
    public async Task A_cancelled_policy_keeps_its_paid_installments_as_history()
    {
        _repo.Payments.Single().PaidDate = Today;
        SetEnrollment(EnrollmentStatus.Cancelled);

        Assert.Single((await _sut.GetScheduleAsync(10, userId: 1, isAdmin: false))!);
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

    // The payment log

    private void AddAttempt(int userId, int enrollmentId, TransactionStatus status) =>
        _transactions.Transactions.Add(new PaymentTransaction
        {
            TransactionId = _transactions.Transactions.Count + 1, UserId = userId, EnrollmentId = enrollmentId, PaymentId = 100,
            Amount = 50m, Status = status, Provider = "Mock", IdempotencyKey = $"k{_transactions.Transactions.Count}"
        });

    [Fact]
    public async Task A_customer_sees_their_own_attempts_including_declines()
    {
        AddAttempt(userId: 1, enrollmentId: 10, TransactionStatus.Declined);
        AddAttempt(userId: 1, enrollmentId: 10, TransactionStatus.Succeeded);
        AddAttempt(userId: 2, enrollmentId: 20, TransactionStatus.Succeeded);

        var mine = await _sut.GetTransactionsAsync(userId: 1, isAdmin: false, enrollmentId: null, status: null);

        Assert.Equal(new[] { "TXN-000002", "TXN-000001" }, mine!.Select(t => t.Reference));   // newest first
        Assert.All(mine!, t => Assert.Null(t.CustomerName));                                    // names are for admins
    }

    [Fact]
    public async Task Another_customers_policy_log_is_not_found()
    {
        Assert.Null(await _sut.GetTransactionsAsync(userId: 2, isAdmin: false, enrollmentId: 10, status: null));
    }

    [Fact]
    public async Task The_admin_log_shows_everyone_with_names_and_can_filter_by_status()
    {
        AddAttempt(userId: 1, enrollmentId: 10, TransactionStatus.Declined);
        AddAttempt(userId: 2, enrollmentId: 20, TransactionStatus.Succeeded);

        var declined = await _sut.GetTransactionsAsync(userId: 99, isAdmin: true, enrollmentId: null, status: TransactionStatus.Declined);

        var row = Assert.Single(declined!);
        Assert.Equal(("Cal Tester", "cal@test.example"), (row.CustomerName, row.CustomerEmail));
    }

    [Theory]
    [InlineData(1, false, true)]    // the customer who paid
    [InlineData(2, false, false)]   // another customer
    [InlineData(99, true, true)]    // an admin
    public async Task Who_can_see_a_receipt(int userId, bool isAdmin, bool visible)
    {
        AddAttempt(userId: 1, enrollmentId: 10, TransactionStatus.Succeeded);

        Assert.Equal(visible, await _sut.GetTransactionAsync(1, userId, isAdmin) is not null);
    }
}
