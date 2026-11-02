using Insurance.Billing.Contracts;
using Insurance.BuildingBlocks.Exceptions;
using Insurance.Catalog.Contracts;
using Insurance.Enrollments.Contracts;
using Insurance.Enrollments.DTOs;
using Insurance.Enrollments.Enums;
using Insurance.Enrollments.Models;
using Insurance.Enrollments.Repositories;
using Insurance.Identity.Contracts;
using Insurance.Quotes.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Insurance.Enrollments.Services;

public class EnrollmentService : IEnrollmentService
{
    // An unpaid installment due within this many days shows a "payment due" alert.
    private const int DueSoonDays = 7;

    private readonly IEnrollmentRepository _enrollments;
    private readonly IQuoteReservations _quotes;
    private readonly IPlanCatalog _plans;
    private readonly IPremiumSummaries _premiums;
    private readonly IUserDirectory _users;
    private readonly EnrollmentOptions _options;
    private readonly ILogger<EnrollmentService> _logger;

    public EnrollmentService(IEnrollmentRepository enrollments, IQuoteReservations quotes, IPlanCatalog plans,
        IPremiumSummaries premiums, IUserDirectory users, IOptions<EnrollmentOptions> options, ILogger<EnrollmentService> logger)
    {
        _logger = logger;
        _users = users;
        _enrollments = enrollments;
        _quotes = quotes;
        _plans = plans;
        _premiums = premiums;
        _options = options.Value;
    }

    // The enrollment saga, orchestrated by this service:
    //   0. Identity service (HTTP): the customer's email address must be verified.
    //   1. Quotes service (HTTP): reserve the quote: ownership, expiry, one-time use.
    //   2. Catalog service (HTTP): the plan must still be active.
    //   3. Here: save the enrollment as Pending, with EnrollmentRequested in the outbox (same transaction).
    //   4. Billing service (event): creates the premium schedule -> PremiumScheduleCreated -> Pending becomes Active.
    // Steps 1-3 answer the customer straight away (400/404/409). If step 2 or 3 refuses, the quote is released.
    // For step 4 the request waits briefly for the saga, so the customer normally gets the finished policy (201).
    public async Task<EnrollmentDto> EnrollAsync(int userId, EnrollmentRequest request)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var start = request.StartDate ?? today;

        // One day of slack: in the evening a customer's local "today" can already be "yesterday" in UTC.
        if (start < today.AddDays(-1))
            throw new BusinessRuleException("Start date cannot be in the past.");
        if (start > today.AddDays(90))
            throw new BusinessRuleException("Start date must be within 90 days from today.");

        // 403 until the customer has clicked the link in their verification email (Identity service, HTTP).
        await _users.EnsureEmailVerifiedAsync(userId, "enroll in a policy");

        // The premium and the plan come from the quote, so the caller can't choose either.
        var quote = await _quotes.ReserveAsync(request.QuoteId, userId);

        int id;
        try
        {
            var plan = await _plans.GetAsync(quote.PolicyId);
            if (plan is null || !plan.IsActive)
                throw new BusinessRuleException("That policy does not exist or is no longer available.");

            // "One active enrollment per plan" is enforced inside usp_Enrollment_CreatePending, with a lock.
            id = await _enrollments.CreatePendingAsync(userId, plan.PolicyId, quote.QuoteId, start, plan.TermMonths, quote.MonthlyPremium);
        }
        catch
        {
            await _quotes.ReleaseAsync(quote.QuoteId);
            throw;
        }

        var enrollment = await WaitForSagaAsync(id);
        if (enrollment is null)
            throw new InvalidOperationException($"Enrollment {id} could not be completed: the premium schedule was not created.");

