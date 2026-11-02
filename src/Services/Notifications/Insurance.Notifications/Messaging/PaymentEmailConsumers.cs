using Insurance.Billing.Contracts;
using Insurance.Notifications.Models;
using Insurance.Notifications.Services;
using Insurance.Notifications.Templates;
using MassTransit;

namespace Insurance.Notifications.Messaging;

// Emails about premiums (Billing service events, through its outbox).

/// <summary>The receipt for a paid installment. Always sent: receipts can't be turned off.</summary>
public class PaymentReceivedConsumer : IConsumer<PaymentReceived>
{
    private readonly INotificationDispatcher _dispatcher;
    private readonly EmailTemplates _templates;
    private readonly IPolicyDetails _policies;

    public PaymentReceivedConsumer(INotificationDispatcher dispatcher, EmailTemplates templates, IPolicyDetails policies)
    {
        _dispatcher = dispatcher;
        _templates = templates;
        _policies = policies;
    }

    public Task Consume(ConsumeContext<PaymentReceived> context)
    {
        var e = context.Message;
        return _dispatcher.SendAsync(new Notification(
            $"receipt:{e.PaymentId}", e.UserId, NotificationKind.PaymentReceipt,
            async user => _templates.PaymentReceipt(user.FullName, e, await _policies.PlanNameAsync(e.PolicyId))),
            context.CancellationToken);
    }
}

/// <summary>Due soon / due today / overdue reminders. Skipped for customers who turned reminders off.</summary>
public class PaymentReminderDueConsumer : IConsumer<PaymentReminderDue>
{
    private readonly INotificationDispatcher _dispatcher;
    private readonly EmailTemplates _templates;
    private readonly IPolicyDetails _policies;

    public PaymentReminderDueConsumer(INotificationDispatcher dispatcher, EmailTemplates templates, IPolicyDetails policies)
    {
        _dispatcher = dispatcher;
        _templates = templates;
        _policies = policies;
    }

    public Task Consume(ConsumeContext<PaymentReminderDue> context)
    {
        var e = context.Message;
        return _dispatcher.SendAsync(new Notification(
            $"reminder:{e.PaymentId}:{e.Stage}", e.UserId, NotificationKind.PaymentReminder,
            async user => _templates.PaymentReminder(user.FullName, e, await _policies.PlanNameAsync(e.PolicyId))),
            context.CancellationToken);
    }
}
