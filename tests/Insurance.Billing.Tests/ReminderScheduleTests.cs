using Insurance.Billing.Contracts;
using Insurance.Billing.Services.Reminders;
using Xunit;

namespace Insurance.Billing.Tests;

// When each reminder goes out, for one installment due on Nov 20 (Overdue from Nov 26, after the 5-day grace period).
public class ReminderScheduleTests
{
    private static readonly DateOnly Due = new(2026, 11, 20);
    private static readonly DateOnly OverdueFrom = Due.AddDays(6);

    private static ReminderCandidate Candidate(params PaymentReminderStage[] sent) =>
        new(1, 10, Due, 50m, OverdueFrom, sent.ToHashSet());

    [Theory]
    [InlineData(-4, null)]                                                        // too early
    [InlineData(-3, PaymentReminderStage.DueSoon)]                                // 3 days before
    [InlineData(-2, PaymentReminderStage.DueSoon)]                                // one day of catch-up
    [InlineData(-1, null)]                                                        // too late for "due soon"
    [InlineData(0, PaymentReminderStage.DueToday)]
    [InlineData(1, PaymentReminderStage.DueToday)]
    [InlineData(3, null)]                                                         // grace period: nothing
    [InlineData(6, PaymentReminderStage.Overdue)]                                 // the day it turns Overdue
    [InlineData(11, PaymentReminderStage.OverdueSecondNotice)]                    // 5 days later
    [InlineData(16, PaymentReminderStage.OverdueFinalNotice)]                     // 10 days later
    [InlineData(17, PaymentReminderStage.OverdueFinalNotice)]
    [InlineData(18, null)]                                                        // and then no more
    [InlineData(40, null)]
    public void Each_stage_goes_out_on_its_day(int daysFromDue, PaymentReminderStage? expected)
    {
        Assert.Equal(expected, ReminderSchedule.StageToSend(Candidate(), Due.AddDays(daysFromDue)));
    }

    [Fact]
    public void A_stage_already_sent_is_not_sent_again()
    {
        Assert.Null(ReminderSchedule.StageToSend(Candidate(PaymentReminderStage.DueToday), Due.AddDays(1)));
    }

    [Fact]
    public void Missed_earlier_stages_are_never_sent_late()
    {
        // DueSoon was never sent (e.g. Billing was down), but on the due date only "due today" goes out.
        Assert.Equal(PaymentReminderStage.DueToday, ReminderSchedule.StageToSend(Candidate(), Due));
    }

    [Fact]
    public void The_candidate_window_covers_every_stage()
    {
        var today = new DateOnly(2026, 11, 2);
        var (from, to) = ReminderSchedule.CandidateWindow(today);

        Assert.Equal(today.AddDays(3), to);                        // due soon
        Assert.True(from <= today.AddDays(-(6 + 10 + 1)));        // final notice, with a 5-day grace period
    }
}
