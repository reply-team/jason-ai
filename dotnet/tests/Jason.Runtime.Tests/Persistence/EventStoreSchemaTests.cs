using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Persistence;

/// <summary>
/// What the events store keeps: the accounts a read hangs on, each with the cursor the plugin last returned; every event
/// a read stored, once; and which campaigns an event was found to concern, one row each. And the moment a campaign's
/// events start being said, which an upgrade gives the campaigns already live.
/// </summary>
public class EventStoreSchemaTests
{
    private const string PreviousMigration = "20261016181342_UiTokens";

    [Fact]
    public void The_store_s_tables_and_their_columns_exist()
    {
        using var database = new TestDatabase();

        Assert.Equal(
            ["id", "public_id", "plugin_id", "binding_identity", "cursor", "cursor_at", "created_at",
             "failures", "last_class", "last_code", "more", "read_ended_at", "read_started_at", "succeeded_at", "signal_at"],
            Rows(database.File, "SELECT name FROM pragma_table_info('event_sources')"));
        Assert.Equal(
            ["id", "public_id", "source_id", "plugin_id", "kind", "occurred_at", "stored_at", "key", "contact_id", "data_json", "read_id"],
            Rows(database.File, "SELECT name FROM pragma_table_info('events')"));
        Assert.Equal(
            ["id", "event_id", "campaign_id", "rule"],
            Rows(database.File, "SELECT name FROM pragma_table_info('event_attributions')"));
        Assert.Contains("events_from", Rows(database.File, "SELECT name FROM pragma_table_info('campaigns')"));

        // Nothing that holds an event or an attribution goes away under it: every reference refuses the delete.
        Assert.Equal(
            ["contacts|contact_id|RESTRICT", "event_sources|source_id|RESTRICT"],
            Rows(database.File, "SELECT \"table\", \"from\", on_delete FROM pragma_foreign_key_list('events') ORDER BY \"table\""));
        Assert.Equal(
            ["campaigns|campaign_id|RESTRICT", "events|event_id|RESTRICT"],
            Rows(database.File, "SELECT \"table\", \"from\", on_delete FROM pragma_foreign_key_list('event_attributions') ORDER BY \"table\""));
    }

    /// <summary>
    /// Each index is written twice, in the model and in the migration that built it; nothing else ties them together.
    /// An account is one source per plugin and binding; an event is stored once under its key; a campaign is told of an
    /// event once; and a campaign says a provider event once — in an index of its own beside the inbound read's, so the
    /// index an earlier migration committed is left as it was.
    /// </summary>
    [Theory]
    [InlineData(typeof(EventSource), "ix_event_sources_public_id", "event_sources", new[] { "public_id" }, true, null)]
    [InlineData(typeof(EventSource), "ix_event_sources_plugin_id_binding_identity", "event_sources", new[] { "plugin_id", "binding_identity" }, true, null)]
    [InlineData(typeof(ProviderEvent), "ix_events_public_id", "events", new[] { "public_id" }, true, null)]
    [InlineData(typeof(ProviderEvent), "ix_events_key", "events", new[] { "key" }, true, null)]
    [InlineData(typeof(ProviderEvent), "ix_events_source_id_occurred_at", "events", new[] { "source_id", "occurred_at" }, false, null)]
    [InlineData(typeof(EventAttribution), "ix_event_attributions_event_id_campaign_id", "event_attributions", new[] { "event_id", "campaign_id" }, true, null)]
    [InlineData(typeof(EventAttribution), "ix_event_attributions_campaign_id_event_id", "event_attributions", new[] { "campaign_id", "event_id" }, false, null)]
    [InlineData(typeof(JournalEntry), "ix_journal_campaign_kind_key_events", "journal", new[] { "campaign_id", "kind", "key" }, true, "kind = 'provider_event_observed'")]
    public void Each_index_has_its_columns_in_the_model_the_migration_and_the_database(
        Type entity,
        string name,
        string table,
        string[] columns,
        bool unique,
        string? filter)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var index = db.Model.FindEntityType(entity)!.GetIndexes().Single(index => index.GetDatabaseName() == name);
        Assert.Equal(unique, index.IsUnique);
        Assert.Equal(filter, index.GetFilter());
        Assert.Equal(columns, index.Properties.Select(property => property.GetColumnName()));

