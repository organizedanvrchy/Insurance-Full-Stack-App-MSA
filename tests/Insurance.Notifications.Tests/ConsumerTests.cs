using Insurance.Billing.Contracts;
using Insurance.Claims.Contracts;
using Insurance.Enrollments.Contracts;
using Insurance.Identity.Contracts;
using Insurance.Notifications.Email;
using Insurance.Notifications.Messaging;
using Insurance.Notifications.Repositories;
using Insurance.Notifications.Services;
using Insurance.Notifications.Templates;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Insurance.Notifications.Tests;

// The consumers on MassTransit's in-memory test bus: an event goes in, an email (or none) comes out.
public class ConsumerTests : IAsyncLifetime
{
    private readonly FakeNotificationRepository _repo = new();
    private readonly FakeEmailSender _smtp = new();
    private ServiceProvider _provider = null!;
    private ITestHarness _harness = null!;

    public async Task InitializeAsync()
    {
        _provider = new ServiceCollection()
            .AddLogging()
            .AddOptions()
            .AddSingleton<INotificationRepository>(_repo)
            .AddSingleton<IUserDirectory, FakeUserDirectory>()
            .AddSingleton<IEmailSender>(_smtp)
            .AddSingleton<IPolicyDetails, FakePolicyDetails>()
            .AddSingleton(new AppLinks("http://app.test"))
            .AddSingleton<EmailTemplates>()
            .AddScoped<INotificationDispatcher, NotificationDispatcher>()
            .Configure<NotificationOptions>(o => o.ClaimEmailDelay = TimeSpan.FromMinutes(2))
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<EmailVerificationRequestedConsumer>();
                x.AddConsumer<ClaimStatusChangedConsumer>();
                x.AddConsumer<ClaimSubmittedConsumer>();
                x.AddConsumer<PaymentReceivedConsumer>();
                x.AddConsumer<PaymentDeclinedConsumer>();
                x.AddConsumer<CustomerRegisteredConsumer>();
                x.AddConsumer<PaymentReminderDueConsumer>();
                x.AddConsumer<EnrollmentActivatedConsumer>();
            })
            .BuildServiceProvider(true);

        _harness = _provider.GetRequiredService<ITestHarness>();
        await _harness.Start();
    }

    public async Task DisposeAsync()
    {
        await _harness.Stop();
        await _provider.DisposeAsync();
    }

    private async Task PublishAndWaitAsync<T>(T message) where T : class
    {
        await _harness.Bus.Publish(message);
        Assert.True(await _harness.Consumed.Any<T>());
        await _harness.InactivityTask;     // let the consumer finish
    }

    [Fact]
    public async Task A_claim_decision_is_emailed_with_the_plan_name()
    {
        await PublishAndWaitAsync(new ClaimStatusChanged(7, "CLM-2026-000007", 1, 10, 500m,
            ClaimStatus.UnderReview, ClaimStatus.Approved, null, DateTime.UtcNow));

        var email = Assert.Single(_smtp.Sent);
        Assert.Equal("Good news: claim CLM-2026-000007 was approved", email.Subject);
        Assert.Contains("Homeowners Preferred", email.TextBody);
        Assert.True(_repo.Emails.ContainsKey("claim-status:7:Approved"));
    }

    [Fact]
    public async Task Moving_a_claim_to_under_review_sends_no_email()
    {
        await PublishAndWaitAsync(new ClaimStatusChanged(7, "CLM-2026-000007", 1, 10, 500m,
            ClaimStatus.Submitted, ClaimStatus.UnderReview, null, DateTime.UtcNow));

        Assert.Empty(_smtp.Sent);
    }

    [Fact]
    public async Task A_paid_claim_tells_the_customer_the_check_is_coming()
    {
        await PublishAndWaitAsync(new ClaimStatusChanged(9, "CLM-2026-000009", 1, 10, 500m,
            ClaimStatus.Approved, ClaimStatus.Paid, null, DateTime.UtcNow));

        Assert.Equal("Your payment for claim CLM-2026-000009 is on its way", Assert.Single(_smtp.Sent).Subject);
    }

    [Fact]
    public async Task A_rejection_is_emailed_with_the_reason()
    {
        await PublishAndWaitAsync(new ClaimStatusChanged(8, "CLM-2026-000008", 1, 10, 500m,
            ClaimStatus.UnderReview, ClaimStatus.Rejected, "Not covered by this plan.", DateTime.UtcNow));

        var email = Assert.Single(_smtp.Sent);
        Assert.Contains("Reason: Not covered by this plan.", email.TextBody);
    }

    [Fact]
    public async Task A_new_claim_waits_for_its_documents_before_emailing()
    {
        await PublishAndWaitAsync(new ClaimSubmitted(5, "CLM-2026-000005", 1, 10, new DateOnly(2026, 11, 1), 250m, DateTime.UtcNow));

        Assert.Empty(_smtp.Sent);                               // not yet: documents are still being uploaded
        var queued = Assert.Single(_repo.PendingClaimEmails);
        Assert.Equal(5, queued.Key);
        Assert.InRange(queued.Value.SendAfter, DateTime.UtcNow.AddSeconds(100), DateTime.UtcNow.AddSeconds(130));
    }

    [Fact]
    public async Task A_payment_produces_a_receipt()
    {
        await PublishAndWaitAsync(new PaymentReceived(42, 7, "TXN-000007", 10, 1, 4, 89.99m, new DateOnly(2026, 11, 1),
            new DateOnly(2026, 11, 2), DateTime.UtcNow, "Visa ending in 4242", 1, 12, new DateOnly(2026, 12, 1), 89.99m));

        Assert.Equal("Payment receipt TXN-000007", Assert.Single(_smtp.Sent).Subject);
    }

    [Fact]
    public async Task A_declined_payment_is_emailed_even_with_reminders_off()
    {
        _repo.PaymentReminders[1] = false;

        await PublishAndWaitAsync(new PaymentDeclined(7, "TXN-000007", 42, 10, 1, 4, 89.99m, new DateOnly(2026, 11, 2),
            "Visa ending in 0002", "Your card was declined.", false, DateTime.UtcNow));

        Assert.Equal("Your Gold Health Plan payment didn't go through", Assert.Single(_smtp.Sent).Subject);
        Assert.True(_repo.Emails.ContainsKey("payment-declined:7"));
    }

    [Fact]
    public async Task The_sign_up_choice_becomes_the_preference()
    {
        await PublishAndWaitAsync(new CustomerRegistered(1, PaymentReminders: false));

        Assert.False(_repo.PaymentReminders[1]);
    }

    [Fact]
    public async Task A_late_sign_up_event_never_overrides_a_choice_made_since()
    {
        _repo.PaymentReminders[1] = true;    // changed on the profile page first

        await PublishAndWaitAsync(new CustomerRegistered(1, PaymentReminders: false));

        Assert.True(_repo.PaymentReminders[1]);
    }

    [Fact]
    public async Task Reminders_respect_the_opt_out()
    {
        _repo.PaymentReminders[1] = false;

        await PublishAndWaitAsync(new PaymentReminderDue(42, 10, 1, 4, new DateOnly(2026, 11, 5), 89.99m,
            PaymentReminderStage.DueSoon, -3));

        Assert.Empty(_smtp.Sent);
    }

    [Fact]
    public async Task The_same_reminder_published_twice_is_emailed_once()
    {
        var reminder = new PaymentReminderDue(42, 10, 1, 4, new DateOnly(2026, 11, 2), 89.99m, PaymentReminderStage.DueToday, 0);

        await PublishAndWaitAsync(reminder);
        await PublishAndWaitAsync(reminder);     // e.g. the outbox republished it after a crash

        Assert.Single(_smtp.Sent);
    }

    [Fact]
    public async Task A_verification_link_goes_to_the_address_it_was_made_for()
    {
        await PublishAndWaitAsync(new EmailVerificationRequested(1, "Jane Doe", "jane.new@example.com", "tok",
            DateTime.UtcNow.AddDays(1)));

        var email = Assert.Single(_smtp.Sent);
        Assert.Equal("jane.new@example.com", email.ToAddress);
        Assert.Contains("http://app.test/verify-email?token=tok", email.TextBody);
    }

    [Fact]
    public async Task An_active_policy_gets_a_welcome_email()
    {
        await PublishAndWaitAsync(new EnrollmentActivated(10, 1, 4, 99));

        var email = Assert.Single(_smtp.Sent);
        Assert.Equal("Your Homeowners Preferred policy is active", email.Subject);
        Assert.Contains("$250,000.00", email.TextBody);
    }
}
