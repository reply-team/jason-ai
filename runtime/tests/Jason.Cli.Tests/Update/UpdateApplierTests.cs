using Jason.Cli.Update;
using Jason.Contracts.Api;
using Jason.Contracts.Update;

namespace Jason.Cli.Tests.Update;

/// <summary>
/// The whole sequence, against an installation this test built: staged, drained, stopped, kept, swapped,
/// started, healthy, complete. Nothing here starts a process or opens a socket.
/// </summary>
public class UpdateApplierTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_whole_sequence_leaves_the_new_version_installed_and_running()
    {
        using var installation = new FakeInstallation().WithRuntime(runningAttempts: 1);

        var ledger = await installation.Applier().ApplyAsync(Request(installation), Ct);

        Assert.Equal(UpdateStep.Complete, ledger.Step);
        Assert.Equal(installation.To.ToString(), installation.Installed());
        Assert.True(installation.Running, "the update left no runtime running");

        // What it asked the runtime to *do*, in order, ignoring what it read along the way: the state of the
        // dispatcher, and where the chronicle stood when the new version was healthy.
        Assert.Equal(
            ["system.drain", "system.shutdown"],
            installation.Operations.Where(operation => operation is not ("system.info" or "journal.list")));

        // The old executable is kept, and the staged copy is gone: it is the installed one now.
        Assert.Equal(installation.From.ToString(), File.ReadAllText(installation.Update.PreviousExecutable).Replace("jason ", string.Empty, StringComparison.Ordinal).Trim());
        Assert.False(File.Exists(installation.Update.StagedExecutable(installation.To)));
    }

    /// <summary>
    /// Every step is written down before it is taken, so the ledger on disk always names the last step that was
    /// begun — which is what makes a kill anywhere recoverable.
    /// </summary>
    /// <remarks>
    /// Asserted by watching the file rather than by trusting the return value: the ledger's whole purpose is to
    /// be right on disk at the moment a process dies, and a record written afterwards would be a record of what
    /// happened rather than an instruction for whoever comes next.
    /// </remarks>
    [Fact]
    public async Task Each_step_is_on_disk_before_it_is_taken()
    {
        using var installation = new FakeInstallation().WithRuntime();

        await installation.Applier().ApplyAsync(Request(installation), Ct);

        // The runtime is asked to drain only once the file says "drained", and asked to stop only once it says
        // "stopped". What the runtime saw is what a process arriving after a kill would have seen.
        Assert.Equal(UpdateStep.Drained, Witness(installation, "system.drain"));
        Assert.Equal(UpdateStep.Stopped, Witness(installation, "system.shutdown"));

        // And the health check happens under "healthy", after the start it is checking.
        Assert.Equal(UpdateStep.Healthy, installation.Witnessed[^1].Step);
    }

    /// <summary>
    /// The steps that leave no trace in the runtime leave one on the filesystem, and the kill-point tests read
    /// them there. This one keeps the pair honest: the sequence the applier reports is the sequence of steps.
    /// </summary>
    [Fact]
    public async Task The_applier_says_what_it_did_in_the_order_it_did_it()
    {
        using var installation = new FakeInstallation().WithRuntime();
        var applier = installation.Applier();

        await applier.ApplyAsync(Request(installation), Ct);

        Assert.Equal(
            ["staged", "drained", "stopped", "kept", "swapped", "started", "healthy"],
            applier.Steps.Select(step => step.Split(' ', ':')[0]).Take(7));
    }

    /// <summary>The swap moves files: the staged file becomes the installed one, and nothing is copied.</summary>
    [Fact]
    public async Task The_swap_is_a_rename_and_the_staged_file_is_gone_afterwards()
    {
        using var installation = new FakeInstallation().WithRuntime();

        await installation.Applier().ApplyAsync(Request(installation), Ct);

        Assert.False(
            Directory.Exists(installation.Update.StagedFor(installation.To)) &&
            File.Exists(installation.Update.StagedExecutable(installation.To)),
            "the staged executable is still there, so what was installed is a copy rather than the file that was proved");
        Assert.True(File.Exists(installation.Update.PreviousExecutable));
    }

    /// <summary>
    /// The health check asks the runtime that is now serving, not the file: an applier that swapped nothing gets
    /// the old version back and says so.
    /// </summary>
    [Fact]
    public async Task An_update_that_did_not_really_swap_fails_its_health_check()
    {
        using var installation = new FakeInstallation().WithRuntime();

        // The swap is undone behind the applier's back at the moment it starts the runtime, which is what a
        // rename that silently did nothing would look like from here: the runtime that comes up is the old build.
        installation.OnStart = () => File.WriteAllText(installation.InstallPath, $"jason {installation.From}");

        var refused = await Assert.ThrowsAsync<UpdateException>(
            () => installation.Applier().ApplyAsync(Request(installation), Ct));

        Assert.Equal(UpdateCodes.NotHealthy, refused.Code);
        Assert.Contains(installation.From.ToString(), refused.Message, StringComparison.Ordinal);
        Assert.Contains("rollback", refused.Message, StringComparison.Ordinal);

        // And it stopped there: the ledger still says the step it was on, for a rollback to read.
        Assert.Equal(UpdateStep.Healthy, installation.Ledger()!.Step);
    }

    private static UpdateStep? Witness(FakeInstallation installation, string operation) =>
        installation.Witnessed.First(w => w.Operation == operation).Step;

    /// <summary>
    /// What the new runtime's first start did to the database is recorded while it is answering, because a
    /// rollback an hour later cannot ask it any more.
    /// </summary>
    [Fact]
    public async Task A_completed_update_records_what_the_first_start_did_to_the_database()
    {
        using var installation = new FakeInstallation { Migrates = true };
        installation.WithRuntime();

        var ledger = await installation.Applier().ApplyAsync(Request(installation), Ct);

        Assert.Equal(UpdateStep.Complete, ledger.Step);
        Assert.NotEmpty(ledger.NewlyApplied);
        Assert.NotNull(ledger.BackupFile);
        Assert.Equal(ledger.ToJson(), installation.Ledger()!.ToJson());
    }

    /// <summary>And an update that migrated nothing says that, rather than leaving a rollback to guess.</summary>
    [Fact]
    public async Task An_update_that_migrated_nothing_records_that_too()
    {
        using var installation = new FakeInstallation().WithRuntime();

        var ledger = await installation.Applier().ApplyAsync(Request(installation), Ct);

        Assert.Empty(ledger.NewlyApplied);
        Assert.Null(ledger.BackupFile);
    }

    /// <summary>
    /// Before the install path is emptied, a copy of the executable performing the update is put where a person
    /// can still reach it — because between that step and the swap there is no `jason` on the PATH to type.
    /// </summary>
    /// <remarks>
    /// The copy is the build that is running the update, not the one being installed: whatever goes wrong next,
    /// the thing that decides how to get out of it is the version that was reviewed. It is an ordinary Jason, so
    /// the way out is the ordinary command — `jason update apply`, run from the copy, reads the ledger and
    /// finishes the update, because a resumed update takes its paths from the ledger and not from where it is
    /// running.
    /// </remarks>
    [Fact]
    public async Task A_copy_of_the_running_build_is_left_where_a_person_can_reach_it()
    {
        using var installation = new FakeInstallation().WithRuntime();

        await installation.Applier().ApplyAsync(Request(installation), Ct);

        var copy = Path.Combine(installation.Update.Applier, Jason.Contracts.Update.ReleaseAssets.ExecutableName);
        Assert.True(File.Exists(copy), "there is no applier to run if the install path is emptied and the process dies");
        Assert.Equal($"jason {installation.From}", File.ReadAllText(copy));
    }

    private static UpdateRequest Request(FakeInstallation installation) =>
        new(installation.Feed, null, TimeSpan.FromSeconds(5));
}
