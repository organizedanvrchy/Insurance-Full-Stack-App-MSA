using Insurance.BuildingBlocks.Security;
using Insurance.Identity.Contracts;
using Insurance.Notifications.Email;
using Insurance.Notifications.Models;
using Insurance.Notifications.Repositories;
using Insurance.Notifications.Services;
using Insurance.Notifications.Templates;

namespace Insurance.Notifications.Tests;

// In-memory stand-ins for the database, the Identity/Catalog/Enrollments services and the SMTP server.

/// <summary>Mimics notifications.usp_Email_Begin / usp_Email_Complete and the preferences procs.</summary>
public class FakeNotificationRepository : INotificationRepository
{
    public record Row(string Key, int UserId, NotificationKind Kind)
    {
        public EmailStatus Status { get; set; } = EmailStatus.Sending;
        public string? ToEmail { get; set; }
        public string? Subject { get; set; }
        public string? Detail { get; set; }
        public int Attempts { get; set; } = 1;
    }

    public Dictionary<string, Row> Emails { get; } = new();
    public Dictionary<int, bool> PaymentReminders { get; } = new();

    public Task<NotificationPreferences> GetPreferencesAsync(int userId) =>
        Task.FromResult(new NotificationPreferences(userId, PaymentReminders.GetValueOrDefault(userId, true), null));

    public Task<NotificationPreferences> SetPreferencesAsync(int userId, bool paymentReminders)
    {
        PaymentReminders[userId] = paymentReminders;
        return GetPreferencesAsync(userId);
    }

    public Task SetInitialPreferencesAsync(int userId, bool paymentReminders)
    {
        PaymentReminders.TryAdd(userId, paymentReminders);   // like the proc: never overwrites a saved choice
        return Task.CompletedTask;
    }

    public Task<bool> BeginAsync(string notificationKey, int userId, NotificationKind kind)
    {
        if (!Emails.TryGetValue(notificationKey, out var row))
        {
            Emails[notificationKey] = new Row(notificationKey, userId, kind);
            return Task.FromResult(true);
        }
        if (row.Status is EmailStatus.Failed or EmailStatus.Sending)
        {
            row.Status = EmailStatus.Sending;
            row.Attempts++;
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public Task CompleteAsync(string notificationKey, EmailStatus status, string? toEmail = null, string? subject = null, string? detail = null)
    {
        var row = Emails[notificationKey];
        row.Status = status;
        row.ToEmail = toEmail ?? row.ToEmail;
        row.Subject = subject ?? row.Subject;
        row.Detail = detail;
        return Task.CompletedTask;
    }

    // ---- Queued claim emails (usp_PendingClaimEmail_*) ----
    public Dictionary<int, (string Payload, DateTime SendAfter)> PendingClaimEmails { get; } = new();

    public Task QueueClaimEmailAsync(int claimId, string payload, DateTime sendAfterUtc)
    {
        PendingClaimEmails.TryAdd(claimId, (payload, sendAfterUtc));
        return Task.CompletedTask;
    }

    public Task<List<PendingClaimEmail>> TakeDueClaimEmailsAsync(int take, TimeSpan lease)
    {
        var due = PendingClaimEmails.Where(p => p.Value.SendAfter <= DateTime.UtcNow).Take(take).ToList();
        foreach (var p in due) PendingClaimEmails[p.Key] = (p.Value.Payload, DateTime.UtcNow.Add(lease));
        return Task.FromResult(due.Select(p => new PendingClaimEmail(p.Key, p.Value.Payload, 1)).ToList());
    }

    public Task RemoveClaimEmailAsync(int claimId)
    {
        PendingClaimEmails.Remove(claimId);
        return Task.CompletedTask;
    }

    public Task<List<SentEmail>> GetRecentAsync(int userId, int take) =>
        Task.FromResult(Emails.Values.Where(e => e.UserId == userId && e.Status == EmailStatus.Sent)
            .Take(take).Select((e, i) => new SentEmail(i + 1, e.Kind, e.ToEmail, e.Subject, DateTime.UtcNow)).ToList());
}

public class FakeUserDirectory : IUserDirectory
{
    public Dictionary<int, UserAccount> Users { get; } = new()
    {
        [1] = new UserAccount(1, "Jane Doe", "jane@example.com", UserRole.Customer, IsActive: true, EmailVerified: true)
    };

    public Task<UserAccount?> GetAsync(int userId) => Task.FromResult(Users.GetValueOrDefault(userId));

    public Task<IReadOnlyDictionary<int, UserAccount>> GetManyAsync(IEnumerable<int> userIds) =>
        Task.FromResult<IReadOnlyDictionary<int, UserAccount>>(
            Users.Where(u => userIds.Contains(u.Key)).ToDictionary(u => u.Key, u => u.Value));
}

public class FakeEmailSender : IEmailSender
{
    public List<EmailMessage> Sent { get; } = new();
    public int FailNext { get; set; }      // how many sends in a row fail (SMTP server down)

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (FailNext > 0)
        {
            FailNext--;
            throw new IOException("SMTP server unreachable");
        }
        Sent.Add(message);
        return Task.CompletedTask;
    }
}

public class FakePolicyDetails : IPolicyDetails
{
    public Task<string> PlanNameAsync(int policyId) => Task.FromResult("Gold Health Plan");
    public Task<string> PlanNameForEnrollmentAsync(int enrollmentId) => Task.FromResult("Homeowners Preferred");

    public Task<PolicySummary> SummaryAsync(int enrollmentId) =>
        Task.FromResult(new PolicySummary("Homeowners Preferred", 250_000m, new DateOnly(2026, 11, 2), new DateOnly(2027, 11, 2)));
}

/// <summary>The Claims service's internal documents API.</summary>
public class FakeClaimDocuments : Insurance.Claims.Contracts.IClaimDocuments
{
    public List<(Insurance.Claims.Contracts.ClaimDocumentInfo Info, byte[] Content)> Documents { get; } = new();
    public bool Unavailable { get; set; }

    public void Add(int claimId, string name, string type, byte[] content) =>
        Documents.Add((new Insurance.Claims.Contracts.ClaimDocumentInfo(Documents.Count + 1, claimId, name, type, content.Length, DateTime.UtcNow), content));

    public Task<List<Insurance.Claims.Contracts.ClaimDocumentInfo>> GetForClaimAsync(int claimId) =>
        Unavailable ? throw new HttpRequestException("Claims is down")
            : Task.FromResult(Documents.Where(d => d.Info.ClaimId == claimId).Select(d => d.Info).ToList());

    public Task<byte[]?> GetContentAsync(int claimId, int claimDocumentId) =>
        Task.FromResult<byte[]?>(Documents.FirstOrDefault(d => d.Info.ClaimId == claimId && d.Info.ClaimDocumentId == claimDocumentId).Content);
}
