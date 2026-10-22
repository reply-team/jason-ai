using Jason.Cli.Update;
using Jason.Contracts.Api;
using Jason.Contracts.Update;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Update;

/// <summary>
/// The whole sequence, against an installation this test built: staged, drained, stopped, kept, swapped,
/// started, healthy, complete. Nothing here starts a process or opens a socket.
/// </summary>
public class UpdateApplierTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// An update against a running runtime has to stop it, and the stopping is done by the same code
    /// <c>jason runtime stop</c> runs — which answers a person, on stdout. Here there is no person: the caller
    /// is whatever read the one JSON document this verb promises, and a second document in front of it is not a
    /// smaller answer but an unparseable one. What the stop did is already said in the steps.
    /// </summary>
    [Fact]
    public async Task An_update_that_stops_a_runtime_writes_nothing_to_the_callers_stdout()
    {
        using var installation = new FakeInstallation().WithRuntime();

        var ledger = await installation.Applier().ApplyAsync(Request(installation), Ct);

        Assert.Equal(UpdateStep.Complete, ledger.Step);
        Assert.Equal(string.Empty, installation.Out.ToString());
    }

    [Fact]
    [Critical(Family.I2, "update apply end to end (in process)")]
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
    [Critical(Family.I2, "the ledger is written ahead")]
    public async Task Each_step_is_on_disk_before_it_is_taken()
    {
        using var installation = new FakeInstallation().WithRuntime();

        await installation.Applier().ApplyAsync(Request(installation), Ct);

        // The runtime is asked to drain only once the file says "drained", and asked to stop only once it says
        // "stopped". What the runtime saw is what a process arriving after a kill would have seen.
        Assert.Equal(UpdateStep.Drained, Witness(installation, "system.drain"));
        Assert.Equal(UpdateStep.Stopped, Witness(installation, "system.shutdown"));

        // And the health check happens under "healthy", after the start it is checking. The one question after it is
        // the UI step's, which asks the new runtime which API it speaks.
        Assert.Equal(UpdateStep.Healthy, installation.Witnessed[^2].Step);
        Assert.Equal(("system.info", UpdateStep.Complete), (installation.Witnessed[^1].Operation, installation.Witnessed[^1].Step));
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
    /// The health check asks the runtime that is now serving, and not the file that was installed. A file with
    /// the right version in it proves a file exists; what an update has to know is which build came up.
    /// </summary>
    /// <remarks>
    /// The two are separated here on purpose: the new version is at the install path, and the runtime answering
    /// is still the old one — a start that silently kept serving the process that was already there, or a
    /// launcher that started the wrong file. A health check that read the install path would call this healthy.
    /// </remarks>
    [Fact]
    public async Task A_runtime_that_is_not_the_version_that_was_installed_fails_the_health_check()
    {
        using var installation = new FakeInstallation().WithRuntime();

        // Swapped for real; serving the old build regardless.
        installation.OnStart = () => installation.ServesVersion = installation.From.ToString();

        var refused = await Assert.ThrowsAsync<UpdateException>(
            () => installation.Applier().ApplyAsync(Request(installation), Ct));

        Assert.Equal(UpdateCodes.NotHealthy, refused.Code);
        Assert.Contains(installation.From.ToString(), refused.Message, StringComparison.Ordinal);

        // And the file really is the new one, so nothing but asking the runtime could have caught this.
        Assert.Equal(installation.To.ToString(), installation.Installed());
    }

    /// <summary>
    /// And a swap that did not happen at all is caught the same way, because the runtime that comes up is then
    /// the old build for the ordinary reason.
    /// </summary>
    [Fact]
    [Critical(Family.I2, "the health check sees the version")]
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

    /// <summary>
    /// A feed served from a plain directory — a manifest with the archive beside it — is read at the address it
    /// was given, and nothing composes another one from it.
    /// </summary>
    /// <remarks>
    /// This is the shape CI serves, and the shape that found the defect: staging pinned the update's own version
    /// into the address before re-reading it, which turns `http://host/manifest.json` into
    /// `http://host/v0.1.1/manifest.json` and asks for a path no such feed has. Every stub in this file was a
    /// GitHub-shaped address until now, so nothing here could see it.
    /// </remarks>
    [Fact]
    public async Task A_feed_served_from_a_directory_is_read_where_it_is_rather_than_where_one_might_be()
    {
        using var installation = new FakeInstallation().ServedFromADirectory();
        installation.WithRuntime();

        var ledger = await installation.Applier().ApplyAsync(Request(installation), Ct);

        Assert.Equal(UpdateStep.Complete, ledger.Step);
        Assert.Equal(installation.To.ToString(), installation.Installed());
        Assert.All(installation.Fetched, address => Assert.DoesNotContain("/v", address, StringComparison.Ordinal));

        // And the UI step reads the same directory: the two files beside the manifest, and nothing composed.
        Assert.Contains("/checksums.txt", installation.Release.Requested);
        Assert.Contains("/jason-ui.zip", installation.Release.Requested);
        Assert.All(installation.Release.Requested, address => Assert.DoesNotContain("/v", address, StringComparison.Ordinal));
    }

    /// <summary>
    /// Every line of the account names its step, whatever that step found. An installation whose runtime was not
    /// running still drains and still stops — there is simply nothing to drain and nothing to stop — and a reader
    /// looking for those steps must find them.
    /// </summary>
    /// <remarks>
    /// This is PB3's path, and the CI job that updates a stopped installation is what found the lines missing:
    /// "no runtime is running, so there is nothing to drain" is a true sentence that does not begin with the
    /// word a reader is looking for. Ten minutes of CI to learn it; one test to keep it.
    /// </remarks>
    [Fact]
    public async Task Every_step_is_named_even_where_it_found_nothing_to_do()
    {
        using var installation = new FakeInstallation();

        var applier = installation.Applier();
        await applier.ApplyAsync(Request(installation), Ct);

        // No runtime was ever started here, so the drain and the stop are the sentences that used to hide.
        Assert.Equal(
            ["staged", "drained", "stopped", "kept", "swapped", "started", "healthy", "complete", "skills", "ui"],
            applier.Steps.Select(step => step.Split(':')[0]));
        Assert.All(applier.Steps, step => Assert.Contains(": ", step, StringComparison.Ordinal));
    }

    /// <summary>
    /// An attempt that outlives the drain does not stop the update: the bound runs out, the update goes on, and
    /// what was left running is said out loud rather than passed over.
    /// </summary>
    /// <remarks>
    /// The lease is what covers it — the next runtime finishes the work or the enforcer takes it back — but a
    /// person whose update stopped a runtime with work in flight should be told, not left to find out. This
    /// branch had never run: the drain in the harness ended the work every time.
    /// </remarks>
    [Fact]
    public async Task An_attempt_that_outlives_the_drain_is_reported_and_left_to_its_lease()
    {
        using var installation = new FakeInstallation { DrainEndsTheWork = false };
        installation.WithRuntime(runningAttempts: 2);

        var applier = installation.Applier();
        await applier.ApplyAsync(new UpdateRequest(installation.Feed, null, TimeSpan.FromMilliseconds(400)), Ct);

        var drained = Assert.Single(applier.Steps, step => step.StartsWith("drained:", StringComparison.Ordinal));
        Assert.Contains("2 attempt(s) still running", drained, StringComparison.Ordinal);
        Assert.Contains("lease", drained, StringComparison.Ordinal);
        Assert.Equal(installation.To.ToString(), installation.Installed());
    }

    /// <summary>And `--drain-seconds 0` waits for none of it, and says the same thing.</summary>
    [Fact]
    public async Task A_drain_of_no_seconds_waits_for_nothing_and_says_what_was_left()
    {
        using var installation = new FakeInstallation { DrainEndsTheWork = false };
        installation.WithRuntime(runningAttempts: 1);

        var applier = installation.Applier();
        await applier.ApplyAsync(new UpdateRequest(installation.Feed, null, TimeSpan.Zero), Ct);

        var drained = Assert.Single(applier.Steps, step => step.StartsWith("drained:", StringComparison.Ordinal));
        Assert.Contains("1 attempt(s) still running", drained, StringComparison.Ordinal);
    }

    /// <summary>
    /// The steps that move files write their line before they move anything, which is what makes a kill in the
    /// middle of one recoverable — and the only way to see it from outside is to make the move fail.
    /// </summary>
    /// <remarks>
    /// The runtime witnesses `drained`, `stopped` and `healthy`, because it is asked something during each. The
    /// filesystem steps ask nobody anything, so moving their ledger write to *after* the rename left every test
    /// green. Here the rename cannot happen — a file sits where the kept executable's directory belongs — and
    /// what is asserted is the file on disk at the moment it failed.
    /// </remarks>
    [Fact]
    public async Task A_step_that_moves_files_has_written_its_line_before_it_fails()
    {
        using var installation = new FakeInstallation().WithRuntime();

        // Where `previous/` has to be a directory, there is a file.
        Directory.CreateDirectory(installation.Update.Root);
        File.WriteAllText(installation.Update.Previous, "not a directory");

        var refused = await Assert.ThrowsAsync<UpdateException>(
            () => installation.Applier().ApplyAsync(Request(installation), Ct));

        Assert.Equal(UpdateCodes.FileRefused, refused.Code);
        Assert.Contains(installation.Update.Previous, refused.Message, StringComparison.Ordinal);
        Assert.Contains("jason update apply", refused.Message, StringComparison.Ordinal);

        // The ledger on disk already says which step was being taken, which is what the next invocation reads.
        Assert.Equal(UpdateStep.Kept, installation.Ledger()!.Step);

        // And nothing was moved: the installed executable is where it was.
        Assert.Equal(installation.From.ToString(), installation.Installed());
    }

    /// <summary>
    /// A runtime that stopped but could not remove its descriptor has stopped: its process is gone, so nothing is
    /// replaced under a runtime that is using the file. The update used to refuse there as "would not stop"; it goes
    /// on to the swap and the start, and the stop's line about the descriptor reaches standard error.
    /// </summary>
    [Fact]
    public async Task An_update_goes_on_past_a_runtime_that_stopped_but_left_its_descriptor_behind()
    {
        using var installation = new FakeInstallation().WithRuntime();
        installation.LeavesItsDescriptorOnStop = true;

        var ledger = await installation.Applier().ApplyAsync(Request(installation), Ct);

        Assert.Equal(UpdateStep.Complete, ledger.Step);
        Assert.Equal(installation.To.ToString(), installation.Installed());
        Assert.True(installation.Running, "the update left no runtime running");
        Assert.Contains(
            $"Its endpoint descriptor at '{installation.Paths.DescriptorFile}' was left behind: the runtime could not remove it.",
            installation.Error.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain("would not stop", installation.Error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A runtime that ended without being stopped — at a sign-out, in a crash — leaves its descriptor behind, naming a
    /// process that has gone or, since, another program that was given its id. No runtime is running there, so the
    /// update drains and stops nothing, as on an installation with no descriptor at all, and goes on. It used to send the
    /// drain to the dead address and refuse with nothing after "would not drain:".
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_update_over_a_descriptor_whose_runtime_has_gone_drains_and_stops_nothing_and_completes(bool idTakenSince)
    {
        using var installation = new FakeInstallation().WithRuntime();
        installation.RuntimeDies();
        if (idTakenSince)
        {
            // Another program has the id now: it started an hour after the runtime the descriptor describes.
            installation.Processes.RunningPids.Add(FakeInstallation.Pid);
            installation.Processes.StartTimes[FakeInstallation.Pid] = DateTimeOffset.UnixEpoch.AddHours(1);
        }

        var applier = installation.Applier();
        var ledger = await applier.ApplyAsync(Request(installation), Ct);

        Assert.Equal(UpdateStep.Complete, ledger.Step);
        Assert.Equal(installation.To.ToString(), installation.Installed());
        Assert.Contains("drained: no runtime is running, so there is nothing to drain", applier.Steps);
        Assert.Contains("stopped: no runtime is running, so there is nothing to stop", applier.Steps);
        Assert.DoesNotContain("system.drain", installation.Operations);
        Assert.DoesNotContain("system.shutdown", installation.Operations);
        Assert.Equal(string.Empty, installation.Out.ToString());
    }

    /// <summary>
    /// A runtime whose process is running and which does not answer is asked to drain and refused on, as the runtime it
    /// may be — shown to have started when its descriptor says, or with a start this account may not read — and the refusal
    /// says why: what the call that could not reach it said, and nothing of it on stdout beside the refusal.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_drain_the_runtime_does_not_answer_refuses_with_its_reason(bool startUnreadable)
    {
        using var installation = new FakeInstallation().WithRuntime();
        installation.RuntimeDies();

        // Its process is still there, under its own id: it has only stopped answering.
        installation.Processes.RunningPids.Add(FakeInstallation.Pid);
        if (startUnreadable)
        {
            installation.Processes.StartTimes[FakeInstallation.Pid] = null;
        }

        var refused = await Assert.ThrowsAsync<UpdateException>(() => installation.Applier().ApplyAsync(Request(installation), Ct));

        Assert.Contains("system.drain", installation.Operations);
        Assert.Equal(UpdateCodes.RuntimeUnreachable, refused.Code);
        Assert.Equal(
            "The runtime would not drain: runtime_unreachable: The runtime at http://127.0.0.1:5000 did not answer (connection refused). The descriptor may be stale.",
            refused.Message);
        Assert.Equal(string.Empty, installation.Out.ToString());
        Assert.DoesNotContain("system.shutdown", installation.Operations);
        Assert.Equal(installation.From.ToString(), installation.Installed());
    }

    /// <summary>
    /// A runtime that answers the drain with an error has not drained, and the update is refused with the runtime's
    /// own code and message rather than going on to stop it as though it had.
    /// </summary>
    [Fact]
    public async Task An_error_answer_to_the_drain_refuses_the_update_with_its_code_and_message()
    {
        using var installation = new FakeInstallation { RefusesToDrain = true }.WithRuntime();

        var refused = await Assert.ThrowsAsync<UpdateException>(() => installation.Applier().ApplyAsync(Request(installation), Ct));

        Assert.Equal(UpdateCodes.RuntimeUnreachable, refused.Code);
        Assert.Equal("The runtime would not drain: internal: the dispatcher could not be paused", refused.Message);
        Assert.Equal(string.Empty, installation.Out.ToString());
        Assert.DoesNotContain("system.shutdown", installation.Operations);
        Assert.Equal(installation.From.ToString(), installation.Installed());
    }

    private static UpdateRequest Request(FakeInstallation installation) =>
        new(installation.Feed, null, TimeSpan.FromSeconds(5));
}
