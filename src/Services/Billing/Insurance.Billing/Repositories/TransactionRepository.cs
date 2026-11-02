using System.Data;
using Insurance.Billing.Contracts;
using Insurance.Billing.Enums;
using Insurance.Billing.Exceptions;
using Insurance.Billing.Models;
using Insurance.BuildingBlocks.Data;
using Insurance.BuildingBlocks.Messaging;
using Microsoft.Data.SqlClient;

namespace Insurance.Billing.Repositories;

public class TransactionRepository : ITransactionRepository
{
    private readonly IDbConnectionFactory _db;
    private readonly IOutbox _outbox;

    public TransactionRepository(IDbConnectionFactory db, IOutbox outbox)
    {
        _db = db;
        _outbox = outbox;
    }

    public async Task<PaymentTransaction?> GetByKeyAsync(int userId, string idempotencyKey)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("billing.usp_Transaction_GetByKey");
        cmd.AddParam("@UserId",         SqlDbType.Int, userId);
        cmd.AddParam("@IdempotencyKey", SqlDbType.NVarChar, idempotencyKey, 100);
        return await ReadOneAsync(conn, cmd);
    }

    public async Task<(PaymentTransaction Transaction, bool Existing)> BeginAsync(PaymentTransaction t)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("billing.usp_Transaction_Begin");
        cmd.AddParam("@PaymentId",         SqlDbType.Int, t.PaymentId);
        cmd.AddParam("@EnrollmentId",      SqlDbType.Int, t.EnrollmentId);
        cmd.AddParam("@UserId",            SqlDbType.Int, t.UserId);
        cmd.AddParam("@Amount",            SqlDbType.Decimal, t.Amount, precision: 18, scale: 2);
        cmd.AddParam("@MethodType",        SqlDbType.NVarChar, t.MethodType.ToString(), 20);
        cmd.AddParam("@CardBrand",         SqlDbType.NVarChar, t.CardBrand, 20);
        cmd.AddParam("@CardLast4",         SqlDbType.Char, t.CardLast4, 4);
        cmd.AddParam("@Provider",          SqlDbType.NVarChar, t.Provider, 30);
        cmd.AddParam("@IdempotencyKey",    SqlDbType.NVarChar, t.IdempotencyKey, 100);
        cmd.AddParam("@BillingName",       SqlDbType.NVarChar, t.BillingName, 100);
        cmd.AddParam("@BillingPostalCode", SqlDbType.NVarChar, t.BillingPostalCode, 10);

        await conn.OpenAsync();
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (Map(reader), reader.GetBoolean(reader.GetOrdinal("Existing")));
    }

    public async Task<PaymentTransaction> CompleteAsync(int transactionId, TransactionStatus status, string? providerChargeId,
        string? declineCode, string? message, PaymentDeclined? declinedEvent = null)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync();

        PaymentTransaction completed;
        await using (var cmd = conn.StoredProc("billing.usp_Transaction_Complete"))
        {
            cmd.Transaction = tx;
            cmd.AddParam("@TransactionId",    SqlDbType.Int, transactionId);
            cmd.AddParam("@Status",           SqlDbType.NVarChar, status.ToString(), 20);
            cmd.AddParam("@ProviderChargeId", SqlDbType.NVarChar, providerChargeId, 100);
            cmd.AddParam("@DeclineCode",      SqlDbType.NVarChar, declineCode, 50);
            cmd.AddParam("@Message",          SqlDbType.NVarChar, message, 300);

            await using var reader = await cmd.ExecuteReaderAsync();
            await reader.ReadAsync();
            completed = Map(reader);
        }

        if (declinedEvent is not null)
            await _outbox.EnqueueAsync(conn, tx, declinedEvent);

        await tx.CommitAsync();
        if (declinedEvent is not null) _outbox.Notify();
        return completed;
    }

    public async Task<Payment> SettleAsync(int paymentId, int transactionId, string providerChargeId, string message, ReceiptDetails receipt)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync();

        // The proc's own BEGIN/COMMIT nest inside this transaction, so the receipt event commits with the payment.
        Payment paid;
        await using (var cmd = conn.StoredProc("billing.usp_Payment_Settle"))
        {
            cmd.Transaction = tx;
            cmd.AddParam("@PaymentId",        SqlDbType.Int, paymentId);
            cmd.AddParam("@TransactionId",    SqlDbType.Int, transactionId);
            cmd.AddParam("@ProviderChargeId", SqlDbType.NVarChar, providerChargeId, 100);
            cmd.AddParam("@Message",          SqlDbType.NVarChar, message, 300);
            try
            {
                await using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    throw new InvalidOperationException("usp_Payment_Settle returned no row.");
                paid = PaymentRepository.Map(reader);
            }
            catch (SqlException ex) when (ex.Number == 50004)
            {
                throw new PaymentNotFoundException(paymentId);
            }
            // 50005 already paid, 50007 earlier installment unpaid: the proc's message is already user-friendly.
            catch (SqlException ex) when (ex.Number is 50005 or 50007)
            {
                throw new PaymentConflictException(ex.Message);
            }
        }

        // Where this installment sits in the schedule and what's due next. (The reader is closed before the outbox
        // insert: both use this connection.)
        int number, count;
        DateOnly? nextDueDate;
        decimal? nextDueAmount;
        await using (var info = conn.StoredProc("billing.usp_Payment_GetReceiptInfo"))
        {
            info.Transaction = tx;
            info.AddParam("@PaymentId", SqlDbType.Int, paymentId);
            await using var reader = await info.ExecuteReaderAsync();
            await reader.ReadAsync();

            number = reader.GetInt32(reader.GetOrdinal("InstallmentNumber"));
            count = reader.GetInt32(reader.GetOrdinal("InstallmentCount"));
            var hasNext = !reader.IsDBNull(reader.GetOrdinal("NextDueDate"));
            nextDueDate = hasNext ? reader.GetDateOnly("NextDueDate") : null;
            nextDueAmount = hasNext ? reader.GetDecimal(reader.GetOrdinal("NextDueAmount")) : null;
        }

        await _outbox.EnqueueAsync(conn, tx, new PaymentReceived(
            paid.PaymentId, transactionId, Services.PaymentService.Reference(transactionId), paid.EnrollmentId,
            receipt.UserId, receipt.PolicyId, paid.Amount, paid.DueDate, paid.PaidDate!.Value, DateTime.UtcNow,
            receipt.PaymentMethod, number, count, nextDueDate, nextDueAmount));

        await tx.CommitAsync();
        _outbox.Notify();
        return paid;
    }

    public async Task<PaymentTransaction?> GetByIdAsync(int transactionId)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("billing.usp_Transaction_GetById");
        cmd.AddParam("@TransactionId", SqlDbType.Int, transactionId);
        return await ReadOneAsync(conn, cmd);
    }

    public async Task<List<PaymentTransaction>> SearchAsync(int? enrollmentId, int? userId, TransactionStatus? status, int top = 200)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("billing.usp_Transaction_Search");
        cmd.AddParam("@EnrollmentId", SqlDbType.Int, enrollmentId);
        cmd.AddParam("@UserId",       SqlDbType.Int, userId);
        cmd.AddParam("@Status",       SqlDbType.NVarChar, status?.ToString(), 20);
        cmd.AddParam("@Top",          SqlDbType.Int, top);

        await conn.OpenAsync();
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<PaymentTransaction>();
        while (await reader.ReadAsync()) list.Add(Map(reader));
        return list;
    }

    private static async Task<PaymentTransaction?> ReadOneAsync(SqlConnection conn, SqlCommand cmd)
    {
        await conn.OpenAsync();
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    private static PaymentTransaction Map(SqlDataReader r) => new()
    {
        TransactionId     = r.GetInt32(r.GetOrdinal("TransactionId")),
        PaymentId         = r.GetInt32(r.GetOrdinal("PaymentId")),
        EnrollmentId      = r.GetInt32(r.GetOrdinal("EnrollmentId")),
        UserId            = r.GetInt32(r.GetOrdinal("UserId")),
        Amount            = r.GetDecimal(r.GetOrdinal("Amount")),
        Currency          = r.GetString(r.GetOrdinal("Currency")),
        MethodType        = r.GetEnum<PaymentMethodType>("MethodType"),
        CardBrand         = r.GetStringOrNull("CardBrand"),
        CardLast4         = r.GetStringOrNull("CardLast4"),
        Provider          = r.GetString(r.GetOrdinal("Provider")),
        ProviderChargeId  = r.GetStringOrNull("ProviderChargeId"),
        Status            = r.GetEnum<TransactionStatus>("Status"),
        DeclineCode       = r.GetStringOrNull("DeclineCode"),
        Message           = r.GetStringOrNull("Message"),
        IdempotencyKey    = r.GetString(r.GetOrdinal("IdempotencyKey")),
        BillingName       = r.GetStringOrNull("BillingName"),
        BillingPostalCode = r.GetStringOrNull("BillingPostalCode"),
        CreatedAt         = r.GetUtcDateTime("CreatedAt"),
        CompletedAt       = r.GetUtcDateTimeOrNull("CompletedAt")
    };
}
