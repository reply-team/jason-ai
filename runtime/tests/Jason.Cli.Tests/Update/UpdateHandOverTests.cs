using System.ComponentModel;
using System.Text.Json;
using Jason.Cli.Commands;
using Jason.Cli.Process;
using Jason.Cli.Update;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Contracts.Update;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Update;

/// <summary>
/// An update and a rollback survive the rename of the file they are running as: what they still need is loaded before
/// the first move, and an apply that put the new executable in place hands the rest of the update to it.
/// </summary>
/// <remarks>
/// <c>jason update apply</c> runs from the install path, and a single-file executable reads its assemblies out of its own
/// file as it first needs them. After <c>swapped</c> that path holds another build, so a first load there reads the wrong
/// file; on Linux, with another .NET patch on the two sides, the apply failed as it started the new runtime. The failure
/// itself needs a published executable and the update job; these rows hold what prevents it.
/// </remarks>
public class UpdateHandOverTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The warm-up runs once, at <c>kept</c>, while the install path still holds the executable this update replaces:
    /// before the first rename, after which the file at this process's own path may be another.
    /// </summary>
    [Fact]
    public async Task An_update_warms_up_before_it_moves_the_installed_executable()
    {
        using var installation = new FakeInstallation();
        var witnessed = new List<(string Installed, UpdateStep? Step)>();
        var applier = new UpdateApplier(installation.Env, installation.Update, TimeProvider.System, installation.InstallPath)
        {
            WarmUp = () => witnessed.Add((installation.Installed(), installation.Ledger()?.Step)),
        };

        await applier.ApplyAsync(Request(installation), Ct);

        Assert.Equal([(installation.From.ToString(), (UpdateStep?)UpdateStep.Kept)], witnessed);
    }

    /// <summary>
    /// The rollback's mirror: typed from the PATH it is the executable it moves aside, so it warms up while that file is
    /// still the one installed — here with no runtime running, where nothing else would have loaded what its start needs.
    /// </summary>
    [Fact]
    public async Task A_rollback_warms_up_before_it_moves_the_installed_executable()
    {
        using var installation = new FakeInstallation();
        await installation.Applier().ApplyAsync(Request(installation), Ct);
        installation.RuntimeGoesAway();
        var witnessed = new List<string>();
        var rollback = new UpdateRollback(installation.Env, installation.Update, TimeProvider.System)
        {
            WarmUp = () => witnessed.Add(installation.Installed()),
        };

        await rollback.RollBackAsync(Ct);

        Assert.Equal([installation.To.ToString()], witnessed);
        Assert.Equal(installation.From.ToString(), installation.Installed());
    }

    /// <summary>
    /// A fresh apply takes the steps up to <c>swapped</c> itself and then runs <c>jason update apply</c> from the install
    /// path, against the same data directory and feed: the runtime is started by the executable it was handed to, and the
    /// answer is one document with every step in order.
    /// </summary>
    [Fact]
    public async Task An_apply_hands_the_rest_to_the_executable_it_put_in_place()
    {
        using var installation = new FakeInstallation();
        var feed = installation.Feed.AbsoluteUri;

        var exit = await UpdateCommands.ApplyAsync(installation.Env, feed, null, 0, human: false, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var (data, command) = Assert.Single(installation.Processes.Runs);
        Assert.Equal(installation.Paths.Root, data);
        Assert.Equal([installation.InstallPath, "update", "apply", "--feed", feed], command);

        // Started by the executable the update was handed to, and by nothing before it.
        Assert.Equal([true], installation.StartsHandedOver);
        Assert.Equal(installation.To.ToString(), installation.Installed());
        Assert.Equal(UpdateStep.Complete, installation.Ledger()!.Step);

        var answer = JsonSerializer.Deserialize<UpdateApplyResponse>(installation.Out.ToString(), JasonJson.Options)!;
        Assert.Equal("complete", answer.Step);
        Assert.Equal(
            ["staged", "drained", "stopped", "kept", "swapped", "plugins", "started", "healthy", "complete", "skills", "ui"],
            answer.Steps.Select(step => step[..step.IndexOf(':', StringComparison.Ordinal)]));
    }

    /// <summary>
    /// The executable it is handed to finds the new build already in place and carries on itself, and so does a person
    /// who resumes an update past <c>swapped</c>: only a run that moved the file hands anything over.
    /// </summary>
    [Fact]
    public async Task An_apply_that_finds_the_new_executable_in_place_carries_on_itself()
    {
        using var installation = new FakeInstallation();
        installation.Killed(UpdateStep.Swapped);

        var exit = await UpdateCommands.ApplyAsync(installation.Env, installation.Feed.AbsoluteUri, null, 0, human: false, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Empty(installation.Processes.Runs);
        Assert.Equal([false], installation.StartsHandedOver);
        Assert.Equal(UpdateStep.Complete, installation.Ledger()!.Step);
    }

    /// <summary>The applier driven on its own takes every step in its own process, which is what every other test of it reads.</summary>
    [Fact]
    public async Task An_applier_not_asked_to_hand_over_takes_every_step_itself()
    {
        using var installation = new FakeInstallation();

        var ledger = await installation.Applier().ApplyAsync(Request(installation), Ct);

        Assert.Equal(UpdateStep.Complete, ledger.Step);
        Assert.Empty(installation.Processes.Runs);
        Assert.Equal([false], installation.StartsHandedOver);
    }

    /// <summary>
    /// What the executable it was handed to refused is the answer, as it said it and with its exit code: the refusal names
    /// the update's own code and remedy, and nothing is put in front of it.
    /// </summary>
    [Fact]
    public async Task A_refusal_from_the_executable_handed_to_is_passed_on_as_it_came()
    {
        using var installation = new FakeInstallation { ServesVersion = "0.0.1" };

        var exit = await UpdateCommands.ApplyAsync(installation.Env, installation.Feed.AbsoluteUri, null, 0, human: false, Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var refusal = JsonSerializer.Deserialize<ErrorResponse>(installation.Out.ToString(), JasonJson.Options)!;
        Assert.Equal(UpdateCodes.NotHealthy, refusal.Error.Code);
        Assert.Single(installation.Processes.Runs);
        Assert.Equal(UpdateStep.Healthy, installation.Ledger()!.Step);
    }

    /// <summary>
    /// An executable that cannot be run at all leaves the new build in place and the ledger at <c>swapped</c>, and the
    /// refusal names that step and says that typing <c>jason update apply</c> — which runs that build — finishes it. Each
    /// way a start can fail is one of these.
    /// </summary>
    [Theory]
    [InlineData("win32")]
    [InlineData("io")]
    [InlineData("invalid")]
    public async Task An_executable_that_cannot_be_run_leaves_the_update_at_swapped_and_says_how_to_finish(string failure)
    {
        using var installation = new FakeInstallation();
        Exception thrown = failure switch
        {
            "win32" => new Win32Exception(8, "Exec format error"),
            "io" => new IOException("The runtime executable could not be started."),
            _ => new InvalidOperationException("No process is associated with this object."),
        };
        installation.Processes.OnRun = (_, _, _, _) => throw thrown;

        var exit = await UpdateCommands.ApplyAsync(installation.Env, installation.Feed.AbsoluteUri, null, 0, human: false, Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var refusal = JsonSerializer.Deserialize<ErrorResponse>(installation.Out.ToString(), JasonJson.Options)!;
        Assert.Equal(UpdateCodes.NotHealthy, refusal.Error.Code);
        Assert.Contains(thrown.Message, refusal.Error.Message, StringComparison.Ordinal);
        AssertBothWaysOn(installation, refusal.Error.Message, "swapped");
        Assert.Equal(UpdateStep.Swapped, installation.Ledger()!.Step);
        Assert.Equal(installation.To.ToString(), installation.Installed());
        Assert.Empty(installation.StartsHandedOver);
    }

    /// <summary>
    /// An executable handed the update that ends outside what this command answers — killed, crashed, a usage exit with
    /// nothing on stdout, or a success whose document was cut short — is no answer to pass on: its exit code is none this
    /// command gives and its output no document. It is refused as one that could not be run is, with the step the
    /// ledger stands at.
    /// </summary>
    [Theory]
    [InlineData(137, "")]
    [InlineData(134, "{\"from\":\"1.0.0\",\"to\":\"9.9.9\",\"step\":\"complete\",\"steps\":[]}")]
    [InlineData(-532462766, "")]
    [InlineData(2, "")]
    [InlineData(0, "{\"from\":\"1.0.0\",\"to\":\"9.9.9\",\"step\":\"comp")]
    [InlineData(0, "")]
    public async Task An_executable_that_ends_outside_the_contract_is_refused_with_the_step_it_left(int exitCode, string printed)
    {
        using var installation = new FakeInstallation();
        installation.Processes.OnRun = (_, _, _, _) => Task.FromResult(new FinishedCommand(exitCode, printed));

        var exit = await UpdateCommands.ApplyAsync(installation.Env, installation.Feed.AbsoluteUri, null, 0, human: false, Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var refusal = JsonSerializer.Deserialize<ErrorResponse>(installation.Out.ToString(), JasonJson.Options)!;
        Assert.Equal(UpdateCodes.NotHealthy, refusal.Error.Code);
        Assert.Contains($"exit code {exitCode}", refusal.Error.Message, StringComparison.Ordinal);
        AssertBothWaysOn(installation, refusal.Error.Message, "swapped");
    }

    /// <summary>
    /// One that ends as this command ends — an exit code it gives, and one JSON document — is passed on as it came, with
    /// that exit code, whichever of them it is.
    /// </summary>
    [Fact]
    public async Task An_answer_inside_the_contract_is_passed_on_with_its_exit_code()
    {
        using var installation = new FakeInstallation();
        const string Printed = "{\"error\":{\"code\":\"runtime_unreachable\",\"message\":\"nothing answered\",\"retryable\":true}}\n";
        installation.Processes.OnRun = (_, _, _, _) => Task.FromResult(new FinishedCommand(ExitCodes.RuntimeUnavailable, Printed));

        var exit = await UpdateCommands.ApplyAsync(installation.Env, installation.Feed.AbsoluteUri, null, 0, human: false, Ct);

        Assert.Equal(ExitCodes.RuntimeUnavailable, exit);
        Assert.Equal(Printed, installation.Out.ToString());
    }

    private static void AssertBothWaysOn(FakeInstallation installation, string message, string step)
    {
        Assert.Contains($"The update stands at '{step}'.", message, StringComparison.Ordinal);
        Assert.Contains("`jason update apply`", message, StringComparison.Ordinal);
        Assert.Contains($"`jason update rollback` to put {installation.From} back", message, StringComparison.Ordinal);
    }

    /// <summary>For a person, the same steps, one line each and in the same order, whoever took them.</summary>
    [Fact]
    public async Task A_person_reads_every_step_whoever_took_it()
    {
        using var installation = new FakeInstallation();

        var exit = await UpdateCommands.ApplyAsync(installation.Env, installation.Feed.AbsoluteUri, null, 0, human: true, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var lines = installation.Out.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.All(lines, line => Assert.StartsWith("- ", line, StringComparison.Ordinal));
        Assert.Equal(
            ["staged", "drained", "stopped", "kept", "swapped", "plugins", "started", "healthy", "complete", "skills", "ui"],
            lines.Select(line => line[2..line.IndexOf(':', StringComparison.Ordinal)]));
    }

    /// <summary>
    /// The steps taken here go in front of the ones in the document, and a field this build does not know is carried; what
    /// is not such a document is not touched.
    /// </summary>
    [Fact]
    public void The_steps_taken_here_go_in_front_and_nothing_else_changes()
    {
        var answer = UpdateCommands.WithStepsBefore(
            ["staged: 9.9.9", "swapped: 9.9.9 is at the install path"],
            """{"from":"1.0.0","to":"9.9.9","step":"complete","steps":["started: 9.9.9"],"later":{"kept":true}}""");

        Assert.NotNull(answer);
        Assert.Equal(
            ["staged: 9.9.9", "swapped: 9.9.9 is at the install path", "started: 9.9.9"],
            answer["steps"]!.AsArray().Select(step => step!.GetValue<string>()));
        Assert.True(answer["later"]!["kept"]!.GetValue<bool>());

        Assert.Null(UpdateCommands.WithStepsBefore(["staged: 9.9.9"], """{"error":{"code":"update_not_healthy","message":"no","retryable":false}}"""));
        Assert.Null(UpdateCommands.WithStepsBefore(["staged: 9.9.9"], "- started: 9.9.9"));
        Assert.Null(UpdateCommands.WithStepsBefore(["staged: 9.9.9"], """{"steps":[1]}"""));
    }

    /// <summary>
    /// The real door: a command run to its end against a data directory, its exit code and standard output handed back and
    /// its standard error passed on. The platform's own shell stands in for the executable: it is on every machine.
    /// </summary>
    [Fact]
    public async Task A_command_run_to_its_end_hands_back_its_exit_code_and_output_and_passes_its_error_on()
    {
        using var tree = new TempTree();
        var paths = new JasonPaths(Path.Combine(tree.Root, "data"));
        var error = new StringWriter();
        string[] command = OperatingSystem.IsWindows()
            ? [Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/d", "/c", "echo %JASON_DATA_DIR%&echo said on the side 1>&2&exit /b 3"]
            : ["/bin/sh", "-c", "echo \"$JASON_DATA_DIR\"; echo said on the side >&2; exit 3"];

        var finished = await RuntimeProcessControl.Instance.RunAsync(paths, command, error, Ct);

        Assert.Equal(3, finished.ExitCode);
        Assert.Equal(paths.Root, finished.StandardOutput.Trim());
        Assert.Equal("said on the side", error.ToString().Trim());
    }

    private static UpdateRequest Request(FakeInstallation installation) => new(installation.Feed, null, TimeSpan.Zero);
}
