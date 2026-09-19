using Jason.Cli.Update;
using Jason.Contracts.Api;
using Jason.Contracts.Update;

namespace Jason.Cli.Tests.Update;

/// <summary>
/// A kill between every pair of steps. Each test puts the installation in the state a process dying there leaves
/// behind — the ledger on disk and the effects of every step up to it — and then runs <c>jason update apply</c>
/// again, which has to finish the update or put the installation back, and say which.
/// </summary>
/// <remarks>
/// The ledger is written <b>before</b> the step it names, so "the ledger says <c>kept</c>" is what the next
/// process sees whether or not the keeping finished, and every step is written so that beginning it twice is the
/// same as beginning it once. What makes these tests more than a second happy path is the moment the killed run
/// wrote down: a resumed update keeps it, and one that quietly started again would stamp its own.
/// </remarks>
public class UpdateKillPointTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Nothing downloaded is trusted across a kill: whatever is in the staging directory was left there by a run
    /// that did not finish, so the next one empties it and fetches the release again.
    /// </summary>
    [Fact]
    public async Task A_kill_after_staging_leaves_bytes_the_next_run_deletes()
    {
        using var installation = new FakeInstallation().WithRuntime();
        var killed = installation.Killed(UpdateStep.Staged);

        // What a run that died in the staging leaves: bytes nothing proved. The digest that would have proved
        // them was checked by the process that is gone, so the next run has no reason to believe any of it.
        File.WriteAllText(installation.Update.StagedExecutable(installation.To), "not the release");
        var leftover = Path.Combine(installation.Update.StagedFor(installation.To), "download");
        File.WriteAllText(leftover, "half an archive");

        var applier = installation.Applier();
        var ledger = await applier.ApplyAsync(Request(installation), Ct);

        Assert.Equal(UpdateStep.Complete, ledger.Step);
        Assert.Equal(killed.StartedAt, ledger.StartedAt);
        Assert.StartsWith("staged", applier.Steps[0], StringComparison.Ordinal);

        // What was installed came from the release and not from the disk, and nothing of the killed run's is
        // left behind it.
        Assert.Equal($"jason {installation.To}", File.ReadAllText(installation.InstallPath));
        Assert.Equal(installation.To.ToString(), installation.Installed());
        Assert.Empty(Directory.EnumerateFiles(installation.Update.StagedFor(installation.To)));
        Assert.True(installation.Running);
    }

    /// <summary>
    /// A drained runtime is still a runtime: it is answering, it is claiming nothing new, and the next run picks
    /// the update up there rather than fetching the release a second time.
    /// </summary>
    [Fact]
    public async Task A_kill_after_the_drain_leaves_a_draining_runtime_the_next_run_carries_on_from()
    {
        using var installation = new FakeInstallation().WithRuntime(runningAttempts: 1);
        var killed = installation.Killed(UpdateStep.Drained);

        Assert.Equal(DispatcherState.Draining, installation.State);
        Assert.True(installation.Running);

        var applier = installation.Applier();
        var ledger = await applier.ApplyAsync(Request(installation), Ct);

        Assert.Equal(UpdateStep.Complete, ledger.Step);
        Assert.Equal(killed.StartedAt, ledger.StartedAt);
        Assert.Empty(installation.Fetched);
        Assert.StartsWith("drained", applier.Steps[0], StringComparison.Ordinal);
        Assert.Contains("system.shutdown", installation.Operations);
        Assert.Equal(installation.To.ToString(), installation.Installed());
    }

    /// <summary>
    /// The runtime is gone and the ledger says why, so the next run neither drains something that is not there
    /// nor wonders whether the machine it is on was in the middle of something.
    /// </summary>
    [Fact]
    public async Task A_kill_after_the_stop_leaves_a_stopped_runtime_and_a_ledger_that_says_so()
    {
        using var installation = new FakeInstallation().WithRuntime();
        var killed = installation.Killed(UpdateStep.Stopped);

        Assert.False(installation.Running);
        Assert.Equal(UpdateStep.Stopped, installation.Ledger()!.Step);
        Assert.NotNull(killed.StoppedAt);

        var applier = installation.Applier();
        var ledger = await applier.ApplyAsync(Request(installation), Ct);

        Assert.Equal(UpdateStep.Complete, ledger.Step);
        Assert.Equal(killed.StoppedAt, ledger.StoppedAt);
        Assert.DoesNotContain("system.drain", installation.Operations);
        Assert.Empty(installation.Fetched);
        Assert.Contains("nothing to stop", applier.Steps[0], StringComparison.Ordinal);
        Assert.Equal(installation.To.ToString(), installation.Installed());
        Assert.True(installation.Running);
    }

    /// <summary>
    /// The one window this design cannot avoid: the old executable has been moved aside and the new one is not
    /// in place yet, so the install path is <b>empty</b> and there is no <c>jason</c> on the PATH to type. There
    /// are two ways out of it, and neither of them is the command a person would reach for first.
    /// </summary>
    /// <remarks>
    /// Both are asserted, on an installation each, because they are exclusive: the first finishes the update and
    /// the second abandons it. The first is the copy of the build performing the update, left under
    /// <c>&lt;data&gt;/update/applier/</c> before the window opens — an ordinary Jason, which reads the ledger
    /// and finishes what it finds because a resumed update takes its paths from the ledger rather than from
    /// wherever it is running. The second needs no Jason at all.
    /// </remarks>
    [Fact]
    public async Task A_kill_after_keeping_leaves_the_install_path_empty_and_two_ways_back()
    {
        using (var installation = new FakeInstallation().WithRuntime())
        {
            var killed = installation.Killed(UpdateStep.Kept);

            Assert.False(File.Exists(installation.InstallPath), "the window this repair is for is not the window the machine leaves");
            Assert.Equal("nothing", installation.Installed());

            // Way out one: the copy of the build that was performing the update.
            var copy = Path.Combine(installation.Update.Applier, ReleaseAssets.ExecutableName);
            Assert.True(File.Exists(copy), "there is no jason anywhere for a person to run");
            Assert.Equal($"jason {installation.From}", File.ReadAllText(copy));

            var applier = new UpdateApplier(installation.Env, installation.Update, TimeProvider.System, copy);
            var ledger = await applier.ApplyAsync(Request(installation), Ct);

            Assert.Equal(UpdateStep.Complete, ledger.Step);
            Assert.Equal(killed.StartedAt, ledger.StartedAt);
            Assert.Equal(installation.To.ToString(), installation.Installed());
            Assert.True(installation.Running);

            // It finished the update the ledger named rather than starting one of its own: the install path it
            // was constructed with is the copy, and what it installed is the path the ledger carries.
            Assert.Equal(installation.InstallPath, ledger.InstallPath);
            Assert.Empty(installation.Fetched);
        }

        using (var byHand = new FakeInstallation().WithRuntime())
        {
            byHand.Killed(UpdateStep.Kept);
            Assert.False(File.Exists(byHand.InstallPath));

            // Way out two: put previous/ back. It needs no Jason, and it returns the installation it began as.
            File.Copy(byHand.Update.PreviousExecutable, byHand.InstallPath);

            Assert.Equal(byHand.From.ToString(), byHand.Installed());
            byHand.Processes.Launch(byHand.Paths, [byHand.InstallPath]);
            Assert.True(byHand.Running);
            Assert.Equal(byHand.From.ToString(), byHand.Installed());
        }
    }

    /// <summary>The new executable is in place and nothing is serving from it, so the next run starts it.</summary>
    [Fact]
    public async Task A_kill_after_the_swap_leaves_a_new_binary_the_next_run_starts()
    {
        using var installation = new FakeInstallation().WithRuntime();
        var killed = installation.Killed(UpdateStep.Swapped);

        Assert.Equal(installation.To.ToString(), installation.Installed());
        Assert.False(installation.Running);

        var applier = installation.Applier();
        var ledger = await applier.ApplyAsync(Request(installation), Ct);

        Assert.Equal(UpdateStep.Complete, ledger.Step);
        Assert.Equal(killed.StartedAt, ledger.StartedAt);
        Assert.True(installation.Running);

        // Started by path, and the path is the installation's rather than whatever is running this.
        Assert.Equal([installation.InstallPath], installation.Processes.Executables[^1]);
        Assert.Equal(["started", "healthy"], applier.Steps.Take(2).Select(step => step.Split(' ', ':')[0]));

        // And nothing was moved twice: what was kept is still what was kept.
        Assert.Equal($"jason {installation.From}", File.ReadAllText(installation.Update.PreviousExecutable));
        Assert.Empty(installation.Fetched);
    }

    /// <summary>
    /// A runtime came up, and whether it is the right one is a question nobody answered before the kill. The
    /// next run answers it — including when the answer is no.
    /// </summary>
    [Fact]
    public async Task A_kill_after_the_start_leaves_a_runtime_whose_health_the_next_run_asserts()
    {
        using var installation = new FakeInstallation().WithRuntime();
        var killed = installation.Killed(UpdateStep.Started);

        Assert.True(installation.Running);

        var applier = installation.Applier();
        var ledger = await applier.ApplyAsync(Request(installation), Ct);

        Assert.Equal(UpdateStep.Complete, ledger.Step);
        Assert.Equal(killed.StartedAt, ledger.StartedAt);
        Assert.Empty(installation.Processes.Launches);
        Assert.Contains(applier.Steps, step => step.StartsWith("healthy", StringComparison.Ordinal));

        // The health check is really made rather than assumed from the ledger: a runtime that came up as the old
        // build — a swap that silently did nothing — is refused, with the version it is and the way back.
        using var wrong = new FakeInstallation().WithRuntime();
        wrong.Killed(UpdateStep.Started);
        File.WriteAllText(wrong.InstallPath, $"jason {wrong.From}");

        var refused = await Assert.ThrowsAsync<UpdateException>(() => wrong.Applier().ApplyAsync(Request(wrong), Ct));

        Assert.Equal(UpdateCodes.NotHealthy, refused.Code);
        Assert.Contains(wrong.From.ToString(), refused.Message, StringComparison.Ordinal);
        Assert.Equal(UpdateStep.Healthy, wrong.Ledger()!.Step);
    }

    /// <summary>
    /// Everything is done and nothing said so. The next run says so — and what it writes down is the record a
    /// rollback reads an hour later, when the runtime cannot be asked any more.
    /// </summary>
    [Fact]
    public async Task A_kill_after_health_completes_on_the_next_run()
    {
        using var installation = new FakeInstallation { Migrates = true };
        installation.WithRuntime();
        var killed = installation.Killed(UpdateStep.Healthy);

        var ledger = await installation.Applier().ApplyAsync(Request(installation), Ct);

        Assert.Equal(UpdateStep.Complete, ledger.Step);
        Assert.Equal(killed.StartedAt, ledger.StartedAt);
        Assert.Equal(UpdateStep.Complete, installation.Ledger()!.Step);
        Assert.Empty(installation.Fetched);
        Assert.Empty(installation.Processes.Launches);
        Assert.NotEmpty(ledger.NewlyApplied);
        Assert.NotNull(ledger.BackupFile);
    }

    /// <summary>
    /// The same window, on an installation whose runtime was not running when the update began — which is not a
    /// corner but how an installation nobody has started yet is updated.
    /// </summary>
    /// <remarks>
    /// It is repaired the same two ways, and the update leaves a runtime running: there was none before, and
    /// there is one afterwards, because what an update finishes with is the new version serving.
    /// </remarks>
    [Fact]
    public async Task A_kill_between_the_stop_and_the_swap_of_an_installation_that_was_not_running_is_repaired_the_same_way()
    {
        using (var installation = new FakeInstallation())
        {
            Assert.False(installation.Running);
            var killed = installation.Killed(UpdateStep.Kept);

            Assert.False(File.Exists(installation.InstallPath));

            var copy = Path.Combine(installation.Update.Applier, ReleaseAssets.ExecutableName);
            Assert.True(File.Exists(copy));

            var applier = new UpdateApplier(installation.Env, installation.Update, TimeProvider.System, copy);
            var ledger = await applier.ApplyAsync(Request(installation), Ct);

            Assert.Equal(UpdateStep.Complete, ledger.Step);
            Assert.Equal(killed.StartedAt, ledger.StartedAt);
            Assert.Equal(installation.To.ToString(), installation.Installed());
            Assert.Empty(installation.Fetched);
            Assert.True(installation.Running, "an update leaves the new version serving, whatever it found running");
        }

        using (var byHand = new FakeInstallation())
        {
            byHand.Killed(UpdateStep.Kept);
            File.Copy(byHand.Update.PreviousExecutable, byHand.InstallPath);

            Assert.Equal(byHand.From.ToString(), byHand.Installed());
            Assert.False(byHand.Running, "nothing was started by putting a file back");
        }
    }

    /// <summary>
    /// The window above is the only one: at every moment the update asks the runtime anything, the install path
    /// is a whole executable. A step order that emptied it earlier would leave a person stranded for as long as
    /// a drain, a stop or a start takes, rather than for one rename.
    /// </summary>
    [Fact]
    public async Task The_install_path_is_a_whole_executable_at_every_moment_the_runtime_is_asked_anything()
    {
        using var installation = new FakeInstallation().WithRuntime(runningAttempts: 1);

        await installation.Applier().ApplyAsync(Request(installation), Ct);

        Assert.NotEmpty(installation.Witnessed);
        Assert.All(
            installation.Witnessed,
            witness => Assert.True(
                witness.Installed,
                $"the install path was empty when the runtime was asked {witness.Operation} under '{witness.Step}'"));
    }

    private static UpdateRequest Request(FakeInstallation installation) =>
        new(installation.Feed, null, TimeSpan.FromSeconds(5));
}
