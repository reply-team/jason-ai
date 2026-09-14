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

        Assert.Throws<InvalidOperationException>(() => pool.Dispatch(new ClaimedWork(999, 999, "wi_absent", "att_absent")));

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

    private static FakeCommand Blocking(TaskCompletionSource<CommandOutcome> gate) => new(WorkItemKind.AiRole, _ => gate.Task);
}
