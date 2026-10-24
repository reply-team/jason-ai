using Jason.Contracts.Api;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Jason.Runtime.Tests.WorkItems;

/// <summary>
/// <c>workitem.resume</c>, and the <c>wait</c> block a waiting item is read with: any named actor may end a wait before
/// what it waits for has happened, the line that says so is theirs and carries their reason, and only a waiting item can
/// be resumed.
/// </summary>
public class WorkItemResumeTests
{
    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly ActorRef Marta = new(ActorType.Human, "marta");

    [Fact]
    [Critical(Family.W2, "a person may end a wait")]
    public async Task A_person_ends_a_wait_and_the_item_goes_back_to_the_queue()
    {
        using var database = new TestDatabase();
        var seeded = await SeedWaitingAsync(database);
        await using var ctx = database.Open();

        var dto = await NewService(ctx, Noon.AddHours(1)).ResumeAsync(new WorkItemResumeRequest(seeded.ItemId, Marta, "  go on without it  "), Ct);

        Assert.Equal(WorkItemStatus.Created, dto.Status);
        Assert.Null(dto.Wait);

        await using var check = database.Open();
        var wait = await check.WorkItemWaits.AsNoTracking().SingleAsync(Ct);
        Assert.Equal((WaitStatus.Released, WaitReleaseReason.ResumedByActor), (wait.Status, wait.ReleaseReason));
        Assert.Equal(Noon.AddHours(1), wait.ReleasedAt);
        Assert.Null(wait.ResumedByAttemptId);

        var line = await check.Journal.AsNoTracking().SingleAsync(e => e.WorkItemId == seeded.ItemId && e.Kind == JournalKinds.WorkItemResumed, Ct);
        Assert.Equal((ActorType.Human, "marta"), (line.ActorType, line.ActorId));
        Assert.Equal("go on without it", line.Reason);
        Assert.Equal(seeded.AttemptId, line.Key);
        Assert.Equal(seeded.AttemptId, line.AttemptId);
        Assert.Equal("""{"because":"resumed_by_actor"}""", line.New!.ToJsonString());
    }

