using Insurance.BuildingBlocks.Time;
using Insurance.Billing.Contracts;
using Insurance.BuildingBlocks.Exceptions;
using Insurance.Catalog.Contracts;
using Insurance.Claims.DTOs;
using Insurance.Claims.Contracts;
using Insurance.Claims.Exceptions;
using Insurance.Claims.Models;
using Insurance.Claims.Repositories;
using Insurance.Enrollments.Contracts;
using Insurance.Identity.Contracts;

namespace Insurance.Claims.Services;

public class ClaimService : IClaimService
{
    private readonly IClaimRepository _claims;
    private readonly IEnrollmentLookup _enrollments;
    private readonly IPremiumSummaries _premiums;
    private readonly IPlanCatalog _plans;
    private readonly IUserDirectory _users;

    public ClaimService(IClaimRepository claims, IEnrollmentLookup enrollments, IPremiumSummaries premiums,
        IPlanCatalog plans, IUserDirectory users)
    {
        _claims = claims;
        _enrollments = enrollments;
        _premiums = premiums;
        _plans = plans;
        _users = users;
    }

    public async Task<ClaimDto> SubmitAsync(int userId, ClaimSubmitRequest request)
    {
        var incidentDate = request.IncidentDate!.Value;
        var today = BusinessTime.Today;

        // 403 until the customer has clicked the link in their verification email.
        await _users.EnsureEmailVerifiedAsync(userId, "file a claim");

        // Same answer for "doesn't exist" and "not yours", so ids can't be probed.
        var enrollment = await _enrollments.GetAsync(request.EnrollmentId);
        if (enrollment is null || enrollment.UserId != userId)
            throw new NotFoundException("Enrollment not found.");

        // Derived status: an Active policy past its end date is Expired.
        if (enrollment.Status != EnrollmentStatus.Active)
            throw new ClaimConflictException("Claims can only be filed on an active policy.");

        if (incidentDate < enrollment.StartDate || incidentDate > enrollment.EndDate || incidentDate > today)
            throw new BusinessRuleException("The incident date must fall within your coverage period and cannot be in the future.");

        // The Billing service decides what "overdue" means, including the grace period.
        var premiums = (await _premiums.GetSummariesAsync(new[] { enrollment.EnrollmentId }))[enrollment.EnrollmentId];
        if (premiums.OverdueCount > 0)
            throw new ClaimConflictException("Please pay your overdue premiums before filing a claim.");

        // The remaining-coverage check needs a lock, so it runs inside usp_Claim_Submit.
        var coverage = await CoverageOfAsync(enrollment);
        var id = await _claims.SubmitAsync(
            userId, enrollment.EnrollmentId, incidentDate, request.Description.Trim(), request.ClaimAmount, coverage,
            request.DocumentCount);

        var created = await _claims.GetByIdAsync(id);
        return (await ComposeAsync(new[] { created! }, isAdmin: false)).Single();
    }

    public async Task<List<ClaimDto>> GetMineAsync(int userId)
    {
        var list = await _claims.GetByUserAsync(userId);
        return await ComposeAsync(list, isAdmin: false);
    }

    public async Task<List<ClaimDto>> GetAllAsync(ClaimStatus? status)
    {
        var list = await _claims.GetAllAsync(status);
        return await ComposeAsync(list, isAdmin: true);
    }

    public async Task<ClaimDto?> GetByIdAsync(int claimId, int userId, bool isAdmin)
    {
        var claim = await _claims.GetByIdAsync(claimId);

        // Same null for "doesn't exist" and "belongs to someone else".
        if (claim is null || (claim.UserId != userId && !isAdmin))
            return null;

        return (await ComposeAsync(new[] { claim }, isAdmin)).Single();
    }

