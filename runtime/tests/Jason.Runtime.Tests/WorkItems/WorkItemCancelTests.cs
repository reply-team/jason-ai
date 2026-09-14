using Jason.Contracts.Api;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.WorkItems;

public class WorkItemCancelTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(WorkItemStatus.Created)]
    [InlineData(WorkItemStatus.Expired)]
    public async Task An_item_nobody_is_running_is_cancelled_and_finished(WorkItemStatus status)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var item = Seed(db, w => w.Status = status);
        var service = NewService(db, new FixedClock(Noon), new RunningAttemptRegistry());

        var cancelled = await service.CancelAsync(new WorkItemCancelRequest(item.PublicId, null, "no longer needed"), Ct);

        Assert.Equal(WorkItemStatus.Cancelled, cancelled.Status);
        Assert.Equal(Noon, cancelled.FinishedAt);
        var entry = Assert.Single(await db.Journal.ToListAsync(Ct));
        Assert.Equal(JournalKinds.WorkItemCancelled, entry.Kind);
        Assert.Equal("status", entry.Key);
        Assert.Equal(SnakeCaseEnumConverter<WorkItemStatus>.Format(status), (string?)entry.Old);
        Assert.Equal("no longer needed", entry.Reason);
        Assert.Null(entry.AttemptId);
    }

    [Fact]
    public async Task A_scheduled_attempt_is_cancelled_with_its_item_and_never_counted()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign();
        var item = WorkItemFactory.NewAiRole(campaign, configure: w =>
        {
            w.Status = WorkItemStatus.Scheduled;
            w.AttemptCount = 1;
        });
        var attempt = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Scheduled, Noon.UtcDateTime);
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);
        var service = NewService(db, new FixedClock(Noon), new RunningAttemptRegistry());

        var cancelled = await service.CancelAsync(new WorkItemCancelRequest(item.PublicId, null, null), Ct);

        Assert.Equal(WorkItemStatus.Cancelled, cancelled.Status);
        Assert.Equal(1, cancelled.AttemptCount);
        Assert.Null(cancelled.CurrentAttemptId);
        var stored = await db.Attempts.AsNoTracking().SingleAsync(a => a.PublicId == attempt.PublicId, Ct);
        Assert.Equal(AttemptStatus.Cancelled, stored.Status);
        Assert.Equal("cancelled", stored.Error!.Code);
        var entry = Assert.Single(await db.Journal.ToListAsync(Ct));
        Assert.Equal(attempt.PublicId, entry.AttemptId);
    }

    [Fact]
    public async Task Cancelling_a_running_item_asks_the_handler_to_stop_its_child_once()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign();
        var item = WorkItemFactory.NewAiRole(campaign, configure: w => w.Status = WorkItemStatus.Processing);
        var attempt = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Running, Noon.UtcDateTime);
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);

        var registry = new RunningAttemptRegistry();
        var kills = 0;
        using var registration = registry.Register(attempt.PublicId, () => kills++);
        var service = NewService(db, new FixedClock(Noon), registry);

        await service.CancelAsync(new WorkItemCancelRequest(item.PublicId, null, null), Ct);

        Assert.Equal(1, kills);
    }

    [Fact]
    public async Task Cancelling_an_already_cancelled_item_changes_nothing_and_records_nothing()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var item = Seed(db, w =>
        {
            w.Status = WorkItemStatus.Cancelled;
            w.FinishedAt = Noon.UtcDateTime.AddHours(-1);
        });
        var service = NewService(db, new FixedClock(Noon), new RunningAttemptRegistry());

        var cancelled = await service.CancelAsync(new WorkItemCancelRequest(item.PublicId, null, "again"), Ct);

        Assert.Equal(WorkItemStatus.Cancelled, cancelled.Status);
        Assert.Equal(Noon.AddHours(-1), cancelled.FinishedAt);
        Assert.Empty(await db.Journal.ToListAsync(Ct));
    }

    [Theory]
    [InlineData(WorkItemStatus.Succeeded)]
    [InlineData(WorkItemStatus.Failed)]
    public async Task A_finished_item_is_not_cancelled(WorkItemStatus status)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var item = Seed(db, w => w.Status = status);
        var service = NewService(db, new FixedClock(Noon), new RunningAttemptRegistry());

        var error = await Assert.ThrowsAsync<ConflictException>(
            () => service.CancelAsync(new WorkItemCancelRequest(item.PublicId, null, null), Ct));

        Assert.Equal("workitem_terminal", error.Code);
    }

    [Fact]
    public async Task An_unknown_item_cannot_be_cancelled()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon), new RunningAttemptRegistry());

        var error = await Assert.ThrowsAsync<NotFoundException>(
            () => service.CancelAsync(new WorkItemCancelRequest("wi_01JASONNOTHERE", null, null), Ct));

        Assert.Equal("work_item_not_found", error.Code);
    }

    private static WorkItemService NewService(JasonDbContext db, TimeProvider clock, RunningAttemptRegistry registry) =>
        new(db, new JournalWriter(clock), clock, TestCanceller.New(clock, registry));

    private static WorkItem Seed(JasonDbContext db, Action<WorkItem> configure)
    {
        var item = WorkItemFactory.NewAiRole(WorkItemFactory.NewCampaign(), configure: configure);
        db.WorkItems.Add(item);
        db.SaveChanges();
        return item;
    }
}
