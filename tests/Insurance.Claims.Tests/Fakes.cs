using Insurance.Billing.Contracts;
using Insurance.Catalog.Contracts;
using Insurance.Claims.Contracts;
using Insurance.Claims.Enums;
using Insurance.Claims.Exceptions;
using Insurance.Claims.Models;
using Insurance.Claims.Repositories;
using Insurance.Claims.Services;
using Insurance.Enrollments.Contracts;
using Insurance.Identity.Contracts;

namespace Insurance.Claims.Tests;

// Fakes are tiny in-memory stand-ins for the 
// real repositories and for the other services' contracts.
// The services only know the interfaces, so they 
// can't tell the difference. That's dependency injection 
// paying off: no database, no Azure, tests run in milliseconds.

public class FakeClaimRepository : IClaimRepository
{
    public List<InsuranceClaim> Claims { get; } = new();

    // Remembers how the service called ReviewAsync, so tests can check the arguments.
    public (int ClaimId, ClaimStatus From, ClaimStatus To, string? Notes, int ReviewerId, decimal? Coverage)? LastReview { get; private set; }
    public decimal? LastSubmitCoverage { get; private set; }

    public Task<int> SubmitAsync(int userId, int enrollmentId, DateOnly incidentDate, string description, decimal amount, decimal coverageAmount)
    {
        LastSubmitCoverage = coverageAmount;
        var claim = new InsuranceClaim
        {
            ClaimId = Claims.Count + 1,
            ClaimNumber = $"CLM-TEST-{Claims.Count + 1:000000}",
            UserId = userId,
            EnrollmentId = enrollmentId,
            IncidentDate = incidentDate,
            Description = description,
            ClaimAmount = amount,
            Status = ClaimStatus.Submitted
        };
        Claims.Add(claim);
        return Task.FromResult(claim.ClaimId);
    }

    public Task<List<InsuranceClaim>> GetByUserAsync(int userId) =>
        Task.FromResult(Claims.Where(c => c.UserId == userId).ToList());

    public Task<List<InsuranceClaim>> GetAllAsync(ClaimStatus? status) =>
        Task.FromResult(Claims.Where(c => status == null || c.Status == status).ToList());

    public Task<InsuranceClaim?> GetByIdAsync(int claimId) =>
        Task.FromResult(Claims.FirstOrDefault(c => c.ClaimId == claimId));

    public Task<InsuranceClaim> ReviewAsync(int claimId, ClaimStatus from, ClaimStatus to, string? adminNotes, int reviewerId, decimal? coverageAmount)
    {
        LastReview = (claimId, from, to, adminNotes, reviewerId, coverageAmount);

        var claim = Claims.Single(c => c.ClaimId == claimId);
        claim.Status = to;
        claim.AdminNotes = adminNotes ?? claim.AdminNotes;
        claim.ReviewedByUserId = reviewerId;
        return Task.FromResult(claim);
    }

    public Task<InsuranceClaim> SendToReviewerAsync(int claimId, int userId)
    {
        var claim = Claims.Single(c => c.ClaimId == claimId && c.UserId == userId);
        claim.Status = ClaimStatus.UnderReview;
        return Task.FromResult(claim);
    }

    public Task<Dictionary<int, ClaimCounts>> GetCountsByUserAsync(IEnumerable<int> userIds) =>
        Task.FromResult(Claims
            .Where(c => userIds.Contains(c.UserId))
            .GroupBy(c => c.UserId)
            .ToDictionary(g => g.Key, g => new ClaimCounts(
                g.Count(), g.Count(c => c.Status is ClaimStatus.Submitted or ClaimStatus.UnderReview or ClaimStatus.NeedsMoreInfo))));
}

public class FakeClaimDocumentRepository : IClaimDocumentRepository
{
    public List<ClaimDocument> Documents { get; } = new();

    // Simulates the database failing after the file has been saved.
    public bool FailNextAdd { get; set; }

