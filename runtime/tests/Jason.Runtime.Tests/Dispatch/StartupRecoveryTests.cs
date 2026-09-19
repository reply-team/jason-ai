using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Dispatch;

public class StartupRecoveryTests
{
    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_restart_hands_back_what_it_never_started_and_leaves_the_rest_to_the_leases()
    {
        var clock = new FixedClock(Noon);
        await using var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths =>
            {
                File.WriteAllText(paths.UserSettingsFile, """{"Dispatcher":{"TickSeconds":3600,"RetryDelaySeconds":0}}""");
                Seed(paths);
            },
            clock: clock);

        await using (var db = Open(fixture.Paths))
        {
            var released = await db.WorkItems.AsNoTracking().Include(w => w.Attempts).SingleAsync(w => w.Role == "planner", Ct);
            Assert.Equal(WorkItemStatus.Created, released.Status);
            Assert.Equal(0, released.AttemptCount);
            Assert.Equal(AttemptStatus.Interrupted, Assert.Single(released.Attempts).Status);

            var entry = Assert.Single(await db.Journal.AsNoTracking().Where(e => e.WorkItemId == released.PublicId).ToListAsync(Ct));
            Assert.Equal(JournalKinds.WorkItemReleased, entry.Kind);
            Assert.Equal(AttemptErrors.Interrupted, (string?)entry.New!["code"]);

            // A running attempt belongs to its executor until the lease says otherwise, restart or no restart.
            var survivor = await db.WorkItems.AsNoTracking().Include(w => w.Attempts).SingleAsync(w => w.Role == "responder", Ct);
            Assert.Equal(WorkItemStatus.Processing, survivor.Status);
            Assert.Equal(AttemptStatus.Running, Assert.Single(survivor.Attempts).Status);
        }

        // This fixture leaves the dispatcher running, because startup recovery is part of starting it, and the
        // loop scans once the moment it starts. That scan is on a thread of its own: under load it can arrive
        // after the line below moves time, find the lease expired and report it — leaving this test's own scan
        // nothing to find and an assertion reading "expected 1, actual 0". A lost attempt is reported by
        // whichever scan reaches it first, so the startup one is waited for while time still stands still.
        var status = fixture.Resolve<DispatcherStatus>();
        while (status.Scans == 0)
        {
            await Task.Delay(10, Ct);
        }

        clock.Advance(TimeSpan.FromHours(2));
        var report = await fixture.Resolve<ScanRunner>().ScanOnceAsync(Ct);

        Assert.Equal(1, report.Lost);
        await using (var db = Open(fixture.Paths))
        {
            var survivor = await db.WorkItems.AsNoTracking().Include(w => w.Attempts).SingleAsync(w => w.Role == "responder", Ct);
            Assert.Equal(AttemptErrors.LeaseExpired, Assert.Single(survivor.Attempts).Error!.Code);
        }
    }

    [Fact]
    public async Task A_restart_anchors_the_heartbeat_grace_on_itself()
    {
        var clock = new FixedClock(Noon);
        await using var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths => File.WriteAllText(paths.UserSettingsFile, """{"Dispatcher":{"TickSeconds":3600}}"""),
            clock: clock);

        Assert.Equal(Noon, fixture.Resolve<DispatcherStatus>().RuntimeStartedAt);
    }

    private static void Seed(JasonPaths paths)
    {
        new DatabaseMigrator(paths).Migrate();
        using var db = Open(paths);

        // Paused, so recovery is all that touches these rows: a claim would muddy what the test is asking.
        var campaign = WorkItemFactory.NewCampaign("Interrupted", CampaignStatus.Paused, now: Noon);
        var scheduled = WorkItemFactory.NewAiRole(campaign, "planner", now: Noon, configure: w => w.Status = WorkItemStatus.Scheduled);
        var processing = WorkItemFactory.NewAiRole(campaign, "responder", now: Noon, configure: w => w.Status = WorkItemStatus.Processing);
        db.Campaigns.Add(campaign);
        db.WorkItems.AddRange(scheduled, processing);
        db.Attempts.Add(WorkItemFactory.NewAttempt(scheduled, 1, AttemptStatus.Scheduled, Noon.AddMinutes(-5)));
        db.Attempts.Add(WorkItemFactory.NewAttempt(processing, 1, AttemptStatus.Running, Noon.AddMinutes(-5)));
        db.SaveChanges();
    }

    private static JasonDbContext Open(JasonPaths paths) => new(JasonDbContext.CreateOptions(paths.DatabaseFile));
}
