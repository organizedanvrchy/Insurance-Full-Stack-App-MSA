using Insurance.BuildingBlocks.Data;
using Xunit;

namespace Insurance.Architecture.Tests;

public class DatabaseMigratorTests
{
    [Fact]
    public void The_database_is_looked_up_in_sys_databases_which_Azure_SQL_master_can_see()
    {
        var sql = DatabaseMigrator.CreateDatabaseIfMissingSql("KVBInsuranceDB-Catalog");

        Assert.Equal("IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = N'KVBInsuranceDB-Catalog') " +
                     "CREATE DATABASE [KVBInsuranceDB-Catalog];", sql);
        Assert.DoesNotContain("DB_ID", sql);
    }

    [Fact]
    public void Quotes_and_brackets_in_the_name_are_escaped()
    {
        var sql = DatabaseMigrator.CreateDatabaseIfMissingSql("odd'name]db");

        Assert.Contains("name = N'odd''name]db'", sql);
        Assert.Contains("CREATE DATABASE [odd'name]]db]", sql);
    }
}
