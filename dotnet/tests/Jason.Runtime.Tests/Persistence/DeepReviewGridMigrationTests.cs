using System.Globalization;
using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Persistence;

/// <summary>
/// The deep review's grid is one nullable column of <c>campaigns</c>. An installation upgraded from the migration before
/// gives every campaign already live a grid one day after the migration, the default interval, so that no live campaign is
/// due a deep review at once and none is never due; a draft, a paused and an archived campaign keep none, since going live
/// writes it. <c>Down</c> takes the column away in place and nothing more.
/// </summary>
public class DeepReviewGridMigrationTests
{
    private const string PreviousMigration = "20261122065647_LaunchLimitLifts";

    private const string ThisMigration = "20261122075940_DeepReviewGrid";

    [Fact]
    [Critical(Family.P2, BrokenUntouched.Migration)]
    public void An_active_campaign_gets_its_grid_a_day_after_the_upgrade_and_the_others_none()
    {
        using var dir = AtThePreviousMigration();
        using (var connection = Open(dir))
        {
            Execute(
                connection,
                """
                INSERT INTO campaigns (public_id, name, project_id, status, context_json, created_at, updated_at)
                SELECT 'cmp_' || status, 'Autumn ' || status, (SELECT id FROM projects ORDER BY id LIMIT 1), status, '{}',
                       '2026-11-01 09:00:00', '2026-11-01 09:00:00'
                FROM (SELECT 'active' AS status UNION ALL SELECT 'draft' UNION ALL SELECT 'paused' UNION ALL SELECT 'archived');
                """);
        }

        SqliteConnection.ClearAllPools();
        var before = DateTime.UtcNow;
        new DatabaseMigrator(dir.Paths).Migrate();
        var after = DateTime.UtcNow;

        using (var connection = Open(dir))
        {
            Assert.Equal(
                ["cmp_archived -", "cmp_draft -", "cmp_paused -"],
                Rows(connection, "SELECT public_id || ' ' || ifnull(deep_review_due_at, '-') FROM campaigns WHERE status <> 'active' ORDER BY public_id"));
        }

        // Read back through the model, as the summon reads it: a day after the migration, in UTC, to the second.
        var floor = new DateTime(before.Ticks - (before.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc).AddDays(1);
        var ceiling = new DateTime(after.Ticks - (after.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc).AddSeconds(1).AddDays(1);
        DateTime grid;
        using (var db = new JasonDbContext(JasonDbContext.CreateOptions(dir.Paths.DatabaseFile)))
        {
            grid = Assert.IsType<DateTime>(db.Campaigns.Single(c => c.PublicId == "cmp_active").DeepReviewDueAt);
            Assert.Equal(DateTimeKind.Utc, grid.Kind);
            Assert.InRange(grid, floor, ceiling);
        }

        // And as text: the backfill writes exactly what the model writes for a moment of campaigns. The model writes the
        // value it read into another campaign's anchor, and the lowest and highest a day after the migration could be into
        // two more; the backfill's text is the first byte for byte and lies between the other two as text, which only a
        // value in the model's own layout can.
        var raw = Moment(dir, "cmp_active", "deep_review_due_at");
        Assert.Equal(EfWritten(dir, "cmp_draft", grid), raw);
        var lowest = EfWritten(dir, "cmp_paused", floor);
        var highest = EfWritten(dir, "cmp_archived", ceiling);
        Assert.True(
            string.CompareOrdinal(lowest, raw) <= 0 && string.CompareOrdinal(raw, highest) <= 0,
            $"The backfill wrote '{raw}', which is not between '{lowest}' and '{highest}', a day after the migration as the model writes it.");
    }

    /// <summary>What the model writes for <paramref name="moment"/>: put into one campaign's anchor and read back as stored text.</summary>
    private static string EfWritten(TempDataDir dir, string campaign, DateTime moment)
    {
        using (var db = new JasonDbContext(JasonDbContext.CreateOptions(dir.Paths.DatabaseFile)))
        {
            db.Campaigns.Single(c => c.PublicId == campaign).ManagerReviewAnchor = moment;
            db.SaveChanges();
        }

        SqliteConnection.ClearAllPools();
        return Moment(dir, campaign, "manager_review_anchor");
    }

    /// <summary>One moment column of one campaign, as the text the database holds.</summary>
    private static string Moment(TempDataDir dir, string campaign, string column)
    {
        using var connection = Open(dir);
        return Assert.Single(Rows(connection, $"SELECT {column} FROM campaigns WHERE public_id = '{campaign}'"));
    }

    [Fact]
    [Critical(Family.P2, BrokenUntouched.Migration)]
    public void Down_drops_the_column_and_leaves_the_rest_as_it_stood()
    {
        using var dir = AtThePreviousMigration();
        var before = Shape(dir.Paths.DatabaseFile);

        SqliteConnection.ClearAllPools();
        Migrate(dir, ThisMigration);

        // The column, and the table's own definition, which every added column rewrites; nothing else.
        var up = Shape(dir.Paths.DatabaseFile);
        Assert.Equal(
            ["campaigns.deep_review_due_at TEXT 0 "],
            up.Except(before).Where(line => !line.StartsWith("table campaigns.campaigns | ", StringComparison.Ordinal)));
        Assert.Equal(
            ["table campaigns.campaigns | "],
            before.Except(up).Select(line => line[..line.IndexOf('|', StringComparison.Ordinal)] + "| "));

        Migrate(dir, PreviousMigration);
        Assert.Equal(before, Shape(dir.Paths.DatabaseFile));
    }

    private static TempDataDir AtThePreviousMigration()
    {
        var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.StateDirectory);
        Migrate(dir, PreviousMigration);
        return dir;
    }

    private static void Migrate(TempDataDir dir, string migration)
    {
        using var db = new JasonDbContext(JasonDbContext.CreateOptions(dir.Paths.DatabaseFile));
        db.GetInfrastructure().GetRequiredService<IMigrator>().Migrate(migration);
    }

    // No pooling: the migrator opens this file next, and a pooled connection would hold it on a machine that locks it.
    private static SqliteConnection Open(TempDataDir dir)
    {
        var connection = new SqliteConnection(JasonDbContext.ConnectionString(dir.Paths.DatabaseFile) + ";Pooling=False");
        connection.Open();
        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>Every table, index and trigger of the database by its definition, and every column of every table.</summary>
    private static List<string> Shape(string file)
    {
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
