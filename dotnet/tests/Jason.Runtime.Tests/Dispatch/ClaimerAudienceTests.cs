using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Plugins;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Manifest;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// A campaign's audiences, made at the claim. The first search under a definition the claim lets through — to run now, or
/// to wait for a person — makes the campaign's next audience; the same definition again, however its members are ordered,
/// finds that audience; a search the claim refuses makes none. A search names an audience by its number and is searched by
/// the definition that audience was made from, and every search carries the audience it runs under in its own context.
/// </summary>
public partial class ClaimerTests
{
    private const string Searcher = "stand-in-search";

    /// <summary>A first search whose start was made and whose answer never came: open again, with no row.</summary>
    private const string StartAnswerLost = "start answer lost";

    /// <summary>A first search the provider refused outright, for good: failed, with no row.</summary>
    private const string FailedForGood = "failed for good";

    private static JsonObject InSoftware => new() { ["industries"] = new JsonArray("Software"), ["titles"] = new JsonArray("Head of Sales") };

    private static JsonObject InLogistics => new() { ["industries"] = new JsonArray("Logistics"), ["titles"] = new JsonArray("Head of Operations") };

    /// <summary>
    /// Two definitions make audiences 1 and 2, in the order their searches were claimed; the first again, written with its
    /// members the other way round, is audience 1; another campaign numbers its own from 1. Each search carries its
    /// audience — the id, the number and the definition it is searched by — said once in the chronicle by the dispatcher,
    /// and the claim that runs a search a person approved finds the same audience and says nothing more.
    /// </summary>
    [Fact]
    public async Task Audience_FirstSearchUnderADefinition_MadeAtTheClaim_NumberedPerCampaign_FoundAgainByContent()
    {
        using var harness = SearchHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var spring = WorkItemFactory.NewCampaign("Spring", now: Noon);
        var autumn = WorkItemFactory.NewCampaign("Autumn", now: Noon);
        db.Campaigns.AddRange(spring, autumn);
        var software = Search(db, spring, new JsonObject { ["definition"] = InSoftware, ["count"] = 50 });
        var elsewhere = Search(db, autumn, new JsonObject { ["definition"] = InSoftware, ["count"] = 50 });
        await db.SaveChangesAsync(Ct);
        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        var logistics = Search(db, spring, new JsonObject { ["definition"] = InLogistics, ["count"] = 50 });
        await db.SaveChangesAsync(Ct);
        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        var reordered = new JsonObject { ["titles"] = new JsonArray("Head of Sales"), ["industries"] = new JsonArray("Software") };
        var again = Search(db, spring, new JsonObject { ["definition"] = reordered, ["count"] = 20 });
        await db.SaveChangesAsync(Ct);
        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using (var fresh = database.Open())
        {
            var rows = await fresh.Audiences.AsNoTracking().OrderBy(a => a.CampaignId).ThenBy(a => a.Number).ToListAsync(Ct);
            Assert.Equal([(spring.Id, 1), (spring.Id, 2), (autumn.Id, 1)], rows.Select(row => (row.CampaignId, row.Number)));
            Assert.All(rows, row => Assert.Equal(Noon, row.CreatedAt));
            Assert.True(JsonNode.DeepEquals(InSoftware, rows[0].Definition));
            Assert.True(JsonNode.DeepEquals(InLogistics, rows[1].Definition));

            await AssertCarriesAsync(fresh, software, rows[0], InSoftware);
            await AssertCarriesAsync(fresh, logistics, rows[1], InLogistics);
            await AssertCarriesAsync(fresh, again, rows[0], reordered);
            await AssertCarriesAsync(fresh, elsewhere, rows[2], InSoftware);
        }

        await ApproveAsync(db, software);
        var released = Assert.Single(await harness.Claimer.ClaimAsync(db, 4, Ct));

        Assert.Equal(software.PublicId, released.WorkItemPublicId);
        Assert.True(JsonNode.DeepEquals(InSoftware, released.Plan!.Input["definition"]));
        await using var after = database.Open();
        Assert.Equal(3, await after.Audiences.CountAsync(Ct));
        Assert.Equal(1, await after.Journal.CountAsync(e => e.WorkItemId == software.PublicId && e.Kind == JournalKinds.WorkItemContextUpdated, Ct));
    }

