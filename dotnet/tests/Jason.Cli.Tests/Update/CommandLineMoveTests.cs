using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jason.Cli.Installation;
using Jason.Cli.Process;
using Jason.Cli.Status;
using Jason.Cli.Update;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Contracts.Update;
using Jason.Runtime.Tests;

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

    /// <summary>
    /// The move warms up before the application's routine, once: the routine replaces or renames the file this process
    /// started from, and everything the move does after it (the start, the record's end, the answer printed) may load an
    /// assembly from that path.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_move_warms_up_before_the_routine_touches_its_own_file(bool windows)
    {
        using var rig = new MoveRig(windows);
        rig.Installation.WithRuntime();

        var answer = await rig.Move(warmUp: () => rig.Acts.Add("warm-up")).RunAsync(rig.Complete(), rig.Installation.Feed, Ct);

        Assert.Equal(MoveStates.Done, answer.State);
        Assert.Single(rig.Acts, act => act == "warm-up");
        Assert.Equal(rig.Acts.IndexOf("routine") - 1, rig.Acts.IndexOf("warm-up"));
    }

    /// <summary>
    /// After going back from 0.49 the move's record still says done, while the application is gone and the file at the install
    /// path is a command-line executable again. Such a record no longer stands: the status check does not call the install
    /// moved, and the next move installs the application again and writes the record over.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_done_record_that_no_longer_stands_is_a_move_to_make_again(bool windows)
    {
        using var rig = new MoveRig(windows);
        rig.Installation.WithRuntime();
        Assert.Equal(MoveStates.Done, (await rig.Move().RunAsync(rig.Complete(), rig.Installation.Feed, Ct)).State);
        Directory.Delete(rig.Application, recursive: true);
        File.WriteAllBytes(rig.Old, new byte[4096]);
        Assert.Equal(MoveStates.Done, rig.Record()!.State);
        Assert.Equal(CheckState.Absent, StatusChecks.Move(rig.Env).State);
        rig.Acts.Clear();

        var answer = await rig.Move().RunAsync(rig.Complete(), rig.Installation.Feed, Ct);

        Assert.Equal(MoveStates.Done, answer.State);
        Assert.Contains(rig.Acts, act => act.StartsWith("setup", StringComparison.Ordinal) || act == "unzip");
        Assert.Contains("routine", rig.Acts);
        Assert.True(rig.Record()!.DoneAndStanding(windows));
        Assert.Equal(CheckState.Ok, StatusChecks.Move(rig.Env).State);
    }

    /// <summary>
    /// The application stands by the move's own look at it: on Windows its <c>Jason.exe</c>, so a folder left without it
    /// does not stand, and the record that names it is a move to make again.
    /// </summary>
    [Fact]
    public async Task A_done_record_whose_application_folder_has_lost_its_executable_no_longer_stands()
    {
        using var rig = new MoveRig(windows: true);
        rig.Installation.WithRuntime();
        Assert.Equal(MoveStates.Done, (await rig.Move().RunAsync(rig.Complete(), rig.Installation.Feed, Ct)).State);
        Assert.True(rig.Record()!.DoneAndStanding(windows: true));

        File.Delete(Path.Combine(rig.Application, "Jason.exe"));

        Assert.True(Directory.Exists(rig.Application));
        Assert.False(rig.Record()!.DoneAndStanding(windows: true));
        Assert.Equal(CheckState.Absent, StatusChecks.Move(rig.Env).State);
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
    /// An application older than 0.49.0 is touched not at all, nor is anything else: the move is pending, never failed, and
    /// says what finishes it; the command-line install answers as it did, its runtime running. Nothing typed here changes it, so
    /// <c>jason status</c> says what stands and what would move it, with no repair that cannot help.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_application_older_than_0_49_leaves_the_move_pending_and_touches_nothing(bool windows)
    {
        using var rig = new MoveRig(windows);
        rig.Installation.WithRuntime();
        MoveRig.PlantApplication(rig.Application, windows, "0.48.2");

        var before = Snapshot(rig.Application);
        var answer = await rig.Move().RunAsync(rig.Complete(), rig.Installation.Feed, Ct);

        Assert.Equal(MoveStates.Pending, answer.State);
        Assert.Contains("0.48.2", answer.Reason!, StringComparison.Ordinal);
        Assert.Contains("Check for Updates", answer.Reason!, StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(rig.Application));
        Assert.DoesNotContain("routine", rig.Acts);
        Assert.True(File.Exists(rig.Old));
        Assert.True(rig.Installation.Running);

        // The same sentence in `jason status`, saying what would move it, and no repair.
        var check = StatusChecks.Move(rig.Env);
        Assert.Equal(CheckState.Unknown, check.State);
        Assert.Contains(answer.Reason!, check.Fact, StringComparison.Ordinal);
        Assert.Contains("goes on once Jason Desktop at", check.Fact, StringComparison.Ordinal);
        Assert.Null(check.Fix);
    }

    /// <summary>
    /// An application whose window is open, of any version and on either system, is left as it is: the routine never stops a
    /// runtime under an open window. Pending, with what to do — close it — and the command that carries the move on.
    /// </summary>
    [Theory]
    [InlineData(true, "0.48.2")]
    [InlineData(true, null)]
    [InlineData(false, "0.48.2")]
    [InlineData(false, null)]
    public async Task An_application_with_its_window_open_of_any_version_leaves_the_move_pending(bool windows, string? version)
    {
        using var rig = new MoveRig(windows);
        rig.Installation.WithRuntime();
        MoveRig.PlantApplication(rig.Application, windows, version ?? SemanticVersion.Current.ToString());
        rig.System.Running.Add(windows ? Path.Combine(rig.Application, "Jason.exe") : Path.Combine(rig.Application, "Contents", "MacOS", "Jason"));
        var before = Snapshot(rig.Application);

        var answer = await rig.Move().RunAsync(rig.Complete(), rig.Installation.Feed, Ct);

        Assert.Equal(MoveStates.Pending, answer.State);
        Assert.Contains("has its window open", answer.Reason!, StringComparison.Ordinal);
        Assert.Contains("Close Jason Desktop", answer.Reason!, StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(rig.Application));
        Assert.DoesNotContain("routine", rig.Acts);
        Assert.True(rig.Installation.Running);
        var check = StatusChecks.Move(rig.Env);
        Assert.Equal(CheckState.Failed, check.State);
        Assert.Equal("jason update apply", check.Fix);
    }

    /// <summary>
    /// An application of no readable version that this move never began: touched not at all, pending, with a repair that can
    /// work — its installer from the release page — and status saying what would move it.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_application_of_no_readable_version_leaves_the_move_pending_with_a_repair_that_can_work(bool windows)
    {
        using var rig = new MoveRig(windows);
        rig.Installation.WithRuntime();
        MoveRig.PlantApplication(rig.Application, windows, SemanticVersion.Current.ToString());
        File.Delete(Path.Combine(Path.GetDirectoryName(rig.Carried)!, CarriedExecutable.RecordName));
        var before = Snapshot(rig.Application);

        var answer = await rig.Move().RunAsync(rig.Complete(), rig.Installation.Feed, Ct);

        Assert.Equal(MoveStates.Pending, answer.State);
        Assert.Contains("no version that can be read", answer.Reason!, StringComparison.Ordinal);
        Assert.Contains($"/releases/tag/v{SemanticVersion.Current}", answer.Reason!, StringComparison.Ordinal);
        Assert.DoesNotContain("Check for Updates", answer.Reason!, StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(rig.Application));
        Assert.DoesNotContain(rig.Acts, act => act.StartsWith("setup", StringComparison.Ordinal));
        Assert.Equal(CheckState.Unknown, StatusChecks.Move(rig.Env).State);
    }

    /// <summary>
    /// The application a setup this move began left half installed — the record put it there at <c>verified</c> and never said
    /// it was placed — is set up again where nothing runs from it; where its setup or anything of it still runs, the move waits.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_setup_this_move_began_and_never_finished_is_run_again_where_nothing_runs_from_it(bool stillRunning)
    {
        using var rig = new MoveRig(windows: true);
        rig.Installation.WithRuntime();
        var ledger = rig.Complete();
        new MoveRecord(SemanticVersion.Current.ToString(), Path.GetFullPath(rig.Old), MoveStates.Pending, MoveSteps.Verified, null, rig.Application, "left running", rig.Installation.Feed.AbsoluteUri, DateTimeOffset.UtcNow, "the-moves-marker").Write(rig.Update.Move);
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(rig.Application).FullName, "Jason.exe"), "half");
        if (stillRunning)
        {
            rig.System.Running.Add(Path.Combine(rig.Update.MoveDownloads, CommandLineMove.WindowsAsset));
        }

        var answer = await rig.Move().RunAsync(ledger, rig.Installation.Feed, Ct);

        if (stillRunning)
        {
            Assert.Equal(MoveStates.Pending, answer.State);
            Assert.DoesNotContain(rig.Acts, act => act.StartsWith("setup", StringComparison.Ordinal));
        }
        else
        {
            Assert.Equal(MoveStates.Done, answer.State);
            Assert.Contains($"setup /S /D={rig.Application}", rig.Acts);
            Assert.Equal("the-moves-marker", rig.Record()!.Marker);
        }
    }

    /// <summary>
    /// While the installer this move downloaded still runs, left by an earlier run past its bound or after a kill, nothing at
    /// its place is used: not an application of this version it may be writing, not a half-written one, and no second
    /// installer is fetched or run. The move is pending, and `jason update apply` carries it on once that installer has ended.
    /// </summary>
    [Theory]
    [InlineData("reusable")]
    [InlineData("half")]
    [InlineData("nothing")]
    public async Task While_the_moves_own_setup_still_runs_nothing_at_its_place_is_used(string there)
    {
        using var rig = new MoveRig(windows: true);
        rig.Installation.WithRuntime();
        var ledger = rig.Complete();
        new MoveRecord(SemanticVersion.Current.ToString(), Path.GetFullPath(rig.Old), MoveStates.Pending, MoveSteps.Verified, null, rig.Application, "left running", rig.Installation.Feed.AbsoluteUri, DateTimeOffset.UtcNow, "the-moves-marker").Write(rig.Update.Move);
        switch (there)
        {
            case "reusable":
                MoveRig.PlantApplication(rig.Application, windows: true, SemanticVersion.Current.ToString());
                break;
            case "half":
                File.WriteAllText(Path.Combine(Directory.CreateDirectory(rig.Application).FullName, "Jason.exe"), "half");
                break;
        }

        var setup = Path.Combine(rig.Update.MoveDownloads, CommandLineMove.WindowsAsset);
        rig.System.Running.Add(setup);

        var answer = await rig.Move().RunAsync(ledger, rig.Installation.Feed, Ct);

        Assert.Equal(MoveStates.Pending, answer.State);
        Assert.Contains("this move began earlier is still running", answer.Reason!, StringComparison.Ordinal);
        Assert.Empty(rig.Acts);
        Assert.True(File.Exists(rig.Old));
        Assert.True(rig.Installation.Running);
        Assert.Equal("the-moves-marker", rig.Record()!.Marker);
        Assert.Equal("jason update apply", StatusChecks.Move(rig.Env).Fix);

        // Once it has ended, the next apply carries the move on.
        rig.System.Running.Remove(setup);
        if (there == "half")
        {
            MoveRig.PlantApplication(rig.Application, windows: true, SemanticVersion.Current.ToString());
        }

        var again = await rig.Move().RunAsync(rig.Complete(), rig.Installation.Feed, Ct);
        Assert.Equal(MoveStates.Done, again.State);
    }

    /// <summary>
    /// On Windows an application of this version or later already there has its jason held to the publisher before anything of
    /// it runs, as a downloaded installer is and as macOS holds a found application: one signed by another is never run.
    /// </summary>
    [Fact]
    public async Task On_Windows_a_found_applications_jason_not_signed_by_the_publisher_is_never_run()
    {
        using var rig = new MoveRig(windows: true);
        rig.Installation.WithRuntime();
        MoveRig.PlantApplication(rig.Application, windows: true, SemanticVersion.Current.ToString());
        rig.System.SignedBy = "Somebody Else Ltd.";

        var answer = await rig.Move().RunAsync(rig.Complete(), rig.Installation.Feed, Ct);

        Assert.Equal(MoveStates.Failed, answer.State);
        Assert.Equal(["signature"], rig.Acts);
        Assert.True(rig.Installation.Running);
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

    /// <summary>
    /// An installer signed by anyone but the publisher that signed this jason, or not at all, is never run, and its download is
    /// removed.
    /// </summary>
    [Theory]
    [InlineData("Somebody Else Ltd.")]
    [InlineData(null)]
    public async Task An_installer_not_signed_by_the_publisher_of_this_jason_is_never_run(string? signer)
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
        Assert.False(File.Exists(Path.Combine(rig.Update.MoveDownloads, CommandLineMove.WindowsAsset)));
    }

    /// <summary>
    /// The installer is held to whoever signed the jason that asks for the move, read from that jason's own verified signature:
    /// no name is written in the sources, so a fork that signs its own builds is held to its own signer.
    /// </summary>
    [Fact]
    public async Task The_installer_is_held_to_the_signer_of_the_running_jason()
    {
        using var rig = new MoveRig(windows: true);
        rig.Installation.WithRuntime();
        rig.System.SelfSignedBy = "A Fork That Signs Its Own";
        rig.System.SignedBy = "A Fork That Signs Its Own";

        var answer = await rig.Move().RunAsync(rig.Complete(), rig.Installation.Feed, Ct);

        Assert.Equal(MoveStates.Done, answer.State);
        Assert.Equal(1, rig.System.OwnSignatureReads);
        Assert.Contains("signature", rig.Acts);
    }

    /// <summary>
    /// A jason that is unsigned, or whose signature does not verify, moves nothing on Windows: refused at the first step, before
    /// anything is fetched or looked at, with the way forward, and no repair typed here that cannot help.
    /// </summary>
    [Fact]
    public async Task A_jason_whose_own_signature_does_not_verify_moves_nothing_and_fetches_nothing()
    {
        using var rig = new MoveRig(windows: true);
        rig.Installation.WithRuntime();
        rig.System.SelfSignedBy = null;
        var fetches = rig.Installation.Release.Requested.Count;

        var answer = await rig.Move().RunAsync(rig.Complete(), rig.Installation.Feed, Ct);

        Assert.Equal(MoveStates.Failed, answer.State);
        Assert.Null(answer.Step);
        Assert.Contains("does not verify as signed", answer.Reason!, StringComparison.Ordinal);
        Assert.Contains("Install Jason Desktop yourself from", answer.Reason!, StringComparison.Ordinal);
        Assert.Empty(rig.Acts);
        Assert.Equal(fetches, rig.Installation.Release.Requested.Count);
        Assert.False(Directory.Exists(rig.Application));
        Assert.False(Directory.Exists(rig.Update.MoveDownloads) && Directory.EnumerateFileSystemEntries(rig.Update.MoveDownloads).Any());
        Assert.True(File.Exists(rig.Old));
        Assert.True(rig.Installation.Running);

        var check = StatusChecks.Move(rig.Env);
        Assert.Equal(CheckState.Unknown, check.State);
        Assert.Null(check.Fix);
    }

    /// <summary>
    /// Only a test build reads the two variables, and whether it is one is read from the build itself, never passed in: this
    /// build is a released one, so both variables change nothing, and a move given them holds the installer to its own signer
    /// still. (A test build's named signer coming first is held by construction, by the guard below, and by the proof that
    /// runs a test build on a runner.)
    /// </summary>
    [Fact]
    public async Task A_released_build_ignores_both_test_build_variables()
    {
        Assert.False(Jason.Contracts.JasonVersion.TestBuild);
        var named = new Dictionary<string, string>
        {
            ["JASON_TEST_BUILD_SIGNER"] = "A Runner's Own Certificate",
            ["JASON_TEST_BUILD_REQUIREMENT"] = "anchor apple",
        };
        Assert.Null(MoveSignatures.TestSigner(named));
        Assert.Equal(MoveSignatures.Requirement, MoveSignatures.RequirementInForce(named));

        // The variable changes nothing in a move, and an installer by that signer is refused.
        using var rig = new MoveRig(windows: true, variables: named);
        rig.Installation.WithRuntime();
        rig.System.SignedBy = "A Runner's Own Certificate";

        var answer = await rig.Move().RunAsync(rig.Complete(), rig.Installation.Feed, Ct);

        Assert.Equal(MoveStates.Failed, answer.State);
        Assert.Contains($"not by {FakeMoveSystem.Publisher}", answer.Reason!, StringComparison.Ordinal);
        Assert.Equal(1, rig.System.OwnSignatureReads);
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
    [InlineData(1, "a Jason process still runs from")]
    [InlineData(1, "or its look for one failed and could not tell")]
    [InlineData(2, "another installer of Jason Desktop was running at the same time, or the uninstaller of the Jason Desktop already installed refused")]
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

    /// <summary>
    /// A silent installer that meets Jason Desktop running, in its window or the tray, ends with its own code having changed
    /// and ended nothing: the move is pending, says to quit Jason Desktop and run the update again, and touches nothing.
    /// The code is the installer's exit 3, <see cref="SetupExits.ApplicationRunning"/>.
    /// </summary>
    [Fact]
    public async Task A_setup_that_meets_Jason_Desktop_running_leaves_the_move_pending_and_says_to_quit_it()
    {
        using var rig = new MoveRig(windows: true);
        rig.Installation.WithRuntime();
        rig.System.OnSetup = _ => Task.FromResult(new SetupRun(true, SetupExits.ApplicationRunning));

        var answer = await rig.Move().RunAsync(rig.Complete(), rig.Installation.Feed, Ct);

        Assert.Equal(MoveStates.Pending, answer.State);
        Assert.Equal(MoveSteps.Verified, answer.Step);
        Assert.Contains("found Jason Desktop running at", answer.Reason!, StringComparison.Ordinal);
        Assert.Contains("or its look for it failed and could not tell whether it runs", answer.Reason!, StringComparison.Ordinal);
        Assert.Contains("Quit Jason Desktop if it runs, then run `jason update apply` again", answer.Reason!, StringComparison.Ordinal);
        Assert.DoesNotContain("routine", rig.Acts);
        Assert.True(File.Exists(rig.Old));
        Assert.True(rig.Installation.Running);

        var check = StatusChecks.Move(rig.Env);
        Assert.Equal(CheckState.Failed, check.State);
        Assert.Equal("jason update apply", check.Fix);
        Assert.NotEqual(SetupExits.ProcessRunsFromPlace, SetupExits.ApplicationRunning);
        Assert.Equal(3, SetupExits.ApplicationRunning);
        Assert.NotEqual(SetupExits.StoppedBeforeInstalling, SetupExits.ApplicationRunning);

        // The page says the same number: the constant and the sentence move together.
        var page = File.ReadAllText(Path.Combine(TreeRoot(), "docs", "release-and-update.md"));
        Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"its exit {SetupExits.ApplicationRunning}, Jason Desktop running in its window or the tray"), page, StringComparison.Ordinal);
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

        // Nothing typed here moves it: status says what stands and what would, with no repair.
        var check = StatusChecks.Move(rig.Env);
        Assert.Equal(CheckState.Unknown, check.State);
        Assert.Contains("goes on once Jason Desktop is offered for Intel Macs", check.Fact, StringComparison.Ordinal);
        Assert.Null(check.Fix);
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
    /// The routine's runtime would not stop, or names no image it runs from, so the routine left it running: nothing was
    /// retired, the runtime runs from the command-line install as it did, and the move is pending with the routine's sentence;
    /// no start is owed, so none is made.
    /// </summary>
    [Theory]
    [InlineData(MachineSetup.RuntimeStates.WouldNotStop, "The runtime running from it would not stop, so nothing of that install was retired.")]
    [InlineData(MachineSetup.RuntimeStates.Running, "The runtime that runs on the data directory does not name the executable it runs from, so it was not retired and the runtime was left running.")]
    public async Task A_runtime_the_routine_left_running_leaves_the_move_pending_and_the_runtime_running(string runtime, string said)
    {
        using var rig = new MoveRig(windows: true);
        rig.Installation.WithRuntime();
        rig.Routine = (command, _) => Report(rig, runtime: runtime, retired: false, problems: [said]);

        var answer = await rig.Move().RunAsync(rig.Complete(), rig.Installation.Feed, Ct);

        Assert.Equal(MoveStates.Pending, answer.State);
        Assert.Equal(MoveSteps.Placed, answer.Step);
        Assert.Contains(said, answer.Reason!, StringComparison.Ordinal);
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
    /// The requirement the move holds an application to on macOS is install.sh's, a second copy of one line held equal to it.
    /// On Windows no publisher is written in the sources: the move reads it from the signature of the jason that asks.
    /// </summary>
    [Fact]
    public void The_requirement_is_the_install_scripts_own()
    {
        var root = TreeRoot();
        var sh = File.ReadAllText(Path.Combine(root, "install", "install.sh"));

        Assert.Contains($"APP_REQUIREMENT='{MoveSignatures.Requirement}'", sh, StringComparison.Ordinal);
        Assert.Contains("BD8M7U5YBV", MoveSignatures.Requirement, StringComparison.Ordinal);
        Assert.Contains(CommandLineMove.WindowsAsset, ReleaseAssets.Desktop);
        Assert.Contains(CommandLineMove.MacAsset, ReleaseAssets.Desktop);
    }

    /// <summary>
    /// The move's installer exits are the include's own numbers, read from <c>desktop/installer/uninstall.nsh</c>: every silent
    /// run that finds Jason Desktop, or whose look failed, sets <see cref="SetupExits.ApplicationRunning"/>; the old
    /// version's refused uninstaller sets <see cref="SetupExits.StoppedBeforeInstalling"/>; a process still found sets
    /// <see cref="SetupExits.ProcessRunsFromPlace"/>; and the include sets no other level, so a new one cannot pass unread.
    /// </summary>
    [Fact]
    public void The_moves_installer_exits_are_the_includes_own_numbers()
    {
        var lines = File.ReadAllLines(Path.Combine(TreeRoot(), "desktop", "installer", "uninstall.nsh"));
        var levels = lines
            .Select((line, at) => (Text: line.Trim(), At: at))
            .Where(line => line.Text.StartsWith("SetErrorLevel ", StringComparison.Ordinal))
            .Select(line => (Level: int.Parse(line.Text["SetErrorLevel ".Length..], CultureInfo.InvariantCulture), line.At))
            .ToList();

        Assert.Equal(
            new[] { SetupExits.ProcessRunsFromPlace, SetupExits.StoppedBeforeInstalling, SetupExits.ApplicationRunning }.Order(),
            levels.Select(level => level.Level).Distinct().Order());

        // Exit 3: set only in a silent run, the line before it opening that branch, and followed by its Quit.
        var silent = levels.Where(level => level.Level == SetupExits.ApplicationRunning).ToList();
        Assert.NotEmpty(silent);
        Assert.All(silent, level =>
        {
            Assert.Equal("${if} ${Silent}", lines[level.At - 1].Trim());
            Assert.Equal("Quit", lines[level.At + 1].Trim());
        });

        // Exit 2: the old version's uninstaller refused, in the macro both uninstall-check hooks insert.
        var hook = Array.FindIndex(lines, line => line.Trim() == "!macro jasonOldUninstallerResult");
        var end = Array.FindIndex(lines, hook, line => line.Trim() == "!macroend");
        Assert.Contains(levels, level => level.Level == SetupExits.StoppedBeforeInstalling && level.At > hook && level.At < end);
        Assert.Contains("!insertmacro jasonOldUninstallerResult", string.Join("\n", lines[end..]), StringComparison.Ordinal);
    }

    /// <summary>
    /// A test build's signer comes from the environment only in a build made with JasonTestBuild, which no release makes:
    /// this one is not, and the release workflow and the package action never name the property. One symbol says it: only
    /// Jason.Contracts defines <c>JASON_TEST_BUILD</c>, and the move's relaxed checks read <c>JasonVersion.TestBuild</c> and
    /// nothing else, so no build relaxes them without saying it is a test build.
    /// </summary>
    [Fact]
    [Critical(Family.B7, "a released build that is a test build takes its signer from the environment and relaxes the move's checks")]
    public void No_released_build_is_a_test_build()
    {
        Assert.False(Jason.Contracts.JasonVersion.TestBuild);
        Assert.Null(MoveSignatures.TestSigner(null));
        Assert.Equal(MoveSignatures.Requirement, MoveSignatures.RequirementInForce(null));

        var root = TreeRoot();
        foreach (var file in new[] { Path.Combine(".github", "workflows", "release.yml"), Path.Combine("dotnet", "Directory.Build.props"), Path.Combine("dotnet", "Directory.Packages.props") }
            .Concat(Directory.EnumerateFiles(Path.Combine(root, ".github", "actions"), "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(root, path))))
        {
            var text = File.ReadAllText(Path.Combine(root, file));
            Assert.DoesNotContain("JasonTestBuild", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("JASON_TEST_BUILD", text, StringComparison.Ordinal);
        }

        // The symbol is defined in one project, and read in source in one file.
        var defining = Directory.EnumerateFiles(Path.Combine(root, "dotnet", "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(project => File.ReadAllText(project).Contains("JASON_TEST_BUILD", StringComparison.Ordinal))
            .Select(project => Path.GetFileName(project));
        Assert.Equal(["Jason.Contracts.csproj"], defining);
        var testing = Directory.EnumerateFiles(Path.Combine(root, "dotnet", "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => File.ReadAllText(file).Contains("#if JASON_TEST_BUILD", StringComparison.Ordinal))
            .Select(file => Path.GetFileName(file));
        Assert.Equal(["JasonVersion.cs"], testing);

        // The door is closed by construction: each variable is read by one exact expression that asks the build itself whether
        // it is a test build, with no flag a caller could pass, and its name stands once in all the sources.
        var source = File.ReadAllText(Path.Combine(root, "dotnet", "src", "Jason.Cli", "Update", "MoveSystem.cs"));
        string[] doors =
        [
            "public static string? TestSigner(IReadOnlyDictionary<string, string>? variables) =>\n"
                + "        Jason.Contracts.JasonVersion.TestBuild && Variable(variables, \"JASON_TEST_BUILD_SIGNER\") is { Length: > 0 } named ? named : null;",
            "public static string RequirementInForce(IReadOnlyDictionary<string, string>? variables) =>\n"
                + "        Jason.Contracts.JasonVersion.TestBuild && Variable(variables, \"JASON_TEST_BUILD_REQUIREMENT\") is { Length: > 0 } named ? named : Requirement;",
        ];
        var unified = source.ReplaceLineEndings("\n");
        foreach (var door in doors)
        {
            Assert.Contains(door, unified, StringComparison.Ordinal);
        }

        // Variable is private and called by the two doors alone.
        Assert.Contains("private static string? Variable(IReadOnlyDictionary<string, string>? variables, string name) =>", unified, StringComparison.Ordinal);
        Assert.Equal(3, Regex.Count(unified, @"\bVariable\("));

        var sources = Directory.EnumerateFiles(Path.Combine(root, "dotnet", "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(File.ReadAllText)
            .ToList();
        foreach (var variable in new[] { "JASON_TEST_BUILD_SIGNER", "JASON_TEST_BUILD_REQUIREMENT" })
        {
            Assert.Equal(1, sources.Sum(text => Regex.Count(text, $"\"{variable}\"")));
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