    [Theory]
    [InlineData(WorkItemStatus.Processing)]
    [InlineData(WorkItemStatus.Created)]
    [InlineData(WorkItemStatus.Succeeded)]
    public async Task Only_a_waiting_item_can_be_resumed(WorkItemStatus status)
    {
        using var database = new TestDatabase();
        string itemId;
        await using (var db = database.Open())
        {
            var campaign = WorkItemFactory.NewCampaign(now: Noon);
            var item = WorkItemFactory.NewAiRole(campaign, "planner", Noon, w => w.Status = status);
            db.Campaigns.Add(campaign);
            db.WorkItems.Add(item);
            await db.SaveChangesAsync(Ct);
            itemId = item.PublicId;
        }

        await using var ctx = database.Open();
        var error = await Assert.ThrowsAsync<ConflictException>(
            () => NewService(ctx, Noon).ResumeAsync(new WorkItemResumeRequest(itemId, Marta, null), Ct));

        Assert.Equal("workitem_not_waiting", error.Code);
        Assert.Contains(SnakeCaseEnumConverter<WorkItemStatus>.Format(status), error.Message, StringComparison.Ordinal);
        await using var check = database.Open();
        Assert.Empty(await check.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.WorkItemResumed).ToListAsync(Ct));
    }

    [Fact]
    public async Task Resuming_twice_is_refused_the_second_time_with_where_the_item_is()
    {
        using var database = new TestDatabase();
        var seeded = await SeedWaitingAsync(database);
        await using (var first = database.Open())
        {
            await NewService(first, Noon).ResumeAsync(new WorkItemResumeRequest(seeded.ItemId, Marta, null), Ct);
        }

        await using var second = database.Open();
        var error = await Assert.ThrowsAsync<ConflictException>(
            () => NewService(second, Noon).ResumeAsync(new WorkItemResumeRequest(seeded.ItemId, Marta, null), Ct));

        Assert.Equal("workitem_not_waiting", error.Code);
        Assert.Contains("created", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_resume_names_who_asks_for_it()
    {
        using var database = new TestDatabase();
        var seeded = await SeedWaitingAsync(database);
        await using var ctx = database.Open();

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => NewService(ctx, Noon).ResumeAsync(new WorkItemResumeRequest(seeded.ItemId, null, "go on"), Ct));

        Assert.Equal("validation_failed", error.Code);
        Assert.True(error.Details!.Any(d => d.Field == "actor" && d.Code == "required"), $"the refusal names {string.Join(", ", error.Details!.Select(d => $"{d.Field}/{d.Code}"))}");
        await using var check = database.Open();
        Assert.Equal(WaitStatus.Open, (await check.WorkItemWaits.AsNoTracking().SingleAsync(Ct)).Status);
        Assert.Empty(await check.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.WorkItemResumed).ToListAsync(Ct));
    }

    [Fact]
    public async Task A_resume_that_meets_the_scan_s_release_between_its_read_and_its_write_is_refused_and_the_release_stands()
    {
        using var database = new TestDatabase();
        var seeded = await SeedWaitingAsync(database);

        // The scan releases the wait, through its own context, at the moment the resume is about to write.
        var scan = new Racing(async () =>
        {
            await using var other = database.Open();
            var item = await other.WorkItems.SingleAsync(w => w.PublicId == seeded.ItemId, Ct);
            var wait = (await OpenWaits.OfAsync(other, item.Id, Ct))!;
            var clock = new FixedClock(Noon.AddMinutes(30));
            OpenWaits.Release(other, new JournalWriter(clock), wait, item, WaitReleaseReason.ChildrenEnded, Actors.Dispatcher, reason: null, Noon.AddMinutes(30));
            await other.SaveChangesAsync(Ct);
        });
        await using var ctx = new JasonDbContext(new DbContextOptionsBuilder<JasonDbContext>(database.Options).AddInterceptors(scan).Options);

        var error = await Assert.ThrowsAsync<ConflictException>(
            () => NewService(ctx, Noon.AddHours(1)).ResumeAsync(new WorkItemResumeRequest(seeded.ItemId, Marta, "go on"), Ct));

        Assert.True(scan.Fired, "the scan's release never ran before the resume wrote");
        Assert.Equal("workitem_not_waiting", error.Code);
        Assert.True(error.Message.Contains("created", StringComparison.Ordinal), $"the refusal says: {error.Message}");
        await using var check = database.Open();
        var released = await check.WorkItemWaits.AsNoTracking().SingleAsync(Ct);
        Assert.Equal((WaitStatus.Released, WaitReleaseReason.ChildrenEnded), (released.Status, released.ReleaseReason));
        var line = Assert.Single(await check.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.WorkItemResumed).ToListAsync(Ct));
        Assert.Equal((ActorType.System, "dispatcher"), (line.ActorType, line.ActorId));
    }

    [Fact]
    public async Task An_unknown_item_is_not_found()
    {
        using var database = new TestDatabase();
        await using var ctx = database.Open();

        var error = await Assert.ThrowsAsync<NotFoundException>(
            () => NewService(ctx, Noon).ResumeAsync(new WorkItemResumeRequest("wi_01J00000000000000000000000", Marta, null), Ct));

        Assert.Equal("work_item_not_found", error.Code);
    }

    [Fact]
    public async Task A_waiting_item_is_read_with_what_it_waits_for_singly_and_counted_in_a_listing()
    {
        using var database = new TestDatabase();
        var seeded = await SeedWaitingAsync(database);
        await using var ctx = database.Open();
        var service = NewService(ctx, Noon);

        var got = (await service.GetAsync(new WorkItemGetRequest(seeded.ItemId, null), Ct)).Wait!;
        var listed = await service.ListAsync(new WorkItemListRequest(null, null, null, null, null, null, null, null), Ct);

        Assert.Equal(seeded.AttemptId, got.AttemptId);
        Assert.Equal(WaitMode.All, got.Mode);
        Assert.Equal(new DateTimeOffset(Noon.AddDays(3)), got.Until);
        Assert.Equal(new DateTimeOffset(Noon), got.OpenedAt);
        Assert.Equal([(seeded.ChildId, WorkItemStatus.Processing)], got.On.Select(i => (i.WorkItemId, i.Status)));

        var row = Assert.Single(listed.Items, i => i.Id == seeded.ItemId);
        Assert.Equal(
            new WaitSummaryDto(got.AttemptId, got.Mode, got.Until, got.Deadline, got.OpenedAt, OnCount: 1, EndedCount: 0),
            row.Wait);
        Assert.Null(Assert.Single(listed.Items, i => i.Id == seeded.ChildId).Wait);

        // Once the item it names has ended, the listing counts it as ended.
        await ctx.WorkItems.Where(w => w.PublicId == seeded.ChildId).ExecuteUpdateAsync(u => u.SetProperty(w => w.Status, WorkItemStatus.Succeeded), Ct);
        var relisted = await service.ListAsync(new WorkItemListRequest(null, null, null, null, null, null, null, null), Ct);
        var ended = Assert.Single(relisted.Items, i => i.Id == seeded.ItemId).Wait!;
        Assert.Equal((1, 1), (ended.OnCount, ended.EndedCount));
    }

    private static WorkItemService NewService(JasonDbContext db, DateTime now)
    {
        var clock = new FixedClock(now);
        return new WorkItemService(db, new JournalWriter(clock), clock, TestCanceller.New(clock), TestOptions.PluginSettings());
    }

    private sealed record Seeded(string ItemId, string AttemptId, string ChildId);

    /// <summary>Another writer that runs once, the first time the context is about to save, and commits before that save does.</summary>
    private sealed class Racing(Func<Task> write) : SaveChangesInterceptor
    {
        public bool Fired { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!Fired)
            {
                Fired = true;
                await write().ConfigureAwait(false);
            }

            return await base.SavingChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>A planner whose first run ended waiting, on all of one researcher it raised and three days at the latest.</summary>
    private static async Task<Seeded> SeedWaitingAsync(TestDatabase database)
    {
        await using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var item = WorkItemFactory.NewAiRole(campaign, "planner", Noon, w => w.Status = WorkItemStatus.Processing);
        var attempt = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Running, Noon);
        var child = WorkItemFactory.NewAiRole(campaign, "researcher", Noon, w => w.Status = WorkItemStatus.Processing);
        db.Campaigns.Add(campaign);
        db.WorkItems.AddRange(item, child);
        await db.SaveChangesAsync(Ct);

        new AttemptOutcomes(new JournalWriter(clock), clock, TestOptions.Settings())
            .Wait(db, item, attempt, [child], WaitMode.All, Noon.AddDays(3), Actors.ForAttempt(attempt));
        await db.SaveChangesAsync(Ct);
        return new Seeded(item.PublicId, attempt.PublicId, child.PublicId);
    }
}
