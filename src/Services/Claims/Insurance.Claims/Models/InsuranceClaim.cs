using Insurance.Claims.Contracts;

namespace Insurance.Claims.Models;

// Named InsuranceClaim (not Claim) so it can't clash with System.Security.Claims.Claim used by the JWT code.
// Only what this service stores: names and plan details are looked up in the Identity and Catalog services.
public class InsuranceClaim
{
    public int ClaimId { get; set; }
    public string ClaimNumber { get; set; } = string.Empty;
    public int EnrollmentId { get; set; }
    public int UserId { get; set; }                       // the customer, for ownership checks
    public DateOnly IncidentDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal ClaimAmount { get; set; }
    public ClaimStatus Status { get; set; }
    public string? AdminNotes { get; set; }
    public DateTime SubmittedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public int? ReviewedByUserId { get; set; }
}
