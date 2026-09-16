using Jason.Contracts.Api;
using Jason.Contracts.Plugins;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jason.Runtime.Tests.Dispatch;

public class AttemptHandlerTests
{
    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Running_an_attempt_moves_the_item_and_tells_the_command_what_to_do()
    {
        var command = FakeCommand.Returning(new CommandOutcome.Completed(null));
        using var harness = new DispatchHarness(Noon, o => o.RetryDelaySeconds = 0, commands: command);
        var seeded = await harness.SeedClaimableAsync(Ct, w => w.Context["brief"] = "read the room");
        var work = Assert.Single(await harness.ClaimAsync(Ct));

        await RunAsync(harness, work);

        var context = Assert.Single(command.Contexts);
        Assert.Equal(seeded.PublicId, context.WorkItemId);
        Assert.Equal(work.AttemptPublicId, context.AttemptId);
        Assert.Equal(1, context.AttemptNumber);
        Assert.Equal(WorkItemKind.AiRole, context.Kind);
        Assert.Equal("researcher", context.Role);
        Assert.Equal("read the room", (string?)context.ContextSnapshot["brief"]);
        Assert.Equal(3600, context.Limits.TimeoutSeconds);
        Assert.Equal(new[] { "agent-host" }, context.EntryCommand);
        Assert.Equal(Path.Combine(harness.Paths.WorkDirectory, seeded.PublicId, work.AttemptPublicId), context.WorkDir);
        Assert.Equal(WorkItemMapper.Utc(Noon.AddSeconds(3600)), context.LockUntil);

        var item = await harness.ReadItemAsync(seeded.PublicId, Ct);
        Assert.Equal(WorkItemStatus.Processing, item.Status);
        var attempt = Assert.Single(item.Attempts);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Equal(Noon, attempt.StartedAt);

        var kinds = (await harness.ReadJournalAsync(seeded.PublicId, Ct)).Select(e => e.Kind).ToList();
        Assert.Equal(new[] { JournalKinds.WorkItemScheduled, JournalKinds.WorkItemProcessing }, kinds);
    }

    [Fact]
    public async Task An_executor_that_exits_without_reporting_loses_its_attempt()
    {
        var command = FakeCommand.Returning(new CommandOutcome.Exited(3, "boom", Launch(2718)));
        using var harness = new DispatchHarness(Noon, o => o.RetryDelaySeconds = 0, commands: command);
        var seeded = await harness.SeedClaimableAsync(Ct);
        var work = Assert.Single(await harness.ClaimAsync(Ct));

        await RunAsync(harness, work);

        var item = await harness.ReadItemAsync(seeded.PublicId, Ct);
        Assert.Equal(WorkItemStatus.Created, item.Status);
        Assert.Equal(1, item.AttemptCount);
        Assert.Null(item.RetryAfter);
        var attempt = Assert.Single(item.Attempts);
        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Equal(AttemptErrors.ExecutorExited, attempt.Error!.Code);
        Assert.True(attempt.Error.Retriable);
        Assert.Equal("boom", attempt.Error.Trace);
        Assert.Contains("3", attempt.Error.Message, StringComparison.Ordinal);

        // The launch the command reported is provenance: it says which process ran and how it ended.
        Assert.Equal(2718, attempt.Launch!.Pid);
    }

    [Fact]
    public async Task An_attempt_the_executor_already_finished_is_left_alone()
    {
        DispatchHarness? harness = null;
        var command = new FakeCommand(WorkItemKind.AiRole, async context =>
        {
            await CompleteThroughTheApiAsync(harness!, context.AttemptId);
            return new CommandOutcome.Exited(0, null, Launch(4242));
        });
        using (harness = new DispatchHarness(Noon, commands: command))
        {
            var seeded = await harness.SeedClaimableAsync(Ct);
            var work = Assert.Single(await harness.ClaimAsync(Ct));

            await RunAsync(harness, work);

            var item = await harness.ReadItemAsync(seeded.PublicId, Ct);
            Assert.Equal(WorkItemStatus.Succeeded, item.Status);
            Assert.Equal(AttemptStatus.Succeeded, Assert.Single(item.Attempts).Status);
            var kinds = (await harness.ReadJournalAsync(seeded.PublicId, Ct)).Select(e => e.Kind).ToList();
            Assert.Equal(new[] { JournalKinds.WorkItemScheduled, JournalKinds.WorkItemProcessing, JournalKinds.WorkItemSucceeded }, kinds);
        }
    }

    [Fact]
    public async Task A_command_that_cannot_be_started_ends_the_item()
    {
        var command = FakeCommand.Returning(new CommandOutcome.LaunchFailed("agent-host is not on the path", Launch(null)));
        using var harness = new DispatchHarness(Noon, commands: command);
        var seeded = await harness.SeedClaimableAsync(Ct);
        var work = Assert.Single(await harness.ClaimAsync(Ct));

        await RunAsync(harness, work);

        var item = await harness.ReadItemAsync(seeded.PublicId, Ct);
        Assert.Equal(WorkItemStatus.Failed, item.Status);
        Assert.Equal(AttemptErrors.ExecutorLaunchFailed, item.LastError!.Code);
        Assert.False(item.LastError.Retriable);

        // An agent's launch happened inside this machine, so the runtime's own code table answers for it and
        // there is no class to carry: a class is what a provider operation's contract is read against.
        Assert.Null(item.LastError.Class);
        Assert.Equal("agent-host is not on the path", Assert.Single(item.Attempts).Error!.Message);
    }

