namespace Insurance.Claims.Contracts;

public enum ClaimStatus
{
    Submitted,
    UnderReview,
    NeedsMoreInfo,      // waiting on the customer: the reviewer has asked for more
    Approved,
    Rejected,
    Paid
}
