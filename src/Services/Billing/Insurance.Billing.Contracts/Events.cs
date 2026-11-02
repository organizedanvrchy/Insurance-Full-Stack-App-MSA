namespace Insurance.Billing.Contracts;

// Events published by the Billing service through its transactional outbox.
// (PremiumScheduleCreated, its step in the enrollment saga, is in IPremiumSummaries.cs.)

/// <summary>
/// A customer paid an installment: the facts for their receipt. InstallmentNumber counts from 1 in due-date order.
/// NextDue* is the earliest installment still unpaid after this one, if any.
/// </summary>
public record PaymentReceived(
    int PaymentId,
    string ReceiptNumber,
    int EnrollmentId,
    int UserId,
    int PolicyId,
    decimal Amount,
    DateOnly DueDate,
    DateOnly PaidDate,
    DateTime PaidAtUtc,
    int InstallmentNumber,
    int InstallmentCount,
    DateOnly? NextDueDate,
    decimal? NextDueAmount);

/// <summary>Which reminder this is, in the order they are sent for one unpaid installment.</summary>
public enum PaymentReminderStage
{
    DueSoon,              // 3 days before the due date
    DueToday,             // on the due date
    Overdue,              // the day the installment turns Overdue (after the grace period)
    OverdueSecondNotice,  // 5 days after that
    OverdueFinalNotice    // 10 days after that
}

/// <summary>Remind the customer about an unpaid installment. Published at most once per installment and stage.</summary>
public record PaymentReminderDue(
    int PaymentId,
    int EnrollmentId,
    int UserId,
    int PolicyId,
    DateOnly DueDate,
    decimal Amount,
    PaymentReminderStage Stage,
    int DaysPastDue);       // 0 or less until the due date has passed
