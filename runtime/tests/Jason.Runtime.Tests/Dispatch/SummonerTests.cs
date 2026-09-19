using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Reports;
using Jason.Runtime.Tests.Reports;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// The scan step that decides a campaign wants looking at. What is asserted here is the bookkeeping — which
/// campaigns are considered, what is consumed, and what the database refuses — rather than what a manager then
/// does with the check-in, which is a launched child's business and is proved against one.
/// </summary>
public class SummonerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task A_line_of_a_trigger_kind_summons_one_check_in()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await SeedAsync(harness, live: true);
        await JournalAsync(harness, campaign, JournalKinds.WorkItemFailed);

        Assert.Equal(1, await SummonAsync(harness));

        var item = await SingleCheckInAsync(harness);
        Assert.Equal("triggered", (string?)item.Context["review_intent"]);
        Assert.Equal(JournalKinds.WorkItemFailed, (string?)item.Context["trigger"]);
    }

    [Fact]
    public async Task A_line_of_any_other_kind_summons_nothing_and_is_still_consumed()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await SeedAsync(harness, live: true);
        await JournalAsync(harness, campaign, JournalKinds.WorkItemSucceeded);

        Assert.Equal(0, await SummonAsync(harness));

        await using var db = harness.Open();
        var stored = await db.Campaigns.SingleAsync(Ct);
        Assert.True(stored.ManagerEventWatermark > 0, "the line was read and not accounted for");
        Assert.Empty(await db.WorkItems.Where(w => w.Role == ManagerCheckIn.Role).ToListAsync(Ct));
    }

    /// <summary>
    /// The burst. Ten failures are one review, not ten: the manager about to be launched reads the whole
    /// chronicle anyway, so everything read in the scan that creates a check-in is accounted for by it.
    /// </summary>
    [Fact]
    public async Task Ten_failures_in_one_read_are_one_check_in()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await SeedAsync(harness, live: true);
        for (var i = 0; i < 10; i++)
        {
            await JournalAsync(harness, campaign, JournalKinds.WorkItemFailed);
        }

        Assert.Equal(1, await SummonAsync(harness));

        var item = await SingleCheckInAsync(harness);
        Assert.Equal(10, (int?)item.Context["cause"]!["qualifying_count"]);
    }

    /// <summary>
    /// A campaign already waiting for a review is skipped whole — and consumes nothing, so a line that arrives
    /// while a review is open is still above the watermark when that review ends and summons the next one. A
    /// manager mid-run may have read the chronicle before that line was written.
    /// </summary>
    [Fact]
    public async Task A_campaign_with_an_open_check_in_is_left_alone_and_consumes_nothing()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await SeedAsync(harness, live: true);
        await JournalAsync(harness, campaign, JournalKinds.WorkItemFailed);
        Assert.Equal(1, await SummonAsync(harness));

        await JournalAsync(harness, campaign, JournalKinds.ApprovalRejected);
        Assert.Equal(0, await SummonAsync(harness));

        int watermark;
        string open;
        await using (var db = harness.Open())
        {
            watermark = (await db.Campaigns.SingleAsync(Ct)).ManagerEventWatermark;
            var entries = await db.Journal.OrderBy(e => e.Id).ToListAsync(Ct);
            Assert.True(entries[^1].Id > watermark, "the line that arrived mid-review was consumed anyway");
            open = (await db.WorkItems.SingleAsync(w => w.Role == ManagerCheckIn.Role, Ct)).PublicId;
        }

        // And the moment the open one ends, the line that waited summons the next.
        await using (var db = harness.Open())
        {
            var item = await db.WorkItems.SingleAsync(w => w.PublicId == open, Ct);
            item.Status = WorkItemStatus.Succeeded;
            item.FinishedAt = Noon;
            await db.SaveChangesAsync(Ct);
        }

        Assert.Equal(1, await SummonAsync(harness));

        await using (var db = harness.Open())
        {
            var second = await db.WorkItems
                .Where(w => w.Role == ManagerCheckIn.Role && w.PublicId != open)
                .SingleAsync(Ct);
            Assert.Equal(JournalKinds.ApprovalRejected, (string?)second.Context["trigger"]);
        }
    }

    [Theory]
    [InlineData(CampaignStatus.Draft)]
    [InlineData(CampaignStatus.Paused)]
    [InlineData(CampaignStatus.Archived)]
    public async Task Only_an_active_campaign_is_ever_summoned(CampaignStatus status)
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await SeedAsync(harness, live: false, status: status);
        await JournalAsync(harness, campaign, JournalKinds.WorkItemFailed);

        Assert.Equal(0, await SummonAsync(harness));

        await using var db = harness.Open();

        // And its lines wait for it: a campaign somebody paused is not one whose events are thrown away.
        Assert.Equal(0, (await db.Campaigns.SingleAsync(Ct)).ManagerEventWatermark);
    }

    /// <summary>The cadence, with nothing in the chronicle at all.</summary>
    [Fact]
    public async Task A_due_campaign_is_summoned_with_no_events()
    {
        using var harness = new DispatchHarness(Noon, manager: new ManagerOptions { ReviewSeconds = 3_600 });
        await SeedAsync(harness, live: true);

        Assert.Equal(0, await SummonAsync(harness));

        harness.Clock.Advance(TimeSpan.FromSeconds(3_600));
        Assert.Equal(1, await SummonAsync(harness));

        var item = await SingleCheckInAsync(harness);
        Assert.Equal("scheduled", (string?)item.Context["review_intent"]);
        Assert.Null(item.Context["trigger"]);
    }

    /// <summary>And never twice, however many scans run while the first one is open.</summary>
    [Fact]
    public async Task A_due_campaign_is_not_summoned_again_while_its_check_in_is_open()
    {
        using var harness = new DispatchHarness(Noon, manager: new ManagerOptions { ReviewSeconds = 3_600 });
        await SeedAsync(harness, live: true);
        harness.Clock.Advance(TimeSpan.FromSeconds(3_600));
        Assert.Equal(1, await SummonAsync(harness));

        harness.Clock.Advance(TimeSpan.FromDays(7));
        Assert.Equal(0, await SummonAsync(harness));
        Assert.Equal(0, await SummonAsync(harness));

        await using var db = harness.Open();
        Assert.Single(await db.WorkItems.Where(w => w.Role == ManagerCheckIn.Role).ToListAsync(Ct));
    }

    /// <summary>
    /// A manager must not summon a manager. A review whose host is missing fails at pre-flight like any other
    /// item, and that failure is a line of exactly the kind that summons a review — so without this the loop
    /// feeds itself for ever, one launch a tick.
    /// </summary>
    [Fact]
    public async Task A_failed_check_in_does_not_summon_its_own_successor()
    {
        using var harness = new DispatchHarness(Noon, manager: new ManagerOptions { ReviewSeconds = 3_600 });
        await SeedAsync(harness, live: true);
        harness.Clock.Advance(TimeSpan.FromSeconds(3_600));
        Assert.Equal(1, await SummonAsync(harness));

        // The check-in fails, and the chronicle says so — exactly as it would for any other item.
        await using (var db = harness.Open())
        {
            var item = await db.WorkItems.SingleAsync(w => w.Role == ManagerCheckIn.Role, Ct);
            item.Status = WorkItemStatus.Failed;
            item.FinishedAt = harness.Clock.GetUtcNow().UtcDateTime;
            new JournalWriter(harness.Clock).Append(
                db,
                Actors.Dispatcher,
                JournalKinds.WorkItemFailed,
                campaign: null,
                key: "status",
                workItem: item);
            await db.SaveChangesAsync(Ct);
        }

        Assert.Equal(0, await SummonAsync(harness));

        await using var read = harness.Open();
        Assert.Single(await read.WorkItems.Where(w => w.Role == ManagerCheckIn.Role).ToListAsync(Ct));
    }

    /// <summary>
    /// The other half of "a manager never summons a manager", and the half that reaches further: a line a
    /// check-in's own attempt <em>wrote</em>. A report carries its reporter as the actor and names no attempt
    /// at all — the chronicle's attempt column is filled only when an attempt object is passed, and the report
    /// path passes none — so a manager that reports an effect it produced would summon the next manager, which
    /// reads the same chronicle and reports again.
    /// </summary>
    [Fact]
    public async Task A_report_written_by_a_check_ins_attempt_summons_nobody()
    {
        using var harness = new DispatchHarness(Noon, manager: new ManagerOptions { ReviewSeconds = 3_600 });
        await SeedAsync(harness, live: true);
        harness.Clock.Advance(TimeSpan.FromSeconds(3_600));
        Assert.Equal(1, await SummonAsync(harness));

        var attempt = await AttemptForCheckInAsync(harness);
        await ReportAsync(harness, attempt);
        await FinishCheckInAsync(harness);

        Assert.Equal(0, await SummonAsync(harness));

        await using var db = harness.Open();
        Assert.Single(await db.WorkItems.Where(w => w.Role == ManagerCheckIn.Role).ToListAsync(Ct));
    }

    /// <summary>
    /// Two writers both looking for an open check-in would both see none and both insert, so the rule is the
    /// database's: a unique index over the open ones. Proved at the row, because two scans cannot overlap in
    /// one runtime — the scan gate serializes them — and the guarantee has to hold for two runtimes anyway.
    /// </summary>
    [Fact]
    public async Task The_second_check_in_for_one_campaign_is_refused_by_the_database()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await SeedAsync(harness, live: true);
        await JournalAsync(harness, campaign, JournalKinds.WorkItemFailed);
        Assert.Equal(1, await SummonAsync(harness));

        await using var db = harness.Open();
        var stored = await db.Campaigns.SingleAsync(Ct);
        db.WorkItems.Add(new WorkItem
        {
            PublicId = "wi_second",
            CampaignId = stored.Id,
            Kind = WorkItemKind.AiRole,
            Role = ManagerCheckIn.Role,
            Status = WorkItemStatus.Created,
            CreatedByType = ActorType.System,
            CreatedById = "dispatcher",
            CreatedAt = Noon,
            UpdatedAt = Noon,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
    }

    /// <summary>
    /// One campaign's summon failing must cost that campaign and nothing else. The scan is four steps and the
    /// fourth hands out work: a summon that threw through the whole scan would skip every campaign after the
    /// one that failed <em>and</em> the claim, and if the cause persisted — a write lock that keeps timing out,
    /// a constraint nobody has noticed — the dispatcher would stop handing out work at all, quietly, while
    /// still logging a tick.
    /// </summary>
    [Fact]
    public async Task One_campaigns_summon_failing_costs_that_campaign_and_nothing_else()
    {
        using var harness = new DispatchHarness(Noon, manager: new ManagerOptions { ReviewSeconds = 3_600 });
        var first = await SeedAsync(harness, live: true, name: "First");
        var second = await SeedAsync(harness, live: true, name: "Second");
        harness.Clock.Advance(TimeSpan.FromSeconds(3_600));

        // The next save inside the summon throws, whichever campaign reaches it first.
        harness.InterfereOnceBeforeSaving(() => throw new InvalidOperationException("the writer was busy"));

        var summoned = await harness.SummonAsync(Ct);

        Assert.Equal(1, summoned);

        await using var db = harness.Open();
        var reviewed = await db.WorkItems
            .Where(w => w.Role == ManagerCheckIn.Role)
            .Select(w => w.Campaign!.PublicId)
            .ToListAsync(Ct);

        var missed = new[] { first, second }.Except(reviewed).ToList();
        Assert.Single(reviewed);
        Assert.Single(missed);

        // And the one that was missed is summoned on the very next scan, with nothing to repair by hand.
        Assert.Equal(1, await harness.SummonAsync(Ct));
    }

    /// <summary>
    /// The watermark is written guarded, and this is the statement that writes it: it moves a campaign's
    /// watermark only from the value the summon read. Another writer who has moved it on may already have
    /// turned those lines into a review, so putting an older number back would have them reviewed twice.
    /// </summary>
    /// <remarks>
    /// Two writers cannot be staged inside one process — this database takes one writer at a time, and the scan
    /// gate serializes scans, so a competing write from inside a summon simply waits for it. The guard is
    /// proved where it lives, as the statement, with a stale value in hand; what it protects against is two
    /// runtimes, which is also the only thing that can really race here.
    /// </remarks>
    [Fact]
    public async Task A_watermark_another_writer_moved_is_refused_rather_than_overwritten()
    {
        using var harness = new DispatchHarness(Noon);
        await SeedAsync(harness, live: true);

        await using var db = harness.Open();
        var campaign = await db.Campaigns.SingleAsync(Ct);
        var before = campaign.ManagerEventWatermark;

        campaign.ManagerEventWatermark = 999;
        await db.SaveChangesAsync(Ct);

        Assert.Equal(0, await Summoner.AccountForAsync(db, campaign.Id, before, 40, Ct));
        Assert.Equal(999, (await db.Campaigns.AsNoTracking().SingleAsync(Ct)).ManagerEventWatermark);

        // And the same statement moves it when the value read is still the value stored.
        Assert.Equal(1, await Summoner.AccountForAsync(db, campaign.Id, 999, 1_200, Ct));
        Assert.Equal(1_200, (await db.Campaigns.AsNoTracking().SingleAsync(Ct)).ManagerEventWatermark);
    }

    /// <summary>
    /// How a refused insert is told from a failed one. The database is what decides that one check-in is open,
    /// so an insert refused while one exists is the race being lost — ordinary, and the review somebody else
    /// created is the review — while an insert refused when none exists is something else, which must not be
    /// filed under a race that did not happen.
    /// </summary>
    [Fact]
    public async Task A_refused_insert_is_told_from_a_failed_one_by_what_is_open()
    {
        using var harness = new DispatchHarness(Noon, manager: new ManagerOptions { ReviewSeconds = 3_600 });
        await SeedAsync(harness, live: true);
        await using var db = harness.Open();
        var stored = await db.Campaigns.SingleAsync(Ct);

        Assert.False(await Summoner.OpenCheckInAsync(db, stored.Id, Ct));

        harness.Clock.Advance(TimeSpan.FromSeconds(3_600));
        Assert.Equal(1, await harness.SummonAsync(Ct));
        Assert.True(await Summoner.OpenCheckInAsync(db, stored.Id, Ct));

        // A finished review is not an open one, so a refusal after this would be a failure and not a race.
        await FinishCheckInAsync(harness);
        Assert.False(await Summoner.OpenCheckInAsync(db, stored.Id, Ct));
    }

    /// <summary>An attempt of the campaign's open check-in, as the claim would have made one.</summary>
    private static async Task<string> AttemptForCheckInAsync(DispatchHarness harness)
    {
        await using var db = harness.Open();
        var checkIn = await db.WorkItems.SingleAsync(w => w.Role == ManagerCheckIn.Role, Ct);
        var attempt = WorkItemFactory.NewAttempt(checkIn, 1, AttemptStatus.Running, Noon);
        db.Attempts.Add(attempt);
        checkIn.Status = WorkItemStatus.Processing;
        await db.SaveChangesAsync(Ct);
        return attempt.PublicId;
    }

    /// <summary>What a manager reporting an effect it produced outside Jason really writes.</summary>
    private static async Task ReportAsync(DispatchHarness harness, string attemptPublicId)
    {
        await using var db = harness.Open();
        var campaign = await db.Campaigns.SingleAsync(Ct);
        var submission = ReportedWorld.Submission();
        submission["campaign_id"] = campaign.PublicId;
        submission["actor"] = new JsonObject { ["type"] = "attempt", ["id"] = attemptPublicId };

        await new ReportService(db, new JournalWriter(harness.Clock), harness.Clock).SubmitAsync(submission, Ct);
    }

    private static async Task FinishCheckInAsync(DispatchHarness harness)
    {
        await using var db = harness.Open();
        var checkIn = await db.WorkItems.SingleAsync(w => w.Role == ManagerCheckIn.Role, Ct);
        checkIn.Status = WorkItemStatus.Succeeded;
        checkIn.FinishedAt = harness.Clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(Ct);
    }

    private static async Task<string> SeedAsync(
        DispatchHarness harness,
        bool live,
        CampaignStatus status = CampaignStatus.Active,
        string name = "Work")
    {
        await using var db = harness.Open();
        var campaign = WorkItemFactory.NewCampaign(name, live ? CampaignStatus.Active : status, now: Noon);
        if (live)
        {
            campaign.ManagerReviewAnchor = Noon;
        }

        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(Ct);
        return campaign.PublicId;
    }

    private static async Task JournalAsync(DispatchHarness harness, string campaignPublicId, string kind)
    {
        await using var db = harness.Open();
        var campaign = await db.Campaigns.SingleAsync(c => c.PublicId == campaignPublicId, Ct);
        new JournalWriter(harness.Clock).Append(db, Actors.Dispatcher, kind, campaign, key: "status");
        await db.SaveChangesAsync(Ct);
    }

    private static Task<int> SummonAsync(DispatchHarness harness) => harness.SummonAsync(Ct);

    private static async Task<WorkItem> SingleCheckInAsync(DispatchHarness harness)
    {
        await using var db = harness.Open();
        return await db.WorkItems.SingleAsync(w => w.Role == ManagerCheckIn.Role, Ct);
    }
}
