using Microsoft.Data.Sqlite;

namespace Jason.Runtime.Tests.Persistence;

public class SchemaTests
{
    [Fact]
    public void Wave_two_tables_columns_indexes_and_triggers_exist()
    {
        using var database = new TestDatabase();
        using var connection = new SqliteConnection($"Data Source={database.File}");
        connection.Open();

        var tables = Names(connection, "SELECT name FROM sqlite_master WHERE type = 'table'");
        Assert.Contains("campaigns", tables);
        Assert.Contains("contacts", tables);
        Assert.Contains("contact_channels", tables);
        Assert.Contains("campaign_contacts", tables);
        Assert.Contains("suppressions", tables);
        Assert.Contains("journal", tables);

        Assert.Equal(
            ["id", "archived_at", "context_json", "created_at", "name", "public_id", "status", "updated_at", "execution_profile",
             "manager_event_watermark", "manager_review_anchor", "manager_review_seconds", "idempotency_digest", "idempotency_key",
             "inbound_failures", "inbound_offset", "inbound_sync_item_id", "inbound_synced_at", "inbound_watermark", "events_from",
             "project_id", "folder_path"],
            Names(connection, "SELECT name FROM pragma_table_info('campaigns')"));
        Assert.Equal(
            ["id", "public_id", "ts", "actor_type", "actor_id", "kind", "campaign_id", "key", "old_json", "new_json", "reason", "attempt_id", "work_item_id", "contact_id"],
            Names(connection, "SELECT name FROM pragma_table_info('journal')"));

        var indexes = Names(connection, "SELECT name FROM sqlite_master WHERE type = 'index'");
        Assert.Contains("ix_contact_channels_channel_value", indexes);
        Assert.Contains("ix_contact_channels_contact_id_channel_value", indexes);
        Assert.Contains("ix_contact_channels_one_primary_per_channel", indexes);
        Assert.Contains("ix_campaign_contacts_campaign_id_contact_id", indexes);
        Assert.Contains("ix_suppressions_channel_value", indexes);
        Assert.Contains("ix_journal_contact_id_public_id", indexes);
        Assert.Contains("ix_journal_suppression_added_ts", indexes);
        Assert.Contains("ix_journal_contacts_added_campaign_ts", indexes);
        Assert.Contains("ix_journal_contacts_removed_campaign_ts", indexes);

        Assert.Equal(
            [
                "attempts_provenance_valid_insert", "attempts_provenance_valid_update", "campaigns_project_required_insert",
                "campaigns_project_required_update", "event_attributions_no_delete",
                "event_attributions_no_update", "events_no_delete", "events_no_update", "execution_profile_revisions_no_delete",
                "execution_profile_revisions_no_update", "journal_no_delete", "journal_no_update", "reports_no_delete",
                "reports_no_update", "work_items_result_frozen",
            ],
            Names(connection, "SELECT name FROM sqlite_master WHERE type = 'trigger' ORDER BY name"));
    }

