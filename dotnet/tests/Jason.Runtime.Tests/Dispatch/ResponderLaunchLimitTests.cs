using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Execution;
using Jason.Contracts.Ids;
using Jason.Contracts.Plugins;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// How many responder runs one campaign may start in a UTC day. The claim counts the responder attempts of the item's
/// campaign that started since 00:00Z, and a responder it finds at the limit is held, not failed: it waits, with no
/// attempt, until the next 00:00Z, so the reply it would answer never reaches the manager by a second path. The count
/// reads columns and the clock only.
/// </summary>
public class ResponderLaunchLimitTests
{
    /// <summary>Half past noon on a day in the middle of a month, so neither midnight is at a month's or a year's edge.</summary>
    private static readonly DateTime Noon = new(2026, 10, 6, 12, 30, 0, DateTimeKind.Utc);

    private static readonly DateTime Today = Noon.Date;

    private static readonly DateTime Tomorrow = Today.AddDays(1);

    private static readonly ActorRef Operator = new(ActorType.Human, "operator");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// At the limit the third responder of the day is held: waiting, with no attempt, until the next 00:00Z, its last
    /// error the limit's code and a message naming the limit, the moment and the ways out, and one line that says why.
    /// </summary>
    [Fact]
    public async Task A_responder_at_the_day_s_limit_is_held_until_the_next_midnight_with_no_attempt()
    {
        using var harness = Harness(limit: 2);
        var campaign = await CampaignAsync(harness);
        await LaunchedAsync(harness, campaign, Today.AddHours(1), Today.AddHours(11));
        var held = await QueuedAsync(harness, campaign);

        Assert.Empty(await harness.ClaimAsync(Ct));

        await using var db = harness.Open();
        var item = await db.WorkItems.AsNoTracking().Include(w => w.Attempts).SingleAsync(w => w.Id == held, Ct);
        Assert.Equal(WorkItemStatus.Waiting, item.Status);
        Assert.Empty(item.Attempts);
        Assert.Equal(0, item.AttemptCount);
        Assert.Equal(AttemptErrors.ResponderLaunchesSpent, item.LastError!.Code);
        Assert.False(item.LastError.Retriable);
        Assert.Equal(
            "Responder work in this campaign has reached the limit Launches:MaxPerDay:responder sets, 2 runs started since "
            + "2026-10-06T00:00:00Z; this item waits until 2026-10-07T00:00:00Z. A person's workitem.resume lets it go sooner, "
            + "past the limit, and a raised limit applies from the next scan to work not yet held, while this item keeps its moment.",
            item.LastError.Message);

        var wait = await db.WorkItemWaits.AsNoTracking().Include(w => w.Items).SingleAsync(w => w.WorkItemId == held, Ct);
        Assert.Equal((WaitStatus.Open, (int?)null, (int?)null, (WaitMode?)null), (wait.Status, wait.AttemptId, wait.ProviderBlockId, wait.Mode));
        Assert.Empty(wait.Items);
        Assert.Equal((Tomorrow, Tomorrow), (wait.Until, wait.Deadline));

        var line = await db.Journal.AsNoTracking().SingleAsync(e => e.WorkItemId == item.PublicId && e.Kind == JournalKinds.WorkItemWaiting, Ct);
        Assert.Null(line.Key);
        Assert.Equal(
            """{"on":[],"mode":null,"until":"2026-10-07T00:00:00.000Z","deadline":"2026-10-07T00:00:00.000Z","held_by":{"code":"responder_launches_spent","limit":2}}""",
            line.New!.ToJsonString());
        Assert.Equal((ActorType.System, "dispatcher"), (line.ActorType, line.ActorId));
        Assert.DoesNotContain(await db.Journal.AsNoTracking().Where(e => e.WorkItemId == item.PublicId).Select(e => e.Kind).ToListAsync(Ct), kind => kind == JournalKinds.WorkItemScheduled);
    }

