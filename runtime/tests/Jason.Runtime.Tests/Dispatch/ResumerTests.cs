using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// The resume step: a waiting item goes back to the queue once the items it named have ended, all of them or any one,
/// or its deadline has come — read from rows at every scan, never from an event, so a release cannot be missed.
/// </summary>
public class ResumerTests
{
    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// A wait on all its items is released by the last of them to end, and only then: the item is ordinary created work
    /// again, its yielded attempt cost it nothing, the wait is closed with its reason, and one line says why.
    /// </summary>
    [Fact]
    public async Task A_wait_on_all_is_released_once_the_last_named_item_ends()
    {
        using var harness = new DispatchHarness(Noon);
        var seeded = await SeedWaitAsync(harness, [WorkItemStatus.Succeeded, WorkItemStatus.Processing], WaitMode.All, until: null);

        Assert.Equal(0, await harness.ResumeAsync(Ct));
        Assert.Equal(WorkItemStatus.Waiting, (await harness.ReadItemAsync(seeded.Parent, Ct)).Status);

        harness.Clock.Now = harness.Clock.Now.AddMinutes(5);
        await SetStatusAsync(harness, seeded.Children[1], WorkItemStatus.Failed);
        Assert.Equal(1, await harness.ResumeAsync(Ct));

        var parent = await harness.ReadItemAsync(seeded.Parent, Ct);
        Assert.Equal(WorkItemStatus.Created, parent.Status);
        Assert.Equal(0, parent.AttemptCount);
        Assert.Null(parent.FinishedAt);
        Assert.Equal(AttemptStatus.Yielded, Assert.Single(parent.Attempts).Status);

        var wait = await WaitOfAsync(harness, seeded.Parent);
        Assert.Equal(WaitStatus.Released, wait.Status);
        Assert.Equal(Noon.AddMinutes(5), wait.ReleasedAt);
        Assert.Equal(WaitReleaseReason.ChildrenEnded, wait.ReleaseReason);
        Assert.Null(wait.ResumedByAttemptId);

        var line = (await harness.ReadJournalAsync(seeded.Parent, Ct))[^1];
        Assert.Equal(JournalKinds.WorkItemResumed, line.Kind);
        Assert.Equal(seeded.Attempt, line.Key);
        Assert.Equal(seeded.Attempt, line.AttemptId);
        Assert.Equal(ActorType.System, line.ActorType);
        Assert.Equal("""{"because":"children_ended"}""", line.New!.ToJsonString());
        Assert.Null(line.Old);

        // Closed once: the next scan finds nothing to release.
        Assert.Equal(0, await harness.ResumeAsync(Ct));
        Assert.Single(await harness.ReadJournalAsync(seeded.Parent, Ct), e => e.Kind == JournalKinds.WorkItemResumed);
    }

    /// <summary>A wait on any of its items is released by the first of them to end, whatever the others are doing.</summary>
    [Fact]
    public async Task A_wait_on_any_is_released_by_the_first_named_item_to_end()
    {
        using var harness = new DispatchHarness(Noon);
        var seeded = await SeedWaitAsync(harness, [WorkItemStatus.Created, WorkItemStatus.Processing], WaitMode.Any, until: null);

        Assert.Equal(0, await harness.ResumeAsync(Ct));

        await SetStatusAsync(harness, seeded.Children[0], WorkItemStatus.Cancelled);
        Assert.Equal(1, await harness.ResumeAsync(Ct));

        Assert.Equal(WorkItemStatus.Created, (await harness.ReadItemAsync(seeded.Parent, Ct)).Status);
        Assert.Equal(WaitReleaseReason.ChildrenEnded, (await WaitOfAsync(harness, seeded.Parent)).ReleaseReason);
        Assert.Equal(WorkItemStatus.Processing, (await harness.ReadItemAsync(seeded.Children[1], Ct)).Status);
    }

