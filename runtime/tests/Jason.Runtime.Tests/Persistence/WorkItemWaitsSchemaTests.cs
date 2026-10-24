using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Persistence;

/// <summary>
/// What a waiting work item waits for is kept in two tables: the wait, with its mode, its moments and how it closed, and
/// the items it named. An item has one open wait at a time, and the database is what says so. The status column of
/// <c>work_items</c> is text without a check, so <c>waiting</c> arrives without rebuilding the table.
/// </summary>
public class WorkItemWaitsSchemaTests
{
    private const string PreviousMigration = "20261022011335_ProjectsAndPreferences";

    private const string OneOpen = "ix_work_item_waits_one_open_per_item";

    /// <summary>An installation that upgrades keeps its work, waits for nothing, and has both tables and every column the wait needs, the provider block it may be parked on among them.</summary>
    [Fact]
    public void An_upgraded_database_keeps_its_work_and_has_the_wait_tables_empty()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.StateDirectory);
        var options = JasonDbContext.CreateOptions(dir.Paths.DatabaseFile);
        using (var db = new JasonDbContext(options))
        {
            db.GetInfrastructure().GetRequiredService<IMigrator>().Migrate(PreviousMigration);
        }

        Execute(
            dir.Paths.DatabaseFile,
            """
            INSERT INTO campaigns (public_id, name, status, context_json, created_at, updated_at, project_id)
            VALUES ('cmp_1', 'one', 'active', '{}', '2026-10-01 00:00:00', '2026-10-01 00:00:00', (SELECT id FROM projects WHERE is_default = 1));
            INSERT INTO work_items (public_id, campaign_id, kind, role, status, priority, created_by_type, context_json, attempt_count, created_at, updated_at, lineage_state)
            VALUES ('wi_old', 1, 'ai_role', 'planner', 'created', 0, 'human', '{}', 0, '2026-10-01 00:00:00', '2026-10-01 00:00:00', 'root');
            """);

        using (var db = new JasonDbContext(options))
        {
            db.Database.Migrate();
        }

        Assert.Equal(["wi_old|created"], Rows(dir.Paths.DatabaseFile, "SELECT public_id, status FROM work_items"));
        Assert.Equal(
            ["id", "work_item_id", "attempt_id", "mode", "until", "deadline", "status", "opened_at", "released_at", "release_reason", "resumed_by_attempt_id", "provider_block_id"],
            Rows(dir.Paths.DatabaseFile, "SELECT name FROM pragma_table_info('work_item_waits') ORDER BY cid"));
        Assert.Equal(
            ["work_item_id|1", "attempt_id|0", "mode|0", "until|0", "deadline|0", "status|1", "opened_at|1", "released_at|0", "release_reason|0", "resumed_by_attempt_id|0", "provider_block_id|0"],
            Rows(dir.Paths.DatabaseFile, "SELECT name, \"notnull\" FROM pragma_table_info('work_item_waits') WHERE name <> 'id' ORDER BY cid"));
        Assert.Equal(
            ["wait_id|1|1", "child_work_item_id|2|1", "position|0|1"],
            Rows(dir.Paths.DatabaseFile, "SELECT name, pk, \"notnull\" FROM pragma_table_info('work_item_wait_items') ORDER BY cid"));
        Assert.Equal(["0|0"], Rows(dir.Paths.DatabaseFile, "SELECT (SELECT COUNT(*) FROM work_item_waits), (SELECT COUNT(*) FROM work_item_wait_items)"));

        // Nothing a wait refers to goes away under it.
        Assert.Equal(
            ["attempts|attempt_id|RESTRICT", "provider_blocks|provider_block_id|RESTRICT", "attempts|resumed_by_attempt_id|RESTRICT", "work_items|work_item_id|RESTRICT"],
            Rows(dir.Paths.DatabaseFile, "SELECT \"table\", \"from\", on_delete FROM pragma_foreign_key_list('work_item_waits') ORDER BY \"from\""));
        Assert.Equal(
            ["work_item_waits|wait_id|RESTRICT", "work_items|child_work_item_id|RESTRICT"],
            Rows(dir.Paths.DatabaseFile, "SELECT \"table\", \"from\", on_delete FROM pragma_foreign_key_list('work_item_wait_items') ORDER BY \"table\""));
    }

    /// <summary>
    /// The indexes the resume step and the one-open rule stand on: one open wait per item, an item's waits by their
    /// status (a released wait is found by its item there), a block's waits by their status, and the named items by the
    /// item. No index by deadline: the resume step reaches the open waits through their waiting items. The open check-in
    /// index keeps its four statuses: a check-in never waits.
    /// </summary>
    [Fact]
    public void The_wait_indexes_are_the_ones_the_resume_step_reads_and_the_check_in_index_is_unchanged()
    {
        using var database = new TestDatabase();
        Assert.Equal(
            [
                "ix_work_item_waits_attempt_id|0|attempt_id",
                "ix_work_item_waits_one_open_per_item|1|work_item_id",
                "ix_work_item_waits_provider_block_id_status|0|provider_block_id,status",
                "ix_work_item_waits_resumed_by_attempt_id|0|resumed_by_attempt_id",
                "ix_work_item_waits_work_item_id_status|0|work_item_id,status",
            ],
            Rows(
                database.File,
                "SELECT il.name, il.\"unique\", (SELECT group_concat(name, ',') FROM (SELECT name FROM pragma_index_info(il.name) ORDER BY seqno)) "
                + "FROM pragma_index_list('work_item_waits') il WHERE il.origin = 'c' ORDER BY il.name"));
        Assert.Equal(
            ["ix_work_item_wait_items_child_work_item_id|child_work_item_id"],
            Rows(
                database.File,
                "SELECT il.name, (SELECT group_concat(name, ',') FROM pragma_index_info(il.name)) FROM pragma_index_list('work_item_wait_items') il WHERE il.origin = 'c'"));

        var oneOpen = Assert.Single(Rows(database.File, $"SELECT sql FROM sqlite_master WHERE name = '{OneOpen}'"));
        Assert.EndsWith("WHERE status = 'open'", oneOpen, StringComparison.Ordinal);

        var checkIn = Assert.Single(Rows(database.File, "SELECT sql FROM sqlite_master WHERE name = 'ix_work_items_one_open_check_in_per_campaign'"));
        Assert.True(
            checkIn.EndsWith("WHERE role = 'manager' AND created_by_type = 'system' AND status IN ('created','scheduled','processing','awaiting_approval')", StringComparison.Ordinal),
            $"The open check-in index changed: {checkIn}");

        using var db = database.Open();
        var model = db.Model.FindEntityType(typeof(WorkItemWait))!.GetIndexes().Single(index => index.GetDatabaseName() == OneOpen);
        Assert.True(model.IsUnique);
        Assert.Equal("status = 'open'", model.GetFilter());
    }

    /// <summary>
    /// A second open wait for the same item is refused by the database; once the first is closed, released or cancelled
    /// or expired, the item may wait again, and another item may wait beside it. A wait names an item once.
    /// </summary>
    [Fact]
    public void An_item_has_one_open_wait_and_may_wait_again_once_it_is_closed()
    {
        using var database = new TestDatabase();
        Execute(
            database.File,
            """
            INSERT INTO campaigns (public_id, name, status, context_json, created_at, updated_at, project_id)
            VALUES ('cmp_1', 'one', 'active', '{}', '2026-10-01 00:00:00', '2026-10-01 00:00:00', (SELECT id FROM projects WHERE is_default = 1));
            INSERT INTO work_items (public_id, campaign_id, kind, role, status, priority, created_by_type, context_json, attempt_count, created_at, updated_at, lineage_state) VALUES
              ('wi_parent', 1, 'ai_role', 'planner', 'waiting', 0, 'human', '{}', 0, '2026-10-01 00:00:00', '2026-10-01 00:00:00', 'root'),
              ('wi_other', 1, 'ai_role', 'planner', 'waiting', 0, 'human', '{}', 0, '2026-10-01 00:00:00', '2026-10-01 00:00:00', 'root'),
              ('wi_child', 1, 'ai_role', 'researcher', 'created', 0, 'human', '{}', 0, '2026-10-01 00:00:00', '2026-10-01 00:00:00', 'root');
            INSERT INTO work_item_waits (work_item_id, mode, deadline, status, opened_at) VALUES
              (1, 'all', '2026-10-15 00:00:00', 'open', '2026-10-01 00:00:00'),
              (2, NULL, '2026-10-02 00:00:00', 'open', '2026-10-01 00:00:00');
            INSERT INTO work_item_wait_items (wait_id, child_work_item_id, position) VALUES (1, 3, 0);
            """);

        var second = Assert.Throws<SqliteException>(() => Execute(
            database.File,
            "INSERT INTO work_item_waits (work_item_id, mode, deadline, status, opened_at) VALUES (1, 'any', '2026-10-15 00:00:00', 'open', '2026-10-01 00:00:01');"));
        Assert.Contains("UNIQUE", second.Message, StringComparison.Ordinal);
        var twice = Assert.Throws<SqliteException>(() => Execute(database.File, "INSERT INTO work_item_wait_items (wait_id, child_work_item_id, position) VALUES (1, 3, 1);"));
        Assert.Contains("UNIQUE", twice.Message, StringComparison.Ordinal);

        Execute(
            database.File,
            """
            UPDATE work_item_waits SET status = 'released', released_at = '2026-10-03 00:00:00', release_reason = 'children_ended' WHERE id = 1;
            INSERT INTO work_item_waits (work_item_id, mode, deadline, status, opened_at) VALUES
              (1, 'any', '2026-10-15 00:00:00', 'cancelled', '2026-10-04 00:00:00'),
              (1, NULL, '2026-10-16 00:00:00', 'open', '2026-10-05 00:00:00');
            """);

        Assert.Equal(
            ["1|1|released|children_ended", "2|2|open|", "3|1|cancelled|", "4|1|open|"],
            Rows(database.File, "SELECT id, work_item_id, status, release_reason FROM work_item_waits ORDER BY id"));
    }

    /// <summary>Each row a query returns, its columns joined by a bar.</summary>
    private static List<string> Rows(string databaseFile, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={databaseFile}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
        {
            rows.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(index => Convert.ToString(reader.GetValue(index), System.Globalization.CultureInfo.InvariantCulture))));
        }

        return rows;
    }

    private static void Execute(string databaseFile, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={databaseFile};Foreign Keys=True");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
