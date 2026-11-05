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

/// <summary>"Your email address was changed", to the OLD address.</summary>
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

/// <summary>"Your password was changed", to the account's email.</summary>
public class PasswordChangedConsumer : IConsumer<PasswordChanged>
{
    private readonly INotificationDispatcher _dispatcher;
    private readonly EmailTemplates _templates;

    public PasswordChangedConsumer(INotificationDispatcher dispatcher, EmailTemplates templates)
    {
        _dispatcher = dispatcher;
        _templates = templates;
    }

    public Task Consume(ConsumeContext<PasswordChanged> context)
    {
        var e = context.Message;
        return _dispatcher.SendAsync(new Notification(
            $"password-changed:{context.MessageId}", e.UserId, NotificationKind.PasswordChanged,
            _ => Task.FromResult(_templates.PasswordChanged(e)),
            ToEmail: e.Email), context.CancellationToken);
    }
}

/// <summary>The one-time password from an admin reset. Always sent: it's the only way to sign in again.</summary>
/// <summary>The account was deactivated: the one email that goes to an account that is no longer active.</summary>
public class AccountDeactivatedConsumer : IConsumer<AccountDeactivated>
{
    private readonly INotificationDispatcher _dispatcher;
    private readonly EmailTemplates _templates;

    public AccountDeactivatedConsumer(INotificationDispatcher dispatcher, EmailTemplates templates)
    {
        _dispatcher = dispatcher;
        _templates = templates;
    }

    // Keyed on the message: deactivated, reactivated and deactivated again is two emails.
    public Task Consume(ConsumeContext<AccountDeactivated> context) =>
        _dispatcher.SendAsync(new Notification(
            $"account-deactivated:{context.MessageId}", context.Message.UserId, NotificationKind.AccountDeactivated,
            _ => Task.FromResult(_templates.AccountDeactivated(context.Message)),
            ToEmail: context.Message.Email), context.CancellationToken);
}

/// <summary>The account was reactivated: the customer can sign in again.</summary>
public class AccountReactivatedConsumer : IConsumer<AccountReactivated>
{
    private readonly INotificationDispatcher _dispatcher;
    private readonly EmailTemplates _templates;

    public AccountReactivatedConsumer(INotificationDispatcher dispatcher, EmailTemplates templates)
    {
        _dispatcher = dispatcher;
        _templates = templates;
    }

    public Task Consume(ConsumeContext<AccountReactivated> context) =>
        _dispatcher.SendAsync(new Notification(
            $"account-reactivated:{context.MessageId}", context.Message.UserId, NotificationKind.AccountReactivated,
            _ => Task.FromResult(_templates.AccountReactivated(context.Message)),
            ToEmail: context.Message.Email), context.CancellationToken);
}

public class PasswordResetByAdminConsumer : IConsumer<PasswordResetByAdmin>
{
    private readonly INotificationDispatcher _dispatcher;
    private readonly EmailTemplates _templates;

    public PasswordResetByAdminConsumer(INotificationDispatcher dispatcher, EmailTemplates templates)
    {
        _dispatcher = dispatcher;
        _templates = templates;
    }

    public Task Consume(ConsumeContext<PasswordResetByAdmin> context)
    {
        var e = context.Message;
        return _dispatcher.SendAsync(new Notification(
            $"password-reset:{context.MessageId}", e.UserId, NotificationKind.PasswordResetByAdmin,
            _ => Task.FromResult(_templates.PasswordResetByAdmin(e)),
            ToEmail: e.Email), context.CancellationToken);
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