    /// <summary>
    /// Ended means finished: succeeded, failed, cancelled or expired. A failed or a cancelled item has ended — the waiter
    /// is told and decides what that means — and an item being retried is <c>created</c> again, so it has not; neither has
    /// anything unclaimed, claimed, parked or waiting itself.
    /// </summary>
    [Theory]
    [InlineData(WorkItemStatus.Succeeded, true)]
    [InlineData(WorkItemStatus.Failed, true)]
    [InlineData(WorkItemStatus.Cancelled, true)]
    [InlineData(WorkItemStatus.Expired, true)]
    [InlineData(WorkItemStatus.Created, false)]
    [InlineData(WorkItemStatus.Scheduled, false)]
    [InlineData(WorkItemStatus.Processing, false)]
    [InlineData(WorkItemStatus.AwaitingApproval, false)]
    [InlineData(WorkItemStatus.Waiting, false)]
    public async Task Only_a_finished_item_has_ended(WorkItemStatus child, bool ended)
    {
        using var harness = new DispatchHarness(Noon);
        var seeded = await SeedWaitAsync(harness, [WorkItemStatus.Processing], WaitMode.All, until: null);
        await SetStatusAsync(harness, seeded.Children[0], child);

        var resumed = await harness.ResumeAsync(Ct);

        Assert.True(resumed == (ended ? 1 : 0), $"A wait on one {child} item released {resumed}.");
        Assert.Equal(ended ? WorkItemStatus.Created : WorkItemStatus.Waiting, (await harness.ReadItemAsync(seeded.Parent, Ct)).Status);
    }

    /// <summary>A wait for a moment alone is released when the clock reaches it, and not a second before.</summary>
    [Fact]
    public async Task A_wait_on_a_moment_alone_is_released_when_the_moment_comes()
    {
        using var harness = new DispatchHarness(Noon);
        var seeded = await SeedWaitAsync(harness, [], mode: null, until: Noon.AddHours(1));

        harness.Clock.Now = harness.Clock.Now.AddHours(1).AddSeconds(-1);
        Assert.Equal(0, await harness.ResumeAsync(Ct));

        harness.Clock.Now = harness.Clock.Now.AddSeconds(1);
        Assert.Equal(1, await harness.ResumeAsync(Ct));

        var wait = await WaitOfAsync(harness, seeded.Parent);
        Assert.Equal(WaitReleaseReason.MomentReached, wait.ReleaseReason);
        Assert.Equal(Noon.AddHours(1), wait.ReleasedAt);
        Assert.Equal(
            """{"because":"moment_reached"}""",
            (await harness.ReadJournalAsync(seeded.Parent, Ct))[^1].New!.ToJsonString());
    }

    /// <summary>
    /// A wait for items and a moment ends at whichever comes first, and says which. Both at once — the items ended and the
    /// moment came by the same scan — is the items: that is what the waiter was waiting for.
    /// </summary>
    [Theory]
    [InlineData(false, 30, WaitReleaseReason.ChildrenEnded)]
    [InlineData(true, 30, null)]
    [InlineData(true, 60, WaitReleaseReason.MomentReached)]
    [InlineData(false, 60, WaitReleaseReason.ChildrenEnded)]
    public async Task A_wait_on_items_and_a_moment_ends_at_whichever_comes_first(bool childStillRunning, int minutesLater, WaitReleaseReason? expected)
    {
        using var harness = new DispatchHarness(Noon);
        var seeded = await SeedWaitAsync(harness, [WorkItemStatus.Processing], WaitMode.All, until: Noon.AddHours(1));
        if (!childStillRunning)
        {
            await SetStatusAsync(harness, seeded.Children[0], WorkItemStatus.Succeeded);
        }

        harness.Clock.Now = harness.Clock.Now.AddMinutes(minutesLater);
        var resumed = await harness.ResumeAsync(Ct);

        var wait = await WaitOfAsync(harness, seeded.Parent);
        Assert.True(resumed == (expected is null ? 0 : 1), $"Released {resumed} with the child running {childStillRunning} at +{minutesLater} min.");
        Assert.Equal(expected, wait.ReleaseReason);
    }

