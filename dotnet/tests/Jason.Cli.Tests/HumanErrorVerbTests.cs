using System.Net;
using Jason.Cli;
using Jason.Cli.Autostart;
using Jason.Cli.Commands;
using Jason.Cli.Tests.Autostart;
using Jason.Cli.Tests.Commands;
using Jason.Cli.Tests.Process;
using Jason.Cli.Tests.Status;
using Jason.Cli.Tests.Update;
using Jason.Cli.Update;
using Jason.Contracts.Discovery;
using Jason.Contracts.Update;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests;

/// <summary>
/// The verbs that print their own refusals, each run twice on the same machine: without <c>--human</c> the one line is the
/// envelope it always was, with the code it always had and the same exit; with it, the line is that envelope's sentence.
/// </summary>
public sealed class HumanErrorVerbTests : IDisposable
{
    private const string NotFound = "{\"error\":{\"code\":\"approval_not_found\",\"message\":\"The approval apr_X was not found.\",\"retryable\":false}}";

    private readonly TempPaths _dir = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task Update_status_on_a_file_that_is_not_a_ledger()
    {
        var ledger = new UpdatePaths(_dir.Paths).Ledger;
        Directory.CreateDirectory(Path.GetDirectoryName(ledger)!);
        await File.WriteAllTextAsync(ledger, "{ not json", Ct);

        await BothAsync(UpdateLedgerException.Invalid, ExitCodes.ApiError, human => Cli(RuntimeVerbs.NeverCalled(), human, "update", "status"));
    }

    [Fact]
    public async Task Update_check_whose_feed_cannot_be_reached()
    {
        await BothAsync("update_feed_unreachable", ExitCodes.ApiError, human => Cli(new FakeHandler(_ => throw new HttpRequestException("connection refused")), human, "update", "check"));
    }

    [Theory]
    [InlineData("apply")]
    [InlineData("rollback")]
    public async Task Update_apply_and_rollback_on_a_file_that_is_not_a_ledger(string verb)
    {
        using var installation = new FakeInstallation().WithRuntime();
        Directory.CreateDirectory(installation.Update.Root);
        File.WriteAllText(installation.Update.Ledger, "half a fi");
        string[] words = verb == "apply" ? ["update", "apply", "--feed", installation.Feed.ToString()] : ["update", "rollback"];

        await BothAsync(UpdateLedgerException.Invalid, ExitCodes.ApiError, async human =>
        {
            installation.Out.GetStringBuilder().Clear();
            var exit = await CliApp.RunAsync(human ? [.. words, "--human"] : words, installation.Env, Ct);
            return (exit, installation.Out.ToString());
        });
    }

    [Fact]
    public async Task Runtime_start_and_restart_while_an_update_is_in_flight_from_the_swap_on()
    {
        UpdateInFlightCheckTests.Ledger(_dir, UpdateStep.Swapped).Write(new UpdatePaths(_dir.Paths).Ledger);
        var processes = new FakeProcessControl { OnLaunch = _ => throw new InvalidOperationException("an update in flight must not be started around") };

        await BothAsync(UpdateCodes.InProgress, ExitCodes.ApiError, human => Run(RuntimeVerbs.NeverCalled(), processes, env => RuntimeStartCommand.RunAsync(env, human, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll)));
        await BothAsync(UpdateCodes.InProgress, ExitCodes.ApiError, human => Run(RuntimeVerbs.NeverCalled(), processes, env => RuntimeRestartCommand.RunAsync(env, human, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll)));
        Assert.Empty(processes.Launches);
    }

    [Fact]
    public async Task Runtime_start_whose_runtime_exits_before_it_publishes()
    {
        var processes = new FakeProcessControl { OnLaunch = RuntimeVerbs.DiesWith(exitCode: 1) };

        await BothAsync(CliErrors.RuntimeStartFailed, ExitCodes.ApiError, human => Run(RuntimeVerbs.NeverCalled(), processes, env => RuntimeStartCommand.RunAsync(env, human, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll)));
    }

