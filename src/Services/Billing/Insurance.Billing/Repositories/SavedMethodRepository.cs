using System.Data;
using Insurance.Billing.Enums;
using Insurance.Billing.Models;
using Insurance.BuildingBlocks.Data;
using Microsoft.Data.SqlClient;

namespace Insurance.Billing.Repositories;

public class SavedMethodRepository : ISavedMethodRepository
{
    private readonly IDbConnectionFactory _db;
    public SavedMethodRepository(IDbConnectionFactory db) => _db = db;

    public async Task<List<SavedPaymentMethod>> GetByUserAsync(int userId)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("billing.usp_SavedMethod_GetByUser");
        cmd.AddParam("@UserId", SqlDbType.Int, userId);

        await conn.OpenAsync();
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<SavedPaymentMethod>();
        while (await reader.ReadAsync()) list.Add(Map(reader));
        return list;
    }

    public async Task<SavedPaymentMethod?> GetByIdAsync(int paymentMethodId)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("billing.usp_SavedMethod_GetById");
        cmd.AddParam("@PaymentMethodId", SqlDbType.Int, paymentMethodId);

        await conn.OpenAsync();
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    public async Task AddAsync(SavedPaymentMethod m)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("billing.usp_SavedMethod_Add");
        cmd.AddParam("@UserId",           SqlDbType.Int, m.UserId);
        cmd.AddParam("@MethodType",       SqlDbType.NVarChar, m.MethodType.ToString(), 20);
        cmd.AddParam("@CardBrand",        SqlDbType.NVarChar, m.CardBrand, 20);
        cmd.AddParam("@CardLast4",        SqlDbType.Char, m.CardLast4, 4);
        cmd.AddParam("@ExpMonth",         SqlDbType.TinyInt, m.ExpMonth is { } month ? (byte)month : null);
        cmd.AddParam("@ExpYear",          SqlDbType.SmallInt, m.ExpYear is { } year ? (short)year : null);
        cmd.AddParam("@HolderName",       SqlDbType.NVarChar, m.HolderName, 100);
        cmd.AddParam("@Provider",         SqlDbType.NVarChar, m.Provider, 30);
        cmd.AddParam("@ProviderMethodId", SqlDbType.NVarChar, m.ProviderMethodId, 100);

        await conn.OpenAsync();
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<bool> DeleteAsync(int paymentMethodId, int userId)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("billing.usp_SavedMethod_Delete");
        cmd.AddParam("@PaymentMethodId", SqlDbType.Int, paymentMethodId);
        cmd.AddParam("@UserId",          SqlDbType.Int, userId);

        await conn.OpenAsync();
        return (int)(await cmd.ExecuteScalarAsync())! == 1;
    }

    private static SavedPaymentMethod Map(SqlDataReader r) => new()
    {
        PaymentMethodId  = r.GetInt32(r.GetOrdinal("PaymentMethodId")),
        UserId           = r.GetInt32(r.GetOrdinal("UserId")),
        MethodType       = r.GetEnum<PaymentMethodType>("MethodType"),
        CardBrand        = r.GetStringOrNull("CardBrand"),
        CardLast4        = r.GetStringOrNull("CardLast4"),
        ExpMonth         = r.IsDBNull(r.GetOrdinal("ExpMonth")) ? null : r.GetByte(r.GetOrdinal("ExpMonth")),
        ExpYear          = r.IsDBNull(r.GetOrdinal("ExpYear")) ? null : r.GetInt16(r.GetOrdinal("ExpYear")),
        HolderName       = r.GetStringOrNull("HolderName"),
        Provider         = r.GetString(r.GetOrdinal("Provider")),
        ProviderMethodId = r.GetString(r.GetOrdinal("ProviderMethodId")),
        CreatedAt        = r.GetUtcDateTime("CreatedAt")
    };
}
