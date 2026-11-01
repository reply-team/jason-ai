using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

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

    /// <summary>The published operation whose contract forbids a repeat: a reply that may have gone is never sent again.</summary>
    private const string NeverRepeated = "conversation.reply";

    /// <summary>The published operation whose repeat rests on the work item's key.</summary>
    private const string Send = "message.send";

    /// <summary>The package a send's attempt ran under, as its provenance names it.</summary>
    private const string Sender = "fake-sender";

    private static readonly string RanDigest = "sha256:" + new string('a', 64);

    private static readonly DateTime Start = new(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [Critical(Family.W5, "the operation decides the repeat")]
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
    /// A send settles a lost answer by the work item's key, so a lease that ran out on one is repeated only where the
    /// package the attempt ran under, as its provenance names it, is the one the registry still holds and lists the send
    /// under <c>settles_by_key</c>. A package without the marker, one replaced since, or none at all, answers no, and
    /// the end is final.
    /// </summary>
    [Theory]
    [InlineData(Keyed.Listed, WorkItemStatus.Created, true)]
    [InlineData(Keyed.Unlisted, WorkItemStatus.Failed, false)]
    [InlineData(Keyed.Replaced, WorkItemStatus.Failed, false)]
    [InlineData(Keyed.Gone, WorkItemStatus.Failed, false)]
    public async Task A_lease_that_ran_out_on_a_send_is_repeated_only_under_the_package_that_settles_it_by_the_key(
        Keyed package,
        WorkItemStatus expected,
        bool retriable)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var harness = new Harness(settlesByKey: UnansweredEnd.RanUnder(Registry(package)));
        var (item, attempt) = await SeedAsync(db, WorkItemKind.ProviderOp, Send, WorkItemStatus.Scheduled, AttemptStatus.Scheduled);
        attempt.Provenance = new AttemptProvenanceDto(Sender, "1.0.0", RanDigest, null, null, Send, null, null, null, null, null);
        await db.SaveChangesAsync(Ct);
        harness.Clock.Now = Start.AddSeconds(61);

        Assert.Equal(1, await harness.Enforcer.EnforceAsync(db, Ct));

        await db.Entry(item).ReloadAsync(Ct);
        await db.Entry(attempt).ReloadAsync(Ct);
        Assert.Equal(FailureClass.Ambiguous, attempt.Error!.Class);
        Assert.Equal(retriable, attempt.Error.Retriable);
        Assert.Equal(expected, item.Status);
    }

    /// <summary>
    /// The rule the runtime composes asks the plugin registry the runtime holds: a send that lost its answer is not
    /// worth repeating while the registry holds no package, and is once its snapshot holds the package the attempt ran
    /// under, listing the send under <c>settles_by_key</c>.
    /// </summary>
    [Fact]
    public void The_composed_end_asks_the_registry_the_runtime_holds()
    {
        using var harness = new DispatchHarness(Start);
        var end = harness.Resolve<UnansweredEnd>();
        var item = WorkItemFactory.NewProviderOp(WorkItemFactory.NewCampaign(), Send);
        var attempt = new Attempt
        {
            PublicId = PublicId.New("att"),
            Number = 1,
            Provenance = new AttemptProvenanceDto(Sender, "1.0.0", RanDigest, null, null, Send, null, null, null, null, null),
        };

        Assert.Equal((FailureClass.Ambiguous, false), end.Verdict(item, attempt));

        harness.Resolve<PluginRegistry>().Replace(
            Registry(Keyed.Listed).Snapshot,
            new ReloadReport(Start, SnapshotSource.Startup, true, [], []));

        Assert.Equal((FailureClass.Ambiguous, true), end.Verdict(item, attempt));
    }

    /// <summary>
    /// The same rule at a restart, and with no word from any package at all: a send found past its start is final
    /// unless the package it ran under says it settles the send by the key, while an operation whose repeat does not
    /// rest on the key is repeated as it always was.
    /// </summary>
    [Theory]
    // Critical: a send no package settles by its key is never repeated.
    [InlineData(Send, WorkItemStatus.Failed, false, Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.W5)])]
    [InlineData(Safe, WorkItemStatus.Created, true)]
    public async Task A_restart_repeats_a_send_only_on_the_word_of_the_package_it_ran_under(string operation, WorkItemStatus expected, bool retriable)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var harness = new Harness();
        var (item, attempt) = await SeedAsync(db, WorkItemKind.ProviderOp, operation, WorkItemStatus.Scheduled, AttemptStatus.Running);

        Assert.Equal(1, await harness.Recovery.RunAsync(db, Ct));

        await db.Entry(item).ReloadAsync(Ct);
        await db.Entry(attempt).ReloadAsync(Ct);
        Assert.Equal(AttemptErrors.Interrupted, attempt.Error!.Code);
        Assert.Equal(FailureClass.Ambiguous, attempt.Error.Class);
        Assert.Equal(retriable, attempt.Error.Retriable);
        Assert.Equal(expected, item.Status);
    }

    /// <summary>What the registry holds for the package a send's attempt ran under.</summary>
    public enum Keyed
    {
        /// <summary>The same package, listing the send under <c>settles_by_key</c>.</summary>
        Listed,

        /// <summary>The same package, listing nothing.</summary>
        Unlisted,

        /// <summary>A package of the same id that lists the send, under another digest: not the one the attempt ran under.</summary>
        Replaced,

        /// <summary>No package of that id.</summary>
        Gone,
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
    [Critical(Family.W5, "a restart never guesses success or failure")]
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
    [Critical(Family.W5, "never repeated after ambiguous")]
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

    /// <summary>The original behaviour, unchanged: an agent's lease is the runtime's own, and the code table decides.</summary>
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

    /// <summary>
    /// The yes way of the same restart: a send found past its start is repeated where the package its provenance names
    /// is the one the registry still holds and lists the send under <c>settles_by_key</c>, and is final under a package
    /// without the marker, one replaced since, or none at all.
    /// </summary>
    [Theory]
    [InlineData(Keyed.Listed, WorkItemStatus.Created, true)]
    [InlineData(Keyed.Unlisted, WorkItemStatus.Failed, false)]
    [InlineData(Keyed.Replaced, WorkItemStatus.Failed, false)]
    [InlineData(Keyed.Gone, WorkItemStatus.Failed, false)]
    public async Task A_restart_repeats_a_send_under_the_package_that_settles_it_by_the_key(
        Keyed package,
        WorkItemStatus expected,
        bool retriable)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var harness = new Harness(settlesByKey: UnansweredEnd.RanUnder(Registry(package)));
        var (item, attempt) = await SeedAsync(db, WorkItemKind.ProviderOp, Send, WorkItemStatus.Scheduled, AttemptStatus.Running);
        attempt.Provenance = new AttemptProvenanceDto(Sender, "1.0.0", RanDigest, null, null, Send, null, null, null, null, null);
        await db.SaveChangesAsync(Ct);

        Assert.Equal(1, await harness.Recovery.RunAsync(db, Ct));

        await db.Entry(item).ReloadAsync(Ct);
        await db.Entry(attempt).ReloadAsync(Ct);
        Assert.Equal(AttemptErrors.Interrupted, attempt.Error!.Code);
        Assert.Equal(FailureClass.Ambiguous, attempt.Error.Class);
        Assert.Equal(retriable, attempt.Error.Retriable);
        Assert.Equal(expected, item.Status);
    }

    /// <summary>
    /// The two ends the handler reaches for a send that may have reached the person — the plugin answered that its
    /// answer was lost, or nothing answered at all — ask one question of the registry the runtime holds: whether the
    /// package the attempt ran under, as its provenance names it, is still the one held, under the same digest, and lists
    /// the send under <c>settles_by_key</c>. The package the claim's plan carries is not that word: one replaced or
    /// removed between the claim and the answer cannot answer for the attempt that ran under it.
    /// </summary>
    [Theory]
    [InlineData(true, Keyed.Listed, WorkItemStatus.Created, true)]
    [InlineData(true, Keyed.Unlisted, WorkItemStatus.Failed, false)]
    [InlineData(true, Keyed.Replaced, WorkItemStatus.Failed, false)]
    [InlineData(true, Keyed.Gone, WorkItemStatus.Failed, false)]
    [InlineData(false, Keyed.Listed, WorkItemStatus.Created, true)]
    [InlineData(false, Keyed.Unlisted, WorkItemStatus.Failed, false)]
    [InlineData(false, Keyed.Replaced, WorkItemStatus.Failed, false)]
    [InlineData(false, Keyed.Gone, WorkItemStatus.Failed, false)]
    public async Task A_send_that_lost_its_answer_in_the_handler_is_repeated_only_under_the_package_that_settles_it_by_the_key(
        bool answered,
        Keyed package,
        WorkItemStatus expected,
        bool retriable)
    {
        var lost = new PluginInvocationResult(
            new InvocationOutcome.Failed(
                new OutcomeError(FailureClass.Ambiguous, AttemptErrors.ProviderAnswerLost, "No answer came back.", null, null),
                new OutcomeDiagnostics(12, 1, 0, 1)),
            new InvocationProvenance(
                Sender,
                "1.0.0",
                RanDigest,
                PluginProtocol.CurrentVersion,
                PluginProtocol.OperationContractVersion,
                PluginProtocol.InvocationIdPrefix + "_01K5B7Q2WE5X3M9T0YH4C6RDNC",
                "att_01K5B7Q2WE5X3M9T0YH4C6RDND",
                PluginProtocol.SnapshotIdPrefix + "_01K5B7Q2WE5X3M9T0YH4C6RDNE"),
            Launch: null);
        var command = new FakeCommand(
            WorkItemKind.ProviderOp,
            _ => Task.FromResult<CommandOutcome>(answered ? new CommandOutcome.Provider(lost, null) : new CommandOutcome.Killed(null)));
        using var harness = new DispatchHarness(Start, o => o.RetryDelaySeconds = 0, commands: command);
        harness.Resolve<PluginRegistry>().Replace(
            Registry(package).Snapshot,
            new ReloadReport(Start, SnapshotSource.Startup, true, [], []));

        // The plan as the claim built it: the package the attempt ran under, which listed the send unless it never did.
        var ran = TestPlugins.Loaded(Sender, [Send]) with { Digest = RanDigest };
        if (package != Keyed.Unlisted)
        {
            ran = ran with { Manifest = ran.Manifest with { KeyedOperations = [Send] } };
        }

        ClaimedWork work;
        await using (var db = harness.Open())
        {
            var (seeded, attempt) = await SeedAsync(db, WorkItemKind.ProviderOp, Send, WorkItemStatus.Scheduled, AttemptStatus.Scheduled);
            attempt.Provenance = new AttemptProvenanceDto(Sender, "1.0.0", RanDigest, null, null, Send, null, null, null, null, null);
            await db.SaveChangesAsync(Ct);
            var plan = new ProviderOpPlan(
                ran,
                PluginProtocol.SnapshotIdPrefix + "_01K5B7Q2WE5X3M9T0YH4C6RDNA",
                RouteSnapshot.IdPrefix + "_01K5B7Q2WE5X3M9T0YH4C6RDNB",
                RouteScope.GlobalDefault,
                null,
                null,
                OperationCatalog.Find(Send)!,
                new JsonObject());
            work = new ClaimedWork(seeded.Id, attempt.Id, seeded.PublicId, attempt.PublicId, seeded.Campaign!.PublicId, plan);
        }

        await AttemptHandler.RunAsync(harness.Scopes, work, harness.Registry, NullLogger.Instance);

        await using var read = harness.Open();
        var item = await read.WorkItems.AsNoTracking().Include(w => w.Attempts).SingleAsync(w => w.Id == work.WorkItemId, Ct);
        var ended = Assert.Single(item.Attempts);
        Assert.Equal(FailureClass.Ambiguous, ended.Error!.Class);
        Assert.True(
            (ended.Error.Retriable, item.Status) == (retriable, expected),
            $"The send ended {item.Status} with retriable {ended.Error.Retriable} ({ended.Error.Code}).");
    }

    private static PluginRegistry Registry(Keyed package)
    {
        var registry = new PluginRegistry(new FixedClock(Start));
        var loaded = TestPlugins.Loaded(Sender, [Send]);
        loaded = package switch
        {
            Keyed.Listed => loaded with { Digest = RanDigest, Manifest = loaded.Manifest with { KeyedOperations = [Send] } },
            Keyed.Unlisted => loaded with { Digest = RanDigest },
            Keyed.Replaced => loaded with { Digest = "sha256:" + new string('b', 64), Manifest = loaded.Manifest with { KeyedOperations = [Send] } },
            _ => TestPlugins.Loaded("other-provider", [Send]),
        };
        registry.Replace(
            new PluginSnapshot("plg_interrupted", Start, SnapshotSource.Startup, [loaded]),
            new ReloadReport(Start, SnapshotSource.Startup, true, [], []));
        return registry;
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
    /// The catalog this build publishes, and nothing else: the operation that says <c>never</c> is the published reply,
    /// so the branch that refuses a repeat is exercised by the very document the runtime enforces.
    /// </summary>
    private static OperationContract? ContractFor(string operation) => OperationCatalog.Find(operation);

    private sealed class Harness
    {
        public Harness(Action<DispatcherOptions>? configure = null, Func<AttemptProvenanceDto?, string, bool>? settlesByKey = null)
        {
            Clock = new FixedClock(Start);
            Options = TestOptions.Dispatcher(o =>
            {
                o.RetryDelaySeconds = 0;
                o.ExitGraceSeconds = 30;
                configure?.Invoke(o);
            });
            Settings = TestOptions.Settings(Options);
            Outcomes = new AttemptOutcomes(new JournalWriter(Clock), Clock, Settings);
            var unanswered = new UnansweredEnd(ContractFor, settlesByKey);
            Enforcer = new LeaseEnforcer(Outcomes, Clock, Settings, new RunningAttemptRegistry(), new DispatcherStatus(), unanswered);
            Recovery = new StartupRecovery(Outcomes, unanswered);
        }

        public FixedClock Clock { get; }

        public TestOptionsMonitor<DispatcherOptions> Options { get; }

        public LiveSettings<DispatcherOptions> Settings { get; }

        public AttemptOutcomes Outcomes { get; }

        public LeaseEnforcer Enforcer { get; }

        public StartupRecovery Recovery { get; }
    }
}