    /// <summary>
    /// The day is UTC and starts at 00:00Z: runs started a minute before it do not count, and the held item, released at
    /// 00:00Z by the moment it waited for, is claimed by the next claim.
    /// </summary>
    [Fact]
    public async Task Yesterday_s_runs_do_not_count_and_midnight_releases_the_held_item_to_the_next_claim()
    {
        using var harness = Harness(limit: 2, now: Today.AddHours(23).AddMinutes(30));
        var campaign = await CampaignAsync(harness);
        await LaunchedAsync(harness, campaign, Today.AddHours(22), Today.AddHours(23));
        var held = await QueuedAsync(harness, campaign);
        Assert.Empty(await harness.ClaimAsync(Ct));

        // A minute before midnight the moment has not come.
        harness.Clock.Now = new DateTimeOffset(Tomorrow.AddMinutes(-1));
        Assert.Equal(0, await harness.ResumeAsync(Ct));

        harness.Clock.Now = new DateTimeOffset(Tomorrow);
        Assert.Equal(1, await harness.ResumeAsync(Ct));
        var work = Assert.Single(await harness.ClaimAsync(Ct));

        await using var db = harness.Open();
        var item = await db.WorkItems.AsNoTracking().SingleAsync(w => w.Id == held, Ct);
        Assert.Equal(item.PublicId, work.WorkItemPublicId);
        Assert.Equal(WorkItemStatus.Scheduled, item.Status);
        var wait = await db.WorkItemWaits.AsNoTracking().SingleAsync(w => w.WorkItemId == held, Ct);
        Assert.Equal((WaitStatus.Released, WaitReleaseReason.MomentReached), (wait.Status, wait.ReleaseReason));
    }

    /// <summary>Runs started at 23:59Z the day before are yesterday's: a responder queued today with two of those behind it runs.</summary>
    [Fact]
    public async Task Runs_started_a_minute_before_midnight_belong_to_the_day_before()
    {
        using var harness = Harness(limit: 2);
        var campaign = await CampaignAsync(harness);
        await LaunchedAsync(harness, campaign, Today.AddMinutes(-1), Today.AddMinutes(-1));
        var queued = await QueuedAsync(harness, campaign);

        var work = Assert.Single(await harness.ClaimAsync(Ct));

        Assert.Equal(await PublicIdAsync(harness, queued), work.WorkItemPublicId);
    }

    /// <summary>The limit is a campaign's: another campaign's responder runs while this one's is held.</summary>
    [Fact]
    public async Task Another_campaign_s_responder_is_claimed_while_this_one_s_is_held()
    {
        using var harness = Harness(limit: 2);
        var spent = await CampaignAsync(harness);
        var other = await CampaignAsync(harness);
        await LaunchedAsync(harness, spent, Today.AddHours(1), Today.AddHours(2));
        await LaunchedAsync(harness, other, Today.AddHours(1));
        var held = await QueuedAsync(harness, spent);
        var runs = await QueuedAsync(harness, other);

        var work = Assert.Single(await harness.ClaimAsync(Ct));

        Assert.Equal(await PublicIdAsync(harness, runs), work.WorkItemPublicId);
        Assert.Equal(WorkItemStatus.Waiting, (await ItemAsync(harness, held)).Status);
    }

