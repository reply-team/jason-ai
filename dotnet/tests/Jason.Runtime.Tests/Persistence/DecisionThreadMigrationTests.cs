using System.Globalization;
using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Persistence;

/// <summary>
/// The thread a question is about is two nullable columns of <c>decisions</c>. An installation upgraded from the
/// migration before keeps every question it had, and each names the thread by the rule a raise follows from now on: a
/// question of the runtime's own responder names the thread its reserved key records, pending or answered; every other
/// question names none. No stored context fails the upgrade: one the rule cannot read leaves its row null. <c>Down</c>
/// takes the two columns away and nothing more.
/// </summary>
public class DecisionThreadMigrationTests
{
    private const string PreviousMigration = "20261116022841_ProviderSends";

    private const string ThisMigration = "20261116053811_DecisionThread";

    [Fact]
    [Critical(Family.P2, BrokenUntouched.Migration)]
    public void An_upgraded_question_names_its_thread_only_where_the_runtimes_own_responder_asked_it()
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
                VALUES ('wi_pending', 1, 'ai_role', 'responder', 'succeeded', 0, 'system', 'dispatcher',
                        '{"inbound_reply":{"plugin":"mailbox","thread":"501","channel":"email"}}', 1, '2026-11-01 09:00:00', '2026-11-01 09:00:00', 'root'),
                       ('wi_answered', 1, 'ai_role', 'responder', 'succeeded', 0, 'system', 'dispatcher',
                        '{"inbound_reply":{"plugin":"mailbox","thread":"502"}}', 1, '2026-11-01 09:00:00', '2026-11-01 09:00:00', 'root'),
                       ('wi_person', 1, 'ai_role', 'responder', 'succeeded', 0, 'human', 'dana',
                        '{"inbound_reply":{"plugin":"mailbox","thread":"503"}}', 1, '2026-11-01 09:00:00', '2026-11-01 09:00:00', 'root'),
                       ('wi_manager', 1, 'ai_role', 'manager', 'succeeded', 0, 'system', 'dispatcher',
                        '{"inbound_reply":{"plugin":"mailbox","thread":"504"}}', 1, '2026-11-01 09:00:00', '2026-11-01 09:00:00', 'root'),
                       ('wi_nokey', 1, 'ai_role', 'responder', 'succeeded', 0, 'system', 'dispatcher',
                        '{"trigger":"inbound_reply_observed"}', 1, '2026-11-01 09:00:00', '2026-11-01 09:00:00', 'root'),
                       ('wi_number', 1, 'ai_role', 'responder', 'succeeded', 0, 'system', 'dispatcher',
                        '{"inbound_reply":{"plugin":"mailbox","thread":505}}', 1, '2026-11-01 09:00:00', '2026-11-01 09:00:00', 'root');
                INSERT INTO attempts (public_id, work_item_id, number, command, status, context_snapshot_json, claimed_at, lock_until, finished_at)
                SELECT 'att_' || substr(public_id, 4), id, 1, 'ai_role', 'succeeded', '{}', '2026-11-01 09:10:00', '2026-11-01 09:40:00', '2026-11-01 09:20:00'
                FROM work_items ORDER BY id;
                INSERT INTO decisions (public_id, campaign_id, work_item_id, attempt_id, question, status, raised_at,
                                       answer, answered_at, answered_by_type, answered_by_id)
                SELECT 'dec_' || substr(public_id, 4), 1, id, id, 'Close the thread?',
                       CASE public_id WHEN 'wi_answered' THEN 'answered' ELSE 'pending' END, '2026-11-01 09:15:00',
                       CASE public_id WHEN 'wi_answered' THEN 'close it' END,
                       CASE public_id WHEN 'wi_answered' THEN '2026-11-01 10:00:00' END,
                       CASE public_id WHEN 'wi_answered' THEN 'human' END,
                       CASE public_id WHEN 'wi_answered' THEN 'dana' END
                FROM work_items ORDER BY id;
                """);
        }

        SqliteConnection.ClearAllPools();
        new DatabaseMigrator(dir.Paths).Migrate();

        using var after = Open(dir);
        Assert.Equal(
            [
                "dec_answered mailbox 502",
                "dec_manager - -",
                "dec_nokey - -",
                "dec_number - -",
                "dec_pending mailbox 501",
                "dec_person - -",
            ],
            Rows(after, "SELECT public_id || ' ' || ifnull(thread_plugin_id, '-') || ' ' || ifnull(thread_id, '-') FROM decisions ORDER BY public_id"));
        Assert.Equal(
            ["dec_answered answered", "dec_pending pending"],
            Rows(after, "SELECT public_id || ' ' || status FROM decisions WHERE thread_id IS NOT NULL ORDER BY public_id"));
    }

    [Fact]
    [Critical(Family.P2, BrokenUntouched.Migration)]
    public void Down_drops_the_two_columns_and_leaves_the_rest_as_it_stood()
    {
        using var dir = AtThePreviousMigration();
        var before = Shape(dir.Paths.DatabaseFile);

        SqliteConnection.ClearAllPools();
        Migrate(dir, ThisMigration);
        // The two columns, and the table's own definition, which every added column rewrites; nothing else.
        var up = Shape(dir.Paths.DatabaseFile);
        Assert.Equal(
            ["decisions.thread_id TEXT 0 ", "decisions.thread_plugin_id TEXT 0 "],
            up.Except(before).Where(line => !line.StartsWith("table decisions.decisions | ", StringComparison.Ordinal)));
        Assert.Equal(
            ["table decisions.decisions | "],
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
