using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Execution;

/// <summary>
/// <c>workitem.wait</c> as the executor calls it: the fence first, then each refusal in its published order — a work item
/// that may not wait, a named item that is not this item's to wait for, a moment already past, and the bounds — one row
/// each, and rows where two refusals meet so the order is held and not only the codes. Every refusal changes nothing.
/// </summary>
public class ExecutorWaitTests
{
    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Critical(Family.W2, "waiting is fenced")]
    public async Task A_stale_attempt_is_told_to_stop_and_nothing_changes()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database);
        await using var ctx = database.Open();

        var error = await Assert.ThrowsAsync<ConflictException>(
            () => NewService(ctx).WaitAsync(Request(seeded.ItemId, PublicId.New("att"), [seeded.ChildId]), Ct));

        Assert.Equal("stale_attempt", error.Code);
        await AssertUnchangedAsync(database, seeded);
    }

    [Fact]
    public async Task The_fence_comes_before_the_check_in_rule()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database, configure: CheckIn);
        await using var ctx = database.Open();

        var error = await Assert.ThrowsAsync<ConflictException>(
            () => NewService(ctx).WaitAsync(Request(seeded.ItemId, PublicId.New("att"), [seeded.ChildId]), Ct));

        Assert.Equal("stale_attempt", error.Code);
    }

    [Fact]
    [Critical(Family.W2, "a check-in never waits")]
    public async Task A_check_in_may_not_wait()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database, configure: CheckIn);
        await using var ctx = database.Open();

        var error = await Assert.ThrowsAsync<InvalidRequestException>(
            () => NewService(ctx).WaitAsync(Request(seeded.ItemId, seeded.AttemptId, [seeded.ChildId]), Ct));

        Assert.Equal("wait_not_allowed", error.Code);
        Assert.Contains("check-in", error.Message, StringComparison.Ordinal);
        await AssertUnchangedAsync(database, seeded);
    }

    [Fact]
    public async Task Provider_work_may_not_wait()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database, provider: true);
        await using var ctx = database.Open();

        var error = await Assert.ThrowsAsync<InvalidRequestException>(
            () => NewService(ctx).WaitAsync(Request(seeded.ItemId, seeded.AttemptId, [], until: Noon.AddDays(1)), Ct));

        Assert.Equal("wait_not_allowed", error.Code);
        await AssertUnchangedAsync(database, seeded);
    }

    [Fact]
    public async Task The_check_in_rule_comes_before_the_named_items()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database, configure: CheckIn);
        await using var ctx = database.Open();

        var error = await Assert.ThrowsAsync<InvalidRequestException>(
            () => NewService(ctx).WaitAsync(Request(seeded.ItemId, seeded.AttemptId, [PublicId.New("wi")]), Ct));

        Assert.Equal("wait_not_allowed", error.Code);
    }

    [Fact]
    [Critical(Family.W2, "a wait names only what the item raised")]
    public async Task An_item_another_actor_raised_is_not_this_item_s_to_wait_for()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database, childBy: ActorType.Human);
        await using var ctx = database.Open();

        var error = await Assert.ThrowsAsync<InvalidRequestException>(
            () => NewService(ctx).WaitAsync(Request(seeded.ItemId, seeded.AttemptId, [seeded.ChildId]), Ct));

        Assert.Equal("wait_target_invalid", error.Code);
        Assert.Contains(seeded.ChildId, error.Message, StringComparison.Ordinal);
        Assert.Contains("no attempt of this item raised it", error.Message, StringComparison.Ordinal);
        await AssertUnchangedAsync(database, seeded);
    }

    [Fact]
    public async Task An_item_an_earlier_run_of_the_same_item_raised_is_this_item_s_to_wait_for()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database, yielded: 1, raiser: Raiser.EarlierAttempt);
        await using var ctx = database.Open();

        var waiting = await NewService(ctx).WaitAsync(Request(seeded.ItemId, seeded.AttemptId, [seeded.ChildId]), Ct);

        Assert.Equal(WorkItemStatus.Waiting, waiting.Status);
        Assert.Equal(seeded.ChildId, Assert.Single(waiting.Wait!.On).WorkItemId);
    }

    [Fact]
    public async Task An_item_another_item_s_run_raised_in_the_same_campaign_is_not_this_item_s_to_wait_for()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database, raiser: Raiser.SiblingAttempt);
        await using var ctx = database.Open();

        var error = await Assert.ThrowsAsync<InvalidRequestException>(
            () => NewService(ctx).WaitAsync(Request(seeded.ItemId, seeded.AttemptId, [seeded.ChildId]), Ct));

        Assert.Equal("wait_target_invalid", error.Code);
        Assert.Contains(seeded.ChildId, error.Message, StringComparison.Ordinal);
        Assert.Contains("no attempt of this item raised it", error.Message, StringComparison.Ordinal);
        await AssertUnchangedAsync(database, seeded);
    }

    [Fact]
    public async Task An_item_raised_in_another_campaign_is_not_this_item_s_to_wait_for()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database, childElsewhere: true);
        await using var ctx = database.Open();

        var error = await Assert.ThrowsAsync<InvalidRequestException>(
            () => NewService(ctx).WaitAsync(Request(seeded.ItemId, seeded.AttemptId, [seeded.ChildId]), Ct));

        Assert.Equal("wait_target_invalid", error.Code);
        Assert.Contains(seeded.ChildId, error.Message, StringComparison.Ordinal);
        Assert.Contains("another campaign", error.Message, StringComparison.Ordinal);
        await AssertUnchangedAsync(database, seeded);
    }

    [Fact]
    public async Task A_decision_is_never_a_target()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database);
        await using var ctx = database.Open();
        var decision = PublicId.New("dec");

        var error = await Assert.ThrowsAsync<InvalidRequestException>(
            () => NewService(ctx).WaitAsync(Request(seeded.ItemId, seeded.AttemptId, [seeded.ChildId, decision]), Ct));

        Assert.Equal("wait_target_invalid", error.Code);
        Assert.Contains(decision, error.Message, StringComparison.Ordinal);
        await AssertUnchangedAsync(database, seeded);
    }

    [Fact]
    public async Task An_item_that_does_not_exist_is_not_a_target()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database);
        await using var ctx = database.Open();
        var missing = PublicId.New("wi");

        var error = await Assert.ThrowsAsync<InvalidRequestException>(
            () => NewService(ctx).WaitAsync(Request(seeded.ItemId, seeded.AttemptId, [seeded.ChildId, missing]), Ct));

        Assert.Equal("wait_target_invalid", error.Code);
        Assert.Contains(missing, error.Message, StringComparison.Ordinal);
        Assert.Contains("no such work item exists", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_first_item_refused_is_the_one_named_although_a_decision_follows_it()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database);
        await using var ctx = database.Open();
        var missing = PublicId.New("wi");

        var error = await Assert.ThrowsAsync<InvalidRequestException>(
            () => NewService(ctx).WaitAsync(Request(seeded.ItemId, seeded.AttemptId, [missing, PublicId.New("dec")]), Ct));

        Assert.Equal("wait_target_invalid", error.Code);
        Assert.True(error.Message.Contains(missing, StringComparison.Ordinal), $"the message names another item: {error.Message}");
        await AssertUnchangedAsync(database, seeded);
    }

    [Fact]
    public async Task More_than_one_hundred_items_is_refused_before_any_is_looked_up()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database);
        await using var ctx = database.Open();
        var named = Enumerable.Range(0, WorkItemWaitRequest.MaxWaitItems + 1).Select(_ => PublicId.New("wi")).ToList();

        var error = await Assert.ThrowsAsync<InvalidRequestException>(
            () => NewService(ctx).WaitAsync(Request(seeded.ItemId, seeded.AttemptId, named), Ct));

        Assert.Equal("wait_target_invalid", error.Code);
        Assert.Contains("101", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_named_items_come_before_the_moment()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database);
        await using var ctx = database.Open();

        var error = await Assert.ThrowsAsync<InvalidRequestException>(
            () => NewService(ctx).WaitAsync(Request(seeded.ItemId, seeded.AttemptId, [PublicId.New("wi")], until: Noon.AddMinutes(-1)), Ct));

        Assert.Equal("wait_target_invalid", error.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-60)]
    public async Task A_moment_already_past_is_the_caller_s_mistake(int seconds)
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database);
        await using var ctx = database.Open();

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => NewService(ctx).WaitAsync(Request(seeded.ItemId, seeded.AttemptId, [seeded.ChildId], until: Noon.AddSeconds(seconds)), Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal(("until", "in_the_past"), (detail.Field, detail.Code));
        await AssertUnchangedAsync(database, seeded);
    }

    [Fact]
    public async Task The_moment_comes_before_the_bounds()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database, yielded: 2);
        await using var ctx = database.Open();

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => NewService(ctx, maxWaits: 2).WaitAsync(Request(seeded.ItemId, seeded.AttemptId, [], until: Noon.AddMinutes(-1)), Ct));

        Assert.Equal("until", Assert.Single(error.Details!).Field);
    }

    [Fact]
    public async Task A_moment_beyond_the_ceiling_is_too_long()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database);
        await using var ctx = database.Open();

        var error = await Assert.ThrowsAsync<InvalidRequestException>(
            () => NewService(ctx, maxWaitSeconds: 3600).WaitAsync(Request(seeded.ItemId, seeded.AttemptId, [], until: Noon.AddSeconds(3601)), Ct));

        Assert.Equal("wait_too_long", error.Code);
        await AssertUnchangedAsync(database, seeded);
    }

    [Fact]
    public async Task An_item_that_has_waited_as_often_as_it_may_is_told_to_finish()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database, yielded: 2);
        await using var ctx = database.Open();

        var error = await Assert.ThrowsAsync<ConflictException>(
            () => NewService(ctx, maxWaits: 2).WaitAsync(Request(seeded.ItemId, seeded.AttemptId, [seeded.ChildId]), Ct));

        Assert.Equal("wait_limit_reached", error.Code);
        await AssertUnchangedAsync(database, seeded);
    }

    [Fact]
    [Critical(Family.W2, "a wait ends the attempt and parks the item")]
    public async Task A_wait_on_what_the_item_raised_ends_the_attempt_and_says_what_it_waits_for()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database, children: 2);
        await using var ctx = database.Open();

        var dto = await NewService(ctx).WaitAsync(
            new WorkItemWaitRequest(seeded.ItemId, seeded.AttemptId, [seeded.Children[1], seeded.Children[0]], null, null, "the research comes first"),
            Ct);

        Assert.Equal(WorkItemStatus.Waiting, dto.Status);
        var wait = dto.Wait!;
        Assert.Equal(seeded.AttemptId, wait.AttemptId);
        Assert.Equal(WaitMode.All, wait.Mode);
        Assert.Null(wait.Until);
        Assert.Equal(new DateTimeOffset(Noon.AddDays(14)), wait.Deadline);
        Assert.Equal(new DateTimeOffset(Noon), wait.OpenedAt);
        Assert.Equal([seeded.Children[1], seeded.Children[0]], wait.On.Select(i => i.WorkItemId));
        Assert.All(wait.On, i => Assert.Equal(WorkItemStatus.Created, i.Status));

        await using var check = database.Open();
        var attempt = await check.Attempts.AsNoTracking().SingleAsync(a => a.PublicId == seeded.AttemptId, Ct);
        Assert.Equal(AttemptStatus.Yielded, attempt.Status);
        var line = await check.Journal.AsNoTracking().SingleAsync(e => e.WorkItemId == seeded.ItemId && e.Kind == JournalKinds.WorkItemWaiting, Ct);
        Assert.Equal((ActorType.Attempt, seeded.AttemptId), (line.ActorType, line.ActorId));
        Assert.Equal("the research comes first", line.Reason);
    }

    [Fact]
    public async Task A_wait_may_ask_for_any_one_item_and_a_moment_at_the_latest()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database);
        await using var ctx = database.Open();

        var dto = await NewService(ctx).WaitAsync(Request(seeded.ItemId, seeded.AttemptId, [seeded.ChildId], WaitMode.Any, Noon.AddDays(3)), Ct);

        Assert.Equal(WaitMode.Any, dto.Wait!.Mode);
        Assert.Equal(new DateTimeOffset(Noon.AddDays(3)), dto.Wait.Until);
        Assert.Equal(new DateTimeOffset(Noon.AddDays(3)), dto.Wait.Deadline);
    }

    [Fact]
    public async Task A_wait_on_a_moment_alone_names_no_items_and_no_mode()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database);
        await using var ctx = database.Open();

        var dto = await NewService(ctx).WaitAsync(Request(seeded.ItemId, seeded.AttemptId, [], until: Noon.AddDays(5)), Ct);

        Assert.Empty(dto.Wait!.On);
        Assert.Null(dto.Wait.Mode);
        Assert.Equal(new DateTimeOffset(Noon.AddDays(5)), dto.Wait.Deadline);
    }

    [Fact]
    public async Task The_attempt_that_waited_is_stale_from_then_on()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database);
        await using (var first = database.Open())
        {
            await NewService(first).WaitAsync(Request(seeded.ItemId, seeded.AttemptId, [seeded.ChildId]), Ct);
        }

        await using var second = database.Open();
        var error = await Assert.ThrowsAsync<ConflictException>(
            () => NewService(second).HeartbeatAsync(new WorkItemHeartbeatRequest(seeded.ItemId, seeded.AttemptId), Ct));

        Assert.Equal("stale_attempt", error.Code);
    }

    [Theory]
    [InlineData(null, null, "on", "required")]
    [InlineData(null, WaitMode.Any, "mode", "not_allowed")]
    [InlineData(" ", null, "on", "invalid")]
    [InlineData("twice", null, "on", "duplicate")]
    public async Task A_wait_of_the_wrong_shape_is_refused_before_the_fence(string? named, WaitMode? mode, string field, string code)
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database);
        await using var ctx = database.Open();
        IReadOnlyList<string>? on = named switch
        {
            null => [],
            "twice" => [seeded.ChildId, seeded.ChildId],
            _ => [named],
        };

        // A stale attempt as well: the shape is the caller's to get right whoever they are, so it is answered first.
        var error = await Assert.ThrowsAsync<ValidationException>(
            () => NewService(ctx).WaitAsync(new WorkItemWaitRequest(seeded.ItemId, PublicId.New("att"), on, mode, null, null), Ct));

        Assert.Contains(error.Details!, d => d.Field == field && d.Code == code);
    }

    private static void CheckIn(WorkItem item)
    {
        item.Role = "manager";
        item.CreatedByType = ActorType.System;
        item.CreatedById = "dispatcher";
    }

    private static WorkItemWaitRequest Request(string itemId, string attemptId, IReadOnlyList<string> on, WaitMode? mode = null, DateTime? until = null) =>
        new(itemId, attemptId, on, mode, until is { } moment ? new DateTimeOffset(moment) : null, null);

    private static ExecutorService NewService(JasonDbContext db, int? maxWaits = null, int? maxWaitSeconds = null)
    {
        var time = new FixedClock(Noon);
        var settings = TestOptions.Settings(o =>
        {
            o.RetryDelaySeconds = 0;
            o.AiRole.MaxWaits = maxWaits ?? o.AiRole.MaxWaits;
            o.AiRole.MaxWaitSeconds = maxWaitSeconds ?? o.AiRole.MaxWaitSeconds;
        });
        return new ExecutorService(db, time, new AttemptOutcomes(new JournalWriter(time), time, settings), settings);
    }

    /// <summary>A refused wait leaves the item processing under its attempt, writes no wait and no line, and keeps the attempt's last heartbeat.</summary>
    private static async Task AssertUnchangedAsync(TestDatabase database, Seeded seeded)
    {
        await using var check = database.Open();
        var item = await check.WorkItems.AsNoTracking().SingleAsync(w => w.PublicId == seeded.ItemId, Ct);
        Assert.Equal(WorkItemStatus.Processing, item.Status);
        var attempt = await check.Attempts.AsNoTracking().SingleAsync(a => a.PublicId == seeded.AttemptId, Ct);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.LastHeartbeatAt);
        Assert.Empty(await check.WorkItemWaits.AsNoTracking().ToListAsync(Ct));
        Assert.Empty(await check.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.WorkItemWaiting).ToListAsync(Ct));
    }

    /// <summary>Whose run raised the seeded children: the item's running attempt, its first and yielded one, or another item's in the campaign.</summary>
    private enum Raiser
    {
        CurrentAttempt,
        EarlierAttempt,
        SiblingAttempt,
    }

    private sealed record Seeded(string ItemId, string AttemptId, IReadOnlyList<string> Children)
    {
        public string ChildId => Children[0];
    }

    private static async Task<Seeded> SeedAsync(
        TestDatabase database,
        Action<WorkItem>? configure = null,
        bool provider = false,
        ActorType childBy = ActorType.Attempt,
        bool childElsewhere = false,
        int children = 1,
        int yielded = 0,
        Raiser raiser = Raiser.CurrentAttempt)
    {
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var elsewhere = WorkItemFactory.NewCampaign("Elsewhere", now: Noon);
        var item = provider
            ? WorkItemFactory.NewProviderOp(campaign, now: Noon)
            : WorkItemFactory.NewAiRole(campaign, "planner", now: Noon, configure: configure);
        item.Status = WorkItemStatus.Processing;
        for (var number = 1; number <= yielded; number++)
        {
            var earlier = WorkItemFactory.NewAttempt(item, number, AttemptStatus.Yielded, Noon.AddHours(-number));
            earlier.FinishedAt = Noon.AddHours(-number);
            db.Attempts.Add(earlier);
        }

        var attempt = WorkItemFactory.NewAttempt(item, yielded + 1, AttemptStatus.Running, Noon);
        var sibling = WorkItemFactory.NewAiRole(campaign, "planner", now: Noon);
        var siblingAttempt = WorkItemFactory.NewAttempt(sibling, 1, AttemptStatus.Yielded, Noon.AddHours(-1));
        siblingAttempt.FinishedAt = Noon.AddHours(-1);
        var raiserId = raiser switch
        {
            Raiser.EarlierAttempt => item.Attempts.Single(a => a.Number == 1).PublicId,
            Raiser.SiblingAttempt => siblingAttempt.PublicId,
            _ => attempt.PublicId,
        };
        var raised = Enumerable.Range(0, children)
            .Select(_ => WorkItemFactory.NewAiRole(childElsewhere ? elsewhere : campaign, "researcher", now: Noon, configure: w =>
            {
                w.CreatedByType = childBy;
                w.CreatedById = childBy == ActorType.Attempt ? raiserId : "marta";
            }))
            .ToList();
        db.Campaigns.AddRange(campaign, elsewhere);
        db.WorkItems.AddRange(item, sibling);
        db.WorkItems.AddRange(raised);
        db.Attempts.AddRange(attempt, siblingAttempt);
        await db.SaveChangesAsync(Ct);
        return new Seeded(item.PublicId, attempt.PublicId, [.. raised.Select(w => w.PublicId)]);
    }
}
