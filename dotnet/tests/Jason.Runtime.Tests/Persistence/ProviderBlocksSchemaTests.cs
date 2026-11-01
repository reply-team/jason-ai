using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Persistence;

/// <summary>
/// A provider block is a row of its own, and the work it holds waits on it through the wait table. One block is open per
/// binding, scope and sender, and the database is what says so. The wait's reference is added in place, so the wait table
/// keeps its rows, its columns in their order and the indexes the resume step reads.
/// </summary>
public class ProviderBlocksSchemaTests
{
    private const string PreviousMigration = "20261024123426_WorkItemWaits";

    /// <summary>
    /// An installation that upgrades keeps its waits as they were, each waiting on no block, and has the block table empty
    /// with every column a block needs.
    /// </summary>
    [Fact]
    public void An_upgraded_database_keeps_its_waits_and_has_the_block_table_empty()
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
            VALUES ('wi_old', 1, 'ai_role', 'planner', 'waiting', 0, 'human', '{}', 0, '2026-10-01 00:00:00', '2026-10-01 00:00:00', 'root');
            INSERT INTO work_item_waits (work_item_id, mode, deadline, status, opened_at) VALUES (1, NULL, '2026-10-02 00:00:00', 'open', '2026-10-01 00:00:00');
            """);

        using (var db = new JasonDbContext(options))
        {
            db.Database.Migrate();
        }

        Assert.Equal(["1|1|open|2026-10-02 00:00:00|"], Rows(dir.Paths.DatabaseFile, "SELECT id, work_item_id, status, deadline, provider_block_id FROM work_item_waits"));
        Assert.Equal(
            ["id", "work_item_id", "attempt_id", "mode", "until", "deadline", "status", "opened_at", "released_at", "release_reason", "resumed_by_attempt_id", "provider_block_id"],
            Rows(dir.Paths.DatabaseFile, "SELECT name FROM pragma_table_info('work_item_waits') ORDER BY cid"));
        Assert.Equal(
            [
                "public_id|1", "plugin_id|1", "binding_identity|1", "scope|1", "sender_external_id|0", "reason|1", "link|0", "clears_at|0",
                "status|1", "opened_at|1", "opened_by_attempt_id|1", "last_seen_at|1", "seen_count|1", "next_trial_at|1",
                "trial_work_item_id|0", "cleared_at|0", "cleared_by_attempt_id|0", "cleared_by_id|0", "cleared_by_type|0",
            ],
            Rows(dir.Paths.DatabaseFile, "SELECT name, \"notnull\" FROM pragma_table_info('provider_blocks') WHERE name <> 'id' ORDER BY cid"));
        Assert.Equal(["0"], Rows(dir.Paths.DatabaseFile, "SELECT COUNT(*) FROM provider_blocks"));

        // Nothing a block or a wait refers to goes away under it, and the wait table was not rebuilt to say so.
        Assert.Equal(
            ["attempts|cleared_by_attempt_id|RESTRICT", "attempts|opened_by_attempt_id|RESTRICT", "work_items|trial_work_item_id|RESTRICT"],
            Rows(dir.Paths.DatabaseFile, "SELECT \"table\", \"from\", on_delete FROM pragma_foreign_key_list('provider_blocks') ORDER BY \"from\""));
        Assert.Equal(
            ["attempts|attempt_id|RESTRICT", "provider_blocks|provider_block_id|RESTRICT", "attempts|resumed_by_attempt_id|RESTRICT", "work_items|work_item_id|RESTRICT"],
            Rows(dir.Paths.DatabaseFile, "SELECT \"table\", \"from\", on_delete FROM pragma_foreign_key_list('work_item_waits') ORDER BY \"from\""));
        Assert.Equal(["ok"], Rows(dir.Paths.DatabaseFile, "PRAGMA integrity_check"));
    }

    /// <summary>
    /// An installation that upgrades from the release before the listing's index keeps every block as it was, open and
    /// cleared alike, and the index is built over them in the same start.
    /// </summary>
    [Fact]
    public void An_upgraded_database_keeps_its_blocks_and_has_them_in_the_listing_index()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.StateDirectory);
        var options = JasonDbContext.CreateOptions(dir.Paths.DatabaseFile);
        using (var db = new JasonDbContext(options))
        {
            db.GetInfrastructure().GetRequiredService<IMigrator>().Migrate("20261025010407_ProviderBlockClearedBy");
        }

        Execute(dir.Paths.DatabaseFile, Seed);
        Execute(
            dir.Paths.DatabaseFile,
            """
            INSERT INTO provider_blocks (public_id, plugin_id, binding_identity, scope, sender_external_id, reason, status, opened_at, opened_by_attempt_id, last_seen_at, seen_count, next_trial_at, cleared_at, cleared_by_type, cleared_by_id) VALUES
              ('blk_1', 'fake', 'sha256:a', 'account', NULL, 'billing', 'cleared', '2026-10-01 00:00:00', 1, '2026-10-01 00:00:00', 1, '2026-10-01 00:15:00', '2026-10-01 01:00:00', 'human', 'marta'),
              ('blk_2', 'fake', 'sha256:a', 'sender', 'snd_1', 'limit', 'open', '2026-10-01 02:00:00', 1, '2026-10-01 02:00:00', 3, '2026-10-01 02:15:00', NULL, NULL, NULL),
              ('blk_3', 'other', 'sha256:b', 'account', NULL, 'channel', 'open', '2026-10-01 03:00:00', 1, '2026-10-01 03:00:00', 1, '2026-10-01 03:15:00', NULL, NULL, NULL);
            INSERT INTO work_item_waits (work_item_id, status, opened_at, provider_block_id) VALUES (1, 'open', '2026-10-01 02:00:00', 2);
            """);
        const string Everything = "SELECT * FROM provider_blocks ORDER BY id";
        var before = Rows(dir.Paths.DatabaseFile, Everything);

        using (var db = new JasonDbContext(options))
        {
            db.Database.Migrate();
        }

        Assert.Equal(before, Rows(dir.Paths.DatabaseFile, Everything));
        Assert.Contains("ix_provider_blocks_status_opened_at_public_id|0|status,opened_at,public_id", Rows(dir.Paths.DatabaseFile, IndexList("provider_blocks")));
        Assert.Equal(
            ["blk_3", "blk_2"],
            Rows(dir.Paths.DatabaseFile, "SELECT public_id FROM provider_blocks INDEXED BY ix_provider_blocks_status_opened_at_public_id WHERE status = 'open' ORDER BY opened_at DESC, public_id DESC"));
        Assert.Equal(["1|open|2"], Rows(dir.Paths.DatabaseFile, "SELECT work_item_id, status, provider_block_id FROM work_item_waits"));
        Assert.Equal(["ok"], Rows(dir.Paths.DatabaseFile, "PRAGMA integrity_check"));
    }

    /// <summary>
    /// The indexes the trial, the listing and the clear stand on: a block's waits by how they stand, the blocks by how they
    /// stand and when their trial is due, the blocks by how they stand and when they opened, and the one-open rule in its
    /// two halves, the account's and a sender's.
    /// </summary>
    [Fact]
    public void The_block_indexes_are_the_ones_the_resume_step_the_listing_and_the_one_open_rule_read()
    {
        using var database = new TestDatabase();
        Assert.Equal(
            [
                "ix_provider_blocks_cleared_by_attempt_id|0|cleared_by_attempt_id",
                "ix_provider_blocks_one_open_per_account|1|plugin_id,binding_identity",
                "ix_provider_blocks_one_open_per_sender|1|plugin_id,binding_identity,sender_external_id",
                "ix_provider_blocks_opened_by_attempt_id|0|opened_by_attempt_id",
                "ix_provider_blocks_public_id|1|public_id",
                "ix_provider_blocks_status_next_trial_at|0|status,next_trial_at",
                "ix_provider_blocks_status_opened_at_public_id|0|status,opened_at,public_id",
                "ix_provider_blocks_trial_work_item_id|0|trial_work_item_id",
            ],
            Rows(database.File, IndexList("provider_blocks")));
        Assert.Contains("ix_work_item_waits_provider_block_id_status|0|provider_block_id,status", Rows(database.File, IndexList("work_item_waits")));
        Assert.Contains("ix_journal_kind_key_campaign_blocks|1|kind,key,campaign_id", Rows(database.File, IndexList("journal")));
        Assert.EndsWith(
            "WHERE kind = 'provider_blocked'",
            Assert.Single(Rows(database.File, "SELECT sql FROM sqlite_master WHERE name = 'ix_journal_kind_key_campaign_blocks'")),
            StringComparison.Ordinal);

        Assert.EndsWith(
            "WHERE status = 'open' AND scope = 'account'",
            Assert.Single(Rows(database.File, "SELECT sql FROM sqlite_master WHERE name = 'ix_provider_blocks_one_open_per_account'")),
            StringComparison.Ordinal);
        Assert.EndsWith(
            "WHERE status = 'open' AND scope = 'sender'",
            Assert.Single(Rows(database.File, "SELECT sql FROM sqlite_master WHERE name = 'ix_provider_blocks_one_open_per_sender'")),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A second open block for the same account is refused, and so is a second for the same sender; a block for another
    /// sender, another binding or another plugin stands beside it, and once a block has cleared the same one may open
    /// again. A sender block names its sender and an account block names none.
    /// </summary>
    [Fact]
    public void One_block_is_open_per_binding_scope_and_sender_and_a_cleared_one_may_open_again()
    {
        using var database = new TestDatabase();
        Execute(database.File, Seed);
        Execute(
            database.File,
            """
            INSERT INTO provider_blocks (public_id, plugin_id, binding_identity, scope, sender_external_id, reason, status, opened_at, opened_by_attempt_id, last_seen_at, seen_count, next_trial_at) VALUES
              ('blk_1', 'fake', 'sha256:a', 'account', NULL, 'billing', 'open', '2026-10-01 00:00:00', 1, '2026-10-01 00:00:00', 1, '2026-10-01 00:15:00'),
              ('blk_2', 'fake', 'sha256:a', 'sender', 'snd_1', 'limit', 'open', '2026-10-01 00:00:00', 1, '2026-10-01 00:00:00', 1, '2026-10-01 00:15:00'),
              ('blk_3', 'fake', 'sha256:a', 'sender', 'snd_2', 'limit', 'open', '2026-10-01 00:00:00', 1, '2026-10-01 00:00:00', 1, '2026-10-01 00:15:00'),
              ('blk_4', 'fake', 'sha256:b', 'account', NULL, 'billing', 'open', '2026-10-01 00:00:00', 1, '2026-10-01 00:00:00', 1, '2026-10-01 00:15:00'),
              ('blk_5', 'other', 'sha256:a', 'account', NULL, 'billing', 'open', '2026-10-01 00:00:00', 1, '2026-10-01 00:00:00', 1, '2026-10-01 00:15:00');
            """);

        var account = Assert.Throws<SqliteException>(() => Execute(
            database.File,
            "INSERT INTO provider_blocks (public_id, plugin_id, binding_identity, scope, reason, status, opened_at, opened_by_attempt_id, last_seen_at, seen_count, next_trial_at) "
            + "VALUES ('blk_6', 'fake', 'sha256:a', 'account', 'credential', 'open', '2026-10-01 00:00:00', 1, '2026-10-01 00:00:00', 1, '2026-10-01 00:15:00');"));
        Assert.Contains("UNIQUE", account.Message, StringComparison.Ordinal);
        var sender = Assert.Throws<SqliteException>(() => Execute(
            database.File,
            "INSERT INTO provider_blocks (public_id, plugin_id, binding_identity, scope, sender_external_id, reason, status, opened_at, opened_by_attempt_id, last_seen_at, seen_count, next_trial_at) "
            + "VALUES ('blk_6', 'fake', 'sha256:a', 'sender', 'snd_1', 'channel', 'open', '2026-10-01 00:00:00', 1, '2026-10-01 00:00:00', 1, '2026-10-01 00:15:00');"));
        Assert.Contains("UNIQUE", sender.Message, StringComparison.Ordinal);

        var unnamed = Assert.Throws<SqliteException>(() => Execute(
            database.File,
            "INSERT INTO provider_blocks (public_id, plugin_id, binding_identity, scope, reason, status, opened_at, opened_by_attempt_id, last_seen_at, seen_count, next_trial_at) "
            + "VALUES ('blk_6', 'fake', 'sha256:c', 'sender', 'limit', 'open', '2026-10-01 00:00:00', 1, '2026-10-01 00:00:00', 1, '2026-10-01 00:15:00');"));
        Assert.Contains("ck_provider_blocks_sender", unnamed.Message, StringComparison.Ordinal);
        var named = Assert.Throws<SqliteException>(() => Execute(
            database.File,
            "INSERT INTO provider_blocks (public_id, plugin_id, binding_identity, scope, sender_external_id, reason, status, opened_at, opened_by_attempt_id, last_seen_at, seen_count, next_trial_at) "
            + "VALUES ('blk_6', 'fake', 'sha256:c', 'account', 'snd_1', 'billing', 'open', '2026-10-01 00:00:00', 1, '2026-10-01 00:00:00', 1, '2026-10-01 00:15:00');"));
        Assert.Contains("ck_provider_blocks_sender", named.Message, StringComparison.Ordinal);

        Execute(
            database.File,
            """
            UPDATE provider_blocks SET status = 'cleared', cleared_at = '2026-10-01 01:00:00', cleared_by_attempt_id = 1 WHERE public_id IN ('blk_1', 'blk_2');
            INSERT INTO provider_blocks (public_id, plugin_id, binding_identity, scope, sender_external_id, reason, status, opened_at, opened_by_attempt_id, last_seen_at, seen_count, next_trial_at) VALUES
              ('blk_6', 'fake', 'sha256:a', 'account', NULL, 'credential', 'open', '2026-10-02 00:00:00', 1, '2026-10-02 00:00:00', 1, '2026-10-02 00:15:00'),
              ('blk_7', 'fake', 'sha256:a', 'sender', 'snd_1', 'limit', 'open', '2026-10-02 00:00:00', 1, '2026-10-02 00:00:00', 1, '2026-10-02 00:15:00');
            """);
        Assert.Equal(
            ["blk_3|open", "blk_4|open", "blk_5|open", "blk_6|open", "blk_7|open"],
            Rows(database.File, "SELECT public_id, status FROM provider_blocks WHERE status = 'open' ORDER BY public_id"));
    }

    /// <summary>A wait parked on a block refers to it, and a block that does not exist cannot be waited on.</summary>
    [Fact]
    public void A_parked_wait_refers_to_its_block()
    {
        using var database = new TestDatabase();
        Execute(database.File, Seed);
        Execute(
            database.File,
            """
            INSERT INTO provider_blocks (public_id, plugin_id, binding_identity, scope, reason, status, opened_at, opened_by_attempt_id, last_seen_at, seen_count, next_trial_at)
            VALUES ('blk_1', 'fake', 'sha256:a', 'account', 'billing', 'open', '2026-10-01 00:00:00', 1, '2026-10-01 00:00:00', 1, '2026-10-01 00:15:00');
            INSERT INTO work_item_waits (work_item_id, status, opened_at, provider_block_id) VALUES (1, 'open', '2026-10-01 00:00:00', 1);
            """);

        var missing = Assert.Throws<SqliteException>(() => Execute(
            database.File,
            "INSERT INTO work_item_waits (work_item_id, status, opened_at, provider_block_id) VALUES (2, 'open', '2026-10-01 00:00:00', 99);"));
        Assert.Contains("FOREIGN KEY", missing.Message, StringComparison.Ordinal);
        Assert.Equal(["1|open||||1"], Rows(database.File, "SELECT work_item_id, status, mode, until, deadline, provider_block_id FROM work_item_waits"));
    }

    /// <summary>Two waiting provider items and the attempt that met the block, in a campaign of the default project.</summary>
    private const string Seed = """
        INSERT INTO campaigns (public_id, name, status, context_json, created_at, updated_at, project_id)
        VALUES ('cmp_1', 'one', 'active', '{}', '2026-10-01 00:00:00', '2026-10-01 00:00:00', (SELECT id FROM projects WHERE is_default = 1));
        INSERT INTO work_items (public_id, campaign_id, kind, operation, status, priority, created_by_type, context_json, attempt_count, created_at, updated_at, lineage_state) VALUES
          ('wi_1', 1, 'provider_op', 'message.send', 'waiting', 0, 'human', '{}', 0, '2026-10-01 00:00:00', '2026-10-01 00:00:00', 'root'),
          ('wi_2', 1, 'provider_op', 'message.send', 'waiting', 0, 'human', '{}', 0, '2026-10-01 00:00:00', '2026-10-01 00:00:00', 'root');
        INSERT INTO attempts (public_id, work_item_id, number, command, status, context_snapshot_json, claimed_at, lock_until)
        VALUES ('att_1', 1, 1, 'provider_op', 'failed', '{}', '2026-10-01 00:00:00', '2026-10-01 00:10:00');
        """;

    private static string IndexList(string table) =>
        "SELECT il.name, il.\"unique\", (SELECT group_concat(name, ',') FROM (SELECT name FROM pragma_index_info(il.name) ORDER BY seqno)) "
        + $"FROM pragma_index_list('{table}') il WHERE il.origin = 'c' ORDER BY il.name";

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