    [Fact]
    public async Task Runtime_autostart_that_the_machine_refuses()
    {
        var registrar = new RecordingRegistrar { Refuses = new AutostartException(AutostartCodes.Refused, "schtasks refused: ERROR: Access is denied.") };

        await BothAsync(AutostartCodes.Refused, ExitCodes.ApiError, async human =>
        {
            var output = new StringWriter();
            var env = new CliEnvironment(output, new StringWriter(), _dir.Paths, RuntimeVerbs.NeverCalled(), Autostart: registrar);
            return (await CliApp.RunAsync(human ? ["runtime", "autostart", "status", "--human"] : ["runtime", "autostart", "status"], env, Ct), output.ToString());
        });
    }

    [Fact]
    public async Task Ui_open_refused_by_the_runtime()
    {
        _dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        const string refused = "{\"error\":{\"code\":\"ui_not_installed\",\"message\":\"No UI is installed.\",\"retryable\":false}}";

        await BothAsync("ui_not_installed", ExitCodes.ApiError, human => Cli(new FakeHandler(_ => RuntimeVerbs.Response(HttpStatusCode.Conflict, refused)), human, "ui", "open", "--no-open"));
    }

    [Fact]
    public async Task A_channel_route_read_asked_of_a_runtime_older_than_channel_routes()
    {
        _dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        var handler = new FakeHandler(_ => RuntimeVerbs.Response(HttpStatusCode.OK, RuntimeVerbs.InfoJson("rt_LIVE")));

        await BothAsync(CliErrors.RuntimeTooOld, ExitCodes.ApiError, human => Cli(handler, human, "route", "resolve", "--campaign", "cmp_A", "--operation", "message.send", "--channel", "linkedin"));
    }

    [Fact]
    public async Task Session_open_on_an_item_the_runtime_does_not_find()
    {
        _dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));

        await BothAsync("approval_not_found", ExitCodes.ApiError, human => Cli(new FakeHandler(_ => RuntimeVerbs.Response(HttpStatusCode.NotFound, NotFound)), human, "session", "open", "--approval", "apr_X"));
    }

    [Fact]
    public async Task Session_open_setup_with_no_role_skills_folder_keeps_its_sentence_on_stderr()
    {
        var errors = new List<string>();
        await BothAsync(CliErrors.FolderMissing, ExitCodes.ApiError, async human =>
        {
            var output = new StringWriter();
            var error = new StringWriter();
            var env = new CliEnvironment(output, error, _dir.Paths, RuntimeVerbs.NeverCalled());
            var exit = await CliApp.RunAsync(human ? ["session", "open", "--setup", "--human"] : ["session", "open", "--setup"], env, Ct);
            errors.Add(error.ToString());
            return (exit, output.ToString());
        });

        Assert.Equal(errors[0], errors[1]);
        Assert.Contains("role skills folder", errors[0], StringComparison.Ordinal);
    }

    /// <summary>The run without <c>--human</c> first, then with it, on the same machine.</summary>
    private static async Task BothAsync(string code, int exitCode, Func<bool, Task<(int Exit, string Out)>> run)
    {
        var plain = await run(false);
        var human = await run(true);

        Assert.Equal(exitCode, plain.Exit);
        Assert.Equal(exitCode, human.Exit);
        var envelope = plain.Out.TrimEnd();
        Assert.Equal(envelope + Environment.NewLine, plain.Out);
        Assert.Equal(code, RuntimeVerbs.Envelope(envelope).Code);
        Assert.StartsWith($"Refused ({code}): ", human.Out, StringComparison.Ordinal);
        Assert.Equal(CliErrors.Sentence(envelope) + Environment.NewLine, human.Out);
    }

    private async Task<(int Exit, string Out)> Cli(HttpMessageHandler handler, bool human, params string[] words)
    {
        var output = new StringWriter();
        var env = new CliEnvironment(output, new StringWriter(), _dir.Paths, handler, null, new FakeProcessControl(), Autostart: new RecordingRegistrar());
        var exit = await CliApp.RunAsync(human ? [.. words, "--human"] : words, env, Ct);
        return (exit, output.ToString());
    }

    private async Task<(int Exit, string Out)> Run(HttpMessageHandler handler, FakeProcessControl processes, Func<CliEnvironment, Task<int>> verb)
    {
        var (env, output, _) = RuntimeVerbs.Environment(_dir, handler, processes);
        return (await verb(env), output.ToString());
    }
}
