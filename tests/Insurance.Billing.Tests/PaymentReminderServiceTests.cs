using Insurance.Billing.Contracts;
using Insurance.Billing.Services.Reminders;
using Insurance.Enrollments.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Insurance.Billing.Tests;

public class PaymentReminderServiceTests
{
    private static readonly DateOnly Today = new(2026, 11, 2);

    private readonly FakePaymentRepository _repo = new();
    private readonly FakeEnrollmentLookup _enrollments = new();
    private readonly PaymentReminderService _sut;

    public PaymentReminderServiceTests()
    {
        _sut = new PaymentReminderService(_repo, _enrollments, NullLogger<PaymentReminderService>.Instance);
        SetEnrollment(10, userId: 1, EnrollmentStatus.Active);
    }

    private void SetEnrollment(int id, int userId, EnrollmentStatus status) =>
        _enrollments.Enrollments[id] = new EnrollmentInfo(id, userId, PolicyId: 4, Today.AddMonths(-3), Today.AddMonths(9), status);

    private void AddInstallment(int paymentId, DateOnly due, int enrollmentId = 10, decimal amount = 75m) =>
        _repo.Candidates.Add(new ReminderCandidate(paymentId, enrollmentId, due, amount, due.AddDays(6), new HashSet<PaymentReminderStage>()));

    [Fact]
    public async Task Publishes_a_reminder_with_who_and_what_is_due()
    {
        AddInstallment(100, Today.AddDays(3));

        var sent = await _sut.SendDueRemindersAsync(Today);

        Assert.Equal(1, sent);
        var r = Assert.Single(_repo.Recorded);
        Assert.Equal(new PaymentReminderDue(100, 10, UserId: 1, PolicyId: 4, Today.AddDays(3), 75m,
            PaymentReminderStage.DueSoon, DaysPastDue: -3), r);
    }

    [Fact]
    public async Task Overdue_reminders_count_the_days_past_due()
    {
        AddInstallment(100, Today.AddDays(-11));

        await _sut.SendDueRemindersAsync(Today);

        var r = Assert.Single(_repo.Recorded);
        Assert.Equal(PaymentReminderStage.OverdueSecondNotice, r.Stage);
        Assert.Equal(11, r.DaysPastDue);
    }

    [Fact]
    public async Task Running_twice_on_the_same_day_sends_nothing_new()
    {
        AddInstallment(100, Today);

        Assert.Equal(1, await _sut.SendDueRemindersAsync(Today));
        Assert.Equal(0, await _sut.SendDueRemindersAsync(Today));
        Assert.Single(_repo.Recorded);
    }

    [Theory]
    [InlineData(EnrollmentStatus.Cancelled, 0)]
    [InlineData(EnrollmentStatus.Pending, 0)]
    [InlineData(EnrollmentStatus.Expired, 1)]      // the last installments of a finished policy are still owed
    public async Task Only_policies_in_force_get_reminders(EnrollmentStatus status, int expected)
    {
        SetEnrollment(10, userId: 1, status);
        AddInstallment(100, Today);

        Assert.Equal(expected, await _sut.SendDueRemindersAsync(Today));
    }

    [Fact]
    public async Task Installments_with_nothing_due_today_cost_no_lookups()
    {
        AddInstallment(100, Today.AddDays(5));       // "due soon" is in 2 days: nothing to send today
        _enrollments.Enrollments.Clear();             // a lookup would find nothing (and must not be needed)

        Assert.Equal(0, await _sut.SendDueRemindersAsync(Today));
        Assert.Empty(_repo.Recorded);
    }

    [Fact]
    public async Task Reads_a_window_wide_enough_for_every_stage()
    {
        await _sut.SendDueRemindersAsync(Today);

        Assert.Equal(ReminderSchedule.CandidateWindow(Today), _repo.LastCandidateWindow);
    }
}
