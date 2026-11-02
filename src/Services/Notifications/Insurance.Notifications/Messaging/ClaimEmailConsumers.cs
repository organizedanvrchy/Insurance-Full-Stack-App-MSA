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

/// <summary>
/// The decision: one email when a claim is approved or rejected (a claim can only move forward, so each happens
/// once). "Under review" and "paid" send nothing; the claim-received email already says a decision will follow.
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
        if (e.NewStatus is not (ClaimStatus.Approved or ClaimStatus.Rejected))
            return Task.CompletedTask;

        return _dispatcher.SendAsync(new Notification(
            $"claim-status:{e.ClaimId}:{e.NewStatus}", e.UserId, NotificationKind.ClaimStatusChanged,
            async user => _templates.ClaimStatusChanged(user.FullName, e, await _policies.PlanNameForEnrollmentAsync(e.EnrollmentId))),
            context.CancellationToken);
    }
}
