using Jason.Contracts.Api;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Execution;

namespace Jason.Runtime.Tests.Dispatch;

public class HandlerPoolTests
{
    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_slot_is_taken_while_the_handler_runs_and_given_back_when_it_ends()
    {
        var gate = new TaskCompletionSource<CommandOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var harness = new DispatchHarness(Noon, commands: Blocking(gate));
        await harness.SeedClaimableAsync(Ct);
        var work = Assert.Single(await harness.ClaimAsync(Ct));
        var pool = harness.Pool;
        Assert.Equal(4, pool.MaxParallel);
        Assert.Equal(4, pool.FreeSlots);

        pool.Dispatch(work);

        Assert.True(await DispatchHarness.EventuallyAsync(() => pool.FreeSlots == 3, Ct));
        Assert.Equal(work.AttemptPublicId, Assert.Single(pool.InFlightAttemptIds));

        gate.SetResult(new CommandOutcome.Completed(null));

        Assert.True(await DispatchHarness.EventuallyAsync(() => pool.FreeSlots == 4, Ct));
        Assert.Empty(pool.InFlightAttemptIds);
    }

    [Fact]
    public async Task Dispatching_without_a_free_slot_is_a_bug_in_the_claim()
    {
        var gate = new TaskCompletionSource<CommandOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var harness = new DispatchHarness(Noon, o => o.MaxParallel = 1, commands: Blocking(gate));
        await harness.SeedClaimableAsync(Ct);
        var work = Assert.Single(await harness.ClaimAsync(Ct));
        var pool = harness.Pool;

        pool.Dispatch(work);
        Assert.True(await DispatchHarness.EventuallyAsync(() => pool.FreeSlots == 0, Ct));

        Assert.Throws<InvalidOperationException>(() => pool.Dispatch(new ClaimedWork(999, 999, "wi_absent", "att_absent", "cmp_absent")));

        gate.SetResult(new CommandOutcome.Completed(null));
        Assert.True(await pool.DrainAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Draining_waits_for_the_work_in_flight()
    {
        var gate = new TaskCompletionSource<CommandOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var harness = new DispatchHarness(Noon, commands: Blocking(gate));
        await harness.SeedClaimableAsync(Ct);
        var work = Assert.Single(await harness.ClaimAsync(Ct));
        var pool = harness.Pool;
        pool.Dispatch(work);
        Assert.True(await DispatchHarness.EventuallyAsync(() => pool.FreeSlots == 3, Ct));

        Assert.False(await pool.DrainAsync(TimeSpan.FromMilliseconds(200)));

        gate.SetResult(new CommandOutcome.Completed(null));

        Assert.True(await pool.DrainAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Draining_an_idle_pool_is_immediate()
    {
        using var harness = new DispatchHarness(Noon);

        Assert.True(await harness.Pool.DrainAsync(TimeSpan.Zero));
    }

    /// <summary>
    /// The line a campaign-filtered log needs most is the one that says a handler gave out: a database that
    /// failed under it, a claim it could not run. It is written by the pool, after the handler has left, so it
    /// names the campaign and the work item as well as the attempt only because the pool says so itself.
    /// </summary>
    [Fact]
    public async Task A_handler_that_gives_out_says_so_under_its_campaign_work_item_and_attempt()
    {
        using var harness = new DispatchHarness(Noon, commands: FakeCommand.Returning(new CommandOutcome.Completed(null)));
        await harness.SeedClaimableAsync(Ct);
        var work = Assert.Single(await harness.ClaimAsync(Ct));

        // The handler's first save is the one that says the work has begun; it throws instead.
        harness.InterfereOnceBeforeSaving(() => throw new InvalidOperationException("the database went away"));
        harness.Pool.Dispatch(work);
        Assert.True(await harness.Pool.DrainAsync(TimeSpan.FromSeconds(5)));

        var failed = Assert.Single(harness.Logs.Lines, line => line.Message.StartsWith("The handler of attempt", StringComparison.Ordinal));
        Assert.Equal($"The handler of attempt {work.AttemptPublicId} ended unexpectedly", failed.Message);
        Assert.Equal(work.CampaignPublicId, failed.Scope.GetValueOrDefault("CampaignId"));
        Assert.Equal(work.WorkItemPublicId, failed.Scope.GetValueOrDefault("WorkItemId"));
        Assert.Equal(work.AttemptPublicId, failed.Scope.GetValueOrDefault("AttemptId"));
    }

    /// <summary>
    /// A handler that gave out while reading JSON is said by the kind of failure and where the reader stopped. The
    /// parser's message quotes what it read and its path names keys a person typed, and the sink writes an exception
    /// whole, so the exception is not handed to the log; the line still names its three ids.
    /// </summary>
    [Fact]
    public async Task A_handler_that_gives_out_reading_json_is_said_by_where_it_stopped_and_not_by_what_it_read()
    {
        using var harness = new DispatchHarness(Noon, commands: FakeCommand.Returning(new CommandOutcome.Completed(null)));
        await harness.SeedClaimableAsync(Ct);
        var work = Assert.Single(await harness.ClaimAsync(Ct));

        harness.InterfereOnceBeforeSaving(() => throw new InvalidOperationException("the context could not be read", UnreadableJson.Failure()));
        harness.Pool.Dispatch(work);
        Assert.True(await harness.Pool.DrainAsync(TimeSpan.FromSeconds(5)));

        var failed = Assert.Single(harness.Logs.Lines, line => line.Message.StartsWith("The handler of attempt", StringComparison.Ordinal));
        Assert.Equal($"The handler of attempt {work.AttemptPublicId} ended unexpectedly: {UnreadableJson.Described}", failed.Message);
        Assert.Null(failed.Exception);
        Assert.Equal(work.CampaignPublicId, failed.Scope.GetValueOrDefault("CampaignId"));
        Assert.False(UnreadableJson.ReachedTheLog(harness.Logs), "what the parser read reached the log");
    }

    private static FakeCommand Blocking(TaskCompletionSource<CommandOutcome> gate) => new(WorkItemKind.AiRole, _ => gate.Task);
}
