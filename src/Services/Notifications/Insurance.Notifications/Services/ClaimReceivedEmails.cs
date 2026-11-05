using System.Text.Json;
using Insurance.Claims.Contracts;
using Insurance.Notifications.Email;
using Insurance.Notifications.Models;
using Insurance.Notifications.Repositories;
using Insurance.Notifications.Templates;
using Microsoft.Extensions.Options;

namespace Insurance.Notifications.Services;

/// <summary>"Notifications" section of the configuration.</summary>
public class NotificationOptions
{
    /// <summary>
    /// How long "We received your claim" waits after the claim is saved. The claim form uploads documents one by one
    /// after saving the claim, and the email is the customer's copy of the claim WITH its documents.
    /// </summary>
    public TimeSpan ClaimEmailDelay { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>How often the job looks for claim emails that are due.</summary>
    public TimeSpan ClaimEmailPollInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>How often EmailRetryJob looks for waiting emails whose next try is due.</summary>
    public TimeSpan EmailRetryPollInterval { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>Builds and sends one claim's "We received your claim" email, with its documents.</summary>
public interface IClaimReceivedEmail
{
    Task SendAsync(ClaimSubmitted claim, CancellationToken ct = default);
}

public class ClaimReceivedEmail : IClaimReceivedEmail
{
    private readonly INotificationDispatcher _dispatcher;
    private readonly EmailTemplates _templates;
    private readonly IPolicyDetails _policies;
    private readonly IClaimDocuments _documents;
    private readonly EmailOptions _email;

    public ClaimReceivedEmail(INotificationDispatcher dispatcher, EmailTemplates templates, IPolicyDetails policies,
        IClaimDocuments documents, IOptions<EmailOptions> email)
    {
        _dispatcher = dispatcher;
        _templates = templates;
        _policies = policies;
        _documents = documents;
        _email = email.Value;
    }

    public Task SendAsync(ClaimSubmitted e, CancellationToken ct = default) =>
        _dispatcher.SendAsync(new Notification(
            $"claim-submitted:{e.ClaimId}", e.UserId, NotificationKind.ClaimSubmitted,
            async user =>
            {
                var policyName = await _policies.PlanNameForEnrollmentAsync(e.EnrollmentId);
                var (copies, attachments) = await GatherDocumentsAsync(e.ClaimId);
                return _templates.ClaimSubmitted(user.FullName, e, policyName, copies) with { Attachments = attachments };
            }), ct);

    // Every document is listed; copies are attached in upload order while they fit in Email:MaxAttachmentBytes.
    private async Task<(List<EmailTemplates.ClaimDocumentCopy>, List<EmailAttachment>)> GatherDocumentsAsync(int claimId)
    {
        var copies = new List<EmailTemplates.ClaimDocumentCopy>();
        var attachments = new List<EmailAttachment>();
        var budget = _email.MaxAttachmentBytes;

        foreach (var d in await _documents.GetForClaimAsync(claimId))
        {
            byte[]? content = null;
            if (d.SizeBytes <= budget)
                content = await _documents.GetContentAsync(claimId, d.ClaimDocumentId);

            if (content is not null)
            {
                attachments.Add(new EmailAttachment(d.FileName, d.ContentType, content));
                budget -= content.Length;
            }
            copies.Add(new EmailTemplates.ClaimDocumentCopy(d.FileName, d.ContentType, d.SizeBytes, Attached: content is not null));
        }
        return (copies, attachments);
    }
}

/// <summary>
/// Sends the queued "We received your claim" emails once their delay has passed. Safe with several instances: each
/// email is leased while it's being sent, and the dispatcher never sends the same claim's email twice.
/// </summary>
public class ClaimReceivedEmailJob : BackgroundService
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);   // a failed send is retried after this

    private readonly IServiceScopeFactory _scopes;
    private readonly NotificationOptions _options;
    private readonly ILogger<ClaimReceivedEmailJob> _logger;

    public ClaimReceivedEmailJob(IServiceScopeFactory scopes, IOptions<NotificationOptions> options, ILogger<ClaimReceivedEmailJob> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.ClaimEmailPollInterval);
        do
        {
            try
            {
                await SendDueAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Sending queued claim emails failed; will retry.");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    public async Task SendDueAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
        var email = scope.ServiceProvider.GetRequiredService<IClaimReceivedEmail>();

        foreach (var pending in await repo.TakeDueClaimEmailsAsync(20, Lease))
        {
            try
            {
                var claim = JsonSerializer.Deserialize<ClaimSubmitted>(pending.Payload)!;
                await email.SendAsync(claim, ct);
                await repo.RemoveClaimEmailAsync(pending.ClaimId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Left in the queue: tried again when the lease runs out.
                _logger.LogWarning(ex, "The claim email for claim {ClaimId} failed (attempt {Attempt}); will retry.",
                    pending.ClaimId, pending.Attempts);
            }
        }
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
