using System.Text;
using System.Text.Json;
using Jason.Cli.Installation;
using Jason.Cli.Process;
using Jason.Cli.Status;
using Jason.Cli.Update;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Contracts.Update;

namespace Jason.Cli.Tests.Update;

/// <summary>
/// The move of a command-line install into Jason Desktop, once its update to this version is complete: through the seams
/// alone — the release, the runtime, the operating system and the application's own <c>jason install</c> stood in for — so
/// nothing is downloaded, installed, signed or started on the machine running the suite.
/// </summary>
public sealed class CommandLineMoveTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // --- the whole move ---------------------------------------------------------------------------------------

    /// <summary>
    /// Windows, with no application: the installer downloaded, held to its line and to the publisher's signature, run
    /// silently into the application's place and waited for; then the application's routine, under this move's lock and
    /// handed its nonce, stops the old runtime; then the move starts it again from the application's jason. In that order.
    /// </summary>
    [Fact]
    public async Task On_Windows_the_application_is_installed_set_up_and_the_runtime_started_from_its_jason()
    {
        using var rig = new MoveRig(windows: true);
        rig.Installation.WithRuntime();
        var move = rig.Move();

        var answer = await move.RunAsync(rig.Complete(), rig.Installation.Feed, Ct);

        Assert.Equal(MoveStates.Done, answer.State);
        Assert.Equal(MoveSteps.Started, answer.Step);
        Assert.Equal(rig.Application, answer.Application);
        Assert.Equal(
            ["signature", $"setup /S /D={rig.Application}", "routine", "stop", $"launch {rig.Carried}"],
            rig.Acts);
        Assert.Equal([rig.Carried, "install", "--retire", rig.Old], rig.Installation.Processes.Runs.Single().Command);

        // The routine went on under the move's own lock: handed the nonce written beside it, which the lock's end takes away.
        var (handed, holder) = Assert.Single(rig.RoutineLocks);
        Assert.NotNull(handed);
        Assert.Equal(holder, handed);
        Assert.False(File.Exists(rig.Update.LockHolder));

        Assert.True(rig.Installation.Running);
        Assert.False(StartOwed.Read(rig.Installation.Paths).Stands);
        var record = rig.Record()!;
        Assert.Equal((MoveStates.Done, MoveSteps.Started, (string?)null), (record.State, record.Step, record.Doing));
        Assert.Contains(move.Lines, line => line.StartsWith("move: done:", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(rig.Update.MoveDownloads, CommandLineMove.WindowsAsset)));
    }

    /// <summary>
    /// macOS on Apple silicon, with no application: the zip unpacked beside its place, held to the requirement before
    /// anything of it runs, put in place by one rename into this account's Applications folder when the shared one may not
    /// be written; the routine and the start as on Windows.
    /// </summary>
    [Fact]
    public async Task On_macOS_the_application_is_unpacked_checked_and_put_in_place_by_one_rename()
    {
        using var rig = new MoveRig(windows: false);
        rig.Installation.WithRuntime();

        var answer = await rig.Move().RunAsync(rig.Complete(), rig.Installation.Feed, Ct);

        Assert.Equal(MoveStates.Done, answer.State);
        Assert.Equal(["sysctl", "unzip", "codesign", "routine", "stop", $"launch {rig.Carried}"], rig.Acts);
        Assert.Equal([MoveSignatures.Requirement], rig.System.Requirements);
        Assert.Equal(SemanticVersion.Current.ToString(), CarriedExecutable.Of(rig.Carried)!.Version);
        Assert.Empty(Directory.EnumerateDirectories(Path.GetDirectoryName(rig.Application)!, ".jason-move.*"));
        Assert.True(rig.Installation.Running);
    }

    /// <summary>
    /// A staging folder a killed move left beside the place is taken away by the next, under the lock; a folder of any other
    /// name there is not.
    /// </summary>
    [Fact]
    public async Task On_macOS_what_a_killed_move_left_unpacked_is_taken_away_and_nothing_else()
    {
        using var rig = new MoveRig(windows: false);
        var place = Path.GetDirectoryName(rig.Application)!;
        var left = Path.Combine(place, $".jason-move.{Guid.NewGuid():N}");
        var other = Path.Combine(place, ".jason-move.someone-elses");
        MoveRig.PlantApplication(Path.Combine(left, "Jason.app"), windows: false, SemanticVersion.Current.ToString());
        Directory.CreateDirectory(other);

        Assert.Equal(MoveStates.Done, (await rig.Move().RunAsync(rig.Complete(), rig.Installation.Feed, Ct)).State);

        Assert.False(Directory.Exists(left));
        Assert.True(Directory.Exists(other));
    }

    /// <summary>The shared Applications folder takes a new application where this account may write there.</summary>
    [Fact]
    public async Task On_macOS_a_writable_shared_folder_takes_the_new_application()
    {
        using var rig = new MoveRig(windows: false);
        Directory.CreateDirectory(rig.SystemApplications);
        rig.System.SharedWritable = true;
        var move = rig.Move();

        var answer = await move.RunAsync(rig.Complete(), rig.Installation.Feed, Ct);

        Assert.Equal(MoveStates.Done, answer.State);
        Assert.Equal(Path.Combine(rig.SystemApplications, "Jason.app"), answer.Application);
    }

    /// <summary>An application of this very version already there is reused: nothing downloaded, its routine run.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_application_of_this_version_already_there_is_reused_and_its_routine_run(bool windows)
    {
        using var rig = new MoveRig(windows);
        MoveRig.PlantApplication(rig.Application, windows, SemanticVersion.Current.ToString());

        var answer = await rig.Move().RunAsync(rig.Complete(), rig.Installation.Feed, Ct);

        Assert.Equal(MoveStates.Done, answer.State);
        Assert.DoesNotContain(rig.Acts, act => act.StartsWith("setup", StringComparison.Ordinal) || act == "unzip");
        Assert.Contains("routine", rig.Acts);
        Assert.DoesNotContain(rig.Installation.Release.Requested, path => path.EndsWith(windows ? CommandLineMove.WindowsAsset : CommandLineMove.MacAsset, StringComparison.Ordinal));
    }

    /// <summary>A later application, 0.49.0 or later, is reused as it is, and its own jason runs the routine.</summary>
    [Fact]
    public async Task A_later_application_is_reused_and_its_own_jason_runs_the_routine()
    {
        using var rig = new MoveRig(windows: true);
        MoveRig.PlantApplication(rig.Application, windows: true, "9.0.0");

        var answer = await rig.Move().RunAsync(rig.Complete(), rig.Installation.Feed, Ct);

        Assert.Equal(MoveStates.Done, answer.State);
        Assert.Equal("9.0.0", CarriedExecutable.Of(rig.Carried)!.Version);
        Assert.Equal(rig.Carried, rig.Installation.Processes.Runs.Single().Command[0]);
    }

    /// <summary>
    /// An application older than 0.49.0, its window open or not, is touched not at all, nor is anything else: the move is
    /// pending, never failed, and says what finishes it; the command-line install answers as it did, its runtime running.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task An_application_older_than_0_49_leaves_the_move_pending_and_touches_nothing(bool windows, bool open)
    {
        using var rig = new MoveRig(windows);
        rig.Installation.WithRuntime();
        MoveRig.PlantApplication(rig.Application, windows, "0.48.2");
        if (open)
        {
            rig.System.Running.Add(Path.Combine(rig.Application, "Jason.exe"));
        }

        var before = Snapshot(rig.Application);
        var answer = await rig.Move().RunAsync(rig.Complete(), rig.Installation.Feed, Ct);

        Assert.Equal(MoveStates.Pending, answer.State);
        Assert.Contains("0.48.2", answer.Reason!, StringComparison.Ordinal);
        Assert.Contains("Check for Updates", answer.Reason!, StringComparison.Ordinal);
        Assert.Equal(open, answer.Reason!.Contains("window is open", StringComparison.Ordinal));
        Assert.Equal(before, Snapshot(rig.Application));
        Assert.DoesNotContain("routine", rig.Acts);
        Assert.True(File.Exists(rig.Old));
        Assert.True(rig.Installation.Running);

        // The same sentence in `jason status`, with the command that carries the move on from the jason that runs it.
        var check = StatusChecks.Move(rig.Env);
        Assert.Equal(CheckState.Failed, check.State);
        Assert.Contains(answer.Reason!, check.Fact, StringComparison.Ordinal);
        Assert.Equal("jason update apply", check.Fix);
    }

    // --- the checks --------------------------------------------------------------------------------------------

    /// <summary>A download that is not the file the release lists is never run: failed, nothing installed or stopped.</summary>
    [Fact]
    public async Task An_installer_that_does_not_match_its_checksum_is_never_run()
    {
        using var rig = new MoveRig(windows: true);
        rig.Installation.WithRuntime();
        rig.Installation.Release.ChecksumsSay($"{new string('0', 64)}  {CommandLineMove.WindowsAsset}\n");

        var answer = await rig.Move().RunAsync(rig.Complete(), rig.Installation.Feed, Ct);

        Assert.Equal(MoveStates.Failed, answer.State);
        Assert.Equal(MoveSteps.Fetched, answer.Step);
        Assert.Contains("does not match checksums.txt", answer.Reason!, StringComparison.Ordinal);
        Assert.Empty(rig.Acts);
        Assert.True(File.Exists(rig.Old));
        Assert.True(rig.Installation.Running);
        Assert.False(File.Exists(Path.Combine(rig.Update.MoveDownloads, CommandLineMove.WindowsAsset)));
    }

    /// <summary>An installer signed by anyone but the publisher, or not at all, is never run.</summary>
    [Theory]
    [InlineData("Somebody Else Ltd.")]
    [InlineData(null)]
    public async Task An_installer_not_signed_by_the_publisher_is_never_run(string? signer)
    {
        using var rig = new MoveRig(windows: true);
        rig.Installation.WithRuntime();
        rig.System.SignedBy = signer;

        var answer = await rig.Move().RunAsync(rig.Complete(), rig.Installation.Feed, Ct);

        Assert.Equal(MoveStates.Failed, answer.State);
        Assert.Equal(MoveSteps.Fetched, answer.Step);
        Assert.Equal(["signature"], rig.Acts);
        Assert.True(File.Exists(rig.Old));
        Assert.True(rig.Installation.Running);
    }

    /// <summary>An application that does not meet the requirement is never run: its jason never sets anything up.</summary>
    [Fact]
    public async Task On_macOS_an_application_that_does_not_verify_is_never_put_in_place()
    {
        using var rig = new MoveRig(windows: false);
        rig.System.MacRefusal = "a sealed resource is missing or invalid";

        var answer = await rig.Move().RunAsync(rig.Complete(), rig.Installation.Feed, Ct);

        Assert.Equal(MoveStates.Failed, answer.State);
        Assert.False(Directory.Exists(rig.Application));
        Assert.DoesNotContain("routine", rig.Acts);
        Assert.Empty(Directory.EnumerateDirectories(Path.GetDirectoryName(rig.Application)!, ".jason-move.*"));
    }

    // --- pending, never failed -----------------------------------------------------------------------------

    /// <summary>
    /// A setup that has not ended within its bound is left running, never killed: the move is pending, the command-line
    /// install and its runtime untouched, and the routine never run.
    /// </summary>
    [Fact]
    public async Task A_setup_that_has_not_ended_within_its_bound_leaves_the_move_pending()
    {
        using var rig = new MoveRig(windows: true);
        rig.Installation.WithRuntime();
        rig.System.OnSetup = _ => Task.FromResult(new SetupRun(false, 0));

        var answer = await rig.Move().RunAsync(rig.Complete(), rig.Installation.Feed, Ct);

        Assert.Equal(MoveStates.Pending, answer.State);
        Assert.Equal(MoveSteps.Verified, answer.Step);
        Assert.Contains("left running", answer.Reason!, StringComparison.Ordinal);
        Assert.DoesNotContain("routine", rig.Acts);
        Assert.True(File.Exists(rig.Old));
        Assert.True(rig.Installation.Running);
    }

    /// <summary>The setup's own refusals fail the move and leave the command-line install as it was.</summary>
    [Theory]
    [InlineData(1, "a Jason process runs from")]
    [InlineData(2, "another install of Jason")]
    [InlineData(5, "exited with 5")]
    public async Task A_setup_that_refuses_fails_the_move_and_touches_nothing(int code, string said)
    {
        using var rig = new MoveRig(windows: true);
        rig.Installation.WithRuntime();
        rig.System.OnSetup = _ => Task.FromResult(new SetupRun(true, code));

        var answer = await rig.Move().RunAsync(rig.Complete(), rig.Installation.Feed, Ct);

        Assert.Equal(MoveStates.Failed, answer.State);
        Assert.Contains(said, answer.Reason!, StringComparison.Ordinal);
        Assert.DoesNotContain("routine", rig.Acts);
        Assert.True(rig.Installation.Running);
    }

    /// <summary>An Intel Mac: never attempted, nothing downloaded or run, and recorded pending.</summary>
    [Fact]
    public async Task On_an_Intel_Mac_the_move_is_pending_and_never_attempted()
    {
        using var rig = new MoveRig(windows: false);
        rig.System.AppleSilicon = false;

        var answer = await rig.Move().RunAsync(rig.Complete(), rig.Installation.Feed, Ct);

        Assert.Equal(MoveStates.Pending, answer.State);
        Assert.Contains("Intel Mac", answer.Reason!, StringComparison.Ordinal);
        Assert.Equal(["sysctl"], rig.Acts);
        Assert.Equal(MoveStates.Pending, rig.Record()!.State);
        Assert.DoesNotContain(rig.Installation.Release.Requested, path => path.EndsWith(CommandLineMove.MacAsset, StringComparison.Ordinal));
    }

    /// <summary>An elevated process moves nothing and writes nothing, the record included.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_elevated_process_leaves_the_move_pending_and_writes_nothing(bool windows)
    {
        using var rig = new MoveRig(windows);
        var ledger = rig.Complete();
        var env = rig.Env with { Setup = rig.Places with { Elevated = true } };

        var answer = await new CommandLineMove(env, rig.Update, TimeProvider.System).RunAsync(ledger, rig.Installation.Feed, Ct);

        Assert.Equal(MoveStates.Pending, answer.State);
        Assert.Contains(windows ? "administrator" : "root", answer.Reason!, StringComparison.Ordinal);
        Assert.Empty(rig.Acts);
        Assert.Null(rig.Record());
        Assert.False(File.Exists(rig.Update.Lock));
    }

    /// <summary>Another jason holding the update's lock: nothing begun, nothing recorded over its record.</summary>
    [Fact]
    public async Task A_lock_held_by_another_leaves_the_move_pending_and_begins_nothing()
    {
        using var rig = new MoveRig(windows: true);
        var ledger = rig.Complete();
        using var other = UpdateLock.Take(rig.Update, "another update");

        var answer = await rig.Move().RunAsync(ledger, rig.Installation.Feed, Ct);

        Assert.Equal(MoveStates.Pending, answer.State);
        Assert.Contains("update.lock", answer.Reason!, StringComparison.Ordinal);
        Assert.Empty(rig.Acts);
        Assert.Null(rig.Record());
    }

    /// <summary>A download that stalls is given up, never failed: pending, nothing installed.</summary>
    [Fact]
    public async Task A_stalled_download_leaves_the_move_pending()
    {
        using var rig = new MoveRig(windows: true);
        var env = rig.Env with { HttpHandler = new Stalling(rig.Installation, CommandLineMove.WindowsAsset) };
        var move = new CommandLineMove(env, rig.Update, TimeProvider.System) { StallWait = TimeSpan.FromMilliseconds(300) };

        var answer = await move.RunAsync(rig.Complete(), rig.Installation.Feed, Ct);

        Assert.Equal(MoveStates.Pending, answer.State);
        Assert.Contains("stalled", answer.Reason!, StringComparison.Ordinal);
        Assert.Empty(rig.Acts);
        Assert.Empty(Directory.EnumerateFiles(rig.Update.MoveDownloads));
    }

    /// <summary>The release read again names another version: nothing of it is downloaded.</summary>
    [Fact]
    public async Task A_release_that_is_another_version_now_is_not_installed()
    {
        using var rig = new MoveRig(windows: true);
        rig.Installation.Release.Says(rig.Installation.Release.Manifest(version: "9.9.9"));

        var answer = await rig.Move().RunAsync(rig.Complete(), rig.Installation.Feed, Ct);

        Assert.Equal(MoveStates.Pending, answer.State);
        Assert.Contains("9.9.9", answer.Reason!, StringComparison.Ordinal);
        Assert.Empty(rig.Acts);
    }

    // --- the routine and the start ------------------------------------------------------------------------

    /// <summary>
    /// The routine's runtime would not stop: nothing was retired, the runtime runs from the command-line install as it did,
    /// and the move is pending; no start is owed, so none is made.
    /// </summary>
    [Fact]
    public async Task A_runtime_that_would_not_stop_leaves_the_move_pending_and_the_runtime_running()
    {
        using var rig = new MoveRig(windows: true);
        rig.Installation.WithRuntime();
        rig.Routine = (command, _) => Report(rig, runtime: MachineSetup.RuntimeStates.WouldNotStop, retired: false, problems: ["The runtime running from it would not stop, so nothing of that install was retired."]);

        var answer = await rig.Move().RunAsync(rig.Complete(), rig.Installation.Feed, Ct);

        Assert.Equal(MoveStates.Pending, answer.State);
        Assert.Equal(MoveSteps.Placed, answer.Step);
        Assert.Contains("would not stop", answer.Reason!, StringComparison.Ordinal);
        Assert.True(rig.Installation.Running);
        Assert.DoesNotContain(rig.Acts, act => act.StartsWith("launch", StringComparison.Ordinal));
    }

    /// <summary>
    /// A routine that stopped the runtime and then ended with no report: the stop owes a start, and the move, which asked for
    /// it, starts the runtime from the application's jason, whole by then.
    /// </summary>
    [Fact]
    public async Task A_routine_that_dies_after_its_stop_still_gets_the_runtime_started_from_the_application()
    {
        using var rig = new MoveRig(windows: true);
        rig.Installation.WithRuntime();
        rig.Routine = (command, updateLock) =>
        {
            rig.Retire(command, updateLock);
            return new FinishedCommand(-1, "{\"version\":");
        };

        var answer = await rig.Move().RunAsync(rig.Complete(), rig.Installation.Feed, Ct);

        Assert.Equal(MoveStates.Failed, answer.State);
        Assert.Equal(MoveSteps.Placed, answer.Step);
        Assert.True(rig.Installation.Running);
        Assert.Equal($"launch {rig.Carried}", rig.Acts[^1]);
    }

    /// <summary>A refusal of the routine's for the lock is pending; any other is failed; neither stops anything here.</summary>
    [Theory]
    [InlineData(UpdateCodes.InProgress, MoveStates.Pending)]
    [InlineData("layout_newer", MoveStates.Failed)]
    public async Task A_routine_that_refuses_is_said_with_its_code(string code, string state)
    {
        using var rig = new MoveRig(windows: true);
        rig.Installation.WithRuntime();
        rig.Routine = (_, _) => new FinishedCommand(1, CliErrors.Serialize(code, "refused", retryable: false));

        var answer = await rig.Move().RunAsync(rig.Complete(), rig.Installation.Feed, Ct);

        Assert.Equal(state, answer.State);
        Assert.Contains(code, answer.Reason!, StringComparison.Ordinal);
        Assert.True(rig.Installation.Running);
    }

    /// <summary>No runtime was running: the routine stops none, and the move starts none.</summary>
    [Fact]
    public async Task With_no_runtime_running_none_is_started()
    {
        using var rig = new MoveRig(windows: true);

        var answer = await rig.Move().RunAsync(rig.Complete(), rig.Installation.Feed, Ct);

        Assert.Equal(MoveStates.Done, answer.State);
        Assert.DoesNotContain(rig.Acts, act => act.StartsWith("launch", StringComparison.Ordinal));
        Assert.False(rig.Installation.Running);
    }

    // --- Linux, a move done, the record ---------------------------------------------------------------------

    /// <summary>On Linux the move does not apply: said, and nothing written, the lock and the record included.</summary>
    [Fact]
    public async Task On_a_system_with_no_application_the_move_does_not_apply_and_changes_nothing()
    {
        using var rig = new MoveRig(windows: true);
        var ledger = rig.Complete();

        var answer = await new CommandLineMove(rig.Env with { Setup = null }, rig.Update, TimeProvider.System).RunAsync(ledger, rig.Installation.Feed, Ct);

        Assert.Equal(MoveStates.NotApplicable, answer.State);
        Assert.Null(rig.Record());
        Assert.False(File.Exists(rig.Update.Lock));
        Assert.Empty(rig.Acts);
    }

    /// <summary>A move done is done: a second run says so and does nothing.</summary>
    [Fact]
    public async Task A_move_done_is_not_done_twice()
    {
        using var rig = new MoveRig(windows: false);
        var ledger = rig.Complete();
        await rig.Move().RunAsync(ledger, rig.Installation.Feed, Ct);
        rig.Acts.Clear();

        var again = await rig.Move().RunAsync(ledger, rig.Installation.Feed, Ct);

        Assert.Equal(MoveStates.Done, again.State);
        Assert.Empty(rig.Acts);
    }

    // --- jason status ---------------------------------------------------------------------------------------

    /// <summary>
    /// <c>jason status</c> says where a move stands from its record: none, done, or open with the command that carries it on
    /// from the jason status runs as; the check is optional, so a pending move never makes an installation not ready.
    /// </summary>
    [Fact]
    public async Task Status_names_a_move_that_is_not_done_with_the_command_that_carries_it_on()
    {
        using var rig = new MoveRig(windows: true);
        Assert.Equal(CheckState.Absent, StatusChecks.Move(rig.Env).State);

        rig.System.OnSetup = _ => Task.FromResult(new SetupRun(false, 0));
        var answer = await rig.Move().RunAsync(rig.Complete(), rig.Installation.Feed, Ct);

        var check = StatusChecks.Move(rig.Env);
        Assert.False(check.Required);
        Assert.Equal(CheckState.Failed, check.State);
        Assert.Contains(answer.Reason!, check.Fact, StringComparison.Ordinal);
        Assert.Equal("jason update apply", check.Fix);

        // Typed into the application's jason, which answers once the routine placed it, the repair is the routine itself.
        MoveRig.PlantApplication(rig.Application, windows: true, SemanticVersion.Current.ToString());
        Assert.Equal("jason install", StatusChecks.Move(rig.Env with { InstallPath = rig.Carried }).Fix);
    }

    // --- the sweep ----------------------------------------------------------------------------------------

    /// <summary>
    /// The folders the routine renamed a running executable into go once nothing runs from them; one still held is left,
    /// quietly; a folder of that name holding anything else, or of another name, is never touched.
    /// </summary>
    [Fact]
    public void The_sweep_takes_away_retired_executables_once_their_process_has_ended()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows alone refuses to delete a running image, which is what is swept after.");
        using var rig = new MoveRig(windows: true);
        string Plant(string parent, string name, params string[] files)
        {
            var folder = Path.Combine(parent, name);
            Directory.CreateDirectory(folder);
            foreach (var file in files)
            {
                File.WriteAllText(Path.Combine(folder, file), "jason 0.49.0");
            }

            return folder;
        }

        var ended = Plant(rig.Temporary, $"jason-retired-{Guid.NewGuid():N}", "jason.exe");
        var held = Plant(rig.Temporary, $"jason-retired-{Guid.NewGuid():N}", "jason.exe");
        var beside = Plant(Path.Combine(rig.LocalAppData, "Programs"), $".jason-retired-{Guid.NewGuid():N}", "jason.exe");
        var notOurs = Plant(rig.Temporary, $"jason-retired-{Guid.NewGuid():N}", "jason.exe", "notes.txt");
        var otherName = Plant(rig.Temporary, "jason-retired-mine", "jason.exe");

        using (new FileStream(Path.Combine(held, "jason.exe"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            RetiredSweep.Sweep(rig.Places);
        }

        Assert.False(Directory.Exists(ended));
        Assert.False(Directory.Exists(beside));
        Assert.True(File.Exists(Path.Combine(held, "jason.exe")));
        Assert.True(Directory.Exists(notOurs));
        Assert.True(Directory.Exists(otherName));

        RetiredSweep.Sweep(rig.Places);
        Assert.False(Directory.Exists(held));
    }

    /// <summary>The move's own routine renames its image aside on Windows; the next move, or the next routine, sweeps it.</summary>
    [Fact]
    public async Task What_a_move_retired_is_swept_by_the_next_sweep()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The aside folder is a Windows routine's.");
        using var rig = new MoveRig(windows: true);
        await rig.Move().RunAsync(rig.Complete(), rig.Installation.Feed, Ct);

        RetiredSweep.Sweep(rig.Places);

        Assert.Empty(Directory.EnumerateDirectories(rig.Temporary, "jason-retired-*"));
    }

    // --- guards over the second copies --------------------------------------------------------------------

    /// <summary>
    /// The publisher the move holds the installer to is install.ps1's, and the requirement it holds the application to is
    /// install.sh's: each a second copy of one line, held equal to it.
    /// </summary>
    [Fact]
    public void The_signer_and_the_requirement_are_the_install_scripts_own()
    {
        var root = TreeRoot();
        var ps1 = File.ReadAllText(Path.Combine(root, "install", "install.ps1"));
        var sh = File.ReadAllText(Path.Combine(root, "install", "install.sh"));

        Assert.Contains($"        $signer = '{MoveSignatures.Publisher}'", ps1, StringComparison.Ordinal);
        Assert.Contains($"APP_REQUIREMENT='{MoveSignatures.Requirement}'", sh, StringComparison.Ordinal);
        Assert.Contains("BD8M7U5YBV", MoveSignatures.Requirement, StringComparison.Ordinal);
        Assert.Contains(CommandLineMove.WindowsAsset, ReleaseAssets.Desktop);
        Assert.Contains(CommandLineMove.MacAsset, ReleaseAssets.Desktop);
    }

    /// <summary>
    /// A test build's signer comes from the environment only in a build made with JasonTestBuild, which no release makes:
    /// this one is not, and the release workflow and the package action never name the property.
    /// </summary>
    [Fact]
    public void No_released_build_is_a_test_build()
    {
        Assert.False(MoveSignatures.TestBuild);
        Assert.Equal(MoveSignatures.Publisher, MoveSignatures.PublisherInForce);
        Assert.Equal(MoveSignatures.Requirement, MoveSignatures.RequirementInForce);

        var root = TreeRoot();
        foreach (var file in new[] { Path.Combine(".github", "workflows", "release.yml"), Path.Combine("dotnet", "Directory.Build.props"), Path.Combine("dotnet", "Directory.Packages.props") }
            .Concat(Directory.EnumerateFiles(Path.Combine(root, ".github", "actions"), "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(root, path))))
        {
            var text = File.ReadAllText(Path.Combine(root, file));
            Assert.DoesNotContain("JasonTestBuild", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("JASON_TEST_BUILD", text, StringComparison.Ordinal);
        }

        var source = File.ReadAllText(Path.Combine(root, "dotnet", "src", "Jason.Cli", "Update", "MoveSystem.cs"));
        var opened = source.IndexOf("#if JASON_TEST_BUILD", StringComparison.Ordinal);
        var closed = source.IndexOf("#else", opened, StringComparison.Ordinal);
        Assert.True(opened >= 0 && closed > opened);
        foreach (var variable in new[] { "JASON_TEST_BUILD_SIGNER", "JASON_TEST_BUILD_REQUIREMENT" })
        {
            var at = source.IndexOf($"\"{variable}\"", StringComparison.Ordinal);
            Assert.True(at > opened && at < closed, $"{variable} is read outside the test build's block");
            Assert.Equal(at, source.LastIndexOf($"\"{variable}\"", StringComparison.Ordinal));
        }
    }

    // --- helpers -------------------------------------------------------------------------------------------

    private static FinishedCommand Report(MoveRig rig, string runtime, bool retired, IReadOnlyList<string> problems) =>
        new(1, JsonSerializer.Serialize(
            new InstallReport(
                SemanticVersion.Current.ToString(),
                rig.Carried,
                null,
                [],
                null,
                retired ? new InstallRetired(rig.Old, null, [rig.Old], false) : null,
                runtime,
                [],
                [],
                problems),
            JasonJson.Options));

    private static string Snapshot(string root) =>
        string.Join('\n', Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(file => $"{Path.GetRelativePath(root, file)} {File.ReadAllText(file)} {File.GetLastWriteTimeUtc(file).Ticks}"));

    internal static string TreeRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "dotnet", "Jason.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository's root was not found above the test's own folder.");
    }

    /// <summary>A transport that answers as the installation does, and never sends a byte of one file.</summary>
    private sealed class Stalling(FakeInstallation installation, string stalls) : HttpMessageHandler
    {
        private readonly HttpMessageInvoker _behind = new(installation, disposeHandler: false);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!request.RequestUri!.AbsolutePath.EndsWith(stalls, StringComparison.Ordinal))
            {
                return await _behind.SendAsync(request, cancellationToken);
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StreamContent(new Silent()) };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _behind.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>A body that never yields a byte until the read is cancelled.</summary>
    private sealed class Silent : MemoryStream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }
}
