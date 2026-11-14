using System.Globalization;
using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Persistence;

/// <summary>
/// Whether a contact is cold, warm or waits on a verdict is read at the claim, inside its write transaction, from the
/// campaign's verdicts on one thread and the one that replaced each under its key. The migration adds one partial
/// expression index for those reads and nothing else: the journal's columns, its other indexes and its append-only
/// triggers are as they were, and <c>Down</c> takes the index away again and nothing more. The reads' plans are pinned by
/// <c>WarmthPlanTests</c>.
/// </summary>
public class JournalVerdictsByThreadMigrationTests
{
    private const string PreviousMigration = "20261113021003_JournalByThread";

    private const string ThisMigration = "20261114031653_JournalVerdictsByThread";

    private const string Index = "ix_journal_campaign_verdict_thread";

    [Fact]
    public void Up_adds_the_verdicts_thread_index_alone()
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
            $"index journal.{Index} | CREATE INDEX {Index} ON journal (campaign_id, json_extract(new_json, '$.thread'), key) WHERE kind = 'reply_classified'",
            added);
        Assert.Empty(before.Except(after));
    }

    [Fact]
    public void Down_drops_the_verdicts_thread_index_and_leaves_the_rest_as_it_stood()
    {
        using var dir = AtThePreviousMigration();
        var before = Shape(dir.Paths.DatabaseFile);
        new DatabaseMigrator(dir.Paths).Migrate();

        SqliteConnection.ClearAllPools();
        using (var db = new JasonDbContext(JasonDbContext.CreateOptions(dir.Paths.DatabaseFile)))
        {
            db.GetInfrastructure().GetRequiredService<IMigrator>().Migrate(PreviousMigration);
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
