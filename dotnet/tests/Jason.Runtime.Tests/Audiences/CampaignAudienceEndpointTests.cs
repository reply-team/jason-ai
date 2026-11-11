using System.Net;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Campaigns;
using Jason.Runtime.Tests.Journal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Audiences;

/// <summary>
/// <c>campaign.audience</c>: where a campaign's audience stands, as facts. Spring keeps a reserve of three and has two
/// untouched members. Its audience 1 ran two searches that ended, one finished and one stalled on the provider's credits;
/// audience 2 runs one search still followed, under the person's allowance, which one release has used; audience 3's first
/// search waits for a person, so the provider has taken none.
/// </summary>
public class CampaignAudienceEndpointTests
{
    private static readonly DateTimeOffset Now = new(CampaignWorld.Now);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_view_says_the_reserve_each_audience_with_its_searches_and_people_and_the_allowance_with_its_use()
    {
        await using var api = await StartAsync();
        var scene = await SceneAsync(api);

        var view = await api.RecordAsync<CampaignAudienceResponse>(Operations.CampaignAudience, "three-audiences", new { campaign_id = scene.Spring }, Ct);

        Assert.Equal(scene.Spring, view.CampaignId);
        Assert.Equal(new AudienceReserve(Untouched: 2, Threshold: 3), view.Reserve);
        Assert.Equal([3, 2, 1], view.Audiences.Select(audience => audience.Number));
        Assert.Equal([(0, 0), (2, 1), (6, 2)], view.Audiences.Select(audience => (audience.Landed, audience.SearchCount)));
        Assert.Equal(["Industry 3", "Industry 2", "Industry 1"], view.Audiences.Select(audience => (string?)audience.Definition["industries"]![0]));
        Assert.All(view.Audiences, audience => Assert.Equal(new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero), audience.CreatedAt));
        Assert.Equal(
            [
                [],
                [("in_progress", 50, (int?)null, 2, (string?)null)],
                [("paused_for_credits", 25, (int?)40, 2, (string?)"stalled"), ("finished", 25, 40, 4, "finished")],
            ],
            view.Audiences.Select(audience => audience.Searches.Select(search => (search.State, search.Asked, search.Found, search.Landed, search.EndedAs)).ToList()));
        Assert.Equal(scene.Running, view.Audiences[1].Searches[0].Search);
        Assert.Null(view.Audiences[1].Searches[0].EndedAt);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 11, 0, 0, TimeSpan.Zero), view.Audiences[1].Searches[0].BeganAt);

        var allowance = view.Allowance!;
        Assert.Equal((2, 100, (int?)50, 1, (int?)50, true), (allowance.Audience!.Number, allowance.MaxPeople!.Value, allowance.MaxPerSearch, allowance.Released, allowance.People, allowance.InForce));
        var listed = await api.PostOkAsync<Page<StandingApprovalDto>>(Operations.ApprovalListStanding, new { campaign_id = scene.Spring }, Ct);
        Assert.Equivalent(Assert.Single(listed.Items), allowance, strict: true);
    }

    /// <summary>The audiences page the newest first; the reserve and the allowance are said on every page.</summary>
    [Fact]
    public async Task The_audiences_page_the_newest_first()
    {
        await using var api = await StartAsync();
        var scene = await SceneAsync(api);

        var first = await api.RecordAsync<CampaignAudienceResponse>(Operations.CampaignAudience, "first-page", new { campaign_id = scene.Spring, limit = 2 }, Ct);
        var next = await api.RecordAsync<CampaignAudienceResponse>(
            Operations.CampaignAudience, "next-page", new { campaign_id = scene.Spring, limit = 2, cursor = first.NextCursor }, Ct);

        Assert.Equal([3, 2], first.Audiences.Select(audience => audience.Number));
        Assert.NotNull(first.NextCursor);
        Assert.Equal([1], next.Audiences.Select(audience => audience.Number));
        Assert.Null(next.NextCursor);
        Assert.Equal(first.Reserve, next.Reserve);
        Assert.Equivalent(first.Allowance, next.Allowance, strict: true);
    }

    /// <summary>
    /// Each audience lists its latest searches, as many as <c>search_limit</c> asks, and counts every one: a campaign
    /// that tops an audience up for a year answers the same few rows at each check-in. What it landed is every search's.
    /// </summary>
    [Fact]
    public async Task Each_audience_lists_its_latest_searches_and_counts_them_all()
    {
        await using var api = await StartAsync();
        var scene = await SceneAsync(api);

        var latest = await api.RecordAsync<CampaignAudienceResponse>(Operations.CampaignAudience, "latest-search", new { campaign_id = scene.Spring, search_limit = 1 }, Ct);
        var none = await api.PostErrorAsync(Operations.CampaignAudience, new { campaign_id = scene.Spring, search_limit = 0 }, HttpStatusCode.BadRequest, Ct);
        var many = await api.PostErrorAsync(Operations.CampaignAudience, new { campaign_id = scene.Spring, search_limit = 101 }, HttpStatusCode.BadRequest, Ct);

        var first = latest.Audiences.Single(audience => audience.Number == 1);
        Assert.Equal((6, 2), (first.Landed, first.SearchCount));
        Assert.Equal(["stalled"], first.Searches.Select(search => search.EndedAs));
        Assert.All([none, many], error => Assert.Equal(("validation_failed", "search_limit", "invalid"), (error.Code, error.Details![0].Field, error.Details[0].Code)));
    }

    /// <summary>
    /// A campaign that has searched under no audience and keeps no reserve: its untouched members still counted, no
    /// threshold, no audience and no allowance.
    /// </summary>
    [Fact]
    public async Task A_campaign_without_audiences_says_so()
    {
        await using var api = await StartAsync();
        var scene = await SceneAsync(api);

        var view = await api.RecordAsync<CampaignAudienceResponse>(Operations.CampaignAudience, "none", new { campaign_id = scene.Autumn }, Ct);

        Assert.Equal((new AudienceReserve(1, null), (StandingApprovalDto?)null, 0, (string?)null), (view.Reserve, view.Allowance, view.Audiences.Count, view.NextCursor));
    }

    [Fact]
    public async Task An_unknown_campaign_is_not_found_and_a_campaign_is_required()
    {
        await using var api = await StartAsync();

        var unknown = await api.RecordErrorAsync(Operations.CampaignAudience, "unknown-campaign", new { campaign_id = "cmp_01K7QN00000000000000000999" }, HttpStatusCode.NotFound, Ct);
        var missing = await api.PostErrorAsync(Operations.CampaignAudience, new { }, HttpStatusCode.BadRequest, Ct);

        Assert.Equal(("campaign_not_found", "validation_failed"), (unknown.Code, missing.Code));
    }

    private sealed record Scene(string Spring, string Autumn, string Running);

    private static Task<RuntimeApiFixture> StartAsync() => RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));

    private static async Task<Scene> SceneAsync(RuntimeApiFixture api)
    {
        var n = 400;
        string Id(string prefix) => $"{prefix}_01K7QN{++n:D20}";

        await using var scope = api.Resolve<IServiceScopeFactory>().CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        var world = await CampaignWorld.WriteAsync(db, Ct, Id);
        var spring = await db.Campaigns.SingleAsync(campaign => campaign.Id == world.Spring.Id, Ct);
        spring.AudienceThreshold = 3;
        await db.SaveChangesAsync(Ct);

        await world.AudienceAsync(db, 1, Ct, [
            CampaignWorld.Finished("Ada", "Ben", "Cleo", "Jon") with { Asked = 25, Found = 40 },
            new CampaignWorld.SearchRun(25, 40, "paused_for_credits", "stalled", "Oli", "Quinn"),
        ]);
        var second = await world.AudienceAsync(db, 2, Ct, [new CampaignWorld.SearchRun(50, null, "in_progress", null, "Rae", "Tess")]);
        await world.AudienceAsync(db, 3, Ct, Array.Empty<CampaignWorld.SearchRun>());
        var running = await db.AudienceSearches.Where(search => search.AudienceId == second.Id).Select(search => search.SearchWorkItem!).SingleAsync(Ct);

        var allowance = await api.PostOkAsync<StandingApprovalDto>(
            Operations.ApprovalSetStanding,
            new
            {
                campaign_id = world.Spring.PublicId,
                operations = new[] { "audience.search" },
                audience = 2,
                max_people = 100,
                max_per_search = 50,
                actor = new { type = "human", id = "Ada Example" },
            },
            Ct);
        new JournalWriter(new FixedClock(Now)).AppendDocumented(
            db,
            Actors.Dispatcher,
            JournalKinds.ApprovalStandingApplied,
            spring,
            key: allowance.LineId,
            with: new JsonObject
            {
                ["operation"] = "audience.search",
                ["standing"] = allowance.LineId,
                ["verdict"] = null,
                ["category"] = null,
                ["audience"] = new JsonObject { ["id"] = second.PublicId, ["number"] = 2 },
                ["count"] = 50,
            },
            workItem: running);
        await db.SaveChangesAsync(Ct);
        return new Scene(world.Spring.PublicId, world.Autumn.PublicId, running.PublicId);
    }
}