    /// <summary>
    /// A1. A provider command that could not be started is an end nobody answered for, and a missing answer
    /// says nothing about whether the provider acted — so the class is ambiguous and the operation's own
    /// contract decides what follows, exactly as for a lease that ran out. It is barely reachable, because the
    /// invoker turns kills, timeouts, launch failures and pin mismatches into outcomes of its own; the sentence
    /// has to be true anyway, or the one path that does reach it fails a claim the runtime cannot account for.
    /// </summary>
    [Fact]
    public async Task A_provider_command_that_cannot_be_started_ends_ambiguous()
    {
        var command = FakeCommand.Returning(new CommandOutcome.LaunchFailed("the plugin host is not there", Launch(null)), WorkItemKind.ProviderOp);
        using var harness = new DispatchHarness(Noon, o => o.RetryDelaySeconds = 0, commands: command);
        var (seeded, work) = await SeedClaimedProviderAsync(harness);

        await RunAsync(harness, work);

        var item = await harness.ReadItemAsync(seeded, Ct);
        var attempt = Assert.Single(item.Attempts);
        Assert.Equal(AttemptErrors.ExecutorLaunchFailed, attempt.Error!.Code);
        Assert.Equal(FailureClass.Ambiguous, attempt.Error.Class);

        // campaign.get says a repeat after an ambiguous end is safe, so the item goes back into the queue.
        Assert.True(attempt.Error.Retriable);
        Assert.Equal(WorkItemStatus.Created, item.Status);
    }

    /// <summary>
    /// The same for a provider command that was stopped. The runtime gave up on the child; whether the child
    /// had already reached the provider is exactly what nobody can say.
    /// </summary>
    [Fact]
    public async Task A_provider_command_that_was_stopped_ends_ambiguous()
    {
        var command = FakeCommand.Returning(new CommandOutcome.Killed(Launch(2718)), WorkItemKind.ProviderOp);
        using var harness = new DispatchHarness(Noon, o => o.RetryDelaySeconds = 0, commands: command);
        var (seeded, work) = await SeedClaimedProviderAsync(harness);

        await RunAsync(harness, work);

        var item = await harness.ReadItemAsync(seeded, Ct);
        var attempt = Assert.Single(item.Attempts);
        Assert.Equal(AttemptErrors.ExecutorExited, attempt.Error!.Code);
        Assert.Equal(FailureClass.Ambiguous, attempt.Error.Class);
        Assert.True(attempt.Error.Retriable);
        Assert.Equal(WorkItemStatus.Created, item.Status);
    }

