using System.Text.Json;
using Insurance.Identity.Contracts;
using Insurance.Notifications.Email;
using Insurance.Notifications.Models;
using Insurance.Notifications.Repositories;
using Insurance.Notifications.Templates;
using Microsoft.Extensions.Options;

namespace Insurance.Notifications.Services;

/// <summary>One email to send because of an event.</summary>
/// <param name="Key">Unique per email (e.g. "receipt:123"): a redelivered event with the same key sends nothing.</param>
/// <param name="ToEmail">Overrides the account's address (verification links go to the address being verified).</param>
/// <param name="Render">Builds the email for the recipient. Only called when the email will really be sent.</param>
public record Notification(
    string Key,
    int UserId,
    NotificationKind Kind,
    Func<UserAccount, Task<RenderedEmail>> Render,
    string? ToEmail = null);

public interface INotificationDispatcher
{
    Task SendAsync(Notification notification, CancellationToken ct = default);
}

/// <summary>
/// Everything every email has in common: send each one once, only to active accounts, only if the customer wants
/// that kind, and keep a record. When the mail server says "later" (its sending limit, a temporary error), the email
/// is queued for EmailRetryJob and marked Waiting; when it refuses the email for good, it's recorded as Failed. Any
/// other failure is recorded and rethrown, so MassTransit retries the event; once the retries run out, the event
/// lands in the service's _error queue for a person to look at.
/// </summary>
public class NotificationDispatcher : INotificationDispatcher
{
    private readonly INotificationRepository _repo;
    private readonly IUserDirectory _users;
    private readonly IEmailSender _sender;
    private readonly EmailOptions _options;
    private readonly ILogger<NotificationDispatcher> _logger;
    private readonly TimeProvider _clock;

    public NotificationDispatcher(INotificationRepository repo, IUserDirectory users, IEmailSender sender,
        IOptions<EmailOptions> options, ILogger<NotificationDispatcher> logger, TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        _repo = repo;
        _users = users;
        _sender = sender;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(Notification n, CancellationToken ct = default)
    {
        if (!await _repo.BeginAsync(n.Key, n.UserId, n.Kind))
        {
            _logger.LogDebug("Email {Key} was already handled; ignoring the repeated event.", n.Key);
            return;
        }

        try
        {
            var user = await _users.GetAsync(n.UserId);   // Identity service, with this service's own token
            if (user is null)
            {
                await SkipAsync(n, "The account no longer exists.");
                return;
            }
            if (!user.IsActive)
            {
                await SkipAsync(n, "The account is deactivated.");
                return;
            }
            if (n.Kind == NotificationKind.PaymentReminder && !(await _repo.GetPreferencesAsync(n.UserId)).PaymentReminders)
            {
                await SkipAsync(n, "The customer turned off payment reminders.");
                return;
            }

            var to = n.ToEmail ?? user.Email;
            if (IsSkippedDomain(to))
            {
                await SkipAsync(n, $"Recipient domain is in Email:SkipDomains ({to}).");
                return;
            }
            if (_options.SkipReservedDomains && IsReservedDomain(to))
            {
                await SkipAsync(n, $"Recipient domain is reserved for testing and can't receive email ({to}).");
                return;
            }

            var email = await n.Render(user);
            var message = new EmailMessage(to, user.FullName, email.Subject, email.Html, email.Text, email.Attachments);
            try
            {
                await _sender.SendAsync(message, ct);
            }
            catch (EmailSendException ex) when (ex.TryLater)
            {
                // The server's sending limit, a temporary error or no connection: EmailRetryJob tries again later.
                var delay = EmailRetrySchedule.DelayAfter(failedAttempts: 1)!.Value;
                await _repo.QueueRetryAsync(n.Key, JsonSerializer.Serialize(message), _clock.GetUtcNow().UtcDateTime + delay,
                    to, email.Subject, ex.Message);
                _logger.LogWarning("Couldn't send {Kind} email {Key} yet ({Reason}); trying again in {Delay}.",
                    n.Kind, n.Key, ex.Message, delay);
                return;
            }
            catch (EmailSendException ex)
            {
                // Refused for good (e.g. an address the server won't deliver to): retrying can't help.
                await _repo.CompleteAsync(n.Key, EmailStatus.Failed, to, email.Subject, ex.Message);
                _logger.LogWarning("The mail server refused {Kind} email {Key}: {Reason}", n.Kind, n.Key, ex.Message);
                return;
            }
            await _repo.CompleteAsync(n.Key, EmailStatus.Sent, to, email.Subject);
            _logger.LogInformation("Sent {Kind} email {Key} to user {UserId}.", n.Kind, n.Key, n.UserId);
        }
        catch (Exception ex)
        {
            await _repo.CompleteAsync(n.Key, EmailStatus.Failed, detail: ex.Message);
            throw;
        }
    }

    private Task SkipAsync(Notification n, string reason)
    {
        _logger.LogInformation("Skipped {Kind} email {Key}: {Reason}", n.Kind, n.Key, reason);
        return _repo.CompleteAsync(n.Key, EmailStatus.Skipped, detail: reason);
    }

    private bool IsSkippedDomain(string address)
    {
        var at = address.LastIndexOf('@');
        if (at < 0) return false;
        var domain = address[(at + 1)..];
        return _options.SkipDomains.Any(d => domain.Equals(d.Trim().TrimStart('@'), StringComparison.OrdinalIgnoreCase));
    }

    private static readonly string[] ReservedDomains = { "example.com", "example.net", "example.org" };
    private static readonly string[] ReservedTopLevelDomains = { "example", "test", "invalid", "localhost" };

    /// <summary>A domain reserved for documentation and testing (RFC 2606, RFC 6761), or one under it.</summary>
    public static bool IsReservedDomain(string address)
    {
        var at = address.LastIndexOf('@');
        var domain = (at < 0 ? address : address[(at + 1)..]).Trim().TrimEnd('.').ToLowerInvariant();
        static bool IsOrUnder(string domain, string reserved) => domain == reserved || domain.EndsWith("." + reserved);
        return ReservedDomains.Any(d => IsOrUnder(domain, d)) || ReservedTopLevelDomains.Any(t => IsOrUnder(domain, t));
    }
}
