using Jason.Cli.Commands;
using Jason.Cli.Status;
using Jason.Cli.Tests.Commands;
using Jason.Cli.Tests.Process;
using Jason.Cli.Update;
using Jason.Contracts.Update;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Status;

/// <summary>
/// An update that stopped part-way is seen by the two verbs a person reaches for next. One that failed after the swap
/// left the runtime stopped and the new build at the install path; `jason status` said only that no runtime was running
/// and offered `jason runtime start`, which ran the new build outside the update and left its last steps — the health
/// check, the skills, the UI, the ledger's own end — undone. The ledger says what is in flight, and both verbs read it.
/// </summary>
public sealed class UpdateInFlightCheckTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(UpdateStep.Kept, "kept")]
    [InlineData(UpdateStep.Swapped, "swapped")]
    [InlineData(UpdateStep.Started, "started")]
    [InlineData(UpdateStep.Healthy, "healthy")]
    public async Task Status_names_an_update_left_in_flight_and_its_repair_is_the_apply_that_finishes_it(UpdateStep step, string said)
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        Ledger(dir, step).Write(new UpdatePaths(dir.Paths).Ledger);

        var exit = await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), Ct);

        var report = StatusCommandTests.Read(output);
        Assert.False(report.Ready);
        Assert.Equal(ExitCodes.ApiError, exit);
        var runtime = Assert.Single(report.Checks, check => check.Name == "runtime");
        Assert.True(runtime.Required);
        Assert.Equal(CheckState.Failed, runtime.State);
        Assert.Equal("jason update apply", runtime.Fix);
        Assert.Contains($"An update from 0.1.0 to 9.9.9 stopped at '{said}' and is not finished", runtime.Fact, StringComparison.Ordinal);
        Assert.DoesNotContain("jason runtime start", runtime.Fact, StringComparison.Ordinal);
    }

    /// <summary>A runtime answering does not finish an update either: its last steps are the apply's, not the runtime's.</summary>
    [Fact]
    public async Task A_runtime_running_beside_an_unfinished_update_is_not_ready()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        StatusCommandTests.Running(dir);
        Ledger(dir, UpdateStep.Started).Write(new UpdatePaths(dir.Paths).Ledger);

        var exit = await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), Ct);

        var report = StatusCommandTests.Read(output);
        Assert.False(report.Ready);
        Assert.Equal(ExitCodes.ApiError, exit);
        var runtime = Assert.Single(report.Checks, check => check.Name == "runtime");
        Assert.Equal(CheckState.Failed, runtime.State);
        Assert.Equal("jason update apply", runtime.Fix);
    }

    /// <summary>A completed ledger is the record of the last update, kept for a rollback, and never work in flight.</summary>
    [Fact]
    public async Task A_completed_update_changes_nothing_status_says()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        StatusCommandTests.Running(dir);
        Ledger(dir, UpdateStep.Complete).Write(new UpdatePaths(dir.Paths).Ledger);

        var exit = await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), Ct);

        var report = StatusCommandTests.Read(output);
        Assert.True(report.Ready);
        Assert.Equal(ExitCodes.Success, exit);
    }

    /// <summary>
    /// Before the step that touches the install path, the installed build is still the one that was there: an apply that
    /// could not download, or met an asset that failed its digest, stops at 'staged' with nothing replaced. Status failing a
    /// required check there stopped every agent that branches on ready until the network came back; the runtime check is the
    /// runtime's again, and an optional check names the update and the apply that resumes it — never the rollback, which has
    /// nothing of this update's to put back.
    /// </summary>
    [Theory]
    [InlineData(UpdateStep.Staged, "staged")]
    [InlineData(UpdateStep.Drained, "drained")]
    [InlineData(UpdateStep.Stopped, "stopped")]
    public async Task Before_the_swap_status_is_ready_beside_a_runtime_and_an_optional_check_names_the_update(UpdateStep step, string said)
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        StatusCommandTests.Running(dir);
        Ledger(dir, step).Write(new UpdatePaths(dir.Paths).Ledger);

        var exit = await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), Ct);

        var report = StatusCommandTests.Read(output);
        Assert.True(report.Ready);
        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(CheckState.Ok, Assert.Single(report.Checks, check => check.Name == "runtime").State);
        var update = Assert.Single(report.Checks, check => check.Name == "update");
        Assert.False(update.Required);
        Assert.Equal(CheckState.Failed, update.State);
        Assert.Equal("jason update apply", update.Fix);
        Assert.Contains($"An update from 0.1.0 to 9.9.9 stopped at '{said}' and is not finished", update.Fact, StringComparison.Ordinal);
        Assert.Contains("nothing has been replaced", update.Fact, StringComparison.Ordinal);
        Assert.DoesNotContain("rollback", update.Fact, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_no_update_in_flight_the_update_check_is_ok()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        StatusCommandTests.Running(dir);

        await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), Ct);

        var update = Assert.Single(StatusCommandTests.Read(output).Checks, check => check.Name == "update");
        Assert.Equal(CheckState.Ok, update.State);
        Assert.Null(update.Fix);
    }

    /// <summary>
    /// And the start verb starts that installed build: refusing it there left an installation with no runtime and no way on
    /// but the network. It says the update is waiting, and the apply that resumes it.
    /// </summary>
    [Theory]
    [InlineData(UpdateStep.Staged, "staged")]
    [InlineData(UpdateStep.Drained, "drained")]
    [InlineData(UpdateStep.Stopped, "stopped")]
    public async Task Before_the_swap_runtime_start_starts_the_installed_build_and_says_the_update_waits(UpdateStep step, string said)
    {
        using var dir = new TempPaths();
        Ledger(dir, step).Write(new UpdatePaths(dir.Paths).Ledger);
        var processes = new FakeProcessControl { OnLaunch = RuntimeVerbs.PublishesAfterAWhile(dir, RuntimeVerbs.Descriptor("rt_NEW")) };
        var (env, output, error) = RuntimeVerbs.Environment(dir, RuntimeVerbs.EchoesTheDescriptor(dir), processes);

        var exit = await RuntimeStartCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Single(processes.Launches);
        Assert.Contains("rt_NEW", output.ToString(), StringComparison.Ordinal);
        var told = error.ToString();
        Assert.Contains($"An update from 0.1.0 to 9.9.9 stopped at '{said}' and is not finished", told, StringComparison.Ordinal);
        Assert.Contains("nothing has been replaced", told, StringComparison.Ordinal);
        Assert.Contains("`jason update apply`", told, StringComparison.Ordinal);
        Assert.DoesNotContain("rollback", told, StringComparison.Ordinal);
    }

    /// <summary>
    /// From the step that moves the installed build aside, the install path is no longer the build that was there, and the
    /// start starts nothing. It says the two ways on, and nothing about what a start would have run: at 'kept' there is
    /// nothing at the path to run.
    /// </summary>
    [Theory]
    [InlineData(UpdateStep.Kept, "kept")]
    [InlineData(UpdateStep.Swapped, "swapped")]
    [InlineData(UpdateStep.Started, "started")]
    [InlineData(UpdateStep.Healthy, "healthy")]
    public async Task From_the_swap_on_runtime_start_starts_nothing_and_says_how_to_finish_it(UpdateStep step, string said)
    {
        using var dir = new TempPaths();
        Ledger(dir, step).Write(new UpdatePaths(dir.Paths).Ledger);
        var processes = new FakeProcessControl { OnLaunch = _ => throw new InvalidOperationException("an update in flight must not be started around") };
        var (env, output, _) = RuntimeVerbs.Environment(dir, RuntimeVerbs.NeverCalled(), processes);

        var exit = await RuntimeStartCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Empty(processes.Launches);
        var envelope = RuntimeVerbs.Envelope(output.ToString());
        Assert.Equal(UpdateCodes.InProgress, envelope.Code);
        Assert.Contains($"An update from 0.1.0 to 9.9.9 stopped at '{said}' and is not finished", envelope.Message, StringComparison.Ordinal);
        Assert.Contains("`jason update apply`", envelope.Message, StringComparison.Ordinal);
        Assert.Contains("`jason update rollback`", envelope.Message, StringComparison.Ordinal);
        Assert.Contains("Nothing was started.", envelope.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("outside the update", envelope.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A restart stopped the runtime and then met the start's refusal, leaving nothing running. It reads the ledger before
    /// the stop now, and refuses the same way with the runtime still up.
    /// </summary>
    [Fact]
    public async Task Runtime_restart_stops_nothing_while_an_update_is_in_flight_from_the_swap_on()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_OLD"));
        Ledger(dir, UpdateStep.Started).Write(new UpdatePaths(dir.Paths).Ledger);
        var processes = new FakeProcessControl { OnLaunch = _ => throw new InvalidOperationException("an update in flight must not be started around") };
        var (env, output, _) = RuntimeVerbs.Environment(dir, RuntimeVerbs.NeverCalled(), processes);

        var exit = await RuntimeRestartCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Empty(processes.Launches);
        Assert.True(File.Exists(dir.Paths.DescriptorFile), "the restart stopped the runtime before it refused.");
        var envelope = RuntimeVerbs.Envelope(output.ToString());
        Assert.Equal(UpdateCodes.InProgress, envelope.Code);
        Assert.Contains("Nothing was stopped or started.", envelope.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A ledger this build cannot read was taken for no update at all, so the start ran whatever was at the install path,
    /// which may be the new build after the swap. It fails closed now, naming the file and the way on when no update is
    /// wanted, by each verb that reads it.
    /// </summary>
    [Fact]
    public async Task Runtime_start_refuses_a_ledger_it_cannot_read_and_names_the_file()
    {
        using var dir = new TempPaths();
        var ledger = Unreadable(dir);
        var processes = new FakeProcessControl { OnLaunch = _ => throw new InvalidOperationException("an unreadable ledger must not be started around") };
        var (env, output, _) = RuntimeVerbs.Environment(dir, RuntimeVerbs.NeverCalled(), processes);

        var exit = await RuntimeStartCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Empty(processes.Launches);
        var envelope = RuntimeVerbs.Envelope(output.ToString());
        Assert.Equal(UpdateLedgerException.Invalid, envelope.Code);
        Assert.Contains(ledger, envelope.Message, StringComparison.Ordinal);
        Assert.Contains("removing it", envelope.Message, StringComparison.Ordinal);
        Assert.Contains("Nothing was started.", envelope.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Runtime_restart_refuses_a_ledger_it_cannot_read_and_stops_nothing()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_OLD"));
        var ledger = Unreadable(dir);
        var (env, output, _) = RuntimeVerbs.Environment(dir, RuntimeVerbs.NeverCalled(), new FakeProcessControl());

        var exit = await RuntimeRestartCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.ApiError, exit);
        var envelope = RuntimeVerbs.Envelope(output.ToString());
        Assert.Equal(UpdateLedgerException.Invalid, envelope.Code);
        Assert.Contains(ledger, envelope.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_fails_its_runtime_check_on_a_ledger_it_cannot_read_and_names_the_file()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        StatusCommandTests.Running(dir);
        var ledger = Unreadable(dir);

        var exit = await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), Ct);

        var report = StatusCommandTests.Read(output);
        Assert.False(report.Ready);
        Assert.Equal(ExitCodes.ApiError, exit);
        var runtime = Assert.Single(report.Checks, check => check.Name == "runtime");
        Assert.Equal(CheckState.Failed, runtime.State);
        Assert.Contains(ledger, runtime.Fact, StringComparison.Ordinal);
        Assert.Contains("removing it", runtime.Fact, StringComparison.Ordinal);
    }

    /// <summary>
    /// A ledger the account cannot open — another program holding it — let the read's error out of status, which then
    /// printed nothing and no ready. It is the same unreadable ledger, said with the body.
    /// </summary>
    [Fact]
    public async Task Status_still_answers_with_its_body_when_the_ledger_cannot_be_opened()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        StatusCommandTests.Running(dir);
        var path = new UpdatePaths(dir.Paths).Ledger;
        Ledger(dir, UpdateStep.Staged).Write(path);

        int exit;
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            exit = await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), Ct);
        }

        var report = StatusCommandTests.Read(output);
        Assert.False(report.Ready);
        Assert.Equal(ExitCodes.ApiError, exit);
        var runtime = Assert.Single(report.Checks, check => check.Name == "runtime");
        Assert.Equal(CheckState.Failed, runtime.State);
        Assert.Contains(path, runtime.Fact, StringComparison.Ordinal);
    }

    /// <summary>The update's own verbs name the file too: their refusal was a fragment, "the ledger is not JSON."</summary>
    [Theory]
    [InlineData("status")]
    [InlineData("apply")]
    public async Task The_update_verbs_name_the_ledger_they_cannot_read(string verb)
    {
        using var dir = new TempPaths();
        var ledger = Unreadable(dir);
        var (env, output, _) = RuntimeVerbs.Environment(dir, RuntimeVerbs.NeverCalled(), new FakeProcessControl());
        var installed = Path.Combine(dir.Paths.Root, "install", "jason");
        Directory.CreateDirectory(Path.GetDirectoryName(installed)!);
        File.WriteAllText(installed, "jason 0.1.0");

        var exit = await CliApp.RunAsync(["update", verb], env with { InstallPath = installed }, Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var envelope = RuntimeVerbs.Envelope(output.ToString());
        Assert.Equal(UpdateLedgerException.Invalid, envelope.Code);
        Assert.Contains(ledger, envelope.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("start")]
    [InlineData("restart")]
    public async Task The_runtime_verbs_help_says_they_start_nothing_around_an_unfinished_update(string verb)
    {
        using var dir = new TempPaths();
        var output = new StringWriter();

        await CliApp.RunAsync(["runtime", verb, "--help"], new CliEnvironment(output, new StringWriter(), dir.Paths), Ct);

        Assert.Contains("an update stopped part-way", System.Text.RegularExpressions.Regex.Replace(output.ToString(), @"\s+", " "), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Runtime_start_after_a_completed_update_starts_as_ever()
    {
        using var dir = new TempPaths();
        Ledger(dir, UpdateStep.Complete).Write(new UpdatePaths(dir.Paths).Ledger);
        var processes = new FakeProcessControl { OnLaunch = RuntimeVerbs.PublishesAfterAWhile(dir, RuntimeVerbs.Descriptor("rt_NEW")) };
        var (env, output, _) = RuntimeVerbs.Environment(dir, RuntimeVerbs.EchoesTheDescriptor(dir), processes);

        var exit = await RuntimeStartCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Single(processes.Launches);
        Assert.Contains("rt_NEW", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>A file at the ledger's path that is not a ledger, and its path.</summary>
    private static string Unreadable(TempPaths dir)
    {
        var path = new UpdatePaths(dir.Paths).Ledger;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");
        return path;
    }

    private static UpdateLedger Ledger(TempPaths dir, UpdateStep step)
    {
        var update = new UpdatePaths(dir.Paths);
        var to = SemanticVersion.Parse("9.9.9");
        return new UpdateLedger(
            SemanticVersion.Parse("0.1.0"),
            to,
            step,
            DateTimeOffset.Parse("2026-09-19T08:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            Path.Combine(dir.Paths.Root, "install", "jason"),
            update.StagedExecutable(to),
            update.PreviousExecutable);
    }
}
