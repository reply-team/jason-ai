using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Operations;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Contacts;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Plugins;

/// <summary>
/// What a provider calls one of our entities is learned once and never rewritten. These tests are about the three
/// things that can happen when a plugin answers with an identifier — it is new, it agrees, or it disagrees — and
/// about what a plugin is allowed to see of what other plugins learned.
/// </summary>
public class ExternalIdStoreTests
{
    private static readonly DateTime Noon = new(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static OperationContract ListMembershipAdd => OperationCatalog.Find("list_membership.add")!;

    private static OperationContract CampaignEnroll => OperationCatalog.Find("campaign.enroll")!;

    [Fact]
    public async Task A_first_identifier_is_pinned_and_journaled()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db);

        var outcome = scene.Store.Apply(
            db,
            ListMembershipAdd,
            TestPlugins.FakeProviderId,
            scene.Item,
            scene.Campaign,
            scene.Contact,
            scene.First,
            new JsonObject { ["contact"] = "prov-1" });
        await db.SaveChangesAsync(Ct);

        Assert.True(outcome.Pinned);
        Assert.False(outcome.Diverged);
        Assert.Null(outcome.UndeclaredKind);

        var pin = Assert.Single(await db.ExternalIds.AsNoTracking().ToListAsync(Ct));
        Assert.Equal(scene.Contact.Id, pin.ContactId);
        Assert.Null(pin.CampaignId);
        Assert.Equal(TestPlugins.FakeProviderId, pin.PluginId);
        Assert.Equal("contact", pin.Kind);
        Assert.Equal("prov-1", pin.Value);
        Assert.Equal(Noon, pin.RecordedAt);
        Assert.Equal(scene.First.PublicId, pin.RecordedByAttemptId);
        Assert.Null(pin.DivergedValue);

