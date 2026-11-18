using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli.Commands;
using Jason.Cli.Installation;
using Jason.Cli.Status;
using Jason.Cli.Tests.Commands;
using Jason.Cli.Update;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Contracts.Update;

namespace Jason.Cli.Tests.Update;

/// <summary>
/// The move as the one door reaches it: the jason a 0.48 <c>jason update apply</c> put at the command-line install's path and
/// handed the rest of the update to. Its 0.48 parent can never change again, so what the child prints is held to what that
/// parent reads, copied below as 0.48 wrote it; and every kill of the child, at each step of the move, leaves what a person
/// needs: a jason that answers, a runtime that can be started, a record and a <c>jason status</c> that tell the truth, and a
/// <c>jason update apply</c> that carries the move on.
/// </summary>
public sealed class MoveHandOverTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The child finishes the update, then moves the install, and answers one document: the update complete, every line of
    /// its own steps, the move's lines after them, and where the move stands said apart in <c>move</c>. The 0.48 parent
    /// takes it as an answer and passes it on, the move with it.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_child_of_a_0_48_hand_over_finishes_the_update_then_moves_the_install(bool windows)
    {
        using var rig = new MoveRig(windows);
        rig.Installation.Killed(UpdateStep.Swapped);
        var output = new StringWriter();

        var exit = await UpdateCommands.ApplyAsync(rig.Env with { Out = output }, rig.Installation.Feed.AbsoluteUri, null, null, human: false, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var parent = Parent048.Reads(exit, output.ToString(), ["staged: 0.49.0", "swapped: in place"]);
        Assert.NotNull(parent);
        var answer = JsonSerializer.Deserialize<UpdateApplyResponse>(parent!.ToJsonString(), JasonJson.Options)!;
        Assert.Equal("complete", answer.Step);
        Assert.Equal(["staged", "swapped", "plugins", "started", "healthy", "complete"], answer.Steps.Take(6).Select(Name));
        Assert.Contains(answer.Steps, step => step.StartsWith("move: done", StringComparison.Ordinal));
        Assert.Equal(MoveStates.Done, answer.Move!.State);
        Assert.True(rig.Installation.Running);
        Assert.Equal($"launch {rig.Carried}", rig.Acts[^1]);
        Assert.Equal(UpdateStep.Complete, rig.Installation.Ledger()!.Step);
    }

    /// <summary>
    /// A move that fails never fails the update: the child still answers a success, the update complete, and the move's
    /// state and reason beside it; the command-line install answers as it did and its runtime runs.
    /// </summary>
    [Fact]
    public async Task A_move_that_fails_still_answers_the_update_complete()
    {
        using var rig = new MoveRig(windows: true);
        rig.System.SignedBy = "Somebody Else Ltd.";
        rig.Installation.Killed(UpdateStep.Swapped);
        var output = new StringWriter();

        var exit = await UpdateCommands.ApplyAsync(rig.Env with { Out = output }, rig.Installation.Feed.AbsoluteUri, null, null, human: false, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var answer = JsonSerializer.Deserialize<UpdateApplyResponse>(Parent048.Reads(exit, output.ToString(), [])!.ToJsonString(), JasonJson.Options)!;
        Assert.Equal("complete", answer.Step);
        Assert.Equal(MoveStates.Failed, answer.Move!.State);
        Assert.Contains("Somebody Else Ltd.", answer.Move.Reason!, StringComparison.Ordinal);
        Assert.True(File.Exists(rig.Old));
        Assert.True(rig.Installation.Running);
    }

    /// <summary>
    /// On a system the move does not apply to, the child changes nothing and says so in <c>move</c>, with no line of its
    /// own among the update's; and an apply there that finds nothing newer is refused as before, there being no move.
    /// </summary>
    [Fact]
    public async Task Where_the_move_does_not_apply_nothing_changes_and_the_answer_says_so()
    {
        using var rig = new MoveRig(windows: true);
        rig.Installation.Killed(UpdateStep.Swapped);
        var output = new StringWriter();
        var linux = rig.Env with { Out = output, Setup = null };

        var exit = await UpdateCommands.ApplyAsync(linux, rig.Installation.Feed.AbsoluteUri, null, null, human: false, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var answer = JsonSerializer.Deserialize<UpdateApplyResponse>(output.ToString(), JasonJson.Options)!;
        Assert.Equal(MoveStates.NotApplicable, answer.Move!.State);
        Assert.DoesNotContain(answer.Steps, step => step.StartsWith("move:", StringComparison.Ordinal));
        Assert.Null(rig.Record());
        Assert.DoesNotContain(rig.Acts, act => !act.StartsWith("launch", StringComparison.Ordinal));

        var again = new StringWriter();
        Assert.Equal(ExitCodes.ApiError, await UpdateCommands.ApplyAsync(linux with { Out = again }, rig.Installation.Feed.AbsoluteUri, null, null, human: false, Ct));
        Assert.Contains(UpdateCodes.UpToDate, again.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>jason update apply</c> typed into the command-line install again, with nothing newer to apply, carries a move that
    /// is not done on at the version it is, and answers one document with the update's versions both this one.
    /// </summary>
    [Fact]
    public async Task Update_apply_at_the_same_version_carries_a_pending_move_on()
    {
        using var rig = new MoveRig(windows: true);
        rig.Installation.WithRuntime();
        var ledger = rig.Complete();
        rig.System.OnSetup = _ => Task.FromResult(new SetupRun(false, 0));
        Assert.Equal(MoveStates.Pending, (await rig.Move().RunAsync(ledger, rig.Installation.Feed, Ct)).State);
        rig.System.OnSetup = null;
        var output = new StringWriter();

        var exit = await UpdateCommands.ApplyAsync(rig.Env with { Out = output }, rig.Installation.Feed.AbsoluteUri, null, null, human: false, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var answer = JsonSerializer.Deserialize<UpdateApplyResponse>(output.ToString(), JasonJson.Options)!;
        Assert.Equal((SemanticVersion.Current.ToString(), SemanticVersion.Current.ToString()), (answer.From, answer.To));
        Assert.Equal(MoveStates.Done, answer.Move!.State);
        Assert.Equal(MoveStates.Done, rig.Record()!.State);
        Assert.True(rig.Installation.Running);

        // Done, it is not carried on again: nothing newer is the refusal it always was.
        var again = new StringWriter();
        rig.Acts.Clear();
        Assert.Equal(ExitCodes.ApiError, await UpdateCommands.ApplyAsync(rig.Env with { Out = again, InstallPath = rig.Carried }, rig.Installation.Feed.AbsoluteUri, null, null, human: false, Ct));
    }

    /// <summary>
    /// The application's routine is this build's own <c>jason install --retire</c>, run as the move's child: handed the move's
    /// nonce it joins the move's hold rather than refuse, stops the command-line install's runtime leaving the note that a
    /// start is owed, retires that install, and prints the report the move reads; the move then starts the runtime from the
    /// application's jason. macOS, whose setup writes only into the rig's own home; the same routine handed no nonce is
    /// refused while the move holds the lock, as the installer's own <c>jason install</c> is.
    /// </summary>
    [Fact]
    public async Task The_real_routine_joins_the_moves_hold_retires_the_install_and_the_move_starts_the_runtime()
    {
        using var rig = new MoveRig(windows: false);
        rig.Installation.WithRuntime();
        var refusedWithout = new List<string>();
        rig.Routine = (command, updateLock) =>
        {
            rig.Acts.Add("routine");

            // The installer's own `jason install`, handed nothing, while the move holds the lock.
            var bare = new StringWriter();
            Assert.Equal(ExitCodes.ApiError, Install(rig, bare, command[0], null, null));
            refusedWithout.Add(JsonNode.Parse(bare.ToString())!["error"]!["code"]!.GetValue<string>());

            var said = new StringWriter();
            var exit = Install(rig, said, command[0], updateLock, command[command.ToList().IndexOf("--retire") + 1]);
            return new Jason.Cli.Process.FinishedCommand(exit, said.ToString());
        };

        // The command-line install where install.sh put it, as the update recorded it.
        var old = Path.Combine(rig.Home, ".local", "bin", "jason");
        Directory.CreateDirectory(Path.GetDirectoryName(old)!);
        File.Copy(rig.Old, old);
        rig.Installation.RunsFrom = old;
        rig.Installation.WithRuntime();
        var ledger = rig.Complete() with { InstallPath = old };
        ledger.Write(rig.Update.Ledger);

        var answer = await rig.Move().RunAsync(ledger, rig.Installation.Feed, Ct);

        Assert.Equal([UpdateCodes.InProgress], refusedWithout);
        Assert.True(answer.State == MoveStates.Done, answer.Reason);
        Assert.True(CommandFiles.IsWrapperFile(old), "the wrapper over the application's jason has taken the install's place");
        Assert.Equal($"launch {rig.Carried}", rig.Acts[^1]);
        Assert.True(rig.Installation.Running);
        Assert.False(StartOwed.Read(rig.Installation.Paths).Stands);
        Assert.Equal(CheckState.Ok, StatusChecks.Move(rig.Env with { InstallPath = rig.Carried }).State);
    }

    private static int Install(MoveRig rig, StringWriter output, string carried, string? updateLock, string? retire) =>
        Install(rig, output, carried, updateLock is null ? [] : new Dictionary<string, string> { [UpdateLock.Variable] = updateLock }, retire, null);

    private static int Install(MoveRig rig, StringWriter output, string carried, Dictionary<string, string> variables, string? retire, SetupPlaces? places)
    {
        var env = rig.Env with { Out = output, Error = new StringWriter(), InstallPath = carried, Variables = variables, SearchPath = "/usr/bin:/bin", Setup = places ?? rig.Places };
        return InstallCommand.RunAsync(env, new InstallOptions(false, false, retire, StopTimeout: TimeSpan.FromSeconds(5), StopPoll: TimeSpan.FromMilliseconds(20)), CancellationToken.None).GetAwaiter().GetResult();
    }

    // --- the installer's own jason install ------------------------------------------------------------------

    /// <summary>
    /// The installer's own <c>jason install</c>, a grandchild through NSIS, is given the environment the move gives the setup:
    /// never the update's nonce, even where the move's own environment held one, and the move's marker naming its record. It does
    /// nothing under it — nothing placed, retired or stopped — says so in its one document and exits 0, as the installer needs;
    /// the move's own routine runs once the setup has returned, handed the nonce. Windows, the routine the setup runs: it ends
    /// before the Path it would write is read.
    /// </summary>
    [Fact]
    public async Task The_installers_own_jason_install_does_nothing_under_the_moves_marker()
    {
        using var rig = new MoveRig(windows: true);
        rig.Installation.WithRuntime();
        rig.System.ProcessVariables = new Dictionary<string, string> { [UpdateLock.Variable] = "a-nonce-the-move-process-was-handed" };
        var grandchild = new List<(bool Nonce, bool Marker, int Exit, string Left)>();
        rig.System.OnSetup = call =>
        {
            MoveRig.PlantApplication(call.Place!, windows: true, SemanticVersion.Current.ToString());

            // NSIS's customInstall: `"$INSTDIR\resources\bundle\jason.exe" install`, in the setup's environment.
            var said = new StringWriter();
            var exit = Install(rig, said, MoveRig.CarriedOf(call.Place!, windows: true), call.Environment.ToDictionary(), null, null);
            var report = JsonNode.Parse(said.ToString())!;
            Assert.Null(report["retired"]);
            Assert.Null(report["command"]);
            grandchild.Add((call.Environment.ContainsKey(UpdateLock.Variable), call.Environment.GetValueOrDefault(MoveMarker.Variable) == rig.Record()!.Marker, exit, report["left"]![0]!.GetValue<string>()));
            Assert.True(File.Exists(rig.Old), "the command-line install stands while the setup runs");
            Assert.True(rig.Installation.Running, "its runtime runs while the setup runs");
            return Task.FromResult(new SetupRun(true, 0));
        };

        var answer = await rig.Move().RunAsync(rig.Complete(), rig.Installation.Feed, Ct);

        var (nonce, marker, code, left) = Assert.Single(grandchild);
        Assert.False(nonce);
        Assert.True(marker);
        Assert.Equal(ExitCodes.Success, code);
        Assert.Contains("did nothing", left, StringComparison.Ordinal);
        Assert.Equal(MoveStates.Done, answer.State);
        Assert.Equal(["signature", $"setup /S /D={rig.Application}", "routine", "stop", $"launch {rig.Carried}"], rig.Acts);
        Assert.NotNull(rig.RoutineLocks.Single().Handed);
    }

    /// <summary>
    /// A setup that outlives the move's bound, or one the move was killed under, goes on alone with the move's marker, the lock
    /// let go (the bound ends the move; a kill ends its process, and with it the open handle that is the lock). Its own
    /// <c>jason install</c> then does nothing while the record stands not done: the command-line install and its runtime stand
    /// as they were. The next <c>jason update apply</c> keeps the marker, finds the application the setup installed, reuses it
    /// and runs the routine under its own hold, and starts the runtime the routine stopped.
    /// </summary>
    [Fact]
    public async Task A_setup_that_outlives_the_move_changes_nothing_and_the_next_apply_carries_the_move_on_into_it()
    {
        using var rig = new MoveRig(windows: true);
        rig.Installation.WithRuntime();
        SetupCall? orphan = null;
        rig.System.OnSetup = call =>
        {
            orphan = call;
            return Task.FromResult(new SetupRun(false, 0));
        };

        var answer = await rig.Move().RunAsync(rig.Complete(), rig.Installation.Feed, Ct);

        Assert.Equal(MoveStates.Pending, answer.State);
        Assert.Contains("never ended", answer.Reason!, StringComparison.Ordinal);
        Assert.False(File.Exists(rig.Update.LockHolder), "the move let its lock go");
        var marker = rig.Record()!.Marker;

        // The setup goes on and ends: the application in place, then its customInstall with the lock free.
        MoveRig.PlantApplication(orphan!.Place!, windows: true, SemanticVersion.Current.ToString());
        var said = new StringWriter();
        Assert.Equal(ExitCodes.Success, Install(rig, said, MoveRig.CarriedOf(orphan.Place!, windows: true), orphan.Environment.ToDictionary(), null, null));
        Assert.Contains("did nothing", said.ToString(), StringComparison.Ordinal);
        Assert.True(File.Exists(rig.Old));
        Assert.True(rig.Installation.Running, "nothing stopped the runtime");
        Assert.False(StartOwed.Read(rig.Installation.Paths).Stands);
        Assert.Equal(CheckState.Failed, StatusChecks.Move(rig.Env).State);

        // The next apply, from the command-line install.
        rig.System.OnSetup = null;
        rig.Acts.Clear();
        var output = new StringWriter();
        Assert.Equal(ExitCodes.Success, await UpdateCommands.ApplyAsync(rig.Env with { Out = output }, rig.Installation.Feed.AbsoluteUri, null, null, human: false, Ct));

        Assert.Equal(MoveStates.Done, JsonSerializer.Deserialize<UpdateApplyResponse>(output.ToString(), JasonJson.Options)!.Move!.State);
        Assert.Equal(marker, rig.Record()!.Marker);
        Assert.Equal(["signature", "routine", "stop", $"launch {rig.Carried}"], rig.Acts);
        Assert.True(rig.Installation.Running);
    }

    /// <summary>
    /// The marker counts only while it names the record standing not done: one with no record, one naming a record that is
    /// done, or another record's, is ignored, and <c>jason install</c> sets the machine up as it always does. A variable left in
    /// somebody's shell can never switch the routine off. macOS, whose setup writes only into the rig's own home.
    /// </summary>
    [Theory]
    [InlineData("no record")]
    [InlineData("done")]
    [InlineData("another marker")]
    public void A_marker_naming_no_standing_record_is_ignored(string record)
    {
        using var rig = new MoveRig(windows: false);
        rig.Complete();
        MoveRig.PlantApplication(rig.Application, windows: false, SemanticVersion.Current.ToString());
        if (record != "no record")
        {
            new MoveRecord(SemanticVersion.Current.ToString(), rig.Old, record == "done" ? MoveStates.Done : MoveStates.Pending, MoveSteps.Verified, null, rig.Application, null, null, DateTimeOffset.UtcNow, "the-moves-marker").Write(rig.Update.Move);
        }

        var said = new StringWriter();
        var exit = Install(rig, said, rig.Carried, new Dictionary<string, string> { [MoveMarker.Variable] = record == "another marker" ? "a-marker-left-in-a-shell" : "the-moves-marker" }, null, null);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.DoesNotContain("did nothing", said.ToString(), StringComparison.Ordinal);
        Assert.NotNull(JsonNode.Parse(said.ToString())!["command"]);
    }

    // --- going back --------------------------------------------------------------------------------------

    /// <summary>
    /// Once the move is done, a rollback from the application's jason refuses: it names no path, the retired install's
    /// above all, and no command, and points to the page's section on going back from 0.49; nothing is changed.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task After_a_done_move_a_rollback_refuses_and_points_to_going_back_from_0_49(bool windows)
    {
        using var rig = new MoveRig(windows);
        rig.Installation.WithRuntime();
        Assert.Equal(MoveStates.Done, (await rig.Move().RunAsync(rig.Complete(), rig.Installation.Feed, Ct)).State);
        var ledger = File.ReadAllText(rig.Update.Ledger);
        var output = new StringWriter();

        var exit = await UpdateCommands.RollBackAsync(rig.Env with { Out = output, InstallPath = rig.Carried }, human: false, Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var refusal = RuntimeVerbs.Envelope(output.ToString());
        Assert.Equal(UpdateCodes.CarriedByDesktop, refusal.Code);
        Assert.EndsWith(PublicRepository.GoingBackFrom049, refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(rig.Installation.Root, refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("`", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(ledger, File.ReadAllText(rig.Update.Ledger));
        Assert.True(File.Exists(rig.Update.PreviousExecutable));
        Assert.True(rig.Installation.Running);
    }

    /// <summary>
    /// A rollback where the install is retired but the move's record is not done — a move stopped after the routine — refuses
    /// the same way: the rule the status check reads says the install is retired, and no path of it is named.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_rollback_with_the_install_retired_and_the_record_not_done_refuses_the_same_way(bool windows)
    {
        using var rig = new MoveRig(windows);
        rig.Installation.WithRuntime();
        Assert.Equal(MoveStates.Done, (await rig.Move().RunAsync(rig.Complete(), rig.Installation.Feed, Ct)).State);
        (rig.Record()! with { State = MoveStates.Moving, Step = MoveSteps.SetUp, Doing = MoveSteps.Started }).Write(rig.Update.Move);
        Assert.True(MoveRecord.Retired(rig.Old));
        var output = new StringWriter();

        var exit = await UpdateCommands.RollBackAsync(rig.Env with { Out = output, InstallPath = rig.Carried }, human: false, Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var refusal = RuntimeVerbs.Envelope(output.ToString());
        Assert.Equal(UpdateCodes.CarriedByDesktop, refusal.Code);
        Assert.EndsWith(PublicRepository.GoingBackFrom049, refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(rig.Installation.Root, refusal.Message, StringComparison.OrdinalIgnoreCase);
    }

    // --- nothing of the move fails the update -------------------------------------------------------------

    /// <summary>
    /// The sweep of retired executables meeting something it never expected — here a delete that throws what no file system
    /// throws — leaves the update complete and the move done: nothing of the move, the sweep included, fails the update.
    /// </summary>
    [Fact]
    public async Task A_sweep_that_meets_the_unexpected_never_fails_the_update()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The sweep is a Windows routine's.");
        using var rig = new MoveRig(windows: true);
        var aside = Path.Combine(rig.Temporary, $"jason-retired-{Guid.NewGuid():N}");
        Directory.CreateDirectory(aside);
        File.WriteAllText(Path.Combine(aside, "jason.exe"), "jason 0.48.2");
        rig.Installation.Killed(UpdateStep.Swapped);
        var output = new StringWriter();
        var throwing = rig.Env with { Out = output, Setup = rig.Places with { Delete = _ => throw new InvalidOperationException("a delete nobody expected") } };
        rig.Installation.Machine = throwing;

        var exit = await UpdateCommands.ApplyAsync(throwing, rig.Installation.Feed.AbsoluteUri, null, null, human: false, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var answer = JsonSerializer.Deserialize<UpdateApplyResponse>(Parent048.Reads(exit, output.ToString(), [])!.ToJsonString(), JasonJson.Options)!;
        Assert.Equal("complete", answer.Step);
        Assert.Equal(MoveStates.Done, answer.Move!.State);
        Assert.True(Directory.Exists(aside), "what the sweep could not take is left for the next");
    }

    // --- the setup's command line -----------------------------------------------------------------------------

    /// <summary>
    /// A profile with a space in its folder's name: the setup's place is the last thing on its raw command line, unquoted, as
    /// NSIS takes it and install.ps1 passes it, so NSIS installs where the move looks; an argument list would have quoted it,
    /// and NSIS would have installed into its default instead.
    /// </summary>
    [Fact]
    public async Task The_setup_is_given_its_place_unquoted_where_the_path_has_a_space()
    {
        using var rig = new MoveRig(windows: true, localData: "Local Data");
        rig.Installation.WithRuntime();
        Assert.Contains(" ", rig.Application, StringComparison.Ordinal);

        var answer = await rig.Move().RunAsync(rig.Complete(), rig.Installation.Feed, Ct);

        Assert.Equal(MoveStates.Done, answer.State);
        Assert.Contains($"setup /S /D={rig.Application}", rig.Acts);
        Assert.Equal(rig.Application, SetupCall.NsisPlace(MoveSystem.SetupCommandLine(rig.Application)));
        Assert.Null(SetupCall.NsisPlace($"/S \"/D={rig.Application}\""));
        Assert.Equal(SemanticVersion.Current.ToString(), CarriedExecutable.Of(rig.Carried)!.Version);
    }

    // --- what a person or an agent waiting sees ------------------------------------------------------------

    /// <summary>
    /// One line on standard error at each step, and while a download runs, each beginning <c>move:</c>; the 0.48 parent passes
    /// standard error through as it comes, so minutes of a download, a setup and a routine are never minutes of silence. The
    /// document on standard output is untouched by them.
    /// </summary>
    [Fact]
    public async Task Each_step_and_a_running_download_say_so_on_standard_error()
    {
        using var rig = new MoveRig(windows: true);
        rig.Installation.WithRuntime();
        var error = new StringWriter();
        var move = new CommandLineMove(rig.Env with { Error = error }, rig.Update, TimeProvider.System)
        {
            Poll = TimeSpan.FromMilliseconds(20),
            StartWait = TimeSpan.FromSeconds(5),
            ProgressEvery = TimeSpan.Zero,
        };

        Assert.Equal(MoveStates.Done, (await move.RunAsync(rig.Complete(), rig.Installation.Feed, Ct)).State);

        var lines = error.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string[] expected =
        [
            "move: looking for Jason Desktop",
            "move: downloading Jason Desktop",
            "move: checking Jason Desktop against the release's checksums and its publisher's signature",
            "move: installing Jason Desktop",
            "move: setting this machine up through Jason Desktop's jason",
            "move: starting the runtime from Jason Desktop's jason",
        ];
        Assert.Equal(expected, lines.Where(expected.Contains));
        Assert.Contains(lines, line => line.StartsWith("move: downloading http", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("move: downloaded ", StringComparison.Ordinal));
        Assert.DoesNotContain("move:", rig.Installation.Out.ToString(), StringComparison.Ordinal);
    }

    // --- a kill inside the real routine ---------------------------------------------------------------------

    /// <summary>
    /// The routine is this build's own <c>jason install --retire</c>, stopped right after it stopped the command-line install's
    /// runtime, before it writes the wrapper — the stop-and-note prefix — and held there with no code of its own running. What
    /// stands: the command-line install, whole, answers as jason; the note says a start is owed; the record says the move
    /// stopped at <c>set_up</c>, and status names <c>jason update apply</c>, which carries it on with the start owed honoured.
    /// Let go, the routine retires the install and the move starts the runtime from the application's jason. macOS, whose
    /// setup writes only into the rig's own home.
    /// </summary>
    [Fact]
    public async Task A_kill_inside_the_real_routine_after_its_stop_leaves_the_install_whole_and_the_start_owed()
    {
        using var rig = new MoveRig(windows: false);
        var old = Path.Combine(rig.Home, ".local", "bin", "jason");
        Directory.CreateDirectory(Path.GetDirectoryName(old)!);
        File.Copy(rig.Old, old);
        rig.Installation.RunsFrom = old;
        rig.Installation.WithRuntime();
        var ledger = rig.Complete() with { InstallPath = old };
        ledger.Write(rig.Update.Ledger);
        rig.Routine = (command, updateLock) =>
        {
            rig.Acts.Add("routine");
            var said = new StringWriter();
            var exit = Install(rig, said, command[0], updateLock, command[command.ToList().IndexOf("--retire") + 1]);
            return new Jason.Cli.Process.FinishedCommand(exit, said.ToString());
        };

        var stopped = new TaskCompletionSource();
        using var resume = new ManualResetEventSlim();
        rig.Installation.OnStop = () =>
        {
            stopped.TrySetResult();
            resume.Wait();
        };
        var run = Task.Run(() => rig.Move().RunAsync(ledger, rig.Installation.Feed, CancellationToken.None), CancellationToken.None);
        try
        {
            await stopped.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);

            Assert.False(CommandFiles.IsWrapperFile(old), "the command-line install is whole");
            Assert.True(File.Exists(old));
            Assert.False(rig.Installation.Running);
            Assert.True(StartOwed.Read(rig.Installation.Paths).Stands, "the routine left the note before its stop");
            Assert.Equal((MoveStates.Moving, MoveSteps.SetUp), (rig.Record()!.State, rig.Record()!.Doing));
            var check = StatusChecks.Move(rig.Env with { InstallPath = old });
            Assert.Equal(CheckState.Failed, check.State);
            Assert.Equal("jason update apply", check.Fix);
        }
        finally
        {
            resume.Set();
        }

        var answer = await run.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        Assert.Equal(MoveStates.Done, answer.State);
        Assert.True(CommandFiles.IsWrapperFile(old));
        Assert.True(rig.Installation.Running);
    }

    // --- a kill at each step ------------------------------------------------------------------------------

    /// <summary>
    /// The child stopped at the boundary before each step of the move — its record written, the step not begun — and held
    /// there with no code of its own running, as a kill at that moment leaves it: the record says the step; a jason answers
    /// where a new terminal looks (the command-line install until the routine retires it, the application's after); a runtime
    /// runs or the note says one is owed; <c>jason status</c> says the move stopped there, with the command that carries it on
    /// from that jason, or that the install is retired; a rollback from the application's jason refuses and points to going
    /// back from 0.49 once the install is retired; and that command, run in a process of its own, carries the move on to the
    /// end. A kill inside a step is not stood here, the routine's own excepted (the row after); a real machine's is U8's.
    /// </summary>
    [Theory]
    [InlineData(true, MoveSteps.Looked)]
    [InlineData(true, MoveSteps.Fetched)]
    [InlineData(true, MoveSteps.Verified)]
    [InlineData(true, MoveSteps.Placed)]
    [InlineData(true, MoveSteps.SetUp)]
    [InlineData(true, MoveSteps.Started)]
    [InlineData(false, MoveSteps.Looked)]
    [InlineData(false, MoveSteps.Fetched)]
    [InlineData(false, MoveSteps.Verified)]
    [InlineData(false, MoveSteps.Placed)]
    [InlineData(false, MoveSteps.SetUp)]
    [InlineData(false, MoveSteps.Started)]
    public async Task A_kill_at_the_boundary_before_each_step_leaves_a_working_installation_and_the_move_carried_on(bool windows, string step)
    {
        using var rig = new MoveRig(windows);
        rig.Installation.WithRuntime();
        var ledger = rig.Complete();

        // What a kill leaves is what the move wrote before it began that step, and what the steps before it did: the move is
        // stopped there, and its record copied as it stood, before anything of the move's own runs again.
        string? left = null;
        var killed = new TaskCompletionSource();
        using var resume = new ManualResetEventSlim();
        var move = rig.Move(written =>
        {
            if (left is null && written.State == MoveStates.Moving && written.Doing == step)
            {
                left = File.ReadAllText(rig.Update.Move);
                killed.TrySetResult();
                resume.Wait();
            }
        });
        var run = Task.Run(() => move.RunAsync(ledger, rig.Installation.Feed, CancellationToken.None), CancellationToken.None);
        try
        {
            await killed.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
            await WhatAKillLeaves(rig, windows, step, left!);
        }
        finally
        {
            // The stopped move let go, so that it ends in this rig before the rig is taken away; what it does then is no
            // part of the row.
            resume.Set();
            await run.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        }
    }

    private static async Task WhatAKillLeaves(MoveRig rig, bool windows, string step, string left)
    {
        var record = MoveRecord.Read(rig.Update.Move)!;
        Assert.Equal((MoveStates.Moving, step), (record.State, record.Doing));
        Assert.Equal(left, File.ReadAllText(rig.Update.Move));

        // A jason a new terminal finds, whole, and a runtime that runs or can be started from it.
        var routineRan = rig.Acts.Contains("routine");
        var answers = routineRan ? rig.Carried : rig.Old;
        Assert.True(File.Exists(answers), $"{answers} answers as jason after a kill at '{step}'");
        Assert.True(rig.Installation.Running || StartOwed.Read(rig.Installation.Paths).Stands, "a runtime runs, or the note says one is owed");
        if (routineRan && !rig.Installation.Running)
        {
            Assert.True(StartOwed.Read(rig.Installation.Paths).Stands);
        }

        // `jason status` from that jason: the move stopped at that step, carried on by the command that jason runs.
        var check = StatusChecks.Move(rig.Env with { InstallPath = answers });
        if (routineRan)
        {
            // Retired by the routine: the move's purpose stands, and the start it owes is the runtime check's to name.
            Assert.Equal(CheckState.Ok, check.State);
            Assert.Contains("is retired into Jason Desktop", check.Fact, StringComparison.Ordinal);

            // And a rollback from the application's jason, the record not done: no path of the retired install, no command.
            var said = new StringWriter();
            Assert.Equal(ExitCodes.ApiError, await UpdateCommands.RollBackAsync(rig.Env with { Out = said, InstallPath = rig.Carried }, human: false, Ct));
            Assert.EndsWith(PublicRepository.GoingBackFrom049, RuntimeVerbs.Envelope(said.ToString()).Message, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(CheckState.Failed, check.State);
            Assert.Contains($"stopped at '{step}'", check.Fact, StringComparison.Ordinal);
            Assert.Equal("jason update apply", check.Fix);
        }

        // Carried on by `jason update apply` in a process of its own, the killed one's lock gone with it: a second rig given the
        // state the kill left, since the stopped move still holds this rig's lock in this process.
        if (!routineRan)
        {
            using var after = new MoveRig(windows);
            CopyState(rig, after, left);
            after.Installation.WithRuntime();
            var output = new StringWriter();
            var exit = await UpdateCommands.ApplyAsync(after.Env with { Out = output }, after.Installation.Feed.AbsoluteUri, null, null, human: false, Ct);
            Assert.Equal(ExitCodes.Success, exit);
            Assert.Equal(MoveStates.Done, JsonSerializer.Deserialize<UpdateApplyResponse>(output.ToString(), JasonJson.Options)!.Move!.State);
            Assert.True(after.Installation.Running);
        }
    }

    // --- what 0.48 reads ------------------------------------------------------------------------------------

    /// <summary>
    /// What the 0.48 parent reads, held by documents of each kind the child prints: an exit code of 0, 1, 2 or 3 and one JSON
    /// object; with 0, a <c>steps</c> array of strings, which it puts its own lines in front of, carrying every other member as
    /// it came. Anything else is <c>update_not_healthy</c> to it.
    /// </summary>
    [Theory]
    [InlineData(MoveStates.Done)]
    [InlineData(MoveStates.Pending)]
    [InlineData(MoveStates.Failed)]
    [InlineData(MoveStates.NotApplicable)]
    public void Every_document_the_child_prints_is_one_the_0_48_parent_takes(string state)
    {
        var printed = JsonSerializer.Serialize(
            new UpdateApplyResponse("0.48.2", "0.49.0", "complete", ["complete: 0.49.0", "move: a line"], new UpdateMoveResponse(state, MoveSteps.Placed, "/Applications/Jason.app", state == MoveStates.Done ? null : "a reason.")),
            JasonJson.Options);

        var taken = Parent048.Reads(ExitCodes.Success, printed, ["swapped: 0.49.0"]);

        Assert.NotNull(taken);
        Assert.Equal(["swapped: 0.49.0", "complete: 0.49.0", "move: a line"], taken!["steps"]!.AsArray().Select(step => step!.GetValue<string>()));
        Assert.Equal(state, taken["move"]!["state"]!.GetValue<string>());
        Assert.Equal(Fixture, Parent048.Reads(ExitCodes.Success, Fixture, [])!.ToJsonString(JasonJson.Options));
    }

    /// <summary>A document as the child prints it after a move, as a 0.48 parent has to keep reading it.</summary>
    private const string Fixture = """{"from":"0.48.2","to":"0.49.0","step":"complete","steps":["complete: 0.49.0","move: done: /Users/a/.local/bin/jason is in Jason Desktop at /Applications/Jason.app"],"move":{"state":"done","step":"started","application":"/Applications/Jason.app","reason":null}}""";

    /// <summary>
    /// The 0.48 parent's reading of what its child printed, as 0.48 wrote it (UpdateCommands.HandOverAsync and
    /// WithStepsBefore at 0.48.x): copied, never shared, since that half can never change again. Null where it refuses
    /// the answer as <c>update_not_healthy</c>.
    /// </summary>
    private static class Parent048
    {
        public static JsonObject? Reads(int exitCode, string printed, IReadOnlyList<string> before)
        {
            if (exitCode is not (0 or 1 or 2 or 3) || Document(printed) is not { } document)
            {
                return null;
            }

            if (exitCode != 0)
            {
                return document;
            }

            if (document["steps"] is not JsonArray after || after.Any(step => step?.GetValueKind() != JsonValueKind.String))
            {
                return null;
            }

            document["steps"] = new JsonArray([.. before.Select(step => (JsonNode?)JsonValue.Create(step)), .. after.Select(step => step!.DeepClone())]);
            return document;
        }

        private static JsonObject? Document(string printed)
        {
            try
            {
                return JsonNode.Parse(printed) as JsonObject;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    // --- helpers -------------------------------------------------------------------------------------------

    private static string Name(string step) => step[..step.IndexOf(':', StringComparison.Ordinal)];

    /// <summary>The state a kill left in one rig, as a second process finds it: the ledger, the record, and the application where one was put in place.</summary>
    private static void CopyState(MoveRig from, MoveRig to, string record)
    {
        to.Complete();
        File.WriteAllText(to.Update.Move, record.Replace(Escape(from.Old), Escape(to.Old), StringComparison.Ordinal).Replace(Escape(from.Installation.Feed.AbsoluteUri), Escape(to.Installation.Feed.AbsoluteUri), StringComparison.Ordinal));
        var application = from.Record()?.Application;
        if (application is not null && Directory.Exists(application) && CarriedExecutable.Of(from.Carried) is { } placed)
        {
            MoveRig.PlantApplication(to.Application, to.Windows, placed.Version);
        }
    }

    private static string Escape(string text) => JsonSerializer.Serialize(text)[1..^1];
}