    public Task<ClaimDocument> AddAsync(ClaimDocument document, int maxDocuments)
    {
        if (FailNextAdd) throw new InvalidOperationException("Database unavailable.");

        // The real check is in usp_ClaimDocument_Add, under a lock.
        if (Documents.Count(d => d.ClaimId == document.ClaimId) >= maxDocuments)
            throw new ClaimConflictException($"A claim can have up to {maxDocuments} documents.");

        document.ClaimDocumentId = Documents.Count + 1;
        Documents.Add(document);
        return Task.FromResult(document);
    }

    public Task<List<ClaimDocument>> GetByClaimAsync(int claimId) =>
        Task.FromResult(Documents.Where(d => d.ClaimId == claimId).ToList());

    public Task<ClaimDocument?> GetByIdAsync(int claimId, int claimDocumentId) =>
        Task.FromResult(Documents.FirstOrDefault(d => d.ClaimId == claimId && d.ClaimDocumentId == claimDocumentId));
}

public class FakeDocumentStorage : IDocumentStorage
{
    public Dictionary<string, byte[]> Files { get; } = new();

    public async Task SaveAsync(string key, Stream content, CancellationToken ct = default)
    {
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, ct);
        Files[key] = copy.ToArray();
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default) =>
        Task.FromResult<Stream?>(Files.TryGetValue(key, out var bytes) ? new MemoryStream(bytes) : null);

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        Files.Remove(key);
        return Task.CompletedTask;
    }
}

// The other services, as the Claims service sees them through their contracts (HTTP clients in production).

public class FakeEnrollmentLookup : IEnrollmentLookup
{
    public Dictionary<int, EnrollmentInfo> Enrollments { get; } = new();

    public Task<EnrollmentInfo?> GetAsync(int enrollmentId) =>
        Task.FromResult(Enrollments.GetValueOrDefault(enrollmentId));

    public Task<IReadOnlyDictionary<int, EnrollmentInfo>> GetManyAsync(IEnumerable<int> enrollmentIds) =>
        Task.FromResult<IReadOnlyDictionary<int, EnrollmentInfo>>(
            Enrollments.Where(e => enrollmentIds.Contains(e.Key)).ToDictionary(e => e.Key, e => e.Value));

    public Task<IReadOnlyDictionary<int, int>> CountActiveByUserAsync(IEnumerable<int> userIds) =>
        throw new NotSupportedException();
}

public class FakePremiumSummaries : IPremiumSummaries
{
    public HashSet<int> Overdue { get; } = new();

    public Task<IReadOnlyDictionary<int, PremiumSummary>> GetSummariesAsync(IEnumerable<int> enrollmentIds) =>
        Task.FromResult<IReadOnlyDictionary<int, PremiumSummary>>(enrollmentIds.Distinct().ToDictionary(
            id => id, id => new PremiumSummary(0, 12, Overdue.Contains(id) ? 1 : 0, null, null)));
}

public class FakePlanCatalog : IPlanCatalog
{
    public static readonly PlanInfo Plan = new(1, "Test Plan", PolicyType.Home, 10_000m, 50m, 12, IsActive: true);

    public Task<PlanInfo?> GetAsync(int policyId) =>
        Task.FromResult<PlanInfo?>(policyId == Plan.PolicyId ? Plan : null);

    public Task<IReadOnlyDictionary<int, PlanInfo>> GetManyAsync(IEnumerable<int> policyIds) =>
        Task.FromResult<IReadOnlyDictionary<int, PlanInfo>>(
            policyIds.Contains(Plan.PolicyId) ? new Dictionary<int, PlanInfo> { [Plan.PolicyId] = Plan } : new());
}

public class FakeUserDirectory : IUserDirectory
{
    public Dictionary<int, UserAccount> Users { get; } = new();

    public Task<UserAccount?> GetAsync(int userId) =>
        Task.FromResult(Users.GetValueOrDefault(userId));

    public Task<IReadOnlyDictionary<int, UserAccount>> GetManyAsync(IEnumerable<int> userIds) =>
        Task.FromResult<IReadOnlyDictionary<int, UserAccount>>(
            Users.Where(u => userIds.Contains(u.Key)).ToDictionary(u => u.Key, u => u.Value));
}