    /// <summary>
    /// A run is counted when it started, and an attempt the claim refused never started: it spends nothing, nor does an
    /// attempt of another role, nor one of a provider operation.
    /// </summary>
    [Fact]
    public async Task An_attempt_that_never_started_or_is_not_a_responder_s_spends_nothing()
    {
        using var harness = Harness(limit: 2);
        var campaign = await CampaignAsync(harness);
        await LaunchedAsync(harness, campaign, Today.AddHours(1));
        await using (var db = harness.Open())
        {
            var tracked = await db.Campaigns.SingleAsync(c => c.Id == campaign, Ct);
            var refused = WorkItemFactory.NewAiRole(tracked, role: Responder.Role, now: Today.AddHours(2));
            Ended(refused, WorkItemStatus.Failed, AttemptStatus.Failed, claimed: Today.AddHours(2), started: null);
            var researcher = WorkItemFactory.NewAiRole(tracked, role: "researcher", now: Today.AddHours(3));
            Ended(researcher, WorkItemStatus.Succeeded, AttemptStatus.Succeeded, claimed: Today.AddHours(3), started: Today.AddHours(3));
            var read = WorkItemFactory.NewProviderOp(tracked, operation: Responder.ReadOperation, now: Today.AddHours(4));
            Ended(read, WorkItemStatus.Succeeded, AttemptStatus.Succeeded, claimed: Today.AddHours(4), started: Today.AddHours(4));
            db.WorkItems.AddRange(refused, researcher, read);
            await db.SaveChangesAsync(Ct);
        }

        var queued = await QueuedAsync(harness, campaign);

        var work = Assert.Single(await harness.ClaimAsync(Ct));

        Assert.Equal(await PublicIdAsync(harness, queued), work.WorkItemPublicId);
    }

    /// <summary>
    /// The limit is read live: a raised limit applies at the next scan to work not yet held, and an item already held keeps
    /// its moment.
    /// </summary>
    [Fact]
    public async Task A_raised_limit_applies_at_the_next_scan_and_an_item_already_held_keeps_its_moment()
    {
        using var harness = Harness(limit: 2);
        var campaign = await CampaignAsync(harness);
        await LaunchedAsync(harness, campaign, Today.AddHours(1), Today.AddHours(2));
        var held = await QueuedAsync(harness, campaign);
        Assert.Empty(await harness.ClaimAsync(Ct));

        Limit(harness, 3);
        var next = await QueuedAsync(harness, campaign);
        var work = Assert.Single(await harness.ClaimAsync(Ct));

        Assert.Equal(await PublicIdAsync(harness, next), work.WorkItemPublicId);
        Assert.Equal(WorkItemStatus.Waiting, (await ItemAsync(harness, held)).Status);
        await using var db = harness.Open();
        var wait = await db.WorkItemWaits.AsNoTracking().SingleAsync(w => w.WorkItemId == held, Ct);
        Assert.Equal((WaitStatus.Open, Tomorrow, Tomorrow), (wait.Status, wait.Until, wait.Deadline));
    }

    /// <summary>
    /// A held responder does not make its campaign busy: the provider operation queued behind it is claimed at the next
    /// scan, the one-item-per-campaign rule having given the campaign's turn in the first to the responder.
    /// </summary>
    [Fact]
    public async Task The_campaign_s_other_work_is_claimed_at_the_next_scan()
    {
        using var harness = Harness(limit: 2);
        Routed(harness);
        var campaign = await CampaignAsync(harness);
        await LaunchedAsync(harness, campaign, Today.AddHours(1), Today.AddHours(2));
        var held = await QueuedAsync(harness, campaign);
        int reading;
        await using (var db = harness.Open())
        {
            var tracked = await db.Campaigns.SingleAsync(c => c.Id == campaign, Ct);
            var item = WorkItemFactory.NewProviderOp(tracked, now: Noon);
            item.Context = new JsonObject
            {
                [WorkItemService.InputKey] = new JsonObject { ["campaign"] = new JsonObject { ["external_id"] = "c-7714" } },
            };
            db.WorkItems.Add(item);
            await db.SaveChangesAsync(Ct);
            reading = item.Id;
        }

        Assert.Empty(await harness.ClaimAsync(Ct));
        var work = Assert.Single(await harness.ClaimAsync(Ct));

        Assert.Equal(await PublicIdAsync(harness, reading), work.WorkItemPublicId);
        Assert.NotNull(work.Plan);
        Assert.Equal(WorkItemStatus.Scheduled, (await ItemAsync(harness, reading)).Status);
        Assert.Equal(WorkItemStatus.Waiting, (await ItemAsync(harness, held)).Status);
    }

