using Jason.Contracts.Api;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Dispatch;

public class ExpirerTests
{
    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_item_nobody_claimed_in_time_expires()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var item = WorkItemFactory.NewAiRole(campaign, now: Noon, configure: w => w.DueAt = Noon.AddMinutes(-1));
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);

        var expired = await NewExpirer().ExpireAsync(db, Ct);

        Assert.Equal(1, expired);
        Assert.Equal(WorkItemStatus.Expired, item.Status);
        Assert.Equal(Noon, item.FinishedAt);

        var entry = Assert.Single(await db.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.WorkItemExpired).ToListAsync(Ct));
        Assert.Equal(ActorType.System, entry.ActorType);
        Assert.Equal("dispatcher", entry.ActorId);
        Assert.Equal("due_at", entry.Key);
        Assert.Equal(item.PublicId, entry.WorkItemId);
        Assert.Equal(item.CampaignId, entry.CampaignId);
        Assert.Equal("2026-09-14T11:59:00.000Z", (string?)entry.Old);
        Assert.Null(entry.AttemptId);
    }

    [Fact]
    public async Task An_item_whose_due_date_is_still_ahead_is_left_alone()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var item = WorkItemFactory.NewAiRole(campaign, now: Noon, configure: w => w.DueAt = Noon.AddMinutes(1));
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);

        Assert.Equal(0, await NewExpirer().ExpireAsync(db, Ct));
        Assert.Equal(WorkItemStatus.Created, item.Status);
        Assert.Empty(await db.Journal.AsNoTracking().ToListAsync(Ct));
    }

    [Fact]
    public async Task Work_that_is_already_running_runs_past_its_due_date()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var item = WorkItemFactory.NewAiRole(campaign, now: Noon, configure: w =>
        {
            w.Status = WorkItemStatus.Processing;
            w.DueAt = Noon.AddMinutes(-1);
        });
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);

        Assert.Equal(0, await NewExpirer().ExpireAsync(db, Ct));
        Assert.Equal(WorkItemStatus.Processing, item.Status);
    }

    [Fact]
    public async Task An_item_changed_under_the_expirer_does_not_stop_the_rest_of_the_backlog()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var first = WorkItemFactory.NewAiRole(campaign, now: Noon, configure: w => w.DueAt = Noon.AddMinutes(-1));
        var second = WorkItemFactory.NewAiRole(campaign, now: Noon, configure: w => w.DueAt = Noon.AddMinutes(-1));
        db.Campaigns.Add(campaign);
        db.WorkItems.AddRange(first, second);
        await db.SaveChangesAsync(Ct);
        var (firstId, secondId) = (first.PublicId, second.PublicId);
        db.ChangeTracker.Clear();

        // A caller cancels the second item in the instant between the expirer reading the backlog and writing
        // its first decision. The status is a concurrency token, so the expirer's write for that item can no
        // longer land — and the first item, which nothing touched, must be expired all the same.
        var interfered = false;
        db.SavingChanges += (_, _) =>
        {
            if (interfered)
            {
                return;
            }

            interfered = true;
            using var caller = database.Open();
            caller.WorkItems.Single(w => w.PublicId == secondId).Status = WorkItemStatus.Cancelled;
            caller.SaveChanges();
        };

        var expired = await NewExpirer().ExpireAsync(db, Ct);

        Assert.Equal(1, expired);
        await using var reader = database.Open();
        Assert.Equal(WorkItemStatus.Expired, reader.WorkItems.Single(w => w.PublicId == firstId).Status);
        Assert.Equal(WorkItemStatus.Cancelled, reader.WorkItems.Single(w => w.PublicId == secondId).Status);
        var entry = Assert.Single(await reader.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.WorkItemExpired).ToListAsync(Ct));
        Assert.Equal(firstId, entry.WorkItemId);
    }

    private static Expirer NewExpirer()
    {
        var clock = new FixedClock(Noon);
        return new Expirer(new JournalWriter(clock), clock);
    }
}
