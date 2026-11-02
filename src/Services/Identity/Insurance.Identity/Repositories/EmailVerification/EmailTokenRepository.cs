using System.Data;
using Insurance.BuildingBlocks.Data;
using Insurance.Identity.Contracts;
using Insurance.Identity.Exceptions;
using Microsoft.Data.SqlClient;

namespace Insurance.Identity.Repositories;

public class EmailTokenRepository : IEmailTokenRepository
{
    private readonly IDbConnectionFactory _db;
    public EmailTokenRepository(IDbConnectionFactory db) => _db = db;

    public async Task CreateAsync(int userId, string email, EmailVerificationPurpose purpose, string tokenHash, DateTime expiresAtUtc)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("accounts.usp_EmailToken_Create");
        cmd.AddParam("@UserId",    SqlDbType.Int, userId);
        cmd.AddParam("@Email",     SqlDbType.NVarChar, email, 256);
        cmd.AddParam("@Purpose",   SqlDbType.NVarChar, purpose.ToString(), 20);
        cmd.AddParam("@TokenHash", SqlDbType.Char, tokenHash, 64);
        cmd.AddParam("@ExpiresAt", SqlDbType.DateTime2, expiresAtUtc);

        await conn.OpenAsync();
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<RecentEmailTokens> GetRecentAsync(int userId)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("accounts.usp_EmailToken_GetRecent");
        cmd.AddParam("@UserId", SqlDbType.Int, userId);

        await conn.OpenAsync();
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();      // an aggregate always returns one row
        return new RecentEmailTokens(
            reader.GetUtcDateTimeOrNull("LastCreatedAt"),
            reader.GetInt32(reader.GetOrdinal("CountLastDay")));
    }

    public async Task<ConsumedEmailToken> ConsumeAsync(string tokenHash)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("accounts.usp_EmailToken_Consume");
        cmd.AddParam("@TokenHash", SqlDbType.Char, tokenHash, 64);

        await conn.OpenAsync();
        try
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                throw new InvalidOperationException("usp_EmailToken_Consume returned no row.");

            return new ConsumedEmailToken(
                reader.GetEnum<EmailVerificationPurpose>("Purpose"),
                reader.GetString(reader.GetOrdinal("PreviousEmail")),
                UserRepository.Map(reader));
        }
        catch (SqlException ex) when (ex.Number == 50020)
        {
            throw new InvalidVerificationLinkException();
        }
        // The new address was registered by someone else after the change was requested.
        catch (SqlException ex) when (ex.Number is 50001 or 2627 or 2601)
        {
            throw new DuplicateEmailException("");
        }
    }
}
