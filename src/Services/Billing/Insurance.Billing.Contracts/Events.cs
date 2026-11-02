namespace Insurance.Billing.Contracts;

// Events published by the Billing service through its transactional outbox.
// (PremiumScheduleCreated, its step in the enrollment saga, is in IPremiumSummaries.cs.)

/// <summary>
/// A customer paid an installment through checkout: the facts for their emailed receipt. ReceiptNumber is the
/// payment's transaction reference (the same as on the in-app receipt). PaymentMethod is safe to show, e.g.
/// "Visa ending in 4242" or "PayPal". InstallmentNumber counts from 1 in due-date order; NextDue* is the earliest
/// installment still unpaid after this one, if any.
/// </summary>
public record PaymentReceived(
    int PaymentId,
    int TransactionId,
    string ReceiptNumber,
    int EnrollmentId,
    int UserId,
    int PolicyId,
    decimal Amount,
    DateOnly DueDate,
    DateOnly PaidDate,
    DateTime PaidAtUtc,
    string PaymentMethod,
    int InstallmentNumber,
    int InstallmentCount,
    DateOnly? NextDueDate,
    decimal? NextDueAmount);

/// <summary>
/// A payment attempt was declined by the card or wallet, or the processor failed (CanRetry: trying again may work).
/// Nothing was charged and the installment is still unpaid. Reason is the processor's customer-facing message.
/// </summary>
public record PaymentDeclined(
    int TransactionId,
    string Reference,
    int PaymentId,
    int EnrollmentId,
    int UserId,
    int PolicyId,
    decimal Amount,
    DateOnly DueDate,
    string PaymentMethod,
    string Reason,
    bool CanRetry,
    DateTime AttemptedAtUtc);

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
