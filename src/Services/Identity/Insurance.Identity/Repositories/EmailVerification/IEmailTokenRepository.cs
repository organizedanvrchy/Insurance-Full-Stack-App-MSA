using Insurance.Identity.Contracts;
using Insurance.Identity.Models;

namespace Insurance.Identity.Repositories;

/// <summary>The links emailed for verification. Only token HASHES go in and out of the database.</summary>
public interface IEmailTokenRepository
{
    // Older unused links of the same purpose stop working.
    Task CreateAsync(int userId, string email, EmailVerificationPurpose purpose, string tokenHash, DateTime expiresAtUtc);

    Task<RecentEmailTokens> GetRecentAsync(int userId);

    // Throws InvalidVerificationLinkException, or DuplicateEmailException if the new address was taken meanwhile.
    Task<ConsumedEmailToken> ConsumeAsync(string tokenHash);
}

public record RecentEmailTokens(DateTime? LastCreatedAtUtc, int CountLastDay);

/// <summary>What using a link did. User is the account as it is now; PreviousEmail is the address before.</summary>
public record ConsumedEmailToken(EmailVerificationPurpose Purpose, string PreviousEmail, User User);
