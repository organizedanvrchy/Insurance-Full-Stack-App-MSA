using Insurance.Notifications.Email;
using Insurance.Notifications.Models;
using Insurance.Notifications.Services;
using Insurance.Notifications.Templates;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Insurance.Notifications.Tests;

public class NotificationDispatcherTests
{
    private readonly FakeNotificationRepository _repo = new();
    private readonly FakeUserDirectory _users = new();
    private readonly FakeEmailSender _smtp = new();
    private readonly EmailOptions _options = new() { SkipDomains = new[] { "demo.example" } };
    private readonly NotificationDispatcher _sut;

    public NotificationDispatcherTests()
    {
        _sut = new NotificationDispatcher(_repo, _users, _smtp, Options.Create(_options), NullLogger<NotificationDispatcher>.Instance);
    }

    private static Notification Email(string key = "receipt:1", NotificationKind kind = NotificationKind.PaymentReceipt,
        int userId = 1, string? to = null) =>
        new(key, userId, kind, user => Task.FromResult(new RenderedEmail($"Hello {user.FullName}", "<p>html</p>", "text")), to);

    [Fact]
    public async Task Sends_to_the_accounts_address_and_records_it()
    {
        await _sut.SendAsync(Email());

        var sent = Assert.Single(_smtp.Sent);
        Assert.Equal("jane@example.com", sent.ToAddress);
        Assert.Equal("Jane Doe", sent.ToName);
        Assert.Equal("Hello Jane Doe", sent.Subject);
        Assert.Equal(EmailStatus.Sent, _repo.Emails["receipt:1"].Status);
        Assert.Equal("jane@example.com", _repo.Emails["receipt:1"].ToEmail);
    }

    [Fact]
    public async Task A_repeated_event_does_not_send_twice()
    {
        await _sut.SendAsync(Email());
        await _sut.SendAsync(Email());

        Assert.Single(_smtp.Sent);
    }

    [Fact]
    public async Task Verification_links_go_to_the_address_being_verified()
    {
        await _sut.SendAsync(Email("verify:1", NotificationKind.VerifyEmail, to: "new@example.com"));

        Assert.Equal("new@example.com", Assert.Single(_smtp.Sent).ToAddress);
    }

    [Fact]
    public async Task Customers_who_turned_reminders_off_get_none()
    {
        _repo.PaymentReminders[1] = false;

        await _sut.SendAsync(Email("reminder:1:DueSoon", NotificationKind.PaymentReminder));

        Assert.Empty(_smtp.Sent);
        Assert.Equal(EmailStatus.Skipped, _repo.Emails["reminder:1:DueSoon"].Status);
        Assert.Contains("turned off", _repo.Emails["reminder:1:DueSoon"].Detail);
    }

    [Fact]
    public async Task Turning_reminders_off_never_stops_receipts()
    {
        _repo.PaymentReminders[1] = false;

        await _sut.SendAsync(Email("receipt:1", NotificationKind.PaymentReceipt));

        Assert.Single(_smtp.Sent);
    }

    [Fact]
    public async Task Deactivated_accounts_get_no_email()
    {
        _users.Users[1] = _users.Users[1] with { IsActive = false };

        await _sut.SendAsync(Email());

        Assert.Empty(_smtp.Sent);
        Assert.Equal(EmailStatus.Skipped, _repo.Emails["receipt:1"].Status);
    }

    [Fact]
    public async Task Unknown_accounts_are_skipped()
    {
        await _sut.SendAsync(Email(userId: 99));

        Assert.Empty(_smtp.Sent);
        Assert.Equal(EmailStatus.Skipped, _repo.Emails["receipt:1"].Status);
    }

    [Fact]
    public async Task Configured_domains_are_never_emailed()
    {
        _users.Users[1] = _users.Users[1] with { Email = "emma.carter@demo.example" };

        await _sut.SendAsync(Email());

        Assert.Empty(_smtp.Sent);
        Assert.Equal(EmailStatus.Skipped, _repo.Emails["receipt:1"].Status);
    }

    [Fact]
    public async Task Reserved_test_domains_are_skipped_when_switched_on()
    {
        _options.SkipReservedDomains = true;            // as on a real mail server (Azure)

        await _sut.SendAsync(Email());                  // jane@example.com

        Assert.Empty(_smtp.Sent);
        Assert.Equal(EmailStatus.Skipped, _repo.Emails["receipt:1"].Status);
    }

    [Fact]
    public async Task Reserved_test_domains_are_emailed_by_default_for_the_local_catch_all()
    {
        await _sut.SendAsync(Email());                  // jane@example.com, to Mailpit

        Assert.Single(_smtp.Sent);
    }

    [Theory]
    [InlineData("jane@example.com", true)]
    [InlineData("jane@EXAMPLE.org", true)]
    [InlineData("jane@mail.example.net", true)]
    [InlineData("cal.1@e2e.example", true)]
    [InlineData("a@b.test", true)]
    [InlineData("a@nowhere.invalid", true)]
    [InlineData("a@localhost", true)]
    [InlineData("jane@gmail.com", false)]
    [InlineData("jane@myexample.com", false)]
    [InlineData("jane@example.co", false)]
    [InlineData("jane@contest.com", false)]
    public void Recognizes_reserved_domains(string address, bool reserved)
    {
        Assert.Equal(reserved, NotificationDispatcher.IsReservedDomain(address));
    }

    [Fact]
    public async Task A_failed_send_is_recorded_rethrown_and_retried()
    {
        _smtp.FailNext = 1;

        await Assert.ThrowsAsync<IOException>(() => _sut.SendAsync(Email()));     // MassTransit retries the event
        Assert.Equal(EmailStatus.Failed, _repo.Emails["receipt:1"].Status);
        Assert.Contains("unreachable", _repo.Emails["receipt:1"].Detail);

        await _sut.SendAsync(Email());                                              // the retry

        Assert.Single(_smtp.Sent);
        Assert.Equal(EmailStatus.Sent, _repo.Emails["receipt:1"].Status);
        Assert.Equal(2, _repo.Emails["receipt:1"].Attempts);
    }

    [Fact]
    public async Task The_email_is_only_rendered_when_it_will_be_sent()
    {
        _repo.PaymentReminders[1] = false;
        var rendered = false;

        await _sut.SendAsync(new Notification("reminder:1:DueToday", 1, NotificationKind.PaymentReminder, _ =>
        {
            rendered = true;      // rendering may call other services, so skipped emails shouldn't pay for it
            return Task.FromResult(new RenderedEmail("s", "h", "t"));
        }));

        Assert.False(rendered);
    }
}