    [Fact]
    public void Wave_three_tables_columns_and_indexes_exist()
    {
        using var database = new TestDatabase();
        using var connection = new SqliteConnection($"Data Source={database.File}");
        connection.Open();

        var tables = Names(connection, "SELECT name FROM sqlite_master WHERE type = 'table'");
        Assert.Contains("work_items", tables);
        Assert.Contains("attempts", tables);
        Assert.Contains("roles", tables);

        Assert.Equal(
            [
                "attempt_count", "campaign_id", "contact_id", "context_json", "created_at", "created_by_id", "created_by_type", "due_at",
                "execution_profile", "finished_at", "heartbeat_seconds", "id", "idempotency_digest", "idempotency_key", "kind", "last_error_json", "lineage_from_attempt_id",
                "lineage_profile_name", "lineage_profile_revision", "lineage_state", "max_attempts", "not_before", "operation",
                "priority", "public_id", "result_format_json", "result_json", "retry_after", "role", "status", "timeout_seconds",
                "updated_at",
            ],
            Sorted(connection, "SELECT name FROM pragma_table_info('work_items')"));

        Assert.Equal(
            [
                "claimed_at", "command", "context_snapshot_json", "error_json", "execution_profile", "finished_at", "id",
                "last_heartbeat_at", "launch_json", "lock_until", "number", "provenance_json", "public_id", "started_at", "status",
                "work_item_id",
            ],
            Sorted(connection, "SELECT name FROM pragma_table_info('attempts')"));

        Assert.Equal(
            [
                "builtin", "created_at", "description", "entry_command_json", "execution_profile", "id", "name",
                "profile_defaults_json", "public_id", "updated_at",
            ],
            Sorted(connection, "SELECT name FROM pragma_table_info('roles')"));

        var indexes = Names(connection, "SELECT name FROM sqlite_master WHERE type = 'index'");
        Assert.Contains("ix_work_items_campaign_id_status", indexes);
        Assert.Contains("ix_work_items_status_not_before", indexes);
        Assert.Contains("ix_work_items_status_due_at", indexes);
        Assert.Contains("ix_work_items_status_finished_at", indexes);
        Assert.Contains("ix_work_items_contact_id", indexes);
        Assert.Contains("ix_work_items_campaign_id_contact_id", indexes);
        Assert.Contains("ix_attempts_work_item_id_number", indexes);
        Assert.Contains("ix_attempts_status", indexes);
        Assert.Contains("ix_roles_name", indexes);
        Assert.Contains("ix_journal_work_item_id_public_id", indexes);

        var singleFlight = Scalar(connection, "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = 'ix_attempts_one_live_per_item'");
        Assert.Contains("UNIQUE", singleFlight, StringComparison.Ordinal);
        Assert.Contains("WHERE status IN ('scheduled','running')", singleFlight, StringComparison.Ordinal);
    }

    [Fact]
    public void Execution_profile_tables_columns_and_indexes_exist()
    {
        using var database = new TestDatabase();
        using var connection = new SqliteConnection($"Data Source={database.File}");
        connection.Open();

        var tables = Names(connection, "SELECT name FROM sqlite_master WHERE type = 'table'");
        Assert.Contains("execution_profiles", tables);
        Assert.Contains("execution_profile_revisions", tables);

        Assert.Equal(
            ["created_at", "current_revision", "description", "disabled_at", "id", "name", "public_id", "updated_at"],
            Sorted(connection, "SELECT name FROM pragma_table_info('execution_profiles')"));

        // No environment, no secret, no free-form bag: a credential has nowhere to go, which is the whole of the
        // rule that a profile never holds one.
        Assert.Equal(
            [
                "args_json", "cli_command", "created_at", "created_by_id", "created_by_type", "deny_json", "host",
                "host_version_verified", "id", "number", "profile_id", "program",
            ],
            Sorted(connection, "SELECT name FROM pragma_table_info('execution_profile_revisions')"));

        var indexes = Names(connection, "SELECT name FROM sqlite_master WHERE type = 'index'");
        Assert.Contains("ix_execution_profiles_name", indexes);
        Assert.Contains("ix_execution_profiles_public_id", indexes);

        var perNumber = Scalar(connection, "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = 'ix_execution_profile_revisions_one_per_number'");
        Assert.Contains("UNIQUE", perNumber, StringComparison.Ordinal);
    }

