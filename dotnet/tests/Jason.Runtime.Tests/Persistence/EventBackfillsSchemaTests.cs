using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Persistence;

/// <summary>
/// What a person's request to read a span of an account again keeps — its range, where its own reading stands, how it
/// ended — beside the source it reads, and the moment a source was last asked to be read sooner. A span is asked for once
/// while it waits: the database is what says so.
/// </summary>
public class EventBackfillsSchemaTests
{
    private const string PreviousMigration = "20261019053859_LinkedInSlugKeys";

    private const string PendingRange = "ix_event_backfills_one_pending_range";

    /// <summary>A source an installation already holds when it upgrades was never asked to be read sooner, and has no span asked for.</summary>
    [Fact]
    public void An_upgraded_source_has_no_signal_and_no_span_asked_for()
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
            INSERT INTO event_sources (public_id, plugin_id, binding_identity, cursor, cursor_at, created_at)
            VALUES ('evs_1', 'reader', 'none', 'c-41', '2026-10-17 12:00:00', '2026-10-17 12:00:00');
            """);

        using (var db = new JasonDbContext(options))
        {
            db.Database.Migrate();
        }

        Assert.Equal(
            ["id", "public_id", "plugin_id", "binding_identity", "cursor", "cursor_at", "created_at",
             "failures", "last_class", "last_code", "more", "read_ended_at", "read_started_at", "succeeded_at", "signal_at"],
            Rows(dir.Paths.DatabaseFile, "SELECT name FROM pragma_table_info('event_sources')"));
        Assert.Equal(["c-41|"], Rows(dir.Paths.DatabaseFile, "SELECT cursor, signal_at FROM event_sources"));
        Assert.Equal(
            ["id", "public_id", "source_id", "range_from", "range_to", "status", "cursor", "failures", "attempted_at",
             "requested_at", "actor_type", "actor_id", "reason", "stored", "duplicates", "ended_at", "last_code", "last_class"],
            Rows(dir.Paths.DatabaseFile, "SELECT name FROM pragma_table_info('event_backfills') ORDER BY cid"));
        Assert.Equal(["0"], Rows(dir.Paths.DatabaseFile, "SELECT COUNT(*) FROM event_backfills"));

        // Nothing a request to read a span again refers to goes away under it.
        Assert.Equal(
            ["event_sources|source_id|RESTRICT"],
            Rows(dir.Paths.DatabaseFile, "SELECT \"table\", \"from\", on_delete FROM pragma_foreign_key_list('event_backfills')"));
    }

    /// <summary>
    /// A span waits once: a second request for the same span of the same account while the first waits is refused by the
    /// database. Once the first has ended, done or failed, the span may be asked for again, and another span, or the same
    /// span of another account, may wait beside it.
    /// </summary>
    [Fact]
    public void A_span_waits_once_and_may_be_asked_for_again_once_it_has_ended()
    {
        using var database = new TestDatabase();
        Execute(
            database.File,
            """
            INSERT INTO event_sources (public_id, plugin_id, binding_identity, created_at) VALUES
              ('evs_1', 'reader', 'none', '2026-10-17 12:00:00'),
              ('evs_2', 'reader', 'sha256:2', '2026-10-17 12:00:00');
            INSERT INTO event_backfills (public_id, source_id, range_from, range_to, status, requested_at, actor_type) VALUES
              ('evb_1', 1, '2026-08-01 00:00:00', '2026-09-01 00:00:00', 'pending', '2026-10-18 12:00:00', 'human'),
              ('evb_2', 1, '2026-08-01 00:00:00', '2026-09-02 00:00:00', 'pending', '2026-10-18 12:00:00', 'human'),
              ('evb_3', 2, '2026-08-01 00:00:00', '2026-09-01 00:00:00', 'pending', '2026-10-18 12:00:00', 'human');
            """);

        var refused = Assert.Throws<SqliteException>(() => Execute(
            database.File,
            "INSERT INTO event_backfills (public_id, source_id, range_from, range_to, status, requested_at, actor_type) VALUES ('evb_4', 1, '2026-08-01 00:00:00', '2026-09-01 00:00:00', 'pending', '2026-10-18 12:00:01', 'human');"));
        Assert.Contains("UNIQUE", refused.Message, StringComparison.Ordinal);

        Execute(database.File, "UPDATE event_backfills SET status = 'done' WHERE public_id = 'evb_1';");
        Execute(
            database.File,
            """
            INSERT INTO event_backfills (public_id, source_id, range_from, range_to, status, requested_at, actor_type) VALUES
              ('evb_5', 1, '2026-08-01 00:00:00', '2026-09-01 00:00:00', 'failed', '2026-10-18 12:00:02', 'human'),
              ('evb_6', 1, '2026-08-01 00:00:00', '2026-09-01 00:00:00', 'pending', '2026-10-18 12:00:03', 'human');
            """);

        Assert.Equal(
            ["evb_1|done|0|0|0", "evb_2|pending|0|0|0", "evb_3|pending|0|0|0", "evb_5|failed|0|0|0", "evb_6|pending|0|0|0"],
            Rows(database.File, "SELECT public_id, status, failures, stored, duplicates FROM event_backfills ORDER BY public_id"));
    }

    /// <summary>The index is written twice, in the model and in the migration that built it, and both name the same columns and the same filter.</summary>
    [Fact]
    public void The_pending_span_index_has_its_columns_in_the_model_the_migration_and_the_database()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var index = db.Model.FindEntityType(typeof(EventBackfill))!.GetIndexes().Single(index => index.GetDatabaseName() == PendingRange);
        Assert.True(index.IsUnique);
        Assert.Equal("status = 'pending'", index.GetFilter());
        Assert.Equal(["source_id", "range_from", "range_to"], index.Properties.Select(property => property.GetColumnName()));

        var migration = File.ReadAllText(Directory.GetFiles(MigrationsDirectory(), "*_EventBackfills.cs").Single());
        var start = migration.IndexOf($"name: \"{PendingRange}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"The migration creates no index named {PendingRange}.");
        var call = migration[start..migration.IndexOf(");", start, StringComparison.Ordinal)];
        Assert.Contains("table: \"event_backfills\"", call, StringComparison.Ordinal);
        Assert.Contains("new[] { \"source_id\", \"range_from\", \"range_to\" }", call, StringComparison.Ordinal);
        Assert.Contains("unique: true", call, StringComparison.Ordinal);
        Assert.Contains("filter: \"status = 'pending'\"", call, StringComparison.Ordinal);

        Assert.Equal(["source_id", "range_from", "range_to"], Rows(database.File, $"SELECT name FROM pragma_index_info('{PendingRange}') ORDER BY seqno"));
    }

    private static string MigrationsDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "dotnet", "src", "Jason.Runtime", "Persistence", "Migrations");
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