    public async Task<ClaimDto> ReviewAsync(int claimId, int adminId, ClaimReviewRequest request)
    {
        var claim = await _claims.GetByIdAsync(claimId)
            ?? throw new NotFoundException("Claim not found.");

        var target = request.Status!.Value;
        var notes = request.AdminNotes?.Trim();

        if (!ClaimStatusTransitions.CanMove(claim.Status, target))
            throw new ClaimConflictException($"A claim that is {claim.Status} cannot be moved to {target}.");

        if (target == ClaimStatus.Rejected && string.IsNullOrWhiteSpace(notes))
            throw new BusinessRuleException("Please give a reason when rejecting a claim.");

        if (target == ClaimStatus.NeedsMoreInfo && string.IsNullOrWhiteSpace(notes))
            throw new BusinessRuleException("Please tell the customer what information you need.");

        // Approving is limited by the plan's coverage; the proc checks it against the other approved claims.
        decimal? coverage = null;
        if (target == ClaimStatus.Approved)
        {
            var enrollment = await _enrollments.GetAsync(claim.EnrollmentId)
                ?? throw new InvalidOperationException($"Claim {claimId} refers to missing enrollment {claim.EnrollmentId}.");
            coverage = await CoverageOfAsync(enrollment);
        }

        // We pass the status we just read as the "from" status. If someone else changes the claim
        // between our read and this call, the proc finds no matching row and we get a 409.
        var updated = await _claims.ReviewAsync(claimId, claim.Status, target, notes, adminId, coverage);
        return (await ComposeAsync(new[] { updated }, isAdmin: true)).Single();
    }

    // Customer: they've responded to a request for more information, so the claim goes back to the reviewer.
    public async Task<ClaimDto> SendToReviewerAsync(int claimId, int userId)
    {
        var claim = await _claims.GetByIdAsync(claimId);

        // Same answer for "doesn't exist" and "not yours".
        if (claim is null || claim.UserId != userId)
            throw new NotFoundException("Claim not found.");

        if (claim.Status != ClaimStatus.NeedsMoreInfo)
            throw new ClaimConflictException("This claim isn't waiting for more information.");

        var updated = await _claims.SendToReviewerAsync(claimId, userId);
        return (await ComposeAsync(new[] { updated }, isAdmin: false)).Single();
    }

    // Plans are only ever deactivated, never deleted, so an enrollment's plan is always there.
    private async Task<decimal> CoverageOfAsync(EnrollmentInfo enrollment)
    {
        var plan = await _plans.GetAsync(enrollment.PolicyId)
            ?? throw new InvalidOperationException($"Enrollment {enrollment.EnrollmentId} refers to missing plan {enrollment.PolicyId}.");
        return plan.CoverageAmount;
    }

    // Adds the plan (via the Enrollments and Catalog services) and the customer and reviewer names (Identity),
    // with one HTTP call to each service for the whole list.
    private async Task<List<ClaimDto>> ComposeAsync(IReadOnlyCollection<InsuranceClaim> claims, bool isAdmin)
    {
        if (claims.Count == 0) return new List<ClaimDto>();

        var enrollments = await _enrollments.GetManyAsync(claims.Select(c => c.EnrollmentId));
        var plans = await _plans.GetManyAsync(enrollments.Values.Select(e => e.PolicyId));
        var userIds = claims.Select(c => c.UserId)
            .Concat(claims.Where(c => c.ReviewedByUserId is not null).Select(c => c.ReviewedByUserId!.Value));
        var users = await _users.GetManyAsync(userIds);

        return claims.Select(c =>
        {
            var plan = plans[enrollments[c.EnrollmentId].PolicyId];
            var customer = users.GetValueOrDefault(c.UserId);
            var reviewer = c.ReviewedByUserId is { } reviewerId ? users.GetValueOrDefault(reviewerId) : null;
            return MapToDto(c, plan, customer, reviewer, isAdmin);
        }).ToList();
    }

    private static ClaimDto MapToDto(InsuranceClaim c, PlanInfo plan, UserAccount? customer, UserAccount? reviewer, bool isAdmin) => new()
    {
        ClaimId = c.ClaimId,
        ClaimNumber = c.ClaimNumber,
        EnrollmentId = c.EnrollmentId,
        PolicyName = plan.PolicyName,
        PolicyType = plan.PolicyType,
        CustomerName = customer?.FullName ?? string.Empty,
        CustomerEmail = customer?.Email ?? string.Empty,
        IncidentDate = c.IncidentDate,
        Description = c.Description,
        ClaimAmount = c.ClaimAmount,
        Status = c.Status,
        AdminNotes = c.AdminNotes,
        SubmittedAt = c.SubmittedAt,
        ReviewedAt = c.ReviewedAt,
        ReviewedBy = reviewer?.FullName,
        AllowedNextStatuses = isAdmin ? ClaimStatusTransitions.NextFrom(c.Status).ToList() : new List<ClaimStatus>(),
        CanAddDocuments = !isAdmin && ClaimStatusTransitions.AcceptsDocuments(c.Status),
        CanSendToReviewer = !isAdmin && c.Status == ClaimStatus.NeedsMoreInfo
    };
}