    /// <summary>
    /// A responder queued by hand is a responder: the count is by role, and the claim reads no intent, so it counts and is
    /// held like one the respond step created.
    /// </summary>
    [Fact]
    public async Task A_responder_queued_by_hand_counts_and_is_held()
    {
        using var harness = Harness(limit: 2);
        var campaign = await CampaignAsync(harness);
        await LaunchedAsync(harness, campaign, Today.AddHours(1));
        await using (var db = harness.Open())
        {
            var tracked = await db.Campaigns.SingleAsync(c => c.Id == campaign, Ct);
            var byHand = WorkItemFactory.NewAiRole(tracked, role: Responder.Role, now: Today.AddHours(2));
            Assert.Equal(ActorType.Human, byHand.CreatedByType);
            Ended(byHand, WorkItemStatus.Succeeded, AttemptStatus.Succeeded, claimed: Today.AddHours(2), started: Today.AddHours(2));
            db.WorkItems.Add(byHand);
            await db.SaveChangesAsync(Ct);
        }

        var held = await QueuedAsync(harness, campaign);

        Assert.Empty(await harness.ClaimAsync(Ct));
        Assert.Equal(WorkItemStatus.Waiting, (await ItemAsync(harness, held)).Status);
    }

    /// <summary>
    /// A person's resume lets a held responder go: the claim takes it though the day's count is still at the limit, with no
    /// second line saying it waits, and the person's own line, with their name and reason, is the record of who let it go.
    /// </summary>
    [Fact]
    public async Task A_held_responder_a_person_resumes_is_claimed_though_the_day_is_spent()
    {
        using var harness = Harness(limit: 2);
        var campaign = await CampaignAsync(harness);
        await LaunchedAsync(harness, campaign, Today.AddHours(1), Today.AddHours(2));
        var held = await QueuedAsync(harness, campaign);
        Assert.Empty(await harness.ClaimAsync(Ct));

        await ResumeAsync(harness, held);
        var work = Assert.Single(await harness.ClaimAsync(Ct));

        Assert.Equal(await PublicIdAsync(harness, held), work.WorkItemPublicId);
        var item = await ItemAsync(harness, held);
        Assert.Equal(WorkItemStatus.Scheduled, item.Status);
        await using var db = harness.Open();
        var lines = await db.Journal.AsNoTracking().Where(e => e.WorkItemId == item.PublicId).OrderBy(e => e.Id).ToListAsync(Ct);
        Assert.Single(lines, e => e.Kind == JournalKinds.WorkItemWaiting);
        var resumed = Assert.Single(lines, e => e.Kind == JournalKinds.WorkItemResumed);
        Assert.Equal((ActorType.Human, "operator", "answer this one today"), (resumed.ActorType, resumed.ActorId, resumed.Reason));
    }

