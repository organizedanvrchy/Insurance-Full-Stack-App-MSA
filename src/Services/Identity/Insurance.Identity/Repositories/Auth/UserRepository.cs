using System.Data;
using Insurance.BuildingBlocks.Data;
using Insurance.BuildingBlocks.Security;
using Insurance.Identity.Contracts;
using Insurance.Identity.Exceptions;
using Insurance.Identity.Models;
using Microsoft.Data.SqlClient;

namespace Insurance.Identity.Repositories;

public class UserRepository : IUserRepository
{
    private readonly IDbConnectionFactory _db;
    public UserRepository(IDbConnectionFactory db) => _db = db;

    public async Task<int> CreateAsync(User user)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("accounts.usp_User_Register");
        cmd.AddParam("@FullName",     SqlDbType.NVarChar, user.FullName, 100);
        cmd.AddParam("@Email",        SqlDbType.NVarChar, user.Email, 256);
        cmd.AddParam("@PasswordHash", SqlDbType.NVarChar, user.PasswordHash, 100);
        cmd.AddParam("@Phone",        SqlDbType.NVarChar, user.Phone, 20);
        cmd.AddParam("@Address",      SqlDbType.NVarChar, user.Address, 250);

        await conn.OpenAsync();
        try
        {
            // The proc ends with SELECT SCOPE_IDENTITY(), so ExecuteScalar gives us the new id.
            return (int)(await cmd.ExecuteScalarAsync())!;
        }
        // 50001 = our THROW in the proc. 2627/2601 = unique index violation (two sign-ups racing).
        catch (SqlException ex) when (ex.Number is 50001 or 2627 or 2601)
        {
            throw new DuplicateEmailException(user.Email);
        }
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("accounts.usp_User_GetByEmail");
        cmd.AddParam("@Email", SqlDbType.NVarChar, email, 256);

        await conn.OpenAsync();
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    public async Task<User?> GetByIdAsync(int userId)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("accounts.usp_User_GetById");
        cmd.AddParam("@UserId", SqlDbType.Int, userId);

        await conn.OpenAsync();
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    public async Task<List<UserAccount>> GetManyAsync(IEnumerable<int> userIds)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("accounts.usp_User_GetMany");
        cmd.AddIdListParam("@UserIds", userIds);

        await conn.OpenAsync();
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<UserAccount>();
        while (await reader.ReadAsync()) list.Add(MapAccount(reader));
        return list;
    }

    public async Task<string> ChangeEmailAsync(int userId, string newEmail)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("accounts.usp_User_ChangeEmail");
        cmd.AddParam("@UserId",   SqlDbType.Int, userId);
        cmd.AddParam("@NewEmail", SqlDbType.NVarChar, newEmail, 256);

        await conn.OpenAsync();
        try
        {
            return (string)(await cmd.ExecuteScalarAsync())!;
        }
        // 50001 = the THROW in the proc. 2627/2601 = unique index violation (two accounts racing for the address).
        catch (SqlException ex) when (ex.Number is 50001 or 2627 or 2601)
        {
            throw new DuplicateEmailException(newEmail);
        }
    }

    public async Task ChangePasswordAsync(int userId, string passwordHash)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("accounts.usp_User_ChangePassword");
        cmd.AddParam("@UserId",       SqlDbType.Int, userId);
        cmd.AddParam("@PasswordHash", SqlDbType.NVarChar, passwordHash, 100);

        await conn.OpenAsync();
        await cmd.ExecuteNonQueryAsync();
    }

    internal static User Map(SqlDataReader r) => new()
    {
        UserId       = r.GetInt32(r.GetOrdinal("UserId")),
        FullName     = r.GetString(r.GetOrdinal("FullName")),
        Email        = r.GetString(r.GetOrdinal("Email")),
        PasswordHash = r.GetString(r.GetOrdinal("PasswordHash")),
        Phone        = r.GetStringOrNull("Phone"),
        Address      = r.GetStringOrNull("Address"),
        Role         = r.GetEnum<UserRole>("Role"),
        IsActive     = r.GetBoolean(r.GetOrdinal("IsActive")),
        CreatedAt    = r.GetDateTime(r.GetOrdinal("CreatedAt")),
        EmailVerifiedAt = r.GetUtcDateTimeOrNull("EmailVerifiedAt")
    };

    // No password hash: this shape is for other services.
    private static UserAccount MapAccount(SqlDataReader r) => new(
        r.GetInt32(r.GetOrdinal("UserId")),
        r.GetString(r.GetOrdinal("FullName")),
        r.GetString(r.GetOrdinal("Email")),
        r.GetEnum<UserRole>("Role"),
        r.GetBoolean(r.GetOrdinal("IsActive")),
        r.GetBoolean(r.GetOrdinal("EmailVerified")));
}