        var entry = Assert.Single(await db.Journal.AsNoTracking().ToListAsync(Ct));
        Assert.Equal(JournalKinds.ExternalIdPinned, entry.Kind);
        Assert.Equal($"contact/{TestPlugins.FakeProviderId}/contact", entry.Key);
        Assert.Equal("prov-1", (string?)entry.New);
        Assert.Null(entry.Old);
        Assert.Equal(scene.Item.PublicId, entry.WorkItemId);
        Assert.Equal(scene.First.PublicId, entry.AttemptId);
        Assert.Equal(ActorType.Attempt, entry.ActorType);
        Assert.Equal(scene.First.PublicId, entry.ActorId);
    }

    [Fact]
    public async Task The_same_identifier_again_changes_nothing()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db);
        await scene.PinAsync(db, "prov-1");

        var outcome = scene.Store.Apply(
            db,
            ListMembershipAdd,
            TestPlugins.FakeProviderId,
            scene.Item,
            scene.Campaign,
            scene.Contact,
            scene.Second,
            new JsonObject { ["contact"] = "prov-1" });
        await db.SaveChangesAsync(Ct);

        Assert.False(outcome.Pinned);
        Assert.False(outcome.Diverged);

        var pin = Assert.Single(await db.ExternalIds.AsNoTracking().ToListAsync(Ct));
        Assert.Equal(scene.First.PublicId, pin.RecordedByAttemptId);
        Assert.Single(await db.Journal.AsNoTracking().ToListAsync(Ct));
    }

    [Fact]
    public async Task A_different_identifier_never_overwrites_the_first()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db);
        await scene.PinAsync(db, "prov-1");

        var outcome = scene.Store.Apply(
            db,
            ListMembershipAdd,
            TestPlugins.FakeProviderId,
            scene.Item,
            scene.Campaign,
            scene.Contact,
            scene.Second,
            new JsonObject { ["contact"] = "prov-2" });
        await db.SaveChangesAsync(Ct);

        Assert.False(outcome.Pinned);
        Assert.True(outcome.Diverged);

        var pin = Assert.Single(await db.ExternalIds.AsNoTracking().ToListAsync(Ct));
        Assert.Equal("prov-1", pin.Value);
        Assert.Equal(scene.First.PublicId, pin.RecordedByAttemptId);
        Assert.Equal("prov-2", pin.DivergedValue);
        Assert.Equal(Noon, pin.DivergedAt);
        Assert.Equal(scene.Second.PublicId, pin.DivergedByAttemptId);

        var entries = await db.Journal.AsNoTracking().OrderBy(entry => entry.Id).ToListAsync(Ct);
        Assert.Equal(2, entries.Count);
        Assert.Equal(JournalKinds.ExternalIdDiverged, entries[1].Kind);
        Assert.Equal($"contact/{TestPlugins.FakeProviderId}/contact", entries[1].Key);
        Assert.Equal("prov-1", (string?)entries[1].Old);
        Assert.Equal("prov-2", (string?)entries[1].New);
    }

    [Fact]
    public async Task A_repeated_disagreement_records_the_attempt_that_last_made_it()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db);
        await scene.PinAsync(db, "prov-1");
        scene.Store.Apply(db, ListMembershipAdd, TestPlugins.FakeProviderId, scene.Item, scene.Campaign, scene.Contact, scene.Second, new JsonObject { ["contact"] = "prov-2" });
        await db.SaveChangesAsync(Ct);

        var outcome = scene.Store.Apply(
            db,
            ListMembershipAdd,
            TestPlugins.FakeProviderId,
            scene.Item,
            scene.Campaign,
            scene.Contact,
            scene.Third,
            new JsonObject { ["contact"] = "prov-2" });
        await db.SaveChangesAsync(Ct);

        Assert.True(outcome.Diverged);
        var pin = Assert.Single(await db.ExternalIds.AsNoTracking().ToListAsync(Ct));
        Assert.Equal("prov-1", pin.Value);
        Assert.Equal("prov-2", pin.DivergedValue);
        Assert.Equal(scene.Third.PublicId, pin.DivergedByAttemptId);
    }

    [Fact]
    public async Task A_campaign_kind_is_pinned_to_the_campaign_and_a_contact_kind_to_the_contact()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db);

        var outcome = scene.Store.Apply(
            db,
            CampaignEnroll,
            TestPlugins.FakeProviderId,
            scene.Item,
            scene.Campaign,
            scene.Contact,
            scene.First,
            new JsonObject { ["contact"] = "person-1", ["campaign"] = "sequence-1" });
        await db.SaveChangesAsync(Ct);

        Assert.True(outcome.Pinned);
        var pins = await db.ExternalIds.AsNoTracking().OrderBy(pin => pin.Kind).ToListAsync(Ct);
        Assert.Equal(2, pins.Count);
        Assert.Equal(scene.Campaign.Id, pins[0].CampaignId);
        Assert.Equal("sequence-1", pins[0].Value);
        Assert.Equal(scene.Contact.Id, pins[1].ContactId);
        Assert.Equal("person-1", pins[1].Value);

        var keys = await db.Journal.AsNoTracking().Select(entry => entry.Key).ToListAsync(Ct);
        Assert.Contains($"campaign/{TestPlugins.FakeProviderId}/campaign", keys, StringComparer.Ordinal);
        Assert.Contains($"contact/{TestPlugins.FakeProviderId}/contact", keys, StringComparer.Ordinal);
    }

    [Fact]
    public async Task An_undeclared_kind_never_reaches_the_table_and_the_declared_one_beside_it_still_does()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db);

        var outcome = scene.Store.Apply(
            db,
            ListMembershipAdd,
            TestPlugins.FakeProviderId,
            scene.Item,
            scene.Campaign,
            scene.Contact,
            scene.First,
            new JsonObject { ["contact"] = "prov-1", ["invoice"] = "inv-9" });
        await db.SaveChangesAsync(Ct);

        Assert.True(outcome.Pinned);
        Assert.Equal("invoice", outcome.UndeclaredKind);
        var pin = Assert.Single(await db.ExternalIds.AsNoTracking().ToListAsync(Ct));
        Assert.Equal("contact", pin.Kind);
    }

    /// <summary>
    /// Built rather than parsed: a plugin's answer reaches the store as nodes, and the store has to survive a node
    /// that is not an identifier at all without throwing and without writing nonsense into Jason's record.
    /// </summary>
    [Fact]
    public async Task An_identifier_that_is_not_a_usable_string_is_not_pinned()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db);

        var outcome = scene.Store.Apply(
            db,
            CampaignEnroll,
            TestPlugins.FakeProviderId,
            scene.Item,
            scene.Campaign,
            scene.Contact,
            scene.First,
            new JsonObject
            {
                ["contact"] = JsonValue.Create(42),
                ["campaign"] = new JsonObject { ["id"] = "sequence-1" },
            });
        await db.SaveChangesAsync(Ct);

        Assert.False(outcome.Pinned);
        Assert.False(outcome.Diverged);
        Assert.Empty(await db.ExternalIds.AsNoTracking().ToListAsync(Ct));
        Assert.Empty(await db.Journal.AsNoTracking().ToListAsync(Ct));
    }

    /// <summary>
    /// Built, not parsed. What a provider calls somebody is printed into an operator's terminal by
    /// <c>contact get --human</c>, so a value carrying a newline or an escape sequence would forge a row of that
    /// table or clear the screen around it. It never becomes a pin, which is the only place it could be read
    /// from later.
    /// </summary>
    [Fact]
    public async Task An_identifier_carrying_a_control_character_is_never_written_down()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db);

        var outcome = scene.Store.Apply(
            db,
            CampaignEnroll,
            TestPlugins.FakeProviderId,
            scene.Item,
            scene.Campaign,
            scene.Contact,
            scene.First,
            new JsonObject
            {
                ["contact"] = "p_884" + '\u001b' + "[2Jgone",
                ["campaign"] = "c_77" + '\n' + "14  forged  row",
            });
        await db.SaveChangesAsync(Ct);

        Assert.False(outcome.Pinned);
        Assert.False(outcome.Diverged);
        Assert.Empty(await db.ExternalIds.AsNoTracking().ToListAsync(Ct));
        Assert.Empty(await db.Journal.AsNoTracking().ToListAsync(Ct));
    }

    [Fact]
    public async Task Nothing_returned_is_an_ordinary_answer()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db);

        var outcome = scene.Store.Apply(db, ListMembershipAdd, TestPlugins.FakeProviderId, scene.Item, scene.Campaign, scene.Contact, scene.First, externalIds: null);
        await db.SaveChangesAsync(Ct);

        Assert.False(outcome.Pinned);
        Assert.False(outcome.Diverged);
        Assert.Null(outcome.UndeclaredKind);
        Assert.Empty(await db.ExternalIds.AsNoTracking().ToListAsync(Ct));
    }

    [Fact]
    public async Task A_plugin_never_sees_another_plugin_s_identifiers()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db);
        await scene.PinAsync(db, "A");
        await scene.PinAsync(db, "B", plugin: TestPlugins.OtherProviderId);

        Assert.Equal(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["contact"] = "A" },
            scene.Store.PinsFor(scene.Contact, TestPlugins.FakeProviderId));
        Assert.Empty(scene.Store.PinsFor(scene.Campaign, TestPlugins.FakeProviderId));
    }

    [Fact]
    public async Task A_diverged_pin_passes_the_value_Jason_recorded_not_the_one_it_disputes()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db);
        await scene.PinAsync(db, "prov-1");
        scene.Store.Apply(db, ListMembershipAdd, TestPlugins.FakeProviderId, scene.Item, scene.Campaign, scene.Contact, scene.Second, new JsonObject { ["contact"] = "prov-2" });
        await db.SaveChangesAsync(Ct);

        var seen = scene.Store.PinsFor(scene.Contact, TestPlugins.FakeProviderId);

        Assert.Equal("prov-1", seen["contact"]);
    }

    [Fact]
    public async Task A_contact_is_read_with_the_pins_of_every_plugin_that_knows_it()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db);
        await scene.PinAsync(db, "A");
        await scene.PinAsync(db, "B", plugin: TestPlugins.OtherProviderId);
        var clock = new FixedClock(new DateTimeOffset(Noon));

        var contact = await new ContactService(db, new JournalWriter(clock), clock, TestCanceller.New(clock))
            .GetAsync(new ContactGetRequest(scene.Contact.PublicId), Ct);

        Assert.Collection(
            contact.ExternalIds,
            first =>
            {
                Assert.Equal(TestPlugins.FakeProviderId, first.PluginId);
                Assert.Equal("contact", first.Kind);
                Assert.Equal("A", first.Value);
                Assert.Equal(new DateTimeOffset(Noon), first.RecordedAt);
                Assert.Equal(scene.First.PublicId, first.RecordedByAttemptId);
                Assert.Null(first.DivergedValue);
            },
            second =>
            {
                Assert.Equal(TestPlugins.OtherProviderId, second.PluginId);
                Assert.Equal("B", second.Value);
            });
    }

    [Fact]
    public async Task A_campaign_is_read_with_its_pins_and_a_divergence_carries_both_values()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SeedAsync(db);
        scene.Store.Apply(db, CampaignEnroll, TestPlugins.FakeProviderId, scene.Item, scene.Campaign, scene.Contact, scene.First, new JsonObject { ["campaign"] = "seq-1" });
        await db.SaveChangesAsync(Ct);
        scene.Store.Apply(db, CampaignEnroll, TestPlugins.FakeProviderId, scene.Item, scene.Campaign, scene.Contact, scene.Second, new JsonObject { ["campaign"] = "seq-2" });
        await db.SaveChangesAsync(Ct);
        var clock = new FixedClock(new DateTimeOffset(Noon));

        var campaign = await new CampaignService(db, new JournalWriter(clock), clock, TestCanceller.New(clock))
            .GetAsync(new CampaignGetRequest(scene.Campaign.PublicId), Ct);

        var pin = Assert.Single(campaign.ExternalIds);
        Assert.Equal("seq-1", pin.Value);
        Assert.Equal("seq-2", pin.DivergedValue);
        Assert.Equal(new DateTimeOffset(Noon), pin.DivergedAt);
        Assert.Equal(scene.Second.PublicId, pin.DivergedByAttemptId);
    }

    private static async Task<Scene> SeedAsync(JasonDbContext db)
    {
        var clock = new FixedClock(new DateTimeOffset(Noon));
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var item = WorkItemFactory.NewProviderOp(campaign, "list_membership.add", Noon);
        var contact = new Contact { PublicId = PublicId.New("cnt"), CreatedAt = Noon, UpdatedAt = Noon };
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(item);
        db.Contacts.Add(contact);
        var scene = new Scene(
            new ExternalIdStore(new JournalWriter(clock), clock),
            campaign,
            contact,
            item,
            // Finished attempts: the database allows only one live attempt per item, and a pin outlives the run
            // that learned it anyway.
            WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Succeeded, Noon),
            WorkItemFactory.NewAttempt(item, 2, AttemptStatus.Succeeded, Noon),
            WorkItemFactory.NewAttempt(item, 3, AttemptStatus.Succeeded, Noon));
        await db.SaveChangesAsync(Ct);
        return scene;
    }

    private sealed record Scene(
        ExternalIdStore Store,
        Campaign Campaign,
        Contact Contact,
        WorkItem Item,
        Attempt First,
        Attempt Second,
        Attempt Third)
    {
        public async Task PinAsync(JasonDbContext db, string value, string plugin = TestPlugins.FakeProviderId)
        {
            Store.Apply(db, ListMembershipAdd, plugin, Item, Campaign, Contact, First, new JsonObject { ["contact"] = value });
            await db.SaveChangesAsync(Ct);
        }
    }
}
