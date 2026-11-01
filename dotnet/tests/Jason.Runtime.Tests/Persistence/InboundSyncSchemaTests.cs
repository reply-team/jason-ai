using Jason.Contracts.Api;
using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Persistence;

/// <summary>
/// What the inbound read keeps on a campaign — where the next read starts, when the last one did, the read still
/// outstanding, the page it is on and how many reads in a row have failed — and the two indexes it reads by: a pin
/// found by its value, and a campaign's lines of one kind found by their key.
/// </summary>
public class InboundSyncSchemaTests
{
    private const string PreviousMigration = "20261005204228_IdempotencyKeys";

    private static readonly DateTime Noon = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_campaign_made_without_them_starts_with_no_read_on_record()
    {
        using var database = new TestDatabase();
        Execute(
            database.File,
            """
            INSERT INTO campaigns (public_id, name, status, context_json, created_at, updated_at, project_id)
            VALUES ('cmp_new', 'new', 'draft', '{}', '2026-10-06T00:00:00Z', '2026-10-06T00:00:00Z', (SELECT id FROM projects WHERE is_default = 1));
            """);

        using var db = database.Open();
        var campaign = db.Campaigns.Single();
        Assert.Null(campaign.InboundWatermark);
        Assert.Null(campaign.InboundSyncedAt);
        Assert.Null(campaign.InboundSyncItemId);
        Assert.Equal(0, campaign.InboundOffset);
        Assert.Equal(0, campaign.InboundFailures);
    }

    /// <summary>The outstanding read names a work item that exists, or none.</summary>
    [Fact]
    public void The_outstanding_read_must_be_a_work_item_that_exists()
    {
        using var database = new TestDatabase();
        using (var db = database.Open())
        {
            db.Campaigns.Add(new Campaign { PublicId = "cmp_A", Name = "A", ProjectId = DefaultProject.Id, Status = CampaignStatus.Active, CreatedAt = Noon, UpdatedAt = Noon });
            db.SaveChanges();
        }

        var refused = Assert.Throws<SqliteException>(() => Execute(database.File, "UPDATE campaigns SET inbound_sync_item_id = 999;"));
        Assert.Contains("FOREIGN KEY", refused.Message, StringComparison.Ordinal);

        // And nothing cascades: a work item is never deleted, and the campaign's row is not where its life is decided.
        var reference = Rows(database.File, "SELECT \"table\", \"to\", on_delete FROM pragma_foreign_key_list('campaigns') WHERE \"from\" = 'inbound_sync_item_id'");
        Assert.Equal(["work_items|id|RESTRICT"], reference);
    }

    [Fact]
    public void A_campaign_round_trips_its_read()
    {
        using var database = new TestDatabase();
        using (var db = database.Open())
        {
            var campaign = new Campaign { PublicId = "cmp_A", Name = "A", ProjectId = DefaultProject.Id, Status = CampaignStatus.Active, CreatedAt = Noon, UpdatedAt = Noon };
            var item = new WorkItem
            {
                PublicId = "wi_1",
                Campaign = campaign,
                Kind = WorkItemKind.ProviderOp,
                Operation = "conversation.list",
                Status = WorkItemStatus.Created,
                CreatedByType = ActorType.System,
                CreatedById = "dispatcher",
                CreatedAt = Noon,
                UpdatedAt = Noon,
            };
            db.WorkItems.Add(item);
            db.SaveChanges();

            campaign.InboundWatermark = Noon.AddHours(-1);
            campaign.InboundSyncedAt = Noon;
            campaign.InboundSyncItemId = item.Id;
            campaign.InboundOffset = 200;
            campaign.InboundFailures = 3;
            db.SaveChanges();
        }

        using (var db = database.Open())
        {
            var campaign = db.Campaigns.Single();
            Assert.Equal(Noon.AddHours(-1), campaign.InboundWatermark);
            Assert.Equal(DateTimeKind.Utc, campaign.InboundWatermark!.Value.Kind);
            Assert.Equal(Noon, campaign.InboundSyncedAt);
            Assert.Equal(db.WorkItems.Single().Id, campaign.InboundSyncItemId);
            Assert.Equal(200, campaign.InboundOffset);
            Assert.Equal(3, campaign.InboundFailures);
        }
    }

    /// <summary>
    /// An installation upgrading into the inbound read owes no read of its history: a campaign that is live, or paused
    /// and able to go live again, starts from the moment of the upgrade, and one that has never been live, or never will
    /// be again, has nothing to start from until it goes live.
    /// </summary>
    [Fact]
    public void An_upgraded_installation_reads_its_live_campaigns_from_the_upgrade_and_no_others()
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

            INSERT INTO contacts (public_id, custom_json, created_at, updated_at)
            VALUES ('cnt_old', '{}', '2026-10-01T00:00:00Z', '2026-10-01T00:00:00Z');

            INSERT INTO campaign_contacts (campaign_id, contact_id, state, added_at, updated_at)
            VALUES (1, 1, 'enrolled', '2026-10-01T00:00:00Z', '2026-10-01T00:00:00Z');

            INSERT INTO campaign_routes (campaign_id, operation, plugin_id, updated_at)
            VALUES (1, NULL, 'reader', '2026-10-01T00:00:00Z');

