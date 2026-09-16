using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// A1. Every way a provider attempt can end without an answer — its lease running out, its executor going
/// quiet, a restart finding it still out — says nothing about whether the child acted. The only honest class is
/// ambiguous, and what follows from that is the operation's own contract's to say. Agent work keeps exactly the
/// behaviour it has: the runtime's code table decides there, because an agent's lease is the runtime's own.
/// </summary>
public class ProviderInterruptionTests
{
    private const string Safe = "campaign.get";

    /// <summary>An operation whose contract forbids a repeat. Nothing published uses the rule, so this build does.</summary>
    private const string NeverRepeated = "never.repeated";

    private static readonly DateTime Start = new(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(Safe, WorkItemStatus.Created, true)]
    [InlineData(NeverRepeated, WorkItemStatus.Failed, false)]
    public async Task A_lease_that_ran_out_on_a_provider_attempt_is_ambiguous_and_then_the_operation_decides(
        string operation,
        WorkItemStatus expected,
        bool retriable)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var harness = new Harness();
        var (item, attempt) = await SeedAsync(db, WorkItemKind.ProviderOp, operation, WorkItemStatus.Scheduled, AttemptStatus.Scheduled);
        harness.Clock.Now = Start.AddSeconds(61);

        Assert.Equal(1, await harness.Enforcer.EnforceAsync(db, Ct));

