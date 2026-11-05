using Insurance.Catalog.Contracts;
using Insurance.Claims.Contracts;

namespace Insurance.Claims.DTOs;

public class ClaimDto
{
    public int ClaimId { get; set; }
    public string ClaimNumber { get; set; } = string.Empty;
    public int EnrollmentId { get; set; }
    public string PolicyName { get; set; } = string.Empty;
    public PolicyType PolicyType { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public DateOnly IncidentDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal ClaimAmount { get; set; }
    public ClaimStatus Status { get; set; }
    public string? AdminNotes { get; set; }
    public DateTime SubmittedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }

    // Only filled for admins. Tells the UI which buttons to show, so Angular never duplicates the workflow rules.
    public List<ClaimStatus> AllowedNextStatuses { get; set; } = new();

    // Only true for the customer. These tell the UI what to offer, for the same reason.
    public bool CanAddDocuments { get; set; }
    public bool CanSendToReviewer { get; set; }
}