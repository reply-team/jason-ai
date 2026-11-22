using Jason.Contracts.Api;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Launches;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// The daily limits of launches count the installation's day, from one midnight to the next in its zone, and a person's
/// lift lets every role in every campaign past them for the rest of that day: what is held is let go at once, nothing new
/// is held until the next local midnight, and then the limits hold again with nothing deleted. The automatic-sends
/// ceiling keeps its UTC day (<c>ClaimerTests.Ceiling_KeepsTheUtcDayInTheInstallationsZone</c>).
/// </summary>
/// <remarks>
/// The zone of most rows is Asia/Tokyo: nine hours ahead of UTC, with no daylight saving since 1951, a fact of the world
/// that has stood for three generations, so the local midnight these rows name is not moved by any law in force. Nine hours
/// apart, its day and the UTC day disagree for most of every day. One row more names Europe/Berlin on two days of 2025
/// that are already history, to show a day of 23 and of 25 hours counted from midnight to midnight as it is.
/// </remarks>
public class LaunchLimitLiftTests
{
    /// <summary>01:00 on 7 October in Tokyo: the UTC day is still the 6th, and the installation's day began an hour ago.</summary>
    private static readonly DateTime Now = new(2026, 10, 6, 16, 0, 0, DateTimeKind.Utc);

    /// <summary>The installation's midnight of 7 October in Tokyo, as a UTC instant.</summary>
    private static readonly DateTime LocalMidnight = new(2026, 10, 6, 15, 0, 0, DateTimeKind.Utc);

    /// <summary>The installation's next midnight, of 8 October in Tokyo.</summary>
    private static readonly DateTime NextLocalMidnight = new(2026, 10, 7, 15, 0, 0, DateTimeKind.Utc);

    private const string Tokyo = "Asia/Tokyo";

    private static readonly ActorRef Operator = new(ActorType.Human, "operator");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Held items in two campaigns and of two roles are let go by one lift: each wait released as a person's resume under
    /// the person's name and reason, each item back to <c>created</c>, and both claimed by the next scan though both days
    /// are still spent.
    /// </summary>
    [Fact]
    public async Task Held_items_in_two_campaigns_and_two_roles_are_released_at_once_and_then_run()
    {
        using var harness = Harness(("planner", 1), ("responder", 1));
        var first = await CampaignAsync(harness);
        var second = await CampaignAsync(harness);
        await LaunchedAsync(harness, first, "planner", LocalMidnight.AddMinutes(10));
        await LaunchedAsync(harness, second, "responder", LocalMidnight.AddMinutes(20));
        var planner = await QueuedAsync(harness, first, "planner");
        var responder = await QueuedAsync(harness, second, "responder");
        Assert.Empty(await harness.ClaimAsync(Ct));
        Assert.Equal(WorkItemStatus.Waiting, (await ItemAsync(harness, planner)).Status);
        Assert.Equal(WorkItemStatus.Waiting, (await ItemAsync(harness, responder)).Status);

        var lift = await LiftAsync(harness);

        Assert.Equal(new LaunchesLiftDto(new DateTimeOffset(NextLocalMidnight), Tokyo, 2, AlreadyLifted: false), lift);
        await using (var db = harness.Open())
        {
            foreach (var id in new[] { planner, responder })
            {
                var item = await db.WorkItems.AsNoTracking().SingleAsync(w => w.Id == id, Ct);
                Assert.Equal(WorkItemStatus.Created, item.Status);
                var wait = await db.WorkItemWaits.AsNoTracking().SingleAsync(w => w.WorkItemId == id, Ct);
                Assert.Equal((WaitStatus.Released, (WaitReleaseReason?)WaitReleaseReason.ResumedByActor, (DateTime?)Now), (wait.Status, wait.ReleaseReason, wait.ReleasedAt));
                var resumed = await db.Journal.AsNoTracking().SingleAsync(e => e.WorkItemId == item.PublicId && e.Kind == JournalKinds.WorkItemResumed, Ct);
                Assert.Equal((ActorType.Human, "operator", "a launch day"), (resumed.ActorType, resumed.ActorId, resumed.Reason));
                Assert.Equal("""{"because":"resumed_by_actor"}""", resumed.New!.ToJsonString());
            }
        }

        var claimed = await harness.ClaimAsync(Ct);

        Assert.Equal(
            new[] { await PublicIdAsync(harness, planner), await PublicIdAsync(harness, responder) }.Order(StringComparer.Ordinal),
            claimed.Select(work => work.WorkItemPublicId).Order(StringComparer.Ordinal));
    }

