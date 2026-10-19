using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Persistence;

/// <summary>
/// What a source keeps about its reads — when the last one started and ended, when the last one that succeeded started,
/// how many in a row failed and how, and whether it stopped with more to read — and the one index that says a gap in
/// an account's events once.
/// </summary>
public class EventReadsSchemaTests
{
    private const string PreviousMigration = "20261018190303_EventStore";

    /// <summary>The migration these columns arrived in: an upgrade is read at it, so a later migration's columns are a later test's.</summary>
    private const string ThisMigration = "20261018230454_EventReads";

    private const string GapIndex = "ix_journal_kind_key_gaps";

    /// <summary>
    /// A source an installation already holds when it upgrades has read nothing in this build: no read started, none
    /// failed, nothing more to read. Its cursor is kept as it was.
    /// </summary>
    [Fact]
    public void An_upgraded_source_keeps_its_cursor_and_has_read_nothing_yet()
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
            db.GetInfrastructure().GetRequiredService<IMigrator>().Migrate(ThisMigration);
        }

        Assert.Equal(
            ["id", "public_id", "plugin_id", "binding_identity", "cursor", "cursor_at", "created_at",
             "failures", "last_class", "last_code", "more", "read_ended_at", "read_started_at", "succeeded_at"],
            Rows(dir.Paths.DatabaseFile, "SELECT name FROM pragma_table_info('event_sources')"));
        Assert.Equal(
            ["c-41||||0|||0"],
            Rows(dir.Paths.DatabaseFile, "SELECT cursor, read_started_at, read_ended_at, succeeded_at, failures, last_code, last_class, more FROM event_sources"));
    }

    /// <summary>
    /// The gap index is written twice, in the model and in the migration that built it. A gap line names no campaign, and
    /// SQLite holds every null apart from every other in a unique index, so the campaign cannot be one of its columns.
    /// </summary>
    [Fact]
    public void The_gap_index_has_its_columns_in_the_model_the_migration_and_the_database()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var index = db.Model.FindEntityType(typeof(JournalEntry))!.GetIndexes().Single(index => index.GetDatabaseName() == GapIndex);
        Assert.True(index.IsUnique);
        Assert.Equal("kind = 'events_gap_observed'", index.GetFilter());
        Assert.Equal(["kind", "key"], index.Properties.Select(property => property.GetColumnName()));

        var migration = File.ReadAllText(Directory.GetFiles(MigrationsDirectory(), "*_EventReads.cs").Single());
        var start = migration.IndexOf($"name: \"{GapIndex}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"The migration creates no index named {GapIndex}.");
        var call = migration[start..migration.IndexOf(");", start, StringComparison.Ordinal)];
        Assert.Contains("table: \"journal\"", call, StringComparison.Ordinal);
        Assert.Contains("new[] { \"kind\", \"key\" }", call, StringComparison.Ordinal);
        Assert.Contains("unique: true", call, StringComparison.Ordinal);
        Assert.Contains("filter: \"kind = 'events_gap_observed'\"", call, StringComparison.Ordinal);

        Assert.Equal(["kind", "key"], Rows(database.File, $"SELECT name FROM pragma_index_info('{GapIndex}') ORDER BY seqno"));
    }

    /// <summary>
    /// A gap is said once, and the database is what says so: a second gap line under a key already said is refused,
    /// though neither names a campaign. Another gap, and a line of another kind under the same key, are not.
    /// </summary>
    [Fact]
    public void A_gap_is_said_once_though_it_names_no_campaign()
    {
        using var database = new TestDatabase();
        Execute(
            database.File,
            """
            INSERT INTO journal (public_id, ts, actor_type, actor_id, kind, key) VALUES
              ('jrn_1', '2026-10-18T00:00:00Z', 'system', 'dispatcher', 'events_gap_observed', 'gap/evs_1/2026-09-01T00:00:00.000Z'),
              ('jrn_2', '2026-10-18T00:00:00Z', 'system', 'dispatcher', 'events_gap_observed', 'gap/evs_2/2026-09-01T00:00:00.000Z'),
              ('jrn_3', '2026-10-18T00:00:00Z', 'system', 'dispatcher', 'events_read', 'gap/evs_1/2026-09-01T00:00:00.000Z');
            """);

        var refused = Assert.Throws<SqliteException>(() => Execute(
            database.File,
            "INSERT INTO journal (public_id, ts, actor_type, actor_id, kind, key) VALUES ('jrn_4', '2026-10-18T00:00:01Z', 'system', 'dispatcher', 'events_gap_observed', 'gap/evs_1/2026-09-01T00:00:00.000Z');"));
        Assert.Contains("UNIQUE", refused.Message, StringComparison.Ordinal);
        Assert.Equal(["3"], Rows(database.File, "SELECT COUNT(*) FROM journal"));
    }

    private static string MigrationsDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "runtime", "src", "Jason.Runtime", "Persistence", "Migrations");
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
