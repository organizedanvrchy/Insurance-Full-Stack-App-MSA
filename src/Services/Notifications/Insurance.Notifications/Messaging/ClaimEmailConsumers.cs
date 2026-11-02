using Insurance.Claims.Contracts;
using Insurance.Notifications.Models;
using Insurance.Notifications.Services;
using Insurance.Notifications.Templates;
using MassTransit;

namespace Insurance.Notifications.Messaging;

// Emails about claims (Claims service events, through its outbox).

public class ClaimSubmittedConsumer : IConsumer<ClaimSubmitted>
{
    private readonly INotificationDispatcher _dispatcher;
    private readonly EmailTemplates _templates;
    private readonly IPolicyDetails _policies;

    public ClaimSubmittedConsumer(INotificationDispatcher dispatcher, EmailTemplates templates, IPolicyDetails policies)
    {
        _dispatcher = dispatcher;
        _templates = templates;
        _policies = policies;
    }

    public Task Consume(ConsumeContext<ClaimSubmitted> context)
    {
        var e = context.Message;
        return _dispatcher.SendAsync(new Notification(
            $"claim-submitted:{e.ClaimId}", e.UserId, NotificationKind.ClaimSubmitted,
            async user => _templates.ClaimSubmitted(user.FullName, e, await _policies.PlanNameForEnrollmentAsync(e.EnrollmentId))),
            context.CancellationToken);
    }
}

/// <summary>One email per status a claim reaches (a claim can only move forward, so each status happens once).</summary>
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
        return _dispatcher.SendAsync(new Notification(
            $"claim-status:{e.ClaimId}:{e.NewStatus}", e.UserId, NotificationKind.ClaimStatusChanged,
            async user => _templates.ClaimStatusChanged(user.FullName, e, await _policies.PlanNameForEnrollmentAsync(e.EnrollmentId))),
            context.CancellationToken);
    }
}
