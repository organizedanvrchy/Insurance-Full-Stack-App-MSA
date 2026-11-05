using System.Data;
using Insurance.BuildingBlocks.Data;
using Insurance.Notifications.Models;
using Microsoft.Data.SqlClient;

namespace Insurance.Notifications.Repositories;

public class NotificationRepository : INotificationRepository
{
    private readonly IDbConnectionFactory _db;
    public NotificationRepository(IDbConnectionFactory db) => _db = db;

    public async Task<NotificationPreferences> GetPreferencesAsync(int userId)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("notifications.usp_Preferences_Get");
        cmd.AddParam("@UserId", SqlDbType.Int, userId);
        return await ReadPreferencesAsync(conn, cmd);
    }

    public async Task<NotificationPreferences> SetPreferencesAsync(int userId, bool paymentReminders)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("notifications.usp_Preferences_Set");
        cmd.AddParam("@UserId",           SqlDbType.Int, userId);
        cmd.AddParam("@PaymentReminders", SqlDbType.Bit, paymentReminders);
        return await ReadPreferencesAsync(conn, cmd);
    }

    public async Task SetInitialPreferencesAsync(int userId, bool paymentReminders)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("notifications.usp_Preferences_SetInitial");
        cmd.AddParam("@UserId",           SqlDbType.Int, userId);
        cmd.AddParam("@PaymentReminders", SqlDbType.Bit, paymentReminders);

        await conn.OpenAsync();
        try
        {
            await cmd.ExecuteNonQueryAsync();
        }
        catch (SqlException ex) when (ex.Number is 2627 or 2601) { }   // a profile change won the race: keep it
    }

    public async Task<bool> BeginAsync(string notificationKey, int userId, NotificationKind kind)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("notifications.usp_Email_Begin");
        cmd.AddParam("@NotificationKey", SqlDbType.NVarChar, notificationKey, 200);
        cmd.AddParam("@UserId",          SqlDbType.Int, userId);
        cmd.AddParam("@Kind",            SqlDbType.NVarChar, kind.ToString(), 40);

        await conn.OpenAsync();
        try
        {
            return (bool)(await cmd.ExecuteScalarAsync())!;
        }
        // Two deliveries of the same event racing: the other one inserted the row first and is sending it.
        catch (SqlException ex) when (ex.Number is 2627 or 2601)
        {
            return false;
        }
    }

    public async Task CompleteAsync(string notificationKey, EmailStatus status, string? toEmail = null, string? subject = null, string? detail = null)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("notifications.usp_Email_Complete");
        cmd.AddParam("@NotificationKey", SqlDbType.NVarChar, notificationKey, 200);
        cmd.AddParam("@Status",          SqlDbType.NVarChar, status.ToString(), 20);
        cmd.AddParam("@ToEmail",         SqlDbType.NVarChar, toEmail, 256);
        cmd.AddParam("@Subject",         SqlDbType.NVarChar, Truncate(subject, 300), 300);
        cmd.AddParam("@Detail",          SqlDbType.NVarChar, Truncate(detail, 1000), 1000);

        await conn.OpenAsync();
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<SentEmail>> GetRecentAsync(int userId, int take)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("notifications.usp_Email_GetRecentByUser");
        cmd.AddParam("@UserId", SqlDbType.Int, userId);
        cmd.AddParam("@Take",   SqlDbType.Int, take);

        await conn.OpenAsync();
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<SentEmail>();
        while (await reader.ReadAsync())
        {
            list.Add(new SentEmail(
                reader.GetInt64(reader.GetOrdinal("EmailId")),
                reader.GetEnum<NotificationKind>("Kind"),
                reader.GetStringOrNull("ToEmail"),
                reader.GetStringOrNull("Subject"),
                reader.GetUtcDateTimeOrNull("SentAt")));
        }
        return list;
    }

    public async Task QueueClaimEmailAsync(int claimId, string payload, DateTime sendAfterUtc)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("notifications.usp_PendingClaimEmail_Add");
        cmd.AddParam("@ClaimId",   SqlDbType.Int, claimId);
        cmd.AddParam("@Payload",   SqlDbType.NVarChar, payload);
        cmd.AddParam("@SendAfter", SqlDbType.DateTime2, sendAfterUtc);

        await conn.OpenAsync();
        try
        {
            await cmd.ExecuteNonQueryAsync();
        }
        catch (SqlException ex) when (ex.Number is 2627 or 2601) { }   // the same event, twice at once: already queued
    }

    public async Task<List<PendingClaimEmail>> TakeDueClaimEmailsAsync(int take, TimeSpan lease)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("notifications.usp_PendingClaimEmail_TakeDue");
        cmd.AddParam("@Take",         SqlDbType.Int, take);
        cmd.AddParam("@LeaseSeconds", SqlDbType.Int, (int)lease.TotalSeconds);

        await conn.OpenAsync();
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<PendingClaimEmail>();
        while (await reader.ReadAsync())
            list.Add(new PendingClaimEmail(reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2)));
        return list;
    }

    public async Task RemoveClaimEmailAsync(int claimId)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("notifications.usp_PendingClaimEmail_Remove");
        cmd.AddParam("@ClaimId", SqlDbType.Int, claimId);

        await conn.OpenAsync();
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DelayClaimEmailAsync(int claimId, DateTime sendAfterUtc)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("notifications.usp_PendingClaimEmail_Delay");
        cmd.AddParam("@ClaimId",   SqlDbType.Int, claimId);
        cmd.AddParam("@SendAfter", SqlDbType.DateTime2, sendAfterUtc);

        await conn.OpenAsync();
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task QueueRetryAsync(string notificationKey, string payload, DateTime sendAfterUtc, string toEmail,
        string subject, string detail)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("notifications.usp_EmailRetry_Add");
        cmd.AddParam("@NotificationKey", SqlDbType.NVarChar, notificationKey, 200);
        cmd.AddParam("@Payload",         SqlDbType.NVarChar, payload);
        cmd.AddParam("@SendAfter",       SqlDbType.DateTime2, sendAfterUtc);
        cmd.AddParam("@ToEmail",         SqlDbType.NVarChar, toEmail, 256);
        cmd.AddParam("@Subject",         SqlDbType.NVarChar, Truncate(subject, 300), 300);
        cmd.AddParam("@Detail",          SqlDbType.NVarChar, Truncate(detail, 1000), 1000);

        await conn.OpenAsync();
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<EmailRetry>> TakeDueRetriesAsync(int take, TimeSpan lease)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("notifications.usp_EmailRetry_TakeDue");
        cmd.AddParam("@Take",         SqlDbType.Int, take);
        cmd.AddParam("@LeaseSeconds", SqlDbType.Int, (int)lease.TotalSeconds);

        await conn.OpenAsync();
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<EmailRetry>();
        while (await reader.ReadAsync())
            list.Add(new EmailRetry(reader.GetString(0), reader.GetString(1), reader.GetInt32(2)));
        return list;
    }

    public async Task RescheduleRetryAsync(string notificationKey, DateTime sendAfterUtc, string detail)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("notifications.usp_EmailRetry_Reschedule");
        cmd.AddParam("@NotificationKey", SqlDbType.NVarChar, notificationKey, 200);
        cmd.AddParam("@SendAfter",       SqlDbType.DateTime2, sendAfterUtc);
        cmd.AddParam("@Detail",          SqlDbType.NVarChar, Truncate(detail, 1000), 1000);

        await conn.OpenAsync();
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RemoveRetryAsync(string notificationKey)
    {
        await using var conn = _db.Create();
        await using var cmd = conn.StoredProc("notifications.usp_EmailRetry_Remove");
        cmd.AddParam("@NotificationKey", SqlDbType.NVarChar, notificationKey, 200);

        await conn.OpenAsync();
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<NotificationPreferences> ReadPreferencesAsync(SqlConnection conn, SqlCommand cmd)
    {
        await conn.OpenAsync();
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return new NotificationPreferences(
            reader.GetInt32(reader.GetOrdinal("UserId")),
            reader.GetBoolean(reader.GetOrdinal("PaymentReminders")),
            reader.GetUtcDateTimeOrNull("UpdatedAt"));
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
