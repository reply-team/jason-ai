using System.Globalization;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Contacts;
using Jason.Runtime.Persistence;
using Jason.Runtime.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Persistence;

/// <summary>
/// Which work items of a campaign name a send — its cancels and its reads — is asked by every touch that counts, the line's
/// root, the hold and the people list, each time for one send. The migration adds one expression index over the send an
/// item's input names, the campaign and the operation, and nothing else; the index is the probe's own wording
/// (<see cref="SendProbe"/>), so a query that probes in other words is a scan again, and the plans of the hot reads are held
/// here to seek by all three of its columns.
/// </summary>
public class SendProbeIndexMigrationTests
{
    private const string PreviousMigration = "20261124042822_DeferredSendReads";

    private const string ThisMigration = "20261124072351_SendProbeIndex";

    /// <summary>The seek on all three of the index's columns, as SQLite's plan names it.</summary>
    private static readonly string FullSeek = $"USING INDEX {SendProbe.Index} (<expr>=? AND campaign_id=? AND operation=?)";

    [Fact]
    [Critical(Family.P2, BrokenUntouched.Migration)]
    public void Up_adds_the_send_probe_index_alone()
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
        Assert.StartsWith($"index work_items.{SendProbe.Index} | ", added, StringComparison.Ordinal);
        Assert.Empty(before.Except(after));
    }

    [Fact]
    [Critical(Family.P2, BrokenUntouched.Migration)]
    public void Down_drops_the_send_probe_index_and_leaves_the_rest_as_it_stood()
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
    /// A context stored with the column's own check turned off, text that is not JSON, neither refuses the migration nor a
    /// later write of its row: the index reads the context only where it is JSON, and such a context names no send.
    /// </summary>
    [Fact]
    [Critical(Family.P2, BrokenUntouched.Migration)]
    public async Task A_context_that_is_not_json_neither_refuses_the_migration_nor_a_later_write()
    {
        using var dir = AtThePreviousMigration();
        string item;
        await using (var db = new JasonDbContext(JasonDbContext.CreateOptions(dir.Paths.DatabaseFile)))
        {
            var campaign = WorkItemFactory.NewCampaign();
            db.Campaigns.Add(campaign);
            var send = WorkItemFactory.NewProviderOp(campaign, "message.cancel");
            db.WorkItems.Add(send);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            item = send.PublicId;
        }

        SqliteConnection.ClearAllPools();
        using (var connection = new SqliteConnection(JasonDbContext.ConnectionString(dir.Paths.DatabaseFile) + ";Pooling=False"))
        {
            connection.Open();
            Execute(connection, "PRAGMA ignore_check_constraints = ON", item);
            Execute(connection, "UPDATE work_items SET context_json = '{\"input\":{\"send\":\"wi_' WHERE public_id = $item", item);
        }

        using (var db = new JasonDbContext(JasonDbContext.CreateOptions(dir.Paths.DatabaseFile)))
        {
            db.GetInfrastructure().GetRequiredService<IMigrator>().Migrate(ThisMigration);
        }

        SqliteConnection.ClearAllPools();
        using (var connection = new SqliteConnection(JasonDbContext.ConnectionString(dir.Paths.DatabaseFile) + ";Pooling=False"))
        {
            connection.Open();
            Execute(connection, "PRAGMA ignore_check_constraints = ON", item);
            Execute(connection, "UPDATE work_items SET context_json = '{\"input\":' WHERE public_id = $item", item);
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT count(*) FROM work_items AS k INDEXED BY {SendProbe.Index} WHERE k.public_id = $item AND {SendProbe.Of("k")} IS NULL";
            command.Parameters.AddWithValue("$item", item);
            Assert.Equal(1L, (long)command.ExecuteScalar()!);
        }
    }

    /// <summary>
    /// A database migrated from nothing holds the index, over the campaign, the operation and the probe's expression in the
    /// probe's own words: an expression index is taken only by a query that writes the same expression, so the day the
    /// wording changes without a migration that indexes the new one, this goes red.
    /// </summary>
    [Fact]
    [Critical(Family.P2, BrokenUntouched.Migration)]
    public void A_database_migrated_from_nothing_indexes_the_probe_s_own_wording()
    {
        using var database = new TestDatabase();
        using var connection = new SqliteConnection($"Data Source={database.File};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT tbl_name, sql FROM sqlite_master WHERE type = 'index' AND name = $name";
        command.Parameters.AddWithValue("$name", SendProbe.Index);
        using var reader = command.ExecuteReader();

        Assert.True(reader.Read(), $"A database migrated from nothing has no index {SendProbe.Index}.");
        Assert.Equal("work_items", reader.GetString(0));
        Assert.Equal($"CREATE INDEX {SendProbe.Index} ON work_items ({SendProbe.Indexed}, campaign_id, operation)", reader.GetString(1));
    }

    /// <summary>
    /// The hot reads that ask which items name a send seek the index by the send, the campaign and the operation, for each
    /// take-back and read they look for: the claim's count of touches, the line's root, the people page's touches, the line
    /// root's question of a take-back, the hold's reading of Jason's own move and the read of the sends a provider holds.
    /// They take the index only because the probe compares with the send's id stripped of its column's affinity by the unary
    /// plus, which <see cref="Without_the_unary_plus_the_index_is_not_taken"/> shows.
    /// </summary>
    [Theory]
    [Critical(Family.P2, BrokenUntouched.Migration)]
    [MemberData(nameof(HotReads))]
    public void The_hot_reads_seek_the_send_probe_index_on_all_three_columns_by_the_unary_plus(string read, string sql)
    {
        var probes = ProbeSteps(sql);

        Assert.True(
            probes.Count > 0 && probes.All(step => step.EndsWith(FullSeek, StringComparison.Ordinal)),
            $"{read}: an item that names a send is not found by the index on all three columns; the plan's probes are: {string.Join(" | ", probes)}");
    }

    /// <summary>
    /// Why the probe writes <c>= +</c>: the send's id is a column of TEXT affinity and the index's first column an expression
    /// with none, and SQLite does not seek an index column whose affinity the comparison does not match, so without the plus
    /// the index is not taken and every take-back and read of the campaign is read.
    /// </summary>
    [Theory]
    [Critical(Family.P2, BrokenUntouched.Migration)]
    [MemberData(nameof(HotReads))]
    public void Without_the_unary_plus_the_index_is_not_taken(string read, string sql)
    {
        Assert.Contains("= +", sql, StringComparison.Ordinal);
        var probes = ProbeSteps(sql.Replace("= +", "= ", StringComparison.Ordinal));

        Assert.True(
            probes.Count > 0 && probes.All(step => !step.Contains(SendProbe.Index, StringComparison.Ordinal)),
            $"{read}: without the unary plus the plan still takes the index, and the plus is no longer what makes it: {string.Join(" | ", probes)}");
    }

    public static TheoryData<string, string> HotReads() => new()
    {
        { "touch count", LineTouches.OthersCountSql.Replace("{0}", "1", StringComparison.Ordinal).Replace("{1}", "1", StringComparison.Ordinal).Replace("{2}", "0", StringComparison.Ordinal) },
        {
            "line root",
            LineRoots.RootSql.Replace("{0}", "1", StringComparison.Ordinal).Replace("{1}", "1", StringComparison.Ordinal).Replace("{2}", "0", StringComparison.Ordinal)
                .Replace("{3}", "'message.send'", StringComparison.Ordinal).Replace("{4}", "'email'", StringComparison.Ordinal)
        },
        { "people page", CampaignPeople.TouchesSql.Replace("{0}", "'[1]'", StringComparison.Ordinal).Replace("{1}", "'[1]'", StringComparison.Ordinal) },
        { "taken back", LineRoots.TakenBackCountSql.Replace("{0}", "1", StringComparison.Ordinal).Replace("{1}", "'wi_1'", StringComparison.Ordinal) },
        {
            "hold's own move",
            Bind(LineHold.OwnMoveSql, "1", "1", "'[\"message.send\"]'", "'th-1'", "'conversation.reply'", "'2026-11-01T00:00:00Z'", "'2026-11-24T00:00:00Z'")
        },
        {
            "held sends",
            Bind(
                HeldSends.Sql, "'[\"message.send\"]'", "'2026-11-24T00:00:00Z'", "'message.cancel'", "'message.status'", "'[]'", "'[]'", "'[]'", "1",
                "NULL", "'to_come'", "NULL", "NULL", "NULL")
        },
    };

    /// <summary>The read with its placeholders, <c>{0}</c> onwards, bound to the literals given in order.</summary>
    private static string Bind(string sql, params string[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            sql = sql.Replace("{" + i.ToString(CultureInfo.InvariantCulture) + "}", values[i], StringComparison.Ordinal);
        }

        return sql;
    }

    /// <summary>The aliases the reads give the items that name a send: the take-backs and reads, and the latest read.</summary>
    private static readonly string[] Probes = ["k", "g", "r"];

    /// <summary>The steps of a read's plan that look for the items naming a send.</summary>
    private static List<string> ProbeSteps(string sql)
    {
        using var database = new TestDatabase();
        using var connection = new SqliteConnection($"Data Source={database.File};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN " + sql;
        using var reader = command.ExecuteReader();
        var steps = new List<string>();
        while (reader.Read())
        {
            var step = reader.GetString(reader.GetOrdinal("detail"));
            if (Probes.Any(probe => step.StartsWith("SEARCH " + probe + " ", StringComparison.Ordinal) || step.StartsWith("SCAN " + probe, StringComparison.Ordinal)))
            {
                steps.Add(step);
            }
        }

        return steps;
    }

    private static void Execute(SqliteConnection connection, string sql, string item)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$item", item);
        command.ExecuteNonQuery();
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
