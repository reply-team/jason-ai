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

    private static async Task<JournalEntry> SingleEntryAsync(JasonDbContext db, string kind) =>
        Assert.Single(await db.Journal.AsNoTracking().Where(e => e.Kind == kind).ToListAsync(Ct));
}
