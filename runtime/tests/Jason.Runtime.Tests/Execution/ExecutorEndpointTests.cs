using System.Net;
using Jason.Contracts.Api;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Tests.Execution;

public class ExecutorEndpointTests
{
    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_heartbeat_answers_the_lease_and_the_next_due_moment()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Noon));
        var seeded = Seed(api);

        var (status, body) = await api.PostAsync(
            Operations.WorkItemHeartbeat, new { WorkItemId = seeded.ItemId, AttemptId = seeded.AttemptId }, Ct);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("\"lock_until\":\"2026-09-14T13:00:00.000Z\"", body, StringComparison.Ordinal);
        Assert.Contains("\"heartbeat_due_by\":\"2026-09-14T12:04:00.000Z\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_result_written_through_the_api_comes_back_on_the_item()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Noon));
        var seeded = Seed(api);

        var dto = await api.PostOkAsync<WorkItemDto>(
            Operations.WorkItemSetResult,
            new { WorkItemId = seeded.ItemId, AttemptId = seeded.AttemptId, Result = new { Summary = "half way" } },
            Ct);

        Assert.Equal(WorkItemStatus.Processing, dto.Status);
        Assert.Equal("half way", (string?)dto.Result!["summary"]);
    }

    [Fact]
    public async Task Completing_an_attempt_finishes_the_item_over_http()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Noon));
        var seeded = Seed(api);

        var (status, body) = await api.PostAsync(
            Operations.WorkItemComplete,
            new { WorkItemId = seeded.ItemId, AttemptId = seeded.AttemptId, Status = "succeeded", Result = new { Found = 3 } },
            Ct);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("\"status\":\"succeeded\"", body, StringComparison.Ordinal);
        Assert.Contains("\"current_attempt_id\":null", body, StringComparison.Ordinal);
        Assert.Contains("\"finished_at\":\"2026-09-14T12:00:00.000Z\"", body, StringComparison.Ordinal);
        Assert.Contains("\"found\":3", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_attempt_that_no_longer_owns_the_item_is_told_to_stop()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Noon));
        var seeded = Seed(api);
        await api.PostOkAsync<WorkItemDto>(
            Operations.WorkItemComplete,
            new { WorkItemId = seeded.ItemId, AttemptId = seeded.AttemptId, Status = "succeeded" },
            Ct);

        var error = await api.PostErrorAsync(
            Operations.WorkItemHeartbeat,
            new { WorkItemId = seeded.ItemId, AttemptId = seeded.AttemptId },
            HttpStatusCode.Conflict,
            Ct);

        Assert.Equal("stale_attempt", error.Code);
        Assert.False(error.Retryable);
    }

    [Fact]
    public async Task A_call_without_its_ids_names_the_missing_fields()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Noon));

        var error = await api.PostErrorAsync(Operations.WorkItemSetResult, new { }, HttpStatusCode.BadRequest, Ct);

        Assert.Equal("validation_failed", error.Code);
        Assert.Contains(error.Details!, d => d.Field == "work_item_id" && d.Code == "required");
    }

    private static (string ItemId, string AttemptId) Seed(RuntimeApiFixture api)
    {
        using var db = new JasonDbContext(JasonDbContext.CreateOptions(api.Paths.DatabaseFile));
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var item = WorkItemFactory.NewAiRole(campaign, now: Noon, configure: w => w.Status = WorkItemStatus.Processing);
        var attempt = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Running, Noon);
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(item);
        db.Attempts.Add(attempt);
        db.SaveChanges();
        return (item.PublicId, attempt.PublicId);
    }
}
