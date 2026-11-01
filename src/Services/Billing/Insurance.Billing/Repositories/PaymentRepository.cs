using System.Data;
using Insurance.Billing.Contracts;
using Insurance.Billing.Enums;
using Insurance.Billing.Models;
using Insurance.BuildingBlocks.Data;
using Microsoft.Data.SqlClient;

namespace Insurance.Billing.Repositories;

public class PaymentRepository : IPaymentRepository
{
    private readonly IDbConnectionFactory _db;
    public PaymentRepository(IDbConnectionFactory db) => _db = db;

    public async Task<List<Payment>> GetByEnrollmentAsync(int enrollmentId)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("billing.usp_Payment_GetByEnrollment");
        cmd.AddParam("@EnrollmentId", SqlDbType.Int, enrollmentId);

        await conn.OpenAsync();
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<Payment>();
        while (await reader.ReadAsync()) list.Add(Map(reader));
        return list;
    }

    public async Task<Payment?> GetByIdAsync(int paymentId)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("billing.usp_Payment_GetById");
        cmd.AddParam("@PaymentId", SqlDbType.Int, paymentId);

        await conn.OpenAsync();
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    public async Task CreateScheduleAsync(int enrollmentId, DateOnly startDate, int termMonths, decimal monthlyPremium)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("billing.usp_Premium_CreateSchedule");
        cmd.AddParam("@EnrollmentId",   SqlDbType.Int, enrollmentId);
        cmd.AddParam("@StartDate",      SqlDbType.Date, startDate.ToDateTime(TimeOnly.MinValue));
        cmd.AddParam("@TermMonths",     SqlDbType.Int, termMonths);
        cmd.AddParam("@MonthlyPremium", SqlDbType.Decimal, monthlyPremium, precision: 18, scale: 2);

        await conn.OpenAsync();
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RemoveUnpaidAsync(int enrollmentId)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("billing.usp_Premium_RemoveUnpaid");
        cmd.AddParam("@EnrollmentId", SqlDbType.Int, enrollmentId);

        await conn.OpenAsync();
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<Dictionary<int, PremiumSummary>> GetSummariesAsync(IEnumerable<int> enrollmentIds)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("billing.usp_Premium_GetSummaries");
        cmd.AddIdListParam("@EnrollmentIds", enrollmentIds);

        await conn.OpenAsync();
        await using var reader = await cmd.ExecuteReaderAsync();
        var summaries = new Dictionary<int, PremiumSummary>();
        while (await reader.ReadAsync())
        {
            summaries[reader.GetInt32(reader.GetOrdinal("EnrollmentId"))] = new PremiumSummary(
                reader.GetInt32(reader.GetOrdinal("PaymentsPaid")),
                reader.GetInt32(reader.GetOrdinal("PaymentsTotal")),
                reader.GetInt32(reader.GetOrdinal("OverdueCount")),
                reader.IsDBNull(reader.GetOrdinal("NextDueDate")) ? null : reader.GetDateOnly("NextDueDate"),
                reader.IsDBNull(reader.GetOrdinal("NextDueAmount")) ? null : reader.GetDecimal(reader.GetOrdinal("NextDueAmount")));
        }
        return summaries;
    }

    // Also used by TransactionRepository, which reads the same row after settling a payment.
    internal static Payment Map(SqlDataReader r) => new()
    {
        PaymentId = r.GetInt32(r.GetOrdinal("PaymentId")),
        EnrollmentId = r.GetInt32(r.GetOrdinal("EnrollmentId")),
        DueDate = r.GetDateOnly("DueDate"),
        Amount = r.GetDecimal(r.GetOrdinal("Amount")),
        PaidDate = r.IsDBNull(r.GetOrdinal("PaidDate")) ? null : r.GetDateOnly("PaidDate"),
        Status = r.GetEnum<PaymentStatus>("Status")
    };
}