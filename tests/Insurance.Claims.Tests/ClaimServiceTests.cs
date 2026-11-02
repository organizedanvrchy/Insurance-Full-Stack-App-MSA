using Insurance.BuildingBlocks.Exceptions;
using Insurance.BuildingBlocks.Security;
using Insurance.Claims.DTOs;
using Insurance.Claims.Contracts;
using Insurance.Claims.Exceptions;
using Insurance.Claims.Models;
using Insurance.Claims.Services;
using Insurance.Enrollments.Contracts;
using Insurance.Identity.Contracts;
using Xunit;

namespace Insurance.Claims.Tests;

public class ClaimServiceTests
{
    private static DateOnly Today => Insurance.BuildingBlocks.Time.BusinessTime.Today;

    private readonly FakeClaimRepository _repo = new();
    private readonly FakeEnrollmentLookup _enrollments = new();
    private readonly FakePremiumSummaries _premiums = new();
    private readonly FakeUserDirectory _users = new();
    private readonly ClaimService _sut;

    public ClaimServiceTests()
    {
        _sut = new ClaimService(_repo, _enrollments, _premiums, new FakePlanCatalog(), _users);

        // Customer 1 holds enrollment 1, customer 3 holds enrollment 5; both are active on plan 1 for a year.
        AddEnrollment(1, ownerId: 1);
        AddEnrollment(5, ownerId: 3);
        _users.Users[1] = new UserAccount(1, "Cal Tester", "cal@test.example", UserRole.Customer, true, EmailVerified: true);
        _users.Users[3] = new UserAccount(3, "Dee Tester", "dee@test.example", UserRole.Customer, true, EmailVerified: true);
        _users.Users[7] = new UserAccount(7, "Ada Admin", "ada@test.example", UserRole.Admin, true, EmailVerified: true);
    }

    private void AddEnrollment(int id, int ownerId, EnrollmentStatus status = EnrollmentStatus.Active) =>
        _enrollments.Enrollments[id] = new EnrollmentInfo(id, ownerId, PolicyId: 1,
            StartDate: Today.AddMonths(-6), EndDate: Today.AddMonths(6), status);

    private static ClaimSubmitRequest Submit(int enrollmentId = 5, DateOnly? incident = null, decimal amount = 500m) => new()
    {
        EnrollmentId = enrollmentId,
        IncidentDate = incident ?? Today.AddDays(-3),
        Description = "Water damage in the kitchen",
        ClaimAmount = amount
    };

    private InsuranceClaim AddClaim(ClaimStatus status, int ownerId = 1)
    {
        var claim = new InsuranceClaim
        {
            ClaimId = _repo.Claims.Count + 1,
            ClaimNumber = "CLM-TEST-000001",
            UserId = ownerId,
            EnrollmentId = 1,
            Status = status,
            ClaimAmount = 500m,
            Description = "Test claim"
        };
        _repo.Claims.Add(claim);
        return claim;
    }

    private static ClaimReviewRequest Review(ClaimStatus to, string? notes = null) =>
        new() { Status = to, AdminNotes = notes };

    // Submit

    [Fact]
    public async Task Submitting_trims_the_description_and_shows_no_admin_actions_to_the_customer()
    {
        var request = Submit();
        request.Description = "  Water damage in the kitchen  ";

        var dto = await _sut.SubmitAsync(userId: 3, request);

        Assert.Equal("Water damage in the kitchen", _repo.Claims.Single().Description);
        Assert.Equal(ClaimStatus.Submitted, dto.Status);
        Assert.Empty(dto.AllowedNextStatuses);
    }

    [Fact]
    public async Task Submitting_sends_the_plans_coverage_for_the_remaining_coverage_check()
    {
        await _sut.SubmitAsync(userId: 3, Submit());

        Assert.Equal(FakePlanCatalog.Plan.CoverageAmount, _repo.LastSubmitCoverage);
    }

