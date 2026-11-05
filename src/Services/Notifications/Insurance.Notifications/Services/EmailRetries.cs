using System.Text.Json;
using Insurance.Notifications.Email;
using Insurance.Notifications.Models;
using Insurance.Notifications.Repositories;
using Microsoft.Extensions.Options;

namespace Insurance.Notifications.Services;

/// <summary>
/// How long an email waits before each new try when the mail server says "later". Growing gaps: a sending limit that
/// resets every hour is waited out, without trying every few seconds. After the last one (about 13 hours in all) the
/// email is given up as Failed.
/// </summary>
public static class EmailRetrySchedule
{
    public static readonly TimeSpan[] Delays =
    {
        TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1), TimeSpan.FromHours(1), TimeSpan.FromHours(2), TimeSpan.FromHours(4), TimeSpan.FromHours(4)
    };

    /// <summary>The wait after this many failed tries, or null when it's time to give up.</summary>
    public static TimeSpan? DelayAfter(int failedAttempts) =>
        failedAttempts >= 1 && failedAttempts <= Delays.Length ? Delays[failedAttempts - 1] : null;
}

/// <summary>
/// Resends the emails waiting in notifications.EmailRetries once their wait is over. Safe with several instances:
/// each email is leased while it's being tried.
/// </summary>
public class EmailRetryJob : BackgroundService
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);   // a try that crashed is picked up after this

    private readonly IServiceScopeFactory _scopes;
    private readonly IEmailSender _sender;
    private readonly NotificationOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<EmailRetryJob> _logger;

    public EmailRetryJob(IServiceScopeFactory scopes, IEmailSender sender, IOptions<NotificationOptions> options,
        TimeProvider clock, ILogger<EmailRetryJob> logger)
    {
        _scopes = scopes;
        _sender = sender;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.EmailRetryPollInterval);
        do
        {
            try
            {
                await RetryDueAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Retrying waiting emails failed; will try again.");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    public async Task RetryDueAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<INotificationRepository>();

        foreach (var retry in await repo.TakeDueRetriesAsync(10, Lease))
        {
            var message = JsonSerializer.Deserialize<EmailMessage>(retry.Payload)!;
            try
            {
                await _sender.SendAsync(message, ct);
                await repo.CompleteAsync(retry.NotificationKey, EmailStatus.Sent, message.ToAddress, message.Subject);
                await repo.RemoveRetryAsync(retry.NotificationKey);
                _logger.LogInformation("Sent email {Key} on retry {Attempt}.", retry.NotificationKey, retry.Attempts);
            }
            catch (EmailSendException ex) when (ex.TryLater)
            {
                // The first failure was the original send, so this is failure number Attempts + 1.
                if (EmailRetrySchedule.DelayAfter(retry.Attempts + 1) is { } delay)
                {
                    await repo.RescheduleRetryAsync(retry.NotificationKey, _clock.GetUtcNow().UtcDateTime + delay, ex.Message);
                    _logger.LogWarning("Email {Key} still can't be sent ({Reason}); trying again in {Delay}.",
                        retry.NotificationKey, ex.Message, delay);
                }
                else
                {
                    await GiveUpAsync(repo, retry, $"Gave up after {retry.Attempts + 1} tries: {ex.Message}");
                }
            }
            catch (EmailSendException ex)
            {
                await GiveUpAsync(repo, retry, ex.Message);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Something else went wrong: left as is, tried again when the lease runs out.
                _logger.LogWarning(ex, "Retrying email {Key} failed unexpectedly; will try again.", retry.NotificationKey);
            }
        }
    }

    private async Task GiveUpAsync(INotificationRepository repo, EmailRetry retry, string reason)
    {
        await repo.CompleteAsync(retry.NotificationKey, EmailStatus.Failed, detail: reason);
        await repo.RemoveRetryAsync(retry.NotificationKey);
        _logger.LogWarning("Email {Key} was not sent: {Reason}", retry.NotificationKey, reason);
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
