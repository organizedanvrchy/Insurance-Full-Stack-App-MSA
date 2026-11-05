using Insurance.Claims.Contracts;
using System.Text.Json;
using Insurance.Notifications.Models;
using Insurance.Notifications.Repositories;
using Insurance.Notifications.Services;
using Insurance.Notifications.Templates;
using MassTransit;
using Microsoft.Extensions.Options;

namespace Insurance.Notifications.Messaging;

// Emails about claims (Claims service events, through its outbox).

public class ClaimSubmittedConsumer : IConsumer<ClaimSubmitted>
{
    private readonly INotificationRepository _repo;
    private readonly IClaimReceivedEmail _email;
    private readonly NotificationOptions _options;

    public ClaimSubmittedConsumer(INotificationRepository repo, IClaimReceivedEmail email, IOptions<NotificationOptions> options)
    {
        _repo = repo;
        _email = email;
        _options = options.Value;
    }

    // The email is the customer's copy of the claim WITH its documents, which the claim form uploads one by one after
    // saving the claim. No documents coming: it goes out now. Some coming: ClaimReceivedEmailJob looks on every tick
    // (ClaimEmailPollInterval) and sends once they're all in, or after ClaimEmailDelay at the latest. Unknown (an
    // older caller): it waits the whole ClaimEmailDelay, as before.
    public Task Consume(ConsumeContext<ClaimSubmitted> context)
    {
        var claim = context.Message;
        if (claim.ExpectedDocuments == 0)
            return _email.SendAsync(claim, context.CancellationToken);

        var sendAfter = claim.ExpectedDocuments > 0 ? DateTime.UtcNow : DateTime.UtcNow.Add(_options.ClaimEmailDelay);
        return _repo.QueueClaimEmailAsync(claim.ClaimId, JsonSerializer.Serialize(claim), sendAfter);
    }
}

/// <summary>
/// One email each when a claim is approved, rejected or paid (each happens once), and one every time the reviewer
/// asks for more information. "Under review" sends nothing: the claim-received email already says a decision will follow.
/// </summary>
public class ClaimStatusChangedConsumer : IConsumer<ClaimStatusChanged>
{
    private readonly INotificationDispatcher _dispatcher;
    private readonly EmailTemplates _templates;
    private readonly IPolicyDetails _policies;

    public ClaimStatusChangedConsumer(INotificationDispatcher dispatcher, EmailTemplates templates, IPolicyDetails policies)
    {
        _dispatcher = dispatcher;
        _templates = templates;
        _policies = policies;
    }

    public Task Consume(ConsumeContext<ClaimStatusChanged> context)
    {
        var e = context.Message;
        if (e.NewStatus is not (ClaimStatus.Approved or ClaimStatus.Rejected or ClaimStatus.Paid or ClaimStatus.NeedsMoreInfo))
            return Task.CompletedTask;

        // A reviewer can ask for more information more than once, so each request gets its own key (a redelivered
        // message keeps its id). The other statuses happen at most once per claim.
        var key = e.NewStatus == ClaimStatus.NeedsMoreInfo
            ? $"claim-status:{e.ClaimId}:{e.NewStatus}:{context.MessageId?.ToString() ?? e.ChangedAtUtc.Ticks.ToString()}"
            : $"claim-status:{e.ClaimId}:{e.NewStatus}";

        return _dispatcher.SendAsync(new Notification(
            key, e.UserId, NotificationKind.ClaimStatusChanged,
            async user => _templates.ClaimStatusChanged(user.FullName, e, await _policies.PlanNameForEnrollmentAsync(e.EnrollmentId))),
            context.CancellationToken);
    }
}
