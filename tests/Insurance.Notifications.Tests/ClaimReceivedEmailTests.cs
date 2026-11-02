using System.Text.Json;
using Insurance.Claims.Contracts;
using Insurance.Notifications.Email;
using Insurance.Notifications.Services;
using Insurance.Notifications.Templates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Insurance.Notifications.Tests;

// The customer's copy of their claim: sent after a short delay, listing every document and attaching copies.
public class ClaimReceivedEmailTests
{
    private static readonly byte[] Pdf = "%PDF-1.4 receipt"u8.ToArray();
    private static readonly byte[] Png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };

    private readonly FakeNotificationRepository _repo = new();
    private readonly FakeEmailSender _smtp = new();
    private readonly FakeClaimDocuments _documents = new();
    private readonly EmailOptions _email = new();

    private ClaimReceivedEmail Sut() => new(
        new NotificationDispatcher(_repo, new FakeUserDirectory(), _smtp, Options.Create(_email), NullLogger<NotificationDispatcher>.Instance),
        new EmailTemplates(new AppLinks("http://app.test")), new FakePolicyDetails(), _documents, Options.Create(_email));

    private static ClaimSubmitted Claim(int id = 5) =>
        new(id, $"CLM-2026-00000{id}", 1, 10, new DateOnly(2026, 11, 1), 250m, DateTime.UtcNow);

    [Fact]
    public async Task The_documents_are_listed_and_copies_attached()
    {
        _documents.Add(5, "receipt.pdf", "application/pdf", Pdf);
        _documents.Add(5, "damage.png", "image/png", Png);
        _documents.Add(6, "someone-elses.pdf", "application/pdf", Pdf);

        await Sut().SendAsync(Claim());

        var email = Assert.Single(_smtp.Sent);
        Assert.Contains("Documents added to this claim (2):", email.TextBody);
        Assert.Contains("receipt.pdf", email.TextBody);
        Assert.Contains("PDF, 16 bytes", email.TextBody);
        Assert.Contains("PNG image", email.TextBody);
        Assert.Contains("Copies are attached to this email.", email.TextBody);
        Assert.DoesNotContain("someone-elses.pdf", email.TextBody);

        Assert.Equal(new[] { "receipt.pdf", "damage.png" }, email.Attachments!.Select(a => a.FileName));
        Assert.Equal(Pdf, email.Attachments![0].Content);
        Assert.Equal("image/png", email.Attachments![1].ContentType);
    }

    [Fact]
    public async Task Files_too_big_to_email_are_listed_but_not_attached()
    {
        _email.MaxAttachmentBytes = 20;
        _documents.Add(5, "small.pdf", "application/pdf", Pdf);                  // 16 bytes: fits
        _documents.Add(5, "big.png", "image/png", new byte[30]);                // over what's left

        await Sut().SendAsync(Claim());

        var email = Assert.Single(_smtp.Sent);
        Assert.Equal("small.pdf", Assert.Single(email.Attachments!).FileName);
        Assert.Contains("big.png", email.TextBody);
        Assert.Contains("except for files too large to email", email.TextBody);
    }

    [Fact]
    public async Task A_claim_without_documents_says_they_can_still_be_added()
    {
        await Sut().SendAsync(Claim());

        var email = Assert.Single(_smtp.Sent);
        Assert.Empty(email.Attachments!);
        Assert.Contains("No documents were added to this claim.", email.TextBody);
    }

    [Fact]
    public async Task The_job_sends_due_emails_and_keeps_failed_ones_for_a_retry()
    {
        _repo.PendingClaimEmails[5] = (JsonSerializer.Serialize(Claim(5)), DateTime.UtcNow.AddSeconds(-1));    // due
        _repo.PendingClaimEmails[7] = (JsonSerializer.Serialize(Claim(7)), DateTime.UtcNow.AddMinutes(2));     // not yet

        var services = new ServiceCollection()
            .AddSingleton<Insurance.Notifications.Repositories.INotificationRepository>(_repo)
            .AddSingleton<IClaimReceivedEmail>(Sut())
            .BuildServiceProvider();
        var job = new ClaimReceivedEmailJob(services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new NotificationOptions()), NullLogger<ClaimReceivedEmailJob>.Instance);

        await job.SendDueAsync(CancellationToken.None);

        Assert.Equal("We received your claim CLM-2026-000005", Assert.Single(_smtp.Sent).Subject);
        Assert.Equal(new[] { 7 }, _repo.PendingClaimEmails.Keys);           // 5 sent and removed; 7 waits

        // Claims is down when 7 is due: the email stays queued and is retried later.
        _repo.PendingClaimEmails[7] = (_repo.PendingClaimEmails[7].Payload, DateTime.UtcNow.AddSeconds(-1));
        _documents.Unavailable = true;
        await job.SendDueAsync(CancellationToken.None);

        Assert.Single(_smtp.Sent);
        Assert.Contains(7, _repo.PendingClaimEmails.Keys);
    }
}
