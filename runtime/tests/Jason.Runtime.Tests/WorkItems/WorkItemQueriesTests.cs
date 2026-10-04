using Jason.Contracts.Api;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.WorkItems;

public class WorkItemQueriesTests
{
    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_item_with_every_gate_open_is_eligible_in_sql_and_in_memory()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var open = WorkItemFactory.NewAiRole(campaign, now: Noon, configure: w =>
        {
            w.NotBefore = Noon.AddMinutes(-1);
            w.DueAt = Noon.AddHours(1);
            w.RetryAfter = Noon.AddMinutes(-1);
        });
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(open);
        await db.SaveChangesAsync(Ct);

        var fromSql = await db.WorkItems.AsNoTracking().WithNavigation().Where(WorkItemQueries.Eligible(Noon)).ToListAsync(Ct);

        Assert.Equal(open.PublicId, Assert.Single(fromSql).PublicId);
        Assert.True(WorkItemQueries.IsEligible(open, Noon));
    }

    [Theory]
    [InlineData("paused_campaign")]
    [InlineData("not_before_future")]
    [InlineData("due_at_past")]
    [InlineData("retry_after_future")]
    [InlineData("already_scheduled")]
    public async Task Every_closed_gate_takes_an_item_out_of_the_queue(string gate)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(status: gate == "paused_campaign" ? CampaignStatus.Paused : CampaignStatus.Active, now: Noon);
        var item = WorkItemFactory.NewAiRole(campaign, now: Noon, configure: w =>
        {
            switch (gate)
            {
                case "not_before_future":
                    w.NotBefore = Noon.AddSeconds(1);
                    break;
                case "due_at_past":
                    w.DueAt = Noon.AddSeconds(-1);
                    break;
                case "retry_after_future":
                    w.RetryAfter = Noon.AddSeconds(1);
                    break;
                case "already_scheduled":
                    w.Status = WorkItemStatus.Scheduled;
                    break;
                default:
                    break;
            }
        });
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);

        var fromSql = await db.WorkItems.AsNoTracking().WithNavigation().Where(WorkItemQueries.Eligible(Noon)).ToListAsync(Ct);

        Assert.Empty(fromSql);
        Assert.False(WorkItemQueries.IsEligible(item, Noon));
    }

    /// <summary>
    /// The one predicate has three readers — the listing's SQL, its compiled twin, and the claimer's own query — and all
    /// three agree in every campaign state and for every kind of work: a paused campaign holds its work, except a
    /// provider operation whose contract says it runs there; a draft or an archived one holds everything. Two
    /// operations say so, which is what makes the claim's set a set; an add is `control` and `auto` like them, and
    /// still waits.
    /// </summary>
    [Fact]
    public async Task The_listing_its_twin_and_the_claim_agree_in_every_campaign_state()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var items = new List<(WorkItem Item, bool Expected)>();
        foreach (var status in Enum.GetValues<CampaignStatus>())
        {
            var kinds = new (string Name, Func<Campaign, WorkItem> Make)[]
            {
                ("a pause", campaign => WorkItemFactory.NewProviderOp(campaign, "campaign.pause", Noon)),
                ("a record of a stop", campaign => WorkItemFactory.NewProviderOp(campaign, "optout.record", Noon)),
                ("an add", campaign => WorkItemFactory.NewProviderOp(campaign, "list_membership.add", Noon)),
                ("an enrollment", campaign => WorkItemFactory.NewProviderOp(campaign, "campaign.enroll", Noon)),
                ("a role's work", campaign => WorkItemFactory.NewAiRole(campaign, now: Noon)),
            };
            foreach (var (name, make) in kinds)
            {
                // One item in a campaign of its own, so the claimer's one-per-campaign rule cannot hide a disagreement.
                var campaign = WorkItemFactory.NewCampaign($"{status} {name}", status, Noon);
                var item = make(campaign);
                db.Campaigns.Add(campaign);
                db.WorkItems.Add(item);
                items.Add((item, status == CampaignStatus.Active || (status == CampaignStatus.Paused && name is "a pause" or "a record of a stop")));
            }
        }

        await db.SaveChangesAsync(Ct);

        var expected = items.Where(entry => entry.Expected).Select(entry => entry.Item.PublicId).Order(StringComparer.Ordinal).ToList();
        var fromSql = await db.WorkItems.AsNoTracking().WithNavigation().Where(WorkItemQueries.Eligible(Noon)).Select(w => w.PublicId).ToListAsync(Ct);
        var all = await db.WorkItems.AsNoTracking().WithNavigation().ToListAsync(Ct);
        var claimable = await Jason.Runtime.Dispatch.Claimer.CandidatesAsync(db, Noon, freeSlots: 100, Ct);

        Assert.Equal(expected, fromSql.Order(StringComparer.Ordinal));
        Assert.Equal(expected, all.Where(w => WorkItemQueries.IsEligible(w, Noon)).Select(w => w.PublicId).Order(StringComparer.Ordinal));
        Assert.Equal(expected, all.Where(w => claimable.Contains(w.Id)).Select(w => w.PublicId).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task The_query_and_its_compiled_twin_agree_row_by_row()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var active = WorkItemFactory.NewCampaign("active", CampaignStatus.Active, Noon);
        var paused = WorkItemFactory.NewCampaign("paused", CampaignStatus.Paused, Noon);
        db.Campaigns.AddRange(active, paused);
        db.WorkItems.AddRange(
            WorkItemFactory.NewAiRole(active, now: Noon),
            WorkItemFactory.NewAiRole(active, now: Noon, configure: w => w.NotBefore = Noon.AddSeconds(1)),
            WorkItemFactory.NewAiRole(active, now: Noon, configure: w => w.RetryAfter = Noon.AddSeconds(-1)),
            WorkItemFactory.NewAiRole(active, now: Noon, configure: w => w.Status = WorkItemStatus.Succeeded),
            WorkItemFactory.NewAiRole(paused, now: Noon));
        await db.SaveChangesAsync(Ct);

        var eligible = await db.WorkItems.AsNoTracking().WithNavigation().Where(WorkItemQueries.Eligible(Noon)).Select(w => w.PublicId).ToListAsync(Ct);
        var all = await db.WorkItems.AsNoTracking().WithNavigation().ToListAsync(Ct);

        Assert.Equal(2, eligible.Count);
        Assert.Equal(
            eligible.Order(StringComparer.Ordinal),
            all.Where(w => WorkItemQueries.IsEligible(w, Noon)).Select(w => w.PublicId).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_live_attempt_is_the_scheduled_or_running_one()
    {
        var item = WorkItemFactory.NewAiRole(WorkItemFactory.NewCampaign(now: Noon), now: Noon);
        Assert.Null(WorkItemQueries.LiveAttempt(item));

        WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Failed, Noon);
        Assert.Null(WorkItemQueries.LiveAttempt(item));

        var running = WorkItemFactory.NewAttempt(item, 2, AttemptStatus.Running, Noon);
        Assert.Same(running, WorkItemQueries.LiveAttempt(item));
    }

    [Fact]
    public async Task With_navigation_loads_what_the_mapper_needs()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var contact = new Contact { PublicId = "cnt_A", CreatedAt = Noon, UpdatedAt = Noon };
        var item = WorkItemFactory.NewAiRole(campaign, now: Noon, configure: w => w.Contact = contact);
        db.Campaigns.Add(campaign);
        db.Contacts.Add(contact);
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);

        var loaded = await db.WorkItems.AsNoTracking().WithNavigation().SingleAsync(Ct);

        Assert.NotNull(loaded.Campaign);
        Assert.Equal("cnt_A", loaded.Contact!.PublicId);
    }
}