            INSERT INTO external_ids (campaign_id, plugin_id, kind, value, recorded_at, recorded_by_attempt_id)
            VALUES (1, 'reader', 'campaign', 'seq-1', '2026-10-01T00:00:00Z', 'att_old');

            INSERT INTO work_items (public_id, campaign_id, kind, status, priority, created_by_type, context_json, attempt_count, created_at, updated_at, lineage_state)
            VALUES ('wi_old', 1, 'ai_role', 'created', 0, 'human', '{}', 0, '2026-10-01T00:00:00Z', '2026-10-01T00:00:00Z', 'root');

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
                var watermark = campaigns[live].InboundWatermark;
                Assert.NotNull(watermark);
                Assert.InRange(watermark!.Value, before, after);
            }

            Assert.Null(campaigns["cmp_draft"].InboundWatermark);
            Assert.Null(campaigns["cmp_archived"].InboundWatermark);
            Assert.All(campaigns.Values, campaign =>
            {
                Assert.Null(campaign.InboundSyncedAt);
                Assert.Null(campaign.InboundSyncItemId);
                Assert.Equal(0, campaign.InboundOffset);
                Assert.Equal(0, campaign.InboundFailures);
            });
            Assert.Equal("a", campaigns["cmp_active"].Name);
        }

        // Everything that refers to a campaign is still there, and still refers to one.
        foreach (var table in new[] { "campaign_contacts", "campaign_routes", "external_ids", "work_items", "journal" })
        {
            Assert.Equal(["1"], Rows(dir.Paths.DatabaseFile, $"SELECT COUNT(*) FROM {table}"));
        }

        Assert.Empty(Rows(dir.Paths.DatabaseFile, "SELECT * FROM pragma_foreign_key_check"));
    }

    /// <summary>
    /// A campaign says an observation once, and the database is what says so: a second line of the kind under a key the
    /// campaign already holds is refused. Another campaign may hold the same key, and so may a line of any other kind.
    /// </summary>
    [Fact]
    public void An_observation_is_said_once_per_campaign()
    {
        using var database = new TestDatabase();
        Execute(
            database.File,
            """
            INSERT INTO campaigns (public_id, name, status, context_json, created_at, updated_at, project_id) VALUES
              ('cmp_A', 'a', 'active', '{}', '2026-10-06T00:00:00Z', '2026-10-06T00:00:00Z', (SELECT id FROM projects WHERE is_default = 1)),
              ('cmp_B', 'b', 'active', '{}', '2026-10-06T00:00:00Z', '2026-10-06T00:00:00Z', (SELECT id FROM projects WHERE is_default = 1));

            INSERT INTO journal (public_id, ts, actor_type, actor_id, kind, campaign_id, key) VALUES
              ('jrn_1', '2026-10-06T00:00:00Z', 'system', 'dispatcher', 'inbound_reply_observed', 1, 'sha256:k'),
              ('jrn_2', '2026-10-06T00:00:00Z', 'system', 'dispatcher', 'inbound_reply_observed', 2, 'sha256:k'),
              ('jrn_3', '2026-10-06T00:00:00Z', 'human', 'ada', 'observation', 1, 'sha256:k'),
              ('jrn_4', '2026-10-06T00:00:00Z', 'human', 'ada', 'observation', 1, 'sha256:k');
            """);

        var refused = Assert.Throws<SqliteException>(() => Execute(
            database.File,
            "INSERT INTO journal (public_id, ts, actor_type, actor_id, kind, campaign_id, key) VALUES ('jrn_5', '2026-10-06T00:00:00Z', 'system', 'dispatcher', 'inbound_reply_observed', 1, 'sha256:k');"));
        Assert.Contains("UNIQUE", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Each index is written twice, in the model and in the migration that built it; nothing else ties them together.
    /// Pins may share a value across plugins and kinds, so that index is not unique. The journal's holds one kind only,
    /// and is unique: a campaign says each observation once.
    /// </summary>
    [Theory]
    [InlineData(typeof(ExternalId), "ix_external_ids_plugin_kind_value", "external_ids", new[] { "plugin_id", "kind", "value" }, false, null)]
    [InlineData(typeof(JournalEntry), "ix_journal_campaign_kind_key", "journal", new[] { "campaign_id", "kind", "key" }, true, "kind = 'inbound_reply_observed'")]
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

        var migration = File.ReadAllText(Directory.GetFiles(MigrationsDirectory(), "*_InboundSync.cs").Single());
        var start = migration.IndexOf($"name: \"{name}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"The migration creates no index named {name}.");
        var call = migration[start..migration.IndexOf(");", start, StringComparison.Ordinal)];
        Assert.Contains($"table: \"{table}\"", call, StringComparison.Ordinal);
        Assert.Contains("new[] { " + string.Join(", ", columns.Select(column => $"\"{column}\"")) + " }", call, StringComparison.Ordinal);
        Assert.Equal(unique, call.Contains("unique: true", StringComparison.Ordinal));
        if (filter is not null)
        {
            Assert.Contains($"filter: \"{filter}\"", call, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain("filter:", call, StringComparison.Ordinal);
        }

        using var connection = new SqliteConnection($"Data Source={database.File}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT name FROM pragma_index_info('{name}') ORDER BY seqno";
        var actual = new List<string>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                actual.Add(reader.GetString(0));
            }
        }

        Assert.Equal(columns, actual);
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
