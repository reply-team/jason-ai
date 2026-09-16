using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Execution;

public class ExecutorServiceTests
{
    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_heartbeat_records_liveness_and_says_when_the_next_one_is_due()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database);
        await using var ctx = database.Open();

        var response = await NewService(ctx).HeartbeatAsync(new WorkItemHeartbeatRequest(seeded.ItemId, seeded.AttemptId), Ct);

        Assert.Equal(seeded.ItemId, response.WorkItemId);
        Assert.Equal(seeded.AttemptId, response.AttemptId);
        Assert.Equal(new DateTimeOffset(Noon.AddSeconds(3600)), response.LockUntil);
        Assert.Equal(new DateTimeOffset(Noon.AddSeconds(240)), response.HeartbeatDueBy);

        await using var check = database.Open();
        var stored = await check.Attempts.AsNoTracking().SingleAsync(a => a.PublicId == seeded.AttemptId, Ct);
        Assert.Equal(Noon, stored.LastHeartbeatAt);
        Assert.Equal(Noon.AddSeconds(3600), stored.LockUntil);
    }

    [Fact]
    public async Task An_item_that_asked_for_no_heartbeats_gets_no_due_date()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database, configure: w => w.HeartbeatSeconds = 0);
        await using var ctx = database.Open();

        var response = await NewService(ctx).HeartbeatAsync(new WorkItemHeartbeatRequest(seeded.ItemId, seeded.AttemptId), Ct);

        Assert.Null(response.HeartbeatDueBy);
    }

    [Fact]
    public async Task A_heartbeat_without_its_ids_is_a_validation_failure()
    {
        using var database = new TestDatabase();
        await SeedAsync(database);
        await using var ctx = database.Open();

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => NewService(ctx).HeartbeatAsync(new WorkItemHeartbeatRequest(null, "  "), Ct));

        Assert.Equal(2, error.Details!.Count);
        Assert.Contains(error.Details, d => d.Field == "work_item_id" && d.Code == "required");
        Assert.Contains(error.Details, d => d.Field == "attempt_id" && d.Code == "required");
    }

    [Fact]
    public async Task Writing_a_result_stores_it_and_counts_as_liveness()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database);
        await using var ctx = database.Open();
        var clock = new FixedClock(Noon.AddMinutes(5));

        var dto = await NewService(ctx, clock).SetResultAsync(
            new WorkItemSetResultRequest(seeded.ItemId, seeded.AttemptId, JsonNode.Parse("""{"summary":"half way"}""")), Ct);

        Assert.Equal("half way", (string?)dto.Result!["summary"]);
        Assert.Equal(new DateTimeOffset(Noon.AddMinutes(5)), dto.UpdatedAt);
        Assert.Equal(WorkItemStatus.Processing, dto.Status);
        Assert.Equal(seeded.AttemptId, dto.CurrentAttemptId);

        await using var check = database.Open();
        var stored = await check.Attempts.AsNoTracking().SingleAsync(a => a.PublicId == seeded.AttemptId, Ct);
        Assert.Equal(Noon.AddMinutes(5), stored.LastHeartbeatAt);
        Assert.Empty(await check.Journal.AsNoTracking().Where(e => e.WorkItemId == seeded.ItemId).ToListAsync(Ct));
    }

    [Fact]
    public async Task A_null_result_clears_what_was_written_before()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database, configure: w => w.Result = JsonNode.Parse("""{"draft":1}"""));
        await using var ctx = database.Open();

        var dto = await NewService(ctx).SetResultAsync(new WorkItemSetResultRequest(seeded.ItemId, seeded.AttemptId, null), Ct);

        Assert.Null(dto.Result);
    }

    [Fact]
    public async Task A_result_larger_than_a_megabyte_is_refused()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database);
        await using var ctx = database.Open();
        var oversized = JsonValue.Create(new string('x', ExecutorService.MaxResultBytes + 1));

        var error = await Assert.ThrowsAsync<InvalidRequestException>(
            () => NewService(ctx).SetResultAsync(new WorkItemSetResultRequest(seeded.ItemId, seeded.AttemptId, oversized), Ct));

        Assert.Equal("result_too_large", error.Code);
        Assert.False(error.Retryable);
    }

    [Fact]
    public async Task Completing_with_success_finishes_the_item_in_the_attempt_s_name()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database);
        await using var ctx = database.Open();

        var dto = await NewService(ctx).CompleteAsync(
            new WorkItemCompleteRequest(seeded.ItemId, seeded.AttemptId, CompletionStatus.Succeeded, JsonNode.Parse("""{"found":3}"""), null, "all sources read"),
            Ct);

        Assert.Equal(WorkItemStatus.Succeeded, dto.Status);
        Assert.Equal(3, (int?)dto.Result!["found"]);
        Assert.Equal(new DateTimeOffset(Noon), dto.FinishedAt);
        Assert.Null(dto.CurrentAttemptId);
        var reported = Assert.Single(dto.Attempts!);
        Assert.Equal(AttemptStatus.Succeeded, reported.Status);

        await using var check = database.Open();
        var entry = Assert.Single(await check.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.WorkItemSucceeded).ToListAsync(Ct));
        Assert.Equal(ActorType.Attempt, entry.ActorType);
        Assert.Equal(seeded.AttemptId, entry.ActorId);
        Assert.Equal("all sources read", entry.Reason);
    }

    [Fact]
    public async Task A_failure_the_runtime_does_not_repeat_ends_the_item()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database);
        await using var ctx = database.Open();

        var dto = await NewService(ctx).CompleteAsync(
            new WorkItemCompleteRequest(seeded.ItemId, seeded.AttemptId, CompletionStatus.Failed, null, new CompletionErrorDto("bad_input", "the brief is unreadable", null), null),
            Ct);

        Assert.Equal(WorkItemStatus.Failed, dto.Status);
        Assert.Equal("bad_input", dto.LastError!.Code);
        Assert.False(dto.LastError.Retriable);
        Assert.Null(dto.LastError.Trace);
        Assert.Equal(1, dto.AttemptCount);
    }

    [Fact]
    public async Task A_transient_failure_hands_the_item_back_for_another_attempt()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database);
        await using var ctx = database.Open();

        var dto = await NewService(ctx).CompleteAsync(
            new WorkItemCompleteRequest(seeded.ItemId, seeded.AttemptId, CompletionStatus.Failed, null, new CompletionErrorDto("rate_limited", "slow down", null), null),
            Ct);

        Assert.Equal(WorkItemStatus.Created, dto.Status);
        Assert.Null(dto.LastError);
        Assert.Equal(1, dto.AttemptCount);

        await using var check = database.Open();
        var stored = await check.Attempts.AsNoTracking().SingleAsync(a => a.PublicId == seeded.AttemptId, Ct);
        Assert.Equal(AttemptStatus.Failed, stored.Status);
        Assert.True(stored.Error!.Retriable);
    }

    [Fact]
    public async Task A_reported_failure_has_to_say_what_went_wrong()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database);
        await using var ctx = database.Open();

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => NewService(ctx).CompleteAsync(new WorkItemCompleteRequest(seeded.ItemId, seeded.AttemptId, CompletionStatus.Failed, null, null, null), Ct));

        Assert.Contains(error.Details!, d => d.Field == "error.code" && d.Code == "required");
        Assert.Contains(error.Details!, d => d.Field == "error.message" && d.Code == "required");
    }

    [Fact]
    public async Task An_error_code_that_is_not_a_code_is_refused()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database);
        await using var ctx = database.Open();

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => NewService(ctx).CompleteAsync(
                new WorkItemCompleteRequest(seeded.ItemId, seeded.AttemptId, CompletionStatus.Failed, null, new CompletionErrorDto("Rate Limited", "slow down", null), null),
                Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("error.code", detail.Field);
        Assert.Equal("invalid", detail.Code);
    }

    [Fact]
    public async Task Succeeding_and_reporting_an_error_at_the_same_time_is_refused()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database);
        await using var ctx = database.Open();

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => NewService(ctx).CompleteAsync(
                new WorkItemCompleteRequest(seeded.ItemId, seeded.AttemptId, CompletionStatus.Succeeded, null, new CompletionErrorDto("bad_input", "unreadable", null), null),
                Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("error", detail.Field);
        Assert.Equal("not_allowed", detail.Code);
    }

    [Fact]
    public async Task Completing_without_a_status_is_a_validation_failure()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database);
        await using var ctx = database.Open();

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => NewService(ctx).CompleteAsync(new WorkItemCompleteRequest(seeded.ItemId, seeded.AttemptId, null, null, null, null), Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("status", detail.Field);
        Assert.Equal("required", detail.Code);
    }

    [Fact]
    public async Task Completing_an_attempt_twice_tells_the_second_caller_to_stop()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database);
        var request = new WorkItemCompleteRequest(seeded.ItemId, seeded.AttemptId, CompletionStatus.Succeeded, null, null, null);
        await using (var first = database.Open())
        {
            await NewService(first).CompleteAsync(request, Ct);
        }

        await using var second = database.Open();
        var error = await Assert.ThrowsAsync<ConflictException>(() => NewService(second).CompleteAsync(request, Ct));

        Assert.Equal("stale_attempt", error.Code);
        Assert.False(error.Retryable);
    }

    private static ExecutorService NewService(JasonDbContext db, FixedClock? clock = null)
    {
        var time = clock ?? new FixedClock(Noon);
        var settings = TestOptions.Settings(o => o.RetryDelaySeconds = 0);
        return new ExecutorService(db, time, new AttemptOutcomes(new JournalWriter(time), time, settings), settings);
    }

    private static async Task<(string ItemId, string AttemptId)> SeedAsync(TestDatabase database, Action<WorkItem>? configure = null)
    {
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var item = WorkItemFactory.NewAiRole(campaign, now: Noon, configure: w =>
        {
            w.Status = WorkItemStatus.Processing;
            configure?.Invoke(w);
        });
        var attempt = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Running, Noon);
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(item);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(Ct);
        return (item.PublicId, attempt.PublicId);
    }
}
