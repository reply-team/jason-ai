using Jason.Contracts.Api;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Decisions;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Decisions;

/// <summary>
/// What retires a question that nobody answered. A campaign that can never run again takes its open questions
/// with it — a live question about work that can never proceed is the row that makes a listing untrue — while
/// a cancelled work item does not, because outliving its attempt is the whole point of a decision.
/// </summary>
public class DecisionCancellationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static readonly ActorRef Ada = new(ActorType.Human, "ada");

    [Fact]
    public async Task Archiving_a_campaign_cancels_its_pending_decisions_and_journals_each_one()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        var first = await RaiseAsync(db, seed, "do we keep calling?");
        var second = await RaiseAsync(db, seed, "and this account?");

        await Campaigns(db).ArchiveAsync(
            new CampaignTransitionRequest(seed.Campaign.PublicId, Ada, "the quarter is over"), Ct);

        // Read through a context of its own: the tracked instances would say what this caller wanted rather
        // than what was committed.
        await using var fresh = database.Open();
        var stored = await fresh.Decisions.AsNoTracking().OrderBy(d => d.Id).ToListAsync(Ct);
        Assert.All(stored, decision => Assert.Equal(DecisionStatus.Cancelled, decision.Status));

        var lines = await fresh.Journal.AsNoTracking()
            .Where(e => e.Kind == JournalKinds.DecisionCancelled)
            .ToListAsync(Ct);
        Assert.Equal(2, lines.Count);
        Assert.All(lines, line => Assert.Equal(ActorType.Human, line.ActorType));
        Assert.Contains(lines, line => (string?)line.New!["decision_id"] == first.Id);
        Assert.Contains(lines, line => (string?)line.New!["decision_id"] == second.Id);
    }

    /// <summary>An answer already given is history, and archiving does not rewrite history.</summary>
    [Fact]
    public async Task Archiving_leaves_an_already_answered_decision_alone()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        var decision = await RaiseAsync(db, seed, "do we keep calling?");
        await Service(db).AnswerAsync(new DecisionAnswerRequest(decision.Id, "stop", null, Ada, null), Ct);

        await Campaigns(db).ArchiveAsync(new CampaignTransitionRequest(seed.Campaign.PublicId, Ada, null), Ct);

        await using var fresh = database.Open();
        var stored = await fresh.Decisions.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(DecisionStatus.Answered, stored.Status);
        Assert.Equal("stop", stored.Answer);
        Assert.Empty(await fresh.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.DecisionCancelled).ToListAsync(Ct));
    }

    /// <summary>
    /// And cancelling the work item does not touch the question. The attempt that asked was always going to
    /// end before the answer arrived; a question retired with its work item would be a question that could
    /// never be answered at all.
    /// </summary>
    [Fact]
    public async Task Cancelling_a_work_item_leaves_its_decision_pending()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await RaiseAsync(db, seed, "do we keep calling?");

        await Items(db).CancelAsync(new WorkItemCancelRequest(seed.Item.PublicId, Ada, "not this one"), Ct);

        await using var fresh = database.Open();
        Assert.Equal(DecisionStatus.Pending, (await fresh.Decisions.AsNoTracking().SingleAsync(Ct)).Status);
    }

    private static DecisionService Service(JasonDbContext db)
    {
        var clock = new FixedClock(Noon);
        return new DecisionService(db, new JournalWriter(clock), clock);
    }

    private static CampaignService Campaigns(JasonDbContext db)
    {
        var clock = new FixedClock(Noon);
        return new CampaignService(db, new JournalWriter(clock), clock, TestCanceller.New(clock));
    }

    private static WorkItemService Items(JasonDbContext db)
    {
        var clock = new FixedClock(Noon);
        return new WorkItemService(db, new JournalWriter(clock), clock, TestCanceller.New(clock), TestOptions.PluginSettings());
    }

    private static Task<DecisionDto> RaiseAsync(JasonDbContext db, Seed seed, string question) =>
        Service(db).RaiseAsync(new DecisionRaiseRequest(seed.Item.PublicId, seed.Attempt.PublicId, question, null, null, null), Ct);

    private static async Task<Seed> SeedAsync(JasonDbContext db)
    {
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        campaign.Status = CampaignStatus.Active;
        var item = WorkItemFactory.NewAiRole(campaign, now: Noon);
        item.Status = WorkItemStatus.Processing;
        var attempt = new Attempt
        {
            PublicId = "att_live",
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
