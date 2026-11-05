using Insurance.Notifications.Models;

namespace Insurance.Notifications.Repositories;

public interface INotificationRepository
{
    Task<NotificationPreferences> GetPreferencesAsync(int userId);
    Task<NotificationPreferences> SetPreferencesAsync(int userId, bool paymentReminders);

    /// <summary>The sign-up choice: saved only if the customer has no preference yet.</summary>
    Task SetInitialPreferencesAsync(int userId, bool paymentReminders);

    /// <summary>true => go ahead and send; false => this email was already sent or skipped (a redelivered event).</summary>
    Task<bool> BeginAsync(string notificationKey, int userId, NotificationKind kind);
    Task CompleteAsync(string notificationKey, EmailStatus status, string? toEmail = null, string? subject = null, string? detail = null);

    Task<List<SentEmail>> GetRecentAsync(int userId, int take);

    // "We received your claim" waits until its documents have been uploaded (ClaimReceivedEmailJob).
    Task QueueClaimEmailAsync(int claimId, string payload, DateTime sendAfterUtc);
    Task<List<PendingClaimEmail>> TakeDueClaimEmailsAsync(int take, TimeSpan lease);
    Task RemoveClaimEmailAsync(int claimId);

    // Emails the mail server said to try later (EmailRetryJob). Queuing one marks it Waiting.
    Task QueueRetryAsync(string notificationKey, string payload, DateTime sendAfterUtc, string toEmail, string subject, string detail);
    Task<List<EmailRetry>> TakeDueRetriesAsync(int take, TimeSpan lease);
    Task RescheduleRetryAsync(string notificationKey, DateTime sendAfterUtc, string detail);
    Task RemoveRetryAsync(string notificationKey);
}
