using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Persistence;

/// <summary>
/// Where each subscribed kind's chain stands in the journal, and the record of each chain started for a line. A kind has
/// one position and at most one chain in flight, and the database is what says so; a run's envelope is JSON or nothing.
/// Neither table is append-only: positions move and runs are pruned.
/// </summary>
public class SubscriptionRunnerSchemaTests
{
    private const string PreviousMigration = "20261102051342_ResponderWatermark";

    private const string OneRunning = "ix_subscription_runs_one_running_per_kind";

    /// <summary>An installation upgrading has no subscriber yet, so it has no position and no run: both tables arrive empty.</summary>
    [Fact]
    public void An_upgraded_installation_has_no_position_and_no_run()
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
            "INSERT INTO journal (public_id, ts, actor_type, actor_id, kind, key) VALUES ('jrn_1', '2026-11-01T00:00:00Z', 'system', 'dispatcher', 'plugins_reloaded', NULL);");

        using (var db = new JasonDbContext(options))
        {
            db.Database.Migrate();
        }

        Assert.Equal(
            ["id", "kind", "after_line", "revision", "created_at", "updated_at"],
            Rows(dir.Paths.DatabaseFile, "SELECT name FROM pragma_table_info('subscription_cursors') ORDER BY cid"));
        Assert.Equal(
            ["id", "public_id", "kind", "line_id", "line_public_id", "status", "handlers_json", "envelope_json",
             "failed_plugin_id", "failure_class", "failure_code", "started_at", "ended_at", "retry_at"],
            Rows(dir.Paths.DatabaseFile, "SELECT name FROM pragma_table_info('subscription_runs') ORDER BY cid"));
        Assert.Equal(["0|0"], Rows(dir.Paths.DatabaseFile, "SELECT (SELECT COUNT(*) FROM subscription_cursors), (SELECT COUNT(*) FROM subscription_runs)"));

        // Positions are journal ids and no more: a line pruned or not, a position never holds the journal to it.
        Assert.Empty(Rows(dir.Paths.DatabaseFile, "SELECT \"table\" FROM pragma_foreign_key_list('subscription_cursors')"));
        Assert.Empty(Rows(dir.Paths.DatabaseFile, "SELECT \"table\" FROM pragma_foreign_key_list('subscription_runs')"));
    }

    /// <summary>A kind has one position: a second row for it is refused by the database.</summary>
    [Fact]
    public void A_kind_has_one_position()
    {
        using var database = new TestDatabase();
        Execute(database.File, "INSERT INTO subscription_cursors (kind, after_line, revision, created_at, updated_at) VALUES ('workitem_failed', 4, 0, '2026-11-04 12:00:00', '2026-11-04 12:00:00');");

        var refused = Assert.Throws<SqliteException>(() => Execute(
            database.File,
            "INSERT INTO subscription_cursors (kind, after_line, revision, created_at, updated_at) VALUES ('workitem_failed', 9, 0, '2026-11-04 12:00:00', '2026-11-04 12:00:00');"));
        Assert.Contains("UNIQUE", refused.Message, StringComparison.Ordinal);

        Execute(database.File, "INSERT INTO subscription_cursors (kind, after_line, revision, created_at, updated_at) VALUES ('approval_requested', 9, 0, '2026-11-04 12:00:00', '2026-11-04 12:00:00');");
        Assert.Equal(["2"], Rows(database.File, "SELECT COUNT(*) FROM subscription_cursors"));
    }

    /// <summary>
    /// One chain in flight per kind, held by the database: a second running run of a kind is refused, a running run of
    /// another kind stands beside it, and once the first has ended the kind may run again.
    /// </summary>
    [Fact]
    public void A_kind_has_at_most_one_chain_in_flight()
    {
        using var database = new TestDatabase();
        Execute(database.File, Run("sbr_1", "workitem_failed", 4, "running"));

        var refused = Assert.Throws<SqliteException>(() => Execute(database.File, Run("sbr_2", "workitem_failed", 5, "running")));
        Assert.Contains("UNIQUE", refused.Message, StringComparison.Ordinal);

        Execute(database.File, Run("sbr_3", "approval_requested", 6, "running"));
        Execute(database.File, "UPDATE subscription_runs SET status = 'succeeded' WHERE public_id = 'sbr_1';");
        Execute(database.File, Run("sbr_4", "workitem_failed", 5, "running"));
        Execute(database.File, Run("sbr_5", "workitem_failed", 7, "failed"));

        Assert.Equal(
            ["sbr_1|succeeded", "sbr_3|running", "sbr_4|running", "sbr_5|failed"],
            Rows(database.File, "SELECT public_id, status FROM subscription_runs ORDER BY public_id"));
        Assert.Equal(
            [OneRunning],
            Rows(database.File, $"SELECT name FROM sqlite_master WHERE type = 'index' AND name = '{OneRunning}'"));
    }

    /// <summary>A run's envelope is JSON or nothing, and so is the list of its handlers.</summary>
    [Fact]
    public void A_run_s_envelope_and_handlers_are_json()
    {
        using var database = new TestDatabase();
        Execute(database.File, Run("sbr_1", "workitem_failed", 4, "succeeded", envelope: "'{\"ticket\":\"T-1\"}'"));
        Execute(database.File, Run("sbr_2", "workitem_failed", 5, "running", envelope: "NULL"));

        var envelope = Assert.Throws<SqliteException>(() => Execute(database.File, Run("sbr_3", "workitem_failed", 6, "succeeded", envelope: "'{not json'")));
        Assert.Contains("CHECK", envelope.Message, StringComparison.Ordinal);

        var handlers = Assert.Throws<SqliteException>(() => Execute(database.File, Run("sbr_4", "workitem_failed", 7, "succeeded", handlers: "'[not json'")));
        Assert.Contains("CHECK", handlers.Message, StringComparison.Ordinal);
    }

    /// <summary>The model reads and writes a run as the tables hold it: the status as its snake_case word, the JSON as JSON.</summary>
    [Fact]
    public async Task A_run_and_a_position_round_trip_through_the_model()
    {
        using var database = new TestDatabase();
        var at = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
        await using (var db = database.Open())
        {
            db.SubscriptionCursors.Add(new SubscriptionCursor { Kind = "workitem_failed", AfterLine = 41, Revision = 0, CreatedAt = at, UpdatedAt = at });
            db.SubscriptionRuns.Add(new SubscriptionRun
            {
                PublicId = "sbr_01K0run",
                Kind = "workitem_failed",
                LineId = 42,
                LinePublicId = "jrn_01K0line",
                Status = SubscriptionRunStatus.Superseded,
                Handlers = [new JsonObject { ["plugin_id"] = "example-tickets", ["started_at"] = "2026-11-04T12:00:00.000Z" }],
                Envelope = new JsonObject { ["ticket"] = "T-1" },
                StartedAt = at,
                RetryAt = at.AddSeconds(30),
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(
            ["superseded|[{\"plugin_id\":\"example-tickets\",\"started_at\":\"2026-11-04T12:00:00.000Z\"}]|{\"ticket\":\"T-1\"}"],
            Rows(database.File, "SELECT status, handlers_json, envelope_json FROM subscription_runs"));

        await using (var db = database.Open())
        {
            var run = await db.SubscriptionRuns.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(SubscriptionRunStatus.Superseded, run.Status);
            Assert.Equal("example-tickets", run.Handlers[0]!["plugin_id"]!.GetValue<string>());
            Assert.Equal(at.AddSeconds(30), run.RetryAt);
            var cursor = await db.SubscriptionCursors.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal((41, 0L), (cursor.AfterLine, cursor.Revision));
        }
    }

    private static string Run(string id, string kind, int line, string status, string envelope = "NULL", string handlers = "'[]'") =>
        $"INSERT INTO subscription_runs (public_id, kind, line_id, line_public_id, status, handlers_json, envelope_json, started_at) VALUES ('{id}', '{kind}', {line}, 'jrn_{line}', '{status}', {handlers}, {envelope}, '2026-11-04 12:00:00');";

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
