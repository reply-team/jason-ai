using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Campaigns;

public class CampaignContextTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Setting_keys_stores_them_and_journals_one_entry_per_key()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var service = NewService(db, clock);
        var id = await CreateAsync(service);
        clock.Advance(TimeSpan.FromMinutes(2));

        var updated = await service.UpdateContextAsync(
            new CampaignUpdateContextRequest(id, new JsonObject { ["icp"] = "founders", ["region"] = "latam" }, null, null, "first pass"),
            Ct);

        Assert.Equal("founders", (string?)updated.Context["icp"]);
        Assert.Equal("latam", (string?)updated.Context["region"]);
        Assert.Equal(Noon.AddMinutes(2), updated.UpdatedAt);

        var entries = await ContextEntriesAsync(db);
        Assert.Equal(2, entries.Count);
        Assert.Equal(["icp", "region"], entries.Select(e => e.Key));
        Assert.Null(entries[0].Old);
        Assert.Equal("founders", (string?)entries[0].New);
        Assert.Equal("first pass", entries[0].Reason);
    }

    [Fact]
    public async Task Setting_a_key_to_the_value_it_already_has_changes_nothing()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var service = NewService(db, clock);
        var id = await CreateAsync(service);
        await service.UpdateContextAsync(new CampaignUpdateContextRequest(id, new JsonObject { ["icp"] = "founders" }, null, null, null), Ct);
        var written = await db.Journal.CountAsync(Ct);
        clock.Advance(TimeSpan.FromMinutes(2));

        var again = await service.UpdateContextAsync(new CampaignUpdateContextRequest(id, new JsonObject { ["icp"] = "founders" }, null, null, null), Ct);

        Assert.Equal("founders", (string?)again.Context["icp"]);
        Assert.Equal(Noon, again.UpdatedAt);
        Assert.Equal(written, await db.Journal.CountAsync(Ct));
    }

    [Fact]
    public async Task Unsetting_removes_a_key_and_journals_it_while_a_missing_key_is_silent()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));
        var id = await CreateAsync(service);
        await service.UpdateContextAsync(new CampaignUpdateContextRequest(id, new JsonObject { ["icp"] = "founders" }, null, null, null), Ct);

        var removed = await service.UpdateContextAsync(new CampaignUpdateContextRequest(id, null, ["icp", "never_there"], null, "out of date"), Ct);

        Assert.Empty(removed.Context);
        var entry = Assert.Single(await ContextEntriesAsync(db), e => e.New is null);
        Assert.Equal("icp", entry.Key);
        Assert.Equal("founders", (string?)entry.Old);
        Assert.Equal("out of date", entry.Reason);
    }

    [Fact]
    public async Task A_json_null_is_a_value_that_only_unset_removes()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));
        var id = await CreateAsync(service);

        var withNull = await service.UpdateContextAsync(new CampaignUpdateContextRequest(id, new JsonObject { ["budget"] = null }, null, null, null), Ct);

        Assert.True(withNull.Context.ContainsKey("budget"));
        Assert.Null(withNull.Context["budget"]);
        var written = Assert.Single(await ContextEntriesAsync(db));
        Assert.Equal("budget", written.Key);
        Assert.Null(written.New);

        var cleared = await service.UpdateContextAsync(new CampaignUpdateContextRequest(id, null, ["budget"], null, null), Ct);

        Assert.False(cleared.Context.ContainsKey("budget"));
        Assert.Equal(2, (await ContextEntriesAsync(db)).Count);
    }

    [Fact]
    public async Task Nested_values_are_stored_verbatim_and_survive_a_reload()
    {
        using var database = new TestDatabase();
        var offer = new JsonObject { ["type"] = "demo", ["steps"] = new JsonArray(1, 2, 3), ["owner"] = new JsonObject { ["role"] = "sdr" } };
        string id;
        using (var db = database.Open())
        {
            var service = NewService(db, new FixedClock(Noon));
            id = await CreateAsync(service);
            await service.UpdateContextAsync(new CampaignUpdateContextRequest(id, new JsonObject { ["offer"] = offer.DeepClone() }, null, null, null), Ct);
        }

        using var reopened = database.Open();
        var reloaded = await NewService(reopened, new FixedClock(Noon)).GetAsync(new CampaignGetRequest(id), Ct);

        Assert.True(JsonNode.DeepEquals(offer, reloaded.Context["offer"]));
    }

    [Fact]
    public async Task A_context_is_accepted_at_the_limit_and_refused_one_byte_over_it()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));
        var id = await CreateAsync(service);

        // {"notes":"…"} costs twelve bytes around the value itself.
        var atLimit = await service.UpdateContextAsync(
            new CampaignUpdateContextRequest(id, new JsonObject { ["notes"] = new string('x', ContextRules.MaxBytes - 12) }, null, null, null),
            Ct);
        Assert.Equal(ContextRules.MaxBytes - 12, ((string?)atLimit.Context["notes"])!.Length);

        var error = await Assert.ThrowsAsync<InvalidRequestException>(
            () => service.UpdateContextAsync(
                new CampaignUpdateContextRequest(id, new JsonObject { ["notes"] = new string('x', ContextRules.MaxBytes - 11) }, null, null, null),
                Ct));

        Assert.Equal("context_too_large", error.Code);
        var stored = await service.GetAsync(new CampaignGetRequest(id), Ct);
        Assert.Equal(ContextRules.MaxBytes - 12, ((string?)stored.Context["notes"])!.Length);
    }

    [Fact]
    public async Task A_context_larger_than_the_limit_cannot_be_created_either()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));

        var error = await Assert.ThrowsAsync<InvalidRequestException>(
            () => service.CreateAsync(new CampaignCreateRequest("LatAm", new JsonObject { ["notes"] = new string('x', ContextRules.MaxBytes) }, null, null), Ct));

        Assert.Equal("context_too_large", error.Code);
        Assert.Empty(await db.Campaigns.ToListAsync(Ct));
    }

    [Fact]
    public async Task A_blank_context_key_is_refused()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));
        var id = await CreateAsync(service);

        var set = await Assert.ThrowsAsync<ValidationException>(
            () => service.UpdateContextAsync(new CampaignUpdateContextRequest(id, new JsonObject { ["  "] = "x" }, null, null, null), Ct));
        var unset = await Assert.ThrowsAsync<ValidationException>(
            () => service.UpdateContextAsync(new CampaignUpdateContextRequest(id, null, ["  "], null, null), Ct));

        Assert.Equal("set", Assert.Single(set.Details!).Field);
        Assert.Equal("unset[0]", Assert.Single(unset.Details!).Field);
    }

    [Fact]
    public async Task An_edit_that_neither_sets_nor_unsets_anything_leaves_the_campaign_alone()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var service = NewService(db, clock);
        var id = await CreateAsync(service);
        clock.Advance(TimeSpan.FromMinutes(2));

        var untouched = await service.UpdateContextAsync(new CampaignUpdateContextRequest(id, null, null, null, null), Ct);

        Assert.Empty(untouched.Context);
        Assert.Equal(Noon, untouched.UpdatedAt);
        Assert.Single(await db.Journal.ToListAsync(Ct));
    }

    private static async Task<List<JournalEntry>> ContextEntriesAsync(JasonDbContext db) =>
        await db.Journal.Where(e => e.Kind == JournalKinds.ContextUpdated).OrderBy(e => e.Id).ToListAsync(Ct);

    private static async Task<string> CreateAsync(CampaignService service) =>
        (await service.CreateAsync(new CampaignCreateRequest("LatAm", null, null, null), Ct)).Id;

    private static CampaignService NewService(JasonDbContext db, TimeProvider clock) => new(db, new JournalWriter(clock), clock);
}
