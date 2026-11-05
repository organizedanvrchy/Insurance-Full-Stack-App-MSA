namespace Insurance.Notifications.Models;

/// <summary>The kinds of email this service sends (stored as text in notifications.Emails.Kind).</summary>
public enum NotificationKind
{
    VerifyEmail,
    EmailChangeRequested,   // no longer sent; kept so older history rows still read
    EmailChanged,           // to the old address, after a change
    PasswordChanged,
    PasswordResetByAdmin,
    AccountDeactivated,
    AccountReactivated,
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
    Failed,
    Waiting      // the mail server said to try later: queued in notifications.EmailRetries
}

public record NotificationPreferences(int UserId, bool PaymentReminders, DateTime? UpdatedAtUtc);

/// <summary>A queued "We received your claim" email. Payload is the ClaimSubmitted event as JSON.</summary>
public record PendingClaimEmail(int ClaimId, string Payload, int Attempts);

/// <summary>An email waiting for another try; Attempts = retries made, this one included.</summary>
public record EmailRetry(string NotificationKey, string Payload, int Attempts);

public record SentEmail(long EmailId, NotificationKind Kind, string? ToEmail, string? Subject, DateTime? SentAtUtc);
