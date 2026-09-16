using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.WorkItems;

public class WorkItemUpdateTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_changed_scalar_is_one_entry_naming_the_field_it_moved()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var item = Seed(db, configure: null);
        var service = NewService(db, new FixedClock(Noon));

        var updated = await service.UpdateAsync(Patch(item.PublicId) with { Priority = Optional<int>.Of(5) }, Ct);

        Assert.Equal(5, updated.Priority);
        var entry = Assert.Single(await db.Journal.ToListAsync(Ct));
        Assert.Equal(JournalKinds.WorkItemUpdated, entry.Kind);
        Assert.Equal("priority", entry.Key);
        Assert.Equal(0, (int?)entry.Old);
        Assert.Equal(5, (int?)entry.New);
        Assert.Equal(item.PublicId, entry.WorkItemId);
    }

    [Fact]
    public async Task A_context_edit_writes_one_entry_per_key_and_never_the_campaign_kind()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var item = Seed(db, w => w.Context = new JsonObject { ["brief"] = "old", ["drop"] = 1 });
        var service = NewService(db, new FixedClock(Noon));

        var updated = await service.UpdateAsync(
            Patch(item.PublicId) with { Set = new JsonObject { ["brief"] = "new" }, Unset = ["drop"] },
            Ct);

        Assert.Equal("new", (string?)updated.Context["brief"]);
        Assert.False(updated.Context.ContainsKey("drop"));
        var entries = await db.Journal.OrderBy(e => e.Id).ToListAsync(Ct);
        Assert.Equal(2, entries.Count);
        Assert.All(entries, entry => Assert.Equal(JournalKinds.WorkItemContextUpdated, entry.Kind));
        Assert.Equal("brief", entries[0].Key);
        Assert.Equal("old", (string?)entries[0].Old);
        Assert.Equal("new", (string?)entries[0].New);
        Assert.Equal("drop", entries[1].Key);
        Assert.Null(entries[1].New);
    }

    [Fact]
    public async Task An_explicit_null_clears_an_override_and_says_so()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var item = Seed(db, w => w.TimeoutSeconds = 60);
        var service = NewService(db, new FixedClock(Noon));

        var updated = await service.UpdateAsync(Patch(item.PublicId) with { TimeoutSeconds = Optional<int?>.Of(null) }, Ct);

        Assert.Null(updated.TimeoutSeconds);
        var entry = Assert.Single(await db.Journal.ToListAsync(Ct));
        Assert.Equal("timeout_seconds", entry.Key);
        Assert.Equal(60, (int?)entry.Old);
        Assert.Null(entry.New);
    }

    [Fact]
    public async Task A_patch_that_changes_nothing_writes_nothing()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var item = Seed(db, w => w.Priority = 5);
        var service = NewService(db, new FixedClock(Noon));

        var updated = await service.UpdateAsync(Patch(item.PublicId) with { Priority = Optional<int>.Of(5) }, Ct);

        Assert.Equal(5, updated.Priority);
        Assert.Empty(await db.Journal.ToListAsync(Ct));
    }

    [Fact]
    public async Task A_provider_operation_still_takes_no_result_format()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign();
        var item = WorkItemFactory.NewProviderOp(campaign);
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);
        var service = NewService(db, new FixedClock(Noon));

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.UpdateAsync(Patch(item.PublicId) with { ResultFormat = Optional<JsonNode?>.Of(new JsonObject()) }, Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("result_format", detail.Field);
        Assert.Equal("not_allowed", detail.Code);
    }

    [Fact]
    public async Task A_patch_that_would_close_the_window_before_it_opens_is_refused()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var item = Seed(db, w => w.NotBefore = Noon.UtcDateTime.AddHours(3));
        var service = NewService(db, new FixedClock(Noon));

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.UpdateAsync(Patch(item.PublicId) with { DueAt = Optional<DateTimeOffset?>.Of(Noon.AddHours(1)) }, Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("due_at", detail.Field);
        Assert.Equal("due_before_start", detail.Code);
    }

    [Fact]
    public async Task A_finished_item_is_not_patched()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var item = Seed(db, w => w.Status = WorkItemStatus.Succeeded);
        var service = NewService(db, new FixedClock(Noon));

        var error = await Assert.ThrowsAsync<ConflictException>(
            () => service.UpdateAsync(Patch(item.PublicId) with { Priority = Optional<int>.Of(5) }, Ct));

        Assert.Equal("workitem_terminal", error.Code);
    }

    [Fact]
    public async Task A_context_edit_on_a_claimed_item_leaves_the_running_attempt_with_what_it_was_told()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign();
        var item = WorkItemFactory.NewAiRole(campaign, configure: w =>
        {
            w.Context = new JsonObject { ["brief"] = "as claimed" };
            w.Status = WorkItemStatus.Processing;
        });
        var attempt = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Running, Noon.UtcDateTime);
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);
        var service = NewService(db, new FixedClock(Noon));

        var updated = await service.UpdateAsync(
            Patch(item.PublicId) with { Set = new JsonObject { ["brief"] = "changed mid-flight" } },
            Ct);

        Assert.Equal("changed mid-flight", (string?)updated.Context["brief"]);
        Assert.Equal(WorkItemStatus.Processing, updated.Status);
        var reloaded = await db.Attempts.AsNoTracking().SingleAsync(a => a.PublicId == attempt.PublicId, Ct);
        Assert.Equal("as claimed", (string?)reloaded.ContextSnapshot["brief"]);
    }

    [Fact]
    public async Task An_expired_item_given_a_future_deadline_is_work_again()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var item = Seed(db, w =>
        {
            w.Status = WorkItemStatus.Expired;
            w.DueAt = Noon.UtcDateTime.AddHours(-1);
            w.FinishedAt = Noon.UtcDateTime;
        });
        var service = NewService(db, new FixedClock(Noon));

        var updated = await service.UpdateAsync(Patch(item.PublicId) with { DueAt = Optional<DateTimeOffset?>.Of(Noon.AddHours(4)) }, Ct);

        Assert.Equal(WorkItemStatus.Created, updated.Status);
        Assert.Null(updated.FinishedAt);
        Assert.True(updated.Eligible);
        var entries = await db.Journal.OrderBy(e => e.Id).ToListAsync(Ct);
        Assert.Equal(2, entries.Count);
        Assert.Equal(JournalKinds.WorkItemUpdated, entries[0].Kind);
        Assert.Equal("due_at", entries[0].Key);
        Assert.Equal(JournalKinds.WorkItemReopened, entries[1].Kind);
        Assert.Equal("status", entries[1].Key);
        Assert.Equal("expired", (string?)entries[1].Old);
        Assert.Equal("created", (string?)entries[1].New);
    }

    [Fact]
    public async Task An_expired_item_given_a_deadline_already_past_is_refused()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var item = Seed(db, w =>
        {
            w.Status = WorkItemStatus.Expired;
            w.DueAt = Noon.UtcDateTime.AddHours(-2);
            w.FinishedAt = Noon.UtcDateTime;
        });
        var service = NewService(db, new FixedClock(Noon));

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.UpdateAsync(Patch(item.PublicId) with { DueAt = Optional<DateTimeOffset?>.Of(Noon.AddHours(-1)) }, Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("due_at", detail.Field);
        Assert.Equal("in_the_past", detail.Code);
    }

    [Fact]
    public async Task An_expired_item_repriorized_alone_stays_expired()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var item = Seed(db, w =>
        {
            w.Status = WorkItemStatus.Expired;
            w.DueAt = Noon.UtcDateTime.AddHours(-2);
            w.FinishedAt = Noon.UtcDateTime;
        });
        var service = NewService(db, new FixedClock(Noon));

        var updated = await service.UpdateAsync(Patch(item.PublicId) with { Priority = Optional<int>.Of(7) }, Ct);

        Assert.Equal(WorkItemStatus.Expired, updated.Status);
        Assert.Equal(7, updated.Priority);
        var entry = Assert.Single(await db.Journal.ToListAsync(Ct));
        Assert.Equal(JournalKinds.WorkItemUpdated, entry.Kind);
    }

    private static WorkItemService NewService(JasonDbContext db, TimeProvider clock) =>
        new(db, new JournalWriter(clock), clock, NewCanceller(clock), TestOptions.Plugins());

    private static WorkItemCanceller NewCanceller(TimeProvider clock) => TestCanceller.New(clock);

    private static WorkItemUpdateRequest Patch(string workItemId) =>
        new(workItemId, null, null, default, default, default, default, default, default, default, null, null);

    private static WorkItem Seed(JasonDbContext db, Action<WorkItem>? configure)
    {
        var item = WorkItemFactory.NewAiRole(WorkItemFactory.NewCampaign(), configure: configure);
        db.WorkItems.Add(item);
        db.SaveChanges();
        return item;
    }
}