    /// <summary>
    /// A search the claim refuses becomes no audience and carries none: a number the campaign holds no audience under —
    /// here one another campaign holds — before anything is composed, and a definition off the routed plugin's schema.
    /// </summary>
    [Theory]
    [InlineData("""{"audience": 2, "count": 50}""", AttemptErrors.AudienceUnknown)]
    [InlineData("""{"definition": {"industries": "Software"}, "count": 50}""", AttemptErrors.DefinitionInvalid)]
    public async Task Audience_SearchRefusedAtTheClaim_MakesNone(string arguments, string code)
    {
        using var harness = SearchHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var spring = WorkItemFactory.NewCampaign("Spring", now: Noon);
        var autumn = WorkItemFactory.NewCampaign("Autumn", now: Noon);
        db.Campaigns.AddRange(spring, autumn);
        await HeldAsync(harness, db, autumn, InSoftware, InLogistics);

        var refused = Search(db, spring, JsonNode.Parse(arguments)!.AsObject());
        await db.SaveChangesAsync(Ct);
        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        var item = await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == refused.Id, Ct);
        Assert.Equal(WorkItemStatus.Failed, item.Status);
        Assert.Equal(code, (await fresh.Attempts.AsNoTracking().SingleAsync(a => a.WorkItemId == refused.Id, Ct)).Error!.Code);
        Assert.False(item.Context.ContainsKey("audience"));
        Assert.Equal([autumn.Id, autumn.Id], await fresh.Audiences.AsNoTracking().Select(a => a.CampaignId).ToListAsync(Ct));
    }

    /// <summary>
    /// A top-up names an audience by its number and is searched by the definition that audience was made from: the input
    /// put to a person for approval carries that definition at its root, the search carries it with its audience, and no
    /// audience is added.
    /// </summary>
    [Fact]
    public async Task Audience_TopUpByNumber_SearchedByTheAudiencesDefinition_CarriedOnTheSearch()
    {
        using var harness = SearchHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var spring = WorkItemFactory.NewCampaign("Spring", now: Noon);
        db.Campaigns.Add(spring);
        await HeldAsync(harness, db, spring, InSoftware, InLogistics);

        var topUp = Search(db, spring, new JsonObject { ["audience"] = 2, ["count"] = 20 });
        await db.SaveChangesAsync(Ct);
        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        Assert.Equal(2, await fresh.Audiences.CountAsync(Ct));
        var audience = await fresh.Audiences.AsNoTracking().SingleAsync(a => a.Number == 2, Ct);
        var asked = await fresh.Approvals.AsNoTracking().SingleAsync(a => a.WorkItemId == topUp.Id, Ct);
        Assert.True(JsonNode.DeepEquals(InLogistics, asked.Subject["input"]!["definition"]));
        Assert.True(JsonNode.DeepEquals(new JsonObject { ["audience"] = 2, ["count"] = 20 }, asked.Subject["input"]!["args"]));
        await AssertCarriesAsync(fresh, topUp, audience, InLogistics);
    }

    /// <summary>
    /// One unfinished search per campaign. A search claimed while another of its campaign is still followed — running, or
    /// held by the provider paused for credits — waits, with no attempt and nobody asked to approve a spend that cannot
    /// start now: its last error names the search in the way, the line says what it waits behind, and the wait lets it go
    /// for the claim to ask again a cadence later, the long cadence of a hold where the search in the way is held, so a
    /// hold of days does not say the wait again every few minutes. A search whose start may have acted although its answer
    /// was lost — open again, with no row, its attempt ended ambiguous — is in the way the same: it may be running at the
    /// provider. One that failed for good, with an answer that says nothing was started, is in nobody's way. Another
    /// campaign's search goes on to its person, and once the search in the way has ended, the waiting one does too.
    /// </summary>
    [Theory]
    [InlineData("in_progress", 300)]
    [InlineData("paused_for_credits", 3600)]
    [InlineData(StartAnswerLost, 300)]
    [InlineData(FailedForGood, null)]
    public async Task Audience_ASecondSearchOfACampaign_WaitsBehindTheOneUnfinished_NamingIt(string state, int? seconds)
    {
        using var harness = SearchHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var spring = WorkItemFactory.NewCampaign("Spring", now: Noon);
        var autumn = WorkItemFactory.NewCampaign("Autumn", now: Noon);
        db.Campaigns.AddRange(spring, autumn);
        await HeldAsync(harness, db, spring, InSoftware);
        var first = await db.WorkItems.SingleAsync(w => w.CampaignId == spring.Id, Ct);
        if (state is StartAnswerLost or FailedForGood)
        {
            // Its one attempt ran and ended as the provider's answer, or its loss, says: back in the queue, a retry delay
            // off, where the start may have acted; failed, where it cannot have.
            Ran(db, first, state == StartAnswerLost
                ? new AttemptErrorDto("provider_answer_lost", "No answer arrived.", Retriable: true, Class: FailureClass.Ambiguous)
                : new AttemptErrorDto("provider_call_failed", "The call was refused.", Retriable: false, Class: FailureClass.Permanent));
            first.Status = state == StartAnswerLost ? WorkItemStatus.Created : WorkItemStatus.Failed;
            first.RetryAfter = Noon.AddMinutes(5);
        }
        else
        {
            first.Status = WorkItemStatus.Succeeded;
            db.AudienceSearches.Add(new AudienceSearch
            {
                CampaignId = spring.Id,
                AudienceId = (await db.Audiences.SingleAsync(a => a.CampaignId == spring.Id, Ct)).Id,
                SearchWorkItem = first,
                PluginId = Searcher,
                Reference = "srch-1",
                State = state,
                Asked = 50,
                QuietSince = Noon,
                CreatedAt = Noon,
            });
        }

        var second = Search(db, spring, new JsonObject { ["definition"] = InLogistics, ["count"] = 50 });
        var elsewhere = Search(db, autumn, new JsonObject { ["definition"] = InLogistics, ["count"] = 50 });
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        if (seconds is null)
        {
            await using var free = database.Open();
            Assert.Equal(WorkItemStatus.AwaitingApproval, (await free.WorkItems.AsNoTracking().SingleAsync(w => w.Id == second.Id, Ct)).Status);
            return;
        }

        await using (var fresh = database.Open())
        {
            var waiting = await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == second.Id, Ct);
            Assert.Equal((WorkItemStatus.Waiting, AttemptErrors.SearchUnfinished), (waiting.Status, waiting.LastError?.Code));
            Assert.Contains(first.PublicId, waiting.LastError!.Message, StringComparison.Ordinal);
            Assert.Empty(await fresh.Attempts.AsNoTracking().Where(a => a.WorkItemId == second.Id).ToListAsync(Ct));
            Assert.Empty(await fresh.Approvals.AsNoTracking().Where(a => a.WorkItemId == second.Id).ToListAsync(Ct));
            var wait = await fresh.WorkItemWaits.AsNoTracking().SingleAsync(w => w.WorkItemId == second.Id, Ct);
            Assert.Equal((Noon.AddSeconds(seconds.Value), (DateTime?)Noon.AddSeconds(seconds.Value)), (wait.Until!.Value, wait.Deadline));
            var line = await fresh.Journal.AsNoTracking().SingleAsync(e => e.WorkItemId == second.PublicId && e.Kind == JournalKinds.WorkItemWaiting, Ct);
            Assert.Equal(first.PublicId, (string?)line.New!["behind_search"]);
            Assert.Equal(WorkItemStatus.AwaitingApproval, (await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == elsewhere.Id, Ct)).Status);
        }

        if (state == StartAnswerLost)
        {
            // Its retry delay over, the search whose answer was lost is claimed again, and nothing it did itself is in its
            // own way: its repeat goes on to the person, and the second search still waits.
            await db.WorkItems.Where(w => w.Id == first.Id).ExecuteUpdateAsync(set => set.SetProperty(w => w.RetryAfter, (DateTime?)null), Ct);
            db.ChangeTracker.Clear();
            Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));
            await using (var repeated = database.Open())
            {
                var repeat = await repeated.WorkItems.AsNoTracking().SingleAsync(w => w.Id == first.Id, Ct);
                Assert.Equal((WorkItemStatus.AwaitingApproval, null), (repeat.Status, repeat.LastError?.Code));
                Assert.Equal(WorkItemStatus.Waiting, (await repeated.WorkItems.AsNoTracking().SingleAsync(w => w.Id == second.Id, Ct)).Status);
            }

            await db.WorkItems.Where(w => w.Id == first.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(w => w.Status, WorkItemStatus.Failed).SetProperty(w => w.FinishedAt, Noon), Ct);
        }

        await db.AudienceSearches.ExecuteUpdateAsync(set => set.SetProperty(row => row.EndedAs, "finished").SetProperty(row => row.EndedAt, Noon), Ct);
        db.ChangeTracker.Clear();
        var released = await db.WorkItems.SingleAsync(w => w.Id == second.Id, Ct);
        WorkItemTransitions.Apply(released, WorkItemStatus.Created, Noon);
        await db.SaveChangesAsync(Ct);
        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var after = database.Open();
        Assert.Equal(WorkItemStatus.AwaitingApproval, (await after.WorkItems.AsNoTracking().SingleAsync(w => w.Id == second.Id, Ct)).Status);
    }

    /// <summary>
    /// Two searches of a campaign whose starts both lost their answers — one reopened after it expired while the other ran,
    /// say — never wait behind each other: the older goes on to its repeat, and the newer waits behind it.
    /// </summary>
    [Fact]
    public async Task Audience_OfTwoSearchesWhoseStartsLostTheirAnswers_TheOlderGoesOnAndTheNewerWaits()
    {
        using var harness = SearchHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var spring = WorkItemFactory.NewCampaign("Spring", now: Noon);
        db.Campaigns.Add(spring);
        var older = Search(db, spring, new JsonObject { ["definition"] = InSoftware, ["count"] = 50 });
        var newer = Search(db, spring, new JsonObject { ["definition"] = InLogistics, ["count"] = 50 });
        foreach (var lost in new[] { older, newer })
        {
            Ran(db, lost, new AttemptErrorDto("provider_answer_lost", "No answer arrived.", Retriable: true, Class: FailureClass.Ambiguous));
        }

        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));
        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        var first = await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == older.Id, Ct);
        var second = await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == newer.Id, Ct);
        Assert.Equal((WorkItemStatus.AwaitingApproval, WorkItemStatus.Waiting), (first.Status, second.Status));
        var line = await fresh.Journal.AsNoTracking().SingleAsync(e => e.WorkItemId == newer.PublicId && e.Kind == JournalKinds.WorkItemWaiting, Ct);
        Assert.Equal(older.PublicId, (string?)line.New!["behind_search"]);
    }

    /// <summary>
    /// An attempt of <paramref name="search"/> that ran under the harness's route and ended with <paramref name="error"/>, as
    /// its claim's provenance kept it.
    /// </summary>
    private static void Ran(JasonDbContext db, WorkItem search, AttemptErrorDto error)
    {
        var ran = WorkItemFactory.NewAttempt(search, search.Attempts.Count + 1, AttemptStatus.Failed, Noon);
        ran.StartedAt = Noon;
        ran.FinishedAt = Noon;
        ran.Error = error;
        ran.Provenance = new AttemptProvenanceDto(
            Searcher, "1.0.0", "sha256:0", null, null, "audience.search", 1, null, null, RouteScope.GlobalDefault, BindingIdentity: null);
        db.Attempts.Add(ran);
    }

    /// <summary>
    /// The campaign's audiences made from <paramref name="definitions"/>, numbered in that order: one search under each,
    /// claimed one scan at a time, since a campaign is claimed once a scan.
    /// </summary>
    private static async Task HeldAsync(Harness harness, JasonDbContext db, Campaign campaign, params JsonObject[] definitions)
    {
        foreach (var definition in definitions)
        {
            Search(db, campaign, new JsonObject { ["definition"] = definition, ["count"] = 50 });
            await db.SaveChangesAsync(Ct);
            Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));
        }
    }

    private static Harness SearchHarness()
    {
        var plugin = TestPlugins.Loaded(Searcher, ["audience.search", "audience.search_status", "audience.preview"]);
        var definition = new AudienceDefinition(
            new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JsonObject
                {
                    ["industries"] = new JsonObject { ["description"] = "Industries the company is in.", ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
                    ["titles"] = new JsonObject { ["description"] = "The person's job titles.", ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
                },
            },
            new Dictionary<string, DefinitionKey>(StringComparer.Ordinal));
        return new Harness(plugin: plugin with { Manifest = plugin.Manifest with { AudienceDefinition = definition } }, route: new Route(Searcher, null, null));
    }

    private static WorkItem Search(JasonDbContext db, Campaign campaign, JsonObject arguments)
    {
        var item = WorkItemFactory.NewProviderOp(campaign, "audience.search", Noon);
        item.Context = new JsonObject { [WorkItemService.InputKey] = arguments };
        db.WorkItems.Add(item);
        return item;
    }

    /// <summary>What a person's approval of the search does to the rows: the question answered, the work back in the queue.</summary>
    private static async Task ApproveAsync(JasonDbContext db, WorkItem search)
    {
        var approval = await db.Approvals.Include(a => a.WorkItem).SingleAsync(a => a.WorkItemId == search.Id, Ct);
        approval.Status = ApprovalStatus.Approved;
        approval.DecidedAt = Noon;
        approval.DecidedByType = ActorType.Human;
        approval.DecidedById = "operator";
        WorkItemTransitions.Apply(approval.WorkItem!, WorkItemStatus.Created, Noon);
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>
    /// The search carries <paramref name="audience"/> under its context's reserved key, with the definition it is searched
    /// by, and the dispatcher said so once in the chronicle.
    /// </summary>
    private static async Task AssertCarriesAsync(JasonDbContext db, WorkItem search, Audience audience, JsonObject definition)
    {
        var expected = new JsonObject { ["id"] = audience.PublicId, ["number"] = audience.Number, ["definition"] = definition.DeepClone() };
        var item = await db.WorkItems.AsNoTracking().SingleAsync(w => w.Id == search.Id, Ct);
        Assert.Equal(WorkItemStatus.AwaitingApproval, item.Status);
        Assert.True(JsonNode.DeepEquals(expected, item.Context["audience"]), item.Context.ToJsonString());

        var line = await db.Journal.AsNoTracking().SingleAsync(e => e.WorkItemId == search.PublicId && e.Kind == JournalKinds.WorkItemContextUpdated, Ct);
        Assert.Equal((ActorType.System, "dispatcher", "audience"), (line.ActorType, line.ActorId, line.Key));
        Assert.Null(line.Old);
        Assert.True(JsonNode.DeepEquals(expected, line.New));
    }
}
