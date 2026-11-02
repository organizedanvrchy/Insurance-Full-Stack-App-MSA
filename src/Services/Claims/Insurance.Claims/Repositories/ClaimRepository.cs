using System.Data;
using Insurance.BuildingBlocks.Data;
using Insurance.BuildingBlocks.Exceptions;
using Insurance.Claims.Contracts;
using Insurance.Claims.Enums;
using Insurance.Claims.Exceptions;
using Insurance.Claims.Models;
using Microsoft.Data.SqlClient;

namespace Insurance.Claims.Repositories;

public class ClaimRepository : IClaimRepository
{
    private readonly IDbConnectionFactory _db;
    public ClaimRepository(IDbConnectionFactory db) => _db = db;

    public async Task<int> SubmitAsync(int userId, int enrollmentId, DateOnly incidentDate, string description, decimal amount, decimal coverageAmount)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("claims.usp_Claim_Submit");
        cmd.AddParam("@UserId",       SqlDbType.Int, userId);
        cmd.AddParam("@EnrollmentId", SqlDbType.Int, enrollmentId);
        cmd.AddParam("@IncidentDate", SqlDbType.Date, incidentDate.ToDateTime(TimeOnly.MinValue));
        cmd.AddParam("@Description",  SqlDbType.NVarChar, description, 2000);
        cmd.AddParam("@ClaimAmount",  SqlDbType.Decimal, amount, precision: 18, scale: 2);
        cmd.AddParam("@CoverageAmount", SqlDbType.Decimal, coverageAmount, precision: 18, scale: 2);

        await conn.OpenAsync();
        try
        {
            return (int)(await cmd.ExecuteScalarAsync())!;
        }
        // Amount over the remaining coverage: the caller's input broke a business rule.
        catch (SqlException ex) when (ex.Number == 50012)
        {
            throw new BusinessRuleException(ex.Message);
        }
    }

    public async Task<List<InsuranceClaim>> GetByUserAsync(int userId)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("claims.usp_Claim_GetByUser");
        cmd.AddParam("@UserId", SqlDbType.Int, userId);
        return await ReadListAsync(conn, cmd);
    }

    public async Task<List<InsuranceClaim>> GetAllAsync(ClaimStatus? status)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("claims.usp_Claim_GetAll");
        cmd.AddParam("@Status", SqlDbType.NVarChar, status?.ToString(), 20);   // null => DBNull => "all statuses"
        return await ReadListAsync(conn, cmd);
    }

    public async Task<InsuranceClaim?> GetByIdAsync(int claimId)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("claims.usp_Claim_GetById");
        cmd.AddParam("@ClaimId", SqlDbType.Int, claimId);

        await conn.OpenAsync();
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    public async Task<InsuranceClaim> ReviewAsync(int claimId, ClaimStatus from, ClaimStatus to, string? adminNotes, int reviewerId, decimal? coverageAmount)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("claims.usp_Claim_Review");
        cmd.AddParam("@ClaimId",    SqlDbType.Int, claimId);
        cmd.AddParam("@FromStatus", SqlDbType.NVarChar, from.ToString(), 20);
        cmd.AddParam("@ToStatus",   SqlDbType.NVarChar, to.ToString(), 20);
        cmd.AddParam("@AdminNotes", SqlDbType.NVarChar, adminNotes, 1000);
        cmd.AddParam("@ReviewerId", SqlDbType.Int, reviewerId);
        cmd.AddParam("@CoverageAmount", SqlDbType.Decimal, coverageAmount, precision: 18, scale: 2);

        await conn.OpenAsync();
        try
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                throw new InvalidOperationException("usp_Claim_Review returned no row.");
            return Map(reader);
        }
        catch (SqlException ex) when (ex.Number == 50014)
        {
            throw new NotFoundException("Claim not found.");
        }
        catch (SqlException ex) when (ex.Number is 50013 or 50015)
        {
            throw new ClaimConflictException(ex.Message);
        }
    }

    public async Task<InsuranceClaim> SendToReviewerAsync(int claimId, int userId)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("claims.usp_Claim_SendToReviewer");
        cmd.AddParam("@ClaimId", SqlDbType.Int, claimId);
        cmd.AddParam("@UserId",  SqlDbType.Int, userId);

        await conn.OpenAsync();
        try
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                throw new InvalidOperationException("usp_Claim_SendToReviewer returned no row.");
            return Map(reader);
        }
        catch (SqlException ex) when (ex.Number == 50021)
        {
            throw new ClaimConflictException(ex.Message);
        }
    }

    public async Task<Dictionary<int, ClaimCounts>> GetCountsByUserAsync(IEnumerable<int> userIds)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("claims.usp_Claim_CountByUser");
        cmd.AddIdListParam("@UserIds", userIds);

        await conn.OpenAsync();
        await using var reader = await cmd.ExecuteReaderAsync();
        var counts = new Dictionary<int, ClaimCounts>();
        while (await reader.ReadAsync())
        {
            counts[reader.GetInt32(reader.GetOrdinal("UserId"))] = new ClaimCounts(
                reader.GetInt32(reader.GetOrdinal("TotalClaims")),
                reader.GetInt32(reader.GetOrdinal("OpenClaims")));
        }
        return counts;
    }

    private static async Task<List<InsuranceClaim>> ReadListAsync(SqlConnection conn, SqlCommand cmd)
    {
        await conn.OpenAsync();
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<InsuranceClaim>();
        while (await reader.ReadAsync()) list.Add(Map(reader));
        return list;
    }

    private static InsuranceClaim Map(SqlDataReader r) => new()
    {
        ClaimId        = r.GetInt32(r.GetOrdinal("ClaimId")),
        ClaimNumber    = r.GetString(r.GetOrdinal("ClaimNumber")),
        EnrollmentId   = r.GetInt32(r.GetOrdinal("EnrollmentId")),
        UserId         = r.GetInt32(r.GetOrdinal("UserId")),
        IncidentDate   = r.GetDateOnly("IncidentDate"),
        Description    = r.GetString(r.GetOrdinal("Description")),
        ClaimAmount    = r.GetDecimal(r.GetOrdinal("ClaimAmount")),
        Status         = r.GetEnum<ClaimStatus>("Status"),
        AdminNotes     = r.GetStringOrNull("AdminNotes"),
        SubmittedAt    = r.GetUtcDateTime("SubmittedAt"),
        ReviewedAt     = r.GetUtcDateTimeOrNull("ReviewedAt"),
        ReviewedByUserId = r.IsDBNull(r.GetOrdinal("ReviewedByUserId")) ? null : r.GetInt32(r.GetOrdinal("ReviewedByUserId"))
    };
}