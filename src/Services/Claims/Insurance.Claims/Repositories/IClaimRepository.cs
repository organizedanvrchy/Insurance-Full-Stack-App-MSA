using Insurance.Claims.Contracts;
using Insurance.Claims.Models;

namespace Insurance.Claims.Repositories;

public interface IClaimRepository
{
    // Saves the claim and its ClaimSubmitted event (outbox) in one transaction.
    Task<int> SubmitAsync(int userId, int enrollmentId, DateOnly incidentDate, string description, decimal amount, decimal coverageAmount,
        int? expectedDocuments = null);
    Task<List<InsuranceClaim>> GetByUserAsync(int userId);
    Task<List<InsuranceClaim>> GetAllAsync(ClaimStatus? status);
    Task<InsuranceClaim?> GetByIdAsync(int claimId);

    // coverageAmount is required when approving. Saves the ClaimStatusChanged event (outbox) in the same transaction.
    Task<InsuranceClaim> ReviewAsync(int claimId, ClaimStatus from, ClaimStatus to, string? adminNotes, int reviewerId, decimal? coverageAmount);
    Task<InsuranceClaim> SendToReviewerAsync(int claimId, int userId);
    Task<Dictionary<int, ClaimCounts>> GetCountsByUserAsync(IEnumerable<int> userIds);
}
