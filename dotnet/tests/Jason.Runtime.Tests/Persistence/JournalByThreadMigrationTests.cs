using System.Globalization;
using Jason.Runtime.Persistence;
using Jason.Runtime.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Persistence;

/// <summary>
/// Where a campaign saw a thread is read at the claim, inside its write transaction, by the campaign and the thread an
/// observed-reply line's body names. The migration adds one partial expression index for that read and nothing else: the
/// journal's columns, its other indexes and its append-only triggers are as they were, and <c>Down</c> takes the index
/// away again and nothing more.
/// </summary>
public class JournalByThreadMigrationTests
{
    private const string PreviousMigration = "20261113000729_ChannelRoutes";

    private const string Index = "ix_journal_campaign_thread";

    [Fact]
    public void Up_adds_the_thread_index_alone()
    {
        using var dir = AtThePreviousMigration();
        var before = Shape(dir.Paths.DatabaseFile);

        new DatabaseMigrator(dir.Paths).Migrate();

        var after = Shape(dir.Paths.DatabaseFile);
        var added = Assert.Single(after.Except(before));
        Assert.Equal(
            $"index journal.{Index} | CREATE INDEX {Index} ON journal (campaign_id, json_extract(new_json, '$.thread')) WHERE kind = 'inbound_reply_observed'",
            added);
        Assert.Empty(before.Except(after));
    }

    [Fact]
    public void Down_drops_the_thread_index_and_leaves_the_rest_as_it_stood()
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

    /// <summary>
    /// The read the claim makes — the sightings of one thread in one campaign — seeks by the index rather than visiting
    /// every observed-reply line of the campaign. Without the index the plan names the kind's index ordered by the key, or
    /// a scan.
    /// </summary>
    [Fact]
    public void The_claim_s_read_of_where_a_thread_was_seen_seeks_by_the_index()
    {
        using var database = new TestDatabase();
        using var connection = new SqliteConnection($"Data Source={database.File}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN " + ThreadSightings.OfThread.Replace("{0}", "$campaign", StringComparison.Ordinal).Replace("{1}", "$thread", StringComparison.Ordinal);
        command.Parameters.AddWithValue("$campaign", 1);
        command.Parameters.AddWithValue("$thread", "th-1");
        using var reader = command.ExecuteReader();
        var plan = new List<string>();
        while (reader.Read())
        {
            plan.Add(reader.GetString(reader.GetOrdinal("detail")));
        }

        Assert.True(
            plan.Any(step => step.Contains($"INDEX {Index} (campaign_id=? AND <expr>=?)", StringComparison.Ordinal)),
            $"The read of where a thread was seen does not seek by its index; the plan is: {string.Join(" | ", plan)}");
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
