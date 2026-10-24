using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Plugins;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Execution;

public class AttemptOutcomesTests
{
    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_successful_attempt_finishes_its_item_and_stores_the_result()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (outcomes, _) = NewOutcomes();
        var (item, attempt) = await SeedAsync(db, AttemptStatus.Running, WorkItemStatus.Processing);

        outcomes.Succeed(db, item, attempt, JsonNode.Parse("""{"summary":"done"}"""), Actors.ForAttempt(attempt));
        await db.SaveChangesAsync(Ct);

        Assert.Equal(AttemptStatus.Succeeded, attempt.Status);
        Assert.Equal(Noon, attempt.FinishedAt);
        Assert.Equal(WorkItemStatus.Succeeded, item.Status);
        Assert.Equal(Noon, item.FinishedAt);
        Assert.Equal("done", (string?)item.Result!["summary"]);

        var entry = await SingleEntryAsync(db, JournalKinds.WorkItemSucceeded);
        Assert.Equal(attempt.PublicId, entry.AttemptId);
        Assert.Equal(item.PublicId, entry.WorkItemId);
        Assert.Equal(item.CampaignId, entry.CampaignId);
        Assert.Equal("result_present", entry.Key);
        Assert.True((bool)entry.New!);
    }

    [Fact]
    [Critical(Family.W4, "retry with delay")]
    public async Task A_retriable_failure_below_the_limit_gives_the_item_back_with_a_delay()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (outcomes, _) = NewOutcomes();
        var (item, attempt) = await SeedAsync(db, AttemptStatus.Running, WorkItemStatus.Processing);

        var status = outcomes.Fail(db, item, attempt, AttemptErrors.LeaseExpired, "the lease ran out", "trace", null, Actors.Dispatcher);
        await db.SaveChangesAsync(Ct);

        Assert.Equal(WorkItemStatus.Created, status);
        Assert.Equal(WorkItemStatus.Created, item.Status);
        Assert.Equal(1, item.AttemptCount);
        Assert.Equal(Noon.AddSeconds(60), item.RetryAfter);
        Assert.Null(item.FinishedAt);
        Assert.Null(item.LastError);
        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.True(attempt.Error!.Retriable);

        var entry = await SingleEntryAsync(db, JournalKinds.WorkItemReleased);
        Assert.Equal(attempt.PublicId, entry.Key);
        Assert.Equal(AttemptErrors.LeaseExpired, (string?)entry.New!["code"]);
        Assert.Equal("2026-09-14T12:01:00.000Z", (string?)entry.New["retry_after"]);
    }

    [Fact]
    [Critical(Family.W4, "the retry limit")]
    public async Task The_last_attempt_fails_the_item_and_leaves_the_trace_on_the_attempt()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (outcomes, _) = NewOutcomes();
        var (item, first) = await SeedAsync(db, AttemptStatus.Running, WorkItemStatus.Processing);

        outcomes.Fail(db, item, first, AttemptErrors.LeaseExpired, "the lease ran out", "trace one", null, Actors.Dispatcher);
        await db.SaveChangesAsync(Ct);

        WorkItemTransitions.Apply(item, WorkItemStatus.Scheduled, Noon);
        WorkItemTransitions.Apply(item, WorkItemStatus.Processing, Noon);
        var second = WorkItemFactory.NewAttempt(item, 2, AttemptStatus.Running, Noon);
        db.Attempts.Add(second);
        await db.SaveChangesAsync(Ct);

        var status = outcomes.Fail(db, item, second, AttemptErrors.HeartbeatMissed, "no heartbeat", "trace two", null, Actors.Dispatcher);
        await db.SaveChangesAsync(Ct);

        Assert.Equal(WorkItemStatus.Failed, status);
        Assert.Equal(2, item.AttemptCount);
        Assert.Equal(Noon, item.FinishedAt);
        Assert.Equal(AttemptErrors.HeartbeatMissed, item.LastError!.Code);
        Assert.Null(item.LastError.Trace);
        Assert.Equal("trace two", second.Error!.Trace);

        var entry = await SingleEntryAsync(db, JournalKinds.WorkItemFailed);
        Assert.Equal(AttemptErrors.HeartbeatMissed, (string?)entry.New!["code"]);
        Assert.Null(entry.New["trace"]);
    }

    [Fact]
    [Critical(Family.W4, "final failures end the item")]
    public async Task A_failure_that_is_not_worth_repeating_ends_the_item_at_once()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (outcomes, _) = NewOutcomes();
        var (item, attempt) = await SeedAsync(db, AttemptStatus.Scheduled, WorkItemStatus.Scheduled);

        var status = outcomes.Fail(db, item, attempt, AttemptErrors.NoRoute, "no provider route exists", null, null, Actors.Dispatcher);
        await db.SaveChangesAsync(Ct);

        Assert.Equal(WorkItemStatus.Failed, status);
        Assert.Equal(1, item.AttemptCount);
        Assert.False(attempt.Error!.Retriable);
        Assert.Null(item.RetryAfter);
    }

    /// <summary>
    /// The default is the runtime's own rule set, unchanged: an agent failure is classified by its code and
    /// carries no class, because nobody with the standing to name one was involved.
    /// </summary>
    [Fact]
    public async Task A_failure_nobody_classified_is_judged_by_its_code_alone()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (outcomes, _) = NewOutcomes();
        var (item, attempt) = await SeedAsync(db, AttemptStatus.Running, WorkItemStatus.Processing);

        var status = outcomes.Fail(db, item, attempt, AttemptErrors.LeaseExpired, "the lease ran out", null, null, Actors.Dispatcher);
        await db.SaveChangesAsync(Ct);

        Assert.Equal(WorkItemStatus.Created, status);
        Assert.True(attempt.Error!.Retriable);
        Assert.Null(attempt.Error.Class);
    }

    /// <summary>
    /// A plugin's answer outranks the code table: the code is in <c>RetriableCodes</c> and the item still ends,
    /// because what the provider said about this particular failure is better evidence than a name.
    /// </summary>
    [Fact]
    [Critical(Family.W4, "the failure class decides")]
    public async Task A_caller_that_was_told_the_class_decides_instead_of_the_code()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (outcomes, _) = NewOutcomes();
        var (item, attempt) = await SeedAsync(db, AttemptStatus.Running, WorkItemStatus.Processing);
        Assert.Contains("timeout", FailureClassifier.RetriableCodes);

        var status = outcomes.Fail(
            db, item, attempt, "timeout", "the provider never answered", null, null, Actors.Dispatcher,
            failureClass: FailureClass.Ambiguous, retriable: false);
        await db.SaveChangesAsync(Ct);

        Assert.Equal(WorkItemStatus.Failed, status);
        Assert.False(attempt.Error!.Retriable);
        Assert.Equal(FailureClass.Ambiguous, attempt.Error.Class);
        Assert.Equal(FailureClass.Ambiguous, item.LastError!.Class);
        Assert.Null(item.RetryAfter);
    }

    /// <summary>A class on its own says what happened; whether to try again is still the code's answer.</summary>
    [Fact]
    public async Task A_class_without_a_verdict_leaves_the_retry_decision_where_it_was()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (outcomes, _) = NewOutcomes();
        var (item, attempt) = await SeedAsync(db, AttemptStatus.Running, WorkItemStatus.Processing);

        var status = outcomes.Fail(
            db, item, attempt, "rate_limited", "the provider asked for a pause", null, null, Actors.Dispatcher,
            failureClass: FailureClass.Transient);
        await db.SaveChangesAsync(Ct);

        Assert.Equal(WorkItemStatus.Created, status);
        Assert.True(attempt.Error!.Retriable);
        Assert.Equal(FailureClass.Transient, attempt.Error.Class);
    }

    [Fact]
    public async Task A_retry_delay_of_zero_makes_the_item_claimable_at_the_next_scan()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (outcomes, options) = NewOutcomes();
        options.CurrentValue.RetryDelaySeconds = 0;
        var (item, attempt) = await SeedAsync(db, AttemptStatus.Running, WorkItemStatus.Processing);

        outcomes.Fail(db, item, attempt, AttemptErrors.ExecutorExited, "the executor exited", null, null, Actors.Dispatcher);
        await db.SaveChangesAsync(Ct);

        Assert.Null(item.RetryAfter);
        Assert.Null((await SingleEntryAsync(db, JournalKinds.WorkItemReleased)).New!["retry_after"]);
    }

    [Fact]
    [Critical(Family.W4, "interruption is free")]
    public async Task An_interrupted_attempt_costs_the_item_nothing()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (outcomes, _) = NewOutcomes();
        var (item, attempt) = await SeedAsync(db, AttemptStatus.Scheduled, WorkItemStatus.Scheduled);
        item.RetryAfter = Noon.AddHours(1);

        outcomes.Interrupt(db, item, attempt, Actors.Dispatcher);
        await db.SaveChangesAsync(Ct);

        Assert.Equal(AttemptStatus.Interrupted, attempt.Status);
        Assert.Equal(WorkItemStatus.Created, item.Status);
        Assert.Equal(0, item.AttemptCount);
        Assert.Null(item.RetryAfter);

        var entry = await SingleEntryAsync(db, JournalKinds.WorkItemReleased);
        Assert.Equal(AttemptErrors.Interrupted, (string?)entry.New!["code"]);
    }

    [Fact]
    public async Task Cancelling_marks_only_the_attempt()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (outcomes, _) = NewOutcomes();
        var (item, attempt) = await SeedAsync(db, AttemptStatus.Running, WorkItemStatus.Processing);

        outcomes.CancelAttempt(attempt);
        await db.SaveChangesAsync(Ct);

        Assert.Equal(AttemptStatus.Cancelled, attempt.Status);
        Assert.Equal(Noon, attempt.FinishedAt);
        Assert.Equal(AttemptErrors.Cancelled, attempt.Error!.Code);
        Assert.False(attempt.Error.Retriable);
        Assert.Equal(WorkItemStatus.Processing, item.Status);
        Assert.Empty(await db.Journal.Where(e => e.WorkItemId == item.PublicId).ToListAsync(Ct));
    }

    /// <summary>
    /// A run that ends waiting on its children costs the item no attempt: the attempt is yielded and no longer live, the
    /// item waits with no finish time, and the wait is bounded by the ceiling counted from the moment it opened.
    /// </summary>
    [Fact]
    [Critical(Family.W4, "a wait costs no attempt")]
    public async Task An_attempt_that_ends_waiting_on_its_children_is_yielded_and_costs_the_item_nothing()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (outcomes, options) = NewOutcomes();
        options.CurrentValue.AiRole.MaxWaitSeconds = 86_400;
        var (item, attempt) = await SeedAsync(db, AttemptStatus.Running, WorkItemStatus.Processing);
        var children = await ChildrenAsync(db, item, 2);

        // Named in the opposite order to the one they were created in, so the order kept is the naming order.
        List<WorkItem> named = [children[1], children[0]];

        var wait = outcomes.Wait(db, item, attempt, named, WaitMode.All, until: null, Actors.ForAttempt(attempt), "the research comes first");
        await db.SaveChangesAsync(Ct);

        Assert.Equal(AttemptStatus.Yielded, attempt.Status);
        Assert.Equal(Noon, attempt.FinishedAt);
        Assert.Null(attempt.Error);
        Assert.Equal(WorkItemStatus.Waiting, item.Status);
        Assert.Equal(0, item.AttemptCount);
        Assert.Null(item.FinishedAt);
        Assert.Null(WorkItemQueries.LiveAttempt(item));

        var stored = await db.WorkItemWaits.AsNoTracking().Include(w => w.Items).SingleAsync(w => w.Id == wait.Id, Ct);
        Assert.Equal(item.Id, stored.WorkItemId);
        Assert.Equal(attempt.Id, stored.AttemptId);
        Assert.Equal(WaitMode.All, stored.Mode);
        Assert.Null(stored.Until);
        Assert.Equal(Noon.AddDays(1), stored.Deadline);
        Assert.Equal(WaitStatus.Open, stored.Status);
        Assert.Equal(Noon, stored.OpenedAt);
        Assert.Null(stored.ReleasedAt);
        Assert.Null(stored.ReleaseReason);
        Assert.Null(stored.ResumedByAttemptId);
        Assert.Equal(
            [$"0|{named[0].Id}", $"1|{named[1].Id}"],
            stored.Items.OrderBy(i => i.Position).Select(i => $"{i.Position}|{i.ChildWorkItemId}"));

        var entry = await SingleEntryAsync(db, JournalKinds.WorkItemWaiting);
        Assert.Equal(attempt.PublicId, entry.Key);
        Assert.Equal(attempt.PublicId, entry.AttemptId);
        Assert.Equal(item.PublicId, entry.WorkItemId);
        Assert.Equal("the research comes first", entry.Reason);
        Assert.Equal(
            $$"""{"on":["{{named[0].PublicId}}","{{named[1].PublicId}}"],"mode":"all","until":null,"deadline":"2026-09-15T12:00:00.000Z"}""",
            entry.New!.ToJsonString());

        // The yielded attempt is not live, so the item can be given another one.
        db.Attempts.Add(WorkItemFactory.NewAttempt(item, 2, AttemptStatus.Scheduled, Noon));
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>A wait for a moment alone names no items and no mode, and its deadline is the moment; a wait for both is bounded by the moment it named.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_wait_that_names_a_moment_is_over_by_that_moment(bool withChildren)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (outcomes, _) = NewOutcomes();
        var (item, attempt) = await SeedAsync(db, AttemptStatus.Running, WorkItemStatus.Processing);
        var children = withChildren ? await ChildrenAsync(db, item, 1) : [];
        var until = Noon.AddHours(3);

        var wait = outcomes.Wait(db, item, attempt, children, withChildren ? WaitMode.Any : null, until, Actors.ForAttempt(attempt));
        await db.SaveChangesAsync(Ct);

        Assert.Equal(until, wait.Until);
        Assert.Equal(until, wait.Deadline);
        Assert.Equal(withChildren ? WaitMode.Any : null, wait.Mode);
        var entry = await SingleEntryAsync(db, JournalKinds.WorkItemWaiting);
        Assert.Equal(withChildren ? "any" : null, (string?)entry.New!["mode"]);
        Assert.Equal("2026-09-14T15:00:00.000Z", (string?)entry.New["until"]);
        Assert.Equal("2026-09-14T15:00:00.000Z", (string?)entry.New["deadline"]);
        Assert.Equal(children.Count, entry.New["on"]!.AsArray().Count);
    }

    /// <summary>A wait that names nothing, a mode without items, items without a mode or one item twice is a caller's bug, and nothing moves.</summary>
    [Fact]
    public async Task A_wait_that_names_nothing_or_contradicts_itself_changes_nothing()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (outcomes, _) = NewOutcomes();
        var (item, attempt) = await SeedAsync(db, AttemptStatus.Running, WorkItemStatus.Processing);
        var children = await ChildrenAsync(db, item, 1);
        var actor = Actors.ForAttempt(attempt);

        Assert.Throws<ArgumentException>(() => outcomes.Wait(db, item, attempt, [], null, null, actor));
        Assert.Throws<ArgumentException>(() => outcomes.Wait(db, item, attempt, [], WaitMode.All, Noon.AddHours(1), actor));
        Assert.Throws<ArgumentException>(() => outcomes.Wait(db, item, attempt, children, null, null, actor));
        Assert.Throws<ArgumentException>(() => outcomes.Wait(db, item, attempt, [children[0], children[0]], WaitMode.All, null, actor));

        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Equal(WorkItemStatus.Processing, item.Status);
        Assert.Empty(db.ChangeTracker.Entries<WorkItemWait>());
        Assert.Empty(db.ChangeTracker.Entries<JournalEntry>());
    }

    /// <summary>
    /// A moment later than the ceiling allows is refused, never moved to the ceiling: a deadline silently changed is one the
    /// role still believes. The ceiling itself may be named.
    /// </summary>
    [Fact]
    public async Task A_wait_for_a_moment_beyond_the_ceiling_is_refused_and_never_clamped()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (outcomes, options) = NewOutcomes();
        options.CurrentValue.AiRole.MaxWaitSeconds = 3_600;
        var (item, attempt) = await SeedAsync(db, AttemptStatus.Running, WorkItemStatus.Processing);
        var actor = Actors.ForAttempt(attempt);

        var error = Assert.Throws<InvalidRequestException>(() => outcomes.Wait(db, item, attempt, [], null, Noon.AddSeconds(3_601), actor));

        Assert.Equal("wait_too_long", error.Code);
        Assert.Contains("3600", error.Message, StringComparison.Ordinal);
        AssertNothingMoved(db, item, attempt);

        var wait = outcomes.Wait(db, item, attempt, [], null, Noon.AddSeconds(3_600), actor);
        Assert.Equal(Noon.AddSeconds(3_600), wait.Deadline);
    }

    /// <summary>A wait names at most a hundred items; one that names more is refused whole, and a hundred are kept.</summary>
    [Fact]
    public async Task A_wait_on_more_than_a_hundred_items_is_refused()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (outcomes, _) = NewOutcomes();
        var (item, attempt) = await SeedAsync(db, AttemptStatus.Running, WorkItemStatus.Processing);
        var children = await ChildrenAsync(db, item, AttemptOutcomes.MaxWaitItems + 1);
        var actor = Actors.ForAttempt(attempt);

        var error = Assert.Throws<InvalidRequestException>(() => outcomes.Wait(db, item, attempt, children, WaitMode.All, null, actor));

        Assert.Equal("wait_target_invalid", error.Code);
        Assert.Contains("101", error.Message, StringComparison.Ordinal);
        AssertNothingMoved(db, item, attempt);

        var wait = outcomes.Wait(db, item, attempt, [.. children.Take(AttemptOutcomes.MaxWaitItems)], WaitMode.All, null, actor);
        Assert.Equal(100, wait.Items.Count);
    }

    /// <summary>
    /// An item that has already waited as often as <c>Dispatcher:AiRole:MaxWaits</c> allows is refused another wait; the
    /// attempt still holds the item and has to finish or fail. The count is of the item's yielded attempts, read live.
    /// </summary>
    [Fact]
    public async Task An_item_that_has_waited_as_often_as_allowed_is_refused_another_wait()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (outcomes, options) = NewOutcomes();
        options.CurrentValue.AiRole.MaxWaits = 2;
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var item = WorkItemFactory.NewAiRole(campaign, "planner", Noon, w => w.Status = WorkItemStatus.Processing);
        db.WorkItems.Add(item);
        foreach (var number in new[] { 1, 2 })
        {
            var yielded = WorkItemFactory.NewAttempt(item, number, AttemptStatus.Yielded, Noon.AddHours(number - 3));
            yielded.FinishedAt = Noon.AddHours(number - 3);
            db.Attempts.Add(yielded);
        }

        var attempt = WorkItemFactory.NewAttempt(item, 3, AttemptStatus.Running, Noon);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(Ct);
        var actor = Actors.ForAttempt(attempt);

        var error = Assert.Throws<ConflictException>(() => outcomes.Wait(db, item, attempt, [], null, Noon.AddHours(1), actor));

        Assert.Equal("wait_limit_reached", error.Code);
        Assert.Contains(item.PublicId, error.Message, StringComparison.Ordinal);
        AssertNothingMoved(db, item, attempt);

        options.CurrentValue.AiRole.MaxWaits = 3;
        outcomes.Wait(db, item, attempt, [], null, Noon.AddHours(1), actor);
        Assert.Equal(WorkItemStatus.Waiting, item.Status);
    }

    private static void AssertNothingMoved(JasonDbContext db, WorkItem item, Attempt attempt)
    {
        Assert.True(attempt.Status == AttemptStatus.Running, $"A refused wait left the attempt {attempt.Status}.");
        Assert.True(item.Status == WorkItemStatus.Processing, $"A refused wait left the item {item.Status}.");
        Assert.Empty(db.ChangeTracker.Entries<WorkItemWait>());
        Assert.Empty(db.ChangeTracker.Entries<JournalEntry>());
    }

    private static (AttemptOutcomes Outcomes, TestOptionsMonitor<Jason.Runtime.Configuration.DispatcherOptions> Options) NewOutcomes()
    {
        var clock = new FixedClock(Noon);
        var options = TestOptions.Dispatcher(o =>
        {
            o.RetryDelaySeconds = 60;
            o.AiRole.MaxAttempts = 2;
        });
        return (new AttemptOutcomes(new JournalWriter(clock), clock, TestOptions.Settings(options)), options);
    }

    private static async Task<(WorkItem Item, Attempt Attempt)> SeedAsync(JasonDbContext db, AttemptStatus attemptStatus, WorkItemStatus itemStatus)
    {
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var item = WorkItemFactory.NewAiRole(campaign, now: Noon, configure: w => w.Status = itemStatus);
        var attempt = WorkItemFactory.NewAttempt(item, 1, attemptStatus, Noon);
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(item);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(Ct);
        return (item, attempt);
    }

    private static async Task<List<WorkItem>> ChildrenAsync(JasonDbContext db, WorkItem parent, int count)
    {
        var children = Enumerable.Range(0, count)
            .Select(_ => WorkItemFactory.NewAiRole(parent.Campaign!, now: Noon))
            .ToList();
        db.WorkItems.AddRange(children);
        await db.SaveChangesAsync(Ct);
        return children;
    }

    private static async Task<JournalEntry> SingleEntryAsync(JasonDbContext db, string kind) =>
        Assert.Single(await db.Journal.AsNoTracking().Where(e => e.Kind == kind).ToListAsync(Ct));
}
