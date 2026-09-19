using Jason.Contracts.Api;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// Where the cadence measures from, and what moves it. The anchor is the whole of the difference between a
/// campaign reviewed on a rhythm and one reviewed the instant somebody touches it.
/// </summary>
public class ReviewAnchorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private const int Hourly = 3_600;

    /// <summary>
    /// A campaign somebody stopped for a week and started again is not a campaign nobody looked at for a week.
    /// Measuring from the start means the review falls a full interval after it wakes — and without an anchor
    /// that moves on every start, a resumed campaign would be summoned on the very tick it came back.
    /// </summary>
    [Fact]
    public async Task A_campaign_resumed_after_a_week_is_reviewed_an_interval_later_not_at_once()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaigns = Campaigns(db, clock);
        var created = await campaigns.CreateAsync(new CampaignCreateRequest("LatAm", null, null, null), Ct);
        await campaigns.StartAsync(new CampaignTransitionRequest(created.Id, null, null), Ct);

        clock.Advance(TimeSpan.FromHours(2));
        await campaigns.PauseAsync(new CampaignTransitionRequest(created.Id, null, null), Ct);

        clock.Advance(TimeSpan.FromDays(7));
        await campaigns.StartAsync(new CampaignTransitionRequest(created.Id, null, null), Ct);

        var campaign = await db.Campaigns.SingleAsync(Ct);
        Assert.False(ReviewSchedule.IsDue(campaign, Hourly, clock.GetUtcNow().UtcDateTime));

        clock.Advance(TimeSpan.FromSeconds(Hourly));
        Assert.True(ReviewSchedule.IsDue(campaign, Hourly, clock.GetUtcNow().UtcDateTime));
    }

    /// <summary>A campaign that has never been live has no moment to measure from, and is not due at once.</summary>
    [Fact]
    public async Task A_draft_campaign_has_no_anchor()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var clock = new FixedClock(Noon);
        await Campaigns(db, clock).CreateAsync(new CampaignCreateRequest("LatAm", null, null, null), Ct);

        var campaign = await db.Campaigns.SingleAsync(Ct);

        Assert.Null(campaign.ManagerReviewAnchor);
        Assert.False(ReviewSchedule.IsDue(campaign, Hourly, clock.GetUtcNow().UtcDateTime.AddYears(1)));
    }

    /// <summary>
    /// Creating a check-in moves the anchor, so the next review is an interval from this one — measured from
    /// when it was created, not from when it finished, because a review that failed must not stop the loop.
    /// </summary>
    [Fact]
    public async Task Creating_a_check_in_moves_the_anchor()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaigns = Campaigns(db, clock);
        var created = await campaigns.CreateAsync(new CampaignCreateRequest("LatAm", null, null, null), Ct);
        await campaigns.StartAsync(new CampaignTransitionRequest(created.Id, null, null), Ct);

        clock.Advance(TimeSpan.FromSeconds(Hourly));
        var campaign = await db.Campaigns.SingleAsync(Ct);
        Assert.True(ReviewSchedule.IsDue(campaign, Hourly, clock.GetUtcNow().UtcDateTime));

        await ManagerCheckIn.CreateAsync(Items(db, clock), db, campaign, cause: null, new ManagerOptions(), Ct);

        Assert.Equal(clock.GetUtcNow().UtcDateTime, campaign.ManagerReviewAnchor);
        Assert.False(ReviewSchedule.IsDue(campaign, Hourly, clock.GetUtcNow().UtcDateTime));
    }

    /// <summary>
    /// And cancelling one does not move it back. A person who cancels a review is saying "not this one", not
    /// "bring the next one forward".
    /// </summary>
    [Fact]
    public async Task Cancelling_a_check_in_leaves_the_anchor_where_it_is()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaigns = Campaigns(db, clock);
        var created = await campaigns.CreateAsync(new CampaignCreateRequest("LatAm", null, null, null), Ct);
        await campaigns.StartAsync(new CampaignTransitionRequest(created.Id, null, null), Ct);
        clock.Advance(TimeSpan.FromSeconds(Hourly));

        var campaign = await db.Campaigns.SingleAsync(Ct);
        var items = Items(db, clock);
        var checkIn = await ManagerCheckIn.CreateAsync(items, db, campaign, cause: null, new ManagerOptions(), Ct);
        var anchor = campaign.ManagerReviewAnchor;

        await items.CancelAsync(new WorkItemCancelRequest(checkIn.PublicId, null, "not this one"), Ct);

        // Read through a context of its own: comparing the tracked instance with itself would pass however the
        // anchor had been changed, because it is the same object.
        await using var fresh = database.Open();
        Assert.Equal(anchor, (await fresh.Campaigns.SingleAsync(Ct)).ManagerReviewAnchor);
    }

    private static CampaignService Campaigns(JasonDbContext db, TimeProvider clock) =>
        new(db, new JournalWriter(clock), clock, TestCanceller.New(clock));

    private static WorkItemService Items(JasonDbContext db, TimeProvider clock) =>
        new(db, new JournalWriter(clock), clock, TestCanceller.New(clock), TestOptions.PluginSettings());
}