    /// <summary>
    /// A wait for items alone is bounded by the ceiling counted from when it opened: past it the item is released though
    /// nothing it named has ended, and the line says it was the ceiling rather than a moment it asked for.
    /// </summary>
    [Fact]
    public async Task A_wait_on_items_alone_is_released_by_the_ceiling()
    {
        using var harness = new DispatchHarness(Noon, o => o.AiRole.MaxWaitSeconds = 3_600);
        var seeded = await SeedWaitAsync(harness, [WorkItemStatus.Processing], WaitMode.Any, until: null);
        Assert.Equal(Noon.AddHours(1), (await WaitOfAsync(harness, seeded.Parent)).Deadline);

        harness.Clock.Now = harness.Clock.Now.AddMinutes(59);
        Assert.Equal(0, await harness.ResumeAsync(Ct));
        harness.Clock.Now = harness.Clock.Now.AddMinutes(1);
        Assert.Equal(1, await harness.ResumeAsync(Ct));

        Assert.Equal(WaitReleaseReason.WaitCeiling, (await WaitOfAsync(harness, seeded.Parent)).ReleaseReason);
        Assert.Equal("""{"because":"wait_ceiling"}""", (await harness.ReadJournalAsync(seeded.Parent, Ct))[^1].New!.ToJsonString());
        Assert.Equal(WorkItemStatus.Processing, (await harness.ReadItemAsync(seeded.Children[0], Ct)).Status);
    }

