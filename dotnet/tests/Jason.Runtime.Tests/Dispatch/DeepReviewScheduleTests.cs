using Jason.Contracts.Api;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Harness;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// The deep review's clock: a daily grid that starts when a campaign goes live, starts again at every resume, and is moved
/// only by a deep review, a whole number of intervals at a time, so a runtime that was down for days runs one deep review
/// and comes back onto the grid rather than owing one for every day it missed.
/// </summary>
public class DeepReviewScheduleTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTime Noon = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);

    private const int Daily = 86_400;

    /// <summary>The grid's first moment is one interval after going live: exactly then it is due, and a second before it is not.</summary>
    [Fact]
    public async Task Exactly_one_interval_after_going_live_is_due_and_one_second_before_is_not()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaign = await LiveAsync(db, clock, new ManagerOptions());

        Assert.Equal(Noon.AddSeconds(Daily), campaign.DeepReviewDueAt);
        Assert.False(DeepReviewSchedule.IsDue(campaign, Noon.AddSeconds(Daily - 1)));
        Assert.True(DeepReviewSchedule.IsDue(campaign, Noon.AddSeconds(Daily)));
    }

    /// <summary>A campaign that has never been live has no grid, and is never due rather than due at once.</summary>
    [Fact]
    public async Task A_draft_has_no_grid_and_is_never_due()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var clock = new FixedClock(Noon);
        await Campaigns(db, clock, new ManagerOptions()).CreateAsync(new CampaignCreateRequest("Autumn", null, null, null), Ct);

        var campaign = await db.Campaigns.SingleAsync(Ct);

        Assert.Null(campaign.DeepReviewDueAt);
        Assert.False(DeepReviewSchedule.IsDue(campaign, Noon.AddYears(1)));
    }

    /// <summary>A paused or archived campaign is never due, whatever its grid says.</summary>
    [Theory]
    [InlineData(CampaignStatus.Draft)]
    [InlineData(CampaignStatus.Paused)]
    [InlineData(CampaignStatus.Archived)]
    public void Only_an_active_campaign_is_ever_due(CampaignStatus status)
    {
        var campaign = WorkItemFactory.NewCampaign("Autumn", status, now: Noon);
        campaign.DeepReviewDueAt = Noon;

        Assert.False(DeepReviewSchedule.IsDue(campaign, Noon.AddYears(1)));
    }

    /// <summary>
    /// Three grid moments missed while nothing ran are one deep review, and the grid lands on the first of its moments after
    /// that review, not on the review's own moment and not three days behind.
    /// </summary>
    [Fact]
    public async Task Three_missed_days_run_once_and_land_on_the_grid()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaign = await LiveAsync(db, clock, new ManagerOptions());

        // Down from just after going live until two hours past the third grid moment: day one, day two and day three missed.
        clock.Advance(TimeSpan.FromDays(3) + TimeSpan.FromHours(2));
        Assert.True(DeepReviewSchedule.IsDue(campaign, Now(clock)));

        await ManagerCheckIn.CreateAsync(Items(db, clock), db, campaign, cause: null, new ManagerOptions(), Ct, ManagerEntryChoice.DeepReview);

        Assert.Equal(Noon.AddDays(4), campaign.DeepReviewDueAt);
        Assert.False(DeepReviewSchedule.IsDue(campaign, Now(clock)));
    }

    /// <summary>The advance, as the pure function it is: exactly on a grid moment moves one interval, and late moves to the next one after now.</summary>
    [Fact]
    public void The_advance_is_by_whole_intervals_to_the_first_grid_moment_after_now()
    {
        var due = Noon.AddDays(1);

        Assert.Equal(Noon.AddDays(2), DeepReviewSchedule.Next(due, Daily, due));
        Assert.Equal(Noon.AddDays(2), DeepReviewSchedule.Next(due, Daily, due.AddHours(23)));
        Assert.Equal(Noon.AddDays(3), DeepReviewSchedule.Next(due, Daily, due.AddDays(1)));
        Assert.Equal(Noon.AddDays(11), DeepReviewSchedule.Next(due, Daily, due.AddDays(9).AddSeconds(1)));

        // A grid still ahead stays where it is, and a campaign with none gets one an interval from now.
        Assert.Equal(due, DeepReviewSchedule.Next(due, Daily, Noon));
        Assert.Equal(Noon.AddSeconds(Daily), DeepReviewSchedule.Next(null, Daily, Noon));
    }

    /// <summary>
    /// A resume starts the grid again from the moment the campaign went live once more: the days it stood paused are not days
    /// a deep review is owed for, and the moment the grid had before the pause is gone.
    /// </summary>
    [Fact]
    public async Task A_resume_resets_the_grid()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaigns = Campaigns(db, clock, new ManagerOptions());
        var created = await campaigns.CreateAsync(new CampaignCreateRequest("Autumn", null, null, null), Ct);
        await campaigns.StartAsync(new CampaignTransitionRequest(created.Id, null, null), Ct);

        clock.Advance(TimeSpan.FromHours(10));
        await campaigns.PauseAsync(new CampaignTransitionRequest(created.Id, null, null), Ct);
        clock.Advance(TimeSpan.FromDays(5));
        await campaigns.StartAsync(new CampaignTransitionRequest(created.Id, null, null), Ct);

        var campaign = await db.Campaigns.SingleAsync(Ct);
        var resumed = Noon.AddHours(10).AddDays(5);
        Assert.Equal(resumed.AddSeconds(Daily), campaign.DeepReviewDueAt);
        Assert.False(DeepReviewSchedule.IsDue(campaign, Now(clock)));
    }

    /// <summary>A triggered check-in and a quick look move the quick clock and leave the deep one exactly where it was.</summary>
    [Fact]
    public async Task A_triggered_or_a_quick_check_in_leaves_the_grid_alone()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaign = await LiveAsync(db, clock, new ManagerOptions());
        var items = Items(db, clock);
        var grid = campaign.DeepReviewDueAt;

        clock.Advance(TimeSpan.FromHours(6));
        var quick = await ManagerCheckIn.CreateAsync(items, db, campaign, cause: null, new ManagerOptions(), Ct, ManagerEntryChoice.QuickLook);
        await db.SaveChangesAsync(Ct);
        await FinishAsync(db, quick, clock);
        Assert.Equal(grid, campaign.DeepReviewDueAt);

        // Past the grid moment, a triggered review is still no deep review: the grid stays due for the summon to choose.
        clock.Advance(TimeSpan.FromHours(20));
        var cause = new ManagerCause(JournalKinds.WorkItemFailed, "jrn_made_up", null, null, null, 1);
        await ManagerCheckIn.CreateAsync(items, db, campaign, cause, new ManagerOptions(), Ct);
        await db.SaveChangesAsync(Ct);

        Assert.Equal(grid, campaign.DeepReviewDueAt);
        Assert.True(DeepReviewSchedule.IsDue(campaign, Now(clock)));
        Assert.Equal(Now(clock), campaign.ManagerReviewAnchor);
    }

    /// <summary>
    /// A change of the setting is read at the two moments the grid is written: the next going live, and the next deep
    /// advance. A grid already written stays where it was until one of them.
    /// </summary>
    [Fact]
    public async Task A_change_of_the_setting_takes_effect_at_the_next_going_live_or_the_next_deep_advance()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var clock = new FixedClock(Noon);
        var monitor = new TestOptionsMonitor<ManagerOptions>(new ManagerOptions());
        var campaigns = new CampaignService(db, new JournalWriter(clock), clock, TestCanceller.New(clock), TestHarness.FoldersFor(db), manager: TestOptions.ManagerSettings(monitor));
        var created = await campaigns.CreateAsync(new CampaignCreateRequest("Autumn", null, null, null), Ct);
        await campaigns.StartAsync(new CampaignTransitionRequest(created.Id, null, null), Ct);
        var campaign = await db.Campaigns.SingleAsync(Ct);

        monitor.CurrentValue = new ManagerOptions { DeepReviewSeconds = 3_600 };
        Assert.Equal(Noon.AddSeconds(Daily), campaign.DeepReviewDueAt);

        // The deep advance steps from the grid's moment by the interval in force now.
        clock.Advance(TimeSpan.FromSeconds(Daily) + TimeSpan.FromMinutes(10));
        await ManagerCheckIn.CreateAsync(Items(db, clock), db, campaign, cause: null, monitor.CurrentValue, Ct, ManagerEntryChoice.DeepReview);
        await db.SaveChangesAsync(Ct);
        Assert.Equal(Noon.AddSeconds(Daily).AddHours(1), campaign.DeepReviewDueAt);

        // And the next going live writes the grid from the interval in force then.
        monitor.CurrentValue = new ManagerOptions { DeepReviewSeconds = 7_200 };
        await FinishOpenAsync(db, clock);
        await campaigns.PauseAsync(new CampaignTransitionRequest(created.Id, null, null), Ct);
        clock.Advance(TimeSpan.FromMinutes(5));
        await campaigns.StartAsync(new CampaignTransitionRequest(created.Id, null, null), Ct);
        Assert.Equal(Now(clock).AddSeconds(7_200), (await db.Campaigns.SingleAsync(Ct)).DeepReviewDueAt);
    }

    /// <summary>
    /// Through the runtime as it is composed: a campaign started over the API takes its grid from the installation's
    /// setting, and the answer carries the moment.
    /// </summary>
    [Fact]
    public async Task A_running_runtime_starts_the_grid_from_the_installation_s_setting()
    {
        await using var api = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths => File.WriteAllText(paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false},"Manager":{"DeepReviewSeconds":3600}}"""),
            clock: new FixedClock(Noon));
        var campaign = (await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Autumn" }, Ct)).Id;

        var started = await api.PostOkAsync<CampaignDto>(Operations.CampaignStart, new { campaign_id = campaign, actor = new { type = "human", id = "ada" } }, Ct);

        Assert.Equal(new DateTimeOffset(Noon).AddHours(1), started.DeepReviewDueAt);
    }

    private static DateTime Now(TimeProvider clock) => clock.GetUtcNow().UtcDateTime;

    private static async Task<Campaign> LiveAsync(JasonDbContext db, TimeProvider clock, ManagerOptions options)
    {
        var campaigns = Campaigns(db, clock, options);
        var created = await campaigns.CreateAsync(new CampaignCreateRequest("Autumn", null, null, null), Ct);
        await campaigns.StartAsync(new CampaignTransitionRequest(created.Id, null, null), Ct);
        return await db.Campaigns.SingleAsync(Ct);
    }

    private static async Task FinishAsync(JasonDbContext db, WorkItem item, TimeProvider clock)
    {
        item.Status = WorkItemStatus.Succeeded;
        item.FinishedAt = Now(clock);
        await db.SaveChangesAsync(Ct);
    }

    private static async Task FinishOpenAsync(JasonDbContext db, TimeProvider clock)
    {
        foreach (var item in await db.WorkItems.Where(w => w.Status == WorkItemStatus.Created).ToListAsync(Ct))
        {
            await FinishAsync(db, item, clock);
        }
    }

    private static CampaignService Campaigns(JasonDbContext db, TimeProvider clock, ManagerOptions options) =>
        new(db, new JournalWriter(clock), clock, TestCanceller.New(clock), TestHarness.FoldersFor(db), manager: TestOptions.ManagerSettings(new TestOptionsMonitor<ManagerOptions>(options)));

    private static WorkItemService Items(JasonDbContext db, TimeProvider clock) =>
        new(db, new JournalWriter(clock), clock, TestCanceller.New(clock), TestOptions.PluginSettings());
}
