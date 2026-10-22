using Jason.Contracts.Api;
using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Persistence;

/// <summary>
/// A caller's key belongs to the entity it created, and the database is what says one key names one entity: two
/// writers deciding "is this key free?" by looking would both see yes and both insert. A campaign's key is unique in
/// the installation, a work item's in its campaign, and an entity made without a key is never in anybody's way.
/// </summary>
public class IdempotencySchemaTests
{
    private const string PreviousMigration = "20260919064112_ManagerEscalation";

    private static readonly DateTime Noon = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void One_key_names_one_campaign_in_the_installation()
    {
        using var database = new TestDatabase();
        using var db = database.Open();

        db.Campaigns.Add(Campaign("cmp_A", key: "launch-q3"));
        db.SaveChanges();

        db.Campaigns.Add(Campaign("cmp_B", key: "launch-q3"));
        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }

    [Fact]
    public void Campaigns_made_without_a_key_are_never_in_each_other_s_way()
    {
        using var database = new TestDatabase();
        using var db = database.Open();

        db.Campaigns.Add(Campaign("cmp_A", key: null));
        db.Campaigns.Add(Campaign("cmp_B", key: null));
        db.SaveChanges();

        Assert.Equal(2, db.Campaigns.Count());
    }

    [Fact]
    [Critical(Family.W8, "the key index")]
    public void One_key_names_one_work_item_in_its_campaign_and_another_campaign_has_keys_of_its_own()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var first = Campaign("cmp_A", key: null);
        var second = Campaign("cmp_B", key: null);
        db.Campaigns.AddRange(first, second);
        db.WorkItems.Add(Item(first, "wi_1", key: "research-1"));
        db.WorkItems.Add(Item(second, "wi_2", key: "research-1"));
        db.WorkItems.Add(Item(first, "wi_3", key: null));
        db.WorkItems.Add(Item(first, "wi_4", key: null));
        db.SaveChanges();

        db.WorkItems.Add(Item(first, "wi_5", key: "research-1"));
        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }

    /// <summary>
    /// An installation upgrading into keys keeps every row it had, and none of them has a key: nothing that existed
    /// before can be answered as somebody's repeat.
    /// </summary>
    [Fact]
    public void An_upgraded_installation_keeps_every_row_and_none_of_them_has_a_key()
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
            INSERT INTO campaigns (public_id, name, status, context_json, created_at, updated_at)
            VALUES ('cmp_old', 'from before', 'active', '{}', '2026-10-01T00:00:00Z', '2026-10-01T00:00:00Z');

            INSERT INTO work_items (public_id, campaign_id, kind, status, priority, created_by_type, context_json, attempt_count, created_at, updated_at, lineage_state)
            VALUES ('wi_old', 1, 'ai_role', 'created', 0, 'human', '{}', 0, '2026-10-01T00:00:00Z', '2026-10-01T00:00:00Z', 'root');
            """);

        using (var db = new JasonDbContext(options))
        {
            db.Database.Migrate();
        }

        using (var db = new JasonDbContext(options))
        {
            var campaign = db.Campaigns.Single(c => c.PublicId == "cmp_old");
            var item = db.WorkItems.Single(w => w.PublicId == "wi_old");
            Assert.Equal("from before", campaign.Name);
            Assert.Null(campaign.IdempotencyKey);
            Assert.Null(campaign.IdempotencyDigest);
            Assert.Null(item.IdempotencyKey);
            Assert.Null(item.IdempotencyDigest);
        }
    }

    /// <summary>
    /// Each index's filter is written twice, in the model and in the migration that built it. Nothing else ties them
    /// together, and a fresh database and an upgraded one would differ if they parted.
    /// </summary>
    [Theory]
    [InlineData(typeof(Campaign), "ix_campaigns_one_per_idempotency_key")]
    [InlineData(typeof(WorkItem), "ix_work_items_one_per_campaign_idempotency_key")]
    public void Each_key_index_is_unique_and_filtered_the_same_way_in_the_model_and_the_migration(Type entity, string name)
    {
        using var database = new TestDatabase();
        using var db = database.Open();

        var index = db.Model.FindEntityType(entity)!.GetIndexes().Single(index => index.GetDatabaseName() == name);
        Assert.True(index.IsUnique);
        Assert.Equal("idempotency_key IS NOT NULL", index.GetFilter());

        // The index's own CreateIndex call, from its name to the end of the call: a filter elsewhere in the file says
        // nothing about this index, and SQLite's unique indexes let any number of nulls through, so nothing else notices.
        var migration = File.ReadAllText(Directory.GetFiles(MigrationsDirectory(), "*_IdempotencyKeys.cs").Single());
        var start = migration.IndexOf($"name: \"{name}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"The migration creates no index named {name}.");
        var call = migration[start..migration.IndexOf(");", start, StringComparison.Ordinal)];
        Assert.Contains("unique: true", call, StringComparison.Ordinal);
        Assert.Contains("filter: \"idempotency_key IS NOT NULL\"", call, StringComparison.Ordinal);
    }

    private static Campaign Campaign(string publicId, string? key) => new()
    {
        PublicId = publicId,
        Name = "Work",
        Status = CampaignStatus.Draft,
        CreatedAt = Noon,
        UpdatedAt = Noon,
        IdempotencyKey = key,
        IdempotencyDigest = key is null ? null : "sha256:" + new string('0', 64),
    };

    private static WorkItem Item(Campaign campaign, string publicId, string? key) => new()
    {
        PublicId = publicId,
        Campaign = campaign,
        Kind = WorkItemKind.AiRole,
        Role = "researcher",
        Status = WorkItemStatus.Created,
        CreatedByType = ActorType.Human,
        CreatedById = "ada",
        CreatedAt = Noon,
        UpdatedAt = Noon,
        IdempotencyKey = key,
        IdempotencyDigest = key is null ? null : "sha256:" + new string('0', 64),
    };

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

    private static void Execute(string databaseFile, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={databaseFile}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