    /// <summary>
    /// Midnight lets a hold go without a person's word, so the next claim counts the new day as usual: a day already spent
    /// by then holds the item again, to the midnight after, with a second line that says so.
    /// </summary>
    [Fact]
    public async Task A_hold_released_at_midnight_is_counted_again_and_held_again_when_the_new_day_is_spent()
    {
        using var harness = Harness(limit: 2);
        var campaign = await CampaignAsync(harness);
        await LaunchedAsync(harness, campaign, Today.AddHours(1), Today.AddHours(2));
        var held = await QueuedAsync(harness, campaign);
        Assert.Empty(await harness.ClaimAsync(Ct));

        harness.Clock.Now = new DateTimeOffset(Tomorrow.AddHours(1));
        await LaunchedAsync(harness, campaign, Tomorrow.AddMinutes(10), Tomorrow.AddMinutes(20));
        Assert.Equal(1, await harness.ResumeAsync(Ct));
        Assert.Empty(await harness.ClaimAsync(Ct));

        var item = await ItemAsync(harness, held);
        Assert.Equal(WorkItemStatus.Waiting, item.Status);
        await using var db = harness.Open();
        var waits = await db.WorkItemWaits.AsNoTracking().Where(w => w.WorkItemId == held).OrderBy(w => w.Id).ToListAsync(Ct);
        Assert.Equal(2, waits.Count);
        Assert.Equal((WaitStatus.Released, (WaitReleaseReason?)WaitReleaseReason.MomentReached), (waits[0].Status, waits[0].ReleaseReason));
        Assert.Equal((WaitStatus.Open, (DateTime?)Tomorrow.AddDays(1)), (waits[1].Status, waits[1].Until));
        Assert.Equal(2, await db.Journal.AsNoTracking().CountAsync(e => e.WorkItemId == item.PublicId && e.Kind == JournalKinds.WorkItemWaiting, Ct));
    }

    /// <summary>
    /// Only the hold is let go by a resume: a responder a person resumed from the wait it was created with, on its thread's
    /// read, is counted like any other and held at the limit.
    /// </summary>
    [Fact]
    public async Task A_responder_resumed_from_another_wait_is_still_held_at_the_limit()
    {
        using var harness = Harness(limit: 2);
        var campaign = await CampaignAsync(harness);
        await LaunchedAsync(harness, campaign, Today.AddHours(1), Today.AddHours(2));
        int responder;
        await using (var db = harness.Open())
        {
            var tracked = await db.Campaigns.SingleAsync(c => c.Id == campaign, Ct);
            // The read has ended, so the claim's one item of this campaign is the responder; the resume is a person's.
            var read = WorkItemFactory.NewProviderOp(tracked, operation: Responder.ReadOperation, now: Noon);
            Ended(read, WorkItemStatus.Succeeded, AttemptStatus.Succeeded, claimed: Noon, started: Noon);
            var item = WorkItemFactory.NewAiRole(tracked, role: Responder.Role, now: Noon);
            db.WorkItems.AddRange(read, item);
            await db.SaveChangesAsync(Ct);
            harness.Resolve<AttemptOutcomes>().WaitFromCreation(db, item, [read], Actors.Dispatcher);
            await db.SaveChangesAsync(Ct);
            responder = item.Id;
        }

        await ResumeAsync(harness, responder);
        Assert.Empty(await harness.ClaimAsync(Ct));

        Assert.Equal(WorkItemStatus.Waiting, (await ItemAsync(harness, responder)).Status);
        await using var check = harness.Open();
        var hold = await check.WorkItemWaits.AsNoTracking().Where(w => w.WorkItemId == responder).OrderByDescending(w => w.Id).FirstAsync(Ct);
        Assert.Equal((WaitStatus.Open, (DateTime?)Tomorrow), (hold.Status, hold.Until));
    }

    /// <summary>
    /// A responder whose run yielded, on a moment, is counted when its wait lets it go, and held if the day is spent by then;
    /// the hold, which has no attempt of its own, takes nothing from the yield: at midnight the next claim tells the run
    /// what its own wait ended with, and that wait names the run it resumed.
    /// </summary>
    [Fact]
    public async Task A_yielded_responder_held_at_the_limit_resumes_its_own_wait_at_midnight()
    {
        using var harness = Harness(limit: 2);
        var campaign = await CampaignAsync(harness);
        await LaunchedAsync(harness, campaign, Today.AddHours(1));
        var yielded = await QueuedAsync(harness, campaign);
        var run = await RunsAndYieldsAsync(harness, yielded, until: Noon.AddHours(1));
        harness.Clock.Now = new DateTimeOffset(Noon.AddHours(1));
        Assert.Equal(1, await harness.ResumeAsync(Ct));
        Assert.Empty(await harness.ClaimAsync(Ct));
        Assert.Equal(WorkItemStatus.Waiting, (await ItemAsync(harness, yielded)).Status);

        harness.Clock.Now = new DateTimeOffset(Tomorrow.AddHours(1));
        Assert.Equal(1, await harness.ResumeAsync(Ct));
        var work = Assert.Single(await harness.ClaimAsync(Ct));

        await AssertResumedFromTheYieldAsync(harness, yielded, run, work);
    }

