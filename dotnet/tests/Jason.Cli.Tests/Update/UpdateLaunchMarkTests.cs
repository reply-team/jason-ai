using Jason.Cli.Commands;
using Jason.Cli.Process;
using Jason.Cli.Tests.Commands;
using Jason.Cli.Tests.Process;
using Jason.Cli.Update;
using Jason.Contracts.Discovery;
using Jason.Contracts.Update;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Update;

/// <summary>
/// The update's own launches of the runtime carry its mark, and no other launch does. From the step that moves the
/// installed build aside on, <c>jason runtime run</c> starts only a run the update marked, so the apply's start and the
/// rollback's have to say they are the update's, and <c>jason runtime start</c> must never pass a mark on.
/// </summary>
public class UpdateLaunchMarkTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_applys_start_carries_the_mark_of_the_step_it_is_made_at()
    {
        using var installation = new FakeInstallation().WithRuntime();
        installation.Killed(UpdateStep.Swapped);

        await installation.Applier().ApplyAsync(new UpdateRequest(installation.Feed, null, TimeSpan.FromSeconds(5)), Ct);

        Assert.Equal("started", Assert.Single(installation.Processes.UpdateLaunches));
    }

    [Fact]
    public async Task The_rollbacks_start_carries_the_mark_of_the_step_the_update_stopped_at()
    {
        using var installation = new FakeInstallation().WithRuntime();
        installation.Killed(UpdateStep.Healthy);

        await installation.Rollback().RollBackAsync(Ct);

        Assert.Equal("healthy", installation.Processes.UpdateLaunches[^1]);
    }

    /// <summary>
    /// A sign-in's run can hold the lock for an instant, and the apply's own run then exits as a second runtime. The
    /// apply ends there, in that run's own words, rather than waiting its minute for an answer that cannot come; and it
    /// starts nothing again, so `jason update apply` typed once more resumes at the start. Its clock is never moved:
    /// an apply that waits for the minute never ends.
    /// </summary>
    [Fact]
    public async Task The_apply_ends_at_once_in_its_runtimes_own_words_when_that_runtime_exits()
    {
        using var installation = new FakeInstallation().WithRuntime();
        installation.Killed(UpdateStep.Swapped);
        installation.Processes.OnLaunch = _ => Exited();
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        // The apply ends on the runtime's exit, never on this bound; it only fails a hang, and is sized to the slowest runner.
        bounded.CancelAfter(TimeSpan.FromSeconds(60));

        var refused = await Assert.ThrowsAsync<UpdateException>(() =>
            installation.Applier(Unmoved()).ApplyAsync(new UpdateRequest(installation.Feed, null, TimeSpan.FromSeconds(5)), bounded.Token));

        Assert.Equal(UpdateCodes.NotHealthy, refused.Code);
        Assert.Contains(SecondRuntime, refused.Message, StringComparison.Ordinal);
        Assert.Contains("exited with code 1", refused.Message, StringComparison.Ordinal);
        Assert.Single(installation.Processes.UpdateLaunches);
        Assert.Equal(UpdateStep.Started, installation.Ledger()!.Step);
    }

    /// <summary>The rollback's own start, read the same way: it says what that run said and leaves the start to a person.</summary>
    [Fact]
    public async Task The_rollback_ends_at_once_in_its_runtimes_own_words_when_that_runtime_exits()
    {
        using var installation = new FakeInstallation().WithRuntime();
        installation.Killed(UpdateStep.Healthy);
        installation.Processes.OnLaunch = _ => Exited();
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        // The apply ends on the runtime's exit, never on this bound; it only fails a hang, and is sized to the slowest runner.
        bounded.CancelAfter(TimeSpan.FromSeconds(60));

        var rollback = installation.Rollback(Unmoved());
        await rollback.RollBackAsync(bounded.Token);

        Assert.Equal(installation.From.ToString(), installation.Installed());
        Assert.Contains(rollback.Steps, step => step.Contains(SecondRuntime, StringComparison.Ordinal)
            && step.Contains("exited with code 1", StringComparison.Ordinal)
            && step.Contains("jason runtime start", StringComparison.Ordinal));
        Assert.DoesNotContain(rollback.Steps, step => step.Contains("no runtime answered", StringComparison.Ordinal));
    }

    private const string SecondRuntime =
        "Another Jason runtime already holds the instance lock 'C:/data/run/runtime.lock'. Exactly one runtime per user may run.";

    private static FakeProcessHandle Exited() => new(4243) { HasExited = true, ExitCode = 1, Said = SecondRuntime };

    private static FixedClock Unmoved() => new(new DateTimeOffset(2026, 9, 20, 4, 30, 0, TimeSpan.Zero));

    [Fact]
    public async Task Runtime_start_launches_without_a_mark()
    {
        using var dir = new TempPaths();
        var processes = new FakeProcessControl { OnLaunch = RuntimeVerbs.PublishesAfterAWhile(dir, RuntimeVerbs.Descriptor("rt_NEW")) };
        var (env, _, _) = RuntimeVerbs.Environment(dir, RuntimeVerbs.EchoesTheDescriptor(dir), processes);

        var exit = await RuntimeStartCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Null(Assert.Single(processes.UpdateLaunches));
    }
}

/// <summary>
/// The launch as the operating system receives it. It sets the mark in this process's own environment to see that a
/// plain launch drops what it would otherwise inherit, so it runs apart from every other test in the assembly.
/// </summary>
[Collection(UpdateLaunchEnvironmentCollection.Name)]
public class UpdateLaunchEnvironmentTests
{
    /// <summary>
    /// What the operating system is handed: the mark in the child's environment for the update's launch, never on its
    /// command line; and for any other launch, no mark at all, even where this process has one set.
    /// </summary>
    [Fact]
    public void The_launch_puts_the_mark_in_the_childs_environment_only_when_it_is_the_updates()
    {
        using var dir = new TempPaths();
        var original = Environment.GetEnvironmentVariable(UpdateLaunch.Variable);
        try
        {
            Environment.SetEnvironmentVariable(UpdateLaunch.Variable, "inherited");

            var marked = RuntimeProcessControl.LaunchStart(dir.Paths, ["jason"], "started");
            Assert.Equal("started", marked.Environment[UpdateLaunch.Variable]);
            Assert.DoesNotContain(marked.ArgumentList, argument => argument.Contains("started", StringComparison.Ordinal));

            var plain = RuntimeProcessControl.LaunchStart(dir.Paths, ["jason"], updateLaunch: null);
            Assert.False(plain.Environment.ContainsKey(UpdateLaunch.Variable), "A launch that is not the update's passed a mark on.");
        }
        finally
        {
            Environment.SetEnvironmentVariable(UpdateLaunch.Variable, original);
        }
    }
}

/// <summary>The tests that change this process's own update mark, kept away from everything else while they run.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class UpdateLaunchEnvironmentCollection
{
    public const string Name = "update launch environment";
}
