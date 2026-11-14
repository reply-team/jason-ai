using System.Globalization;
using Jason.Runtime.Approvals;
using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Persistence;

/// <summary>
/// The daily ceiling of automatic sends counts a campaign's automatic releases of the day at the claim, inside its write
/// transaction. The migration adds one partial index for that count and nothing else: the approvals table's columns, its
/// other indexes and every trigger are as they were, and <c>Down</c> drops the index again and rebuilds nothing. The
/// count's plan is pinned by <c>DailyCeilingPlanTests</c>.
/// </summary>
public class ApprovalsAutomaticByDayMigrationTests
{
    private const string PreviousMigration = "20261114064450_WorkItemDoubtNotes";

    private const string ThisMigration = "20261114084851_ApprovalsAutomaticByDay";

    [Fact]
    public void Up_adds_the_automatic_releases_index_alone()
    {
        using var dir = AtThePreviousMigration();
        var before = Shape(dir.Paths.DatabaseFile);

        // To this migration and no further, so a later migration's own change is not read as this one's.
        SqliteConnection.ClearAllPools();
        using (var db = new JasonDbContext(JasonDbContext.CreateOptions(dir.Paths.DatabaseFile)))
        {
            db.GetInfrastructure().GetRequiredService<IMigrator>().Migrate(ThisMigration);
        }

        var after = Shape(dir.Paths.DatabaseFile);
        var added = Assert.Single(after.Except(before));
        Assert.Equal(
            $"index approvals.{DailyCeiling.Index} | CREATE INDEX \"{DailyCeiling.Index}\" ON \"approvals\" (\"campaign_id\", \"decided_at\") WHERE reason = 'automatic_mode'",
            added);
        Assert.Empty(before.Except(after));
    }

    /// <summary>
    /// Down is the index's drop and nothing more: no table is rebuilt, which would drop the triggers and change what a column
    /// may hold, so the database is exactly what it was at the migration before.
    /// </summary>
    [Fact]
    public void Down_drops_the_index_alone_and_leaves_the_rest_as_it_stood()
    {
        using var dir = AtThePreviousMigration();
        var before = Shape(dir.Paths.DatabaseFile);
        new DatabaseMigrator(dir.Paths).Migrate();

        SqliteConnection.ClearAllPools();
        using (var db = new JasonDbContext(JasonDbContext.CreateOptions(dir.Paths.DatabaseFile)))
        {
            var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
            var down = migrator.GenerateScript(ThisMigration, PreviousMigration);
            Assert.Equal(
                [$"DROP INDEX \"{DailyCeiling.Index}\";"],
                down.ReplaceLineEndings("\n").Split('\n').Select(line => line.Trim())
                    .Where(line => line.Length > 0 && !line.StartsWith("DELETE FROM \"__EFMigrationsHistory\"", StringComparison.Ordinal)
                        && !line.StartsWith("WHERE \"MigrationId\"", StringComparison.Ordinal)
                        && !line.StartsWith("BEGIN TRANSACTION", StringComparison.Ordinal)
                        && !line.StartsWith("COMMIT", StringComparison.Ordinal)));
            migrator.Migrate(PreviousMigration);
        }

        Assert.Equal(before, Shape(dir.Paths.DatabaseFile));
    }

    private static TempDataDir AtThePreviousMigration()
    {
        var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.StateDirectory);
        using var db = new JasonDbContext(JasonDbContext.CreateOptions(dir.Paths.DatabaseFile));
        db.GetInfrastructure().GetRequiredService<IMigrator>().Migrate(PreviousMigration);
        return dir;
    }

    /// <summary>Every table, index and trigger of the database by its definition, and every column of every table.</summary>
    private static List<string> Shape(string file)
    {
        // No pooling: the migrator opens this file next, and a pooled connection would hold it on a machine that locks it.
        using var connection = new SqliteConnection(JasonDbContext.ConnectionString(file) + ";Pooling=False");
        connection.Open();
        var shape = Rows(connection, "SELECT type || ' ' || tbl_name || '.' || name || ' | ' || sql FROM sqlite_master WHERE sql IS NOT NULL AND substr(name, 1, 4) <> '__EF'");
        foreach (var table in Rows(connection, "SELECT name FROM sqlite_master WHERE type = 'table' AND substr(name, 1, 7) <> 'sqlite_' AND substr(name, 1, 4) <> '__EF'"))
        {
            shape.AddRange(Rows(connection, $"SELECT '{table}.' || name || ' ' || type || ' ' || \"notnull\" || ' ' || ifnull(dflt_value, '') FROM pragma_table_info('{table}')"));
        }

        shape.Sort(StringComparer.Ordinal);
        return shape;
    }

    private static List<string> Rows(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
        {
            rows.Add(Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture)!);
        }

        return rows;
    }
}
