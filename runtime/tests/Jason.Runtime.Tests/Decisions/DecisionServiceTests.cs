using System.Net;
using Jason.Contracts.Api;
using Jason.Runtime.Decisions;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Decisions;

/// <summary>
/// Reading questions: what is waiting, and what one of them says. A person opens this with "what is waiting
/// on me?", so pending is the default and everything else is the history of questions already settled.
/// </summary>
public class DecisionServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static readonly ActorRef Ada = new(ActorType.Human, "ada");

    [Fact]
    public async Task Listing_answers_with_what_is_pending_unless_asked_otherwise()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        var first = await RaiseAsync(db, seed, "do we keep calling?");
        await RaiseAsync(db, seed, "and this one?");
        await Service(db).AnswerAsync(new DecisionAnswerRequest(first.Id, "stop", null, Ada, null), Ct);

        var pending = await Service(db).ListAsync(new DecisionListRequest(null, null, null, null, null), Ct);
        var answered = await Service(db).ListAsync(new DecisionListRequest(DecisionStatus.Answered, null, null, null, null), Ct);

        Assert.Equal("and this one?", Assert.Single(pending.Items).Question);
        Assert.Equal(first.Id, Assert.Single(answered.Items).Id);
        Assert.Equal(Ada, answered.Items[0].AnsweredBy);
    }

    /// <summary>The listing carries the question whole: "what am I being asked?" is the only reason to open one.</summary>
    [Fact]
    public async Task A_listing_carries_the_question_and_the_work_it_is_about()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await RaiseAsync(db, seed, "do we keep calling?");

        var listed = Assert.Single((await Service(db).ListAsync(new DecisionListRequest(null, null, null, null, null), Ct)).Items);

        Assert.Equal("do we keep calling?", listed.Question);
        Assert.Equal(seed.Campaign.PublicId, listed.CampaignId);
        Assert.Equal(seed.Item.PublicId, listed.WorkItemId);
    }

    [Fact]
    public async Task Listing_narrows_to_one_campaign_and_to_one_work_item()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var mine = await SeedAsync(db);
        var theirs = await SeedAsync(db, "cmp_other", "att_other");
        await RaiseAsync(db, mine, "mine?");
        await RaiseAsync(db, theirs, "theirs?");

        var byCampaign = await Service(db).ListAsync(new DecisionListRequest(null, mine.Campaign.PublicId, null, null, null), Ct);
        var byItem = await Service(db).ListAsync(new DecisionListRequest(null, null, theirs.Item.PublicId, null, null), Ct);

        Assert.Equal("mine?", Assert.Single(byCampaign.Items).Question);
        Assert.Equal("theirs?", Assert.Single(byItem.Items).Question);
    }

    [Fact]
    public async Task A_listing_pages_through_a_cursor()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        for (var index = 0; index < 3; index++)
        {
            await RaiseAsync(db, seed, $"question {index}");
        }

        var first = await Service(db).ListAsync(new DecisionListRequest(null, null, null, 2, null), Ct);
        var second = await Service(db).ListAsync(new DecisionListRequest(null, null, null, 2, first.NextCursor), Ct);

        Assert.Equal(2, first.Items.Count);
        Assert.NotNull(first.NextCursor);
        Assert.Single(second.Items);
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task An_unknown_decision_is_not_found()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        var refused = await Assert.ThrowsAsync<NotFoundException>(
            () => Service(db).GetAsync(new DecisionGetRequest("dec_nobody"), Ct));

        Assert.Equal("decision_not_found", refused.Code);
    }

    /// <summary>All four verbs answer over the wire, which is where a person and a launched role both reach them.</summary>
    [Fact]
    public async Task The_four_verbs_answer_over_the_api()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "LatAm" }, Ct);
        await api.PostOkAsync<CampaignDto>(Operations.CampaignStart, new { campaign_id = campaign.Id }, Ct);

        var listed = await api.PostOkAsync<Page<DecisionSummaryDto>>(Operations.DecisionList, new { }, Ct);
        Assert.Empty(listed.Items);

        // The other three are reachable and answer in their own words rather than with "no such operation".
        var missing = await api.PostErrorAsync(Operations.DecisionGet, new { decision_id = "dec_nobody" }, HttpStatusCode.NotFound, Ct);
        Assert.Equal("decision_not_found", missing.Code);

        var stale = await api.PostErrorAsync(
            Operations.DecisionRaise,
            new { work_item_id = "wi_nobody", attempt_id = "att_nobody", question = "?" },
            HttpStatusCode.Conflict,
            Ct);
        Assert.Equal("stale_attempt", stale.Code);

        var unanswerable = await api.PostErrorAsync(
            Operations.DecisionAnswer,
            new { decision_id = "dec_nobody", answer = "x", actor = new { type = "human", id = "ada" } },
            HttpStatusCode.NotFound,
            Ct);
        Assert.Equal("decision_not_found", unanswerable.Code);
    }

    private static DecisionService Service(JasonDbContext db)
    {
        var clock = new FixedClock(Noon);
        return new DecisionService(db, new JournalWriter(clock), clock);
    }

    private static Task<DecisionDto> RaiseAsync(JasonDbContext db, Seed seed, string question) =>
        Service(db).RaiseAsync(new DecisionRaiseRequest(seed.Item.PublicId, seed.Attempt.PublicId, question, null, null, null), Ct);

    private static async Task<Seed> SeedAsync(JasonDbContext db, string? campaignId = null, string attemptId = "att_live")
    {
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        if (campaignId is not null)
        {
            campaign.PublicId = campaignId;
        }

        var item = WorkItemFactory.NewAiRole(campaign, now: Noon);
        item.Status = WorkItemStatus.Processing;
        var attempt = new Attempt
        {
            PublicId = attemptId,
            WorkItem = item,
            Number = 1,
            Status = AttemptStatus.Running,
            StartedAt = Noon,
            LockUntil = Noon.AddMinutes(30),
        };

        db.Campaigns.Add(campaign);
        db.WorkItems.Add(item);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(Ct);
        return new Seed(campaign, item, attempt);
    }

    private sealed record Seed(Campaign Campaign, WorkItem Item, Attempt Attempt);
}
