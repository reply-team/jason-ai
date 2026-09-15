using System.Text.Json.Nodes;
using Jason.Contracts.Ids;
using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Persistence;

/// <summary>
/// What the new tables promise, asserted as rules rather than as columns: a list of column names passes just as
/// happily for a typo, while a rule only passes when the database really refuses what it must refuse.
/// </summary>
public class RoutingSchemaTests
{
    private static readonly DateTime Noon = new(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task A_pin_belongs_to_exactly_one_entity(bool contact, bool campaign)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (contactId, campaignId) = await SeedAsync(db);

        db.Set<ExternalId>().Add(NewPin(
            contactId: contact ? contactId : null,
            campaignId: campaign ? campaignId : null));

        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        Assert.Contains("ck_external_ids_one_entity", failure.InnerException!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_plugin_pins_one_value_per_kind_on_a_contact()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (contactId, _) = await SeedAsync(db);
        db.Set<ExternalId>().Add(NewPin(contactId: contactId, kind: "contact", value: "p-1"));
        await db.SaveChangesAsync(Ct);

        db.Set<ExternalId>().Add(NewPin(contactId: contactId, kind: "contact", value: "p-2"));

        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        Assert.Contains("UNIQUE", failure.InnerException!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_plugin_pins_one_value_per_kind_on_a_campaign()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (_, campaignId) = await SeedAsync(db);
        db.Set<ExternalId>().Add(NewPin(campaignId: campaignId, kind: "campaign", value: "s-1"));
        await db.SaveChangesAsync(Ct);

        db.Set<ExternalId>().Add(NewPin(campaignId: campaignId, kind: "campaign", value: "s-2"));

        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        Assert.Contains("UNIQUE", failure.InnerException!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Another_plugin_and_another_kind_pin_the_same_entity_freely()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (contactId, _) = await SeedAsync(db);

        db.Set<ExternalId>().Add(NewPin(contactId: contactId, plugin: "one", kind: "contact", value: "a"));
        db.Set<ExternalId>().Add(NewPin(contactId: contactId, plugin: "two", kind: "contact", value: "b"));
        db.Set<ExternalId>().Add(NewPin(contactId: contactId, plugin: "one", kind: "lead", value: "c"));
        await db.SaveChangesAsync(Ct);

        Assert.Equal(3, await db.Set<ExternalId>().CountAsync(Ct));
    }

    [Fact]
    public async Task Pins_vanish_with_the_entity_they_name()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (contactId, campaignId) = await SeedAsync(db);
        db.Set<ExternalId>().Add(NewPin(contactId: contactId, kind: "contact", value: "p-1"));
        db.Set<ExternalId>().Add(NewPin(campaignId: campaignId, kind: "campaign", value: "s-1"));
        await db.SaveChangesAsync(Ct);

        // Deleted in the database, not in the change tracker: the cascade under test is the schema's, not EF's.
        await db.Contacts.Where(c => c.Id == contactId).ExecuteDeleteAsync(Ct);

        var remaining = await db.Set<ExternalId>().AsNoTracking().ToListAsync(Ct);
        var kept = Assert.Single(remaining);
        Assert.Equal(campaignId, kept.CampaignId);
    }

    [Fact]
    public async Task A_campaign_routes_an_operation_once()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (_, campaignId) = await SeedAsync(db);
        db.Set<CampaignRoute>().Add(NewRoute(campaignId, "contact.upsert", "one"));
        await db.SaveChangesAsync(Ct);

        db.Set<CampaignRoute>().Add(NewRoute(campaignId, "contact.upsert", "two"));

        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        Assert.Contains("UNIQUE", failure.InnerException!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_campaign_has_at_most_one_default_route()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (_, campaignId) = await SeedAsync(db);
        db.Set<CampaignRoute>().Add(NewRoute(campaignId, operation: null, plugin: "one"));
        await db.SaveChangesAsync(Ct);

        db.Set<CampaignRoute>().Add(NewRoute(campaignId, operation: null, plugin: "two"));

        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        Assert.Contains("UNIQUE", failure.InnerException!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_default_route_and_an_operation_route_live_side_by_side()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (_, campaignId) = await SeedAsync(db);

        db.Set<CampaignRoute>().Add(NewRoute(campaignId, operation: null, plugin: "one"));
        db.Set<CampaignRoute>().Add(NewRoute(campaignId, "contact.upsert", "two"));
        await db.SaveChangesAsync(Ct);

        Assert.Equal(2, await db.Set<CampaignRoute>().CountAsync(Ct));
    }

    [Fact]
    public async Task A_route_binding_must_be_json()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        await SeedAsync(db);
        using var connection = Connect(database);

        var failure = Assert.Throws<SqliteException>(() => Execute(
            connection,
            "INSERT INTO campaign_routes (campaign_id, plugin_id, binding_json, updated_at) VALUES (1, 'one', '{not json', '2026-01-01')"));

        Assert.Contains("CHECK constraint failed", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Written as raw statements rather than through the entity: the converter would only prove itself, and the
    /// column is also written by guarded updates that never pass through it. Null is the normal case — an agent
    /// attempt has no provenance at all — so the guard must let it through.
    /// </summary>
    [Fact]
    public async Task Provenance_that_is_not_json_is_refused_when_it_is_written_and_when_it_is_changed()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        await SeedAsync(db);
        using var connection = Connect(database);

        var onInsert = Assert.Throws<SqliteException>(() => Execute(connection, Insert("'{not json'")));
        Execute(connection, Insert("NULL"));
        Execute(connection, "UPDATE attempts SET provenance_json = '{\"plugin_id\":\"one\"}' WHERE public_id = 'att_X'");
        var onUpdate = Assert.Throws<SqliteException>(() => Execute(connection, "UPDATE attempts SET provenance_json = 'nonsense' WHERE public_id = 'att_X'"));

        Assert.Contains("provenance", onInsert.Message, StringComparison.Ordinal);
        Assert.Contains("provenance", onUpdate.Message, StringComparison.Ordinal);
        Assert.Equal("{\"plugin_id\":\"one\"}", Scalar(connection, "SELECT provenance_json FROM attempts WHERE public_id = 'att_X'"));
    }

    private static string Insert(string provenance) =>
        "INSERT INTO attempts (public_id, work_item_id, number, command, status, context_snapshot_json, claimed_at, lock_until, provenance_json)"
        + $" VALUES ('att_X', 1, 1, 'provider_op', 'running', '{{}}', '2026-01-01', '2026-01-01', {provenance})";

    private static SqliteConnection Connect(TestDatabase database)
    {
        var connection = new SqliteConnection($"Data Source={database.File}");
        connection.Open();
        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar() as string;
    }

    private static ExternalId NewPin(
        int? contactId = null,
        int? campaignId = null,
        string plugin = "one",
        string kind = "contact",
        string value = "p-1") =>
        new()
        {
            ContactId = contactId,
            CampaignId = campaignId,
            PluginId = plugin,
            Kind = kind,
            Value = value,
            RecordedAt = Noon,
            RecordedByAttemptId = PublicId.New("att"),
        };

    private static CampaignRoute NewRoute(int campaignId, string? operation, string plugin) =>
        new()
        {
            CampaignId = campaignId,
            Operation = operation,
            PluginId = plugin,
            Binding = new JsonObject { ["account"] = "main" },
            UpdatedAt = Noon,
        };

    /// <summary>One campaign and one contact, and a work item so an attempt row has something to belong to.</summary>
    private static async Task<(int ContactId, int CampaignId)> SeedAsync(JasonDbContext db)
    {
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var item = WorkItemFactory.NewProviderOp(campaign, now: Noon);
        var contact = new Contact { PublicId = PublicId.New("cnt"), CreatedAt = Noon, UpdatedAt = Noon };
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(item);
        db.Contacts.Add(contact);
        await db.SaveChangesAsync(Ct);
        return (contact.Id, campaign.Id);
    }
}
