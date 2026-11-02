using Insurance.Catalog.Contracts;
using Insurance.Enrollments.Contracts;
using Insurance.Notifications.Templates;

namespace Insurance.Notifications.Services;

/// <summary>Plan names and policy dates for the emails, from the Catalog and Enrollments services (HTTP).</summary>
public interface IPolicyDetails
{
    Task<string> PlanNameAsync(int policyId);
    Task<string> PlanNameForEnrollmentAsync(int enrollmentId);
    Task<PolicySummary> SummaryAsync(int enrollmentId);
}

public class PolicyDetails : IPolicyDetails
{
    private const string Fallback = "your policy";

    private readonly IPlanCatalog _plans;
    private readonly IEnrollmentLookup _enrollments;

    public PolicyDetails(IPlanCatalog plans, IEnrollmentLookup enrollments)
    {
        _plans = plans;
        _enrollments = enrollments;
    }

    public async Task<string> PlanNameAsync(int policyId) =>
        (await _plans.GetAsync(policyId))?.PolicyName ?? Fallback;

    public async Task<string> PlanNameForEnrollmentAsync(int enrollmentId)
    {
        var enrollment = await _enrollments.GetAsync(enrollmentId);
        return enrollment is null ? Fallback : await PlanNameAsync(enrollment.PolicyId);
    }

    public async Task<PolicySummary> SummaryAsync(int enrollmentId)
    {
        var enrollment = await _enrollments.GetAsync(enrollmentId)
            ?? throw new InvalidOperationException($"Enrollment {enrollmentId} was not found.");
        var plan = await _plans.GetAsync(enrollment.PolicyId);
        return new PolicySummary(plan?.PolicyName ?? Fallback, plan?.CoverageAmount ?? 0m, enrollment.StartDate, enrollment.EndDate);
    }
}
