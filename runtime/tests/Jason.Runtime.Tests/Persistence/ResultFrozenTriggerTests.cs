using Microsoft.Data.Sqlite;

namespace Jason.Runtime.Tests.Persistence;

/// <summary>
/// The two guarantees the database itself makes about work: a finished item's result never changes under
/// anyone, and one work item never has two live attempts.
/// </summary>
public class ResultFrozenTriggerTests
{
    [Fact]
    public void The_result_of_a_finished_work_item_cannot_be_rewritten()
    {
        using var database = new TestDatabase();
        using var connection = Open(database);
        SeedCampaign(connection);
        SeedWorkItem(connection, id: 1, publicId: "wi_S", status: "succeeded");
        SeedWorkItem(connection, id: 2, publicId: "wi_P", status: "processing");

        var frozen = Assert.Throws<SqliteException>(() => Execute(connection, "UPDATE work_items SET result_json = '{\"x\":1}' WHERE id = 1"));

        Assert.Contains("frozen", frozen.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_running_item_still_writes_its_result_and_carries_it_into_the_terminal_transition()
    {
        using var database = new TestDatabase();
        using var connection = Open(database);
        SeedCampaign(connection);
        SeedWorkItem(connection, id: 1, publicId: "wi_P", status: "processing");

        Execute(connection, "UPDATE work_items SET result_json = '{\"x\":1}' WHERE id = 1");
        Execute(connection, "UPDATE work_items SET status = 'succeeded', result_json = '{\"x\":2}' WHERE id = 1");

        Assert.Equal("{\"x\":2}", Scalar(connection, "SELECT result_json FROM work_items WHERE id = 1"));
    }

    [Fact]
    public void Every_way_a_work_item_can_be_finished_freezes_its_result()
    {
        using var database = new TestDatabase();
        using var connection = Open(database);
        SeedCampaign(connection);
        var id = 1;
        foreach (var status in new[] { "succeeded", "failed", "cancelled", "expired" })
        {
            SeedWorkItem(connection, id, $"wi_{status}", status);
            Assert.Throws<SqliteException>(() => Execute(connection, $"UPDATE work_items SET result_json = '{{\"x\":1}}' WHERE id = {id}"));
            id++;
        }
    }

    [Fact]
    public void One_live_attempt_per_work_item_is_a_database_guarantee()
    {
        using var database = new TestDatabase();
        using var connection = Open(database);
        SeedCampaign(connection);
        SeedWorkItem(connection, id: 1, publicId: "wi_A", status: "processing");
        SeedAttempt(connection, id: 1, publicId: "att_1", number: 1, status: "running");

        var duplicate = Assert.Throws<SqliteException>(() => SeedAttempt(connection, id: 2, publicId: "att_2", number: 2, status: "scheduled"));
        Assert.Contains("UNIQUE", duplicate.Message, StringComparison.Ordinal);

        // A finished attempt is outside the filter, so the history of an item is unbounded.
        SeedAttempt(connection, id: 3, publicId: "att_3", number: 3, status: "failed");
        SeedAttempt(connection, id: 4, publicId: "att_4", number: 4, status: "interrupted");
        Assert.Equal("3", Scalar(connection, "SELECT COUNT(*) FROM attempts WHERE work_item_id = 1"));
    }

    private static SqliteConnection Open(TestDatabase database)
    {
        var connection = new SqliteConnection($"Data Source={database.File}");
        connection.Open();
        return connection;
    }

    private static void SeedCampaign(SqliteConnection connection) =>
        Execute(connection, "INSERT INTO campaigns (id, public_id, name, status, context_json, created_at, updated_at) VALUES (1, 'cmp_A', 'a', 'active', '{}', '2026-01-01', '2026-01-01')");

    private static void SeedWorkItem(SqliteConnection connection, int id, string publicId, string status) =>
        Execute(
            connection,
            "INSERT INTO work_items (id, public_id, campaign_id, kind, status, priority, created_by_type, context_json, attempt_count, created_at, updated_at) "
            + $"VALUES ({id}, '{publicId}', 1, 'ai_role', '{status}', 0, 'human', '{{}}', 0, '2026-01-01', '2026-01-01')");

    private static void SeedAttempt(SqliteConnection connection, int id, string publicId, int number, string status) =>
        Execute(
            connection,
            "INSERT INTO attempts (id, public_id, work_item_id, number, command, status, context_snapshot_json, claimed_at, lock_until) "
            + $"VALUES ({id}, '{publicId}', 1, {number}, 'ai_role', '{status}', '{{}}', '2026-01-01', '2026-01-02')");

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
