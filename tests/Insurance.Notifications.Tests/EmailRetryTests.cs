using System.Net.Sockets;
using MailKit.Net.Smtp;
using Insurance.Notifications.Email;
using Insurance.Notifications.Models;
using Insurance.Notifications.Repositories;
using Insurance.Notifications.Services;
using Insurance.Notifications.Templates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Insurance.Notifications.Tests;

/// <summary>When the mail server says "later" (its sending limit), the email waits and goes out on a later try.</summary>
public class EmailRetryTests
{
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private readonly ManualClock _clock = new();
    private readonly FakeNotificationRepository _repo = new();
    private readonly FakeUserDirectory _users = new();
    private readonly FakeEmailSender _smtp = new();
    private readonly NotificationDispatcher _dispatcher;
    private readonly EmailRetryJob _job;

    public EmailRetryTests()
    {
        _repo.Now = () => _clock.Now.UtcDateTime;
        _dispatcher = new NotificationDispatcher(_repo, _users, _smtp, Options.Create(new EmailOptions()),
            NullLogger<NotificationDispatcher>.Instance, _clock);
        var services = new ServiceCollection().AddSingleton<INotificationRepository>(_repo).BuildServiceProvider();
        _job = new EmailRetryJob(services.GetRequiredService<IServiceScopeFactory>(), _smtp,
            Options.Create(new NotificationOptions()), _clock, NullLogger<EmailRetryJob>.Instance);
    }

    private static Notification Receipt() =>
        new("receipt:1", 1, NotificationKind.PaymentReceipt, _ => Task.FromResult(
            new RenderedEmail("Payment receipt TXN-1", "<p>html</p>", "text")
            {
                Attachments = new[] { new EmailAttachment("receipt.pdf", "application/pdf", new byte[] { 1, 2, 3 }) }
            }));

    private void After(TimeSpan time) => _clock.Now += time;

    [Fact]
    public async Task A_sending_limit_queues_the_email_instead_of_failing_it()
    {
        _smtp.LimitNext = 1;

        await _dispatcher.SendAsync(Receipt());                         // no exception: nothing for MassTransit to retry

        Assert.Empty(_smtp.Sent);
        var row = _repo.Emails["receipt:1"];
        Assert.Equal(EmailStatus.Waiting, row.Status);
        Assert.Contains("rate limit", row.Detail);
        Assert.Equal(_clock.Now.UtcDateTime.AddMinutes(1), _repo.Retries["receipt:1"].SendAfter);
    }

    [Fact]
    public async Task A_redelivered_event_doesnt_send_a_waiting_email_again()
    {
        _smtp.LimitNext = 1;
        await _dispatcher.SendAsync(Receipt());

        await _dispatcher.SendAsync(Receipt());

        Assert.Empty(_smtp.Sent);
        Assert.Single(_repo.Retries);
    }

    [Fact]
    public async Task An_email_refused_for_good_is_failed_without_retrying()
    {
        _smtp.RefuseNext = 1;

        await _dispatcher.SendAsync(Receipt());

        Assert.Equal(EmailStatus.Failed, _repo.Emails["receipt:1"].Status);
        Assert.Empty(_repo.Retries);
    }

    [Fact]
    public async Task The_waiting_email_goes_out_once_its_wait_is_over_with_its_attachments()
    {
        _smtp.LimitNext = 1;
        await _dispatcher.SendAsync(Receipt());

        await _job.RetryDueAsync(default);                              // not due yet
        Assert.Empty(_smtp.Sent);

        After(TimeSpan.FromMinutes(1));
        await _job.RetryDueAsync(default);

        var sent = Assert.Single(_smtp.Sent);
        Assert.Equal(("jane@example.com", "Payment receipt TXN-1"), (sent.ToAddress, sent.Subject));
        Assert.Equal(new byte[] { 1, 2, 3 }, Assert.Single(sent.Attachments!).Content);
        Assert.Equal(EmailStatus.Sent, _repo.Emails["receipt:1"].Status);
        Assert.Empty(_repo.Retries);
    }

    [Fact]
    public async Task Each_new_refusal_waits_longer()
    {
        _smtp.LimitNext = 2;                                            // the first send and the first retry
        await _dispatcher.SendAsync(Receipt());
        After(TimeSpan.FromMinutes(1));

        await _job.RetryDueAsync(default);

        Assert.Empty(_smtp.Sent);
        Assert.Equal(EmailStatus.Waiting, _repo.Emails["receipt:1"].Status);
        Assert.Equal(_clock.Now.UtcDateTime.AddMinutes(5), _repo.Retries["receipt:1"].SendAfter);
    }

    [Fact]
    public async Task After_the_whole_schedule_the_email_is_given_up()
    {
        _smtp.LimitNext = 100;
        await _dispatcher.SendAsync(Receipt());

        for (var i = 0; i < EmailRetrySchedule.Delays.Length + 2; i++)
        {
            After(TimeSpan.FromHours(5));
            await _job.RetryDueAsync(default);
        }

        Assert.Empty(_smtp.Sent);
        Assert.Equal(EmailStatus.Failed, _repo.Emails["receipt:1"].Status);
        Assert.StartsWith($"Gave up after {EmailRetrySchedule.Delays.Length + 1} tries", _repo.Emails["receipt:1"].Detail);
        Assert.Empty(_repo.Retries);
    }

    [Fact]
    public void The_schedule_waits_about_half_a_day_in_all()
    {
        Assert.Equal(TimeSpan.FromMinutes(1), EmailRetrySchedule.DelayAfter(1));
        Assert.Null(EmailRetrySchedule.DelayAfter(EmailRetrySchedule.Delays.Length + 1));
        var total = EmailRetrySchedule.Delays.Aggregate(TimeSpan.Zero, (sum, d) => sum + d);
        Assert.InRange(total, TimeSpan.FromHours(10), TimeSpan.FromHours(16));
    }

    [Theory]
    [InlineData(451, "4.3.2 Try again later", true)]
    [InlineData(421, "4.7.0 Too many connections", true)]
    [InlineData(554, "5.2.0 SubmissionQuotaExceededException", true)]     // a limit reported as 5xx
    [InlineData(550, "5.7.1 Message rate limit exceeded", true)]
    [InlineData(550, "5.1.5 Recipient address reserved by RFC 2606.", false)]
    [InlineData(550, "5.1.1 Recipient address rejected: user unknown", false)]
    public void Server_answers_are_sorted_into_later_and_never(int status, string message, bool tryLater)
    {
        var ex = new SmtpCommandException(SmtpErrorCode.RecipientNotAccepted, (SmtpStatusCode)status, message);

        Assert.Equal(tryLater, SmtpEmailSender.Classify(ex)!.TryLater);
    }

    [Fact]
    public void No_connection_or_a_failed_sign_in_means_later_and_anything_else_is_left_alone()
    {
        Assert.True(SmtpEmailSender.Classify(new IOException("connection reset"))!.TryLater);
        Assert.True(SmtpEmailSender.Classify(new SocketException())!.TryLater);
        Assert.True(SmtpEmailSender.Classify(new MailKit.Security.AuthenticationException("535 bad login"))!.TryLater);
        Assert.Null(SmtpEmailSender.Classify(new InvalidOperationException("a bug")));
    }
}