    /// <summary>The same yielded responder, held, let go by a person instead: the run is still told what the yield's wait ended with.</summary>
    [Fact]
    public async Task A_yielded_responder_held_at_the_limit_and_resumed_by_a_person_resumes_its_own_wait()
    {
        using var harness = Harness(limit: 2);
        var campaign = await CampaignAsync(harness);
        await LaunchedAsync(harness, campaign, Today.AddHours(1));
        var yielded = await QueuedAsync(harness, campaign);
        var run = await RunsAndYieldsAsync(harness, yielded, until: Noon.AddHours(1));
        harness.Clock.Now = new DateTimeOffset(Noon.AddHours(1));
        Assert.Equal(1, await harness.ResumeAsync(Ct));
        Assert.Empty(await harness.ClaimAsync(Ct));

        await ResumeAsync(harness, yielded);
        var work = Assert.Single(await harness.ClaimAsync(Ct));

        await AssertResumedFromTheYieldAsync(harness, yielded, run, work);
    }

    /// <summary>
    /// A person's resume lets one hold go, not every later wait: a responder resumed past the limit, which runs and yields
    /// and is resumed by a person again, still carries the hold's error, and its next run is counted and held at the limit.
    /// </summary>
    [Fact]
    public async Task A_responder_resumed_past_the_limit_that_yields_and_is_resumed_again_is_counted_and_held_again()
    {
        using var harness = Harness(limit: 2);
        var campaign = await CampaignAsync(harness);
        await LaunchedAsync(harness, campaign, Today.AddHours(1), Today.AddHours(2));
        var held = await QueuedAsync(harness, campaign);
        Assert.Empty(await harness.ClaimAsync(Ct));
        await ResumeAsync(harness, held);
        Assert.Single(await harness.ClaimAsync(Ct));

        await RunsAndYieldsAsync(harness, held, until: Noon.AddHours(6));
        await ResumeAsync(harness, held);
        Assert.Empty(await harness.ClaimAsync(Ct));

        var item = await ItemAsync(harness, held);
        Assert.Equal(WorkItemStatus.Waiting, item.Status);
        Assert.Equal(AttemptErrors.ResponderLaunchesSpent, item.LastError!.Code);
        await using var db = harness.Open();
        var waits = await db.WorkItemWaits.AsNoTracking().Where(w => w.WorkItemId == held).OrderBy(w => w.Id).ToListAsync(Ct);
        Assert.Equal(3, waits.Count);
        Assert.Equal((WaitStatus.Open, (int?)null, (DateTime?)Tomorrow), (waits[2].Status, waits[2].AttemptId, waits[2].Until));
        Assert.Equal(2, await db.Journal.AsNoTracking().CountAsync(e => e.WorkItemId == item.PublicId && e.Kind == JournalKinds.WorkItemWaiting && e.Key == null, Ct));
    }

