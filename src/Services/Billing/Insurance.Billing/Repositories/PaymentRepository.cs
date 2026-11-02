using System.Data;
using Insurance.Billing.Contracts;
using Insurance.Billing.Enums;
using Insurance.Billing.Exceptions;
using Insurance.Billing.Models;
using Insurance.Billing.Services.Reminders;
using Insurance.BuildingBlocks.Data;
using Insurance.BuildingBlocks.Messaging;
using Microsoft.Data.SqlClient;

namespace Insurance.Billing.Repositories;

public class PaymentRepository : IPaymentRepository
{
    private readonly IDbConnectionFactory _db;
    private readonly IOutbox _outbox;

    public PaymentRepository(IDbConnectionFactory db, IOutbox outbox)
    {
        _db = db;
        _outbox = outbox;
    }

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

    public async Task<Payment> PayAsync(int paymentId, int userId, int policyId)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync();

        Payment paid;
        await using (var cmd = conn.StoredProc("billing.usp_Payment_Pay"))
        {
            cmd.Transaction = tx;
            cmd.AddParam("@PaymentId", SqlDbType.Int, paymentId);
            try
            {
                await using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    throw new InvalidOperationException("usp_Payment_Pay returned no row.");
                paid = Map(reader);
            }
            catch (SqlException ex) when (ex.Number == 50004)
            {
                throw new PaymentNotFoundException(paymentId);
            }
            // 50005 already paid, 50007 earlier installment unpaid.
            // The proc's THROW message is already user-friendly, so we pass it along.
            catch (SqlException ex) when (ex.Number is 50005 or 50007)
            {
                throw new PaymentConflictException(ex.Message);
            }
        }

        // The receipt: where this installment sits in the schedule and what's due next.
        // (Read and close the reader first: the outbox insert needs the same connection.)
        int installmentNumber, installmentCount;
        DateOnly? nextDueDate;
        decimal? nextDueAmount;
        await using (var info = conn.StoredProc("billing.usp_Payment_GetReceiptInfo"))
        {
            info.Transaction = tx;
            info.AddParam("@PaymentId", SqlDbType.Int, paymentId);
            await using var reader = await info.ExecuteReaderAsync();
            await reader.ReadAsync();

            installmentNumber = reader.GetInt32(reader.GetOrdinal("InstallmentNumber"));
            installmentCount = reader.GetInt32(reader.GetOrdinal("InstallmentCount"));
            var hasNext = !reader.IsDBNull(reader.GetOrdinal("NextDueDate"));
            nextDueDate = hasNext ? reader.GetDateOnly("NextDueDate") : null;
            nextDueAmount = hasNext ? reader.GetDecimal(reader.GetOrdinal("NextDueAmount")) : null;
        }

        await _outbox.EnqueueAsync(conn, tx, new PaymentReceived(
            paid.PaymentId,
            ReceiptNumber(paid.PaymentId),
            paid.EnrollmentId,
            userId,
            policyId,
            paid.Amount,
            paid.DueDate,
            paid.PaidDate!.Value,
            DateTime.UtcNow,
            installmentNumber,
            installmentCount,
            nextDueDate,
            nextDueAmount));

        await tx.CommitAsync();
        _outbox.Notify();
        return paid;
    }

    /// <summary>The number printed on a payment receipt, e.g. PAY-00001234.</summary>
    public static string ReceiptNumber(int paymentId) => $"PAY-{paymentId:D8}";

    public async Task<List<ReminderCandidate>> GetReminderCandidatesAsync(DateOnly fromDueDate, DateOnly toDueDate)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("billing.usp_Reminder_GetCandidates");
        cmd.AddParam("@FromDate", SqlDbType.Date, fromDueDate.ToDateTime(TimeOnly.MinValue));
        cmd.AddParam("@ToDate",   SqlDbType.Date, toDueDate.ToDateTime(TimeOnly.MinValue));

        await conn.OpenAsync();
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<ReminderCandidate>();
        while (await reader.ReadAsync())
        {
            var sent = (reader.GetStringOrNull("SentStages") ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(Enum.Parse<PaymentReminderStage>)
                .ToHashSet();

            list.Add(new ReminderCandidate(
                reader.GetInt32(reader.GetOrdinal("PaymentId")),
                reader.GetInt32(reader.GetOrdinal("EnrollmentId")),
                reader.GetDateOnly("DueDate"),
                reader.GetDecimal(reader.GetOrdinal("Amount")),
                reader.GetDateOnly("OverdueFrom"),
                sent));
        }
        return list;
    }

    public async Task<bool> RecordReminderAsync(PaymentReminderDue reminder)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync();

        await using var cmd = conn.StoredProc("billing.usp_Reminder_Record");
        cmd.Transaction = tx;
        cmd.AddParam("@PaymentId", SqlDbType.Int, reminder.PaymentId);
        cmd.AddParam("@Stage",     SqlDbType.NVarChar, reminder.Stage.ToString(), 30);

        bool recorded;
        try
        {
            recorded = (int)(await cmd.ExecuteScalarAsync())! == 1;
        }
        // Another Billing instance recorded the same reminder a moment earlier.
        catch (SqlException ex) when (ex.Number is 2627 or 2601)
        {
            return false;
        }

        if (recorded)
            await _outbox.EnqueueAsync(conn, tx, reminder);

        await tx.CommitAsync();
        if (recorded) _outbox.Notify();
        return recorded;
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

    private static Payment Map(SqlDataReader r) => new()
    {
        PaymentId = r.GetInt32(r.GetOrdinal("PaymentId")),
        EnrollmentId = r.GetInt32(r.GetOrdinal("EnrollmentId")),
        DueDate = r.GetDateOnly("DueDate"),
        Amount = r.GetDecimal(r.GetOrdinal("Amount")),
        PaidDate = r.IsDBNull(r.GetOrdinal("PaidDate")) ? null : r.GetDateOnly("PaidDate"),
        Status = r.GetEnum<PaymentStatus>("Status")
    };
}