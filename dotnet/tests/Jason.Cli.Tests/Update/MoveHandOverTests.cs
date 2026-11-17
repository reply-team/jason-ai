using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli.Commands;
using Jason.Cli.Installation;
using Jason.Cli.Status;
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

    private static int Install(MoveRig rig, StringWriter output, string carried, string? updateLock, string? retire)
    {
        var variables = new Dictionary<string, string>();
        if (updateLock is not null)
        {
            variables[UpdateLock.Variable] = updateLock;
        }

        var env = rig.Env with { Out = output, Error = new StringWriter(), InstallPath = carried, Variables = variables, SearchPath = "/usr/bin:/bin" };
        return InstallCommand.RunAsync(env, new InstallOptions(false, false, retire, StopTimeout: TimeSpan.FromSeconds(5), StopPoll: TimeSpan.FromMilliseconds(20)), CancellationToken.None).GetAwaiter().GetResult();
    }

    // --- a kill at each step ------------------------------------------------------------------------------

    /// <summary>
    /// The child killed at each step of the move, with no code of its own running after: the record says the step it was
    /// in; a jason answers where a new terminal looks (the command-line install until the routine retires it, the
    /// application's after); a runtime can be started from it; <c>jason status</c> says the move stopped there, with the
    /// command that carries it on from that jason; and that command carries it on to the end.
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
    [InlineData(false, MoveSteps.Placed)]
    [InlineData(false, MoveSteps.SetUp)]
    [InlineData(false, MoveSteps.Started)]
    public async Task A_kill_at_any_step_leaves_a_working_installation_and_the_move_carried_on(bool windows, string step)
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
