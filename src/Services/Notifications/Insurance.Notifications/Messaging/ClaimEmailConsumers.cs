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
    private readonly NotificationOptions _options;

    public ClaimSubmittedConsumer(INotificationRepository repo, IOptions<NotificationOptions> options)
    {
        _repo = repo;
        _options = options.Value;
    }

    // Not sent yet: the claim's documents are still being uploaded. ClaimReceivedEmailJob sends it after
    // Notifications:ClaimEmailDelay, with copies of the documents.
    public Task Consume(ConsumeContext<ClaimSubmitted> context) =>
        _repo.QueueClaimEmailAsync(context.Message.ClaimId, JsonSerializer.Serialize(context.Message),
            DateTime.UtcNow.Add(_options.ClaimEmailDelay));
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