    [Fact]
    public void Role_note_table_columns_and_index_exist()
    {
        using var database = new TestDatabase();
        using var connection = new SqliteConnection($"Data Source={database.File}");
        connection.Open();

        Assert.Contains("role_notes", Names(connection, "SELECT name FROM sqlite_master WHERE type = 'table'"));

        Assert.Equal(
            [
                "campaign_id", "created_at", "id", "note_bytes", "note_hash", "note_json", "project_id", "role", "updated_at",
                "updated_by_id", "updated_by_type",
            ],
            Sorted(connection, "SELECT name FROM pragma_table_info('role_notes')"));

        // One document per role at each level — a campaign's, a project's, the client's — which is what makes a write a
        // replacement: there is nowhere for a second note to go.
        foreach (var level in new[] { "ix_role_notes_one_per_campaign_role", "ix_role_notes_one_per_project_role", "ix_role_notes_one_per_client_role" })
        {
            var perRole = Scalar(connection, $"SELECT sql FROM sqlite_master WHERE type = 'index' AND name = '{level}'");
            Assert.True(perRole.Contains("UNIQUE", StringComparison.Ordinal), $"{level} is not unique: {perRole}");
        }

        // A note belongs to a campaign or a project that exists, never to both, and what it holds is JSON — all enforced
        // by the database rather than only by the service that writes through it.
        var table = Scalar(connection, "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'role_notes'");
        Assert.Contains("json_valid(note_json)", table, StringComparison.Ordinal);
        Assert.Contains("REFERENCES \"campaigns\" (\"id\")", table, StringComparison.Ordinal);
        Assert.Contains("REFERENCES \"projects\" (\"id\")", table, StringComparison.Ordinal);
        Assert.Contains("CHECK (campaign_id IS NULL OR project_id IS NULL)", table, StringComparison.Ordinal);
    }

    /// <summary>
    /// A project's note is found by its project and role through the index that holds it unique, and by the plan rather
    /// than by a clock: SQLite uses a partial index only when its filter is one the query implies, and a filter with an
    /// extra term would send this lookup to a scan of the table.
    /// </summary>
    [Fact]
    public void A_project_notes_lookup_by_project_and_role_uses_its_index()
    {
        using var database = new TestDatabase();
        using var connection = new SqliteConnection($"Data Source={database.File}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN SELECT id FROM role_notes WHERE project_id = 1 AND role = 'manager'";
        using var reader = command.ExecuteReader();
        var plan = new List<string>();
        while (reader.Read())
        {
            plan.Add(reader.GetString(reader.GetOrdinal("detail")));
        }

        Assert.True(
            plan.Any(step => step.Contains("INDEX ix_role_notes_one_per_project_role", StringComparison.Ordinal)),
            $"The lookup of a project's note does not use its index; the plan is: {string.Join(" | ", plan)}");
    }

    [Fact]
    public void Invalid_json_is_rejected_by_the_database()
    {
        using var database = new TestDatabase();
        using var connection = new SqliteConnection($"Data Source={database.File}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO campaigns (public_id, name, status, created_at, updated_at, context_json, project_id) VALUES ('cmp_X', 'x', 'draft', '2026-01-01', '2026-01-01', '{not json', (SELECT id FROM projects WHERE is_default = 1))";

        Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
    }

    [Fact]
    [Critical(Family.S1, "the primary index")]
    public void One_primary_channel_per_contact_and_channel_is_enforced()
    {
        using var database = new TestDatabase();
        using var connection = new SqliteConnection($"Data Source={database.File}");
        connection.Open();
        Execute(connection, "INSERT INTO contacts (public_id, custom_json, created_at, updated_at) VALUES ('cnt_A', '{}', '2026-01-01', '2026-01-01')");
        Execute(connection, "INSERT INTO contact_channels (contact_id, channel, value, is_primary) VALUES (1, 'email', 'a@example.test', 1)");

        var duplicate = Assert.Throws<SqliteException>(() =>
            Execute(connection, "INSERT INTO contact_channels (contact_id, channel, value, is_primary) VALUES (1, 'email', 'b@example.test', 1)"));

        Assert.Contains("UNIQUE", duplicate.Message, StringComparison.Ordinal);
        Execute(connection, "INSERT INTO contact_channels (contact_id, channel, value, is_primary) VALUES (1, 'email', 'b@example.test', 0)");
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static List<string> Sorted(SqliteConnection connection, string sql)
    {
        var names = Names(connection, sql);
        names.Sort(StringComparer.Ordinal);
        return names;
    }

    private static string Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static List<string> Names(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
