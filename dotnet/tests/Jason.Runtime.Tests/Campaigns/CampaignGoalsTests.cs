using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Harness;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Campaigns;

/// <summary>
/// The campaign's goals, the first reserved key of a campaign's context: held to their published document at
/// <c>campaign.create</c> and at every <c>campaign.update_context</c> whose <c>set</c> names the key, as a work item's
/// <c>context.input</c> is held to its operation; absent, null and empty are all valid, and a patch that does not name
/// the key never reads it again.
/// </summary>
public class CampaignGoalsTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The refusals a role is most likely to meet, with the field and the reason each is answered with.</summary>
    public static TheoryData<string, string, string> BadShapes() => new()
    {
        { """[{"id":"demo","title":"A demo","reached_when":"booked","order":1}]""", "context.goals/0/kind", "required" },
        { """[{"id":"demo","kind":"webinar","title":"A demo","reached_when":"booked","order":1}]""", "context.goals/0/kind", "enum" },
        { """{"id":"demo","kind":"demo","title":"A demo","reached_when":"booked","order":1}""", "context.goals", "type" },
        { """[{"id":"demo","kind":"demo","title":"A demo","reached_when":"booked","order":"first"}]""", "context.goals/0/order", "type" },
        { """[{"id":"demo","kind":"demo","title":"A demo","reached_when":"booked","order":1},{"id":"demo","kind":"trial","title":"A trial","reached_when":"signed up","order":2}]""", "context.goals/1/id", "unique_items" },
        { "\"a demo, then a trial\"", "context.goals", "type" },
    };

    [Fact]
    public async Task Goals_given_at_create_are_kept_as_written()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db);

        var created = await service.CreateAsync(new CampaignCreateRequest("Spring", Context(Goals()), null, null), Ct);

        Assert.True(JsonNode.DeepEquals(Goals(), created.Context["goals"]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    [InlineData("[]")]
    public async Task A_campaign_without_goals_is_valid(string? goals)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db);
        var context = goals is null ? new JsonObject { ["icp"] = "founders" } : new JsonObject { ["goals"] = JsonNode.Parse(goals) };

        var created = await service.CreateAsync(new CampaignCreateRequest("Spring", context, null, null), Ct);

        Assert.Equal(goals is null, !created.Context.ContainsKey("goals"));
    }

    [Theory]
    [MemberData(nameof(BadShapes))]
    public async Task Goals_that_break_the_document_are_refused_at_create_and_nothing_is_written(string goals, string field, string reason)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db);

        var refused = await Assert.ThrowsAsync<ValidationException>(
            () => service.CreateAsync(new CampaignCreateRequest("Spring", Context(JsonNode.Parse(goals)), null, null), Ct));

        var detail = Assert.Single(refused.Details!);
        Assert.Equal(field, detail.Field);
        Assert.Equal("invalid", detail.Code);
        Assert.Contains(reason, Reasons(goals), StringComparer.Ordinal);
        Assert.Equal(0, await db.Campaigns.CountAsync(Ct));
    }

    [Theory]
    [MemberData(nameof(BadShapes))]
    public async Task Goals_that_break_the_document_are_refused_at_update_and_the_context_is_left_alone(string goals, string field, string reason)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db);
        var id = (await service.CreateAsync(new CampaignCreateRequest("Spring", Context(Goals()), null, null), Ct)).Id;
        var written = await db.Journal.CountAsync(Ct);

        var refused = await Assert.ThrowsAsync<ValidationException>(
            () => service.UpdateContextAsync(new CampaignUpdateContextRequest(id, new JsonObject { ["goals"] = JsonNode.Parse(goals), ["icp"] = "founders" }, null, null, null), Ct));

        var detail = Assert.Single(refused.Details!);
        Assert.Equal(field, detail.Field);
        Assert.Equal("invalid", detail.Code);
        Assert.Contains(reason, Reasons(goals), StringComparer.Ordinal);

        var kept = await service.GetAsync(new CampaignGetRequest(id), Ct);
        Assert.True(JsonNode.DeepEquals(Goals(), kept.Context["goals"]));
        Assert.False(kept.Context.ContainsKey("icp"));
        Assert.Equal(written, await db.Journal.CountAsync(Ct));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    public async Task An_update_may_clear_the_goals_with_null_or_an_empty_list(string goals)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db);
        var id = (await service.CreateAsync(new CampaignCreateRequest("Spring", Context(Goals()), null, null), Ct)).Id;

        var updated = await service.UpdateContextAsync(new CampaignUpdateContextRequest(id, new JsonObject { ["goals"] = JsonNode.Parse(goals) }, null, null, null), Ct);

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(goals), updated.Context["goals"]));
    }

    [Fact]
    public async Task An_update_that_replaces_the_goals_with_valid_ones_is_written_and_journaled()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db);
        var id = (await service.CreateAsync(new CampaignCreateRequest("Spring", null, null, null), Ct)).Id;

        var updated = await service.UpdateContextAsync(new CampaignUpdateContextRequest(id, new JsonObject { ["goals"] = Goals() }, null, null, "settled with the person"), Ct);

        Assert.True(JsonNode.DeepEquals(Goals(), updated.Context["goals"]));
        Assert.Single(await db.Journal.Where(entry => entry.Kind == JournalKinds.ContextUpdated && entry.Key == "goals").ToListAsync(Ct));
    }

    /// <summary>
    /// The patch writes the set before it honours the unset, so a key named in both ends up gone: the document reads the
    /// goals the campaign will actually hold, which here are none.
    /// </summary>
    [Fact]
    public async Task Goals_both_set_and_unset_end_gone_and_are_not_read()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db);
        var id = (await service.CreateAsync(new CampaignCreateRequest("Spring", Context(Goals()), null, null), Ct)).Id;

        var updated = await service.UpdateContextAsync(new CampaignUpdateContextRequest(id, new JsonObject { ["goals"] = "not a list" }, ["goals"], null, null), Ct);

        Assert.False(updated.Context.ContainsKey("goals"));
    }

    /// <summary>
    /// Goals already stored are read as they stand: a patch that leaves the key alone never measures it again, even
    /// when what is stored would not pass today, which is what lets a document gain a rule without stranding every
    /// campaign written before it.
    /// </summary>
    [Fact]
    public async Task An_update_that_does_not_name_the_goals_never_reads_them_again()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db);
        var id = (await service.CreateAsync(new CampaignCreateRequest("Spring", null, null, null), Ct)).Id;
        var row = await db.Campaigns.SingleAsync(campaign => campaign.PublicId == id, Ct);
        row.Context = new JsonObject { ["goals"] = "written before the document" };
        await db.SaveChangesAsync(Ct);

        var updated = await service.UpdateContextAsync(new CampaignUpdateContextRequest(id, new JsonObject { ["icp"] = "founders" }, ["region"], null, null), Ct);

        Assert.Equal("founders", (string?)updated.Context["icp"]);
        Assert.Equal("written before the document", (string?)updated.Context["goals"]);
    }

    /// <summary>A pattern that does not finish is no verdict on the goals: nothing is written, and the answer is one to send again.</summary>
    [Fact]
    public async Task A_goal_id_whose_pattern_does_not_finish_is_answered_as_unmeasured()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var service = new CampaignService(db, new JournalWriter(clock), clock, TestCanceller.New(clock), TestHarness.FoldersFor(db), TimingOut.Always());

        var refused = await Assert.ThrowsAsync<DomainException>(
            () => service.CreateAsync(new CampaignCreateRequest("Spring", Context(Goals()), null, null), Ct));

        Assert.True(refused.Retryable);
        Assert.Contains(refused.Details!, detail => detail.Field == "context.goals/0/id" && detail.Code == SchemaValidator.PatternTimedOut);
        Assert.Equal(0, await db.Campaigns.CountAsync(Ct));
    }

    private static string[] Reasons(string goals) =>
        [.. Jason.Contracts.Context.ContextKeyCatalog.Find("goals")!.CheckValue(JsonNode.Parse(goals)).Select(problem => problem.Reason)];

    private static JsonArray Goals() => new(
        new JsonObject { ["id"] = "demo", ["kind"] = "demo", ["title"] = "A demo with the operations lead", ["reached_when"] = "a demo is on their calendar", ["order"] = 1 },
        new JsonObject { ["id"] = "trial", ["kind"] = "trial", ["title"] = "A two-week trial", ["reached_when"] = "their team has signed up", ["order"] = 2 });

    private static JsonObject Context(JsonNode? goals) => new() { ["goals"] = goals };

    private static CampaignService NewService(JasonDbContext db)
    {
        var clock = new FixedClock(Noon);
        return new CampaignService(db, new JournalWriter(clock), clock, TestCanceller.New(clock), TestHarness.FoldersFor(db));
    }
}
