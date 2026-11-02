using System.Data;
using Insurance.BuildingBlocks.Data;
using Insurance.Claims.Exceptions;
using Insurance.Claims.Models;
using Microsoft.Data.SqlClient;

namespace Insurance.Claims.Repositories;

public class ClaimDocumentRepository : IClaimDocumentRepository
{
    private readonly IDbConnectionFactory _db;
    public ClaimDocumentRepository(IDbConnectionFactory db) => _db = db;

    public async Task<ClaimDocument> AddAsync(ClaimDocument document, int maxDocuments)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("claims.usp_ClaimDocument_Add");
        cmd.AddParam("@ClaimId",          SqlDbType.Int, document.ClaimId);
        cmd.AddParam("@FileName",         SqlDbType.NVarChar, document.FileName, 255);
        cmd.AddParam("@ContentType",      SqlDbType.NVarChar, document.ContentType, 100);
        cmd.AddParam("@SizeBytes",        SqlDbType.Int, document.SizeBytes);
        cmd.AddParam("@StorageKey",       SqlDbType.NVarChar, document.StorageKey, 100);
        cmd.AddParam("@UploadedByUserId", SqlDbType.Int, document.UploadedByUserId);
        cmd.AddParam("@MaxDocuments",     SqlDbType.Int, maxDocuments);

        await conn.OpenAsync();
        try
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                throw new InvalidOperationException("usp_ClaimDocument_Add returned no row.");
            return Map(reader);
        }
        catch (SqlException ex) when (ex.Number == 50020)
        {
            throw new ClaimConflictException(ex.Message);
        }
    }

    public async Task<List<ClaimDocument>> GetByClaimAsync(int claimId)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("claims.usp_ClaimDocument_GetByClaim");
        cmd.AddParam("@ClaimId", SqlDbType.Int, claimId);

        await conn.OpenAsync();
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<ClaimDocument>();
        while (await reader.ReadAsync()) list.Add(Map(reader));
        return list;
    }

    public async Task<ClaimDocument?> GetByIdAsync(int claimId, int claimDocumentId)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("claims.usp_ClaimDocument_GetById");
        cmd.AddParam("@ClaimId",         SqlDbType.Int, claimId);
        cmd.AddParam("@ClaimDocumentId", SqlDbType.Int, claimDocumentId);

        await conn.OpenAsync();
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    private static ClaimDocument Map(SqlDataReader r) => new()
    {
        ClaimDocumentId  = r.GetInt32(r.GetOrdinal("ClaimDocumentId")),
        ClaimId          = r.GetInt32(r.GetOrdinal("ClaimId")),
        FileName         = r.GetString(r.GetOrdinal("FileName")),
        ContentType      = r.GetString(r.GetOrdinal("ContentType")),
        SizeBytes        = r.GetInt32(r.GetOrdinal("SizeBytes")),
        StorageKey       = r.GetString(r.GetOrdinal("StorageKey")),
        UploadedByUserId = r.GetInt32(r.GetOrdinal("UploadedByUserId")),
        UploadedAt       = r.GetUtcDateTime("UploadedAt")
    };
}
