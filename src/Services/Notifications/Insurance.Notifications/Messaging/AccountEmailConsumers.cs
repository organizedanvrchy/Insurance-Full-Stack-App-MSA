using Insurance.Identity.Contracts;
using Insurance.Notifications.Models;
using Insurance.Notifications.Repositories;
using Insurance.Notifications.Services;
using Insurance.Notifications.Templates;
using MassTransit;

namespace Insurance.Notifications.Messaging;

// The account itself (Identity service events). These are published straight to the bus, never twice for the same
// request, so the message id is a safe idempotency key.

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
            ToEmail: e.Email), context.CancellationToken);    // the address the link was made for
    }
}

/// <summary>Saves the "email me payment reminders" answer from the sign-up form, unless the customer already chose.</summary>
public class CustomerRegisteredConsumer : IConsumer<CustomerRegistered>
{
    private readonly INotificationRepository _repo;
    public CustomerRegisteredConsumer(INotificationRepository repo) => _repo = repo;

    public Task Consume(ConsumeContext<CustomerRegistered> context) =>
        _repo.SetInitialPreferencesAsync(context.Message.UserId, context.Message.PaymentReminders);
}
