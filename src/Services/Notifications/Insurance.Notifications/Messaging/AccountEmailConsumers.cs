using Insurance.Identity.Contracts;
using Insurance.Notifications.Models;
using Insurance.Notifications.Services;
using Insurance.Notifications.Templates;
using MassTransit;

namespace Insurance.Notifications.Messaging;

// Emails about the account itself (Identity service events). These are published straight to the bus, never twice
// for the same request, so the message id is a safe idempotency key.

public class EmailVerificationRequestedConsumer : IConsumer<EmailVerificationRequested>
{
    private readonly INotificationDispatcher _dispatcher;
    private readonly EmailTemplates _templates;

    public EmailVerificationRequestedConsumer(INotificationDispatcher dispatcher, EmailTemplates templates)
    {
        _dispatcher = dispatcher;
        _templates = templates;
    }

    public Task Consume(ConsumeContext<EmailVerificationRequested> context)
    {
        var e = context.Message;
        return _dispatcher.SendAsync(new Notification(
            $"verify:{context.MessageId}", e.UserId, NotificationKind.VerifyEmail,
            _ => Task.FromResult(_templates.VerifyEmail(e)),
            ToEmail: e.Email), context.CancellationToken);    // the address being verified, which may be a new one
    }
}

public class EmailChangeRequestedConsumer : IConsumer<EmailChangeRequested>
{
    private readonly INotificationDispatcher _dispatcher;
    private readonly EmailTemplates _templates;

    public EmailChangeRequestedConsumer(INotificationDispatcher dispatcher, EmailTemplates templates)
    {
        _dispatcher = dispatcher;
        _templates = templates;
    }

    public Task Consume(ConsumeContext<EmailChangeRequested> context)
    {
        var e = context.Message;
        return _dispatcher.SendAsync(new Notification(
            $"email-change-requested:{context.MessageId}", e.UserId, NotificationKind.EmailChangeRequested,
            _ => Task.FromResult(_templates.EmailChangeRequested(e)),
            ToEmail: e.CurrentEmail), context.CancellationToken);
    }
}

public class EmailChangedConsumer : IConsumer<EmailChanged>
{
    private readonly INotificationDispatcher _dispatcher;
    private readonly EmailTemplates _templates;

    public EmailChangedConsumer(INotificationDispatcher dispatcher, EmailTemplates templates)
    {
        _dispatcher = dispatcher;
        _templates = templates;
    }

    public Task Consume(ConsumeContext<EmailChanged> context)
    {
        var e = context.Message;
        return _dispatcher.SendAsync(new Notification(
            $"email-changed:{context.MessageId}", e.UserId, NotificationKind.EmailChanged,
            _ => Task.FromResult(_templates.EmailChanged(e)),
            ToEmail: e.OldEmail), context.CancellationToken);   // the account already uses the new one
    }
}
