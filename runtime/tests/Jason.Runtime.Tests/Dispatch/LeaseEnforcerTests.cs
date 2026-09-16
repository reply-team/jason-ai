using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Tests.Dispatch;

public class LeaseEnforcerTests
{
    private static readonly DateTime Start = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_lease_that_ran_out_loses_the_attempt_and_stops_the_child()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var harness = new Harness();
        var (item, attempt) = await SeedAsync(db, WorkItemStatus.Scheduled, AttemptStatus.Scheduled, timeoutSeconds: 60);
        var killed = 0;
        using var registration = harness.Registry.Register(attempt.PublicId, () => killed++);
        harness.Clock.Now = Start.AddSeconds(61);

        Assert.Equal(1, await harness.Enforcer.EnforceAsync(db, Ct));

        await db.Entry(item).ReloadAsync(Ct);
        await db.Entry(attempt).ReloadAsync(Ct);
        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Equal(AttemptErrors.LeaseExpired, attempt.Error!.Code);
        Assert.True(attempt.Error.Retriable);
        Assert.Equal(WorkItemStatus.Created, item.Status);
        Assert.Equal(1, killed);
    }

    [Theory]
    [InlineData(239, false)]
    [InlineData(241, true)]
    public async Task A_silent_executor_is_given_one_interval_to_arrive_and_one_of_grace(int elapsedSeconds, bool lost)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var harness = new Harness();
        var (item, attempt) = await SeedAsync(db, WorkItemStatus.Processing, AttemptStatus.Running);
        harness.Clock.Now = Start.AddSeconds(elapsedSeconds);

        Assert.Equal(lost ? 1 : 0, await harness.Enforcer.EnforceAsync(db, Ct));

        await db.Entry(item).ReloadAsync(Ct);
        await db.Entry(attempt).ReloadAsync(Ct);
        Assert.Equal(lost ? AttemptStatus.Failed : AttemptStatus.Running, attempt.Status);
        if (lost)
        {
            Assert.Equal(AttemptErrors.HeartbeatMissed, attempt.Error!.Code);
            Assert.Equal(WorkItemStatus.Created, item.Status);
        }
    }

    [Fact]
    public async Task A_heartbeat_moves_the_deadline_forward()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var harness = new Harness();
        var (_, attempt) = await SeedAsync(db, WorkItemStatus.Processing, AttemptStatus.Running);
        attempt.LastHeartbeatAt = Start.AddSeconds(200);
        await db.SaveChangesAsync(Ct);
        harness.Clock.Now = Start.AddSeconds(430);

        Assert.Equal(0, await harness.Enforcer.EnforceAsync(db, Ct));
        Assert.Equal(AttemptStatus.Running, attempt.Status);
    }

    [Theory]
    [InlineData(500, false)]
    [InlineData(541, true)]
    public async Task A_restart_gives_every_running_attempt_the_grace_it_could_not_observe(int elapsedSeconds, bool lost)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var harness = new Harness();
        harness.Status.RuntimeStartedAt = Start.AddSeconds(300);
        var (_, attempt) = await SeedAsync(db, WorkItemStatus.Processing, AttemptStatus.Running);
        harness.Clock.Now = Start.AddSeconds(elapsedSeconds);

        Assert.Equal(lost ? 1 : 0, await harness.Enforcer.EnforceAsync(db, Ct));

        await db.Entry(attempt).ReloadAsync(Ct);
        Assert.Equal(lost ? AttemptStatus.Failed : AttemptStatus.Running, attempt.Status);
    }

    [Fact]
    public async Task An_item_without_a_heartbeat_is_only_ever_lost_to_its_lease()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var harness = new Harness();
        var (_, attempt) = await SeedAsync(db, WorkItemStatus.Processing, AttemptStatus.Running, timeoutSeconds: 3600, heartbeatSeconds: 0);
        harness.Clock.Now = Start.AddSeconds(3000);

        Assert.Equal(0, await harness.Enforcer.EnforceAsync(db, Ct));

        harness.Clock.Now = Start.AddSeconds(3601);
        Assert.Equal(1, await harness.Enforcer.EnforceAsync(db, Ct));

        await db.Entry(attempt).ReloadAsync(Ct);
        Assert.Equal(AttemptErrors.LeaseExpired, attempt.Error!.Code);
    }

    [Fact]
    public async Task A_process_that_lingers_after_its_attempt_finished_is_stopped_once_the_grace_is_over()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var harness = new Harness();
        var (item, attempt) = await SeedAsync(db, WorkItemStatus.Processing, AttemptStatus.Running);
        item.Status = WorkItemStatus.Succeeded;
        item.FinishedAt = Start;
        attempt.Status = AttemptStatus.Succeeded;
        attempt.FinishedAt = Start;
        await db.SaveChangesAsync(Ct);
        var killed = 0;
        using var registration = harness.Registry.Register(attempt.PublicId, () => killed++);

        harness.Clock.Now = Start.AddSeconds(30);
        Assert.Equal(0, await harness.Enforcer.EnforceAsync(db, Ct));
        Assert.Equal(0, killed);

        harness.Clock.Now = Start.AddSeconds(31);
        Assert.Equal(0, await harness.Enforcer.EnforceAsync(db, Ct));
        Assert.Equal(1, killed);
    }

    private static async Task<(WorkItem Item, Attempt Attempt)> SeedAsync(
        JasonDbContext db,
        WorkItemStatus itemStatus,
        AttemptStatus attemptStatus,
        int timeoutSeconds = 3600,
        int? heartbeatSeconds = null)
    {
        var campaign = WorkItemFactory.NewCampaign(now: Start);
        var item = WorkItemFactory.NewAiRole(campaign, now: Start, configure: w =>
        {
            w.Status = itemStatus;
            w.TimeoutSeconds = timeoutSeconds;
            w.HeartbeatSeconds = heartbeatSeconds;
        });
        var attempt = WorkItemFactory.NewAttempt(item, 1, attemptStatus, Start, timeoutSeconds);
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(item);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(Ct);
        return (item, attempt);
    }

    private sealed class Harness
    {
        public Harness()
        {
            Clock = new FixedClock(Start);
            Options = TestOptions.Dispatcher(o =>
            {
                o.RetryDelaySeconds = 0;
                o.ExitGraceSeconds = 30;
                o.AiRole.HeartbeatSeconds = 120;
            });
            Registry = new RunningAttemptRegistry();
            Status = new DispatcherStatus();
            Enforcer = new LeaseEnforcer(
                new AttemptOutcomes(new JournalWriter(Clock), Clock, Options),
                Clock,
                Options,
                Registry,
                Status,
                new UnansweredEnd(OperationCatalog.Find));
        }

        public FixedClock Clock { get; }

        public TestOptionsMonitor<DispatcherOptions> Options { get; }

        public RunningAttemptRegistry Registry { get; }

        public DispatcherStatus Status { get; }

        public LeaseEnforcer Enforcer { get; }
    }
}