    /// <summary>A later item of a role whose day is spent, queued after the lift the same local day, is not held: it runs.</summary>
    [Fact]
    public async Task A_later_item_of_a_spent_role_the_same_local_day_is_not_held()
    {
        using var harness = Harness(("planner", 1));
        var campaign = await CampaignAsync(harness);
        await LaunchedAsync(harness, campaign, "planner", LocalMidnight.AddMinutes(10));
        Assert.Equal(0, (await LiftAsync(harness)).Released);

        harness.Clock.Now = new DateTimeOffset(NextLocalMidnight.AddMinutes(-1));
        var later = await QueuedAsync(harness, campaign, "planner");
        var work = Assert.Single(await harness.ClaimAsync(Ct));

        Assert.Equal(await PublicIdAsync(harness, later), work.WorkItemPublicId);
        Assert.Equal(0, await WaitingLinesAsync(harness));
    }

    /// <summary>
    /// At the next local midnight the lift no longer covers the day the claim counts: a day spent again holds the next item,
    /// until the midnight after, and the lift's row is still there.
    /// </summary>
    [Fact]
    public async Task The_next_local_day_the_limit_holds_again_and_nothing_is_deleted()
    {
        using var harness = Harness(("planner", 1));
        var campaign = await CampaignAsync(harness);
        await LaunchedAsync(harness, campaign, "planner", LocalMidnight.AddMinutes(10));
        await LiftAsync(harness);

        harness.Clock.Now = new DateTimeOffset(NextLocalMidnight.AddMinutes(30));
        await LaunchedAsync(harness, campaign, "planner", NextLocalMidnight.AddMinutes(5));
        var held = await QueuedAsync(harness, campaign, "planner");
        Assert.Empty(await harness.ClaimAsync(Ct));

        var item = await ItemAsync(harness, held);
        Assert.Equal(WorkItemStatus.Waiting, item.Status);
        Assert.Equal(AttemptErrors.LaunchesSpent, item.LastError!.Code);
        await using var db = harness.Open();
        var wait = await db.WorkItemWaits.AsNoTracking().SingleAsync(w => w.WorkItemId == held, Ct);
        Assert.Equal((WaitStatus.Open, (DateTime?)NextLocalMidnight.AddDays(1)), (wait.Status, wait.Until));
        var row = await db.LaunchLimitLifts.AsNoTracking().SingleAsync(Ct);
        Assert.Equal((new DateOnly(2026, 10, 7), Tokyo), (row.LocalDate, row.TimeZone));
    }