    /// <summary>
    /// A hold numbers no resumption: a fresh responder held at the limit and let go at midnight runs with nothing to be
    /// told, and when that run yields and its wait is released, the next run is told it is the first resumption, the
    /// hold's released wait, which has no attempt, counting for nothing.
    /// </summary>
    [Fact]
    public async Task A_hold_released_before_a_yield_is_not_counted_among_the_item_s_resumptions()
    {
        using var harness = Harness(limit: 2);
        var campaign = await CampaignAsync(harness);
        await LaunchedAsync(harness, campaign, Today.AddHours(1), Today.AddHours(2));
        var held = await QueuedAsync(harness, campaign);
        Assert.Empty(await harness.ClaimAsync(Ct));

        harness.Clock.Now = new DateTimeOffset(Tomorrow.AddHours(1));
        Assert.Equal(1, await harness.ResumeAsync(Ct));
        Assert.Null(Assert.Single(await harness.ClaimAsync(Ct)).Resumption);

        var run = await RunsAndYieldsAsync(harness, held, until: Tomorrow.AddHours(2));
        harness.Clock.Now = new DateTimeOffset(Tomorrow.AddHours(2));
        Assert.Equal(1, await harness.ResumeAsync(Ct));
        var resumption = Assert.IsType<Resumption>(Assert.Single(await harness.ClaimAsync(Ct)).Resumption);

        Assert.Equal((run, 1, WaitReleaseReason.MomentReached), (resumption.PreviousAttemptId, resumption.ResumptionNumber, resumption.WokeBecause));
    }

    /// <summary>The claimed run follows the yield's wait: told its attempt and its reason, numbered as its first resumption, and named on that wait.</summary>
    private static async Task AssertResumedFromTheYieldAsync(DispatchHarness harness, int itemId, string yieldedAttempt, ClaimedWork work)
    {
        Assert.Equal(await PublicIdAsync(harness, itemId), work.WorkItemPublicId);
        var resumption = Assert.IsType<Resumption>(work.Resumption);
        Assert.Equal((yieldedAttempt, 1, WaitReleaseReason.MomentReached), (resumption.PreviousAttemptId, resumption.ResumptionNumber, resumption.WokeBecause));
        Assert.Equal(new DateTimeOffset(Noon), resumption.WaitedSince);

        await using var db = harness.Open();
        var waits = await db.WorkItemWaits.AsNoTracking().Include(w => w.ResumedByAttempt).Where(w => w.WorkItemId == itemId).OrderBy(w => w.Id).ToListAsync(Ct);
        Assert.Equal(2, waits.Count);
        Assert.Equal(work.AttemptPublicId, waits[0].ResumedByAttempt?.PublicId);
        Assert.Equal(((int?)null, (Attempt?)null), (waits[1].AttemptId, waits[1].ResumedByAttempt));
    }

    /// <summary>
    /// The item's latest attempt starts now and its run yields on a moment, through <see cref="AttemptOutcomes.Wait"/>, as a
    /// responder's executor would end it; the attempt's id comes back.
    /// </summary>
    private static async Task<string> RunsAndYieldsAsync(DispatchHarness harness, int itemId, DateTime until)
    {
        var now = harness.Clock.Now.UtcDateTime;
        await using var scope = harness.Scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        var item = await db.WorkItems.Include(w => w.Attempts).SingleAsync(w => w.Id == itemId, Ct);
        var attempt = item.Attempts.OrderBy(a => a.Number).LastOrDefault();
        if (attempt is null)
        {
            attempt = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Running, now);
            item.AttemptCount = 1;
        }

