using Insurance.Claims.Contracts;

namespace Insurance.Claims.Services;

public static class ClaimStatusTransitions
{
    private static readonly Dictionary<ClaimStatus, ClaimStatus[]> Allowed = new()
    {
        [ClaimStatus.Submitted]   = new[] { ClaimStatus.UnderReview },
        [ClaimStatus.UnderReview] = new[] { ClaimStatus.NeedsMoreInfo, ClaimStatus.Approved, ClaimStatus.Rejected },
        // The customer normally sends the claim back (ClaimService.SendToReviewerAsync). An admin can resume the
        // review without them, or reject the claim if they never respond.
        [ClaimStatus.NeedsMoreInfo] = new[] { ClaimStatus.UnderReview, ClaimStatus.Rejected },
        [ClaimStatus.Approved]    = new[] { ClaimStatus.Paid },
        [ClaimStatus.Rejected]    = Array.Empty<ClaimStatus>(),    // terminal
        [ClaimStatus.Paid]        = Array.Empty<ClaimStatus>()     // terminal
    };

    public static IReadOnlyList<ClaimStatus> NextFrom(ClaimStatus current) => Allowed[current];

    public static bool CanMove(ClaimStatus from, ClaimStatus to) => Allowed[from].Contains(to);

    // Customers can add evidence until a decision has been made.
    public static bool AcceptsDocuments(ClaimStatus status) =>
        status is ClaimStatus.Submitted or ClaimStatus.UnderReview or ClaimStatus.NeedsMoreInfo;
}