    /// <summary>
    /// A lift is a person's word: a role and an attempt are refused with <c>lift_not_human</c>, and a request that names no
    /// person with an id with <c>actor_required</c>; the system is no caller's to claim at all. Nothing is let go, written or said.
    /// </summary>
    [Theory]
    [InlineData(ActorType.Role, "planner", "lift_not_human")]
    [InlineData(ActorType.Attempt, "att_01K7QN0000000000000000000R", "lift_not_human")]
    [InlineData(ActorType.Human, null, "actor_required")]
    [InlineData(ActorType.Human, "  ", "actor_required")]
    public async Task Anybody_but_a_named_person_is_refused_and_nothing_moves(ActorType type, string? id, string code)
    {
        using var harness = Harness(("planner", 1));
        var campaign = await CampaignAsync(harness);
        await LaunchedAsync(harness, campaign, "planner", LocalMidnight.AddMinutes(10));
        var held = await QueuedAsync(harness, campaign, "planner");
        Assert.Empty(await harness.ClaimAsync(Ct));

        var refused = await Assert.ThrowsAsync<InvalidRequestException>(() => LiftAsync(harness, new ActorRef(type, id)));

        Assert.Equal(code, refused.Code);
        Assert.Equal(WorkItemStatus.Waiting, (await ItemAsync(harness, held)).Status);
        await using var db = harness.Open();
        Assert.Empty(await db.LaunchLimitLifts.AsNoTracking().ToListAsync(Ct));
        Assert.Equal(0, await db.Journal.AsNoTracking().CountAsync(e => e.Kind == JournalKinds.LaunchLimitLifted || e.Kind == JournalKinds.WorkItemResumed, Ct));
    }

    /// <summary>No actor at all is no named person either: the anonymous human a create defaults to does not lift.</summary>
    [Fact]
    public async Task No_actor_is_refused_as_no_named_person()
    {
        using var harness = Harness(("planner", 1));

        var refused = await Assert.ThrowsAsync<InvalidRequestException>(() => LiftAsync(harness, actor: null));

        Assert.Equal("actor_required", refused.Code);
    }

