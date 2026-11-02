namespace Insurance.Claims.Contracts;

// Events published by the Claims service, through its transactional outbox (so they go out exactly when the
// change commits). Today the Notifications service emails the customer about each one; it looks up the plan
// through the enrollment.

/// <summary>A customer filed a claim.</summary>
public record ClaimSubmitted(
    int ClaimId,
    string ClaimNumber,
    int UserId,
    int EnrollmentId,
    DateOnly IncidentDate,
    decimal ClaimAmount,
    DateTime SubmittedAtUtc);

/// <summary>An admin moved a claim along the review workflow. Notes are the reviewer's notes, if any.</summary>
public record ClaimStatusChanged(
    int ClaimId,
    string ClaimNumber,
    int UserId,
    int EnrollmentId,
    decimal ClaimAmount,
    ClaimStatus PreviousStatus,
    ClaimStatus NewStatus,
    string? Notes,
    DateTime ChangedAtUtc);
