using Insurance.Identity.Models;

namespace Insurance.Identity.Repositories;

/// <summary>The links emailed for verification. Only token HASHES go in and out of the database.</summary>
public interface IEmailTokenRepository
{
    // Older unused links stop working.
    Task CreateAsync(int userId, string email, string tokenHash, DateTime expiresAtUtc);

    Task<RecentEmailTokens> GetRecentAsync(int userId);

    // Marks the address verified and returns the account. Throws InvalidVerificationLinkException.
    Task<User> ConsumeAsync(string tokenHash);
}

/// <summary>LastPendingCreatedAtUtc: the newest link not used yet (null if none). CountLastDay: every link sent in 24 hours.</summary>
public record RecentEmailTokens(DateTime? LastPendingCreatedAtUtc, int CountLastDay);