        return (await ComposeAsync(new[] { enrollment })).Single();
    }

    // Polls this service's own database until the saga leaves Pending. Failed enrollments read as null.
    // If it takes longer than the timeout, the still-Pending enrollment is returned (the controller answers 202).
    private async Task<Enrollment?> WaitForSagaAsync(int id)
    {
        var deadline = DateTime.UtcNow + _options.SagaTimeout;
        while (true)
        {
            var enrollment = await _enrollments.GetByIdAsync(id);
            if (enrollment is null || enrollment.Status != EnrollmentStatus.Pending || DateTime.UtcNow >= deadline)
                return enrollment;
            await Task.Delay(_options.SagaPollInterval);
        }
    }

    public async Task<List<EnrollmentDto>> GetMineAsync(int userId)
    {
        var list = await _enrollments.GetByUserAsync(userId);
        return await ComposeAsync(list);
    }

    public async Task<EnrollmentDto?> GetByIdAsync(int enrollmentId, int userId, bool isAdmin)
    {
        var enrollment = await _enrollments.GetByIdAsync(enrollmentId);

        // Same null for "doesn't exist" and "belongs to someone else", so ids can't be probed.
        if (enrollment is null || (enrollment.UserId != userId && !isAdmin))
            return null;

        return (await ComposeAsync(new[] { enrollment })).Single();
    }

    // The SQL checks ownership (WHERE UserId = @UserId), so a stranger's id simply matches nothing.
    // Billing removes the unpaid installments when it receives EnrollmentCancelled; paid ones stay as history.
    public Task<bool> CancelAsync(int enrollmentId, int userId) =>
        _enrollments.CancelAsync(enrollmentId, userId);

    // Adds the plan details (Catalog service) and payment progress (Billing service), one HTTP call each for the whole list.
    private async Task<List<EnrollmentDto>> ComposeAsync(IReadOnlyCollection<Enrollment> enrollments)
    {
        if (enrollments.Count == 0) return new List<EnrollmentDto>();

        var plans = await _plans.GetManyAsync(enrollments.Select(e => e.PolicyId));
        var premiums = await TryGetPremiumsAsync(enrollments.Select(e => e.EnrollmentId));

        return enrollments.Select(e => MapToDto(e, plans[e.PolicyId], premiums.GetValueOrDefault(e.EnrollmentId) ?? NoPremiumInfo)).ToList();
    }

    private static readonly PremiumSummary NoPremiumInfo = new(0, 0, 0, null, null);

    // Graceful degradation: if Billing is down, policies still show, just without payment progress or alerts.
    // (Plan details are not optional, and the claim and payment rules never degrade: they fail instead.)
    private async Task<IReadOnlyDictionary<int, PremiumSummary>> TryGetPremiumsAsync(IEnumerable<int> enrollmentIds)
    {
        try
        {
            return await _premiums.GetSummariesAsync(enrollmentIds);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Billing service unavailable; showing enrollments without payment progress.");
            return new Dictionary<int, PremiumSummary>();
        }
    }

    // Overdue comes from Billing (the same rule that blocks claims).
    // "Due soon" is decided here, with the threshold in one place.
    private static PaymentAlert GetAlert(Enrollment e, PremiumSummary p)
    {
        if (e.Status != EnrollmentStatus.Active) return PaymentAlert.None;
        if (p.OverdueCount > 0) return PaymentAlert.Overdue;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (p.NextDueDate is { } due && due <= today.AddDays(DueSoonDays))
            return PaymentAlert.DueSoon;

        return PaymentAlert.None;
    }

    private static EnrollmentDto MapToDto(Enrollment e, PlanInfo plan, PremiumSummary p) => new()
    {
        EnrollmentId = e.EnrollmentId,
        PolicyId = e.PolicyId,
        PolicyName = plan.PolicyName,
        PolicyType = plan.PolicyType,
        CoverageAmount = plan.CoverageAmount,
        StartDate = e.StartDate,
        EndDate = e.EndDate,
        MonthlyPremium = e.MonthlyPremium,
        Status = e.Status,
        PaymentsPaid = p.PaymentsPaid,
        PaymentsTotal = p.PaymentsTotal,
        PaymentAlert = GetAlert(e, p),
        OverdueCount = p.OverdueCount,
        NextDueDate = p.NextDueDate,
        NextDueAmount = p.NextDueAmount
    };
}