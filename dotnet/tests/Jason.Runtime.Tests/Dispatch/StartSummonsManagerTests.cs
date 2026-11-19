using Jason.Contracts.Api;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Harness;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// A campaign's going live summons its manager at the next scan rather than a cadence later: what a started campaign
/// needs now — work for its people, or a look at what changed while it stood — is the manager's. The start is a line of
/// the chronicle like any trigger, so it is held to the same rules: one open check-in, a check-in's own line summons
/// nobody, and an installation that narrowed the list is not woken by a kind it did not name.
/// </summary>
public class StartSummonsManagerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private const int Hourly = 3_600;

    [Fact]
    [Critical(Family.M1, "a start wakes the manager")]
    public async Task A_started_campaign_has_a_check_in_at_the_next_scan_caused_by_the_start()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CreateAsync(harness);
        await StartAsync(harness, campaign);

        Assert.Equal(1, await harness.SummonAsync(Ct));

        var checkIn = await SingleCheckInAsync(harness);
        Assert.Equal("triggered", (string?)checkIn.Context["review_intent"]);
        Assert.Equal(JournalKinds.CampaignStarted, (string?)checkIn.Context["trigger"]);
        Assert.Equal(await StartLineAsync(harness, campaign), (string?)checkIn.Context["cause"]!["journal_entry_id"]);
    }

    [Fact]
    public async Task A_campaign_started_again_after_a_pause_is_summoned_too()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CreateAsync(harness);
        await StartAsync(harness, campaign);
        Assert.Equal(1, await harness.SummonAsync(Ct));
        await FinishCheckInAsync(harness);

        await PauseAsync(harness, campaign);
        harness.Clock.Advance(TimeSpan.FromDays(7));
        await StartAsync(harness, campaign);

        Assert.Equal(1, await harness.SummonAsync(Ct));

        await using var db = harness.Open();
        var resumed = await db.WorkItems.SingleAsync(w => w.Role == ManagerCheckIn.Role && w.Status == WorkItemStatus.Created, Ct);
        Assert.Equal(JournalKinds.CampaignStarted, (string?)resumed.Context["trigger"]);
        Assert.Equal(await StartLineAsync(harness, campaign, latest: true), (string?)resumed.Context["cause"]!["journal_entry_id"]);
    }

    /// <summary>A pause cancels nothing, so a check-in can still be open when the campaign is resumed: it stays the one.</summary>
    [Fact]
    public async Task A_campaign_with_an_open_check_in_gets_no_second_from_a_start()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CreateAsync(harness);
        await StartAsync(harness, campaign);
        Assert.Equal(1, await harness.SummonAsync(Ct));

        await PauseAsync(harness, campaign);
        await StartAsync(harness, campaign);

        Assert.Equal(0, await harness.SummonAsync(Ct));
        Assert.Equal(0, await harness.SummonAsync(Ct));

        await using var db = harness.Open();
        Assert.Single(await db.WorkItems.Where(w => w.Role == ManagerCheckIn.Role).ToListAsync(Ct));
    }

    /// <summary>
    /// The scan order behind the row above: the campaign with an open check-in is skipped whole and reads nothing, so the
    /// resume's line is still above the watermark when the open review ends, and it summons one more — and only one. The
    /// cadence is then measured from that check-in, not from the start: the clock moves between the two.
    /// </summary>
    [Fact]
    public async Task A_start_while_a_check_in_is_open_summons_one_more_once_it_ends()
    {
        using var harness = new DispatchHarness(Noon, manager: new ManagerOptions { ReviewSeconds = Hourly });
        var campaign = await CreateAsync(harness);
        await StartAsync(harness, campaign);
        Assert.Equal(1, await harness.SummonAsync(Ct));
        var first = (await SingleCheckInAsync(harness)).PublicId;

        await PauseAsync(harness, campaign);
        await StartAsync(harness, campaign);
        Assert.Equal(0, await harness.SummonAsync(Ct));

        // Half an hour after the resume, the open review ends and the resume's line summons the next.
        harness.Clock.Advance(TimeSpan.FromMinutes(30));
        await FinishCheckInAsync(harness);
        Assert.Equal(1, await harness.SummonAsync(Ct));

        await using (var db = harness.Open())
        {
            var second = await db.WorkItems.SingleAsync(w => w.Role == ManagerCheckIn.Role && w.PublicId != first, Ct);
            Assert.Equal(JournalKinds.CampaignStarted, (string?)second.Context["trigger"]);
            Assert.Equal(await StartLineAsync(harness, campaign, latest: true), (string?)second.Context["cause"]!["journal_entry_id"]);
        }

        // And the cadence is measured from that check-in. Seventy minutes after the resume is past an hour from the
        // resume and short of an hour from the check-in: nothing is due. An hour after the check-in, the review is.
        await FinishCheckInAsync(harness);
        harness.Clock.Advance(TimeSpan.FromMinutes(40));
        Assert.Equal(0, await harness.SummonAsync(Ct));
        harness.Clock.Advance(TimeSpan.FromMinutes(20));
        Assert.Equal(1, await harness.SummonAsync(Ct));
        await using var read = harness.Open();
        Assert.Equal("scheduled", (string?)(await read.WorkItems.Where(w => w.Role == ManagerCheckIn.Role).OrderByDescending(w => w.Id).FirstAsync(Ct)).Context["review_intent"]);
    }

    /// <summary>
    /// A start a check-in's own attempt wrote is the check-in's own line, so it summons no successor, as a report or a
    /// failure of a check-in's does not. No run Jason launches can write one: a start sent from inside a run is refused
    /// before its actor is read (CampaignStartInsideARunTests, which carries the critical mark). What is left is a program
    /// that dropped the run's header and still named its attempt, written here at the service, below that refusal.
    /// </summary>
    [Fact]
    public async Task A_start_written_by_a_check_ins_own_attempt_summons_nothing()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CreateAsync(harness);
        await StartAsync(harness, campaign);
        Assert.Equal(1, await harness.SummonAsync(Ct));

        string attempt;
        await using (var db = harness.Open())
        {
            var checkIn = await db.WorkItems.SingleAsync(w => w.Role == ManagerCheckIn.Role, Ct);
            var run = WorkItemFactory.NewAttempt(checkIn, 1, AttemptStatus.Running, Noon);
            db.Attempts.Add(run);
            checkIn.Status = WorkItemStatus.Processing;
            await db.SaveChangesAsync(Ct);
            attempt = run.PublicId;
        }

        var byTheRun = new ActorRef(ActorType.Attempt, attempt);
        await PauseAsync(harness, campaign, byTheRun);
        await StartAsync(harness, campaign, byTheRun);
        await FinishCheckInAsync(harness);

        Assert.Equal(0, await harness.SummonAsync(Ct));

        await using var read = harness.Open();
        Assert.Single(await read.WorkItems.Where(w => w.Role == ManagerCheckIn.Role).ToListAsync(Ct));
        Assert.Equal(
            (await read.Journal.MaxAsync(e => e.Id, Ct)),
            (await read.Campaigns.SingleAsync(Ct)).ManagerEventWatermark);
    }

    /// <summary>An installation that narrowed the list before the kind was in it is not woken by a start until it adds it.</summary>
    [Fact]
    public async Task An_installation_whose_settings_narrow_the_list_is_not_woken_by_a_start()
    {
        using var harness = new DispatchHarness(Noon, manager: new ManagerOptions { Triggers = [JournalKinds.WorkItemFailed] });
        var campaign = await CreateAsync(harness);
        await StartAsync(harness, campaign);

        Assert.Equal(0, await harness.SummonAsync(Ct));

        await using var db = harness.Open();
        Assert.Empty(await db.WorkItems.Where(w => w.Role == ManagerCheckIn.Role).ToListAsync(Ct));
        Assert.Equal(await db.Journal.MaxAsync(e => e.Id, Ct), (await db.Campaigns.SingleAsync(Ct)).ManagerEventWatermark);
    }

    private static async Task<string> CreateAsync(DispatchHarness harness)
    {
        await using var db = harness.Open();
        return (await Campaigns(harness, db).CreateAsync(new CampaignCreateRequest("LatAm", null, null, null), Ct)).Id;
    }

    private static async Task StartAsync(DispatchHarness harness, string campaign, ActorRef? actor = null)
    {
        await using var db = harness.Open();
        await Campaigns(harness, db).StartAsync(new CampaignTransitionRequest(campaign, actor, null), Ct);
    }

    private static async Task PauseAsync(DispatchHarness harness, string campaign, ActorRef? actor = null)
    {
        await using var db = harness.Open();
        await Campaigns(harness, db).PauseAsync(new CampaignTransitionRequest(campaign, actor, null), Ct);
    }

    private static CampaignService Campaigns(DispatchHarness harness, JasonDbContext db) =>
        new(db, new JournalWriter(harness.Clock), harness.Clock, TestCanceller.New(harness.Clock), TestHarness.FoldersFor(db));

    private static async Task<string> StartLineAsync(DispatchHarness harness, string campaign, bool latest = false)
    {
        await using var db = harness.Open();
        var lines = db.Journal.Where(e => e.Kind == JournalKinds.CampaignStarted && e.Campaign!.PublicId == campaign);
        return await (latest ? lines.OrderByDescending(e => e.Id) : lines.OrderBy(e => e.Id)).Select(e => e.PublicId).FirstAsync(Ct);
    }

    private static async Task FinishCheckInAsync(DispatchHarness harness)
    {
        await using var db = harness.Open();
        var open = await db.WorkItems.SingleAsync(w => w.Role == ManagerCheckIn.Role && w.Status != WorkItemStatus.Succeeded, Ct);
        open.Status = WorkItemStatus.Succeeded;
        open.FinishedAt = harness.Clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(Ct);
    }

    private static async Task<WorkItem> SingleCheckInAsync(DispatchHarness harness)
    {
        await using var db = harness.Open();
        return await db.WorkItems.SingleAsync(w => w.Role == ManagerCheckIn.Role, Ct);
    }
}
