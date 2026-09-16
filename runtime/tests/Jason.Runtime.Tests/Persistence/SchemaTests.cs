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
            ["id", "archived_at", "context_json", "created_at", "name", "public_id", "status", "updated_at"],
            Names(connection, "SELECT name FROM pragma_table_info('campaigns')"));
        Assert.Equal(
            ["id", "public_id", "ts", "actor_type", "actor_id", "kind", "campaign_id", "key", "old_json", "new_json", "reason", "attempt_id", "work_item_id"],
            Names(connection, "SELECT name FROM pragma_table_info('journal')"));

        var indexes = Names(connection, "SELECT name FROM sqlite_master WHERE type = 'index'");
        Assert.Contains("ix_contact_channels_channel_value", indexes);
        Assert.Contains("ix_contact_channels_contact_id_channel_value", indexes);
        Assert.Contains("ix_contact_channels_one_primary_per_channel", indexes);
        Assert.Contains("ix_campaign_contacts_campaign_id_contact_id", indexes);
        Assert.Contains("ix_suppressions_channel_value", indexes);

        Assert.Equal(
            [
                "attempts_provenance_valid_insert", "attempts_provenance_valid_update", "journal_no_delete", "journal_no_update",
                "work_items_result_frozen",
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
                "execution_profile", "finished_at", "heartbeat_seconds", "id", "kind", "last_error_json", "max_attempts", "not_before",
                "operation", "priority", "public_id", "result_format_json", "result_json", "retry_after", "role", "status",
                "timeout_seconds", "updated_at",
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
            ["builtin", "created_at", "description", "entry_command_json", "id", "name", "profile_defaults_json", "public_id", "updated_at"],
            Sorted(connection, "SELECT name FROM pragma_table_info('roles')"));

        var indexes = Names(connection, "SELECT name FROM sqlite_master WHERE type = 'index'");
        Assert.Contains("ix_work_items_campaign_id_status", indexes);
        Assert.Contains("ix_work_items_status_not_before", indexes);
        Assert.Contains("ix_work_items_status_due_at", indexes);
        Assert.Contains("ix_work_items_contact_id", indexes);
        Assert.Contains("ix_attempts_work_item_id_number", indexes);
        Assert.Contains("ix_attempts_status", indexes);
        Assert.Contains("ix_roles_name", indexes);
        Assert.Contains("ix_journal_work_item_id_public_id", indexes);

        var singleFlight = Scalar(connection, "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = 'ix_attempts_one_live_per_item'");
        Assert.Contains("UNIQUE", singleFlight, StringComparison.Ordinal);
        Assert.Contains("WHERE status IN ('scheduled','running')", singleFlight, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_json_is_rejected_by_the_database()
    {
        using var database = new TestDatabase();
        using var connection = new SqliteConnection($"Data Source={database.File}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO campaigns (public_id, name, status, created_at, updated_at, context_json) VALUES ('cmp_X', 'x', 'draft', '2026-01-01', '2026-01-01', '{not json')";

        Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
    }

    [Fact]
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
