using Insurance.Claims.Models;

namespace Insurance.Claims.Repositories;

public interface IClaimDocumentRepository
{
    // Throws ClaimConflictException if the claim already has maxDocuments.
    Task<ClaimDocument> AddAsync(ClaimDocument document, int maxDocuments);
    Task<List<ClaimDocument>> GetByClaimAsync(int claimId);
    Task<ClaimDocument?> GetByIdAsync(int claimId, int claimDocumentId);
}