    [Theory]
    [InlineData(999)]   // doesn't exist
    [InlineData(1)]     // belongs to customer 1
    public async Task Claiming_on_an_enrollment_that_is_not_yours_is_a_not_found(int enrollmentId)
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.SubmitAsync(userId: 3, Submit(enrollmentId)));
        Assert.Empty(_repo.Claims);
    }

    [Theory]
    [InlineData(EnrollmentStatus.Cancelled)]
    [InlineData(EnrollmentStatus.Expired)]
    public async Task Claims_need_an_active_policy(EnrollmentStatus status)
    {
        AddEnrollment(5, ownerId: 3, status);

        await Assert.ThrowsAsync<ClaimConflictException>(() => _sut.SubmitAsync(userId: 3, Submit()));
    }

    [Fact]
    public async Task An_incident_before_the_coverage_started_is_rejected()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(
            () => _sut.SubmitAsync(userId: 3, Submit(incident: Today.AddMonths(-7))));
    }

    [Fact]
    public async Task An_incident_in_the_future_is_rejected()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(
            () => _sut.SubmitAsync(userId: 3, Submit(incident: Today.AddDays(1))));
    }

    [Fact]
    public async Task Overdue_premiums_block_new_claims()
    {
        _premiums.Overdue.Add(5);

        await Assert.ThrowsAsync<ClaimConflictException>(() => _sut.SubmitAsync(userId: 3, Submit()));
        Assert.Empty(_repo.Claims);
    }

    // Review

    [Fact]
    public async Task Reviewing_a_claim_that_does_not_exist_is_a_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _sut.ReviewAsync(999, adminId: 1, Review(ClaimStatus.UnderReview)));
    }

    [Fact]
    public async Task Skipping_a_step_in_the_workflow_is_a_conflict()
    {
        AddClaim(ClaimStatus.Submitted);

        await Assert.ThrowsAsync<ClaimConflictException>(
            () => _sut.ReviewAsync(1, adminId: 1, Review(ClaimStatus.Approved)));

        Assert.Null(_repo.LastReview);    // the database was never called
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Rejecting_requires_a_reason(string? notes)
    {
        AddClaim(ClaimStatus.UnderReview);

        await Assert.ThrowsAsync<BusinessRuleException>(
            () => _sut.ReviewAsync(1, adminId: 1, Review(ClaimStatus.Rejected, notes)));
    }

    [Fact]
    public async Task Rejecting_with_a_reason_works_and_leaves_no_further_actions()
    {
        AddClaim(ClaimStatus.UnderReview);

        var dto = await _sut.ReviewAsync(1, adminId: 1, Review(ClaimStatus.Rejected, "Not covered"));

        Assert.Equal(ClaimStatus.Rejected, dto.Status);
        Assert.Empty(dto.AllowedNextStatuses);
    }

    [Fact]
    public async Task The_service_passes_the_status_it_just_read_as_the_from_status()
    {
        // This is the optimistic-concurrency guard: 
        // the stored procedure only updates the claim
        // if it is STILL in this status, so two admins 
        // can't overwrite each other.
        AddClaim(ClaimStatus.UnderReview);

        var dto = await _sut.ReviewAsync(1, adminId: 7, Review(ClaimStatus.Approved, "Looks good"));

        var call = _repo.LastReview!.Value;
        Assert.Equal(ClaimStatus.UnderReview, call.From);
        Assert.Equal(ClaimStatus.Approved, call.To);
        Assert.Equal(7, call.ReviewerId);
        Assert.Equal("Looks good", call.Notes);
        Assert.Equal(FakePlanCatalog.Plan.CoverageAmount, call.Coverage);    // approving is limited by coverage
        Assert.Equal(new[] { ClaimStatus.Paid }, dto.AllowedNextStatuses);   // the next legal step
    }

    [Fact]
    public async Task Moves_other_than_approving_do_not_need_the_coverage()
    {
        AddClaim(ClaimStatus.Submitted);

        await _sut.ReviewAsync(1, adminId: 7, Review(ClaimStatus.UnderReview));

        Assert.Null(_repo.LastReview!.Value.Coverage);
    }

    [Fact]
    public async Task Claims_show_the_plan_and_the_customer_and_reviewer_names_from_the_other_services()
    {
        AddClaim(ClaimStatus.Submitted, ownerId: 1);

        var dto = await _sut.ReviewAsync(1, adminId: 7, Review(ClaimStatus.UnderReview));

        Assert.Equal("Test Plan", dto.PolicyName);
        Assert.Equal("Cal Tester", dto.CustomerName);
        Assert.Equal("cal@test.example", dto.CustomerEmail);
        Assert.Equal("Ada Admin", dto.ReviewedBy);
    }

    // Who can see a claim

    [Fact]
    public async Task The_owner_sees_their_claim_without_admin_actions()
    {
        AddClaim(ClaimStatus.Submitted, ownerId: 1);

        var dto = await _sut.GetByIdAsync(1, userId: 1, isAdmin: false);

        Assert.NotNull(dto);
        Assert.Empty(dto!.AllowedNextStatuses);
    }

    [Fact]
    public async Task Another_customer_cannot_see_the_claim()
    {
        AddClaim(ClaimStatus.Submitted, ownerId: 1);

        Assert.Null(await _sut.GetByIdAsync(1, userId: 2, isAdmin: false));
    }

    [Fact]
    public async Task An_admin_sees_the_claim_with_the_available_next_steps()
    {
        AddClaim(ClaimStatus.Submitted, ownerId: 1);

        var dto = await _sut.GetByIdAsync(1, userId: 99, isAdmin: true);

        Assert.NotNull(dto);
        Assert.Equal(new[] { ClaimStatus.UnderReview }, dto!.AllowedNextStatuses);
    }

    [Fact]
    public async Task The_admin_list_can_be_filtered_by_status()
    {
        AddClaim(ClaimStatus.Submitted);
        AddClaim(ClaimStatus.Paid);

        var submitted = await _sut.GetAllAsync(ClaimStatus.Submitted);

        Assert.Single(submitted);
    }

    // ---------------- Email verification ----------------

    [Fact]
    public async Task A_customer_must_verify_their_email_before_filing_a_claim()
    {
        _users.Users[3] = _users.Users[3] with { EmailVerified = false };

        var ex = await Assert.ThrowsAsync<EmailNotVerifiedException>(() => _sut.SubmitAsync(3, Submit()));

        Assert.IsAssignableFrom<ForbiddenException>(ex);          // a 403
        Assert.Contains("file a claim", ex.Message);
        Assert.Empty(_repo.Claims);
    }
}
