using System.Data;
using Insurance.BuildingBlocks.Data;
using Insurance.Identity.Contracts;
using Microsoft.Data.SqlClient;

namespace Insurance.Identity.Repositories;

public class CustomerRepository : ICustomerRepository
{
    private readonly IDbConnectionFactory _db;
    public CustomerRepository(IDbConnectionFactory db) => _db = db;

    public async Task<List<CustomerInfo>> GetAllAsync(string? search, bool? isActive)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("accounts.usp_Customer_GetAll");
        cmd.AddParam("@Search",   SqlDbType.NVarChar, search, 100);   // null => DBNull => "no filter"
        cmd.AddParam("@IsActive", SqlDbType.Bit, isActive);

        await conn.OpenAsync();
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<CustomerInfo>();
        while (await reader.ReadAsync()) list.Add(Map(reader));
        return list;
    }

    public Task<CustomerInfo?> GetAsync(int userId) =>
        ReadOneAsync("accounts.usp_Customer_GetById", cmd => cmd.AddParam("@UserId", SqlDbType.Int, userId));

    public Task<CustomerInfo?> SetActiveAsync(int userId, bool isActive) =>
        ReadOneAsync("accounts.usp_Customer_SetActive", cmd =>
        {
            cmd.AddParam("@UserId",   SqlDbType.Int, userId);
            cmd.AddParam("@IsActive", SqlDbType.Bit, isActive);
        });

    public Task<CustomerInfo?> UpdateDetailsAsync(int userId, string fullName, string? phone, string? address) =>
        ReadOneAsync("accounts.usp_Customer_UpdateDetails", cmd =>
        {
            cmd.AddParam("@UserId",   SqlDbType.Int, userId);
            cmd.AddParam("@FullName", SqlDbType.NVarChar, fullName, 100);
            cmd.AddParam("@Phone",    SqlDbType.NVarChar, phone, 20);
            cmd.AddParam("@Address",  SqlDbType.NVarChar, address, 250);
        });

    public Task<CustomerInfo?> ResetPasswordAsync(int userId, string passwordHash, DateTime expiresAtUtc) =>
        ReadOneAsync("accounts.usp_Customer_ResetPassword", cmd =>
        {
            cmd.AddParam("@UserId",       SqlDbType.Int, userId);
            cmd.AddParam("@PasswordHash", SqlDbType.NVarChar, passwordHash, 100);
            cmd.AddParam("@ExpiresAt",    SqlDbType.DateTime2, expiresAtUtc);
        });

    private async Task<CustomerInfo?> ReadOneAsync(string proc, Action<SqlCommand> addParams)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc(proc);
        addParams(cmd);

        await conn.OpenAsync();
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    private static CustomerInfo Map(SqlDataReader r) => new(
        r.GetInt32(r.GetOrdinal("UserId")),
        r.GetString(r.GetOrdinal("FullName")),
        r.GetString(r.GetOrdinal("Email")),
        r.GetStringOrNull("Phone"),
        r.GetBoolean(r.GetOrdinal("IsActive")),
        r.GetUtcDateTime("CreatedAt"),
        r.GetBoolean(r.GetOrdinal("EmailVerified")),
        r.GetStringOrNull("Address"),
        r.GetBoolean(r.GetOrdinal("MustChangePassword")));
}