        (attempt.Status, attempt.StartedAt, item.Status) = (AttemptStatus.Running, now, WorkItemStatus.Processing);
        await db.SaveChangesAsync(Ct);
        scope.ServiceProvider.GetRequiredService<AttemptOutcomes>().Wait(db, item, attempt, [], mode: null, until, Actors.ForAttempt(attempt));
        await db.SaveChangesAsync(Ct);
        return attempt.PublicId;
    }

    private static DispatchHarness Harness(int limit, DateTime? now = null)
    {
        var harness = new DispatchHarness(now ?? Noon);
        Limit(harness, limit);
        return harness;
    }

    private static void Limit(DispatchHarness harness, int limit) =>
        harness.Launches.CurrentValue = new LaunchesOptions { MaxPerDay = new(StringComparer.Ordinal) { ["responder"] = limit } };

    private static async Task<int> CampaignAsync(DispatchHarness harness)
    {
        await using var db = harness.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Today);
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(Ct);
        return campaign.Id;
    }

    /// <summary>One finished responder per moment, each with one attempt that started then.</summary>
    private static async Task LaunchedAsync(DispatchHarness harness, int campaignId, params DateTime[] started)
    {
        await using var db = harness.Open();
        var campaign = await db.Campaigns.SingleAsync(c => c.Id == campaignId, Ct);
        foreach (var moment in started)
        {
            var item = WorkItemFactory.NewAiRole(campaign, role: Responder.Role, now: moment);
            item.CreatedByType = ActorType.System;
            item.CreatedById = "dispatcher";
            Ended(item, WorkItemStatus.Succeeded, AttemptStatus.Succeeded, claimed: moment, started: moment);
            db.WorkItems.Add(item);
        }

        await db.SaveChangesAsync(Ct);
    }

    /// <summary>A responder ready to be claimed, as the respond step leaves one once its read has ended.</summary>
    private static async Task<int> QueuedAsync(DispatchHarness harness, int campaignId)
    {
        await using var db = harness.Open();
        var campaign = await db.Campaigns.SingleAsync(c => c.Id == campaignId, Ct);
        var item = WorkItemFactory.NewAiRole(campaign, role: Responder.Role, now: harness.Clock.Now.UtcDateTime, configure: w =>
        {
            w.CreatedByType = ActorType.System;
            w.CreatedById = "dispatcher";
            w.Priority = 50;
        });
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);
        return item.Id;
    }

    private static void Ended(WorkItem item, WorkItemStatus status, AttemptStatus attempt, DateTime claimed, DateTime? started)
    {
        var run = WorkItemFactory.NewAttempt(item, 1, attempt, claimed);
        run.StartedAt = started;
        run.FinishedAt = claimed.AddMinutes(5);
        item.Status = status;
        item.AttemptCount = 1;
        item.UpdatedAt = claimed.AddMinutes(5);
    }

    private static async Task ResumeAsync(DispatchHarness harness, int itemId)
    {
        await using var db = harness.Open();
        var publicId = await db.WorkItems.Where(w => w.Id == itemId).Select(w => w.PublicId).SingleAsync(Ct);
        await new WorkItemService(db, new JournalWriter(harness.Clock), harness.Clock, TestCanceller.New(harness.Clock), TestOptions.PluginSettings())
            .ResumeAsync(new WorkItemResumeRequest(publicId, Operator, "answer this one today"), Ct);
    }

    private static async Task<WorkItem> ItemAsync(DispatchHarness harness, int itemId)
    {
        await using var db = harness.Open();
        return await db.WorkItems.AsNoTracking().SingleAsync(w => w.Id == itemId, Ct);
    }

    private static async Task<string> PublicIdAsync(DispatchHarness harness, int itemId) => (await ItemAsync(harness, itemId)).PublicId;

    /// <summary>A plugin that reads campaigns, routed by default, so a provider operation the claim takes has a plan.</summary>
    private static void Routed(DispatchHarness harness)
    {
        var plugins = harness.Resolve<PluginRegistry>();
        plugins.Replace(
            new PluginSnapshot(PublicId.New(PluginProtocol.SnapshotIdPrefix), Noon, SnapshotSource.Startup, [TestPlugins.Loaded("stand-in-provider", ["campaign.get"])]),
            new ReloadReport(Noon, SnapshotSource.Startup, Activated: true, [], []));
        var routing = harness.Resolve<RouteRegistry>();
        routing.Replace(new RouteSnapshot(
            PublicId.New(RouteSnapshot.IdPrefix),
            Noon,
            plugins.Snapshot.Id,
            new RouteSet(new Route("stand-in-provider", null, null), RouteSet.Empty.Operations),
            RouteSnapshot.Empty(Noon, plugins.Snapshot.Id).Campaigns));
    }
}
