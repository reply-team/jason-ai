using System.Globalization;
using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Persistence;

/// <summary>
/// A work item's entry is one nullable column of <c>work_items</c>, with no default. An installation upgraded from the
/// migration before keeps every item it had, and none of them carries an entry: the upgrade rewrites no row and reads no
/// context. <c>Down</c> takes the column away in place and nothing more, so the trigger that freezes a finished item's
/// result and the index that allows one live attempt survive it.
/// </summary>
public class WorkItemEntryMigrationTests
{
    private const string PreviousMigration = "20261116053811_DecisionThread";

    private const string ThisMigration = "20261122055316_WorkItemEntry";

    [Fact]
    [Critical(Family.P2, BrokenUntouched.Migration)]
    public void An_upgraded_item_keeps_its_row_and_carries_no_entry()
    {
        using var dir = AtThePreviousMigration();
        using (var connection = Open(dir))
        {
            Execute(
                connection,
                """
                INSERT INTO campaigns (public_id, name, project_id, status, context_json, created_at, updated_at)
                SELECT 'cmp_old', 'Autumn outreach', id, 'active', '{}', '2026-11-01 09:00:00', '2026-11-01 09:00:00'
                FROM projects ORDER BY id LIMIT 1;
                INSERT INTO work_items (public_id, campaign_id, kind, role, status, priority, created_by_type, created_by_id,
                                        context_json, attempt_count, created_at, updated_at, lineage_state)
                VALUES ('wi_checkin', 1, 'ai_role', 'manager', 'created', 0, 'system', 'dispatcher',
                        '{"review_intent":"scheduled","entry":"start"}', 0, '2026-11-01 09:00:00', '2026-11-01 09:00:00', 'root'),
                       ('wi_research', 1, 'ai_role', 'researcher', 'succeeded', 0, 'human', 'dana',
                        '{"goal":"find the decision maker"}', 1, '2026-11-01 09:00:00', '2026-11-01 09:00:00', 'root');
                """);
        }

        SqliteConnection.ClearAllPools();
        new DatabaseMigrator(dir.Paths).Migrate();

        using var after = Open(dir);

        // A context that happens to hold a key of that name is a context: nothing reads it to find an entry.
        Assert.Equal(
            ["wi_checkin manager - {\"review_intent\":\"scheduled\",\"entry\":\"start\"}", "wi_research researcher - {\"goal\":\"find the decision maker\"}"],
            Rows(after, "SELECT public_id || ' ' || role || ' ' || ifnull(entry, '-') || ' ' || context_json FROM work_items ORDER BY public_id"));
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
            ["work_items.entry TEXT 0 "],
            up.Except(before).Where(line => !line.StartsWith("table work_items.work_items | ", StringComparison.Ordinal)));
        Assert.Equal(
            ["table work_items.work_items | "],
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
