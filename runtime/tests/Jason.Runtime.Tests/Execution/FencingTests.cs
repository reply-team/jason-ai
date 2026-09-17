using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Execution;

/// <summary>
/// One attempt at a time owns a work item, and the runtime says so in SQL rather than by reading first and
/// hoping. Everything here is a way for a stale executor to try to write, and the same answer: stop.
/// </summary>
public class FencingTests
{
    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_attempt_that_was_replaced_cannot_write_and_does_not_disturb_the_one_that_replaced_it()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database);
        await using var ctx = database.Open();
        var service = NewService(ctx);
        var stale = new WorkItemHeartbeatRequest(seeded.ItemId, seeded.SupersededAttemptId);

        Assert.Equal("stale_attempt", (await Assert.ThrowsAsync<ConflictException>(() => service.HeartbeatAsync(stale, Ct))).Code);
        Assert.Equal("stale_attempt", (await Assert.ThrowsAsync<ConflictException>(
            () => service.SetResultAsync(new WorkItemSetResultRequest(seeded.ItemId, seeded.SupersededAttemptId, JsonNode.Parse("{}")), Ct))).Code);
        Assert.Equal("stale_attempt", (await Assert.ThrowsAsync<ConflictException>(
            () => service.CompleteAsync(new WorkItemCompleteRequest(seeded.ItemId, seeded.SupersededAttemptId, CompletionStatus.Succeeded, null, null, null), Ct))).Code);

        await using var check = database.Open();
        var live = await check.Attempts.AsNoTracking().SingleAsync(a => a.PublicId == seeded.LiveAttemptId, Ct);
        Assert.Null(live.LastHeartbeatAt);
        var item = await check.WorkItems.AsNoTracking().SingleAsync(w => w.PublicId == seeded.ItemId, Ct);
        Assert.Equal(WorkItemStatus.Processing, item.Status);
    }

    [Fact]
    public async Task An_attempt_of_another_item_is_not_this_item_s_attempt()
    {
        using var database = new TestDatabase();
        var first = await SeedAsync(database);
        var second = await SeedAsync(database);
        await using var ctx = database.Open();

        var error = await Assert.ThrowsAsync<ConflictException>(
            () => NewService(ctx).HeartbeatAsync(new WorkItemHeartbeatRequest(first.ItemId, second.LiveAttemptId), Ct));

        Assert.Equal("stale_attempt", error.Code);
    }

    [Fact]
    public async Task An_attempt_nobody_ever_claimed_is_stale_too()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database);
        await using var ctx = database.Open();

        var error = await Assert.ThrowsAsync<ConflictException>(
            () => NewService(ctx).HeartbeatAsync(new WorkItemHeartbeatRequest(seeded.ItemId, "att_01JASONNOTHERE"), Ct));

        Assert.Equal("stale_attempt", error.Code);
    }

    [Fact]
    public async Task A_running_attempt_whose_item_moved_on_cannot_write_either()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database);
        await using (var move = database.Open())
        {
            var item = await move.WorkItems.SingleAsync(w => w.PublicId == seeded.ItemId, Ct);
            WorkItemTransitions.Apply(item, WorkItemStatus.Cancelled, Noon);
            await move.SaveChangesAsync(Ct);
        }

        await using var ctx = database.Open();
        var error = await Assert.ThrowsAsync<ConflictException>(
            () => NewService(ctx).HeartbeatAsync(new WorkItemHeartbeatRequest(seeded.ItemId, seeded.LiveAttemptId), Ct));

        Assert.Equal("stale_attempt", error.Code);
    }

    [Fact]
    public async Task The_fence_is_one_guarded_statement_and_not_a_read_followed_by_a_write()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database);
        await using var ctx = database.Open();

        var sql = ExecutorService.FenceQuery(ctx, seeded.ItemId, seeded.LiveAttemptId).ToQueryString();

        Assert.Contains("WHERE", sql, StringComparison.Ordinal);
        Assert.Contains(seeded.ItemId, sql, StringComparison.Ordinal);
        Assert.Contains(seeded.LiveAttemptId, sql, StringComparison.Ordinal);
        Assert.Contains("'running'", sql, StringComparison.Ordinal);
        Assert.Contains("'processing'", sql, StringComparison.Ordinal);
    }

    private static ExecutorService NewService(JasonDbContext db)
    {
        var clock = new FixedClock(Noon);
        var settings = TestOptions.Settings();
        return new ExecutorService(db, clock, new AttemptOutcomes(new JournalWriter(clock), clock, settings), settings);
    }

    private static async Task<(string ItemId, string LiveAttemptId, string SupersededAttemptId)> SeedAsync(TestDatabase database)
    {
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var item = WorkItemFactory.NewAiRole(campaign, now: Noon, configure: w =>
        {
            w.Status = WorkItemStatus.Processing;
            w.AttemptCount = 1;
        });
        var superseded = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Failed, Noon);
        superseded.FinishedAt = Noon;
        superseded.Error = new AttemptErrorDto(AttemptErrors.LeaseExpired, "the lease ran out", Retriable: true);
        var live = WorkItemFactory.NewAttempt(item, 2, AttemptStatus.Running, Noon);
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(item);
        db.Attempts.AddRange(superseded, live);
        await db.SaveChangesAsync(Ct);
        return (item.PublicId, live.PublicId, superseded.PublicId);
    }
}