    [Fact]
    public async Task A_kind_no_command_serves_fails_the_attempt_instead_of_hanging()
    {
        using var harness = new DispatchHarness(Noon);
        var seeded = await harness.SeedClaimableAsync(Ct);
        var work = Assert.Single(await harness.ClaimAsync(Ct));

        await RunAsync(harness, work);

        var item = await harness.ReadItemAsync(seeded.PublicId, Ct);
        Assert.Equal(WorkItemStatus.Failed, item.Status);
        Assert.Equal(AttemptErrors.ExecutorLaunchFailed, item.LastError!.Code);
        Assert.Contains("ai_role", Assert.Single(item.Attempts).Error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_command_registered_last_is_the_one_that_runs()
    {
        var first = FakeCommand.Returning(new CommandOutcome.Completed(null));
        var second = FakeCommand.Returning(new CommandOutcome.Completed(null));
        using var harness = new DispatchHarness(Noon, commands: [first, second]);
        await harness.SeedClaimableAsync(Ct);
        var work = Assert.Single(await harness.ClaimAsync(Ct));

        await RunAsync(harness, work);

        Assert.Empty(first.Contexts);
        Assert.Single(second.Contexts);
    }

    [Fact]
    public async Task A_kill_reaches_the_command_and_the_cancelled_attempt_is_left_as_it_is()
    {
        DispatchHarness? harness = null;
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new FakeCommand(WorkItemKind.AiRole, async context =>
        {
            running.SetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, context.Kill);
            }
            catch (OperationCanceledException)
            {
                // The kill is how a cancellation reaches a running child.
            }

            return new CommandOutcome.Killed(null);
        });

        using (harness = new DispatchHarness(Noon, commands: command))
        {
            var seeded = await harness.SeedClaimableAsync(Ct);
            var work = Assert.Single(await harness.ClaimAsync(Ct));
            var handler = RunAsync(harness, work);
            await running.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);

            await CancelThroughTheApiAsync(harness, work.AttemptPublicId);
            Assert.True(harness.Registry.TryKill(work.AttemptPublicId));
            await handler.WaitAsync(TimeSpan.FromSeconds(5), Ct);

            var item = await harness.ReadItemAsync(seeded.PublicId, Ct);
            Assert.Equal(WorkItemStatus.Cancelled, item.Status);
            Assert.Equal(AttemptStatus.Cancelled, Assert.Single(item.Attempts).Status);
        }
    }

    [Fact]
    public async Task An_attempt_decided_while_its_verdict_is_being_written_still_records_how_it_ran()
    {
        DispatchHarness? harness = null;
        var command = new FakeCommand(WorkItemKind.AiRole, context =>
        {
            // A cancellation stops the child before its own change is committed, so the handler can reach the
            // row still believing the attempt is running; this puts the cancellation exactly there.
            harness!.InterfereOnceBeforeSaving(() => CancelThroughTheApiAsync(harness!, context.AttemptId).GetAwaiter().GetResult());
            return Task.FromResult<CommandOutcome>(new CommandOutcome.Killed(Launch(2718)));
        });

        using (harness = new DispatchHarness(Noon, commands: command))
        {
            var seeded = await harness.SeedClaimableAsync(Ct);
            var work = Assert.Single(await harness.ClaimAsync(Ct));

            await RunAsync(harness, work);

            // The cancellation stands, and how the attempt was run is kept even though its verdict was not.
            var item = await harness.ReadItemAsync(seeded.PublicId, Ct);
            Assert.Equal(WorkItemStatus.Cancelled, item.Status);
            var attempt = Assert.Single(item.Attempts);
            Assert.Equal(AttemptStatus.Cancelled, attempt.Status);
            Assert.Equal(2718, attempt.Launch!.Pid);
        }
    }

    [Fact]
    public async Task An_attempt_cancelled_before_the_handler_started_is_never_run()
    {
        var command = FakeCommand.Returning(new CommandOutcome.Completed(null));
        using var harness = new DispatchHarness(Noon, commands: command);
        var seeded = await harness.SeedClaimableAsync(Ct);
        var work = Assert.Single(await harness.ClaimAsync(Ct));
        await CancelThroughTheApiAsync(harness, work.AttemptPublicId);

        await RunAsync(harness, work);

        Assert.Empty(command.Contexts);
        var item = await harness.ReadItemAsync(seeded.PublicId, Ct);
        Assert.Equal(WorkItemStatus.Cancelled, item.Status);
        Assert.Null(Assert.Single(item.Attempts).StartedAt);
    }

    private static Task RunAsync(DispatchHarness harness, ClaimedWork work) =>
        AttemptHandler.RunAsync(harness.Scopes, work, harness.Registry, NullLogger.Instance);

    /// <summary>
    /// A provider attempt as a claim leaves it, written without the claim: what is proven here is how the
    /// handler answers for an outcome, and which plugin would have run it is the pre-flight's subject. No plan
    /// is carried, because neither outcome here is one a plugin answered.
    /// </summary>
    private static async Task<(string WorkItemId, ClaimedWork Work)> SeedClaimedProviderAsync(DispatchHarness harness)
    {
        await using var db = harness.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var item = WorkItemFactory.NewProviderOp(campaign, now: Noon);
        item.Status = WorkItemStatus.Scheduled;
        var attempt = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Scheduled, Noon);
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(item);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(Ct);
        return (item.PublicId, new ClaimedWork(item.Id, attempt.Id, item.PublicId, attempt.PublicId));
    }

    private static AttemptLaunchDto Launch(int? pid) => new(["agent-host"], "work", pid, null);

    /// <summary>What the executor operations do, without the service that will own them.</summary>
    private static async Task CompleteThroughTheApiAsync(DispatchHarness harness, string attemptId)
    {
        await using var db = harness.Open();
        var attempt = await db.Attempts.Include(a => a.WorkItem).SingleAsync(a => a.PublicId == attemptId, Ct);
        var item = attempt.WorkItem!;
        var outcomes = new AttemptOutcomes(new JournalWriter(harness.Clock), harness.Clock, TestOptions.Settings(harness.Options));
        outcomes.Succeed(db, item, attempt, null, Actors.ForAttempt(attempt));
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>What the shared canceller does: the attempt is dropped and the item ends.</summary>
    private static async Task CancelThroughTheApiAsync(DispatchHarness harness, string attemptId)
    {
        await using var db = harness.Open();
        var attempt = await db.Attempts.Include(a => a.WorkItem).SingleAsync(a => a.PublicId == attemptId, Ct);
        var item = attempt.WorkItem!;
        new AttemptOutcomes(new JournalWriter(harness.Clock), harness.Clock, TestOptions.Settings(harness.Options)).CancelAttempt(attempt);
        WorkItemTransitions.Apply(item, WorkItemStatus.Cancelled, harness.Clock.Now.UtcDateTime);
        await db.SaveChangesAsync(Ct);
    }
}