    /// <summary>
    /// The release reads no campaign: an item of a paused campaign is released when its wait is over, and then waits for
    /// its campaign like any created item — it is claimed once the campaign is active again, and not before.
    /// </summary>
    [Fact]
    public async Task A_waiting_item_of_a_paused_campaign_is_released_and_then_waits_for_its_campaign()
    {
        var command = FakeCommand.Returning(new CommandOutcome.Completed(null));
        using var harness = new DispatchHarness(Noon, o => o.RetryDelaySeconds = 0, commands: command);
        var seeded = await SeedWaitAsync(harness, [WorkItemStatus.Succeeded], WaitMode.All, until: null, CampaignStatus.Paused);

        var first = await harness.Runner.ScanOnceAsync(Ct);
        Assert.Equal(1, first.Resumed);
        Assert.Equal(0, first.Claimed);
        Assert.Equal(WorkItemStatus.Created, (await harness.ReadItemAsync(seeded.Parent, Ct)).Status);

        await using (var db = harness.Open())
        {
            await db.Campaigns.Where(c => c.PublicId == seeded.Campaign).ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, CampaignStatus.Active), Ct);
        }

        var second = await harness.Runner.ScanOnceAsync(Ct);
        Assert.Equal(0, second.Resumed);
        Assert.Equal(1, second.Claimed);
        Assert.True(await DispatchHarness.EventuallyAsync(() => command.Contexts.Count == 1, Ct));
        Assert.True(await harness.Pool.DrainAsync(TimeSpan.FromSeconds(5)));
    }

    /// <summary>
    /// A waiting item keeps no campaign busy: the claim's one-at-a-time rule counts claimed and running work, and a waiter
    /// is neither, so the very items it waits for are claimed in its campaign while it waits.
    /// </summary>
    [Fact]
    public async Task A_waiting_item_leaves_its_campaign_free_for_the_items_it_waits_for()
    {
        var gate = new TaskCompletionSource<CommandOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new FakeCommand(WorkItemKind.AiRole, _ => gate.Task);
        using var harness = new DispatchHarness(Noon, commands: command);
        var seeded = await SeedWaitAsync(harness, [WorkItemStatus.Created], WaitMode.All, until: null);

        var report = await harness.Runner.ScanOnceAsync(Ct);

        Assert.Equal(1, report.Claimed);
        Assert.Equal(0, report.Resumed);
        Assert.True(await DispatchHarness.EventuallyAsync(() => command.Contexts.Count == 1, Ct));
        Assert.Equal(seeded.Children[0], Assert.Single(command.Contexts).WorkItemId);
        Assert.Equal(WorkItemStatus.Waiting, (await harness.ReadItemAsync(seeded.Parent, Ct)).Status);
        gate.SetResult(new CommandOutcome.Completed(null));
        Assert.True(await harness.Pool.DrainAsync(TimeSpan.FromSeconds(5)));
    }

    /// <summary>
    /// The release step wins no race it should lose: an item cancelled between the step's read and its write stays
    /// cancelled, its wait is not released, and the step says it released nothing.
    /// </summary>
    [Fact]
    public async Task An_item_cancelled_while_its_release_is_being_written_stays_cancelled()
    {
        using var harness = new DispatchHarness(Noon);
        var seeded = await SeedWaitAsync(harness, [WorkItemStatus.Succeeded], WaitMode.All, until: null);
        harness.InterfereOnceBeforeSaving(() =>
        {
            using var db = harness.Open();
            db.WorkItems.Where(w => w.PublicId == seeded.Parent)
                .ExecuteUpdate(s => s.SetProperty(w => w.Status, WorkItemStatus.Cancelled));
        });

        Assert.Equal(0, await harness.ResumeAsync(Ct));

        Assert.Equal(WorkItemStatus.Cancelled, (await harness.ReadItemAsync(seeded.Parent, Ct)).Status);
        Assert.DoesNotContain(await harness.ReadJournalAsync(seeded.Parent, Ct), e => e.Kind == JournalKinds.WorkItemResumed);
    }

    /// <summary>
    /// Nested waits: a waiting item has not ended, so a parent waiting on it keeps waiting while the child is released
    /// and runs again; and the parent's own deadline bounds it whatever its child does.
    /// </summary>
    [Fact]
    [Critical(Family.W4, "nested waits")]
    public async Task Nested_waits_a_waiting_child_has_not_ended_and_the_parents_deadline_bounds_it()
    {
        using var harness = new DispatchHarness(Noon, o => o.AiRole.MaxWaitSeconds = 7_200);
        var parent = await SeedWaitAsync(harness, [WorkItemStatus.Processing], WaitMode.All, until: null);
        var child = parent.Children[0];
        var grandchild = await SeedWaitOnAsync(harness, child, WaitMode.All, until: Noon.AddHours(1));

        Assert.Equal(0, await harness.ResumeAsync(Ct));
        Assert.Equal(WorkItemStatus.Waiting, (await harness.ReadItemAsync(child, Ct)).Status);

        // The grandchild ends: the child is released — it is created work again, not ended — and the parent waits on.
        harness.Clock.Now = harness.Clock.Now.AddMinutes(10);
        await SetStatusAsync(harness, grandchild, WorkItemStatus.Succeeded);
        Assert.Equal(1, await harness.ResumeAsync(Ct));
        Assert.Equal(WorkItemStatus.Created, (await harness.ReadItemAsync(child, Ct)).Status);
        Assert.Equal(WorkItemStatus.Waiting, (await harness.ReadItemAsync(parent.Parent, Ct)).Status);

        // Two hours after it opened, the parent's ceiling releases it while its child has still not ended.
        harness.Clock.Now = Noon.AddHours(2);
        Assert.Equal(1, await harness.ResumeAsync(Ct));
        Assert.Equal(WorkItemStatus.Created, (await harness.ReadItemAsync(parent.Parent, Ct)).Status);
        Assert.Equal(WaitReleaseReason.WaitCeiling, (await WaitOfAsync(harness, parent.Parent)).ReleaseReason);
        Assert.Equal(WorkItemStatus.Created, (await harness.ReadItemAsync(child, Ct)).Status);
    }

    /// <summary>
    /// No lost wake: the condition is read from rows, so a wait whose items had all ended before it was written — there was
    /// never an event left to see — is released by the next scan.
    /// </summary>
    [Fact]
    [Critical(Family.W4, "no lost wake")]
    public async Task No_lost_wake_items_that_ended_before_the_wait_was_written_release_it_at_the_next_scan()
    {
        using var harness = new DispatchHarness(Noon);
        var seeded = await SeedWaitAsync(harness, [WorkItemStatus.Succeeded, WorkItemStatus.Failed], WaitMode.All, until: null);
        Assert.Equal(WaitStatus.Open, (await WaitOfAsync(harness, seeded.Parent)).Status);

        var report = await harness.Runner.ScanOnceAsync(Ct);

        Assert.Equal(1, report.Resumed);
        Assert.Equal(WaitReleaseReason.ChildrenEnded, (await WaitOfAsync(harness, seeded.Parent)).ReleaseReason);
    }

    /// <summary>
    /// Restart: a deadline that passed while the runtime was down releases its item at the runtime's first scan. The
    /// rows are written before the runtime starts, and nothing but that first scan moves them.
    /// </summary>
    [Fact]
    [Critical(Family.W4, "a wait across a restart")]
    public async Task Restart_a_deadline_that_passed_while_the_runtime_was_down_releases_at_the_first_scan()
    {
        string parent = string.Empty;
        await using var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths =>
            {
                File.WriteAllText(paths.UserSettingsFile, """{"Dispatcher":{"TickSeconds":3600}}""");
                parent = SeedDownTime(paths);
            },
            clock: new FixedClock(Noon));

        var status = fixture.Resolve<DispatcherStatus>();
        Assert.True(await DispatchHarness.FirstScanDoneAsync(status, Ct), "The runtime never ran its first scan.");

        await using var db = new JasonDbContext(JasonDbContext.CreateOptions(fixture.Paths.DatabaseFile));
        var item = await db.WorkItems.AsNoTracking().SingleAsync(w => w.PublicId == parent, Ct);
        Assert.Equal(WorkItemStatus.Created, item.Status);
        var wait = await db.WorkItemWaits.AsNoTracking().SingleAsync(w => w.WorkItemId == item.Id, Ct);
        Assert.Equal(WaitStatus.Released, wait.Status);
        Assert.Equal(WaitReleaseReason.WaitCeiling, wait.ReleaseReason);
        Assert.Equal(Noon, wait.ReleasedAt);
    }

    /// <summary>
    /// The ready query reaches the open waits through the waiting items, by indexes, and every named item and every item
    /// by a key: no scan of the work items or of the named items, however many there are. Pinned by the plan, not by a
    /// timing.
    /// </summary>
    [Fact]
    public async Task The_ready_query_reads_the_open_waits_by_their_index_and_every_item_by_its_key()
    {
        using var harness = new DispatchHarness(Noon);
        await SeedWaitAsync(harness, [WorkItemStatus.Processing], WaitMode.All, until: null);
        harness.Commands.Clear();

        await harness.ResumeAsync(Ct);

        var sent = Assert.Single(harness.Commands.Texts, text => text.Contains("work_item_wait_items", StringComparison.Ordinal));
        var plan = PlanOf(harness, sent);
        AssertReadByKeys(plan);
    }

    /// <summary>
    /// A thousand open waits over a hundred items each, a hundred thousand named items in all, in every shape the step
    /// decides between: all ended, one not; any ended, none; a moment come, a ceiling come, neither. The step releases
    /// exactly the waits that are over, each for its own reason, and its plan at that size is still the pinned one. What
    /// is asserted is what was released; how long it took is not.
    /// </summary>
    [Fact]
    public async Task A_thousand_waits_over_a_hundred_items_each_release_exactly_the_waits_that_are_over()
    {
        using var harness = new DispatchHarness(Noon);
        await using (var db = harness.Open())
        {
            var campaign = WorkItemFactory.NewCampaign(now: Noon);
            db.Campaigns.Add(campaign);
            var template = WorkItemFactory.NewAiRole(campaign, now: Noon);
            db.WorkItems.Add(template);
            await db.SaveChangesAsync(Ct);
            await SeedThousandWaitsAsync(db, template.PublicId);
        }

        harness.Commands.Clear();
        var resumed = await harness.ResumeAsync(Ct);

        // Wait n (1..1000): n % 4 == 0 all ended, n % 4 == 2 any ended (one cancelled), n % 8 == 5 a moment that came
        // over items not all ended, n % 8 == 7 a ceiling that came over items none of which ended; n % 8 == 1 and
        // n % 8 == 3 are not over.
        Assert.Equal(750, resumed);
        await using (var db = harness.Open())
        {
            var closed = await db.WorkItemWaits.AsNoTracking()
                .GroupBy(w => new { w.Status, w.ReleaseReason })
                .Select(g => new { g.Key.Status, g.Key.ReleaseReason, Count = g.Count() })
                .ToListAsync(Ct);
            var said = string.Join(", ", closed.Select(g => $"{g.Status}/{g.ReleaseReason}={g.Count}"));
            Assert.True(closed.Count == 4, $"The waits closed as {said}.");
            Assert.Contains(closed, g => g is { Status: WaitStatus.Released, ReleaseReason: WaitReleaseReason.ChildrenEnded, Count: 500 });
            Assert.Contains(closed, g => g is { Status: WaitStatus.Released, ReleaseReason: WaitReleaseReason.MomentReached, Count: 125 });
            Assert.Contains(closed, g => g is { Status: WaitStatus.Released, ReleaseReason: WaitReleaseReason.WaitCeiling, Count: 125 });
            Assert.Contains(closed, g => g is { Status: WaitStatus.Open, ReleaseReason: null, Count: 250 });

            var stillWaiting = await db.WorkItems.AsNoTracking()
                .Where(w => w.Status == WorkItemStatus.Waiting && w.PublicId.StartsWith("wi_P"))
                .Select(w => w.PublicId)
                .ToListAsync(Ct);
            var notOver = stillWaiting.Select(id => int.Parse(id[4..], System.Globalization.CultureInfo.InvariantCulture) % 8).Distinct().Order().ToList();
            Assert.True(stillWaiting.Count == 250 && notOver.SequenceEqual([1, 3]), $"{stillWaiting.Count} items still wait, by n % 8: {string.Join(",", notOver)}.");
            Assert.Equal(750, await db.Journal.AsNoTracking().CountAsync(e => e.Kind == JournalKinds.WorkItemResumed, Ct));
        }

        AssertReadByKeys(PlanOf(harness, Assert.Single(harness.Commands.Texts, text => text.Contains("work_item_wait_items", StringComparison.Ordinal))));
    }

    /// <summary>
    /// The rows of the sized run, written in four statements from the one item the test made through the runtime: a
    /// thousand waiting items, a hundred thousand named items, a thousand open waits and the pairs between them.
    /// </summary>
    private static async Task SeedThousandWaitsAsync(JasonDbContext db, string template)
    {
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        await connection.OpenAsync(Ct);
        var columns = new List<string>();
        using (var info = connection.CreateCommand())
        {
            info.CommandText = "PRAGMA table_info(work_items)";
            using var reader = await info.ExecuteReaderAsync(Ct);
            while (await reader.ReadAsync(Ct))
            {
                columns.Add(reader.GetString(1));
            }
        }

        string Copy(string publicId, string status)
        {
            var into = columns.Where(c => c != "id").ToList();
            var values = into.Select(c => c switch
            {
                "public_id" => publicId,
                "status" => status,
                _ => "t." + c,
            });
            return $"INSERT INTO work_items ({string.Join(", ", into)}) SELECT {string.Join(", ", values)} FROM work_items t, n WHERE t.public_id = '{template}'";
        }

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(Ct);
        await RunAsync(
            Numbers(1000)
            + Copy("'wi_P' || printf('%06d', n.v)", "'waiting'"));
        await RunAsync(
            Numbers(100_000)
            + Copy(
                "'wi_C' || printf('%07d', n.v)",
                """
                CASE ((n.v - 1) / 100 + 1) % 4
                  WHEN 0 THEN 'succeeded'
                  WHEN 1 THEN CASE WHEN (n.v - 1) % 100 = 0 THEN 'created' ELSE 'failed' END
                  WHEN 2 THEN CASE WHEN (n.v - 1) % 100 = 0 THEN 'cancelled' ELSE 'processing' END
                  ELSE CASE (n.v - 1) % 3 WHEN 0 THEN 'processing' WHEN 1 THEN 'created' ELSE 'waiting' END
                END
                """));
        await RunAsync(
            """
            WITH RECURSIVE n(v) AS (SELECT 1 UNION ALL SELECT v + 1 FROM n WHERE v < 1000)
            INSERT INTO work_item_waits (work_item_id, attempt_id, mode, until, deadline, status, opened_at)
            SELECT p.id, NULL,
                   CASE WHEN n.v % 4 IN (0, 1) THEN 'all' ELSE 'any' END,
                   CASE WHEN n.v % 8 = 5 THEN @p0 WHEN n.v % 8 = 3 THEN @p1 END,
                   CASE WHEN n.v % 8 IN (5, 7) THEN @p0 ELSE @p1 END,
                   'open', @p2
            FROM n JOIN work_items p ON p.public_id = 'wi_P' || printf('%06d', n.v)
            """,
            Noon.AddMinutes(-1), Noon.AddDays(7), Noon.AddDays(-1));
        await RunAsync(
            """
            WITH RECURSIVE n(v) AS (SELECT 1 UNION ALL SELECT v + 1 FROM n WHERE v < 100000)
            INSERT INTO work_item_wait_items (wait_id, child_work_item_id, position)
            SELECT x.id, c.id, (n.v - 1) % 100
            FROM n
            JOIN work_items c ON c.public_id = 'wi_C' || printf('%07d', n.v)
            JOIN work_items p ON p.public_id = 'wi_P' || printf('%06d', (n.v - 1) / 100 + 1)
            JOIN work_item_waits x ON x.work_item_id = p.id
            """);
        await transaction.CommitAsync(Ct);

        static string Numbers(int to) => $"WITH RECURSIVE n(v) AS (SELECT 1 UNION ALL SELECT v + 1 FROM n WHERE v < {to}) ";

        async Task RunAsync(string sql, params object[] values)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            for (var i = 0; i < values.Length; i++)
            {
                command.Parameters.AddWithValue($"@p{i}", values[i]);
            }

            await command.ExecuteNonQueryAsync(Ct);
        }
    }

    /// <summary>
    /// The plan the ready query must keep: the waiting items by the status index, each one's open wait by the index of the
    /// waits by item and status, each wait's items by the key of the named-items table, and each named item by its own
    /// key. Nothing scanned.
    /// </summary>
    internal static void AssertReadByKeys(IReadOnlyList<string> plan)
    {
        var said = string.Join(" / ", plan);
        Assert.False(plan.Any(step => step.StartsWith("SCAN", StringComparison.Ordinal)), $"The ready query scans a table: {said}");
        Assert.True(
            plan.Where(step => step.StartsWith("SEARCH", StringComparison.Ordinal)).Distinct().Order(StringComparer.Ordinal).SequenceEqual(
            [
                "SEARCH c USING INTEGER PRIMARY KEY (rowid=?)",
                "SEARCH i USING COVERING INDEX sqlite_autoindex_work_item_wait_items_1 (wait_id=?)",
                "SEARCH w USING COVERING INDEX ix_work_items_status_finished_at (status=?)",
                "SEARCH x USING INDEX ix_work_item_waits_work_item_id_status (work_item_id=? AND status=?)",
            ]),
            $"The ready query's plan changed: {said}");
    }

    /// <summary>What SQLite plans for the command the step sent, its parameters bound to the harness's clock; one step a line.</summary>
    private static List<string> PlanOf(DispatchHarness harness, string text)
    {
        using var db = harness.Open();
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        connection.Open();
        using var explain = connection.CreateCommand();
        explain.CommandText = "EXPLAIN QUERY PLAN " + text;
        foreach (System.Text.RegularExpressions.Match name in System.Text.RegularExpressions.Regex.Matches(text, @"@p\d+"))
        {
            if (!explain.Parameters.Contains(name.Value))
            {
                explain.Parameters.AddWithValue(name.Value, "2026-09-14 12:00:00");
            }
        }

        using var reader = explain.ExecuteReader();
        var plan = new List<string>();
        while (reader.Read())
        {
            plan.Add(reader.GetString(3));
        }

        return plan;
    }

    private sealed record Seeded(string Campaign, string Parent, string Attempt, IReadOnlyList<string> Children);

    /// <summary>
    /// A campaign with a planner whose running attempt ends waiting, through the runtime's own routine, on new items in the
    /// given statuses: the rows a role's wait leaves behind, written the way the runtime writes them.
    /// </summary>
    private static async Task<Seeded> SeedWaitAsync(
        DispatchHarness harness,
        WorkItemStatus[] children,
        WaitMode? mode,
        DateTime? until,
        CampaignStatus campaignStatus = CampaignStatus.Active)
    {
        var now = harness.Clock.Now.UtcDateTime;
        string campaignId;
        string parentId;
        List<string> childIds;
        await using (var db = harness.Open())
        {
            var campaign = WorkItemFactory.NewCampaign(status: campaignStatus, now: now);
            var parent = WorkItemFactory.NewAiRole(campaign, "planner", now, w => w.Status = WorkItemStatus.Processing);
            WorkItemFactory.NewAttempt(parent, 1, AttemptStatus.Running, now);
            var kids = children.Select(status => WorkItemFactory.NewAiRole(campaign, "researcher", now, w => w.Status = status)).ToList();
            db.Campaigns.Add(campaign);
            db.WorkItems.Add(parent);
            db.WorkItems.AddRange(kids);
            await db.SaveChangesAsync(Ct);
            (campaignId, parentId, childIds) = (campaign.PublicId, parent.PublicId, [.. kids.Select(k => k.PublicId)]);
        }

        var attempt = await WaitAsync(harness, parentId, childIds, mode, until);
        return new Seeded(campaignId, parentId, attempt, childIds);
    }

    /// <summary>An item already in the queue starts running and ends waiting on one new item; the new item's id comes back.</summary>
    private static async Task<string> SeedWaitOnAsync(DispatchHarness harness, string waiter, WaitMode mode, DateTime? until)
    {
        var now = harness.Clock.Now.UtcDateTime;
        string target;
        await using (var db = harness.Open())
        {
            var item = await db.WorkItems.Include(w => w.Campaign).Include(w => w.Attempts).SingleAsync(w => w.PublicId == waiter, Ct);
            item.Status = WorkItemStatus.Processing;
            db.Attempts.Add(WorkItemFactory.NewAttempt(item, item.Attempts.Count + 1, AttemptStatus.Running, now));
            var grandchild = WorkItemFactory.NewAiRole(item.Campaign!, "researcher", now, w => w.Status = WorkItemStatus.Processing);
            db.WorkItems.Add(grandchild);
            await db.SaveChangesAsync(Ct);
            target = grandchild.PublicId;
        }

        await WaitAsync(harness, waiter, [target], mode, until);
        return target;
    }

    /// <summary>The item's running attempt ends waiting, through <see cref="AttemptOutcomes.Wait"/>; its id comes back.</summary>
    private static async Task<string> WaitAsync(DispatchHarness harness, string itemId, IReadOnlyList<string> on, WaitMode? mode, DateTime? until)
    {
        await using var scope = harness.Scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        var item = await db.WorkItems.Include(w => w.Attempts).SingleAsync(w => w.PublicId == itemId, Ct);
        var attempt = item.Attempts.Single(a => a.Status == AttemptStatus.Running);
        var targets = await db.WorkItems.Where(w => on.Contains(w.PublicId)).ToListAsync(Ct);
        scope.ServiceProvider.GetRequiredService<AttemptOutcomes>()
            .Wait(db, item, attempt, [.. on.Select(id => targets.Single(t => t.PublicId == id))], mode, until, Actors.ForAttempt(attempt));
        await db.SaveChangesAsync(Ct);
        return attempt.PublicId;
    }

    /// <summary>
    /// Rows a runtime that stopped left behind: a planner of a paused campaign — so no claim muddies the question — that
    /// ended waiting a day ago on a researcher still running, with a ceiling that came an hour ago.
    /// </summary>
    private static string SeedDownTime(JasonPaths paths)
    {
        new DatabaseMigrator(paths).Migrate();
        using var db = new JasonDbContext(JasonDbContext.CreateOptions(paths.DatabaseFile));
        var opened = Noon.AddDays(-1);
        var campaign = WorkItemFactory.NewCampaign("Down", CampaignStatus.Paused, now: opened);
        var parent = WorkItemFactory.NewAiRole(campaign, "planner", opened, w => w.Status = WorkItemStatus.Waiting);
        var attempt = WorkItemFactory.NewAttempt(parent, 1, AttemptStatus.Yielded, opened);
        attempt.FinishedAt = opened;
        var child = WorkItemFactory.NewAiRole(campaign, "researcher", opened, w => w.Status = WorkItemStatus.Processing);
        WorkItemFactory.NewAttempt(child, 1, AttemptStatus.Running, Noon.AddMinutes(-5));
        db.Campaigns.Add(campaign);
        db.WorkItems.AddRange(parent, child);
        db.WorkItemWaits.Add(new WorkItemWait
        {
            WorkItem = parent,
            Attempt = attempt,
            Mode = WaitMode.All,
            Deadline = Noon.AddHours(-1),
            OpenedAt = opened,
            Items = [new WorkItemWaitItem { ChildWorkItem = child }],
        });
        db.SaveChanges();
        return parent.PublicId;
    }

    private static async Task SetStatusAsync(DispatchHarness harness, string publicId, WorkItemStatus status)
    {
        await using var db = harness.Open();
        await db.WorkItems.Where(w => w.PublicId == publicId).ExecuteUpdateAsync(s => s.SetProperty(w => w.Status, status), Ct);
    }

    private static async Task<WorkItemWait> WaitOfAsync(DispatchHarness harness, string publicId)
    {
        await using var db = harness.Open();
        return await db.WorkItemWaits.AsNoTracking().Where(w => w.WorkItem!.PublicId == publicId).OrderByDescending(w => w.Id).FirstAsync(Ct);
    }
}
