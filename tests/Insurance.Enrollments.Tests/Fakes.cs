using Insurance.Billing.Contracts;
using Insurance.Catalog.Contracts;
using Insurance.Enrollments.Contracts;
using Insurance.Enrollments.Exceptions;
using Insurance.Enrollments.Models;
using Insurance.Enrollments.Repositories;
using Insurance.BuildingBlocks.Security;
using Insurance.Identity.Contracts;
using Insurance.Quotes.Contracts;

namespace Insurance.Enrollments.Tests;

// Fakes are tiny in-memory stand-ins for the
// real repositories and for the other services' contracts.
// The services only know the interfaces, so they
// can't tell the difference. That's dependency injection
// paying off: no database, no Azure, tests run in milliseconds.

public class FakeEnrollmentRepository : IEnrollmentRepository
{
    public List<Enrollment> Enrollments { get; } = new();
    public (int UserId, int PolicyId, int QuoteId, DateOnly StartDate, int TermMonths, decimal MonthlyPremium)? LastCreate { get; private set; }
    public List<int> CancelledEvents { get; } = new();   // what CancelAsync would have written to the outbox

    // What the rest of the saga (Billing, then the consumers) does after CreatePendingAsync: by default it succeeds
    // at once. Null leaves the enrollment Pending, as if Billing were slow.
    public EnrollmentStatus? SagaOutcome { get; set; } = EnrollmentStatus.Active;
    private int _nextId = 100;

    public Task<int> CreatePendingAsync(int userId, int policyId, int quoteId, DateOnly startDate, int termMonths, decimal monthlyPremium)
    {
        // Mimics the proc's "one active enrollment per plan" rule, which counts Pending ones too.
        if (Enrollments.Any(e => e.UserId == userId && e.PolicyId == policyId
                                 && e.Status is EnrollmentStatus.Active or EnrollmentStatus.Pending))
            throw new DuplicateEnrollmentException();

        LastCreate = (userId, policyId, quoteId, startDate, termMonths, monthlyPremium);
        var enrollment = new Enrollment
        {
            EnrollmentId = _nextId++,
            UserId = userId,
            PolicyId = policyId,
            QuoteId = quoteId,
            StartDate = startDate,
            EndDate = startDate.AddMonths(termMonths),
            MonthlyPremium = monthlyPremium,
            Status = SagaOutcome ?? EnrollmentStatus.Pending
        };
        Enrollments.Add(enrollment);
        return Task.FromResult(enrollment.EnrollmentId);
    }

    public Task<Enrollment?> CompleteAsync(int enrollmentId, bool succeeded)
    {
        var e = Enrollments.FirstOrDefault(x => x.EnrollmentId == enrollmentId);
        if (e is { Status: EnrollmentStatus.Pending })
            e.Status = succeeded ? EnrollmentStatus.Active : EnrollmentStatus.Failed;
        return Task.FromResult(e);
    }

    public Task<List<Enrollment>> GetByUserAsync(int userId) =>
        Task.FromResult(Enrollments.Where(e => e.UserId == userId).ToList());

    // Like the proc: Failed enrollments are never returned.
    public Task<Enrollment?> GetByIdAsync(int enrollmentId) =>
        Task.FromResult(Enrollments.FirstOrDefault(e => e.EnrollmentId == enrollmentId && e.Status != EnrollmentStatus.Failed));

    public Task<List<Enrollment>> GetManyAsync(IEnumerable<int> enrollmentIds) =>
        Task.FromResult(Enrollments.Where(e => enrollmentIds.Contains(e.EnrollmentId)).ToList());

    public Task<Dictionary<int, int>> CountActiveByUserAsync(IEnumerable<int> userIds) =>
        Task.FromResult(Enrollments
            .Where(e => userIds.Contains(e.UserId) && e.Status == EnrollmentStatus.Active)
            .GroupBy(e => e.UserId)
            .ToDictionary(g => g.Key, g => g.Count()));

    public Task<bool> CancelAsync(int enrollmentId, int userId)
    {
        var enrollment = Enrollments.FirstOrDefault(e =>
            e.EnrollmentId == enrollmentId && e.UserId == userId && e.Status == EnrollmentStatus.Active);

        if (enrollment is null) return Task.FromResult(false);

        enrollment.Status = EnrollmentStatus.Cancelled;
        CancelledEvents.Add(enrollmentId);
        return Task.FromResult(true);
    }
}

public class FakeQuoteReservations : IQuoteReservations
{
    // Every quote is valid for plan 1 at $100 unless a test says otherwise.
    public int PolicyId { get; set; } = 1;
    public Exception? RejectWith { get; set; }

    public (int QuoteId, int UserId)? LastReserve { get; private set; }
    public List<int> Released { get; } = new();

    public Task<RedeemableQuote> ReserveAsync(int quoteId, int userId)
    {
        LastReserve = (quoteId, userId);
        if (RejectWith is not null) throw RejectWith;
        return Task.FromResult(new RedeemableQuote(quoteId, PolicyId, 100m));
    }

    public Task ReleaseAsync(int quoteId)
    {
        Released.Add(quoteId);
        return Task.CompletedTask;
    }
}

public class FakePlanCatalog : IPlanCatalog
{
    public Dictionary<int, PlanInfo> Plans { get; } = new()
    {
        [1] = new PlanInfo(1, "Test Plan", PolicyType.Auto, 50_000m, 80m, 12, IsActive: true)
    };

    public Task<PlanInfo?> GetAsync(int policyId) =>
        Task.FromResult(Plans.GetValueOrDefault(policyId));

    public Task<IReadOnlyDictionary<int, PlanInfo>> GetManyAsync(IEnumerable<int> policyIds) =>
        Task.FromResult<IReadOnlyDictionary<int, PlanInfo>>(
            Plans.Where(p => policyIds.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value));
}

public class FakePremiumSummaries : IPremiumSummaries
{
    // Enrollments with no entry here have no installments at all.
    public Dictionary<int, PremiumSummary> Summaries { get; } = new();
    public bool Unavailable { get; set; }   // simulates the Billing service being down

    public Task<IReadOnlyDictionary<int, PremiumSummary>> GetSummariesAsync(IEnumerable<int> enrollmentIds) =>
        Unavailable ? throw new HttpRequestException("Connection refused") : Task.FromResult<IReadOnlyDictionary<int, PremiumSummary>>(enrollmentIds.Distinct().ToDictionary(
            id => id, id => Summaries.GetValueOrDefault(id) ?? new PremiumSummary(0, 0, 0, null, null)));
}

// Every customer has a verified email unless a test adds them to Unverified.
public class FakeUserDirectory : IUserDirectory
{
    public HashSet<int> Unverified { get; } = new();
    public bool Unavailable { get; set; }   // simulates the Identity service being down

    public Task<UserAccount?> GetAsync(int userId) =>
        Unavailable
            ? throw new HttpRequestException("Connection refused")
            : Task.FromResult<UserAccount?>(new UserAccount(userId, $"Customer {userId}", $"c{userId}@test.example",
                UserRole.Customer, IsActive: true, EmailVerified: !Unverified.Contains(userId)));

    public Task<IReadOnlyDictionary<int, UserAccount>> GetManyAsync(IEnumerable<int> userIds) =>
        throw new NotSupportedException();
}
