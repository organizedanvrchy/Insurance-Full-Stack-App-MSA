using System.Reflection;
using System.Text.RegularExpressions;
using Insurance.BuildingBlocks.Time;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Insurance.BuildingBlocks.Data;

/// <summary>
/// Each service owns its database and creates or upgrades it at startup. The .sql files embedded under the
/// service's Database/ folder run once each, in name order (001_..., 002_...), split on GO lines like sqlcmd does.
/// Applied scripts are recorded in dbo.SchemaVersions, so restarting a service changes nothing.
/// </summary>
public static partial class DatabaseMigrator
{
    public static async Task MigrateAsync(IConfiguration config, Assembly scripts, ILogger logger, CancellationToken ct = default)
    {
        var connectionString = config.GetConnectionString("Database")
            ?? throw new InvalidOperationException("Connection string 'Database' is missing.");
        var builder = new SqlConnectionStringBuilder(connectionString);
        var database = builder.InitialCatalog;

        // SQL Server may still be starting (docker compose starts everything at once), so retry for a while.
        // The database is looked up in sys.databases rather than with DB_ID(): on Azure SQL, DB_ID() run in master
        // doesn't see the server's other databases, so it would try to create one that already exists.
        builder.InitialCatalog = "master";
        await RetryAsync(() => ExecuteAsync(builder.ConnectionString, CreateDatabaseIfMissingSql(database)), logger, ct);

        // Retried too: an Azure SQL serverless database that was paused takes up to a minute to wake up.
        await RetryAsync(() => ExecuteAsync(connectionString, """
            IF OBJECT_ID('dbo.SchemaVersions', 'U') IS NULL
                CREATE TABLE dbo.SchemaVersions (
                    ScriptName NVARCHAR(200) NOT NULL CONSTRAINT PK_SchemaVersions PRIMARY KEY,
                    AppliedAt  DATETIME2     NOT NULL CONSTRAINT DF_SchemaVersions_AppliedAt DEFAULT SYSUTCDATETIME());
            """), logger, ct);

        // "Today" in the business time zone, for views and procedures (see BusinessTime). Recreated at every start
        // from configuration, before the scripts run, so the scripts can use it and it always matches the C# side.
        var zone = BusinessTime.SqlServerZoneName.Replace("'", "''");
        await ExecuteAsync(connectionString, $"""
            CREATE OR ALTER FUNCTION dbo.ufn_Today()
            RETURNS DATE
            AS
            BEGIN
                RETURN CAST(SYSUTCDATETIME() AT TIME ZONE 'UTC' AT TIME ZONE '{zone}' AS DATE);
            END
            """);

        var applied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var conn = new SqlConnection(connectionString))
        {
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand("SELECT ScriptName FROM dbo.SchemaVersions", conn);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) applied.Add(reader.GetString(0));
        }

        var pending = scripts.GetManifestResourceNames()
            .Where(n => n.Contains(".Database.") && n.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .Select(n => (Resource: n, Name: n[(n.IndexOf(".Database.") + ".Database.".Length)..]))
            .Where(s => !applied.Contains(s.Name))
            .OrderBy(s => s.Name, StringComparer.Ordinal);

        foreach (var (resource, name) in pending)
        {
            await using var stream = scripts.GetManifestResourceStream(resource)!;
            var sql = await new StreamReader(stream).ReadToEndAsync(ct);

            foreach (var batch in GoLine().Split(sql).Where(b => !string.IsNullOrWhiteSpace(b)))
                await ExecuteAsync(connectionString, batch);

            await ExecuteAsync(connectionString, $"INSERT INTO dbo.SchemaVersions (ScriptName) VALUES (N'{name.Replace("'", "''")}');");
            logger.LogInformation("Applied database script {Script} to {Database}", name, database);
        }
    }

    /// <summary>Run in master: creates the database unless the server already has one by that name.</summary>
    public static string CreateDatabaseIfMissingSql(string database) =>
        $"IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = N'{database.Replace("'", "''")}') " +
        $"CREATE DATABASE [{database.Replace("]", "]]")}];";

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 120 };
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task RetryAsync(Func<Task> action, ILogger logger, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await action();
                return;
            }
            catch (SqlException ex) when (attempt < 30)
            {
                logger.LogWarning("Database not reachable yet ({Message}). Retrying in 2s...", ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
    }

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex GoLine();
}
