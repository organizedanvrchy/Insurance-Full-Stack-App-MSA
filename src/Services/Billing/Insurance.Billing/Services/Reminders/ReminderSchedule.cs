using Insurance.Billing.Contracts;

namespace Insurance.Billing.Services.Reminders;

/// <summary>An unpaid installment the reminder job looks at, as read from the database.</summary>
/// <param name="OverdueFrom">The first day it counts as Overdue (due date + grace period + 1), from SQL.</param>
public record ReminderCandidate(
    int PaymentId,
    int EnrollmentId,
    DateOnly DueDate,
    decimal Amount,
    DateOnly OverdueFrom,
    IReadOnlySet<PaymentReminderStage> AlreadySent);

/// <summary>
/// When each payment reminder goes out, relative to the installment's due date:
///
///   DueSoon              3 days before the due date
///   DueToday             on the due date
///   Overdue              the day it turns Overdue (the day after the grace period; the same moment the app's
///                        "overdue" badge appears and claims are blocked)
///   OverdueSecondNotice  5 days after Overdue
///   OverdueFinalNotice   10 days after Overdue
///
/// The job runs every hour, but a stage is only sent on its day or the day after (so a restart around midnight
/// doesn't lose one). Older stages are never sent late: when Billing is first deployed, or after an outage, nobody
/// receives a burst of stale reminders. If several stages are due at once, only the latest one is sent.
/// </summary>
public static class ReminderSchedule
{
    public const int DueSoonDaysBefore = 3;
    public const int SecondNoticeDaysAfterOverdue = 5;
    public const int FinalNoticeDaysAfterOverdue = 10;

    /// <summary>How many days late a stage may still be sent (see above).</summary>
    public const int CatchUpDays = 1;

    public static DateOnly DateOf(PaymentReminderStage stage, DateOnly dueDate, DateOnly overdueFrom) => stage switch
    {
        PaymentReminderStage.DueSoon             => dueDate.AddDays(-DueSoonDaysBefore),
        PaymentReminderStage.DueToday            => dueDate,
        PaymentReminderStage.Overdue             => overdueFrom,
        PaymentReminderStage.OverdueSecondNotice => overdueFrom.AddDays(SecondNoticeDaysAfterOverdue),
        PaymentReminderStage.OverdueFinalNotice  => overdueFrom.AddDays(FinalNoticeDaysAfterOverdue),
        _ => throw new ArgumentOutOfRangeException(nameof(stage))
    };

    // The grace period itself lives in SQL (billing.ufn_OverdueGraceDays); this only needs to be at least as long.
    private const int MaxGraceDays = 30;

    /// <summary>The due dates worth reading on <paramref name="today"/>: wide enough for every stage.</summary>
    public static (DateOnly From, DateOnly To) CandidateWindow(DateOnly today) =>
        (today.AddDays(-(MaxGraceDays + 1 + FinalNoticeDaysAfterOverdue + CatchUpDays)), today.AddDays(DueSoonDaysBefore));

    /// <summary>The reminder to send today for this installment, if any.</summary>
    public static PaymentReminderStage? StageToSend(ReminderCandidate c, DateOnly today)
    {
        // Latest stage first: if two are due (e.g. after an outage), only the most relevant is sent.
        foreach (var stage in Enum.GetValues<PaymentReminderStage>().Reverse())
        {
            var date = DateOf(stage, c.DueDate, c.OverdueFrom);
            if (date > today) continue;                              // not yet
            if (date < today.AddDays(-CatchUpDays)) return null;     // this one and every earlier one is stale
            if (c.AlreadySent.Contains(stage)) return null;          // already sent: so were earlier ones, or they're stale
            return stage;
        }
        return null;
    }
}
