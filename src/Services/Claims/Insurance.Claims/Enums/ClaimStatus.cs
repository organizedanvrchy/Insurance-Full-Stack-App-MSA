namespace Insurance.Claims.Enums;

public enum ClaimStatus
{
    Submitted,
    UnderReview,
    NeedsMoreInfo,      // waiting on the customer: the reviewer has asked for more
    Approved,
    Rejected,
    Paid
}
