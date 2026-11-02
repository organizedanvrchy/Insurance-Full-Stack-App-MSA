using Insurance.Claims.DTOs;
using Insurance.Claims.Contracts;

namespace Insurance.Claims.Services;

public interface IClaimService
{
    Task<ClaimDto> SubmitAsync(int userId, ClaimSubmitRequest request);
    Task<List<ClaimDto>> GetMineAsync(int userId);
    Task<List<ClaimDto>> GetAllAsync(ClaimStatus? status);
    Task<ClaimDto?> GetByIdAsync(int claimId, int userId, bool isAdmin);   // null => 404
    Task<ClaimDto> ReviewAsync(int claimId, int adminId, ClaimReviewRequest request);
    Task<ClaimDto> SendToReviewerAsync(int claimId, int userId);
}