using Insurance.Claims.Contracts;

namespace Insurance.Claims.Services;

public static class ClaimStatusTransitions
{
    private static readonly Dictionary<ClaimStatus, ClaimStatus[]> Allowed = new()
    {
        [ClaimStatus.Submitted]   = new[] { ClaimStatus.UnderReview },
        [ClaimStatus.UnderReview] = new[] { ClaimStatus.Approved, ClaimStatus.Rejected },
        [ClaimStatus.Approved]    = new[] { ClaimStatus.Paid },
        [ClaimStatus.Rejected]    = Array.Empty<ClaimStatus>(),    // terminal
        [ClaimStatus.Paid]        = Array.Empty<ClaimStatus>()     // terminal
    };

    public static IReadOnlyList<ClaimStatus> NextFrom(ClaimStatus current) => Allowed[current];

    public static bool CanMove(ClaimStatus from, ClaimStatus to) => Allowed[from].Contains(to);
}