        var migration = File.ReadAllText(Directory.GetFiles(MigrationsDirectory(), "*_EventStore.cs").Single());
        var start = migration.IndexOf($"name: \"{name}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"The migration creates no index named {name}.");
        var call = migration[start..migration.IndexOf(");", start, StringComparison.Ordinal)];
        Assert.Contains($"table: \"{table}\"", call, StringComparison.Ordinal);
        Assert.Contains(
            columns.Length == 1 ? $"column: \"{columns[0]}\"" : "new[] { " + string.Join(", ", columns.Select(column => $"\"{column}\"")) + " }",
            call,
            StringComparison.Ordinal);
        Assert.Equal(unique, call.Contains("unique: true", StringComparison.Ordinal));
        if (filter is not null)
        {
            Assert.Contains($"filter: \"{filter}\"", call, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain("filter:", call, StringComparison.Ordinal);
        }

        Assert.Equal(columns, Rows(database.File, $"SELECT name FROM pragma_index_info('{name}') ORDER BY seqno"));
    }

    /// <summary>
    /// A campaign says a provider event once, and the database is what says so: a second line of the kind under a key the
    /// campaign already holds is refused. Another campaign may hold the same key — one event may concern two campaigns —
    /// and the inbound read's index is untouched by it.
    /// </summary>
    [Fact]
    public void A_campaign_says_a_provider_event_once()
    {
        using var database = new TestDatabase();
        Execute(
            database.File,
            """
            INSERT INTO campaigns (public_id, name, status, context_json, created_at, updated_at, project_id) VALUES
              ('cmp_A', 'a', 'active', '{}', '2026-10-18T00:00:00Z', '2026-10-18T00:00:00Z', (SELECT id FROM projects WHERE is_default = 1)),
              ('cmp_B', 'b', 'active', '{}', '2026-10-18T00:00:00Z', '2026-10-18T00:00:00Z', (SELECT id FROM projects WHERE is_default = 1));
            INSERT INTO journal (public_id, ts, actor_type, actor_id, kind, campaign_id, key) VALUES
              ('jrn_1', '2026-10-18T00:00:00Z', 'system', 'dispatcher', 'provider_event_observed', 1, 'sha256:k'),
              ('jrn_2', '2026-10-18T00:00:00Z', 'system', 'dispatcher', 'provider_event_observed', 2, 'sha256:k'),
              ('jrn_3', '2026-10-18T00:00:00Z', 'system', 'dispatcher', 'inbound_reply_observed', 1, 'sha256:k');
            """);

        var refused = Assert.Throws<SqliteException>(() => Execute(
            database.File,
            "INSERT INTO journal (public_id, ts, actor_type, actor_id, kind, campaign_id, key) VALUES ('jrn_4', '2026-10-18T00:00:01Z', 'system', 'dispatcher', 'provider_event_observed', 1, 'sha256:k');"));
        Assert.Contains("UNIQUE", refused.Message, StringComparison.Ordinal);
        Assert.Equal(["3"], Rows(database.File, "SELECT COUNT(*) FROM journal"));
    }

    /// <summary>
    /// An installation upgrading into the events store owes no campaign its history: a campaign that is live, or paused
    /// and able to go live again, has its events said from the moment of the upgrade, and one that has never been live,
    /// or never will be again, has nothing said until it goes live. Everything that refers to a campaign still does.
    /// </summary>
    [Fact]
    public void An_upgraded_installation_gives_events_from_to_its_live_and_paused_campaigns_and_no_others()
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
            INSERT INTO campaigns (public_id, name, status, context_json, created_at, updated_at, archived_at) VALUES
              ('cmp_active', 'a', 'active', '{}', '2026-10-01T00:00:00Z', '2026-10-01T00:00:00Z', NULL),
              ('cmp_paused', 'p', 'paused', '{}', '2026-10-01T00:00:00Z', '2026-10-01T00:00:00Z', NULL),
              ('cmp_draft', 'd', 'draft', '{}', '2026-10-01T00:00:00Z', '2026-10-01T00:00:00Z', NULL),
              ('cmp_archived', 'x', 'archived', '{}', '2026-10-01T00:00:00Z', '2026-10-01T00:00:00Z', '2026-10-02T00:00:00Z');
            INSERT INTO journal (public_id, ts, actor_type, actor_id, kind, campaign_id)
            VALUES ('jrn_old', '2026-10-01T00:00:00Z', 'human', 'ada', 'campaign_created', 1);
            """);

        var before = DateTime.UtcNow.AddSeconds(-1);
        using (var db = new JasonDbContext(options))
        {
            db.Database.Migrate();
        }

        var after = DateTime.UtcNow.AddSeconds(1);

        using (var db = new JasonDbContext(options))
        {
            var campaigns = db.Campaigns.ToDictionary(c => c.PublicId);
            Assert.Equal(4, campaigns.Count);
            foreach (var live in new[] { "cmp_active", "cmp_paused" })
            {
                var from = campaigns[live].EventsFrom;
                Assert.True(from is { } moment && moment >= before && moment <= after, $"{live} has events_from {from}, outside {before:O}..{after:O}.");
            }

            Assert.Null(campaigns["cmp_draft"].EventsFrom);
            Assert.Null(campaigns["cmp_archived"].EventsFrom);
        }

        Assert.Equal(["1"], Rows(dir.Paths.DatabaseFile, "SELECT COUNT(*) FROM journal"));
        Assert.Empty(Rows(dir.Paths.DatabaseFile, "SELECT * FROM pragma_foreign_key_check"));
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