        await db.Entry(item).ReloadAsync(Ct);
        await db.Entry(attempt).ReloadAsync(Ct);
        Assert.Equal(AttemptErrors.LeaseExpired, attempt.Error!.Code);
        Assert.Equal(FailureClass.Ambiguous, attempt.Error.Class);
        Assert.Equal(retriable, attempt.Error.Retriable);
        Assert.Equal(expected, item.Status);
    }

    /// <summary>
    /// PA7. The heartbeat check is disabled at the shipped <c>ProviderOp</c> default of zero, so this test names
    /// an interval: a test written against the defaults would pass without ever reaching the arm it is about.
    /// </summary>
    [Theory]
    [InlineData(Safe, WorkItemStatus.Created, true)]
    [InlineData(NeverRepeated, WorkItemStatus.Failed, false)]
    public async Task A_provider_executor_that_went_quiet_is_ambiguous_and_then_the_operation_decides(
        string operation,
        WorkItemStatus expected,
        bool retriable)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var harness = new Harness(o => o.ProviderOp.HeartbeatSeconds = 120);
        var (item, attempt) = await SeedAsync(db, WorkItemKind.ProviderOp, operation, WorkItemStatus.Processing, AttemptStatus.Running, timeoutSeconds: 3600);
        harness.Clock.Now = Start.AddSeconds(241);

        Assert.Equal(1, await harness.Enforcer.EnforceAsync(db, Ct));

        await db.Entry(item).ReloadAsync(Ct);
        await db.Entry(attempt).ReloadAsync(Ct);
        Assert.Equal(AttemptErrors.HeartbeatMissed, attempt.Error!.Code);
        Assert.Equal(FailureClass.Ambiguous, attempt.Error.Class);
        Assert.Equal(retriable, attempt.Error.Retriable);
        Assert.Equal(expected, item.Status);
    }

    /// <summary>
    /// The interval is what makes the arm reachable at all: at the shipped default of zero the same silence is
    /// only ever lost to the lease, which is why the test above configures one.
    /// </summary>
    [Fact]
    public async Task At_the_shipped_default_a_silent_provider_executor_is_only_ever_lost_to_its_lease()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var harness = new Harness();
        Assert.Equal(0, harness.Options.CurrentValue.ProviderOp.HeartbeatSeconds);
        await SeedAsync(db, WorkItemKind.ProviderOp, Safe, WorkItemStatus.Processing, AttemptStatus.Running, timeoutSeconds: 3600);
        harness.Clock.Now = Start.AddSeconds(241);

        Assert.Equal(0, await harness.Enforcer.EnforceAsync(db, Ct));
    }

    /// <summary>
    /// The claim is not the launch. An attempt a restart finds still scheduled is one the handler never
    /// committed <c>processing</c> for, and the handler commits that before it launches anything — so no child
    /// was started, no provider was asked, and there is nothing ambiguous to record. It goes back uncounted,
    /// exactly as agent work does. Calling it ambiguous would permanently fail an item whose operation may
    /// never be repeated, over work that provably never happened.
    /// </summary>
    [Theory]
    [InlineData(Safe)]
    [InlineData(NeverRepeated)]
    public async Task A_provider_attempt_a_restart_found_before_it_started_goes_straight_back(string operation)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var harness = new Harness();
        var (item, attempt) = await SeedAsync(db, WorkItemKind.ProviderOp, operation, WorkItemStatus.Scheduled, AttemptStatus.Scheduled);

        Assert.Equal(1, await harness.Recovery.RunAsync(db, Ct));

        await db.Entry(item).ReloadAsync(Ct);
        await db.Entry(attempt).ReloadAsync(Ct);
        Assert.Equal(AttemptStatus.Interrupted, attempt.Status);
        Assert.Null(attempt.Error);
        Assert.Equal(WorkItemStatus.Created, item.Status);
        Assert.Equal(0, item.AttemptCount);
        Assert.Null(item.RetryAfter);
    }

    /// <summary>
    /// The other side of that guard, and why it is written as one: an attempt that did start is past the moment
    /// the handler commits, so nobody can say the provider was not asked. The handler writes the item and the
    /// attempt together, which is why no run leaves a row like this and the test has to write it itself.
    /// </summary>
    [Fact]
    public async Task A_provider_attempt_found_past_its_start_is_ambiguous_however_a_restart_finds_it()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var harness = new Harness();
        var (item, attempt) = await SeedAsync(db, WorkItemKind.ProviderOp, NeverRepeated, WorkItemStatus.Scheduled, AttemptStatus.Running);

        Assert.Equal(1, await harness.Recovery.RunAsync(db, Ct));

        await db.Entry(item).ReloadAsync(Ct);
        await db.Entry(attempt).ReloadAsync(Ct);
        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Equal(AttemptErrors.Interrupted, attempt.Error!.Code);
        Assert.Equal(FailureClass.Ambiguous, attempt.Error.Class);
        Assert.False(attempt.Error.Retriable);
        Assert.Equal(WorkItemStatus.Failed, item.Status);
    }

    /// <summary>
    /// The item nobody may hand out again is in the manager's inbox with both halves of the reason: the
    /// operation could not be repeated, and nobody knows whether it happened.
    /// </summary>
    [Fact]
    public async Task An_item_that_may_never_be_repeated_waits_for_a_person_with_the_reason_it_is_waiting()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var harness = new Harness();
        var (item, _) = await SeedAsync(db, WorkItemKind.ProviderOp, NeverRepeated, WorkItemStatus.Scheduled, AttemptStatus.Scheduled);
        harness.Clock.Now = Start.AddSeconds(61);

        Assert.Equal(1, await harness.Enforcer.EnforceAsync(db, Ct));

        await db.Entry(item).ReloadAsync(Ct);
        Assert.Equal(WorkItemStatus.Failed, item.Status);

        // Counted, unlike an attempt a restart hands back: this one was out when its lease ran out, and how it
        // ended is the reason the item is here rather than in the queue.
        Assert.Equal(1, item.AttemptCount);
        Assert.Equal(FailureClass.Ambiguous, item.LastError!.Class);
        Assert.False(item.LastError.Retriable);
        Assert.Null(item.RetryAfter);
    }

    /// <summary>Wave 3's behaviour, unchanged: an agent's lease is the runtime's own, and the code table decides.</summary>
    [Fact]
    public async Task An_agent_attempt_keeps_the_lease_behaviour_it_has_always_had()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var harness = new Harness();
        var (item, attempt) = await SeedAsync(db, WorkItemKind.AiRole, operation: null, WorkItemStatus.Scheduled, AttemptStatus.Scheduled);
        harness.Clock.Now = Start.AddSeconds(61);

        Assert.Equal(1, await harness.Enforcer.EnforceAsync(db, Ct));

        await db.Entry(item).ReloadAsync(Ct);
        await db.Entry(attempt).ReloadAsync(Ct);
        Assert.Equal(AttemptErrors.LeaseExpired, attempt.Error!.Code);
        Assert.True(attempt.Error.Retriable);
        Assert.Null(attempt.Error.Class);
        Assert.Equal(WorkItemStatus.Created, item.Status);
    }

    /// <summary>
    /// And an agent attempt a restart finds is still handed straight back, uncounted: nobody ran it, so nothing
    /// was spent — which is a different fact from "nobody knows what happened", and stays a different one.
    /// </summary>
    [Fact]
    public async Task An_agent_attempt_a_restart_found_is_still_handed_straight_back()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var harness = new Harness();
        var (item, attempt) = await SeedAsync(db, WorkItemKind.AiRole, operation: null, WorkItemStatus.Scheduled, AttemptStatus.Scheduled);

        Assert.Equal(1, await harness.Recovery.RunAsync(db, Ct));

        await db.Entry(item).ReloadAsync(Ct);
        await db.Entry(attempt).ReloadAsync(Ct);
        Assert.Equal(AttemptStatus.Interrupted, attempt.Status);
        Assert.Null(attempt.Error);
        Assert.Equal(WorkItemStatus.Created, item.Status);
        Assert.Equal(0, item.AttemptCount);
    }

    /// <summary>
    /// A cancellation is not an unanswered end. Somebody decided, so the attempt records that decision rather
    /// than the runtime's guess about what a provider may have done.
    /// </summary>
    [Fact]
    public async Task A_cancellation_of_a_provider_attempt_stays_a_cancellation()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var harness = new Harness();
        var (_, attempt) = await SeedAsync(db, WorkItemKind.ProviderOp, NeverRepeated, WorkItemStatus.Processing, AttemptStatus.Running);

        harness.Outcomes.CancelAttempt(attempt);
        await db.SaveChangesAsync(Ct);

        Assert.Equal(AttemptStatus.Cancelled, attempt.Status);
        Assert.Equal(AttemptErrors.Cancelled, attempt.Error!.Code);
        Assert.Null(attempt.Error.Class);
        Assert.False(attempt.Error.Retriable);
    }

    private static async Task<(WorkItem Item, Attempt Attempt)> SeedAsync(
        JasonDbContext db,
        WorkItemKind kind,
        string? operation,
        WorkItemStatus itemStatus,
        AttemptStatus attemptStatus,
        int timeoutSeconds = 60)
    {
        var campaign = WorkItemFactory.NewCampaign(now: Start);
        var item = kind == WorkItemKind.ProviderOp
            ? WorkItemFactory.NewProviderOp(campaign, operation!, Start)
            : WorkItemFactory.NewAiRole(campaign, now: Start);
        item.Status = itemStatus;
        item.TimeoutSeconds = timeoutSeconds;
        var attempt = WorkItemFactory.NewAttempt(item, 1, attemptStatus, Start, timeoutSeconds);
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(item);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(Ct);
        return (item, attempt);
    }

    /// <summary>
    /// The catalog this build publishes, plus one operation it does not: <c>never</c> is part of the contract
    /// vocabulary and no published operation uses it, so without a contract built here the branch that refuses a
    /// repeat would have nothing exercising it.
    /// </summary>
    private static OperationContract? ContractFor(string operation) => operation switch
    {
        NeverRepeated => OperationCatalog.Find(Safe)! with { Id = NeverRepeated, RepeatAfterAmbiguous = RepeatAfterAmbiguous.Never },
        _ => OperationCatalog.Find(operation),
    };

    private sealed class Harness
    {
        public Harness(Action<DispatcherOptions>? configure = null)
        {
            Clock = new FixedClock(Start);
            Options = TestOptions.Dispatcher(o =>
            {
                o.RetryDelaySeconds = 0;
                o.ExitGraceSeconds = 30;
                configure?.Invoke(o);
            });
            Outcomes = new AttemptOutcomes(new JournalWriter(Clock), Clock, Options);
            var unanswered = new UnansweredEnd(ContractFor);
            Enforcer = new LeaseEnforcer(Outcomes, Clock, Options, new RunningAttemptRegistry(), new DispatcherStatus(), unanswered);
            Recovery = new StartupRecovery(Outcomes, unanswered);
        }

        public FixedClock Clock { get; }

        public TestOptionsMonitor<DispatcherOptions> Options { get; }

        public AttemptOutcomes Outcomes { get; }

        public LeaseEnforcer Enforcer { get; }

        public StartupRecovery Recovery { get; }
    }
}
