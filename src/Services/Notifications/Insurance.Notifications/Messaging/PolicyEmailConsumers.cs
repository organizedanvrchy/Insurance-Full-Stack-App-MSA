using Insurance.Enrollments.Contracts;
using Insurance.Notifications.Models;
using Insurance.Notifications.Services;
using Insurance.Notifications.Templates;
using MassTransit;

namespace Insurance.Notifications.Messaging;

// Emails about policies (Enrollments service events). These come through an outbox and can be published again
// after a crash, with a new message id, so the keys are built from the business ids instead.

public class EnrollmentActivatedConsumer : IConsumer<EnrollmentActivated>
{
    private readonly INotificationDispatcher _dispatcher;
    private readonly EmailTemplates _templates;
    private readonly IPolicyDetails _policies;

    public EnrollmentActivatedConsumer(INotificationDispatcher dispatcher, EmailTemplates templates, IPolicyDetails policies)
    {
        _dispatcher = dispatcher;
        _templates = templates;
        _policies = policies;
    }

    public Task Consume(ConsumeContext<EnrollmentActivated> context)
    {
        var e = context.Message;
        return _dispatcher.SendAsync(new Notification(
            $"policy-activated:{e.EnrollmentId}", e.UserId, NotificationKind.PolicyActivated,
            async user => _templates.PolicyActivated(user.FullName, await _policies.SummaryAsync(e.EnrollmentId))),
            context.CancellationToken);
    }
}

public class EnrollmentCancelledConsumer : IConsumer<EnrollmentCancelled>
{
    private readonly INotificationDispatcher _dispatcher;
    private readonly EmailTemplates _templates;
    private readonly IPolicyDetails _policies;

    public EnrollmentCancelledConsumer(INotificationDispatcher dispatcher, EmailTemplates templates, IPolicyDetails policies)
    {
        _dispatcher = dispatcher;
        _templates = templates;
        _policies = policies;
    }

    public Task Consume(ConsumeContext<EnrollmentCancelled> context)
    {
        var e = context.Message;
        return _dispatcher.SendAsync(new Notification(
            $"policy-cancelled:{e.EnrollmentId}", e.UserId, NotificationKind.PolicyCancelled,
            async user => _templates.PolicyCancelled(user.FullName, await _policies.SummaryAsync(e.EnrollmentId))),
            context.CancellationToken);
    }
}
