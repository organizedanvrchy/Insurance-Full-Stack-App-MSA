namespace Insurance.Notifications.Models;

/// <summary>The kinds of email this service sends (stored as text in notifications.Emails.Kind).</summary>
public enum NotificationKind
{
    VerifyEmail,
    EmailChangeRequested,   // no longer sent; kept so older history rows still read
    EmailChanged,           // to the old address, after a change
    PasswordChanged,
    PolicyActivated,
    PolicyCancelled,
    ClaimSubmitted,
    ClaimStatusChanged,
    PaymentReceipt,
    PaymentDeclined,
    PaymentReminder     // the only kind a customer can turn off
}

public enum EmailStatus
{
    Sending,
    Sent,
    Skipped,
    Failed
}

public record NotificationPreferences(int UserId, bool PaymentReminders, DateTime? UpdatedAtUtc);

public record SentEmail(long EmailId, NotificationKind Kind, string? ToEmail, string? Subject, DateTime? SentAtUtc);