    /// <summary>
    /// A second lift the same local day answers what stands — the same midnight and zone, <c>already_lifted</c> — and writes
    /// no row and no line; a hold that appeared since is still let go, under the person's name.
    /// </summary>
    [Fact]
    public async Task A_second_lift_the_same_day_writes_nothing_and_still_lets_go_a_hold_that_appeared_since()
    {
        using var harness = Harness(("planner", 1));
        var campaign = await CampaignAsync(harness);
        await LiftAsync(harness);

        harness.Clock.Advance(TimeSpan.FromHours(5));
        var since = await HeldByHandAsync(harness, campaign, "planner");
        var again = await LiftAsync(harness, Operator, reason: "once more");

        Assert.Equal(new LaunchesLiftDto(new DateTimeOffset(NextLocalMidnight), Tokyo, 1, AlreadyLifted: true), again);
        Assert.Equal(WorkItemStatus.Created, (await ItemAsync(harness, since)).Status);
        await using var db = harness.Open();
        Assert.Single(await db.LaunchLimitLifts.AsNoTracking().ToListAsync(Ct));
        Assert.Single(await db.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.LaunchLimitLifted).ToListAsync(Ct));
        var resumed = await db.Journal.AsNoTracking().SingleAsync(e => e.Kind == JournalKinds.WorkItemResumed, Ct);
        Assert.Equal(("operator", "once more"), (resumed.ActorId, resumed.Reason));
    }

    /// <summary>
    /// The one line a lift writes: keyed by the local date it covers, naming no campaign, with the next local midnight as a
    /// UTC instant, the zone and the count let go, under the person and their reason; and the row beside it says the same.
    /// </summary>
    [Fact]
    public async Task The_line_carries_the_date_the_midnight_the_zone_the_count_the_person_and_the_reason()
    {
        using var harness = Harness(("planner", 1));
        var campaign = await CampaignAsync(harness);
        await LaunchedAsync(harness, campaign, "planner", LocalMidnight.AddMinutes(10));
        await QueuedAsync(harness, campaign, "planner");
        Assert.Empty(await harness.ClaimAsync(Ct));

        await LiftAsync(harness);

        await using var db = harness.Open();
        var line = await db.Journal.AsNoTracking().SingleAsync(e => e.Kind == JournalKinds.LaunchLimitLifted, Ct);
        Assert.Equal("2026-10-07", line.Key);
        Assert.Null(line.Old);
        Assert.Equal("""{"until":"2026-10-07T15:00:00.000Z","time_zone":"Asia/Tokyo","released":1}""", line.New!.ToJsonString());
        Assert.Equal((ActorType.Human, "operator", "a launch day"), (line.ActorType, line.ActorId, line.Reason));
        Assert.Null(line.CampaignId);
        Assert.Null(line.WorkItemId);
        var row = await db.LaunchLimitLifts.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(
            (new DateOnly(2026, 10, 7), Tokyo, ActorType.Human, (string?)"operator", (string?)"a launch day", Now),
            (row.LocalDate, row.TimeZone, row.ActorType, row.ActorId, row.Reason, row.CreatedAt));
    }

    /// <summary>
    /// The count's day follows <c>Runtime:TimeZone</c>, whatever zone the system keeps: two runs started late on 6 October in
    /// UTC are yesterday's in Tokyo, so the next is not held; two started after Tokyo's midnight hold it until the next
    /// one, and the hold says the installation's midnight, its zone, and the lift beside the resume.
    /// </summary>
    [Fact]
    public async Task The_count_s_day_follows_the_installation_s_zone_setting()
    {
        using var harness = new DispatchHarness(Now);
        harness.Clock.Zone = TimeZoneInfo.Utc;
        harness.Runtime.CurrentValue = new RuntimeOptions { TimeZone = Tokyo };
        Limits(harness, ("planner", 2));
        await AssertTheDayIsTokyosAsync(harness);
    }

    /// <summary>With the setting absent, the count's day follows the zone the system keeps, as the runtime's clock reads it.</summary>
    [Fact]
    public async Task With_the_setting_absent_the_count_s_day_follows_the_clock_s_zone()
    {
        using var harness = new DispatchHarness(Now);
        harness.Clock.Zone = TimeZoneInfo.FindSystemTimeZoneById(Tokyo);
        Limits(harness, ("planner", 2));
        await AssertTheDayIsTokyosAsync(harness);
    }

    /// <summary>
    /// A day daylight saving changes is counted from midnight to midnight as it is: in Berlin, 30 March 2025 lasted 23 hours
    /// and 26 October 2025 lasted 25. A run a minute after the local midnight counts, one a minute before does not, and the
    /// hold waits until the next local midnight, the length of the day apart.
    /// </summary>
    [Theory]
    [InlineData("2025-03-29T23:00:00Z", "2025-03-30T22:00:00Z", 23)]
    [InlineData("2025-10-25T22:00:00Z", "2025-10-26T23:00:00Z", 25)]
    public async Task A_day_daylight_saving_shortens_or_lengthens_is_counted_midnight_to_midnight(string midnight, string next, int hours)
    {
        var start = DateTime.Parse(midnight, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        var end = DateTime.Parse(next, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        Assert.Equal(TimeSpan.FromHours(hours), end - start);
        using var harness = new DispatchHarness(end.AddMinutes(-30));
        harness.Runtime.CurrentValue = new RuntimeOptions { TimeZone = "Europe/Berlin" };
        Limits(harness, ("planner", 1));
        var campaign = await CampaignAsync(harness);
        await LaunchedAsync(harness, campaign, "planner", start.AddMinutes(-1));
        var first = await QueuedAsync(harness, campaign, "planner");
        Assert.Equal(await PublicIdAsync(harness, first), Assert.Single(await harness.ClaimAsync(Ct)).WorkItemPublicId);

        await EndedAsync(harness, first, start.AddMinutes(1));
        var held = await QueuedAsync(harness, campaign, "planner");
        Assert.Empty(await harness.ClaimAsync(Ct));

        var item = await ItemAsync(harness, held);
        Assert.Contains($"since the installation's midnight ({midnight}, Europe/Berlin); this item waits until {next}.", item.LastError!.Message, StringComparison.Ordinal);
        await using var db = harness.Open();
        var wait = await db.WorkItemWaits.AsNoTracking().SingleAsync(w => w.WorkItemId == held, Ct);
        Assert.Equal((DateTime?)end, wait.Until);
    }

    /// <summary>
    /// A lift covers its day in the zone it was taken in: once the installation's zone is moved, the same moment is another
    /// day, which the lift does not cover, and a spent day holds again.
    /// </summary>
    [Fact]
    public async Task A_lift_covers_nothing_once_the_installation_s_zone_moves()
    {
        using var harness = Harness(("planner", 1));
        var campaign = await CampaignAsync(harness);
        await LaunchedAsync(harness, campaign, "planner", LocalMidnight.AddMinutes(10));
        await LiftAsync(harness);

        harness.Runtime.CurrentValue = new RuntimeOptions { TimeZone = "UTC" };
        var held = await QueuedAsync(harness, campaign, "planner");
        Assert.Empty(await harness.ClaimAsync(Ct));

        Assert.Equal(WorkItemStatus.Waiting, (await ItemAsync(harness, held)).Status);
    }

    /// <summary>The Tokyo day of <see cref="Now"/>, seen by the claim: yesterday's runs spend nothing, today's hold, with the hold's words.</summary>
    private static async Task AssertTheDayIsTokyosAsync(DispatchHarness harness)
    {
        var campaign = await CampaignAsync(harness);
        await LaunchedAsync(harness, campaign, "planner", LocalMidnight.AddHours(-3), LocalMidnight.AddMinutes(-1));
        var runs = await QueuedAsync(harness, campaign, "planner");
        Assert.Equal(await PublicIdAsync(harness, runs), Assert.Single(await harness.ClaimAsync(Ct)).WorkItemPublicId);

        await EndedAsync(harness, runs, LocalMidnight.AddMinutes(1));
        await LaunchedAsync(harness, campaign, "planner", LocalMidnight.AddMinutes(2));
        var held = await QueuedAsync(harness, campaign, "planner");
        Assert.Empty(await harness.ClaimAsync(Ct));

        var item = await ItemAsync(harness, held);
        Assert.Equal(WorkItemStatus.Waiting, item.Status);
        Assert.Equal(
            "Work of the planner role in this campaign has reached the limit Launches:MaxPerDay:planner sets, 2 runs started since the "
            + "installation's midnight (2026-10-06T15:00:00Z, Asia/Tokyo); this item waits until 2026-10-07T15:00:00Z. A person's "
            + "workitem.resume lets it go sooner, past the limit, jason launches lift lets every role in every campaign past it until "
            + "the installation's next midnight, and a raised limit applies from the next scan to work not yet held, while this item "
            + "keeps its moment.",
            item.LastError!.Message);
        await using var db = harness.Open();
        var wait = await db.WorkItemWaits.AsNoTracking().SingleAsync(w => w.WorkItemId == held, Ct);
        Assert.Equal((DateTime?)NextLocalMidnight, wait.Until);
    }

    /// <summary>A harness in Tokyo's zone, by the clock, with the limits given.</summary>
    private static DispatchHarness Harness(params (string Role, int Limit)[] limits)
    {
        var harness = new DispatchHarness(Now);
        harness.Clock.Zone = TimeZoneInfo.FindSystemTimeZoneById(Tokyo);
        Limits(harness, limits);
        return harness;
    }

    private static void Limits(DispatchHarness harness, params (string Role, int Limit)[] limits) =>
        harness.Launches.CurrentValue = new LaunchesOptions { MaxPerDay = limits.ToDictionary(l => l.Role, l => l.Limit, StringComparer.Ordinal) };

    private static Task<LaunchesLiftDto> LiftAsync(DispatchHarness harness) => LiftAsync(harness, Operator, "a launch day");

    private static async Task<LaunchesLiftDto> LiftAsync(DispatchHarness harness, ActorRef? actor, string? reason = null)
    {
        await using var scope = harness.Scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        return await new LaunchesService(db, new JournalWriter(harness.Clock), harness.Clock, harness.Resolve<InstallationDay>())
            .LiftAsync(new LaunchesLiftRequest(actor, reason), Ct);
    }

    private static async Task<int> CampaignAsync(DispatchHarness harness)
    {
        await using var db = harness.Open();
        var campaign = WorkItemFactory.NewCampaign(now: LocalMidnight.AddDays(-1));
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(Ct);
        return campaign.Id;
    }

    /// <summary>One finished item of the role per moment, each with one attempt that started then.</summary>
    private static async Task LaunchedAsync(DispatchHarness harness, int campaignId, string role, params DateTime[] started)
    {
        await using var db = harness.Open();
        var campaign = await db.Campaigns.SingleAsync(c => c.Id == campaignId, Ct);
        foreach (var moment in started)
        {
            var item = WorkItemFactory.NewAiRole(campaign, role: role, now: moment);
            var run = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Succeeded, moment);
            run.StartedAt = moment;
            run.FinishedAt = moment.AddMinutes(5);
            item.Status = WorkItemStatus.Succeeded;
            item.AttemptCount = 1;
            item.UpdatedAt = moment.AddMinutes(5);
            db.WorkItems.Add(item);
        }

        await db.SaveChangesAsync(Ct);
    }

    /// <summary>The claimed item's attempt started at <paramref name="started"/> and ended, so it counts and its campaign is free again.</summary>
    private static async Task EndedAsync(DispatchHarness harness, int itemId, DateTime started)
    {
        await using var db = harness.Open();
        var item = await db.WorkItems.Include(w => w.Attempts).SingleAsync(w => w.Id == itemId, Ct);
        var attempt = item.Attempts.Single();
        (attempt.Status, attempt.StartedAt, attempt.FinishedAt) = (AttemptStatus.Succeeded, started, started.AddMinutes(1));
        item.Status = WorkItemStatus.Succeeded;
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>An item of the role, ready to be claimed, as the dispatcher leaves one.</summary>
    private static async Task<int> QueuedAsync(DispatchHarness harness, int campaignId, string role)
    {
        await using var db = harness.Open();
        var campaign = await db.Campaigns.SingleAsync(c => c.Id == campaignId, Ct);
        var item = WorkItemFactory.NewAiRole(campaign, role: role, now: harness.Clock.Now.UtcDateTime, configure: w =>
        {
            w.CreatedByType = ActorType.System;
            w.CreatedById = "dispatcher";
        });
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);
        return item.Id;
    }

    /// <summary>
    /// An item held at its role's limit by the one routine that holds, written after a lift as a scan of another runtime that
    /// read the day before the lift was committed could have left it.
    /// </summary>
    private static async Task<int> HeldByHandAsync(DispatchHarness harness, int campaignId, string role)
    {
        await using var scope = harness.Scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        var campaign = await db.Campaigns.SingleAsync(c => c.Id == campaignId, Ct);
        var item = WorkItemFactory.NewAiRole(campaign, role: role, now: harness.Clock.Now.UtcDateTime);
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);
        var day = harness.Resolve<InstallationDay>().Of(harness.Clock.Now.UtcDateTime);
        scope.ServiceProvider.GetRequiredService<AttemptOutcomes>()
            .Hold(db, item, day.End, Claimer.LaunchesSpent(role, 1, day), 1, Actors.Dispatcher);
        await db.SaveChangesAsync(Ct);
        return item.Id;
    }

    private static async Task<int> WaitingLinesAsync(DispatchHarness harness)
    {
        await using var db = harness.Open();
        return await db.Journal.AsNoTracking().CountAsync(e => e.Kind == JournalKinds.WorkItemWaiting, Ct);
    }

    private static async Task<WorkItem> ItemAsync(DispatchHarness harness, int itemId)
    {
        await using var db = harness.Open();
        return await db.WorkItems.AsNoTracking().SingleAsync(w => w.Id == itemId, Ct);
    }

    private static async Task<string> PublicIdAsync(DispatchHarness harness, int itemId) => (await ItemAsync(harness, itemId)).PublicId;
}
