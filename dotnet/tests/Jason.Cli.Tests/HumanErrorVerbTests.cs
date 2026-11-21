using System.Net;
using Jason.Cli;
using Jason.Cli.Autostart;
using Jason.Cli.Commands;
using Jason.Cli.Tests.Autostart;
using Jason.Cli.Tests.Commands;
using Jason.Cli.Tests.Installation;
using Jason.Cli.Tests.Process;
using Jason.Cli.Tests.Status;
using Jason.Cli.Tests.Update;
using Jason.Cli.Update;
using Jason.Contracts.Discovery;
using Jason.Contracts.Update;
using Jason.Runtime.Tests;
using static Jason.Cli.Tests.HumanErrorPins;

namespace Jason.Cli.Tests;

/// <summary>
/// The verbs that print their own refusals, each run twice on the same machine: without <c>--human</c> the one line is the
/// envelope it always was, with the same exit; with it, the line is that envelope's sentence.
/// </summary>
public sealed class HumanErrorVerbTests : IDisposable
{
    private readonly TempPaths _dir = new();

    private readonly ManagedLayouts _layouts = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _layouts.Dispose();
        _dir.Dispose();
    }

    // ---- update: one row for each kind of refusal the verbs print ----

    [Fact]
    public async Task Update_status_on_a_file_that_is_not_a_ledger()
    {
        var ledger = new UpdatePaths(_dir.Paths).Ledger;
        Directory.CreateDirectory(Path.GetDirectoryName(ledger)!);
        await File.WriteAllTextAsync(ledger, "{ not json", Ct);

        await BothAsync(
            human => Cli(RuntimeVerbs.NeverCalled(), human, "update", "status"),
            ExitCodes.ApiError,
            "{\"error\":{\"code\":\"update_ledger_invalid\",\"message\":\"The update ledger at \\u0027" + Json(ledger) + "\\u0027 is not one this build can act on: the ledger is not JSON.\",\"retryable\":false}}",
            "Refused (update_ledger_invalid): The update ledger at '" + ledger + "' is not one this build can act on: the ledger is not JSON.");
    }

    [Fact]
    public async Task Update_check_whose_feed_cannot_be_reached()
    {
        await BothAsync(
            human => Cli(new FakeHandler(_ => throw new HttpRequestException("connection refused")), human, "update", "check"),
            ExitCodes.ApiError,
            "{\"error\":{\"code\":\"update_feed_unreachable\",\"message\":\"The update feed at https://get.jasonharness.ai/releases/latest/download/manifest.json could not be reached.\",\"retryable\":true}}",
            "Refused (update_feed_unreachable): The update feed at https://get.jasonharness.ai/releases/latest/download/manifest.json could not be reached. It can be tried again.");
    }

    [Theory]
    [InlineData("apply")]
    [InlineData("rollback")]
    public async Task Update_apply_and_rollback_on_a_file_that_is_not_a_ledger(string verb)
    {
        using var installation = new FakeInstallation().WithRuntime();
        Directory.CreateDirectory(installation.Update.Root);
        File.WriteAllText(installation.Update.Ledger, "half a fi");
        var ledger = installation.Update.Ledger;

        await BothAsync(
            human => Installed(installation, installation.Env, human, Words(installation, verb)),
            ExitCodes.ApiError,
            "{\"error\":{\"code\":\"update_ledger_invalid\",\"message\":\"The update ledger at \\u0027" + Json(ledger) + "\\u0027 is not one this build can act on: the ledger is not JSON.\",\"retryable\":false}}",
            "Refused (update_ledger_invalid): The update ledger at '" + ledger + "' is not one this build can act on: the ledger is not JSON.");
    }

    [Fact]
    public async Task Update_apply_with_nothing_newer()
    {
        using var installation = new FakeInstallation(to: SemanticVersion.Parse("0.0.0")).WithRuntime();
        var current = SemanticVersion.Current.ToString();

        await BothAsync(
            human => Installed(installation, installation.Env, human, Words(installation, "apply")),
            ExitCodes.ApiError,
            "{\"error\":{\"code\":\"update_up_to_date\",\"message\":\"This installation is " + current + " and the feed offers 0.0.0: there is nothing to apply.\",\"retryable\":false}}",
            "Refused (update_up_to_date): This installation is " + current + " and the feed offers 0.0.0: there is nothing to apply.");
    }

    [Fact]
    public async Task Update_rollback_with_no_update_to_put_back()
    {
        using var installation = new FakeInstallation().WithRuntime();
        var ledger = installation.Update.Ledger;

        await BothAsync(
            human => Installed(installation, installation.Env, human, Words(installation, "rollback")),
            ExitCodes.ApiError,
            "{\"error\":{\"code\":\"update_nothing_to_roll_back\",\"message\":\"There is no record of an update at " + Json(ledger) + ", so there is nothing to put back.\",\"retryable\":false}}",
            "Refused (update_nothing_to_roll_back): There is no record of an update at " + ledger + ", so there is nothing to put back.");
    }

    [Theory]
    [InlineData("apply")]
    [InlineData("rollback")]
    public async Task Update_apply_and_rollback_of_an_installation_a_package_manager_owns(string verb)
    {
        using var installation = new FakeInstallation().WithRuntime();
        var winget = _layouts.Winget();
        var env = installation.Env with { InstallPath = winget.Executable, Managers = new PlantedRecords(winget.Registration()) };

        await BothAsync(
            human => Installed(installation, env, human, Words(installation, verb)),
            ExitCodes.ApiError,
            "{\"error\":{\"code\":\"update_managed_by_package_manager\",\"message\":\"This Jason was installed by winget, which keeps its own record of the version, so \\u0060jason update " + verb
            + "\\u0060 would replace a file the package manager owns. Its version is changed through winget, with the runtime stopped first and started after:\\n  jason runtime stop\\n  winget upgrade Reply.Jason\\n  jason runtime start\",\"retryable\":false}}",
            "Refused (update_managed_by_package_manager): This Jason was installed by winget, which keeps its own record of the version, so `jason update " + verb
            + "` would replace a file the package manager owns. Its version is changed through winget, with the runtime stopped first and started after:\n  jason runtime stop\n  winget upgrade Reply.Jason\n  jason runtime start");
    }

    // ---- runtime start, restart and autostart ----

    [Fact]
    public async Task Runtime_start_and_restart_while_an_update_is_in_flight_from_the_swap_on()
    {
        UpdateInFlightCheckTests.Ledger(_dir, UpdateStep.Swapped).Write(new UpdatePaths(_dir.Paths).Ledger);
        var processes = new FakeProcessControl { OnLaunch = _ => throw new InvalidOperationException("an update in flight must not be started around") };

        const string inFlight = "An update from 0.1.0 to 9.9.9 stopped at \\u0027swapped\\u0027 and is not finished. \\u0060jason update apply\\u0060 finishes it and starts the runtime as part of it, and \\u0060jason update rollback\\u0060 goes back to 0.1.0.";
        const string said = "An update from 0.1.0 to 9.9.9 stopped at 'swapped' and is not finished. `jason update apply` finishes it and starts the runtime as part of it, and `jason update rollback` goes back to 0.1.0.";

        await BothAsync(
            human => Run(RuntimeVerbs.NeverCalled(), processes, env => RuntimeStartCommand.RunAsync(env, human, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll)),
            ExitCodes.ApiError,
            "{\"error\":{\"code\":\"update_in_progress\",\"message\":\"" + inFlight + " Nothing was started.\",\"retryable\":false}}",
            "Refused (update_in_progress): " + said + " Nothing was started.");
        await BothAsync(
            human => Run(RuntimeVerbs.NeverCalled(), processes, env => RuntimeRestartCommand.RunAsync(env, human, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll)),
            ExitCodes.ApiError,
            "{\"error\":{\"code\":\"update_in_progress\",\"message\":\"" + inFlight + " Nothing was stopped or started.\",\"retryable\":false}}",
            "Refused (update_in_progress): " + said + " Nothing was stopped or started.");
        Assert.Empty(processes.Launches);
    }

    [Fact]
    public async Task Runtime_start_whose_runtime_exits_before_it_publishes()
    {
        var processes = new FakeProcessControl { OnLaunch = RuntimeVerbs.DiesWith(exitCode: 1) };
        var paths = _dir.Paths;
        string Failed(Func<string, string> path, string quote, string dash) =>
            "The runtime process exited with code 1 before publishing an endpoint descriptor. There are no log files under " + quote + path(paths.LogsDirectory) + quote
            + ", so it stopped before logging started " + dash + " which is what happens when its settings file at " + quote + path(paths.UserSettingsFile) + quote
            + " cannot be read, or the data directory at " + quote + path(paths.Root) + quote + " cannot be prepared.";

        await BothAsync(
            human => Run(RuntimeVerbs.NeverCalled(), processes, env => RuntimeStartCommand.RunAsync(env, human, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll)),
            ExitCodes.ApiError,
            "{\"error\":{\"code\":\"runtime_start_failed\",\"message\":\"" + Failed(Json, "\\u0027", "\\u2014") + "\",\"retryable\":false}}",
            "Refused (runtime_start_failed): " + Failed(text => text, "'", "—"));
    }

    [Fact]
    public async Task Runtime_start_whose_runtime_refuses_a_database_a_newer_jason_migrated()
    {
        var envelope = CliErrors.Serialize(DataNewer.Code, DataNewer.Sentence("0.49.0", _dir.Paths.Root), retryable: false);
        var processes = new FakeProcessControl { OnLaunch = _ => new FakeProcessHandle(4242) { HasExited = true, ExitCode = 1, Said = envelope } };

        await BothAsync(
            human => Run(RuntimeVerbs.EchoesTheDescriptor(_dir), processes, env => RuntimeStartCommand.RunAsync(env, human, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll)),
            ExitCodes.ApiError,
            "{\"error\":{\"code\":\"data_newer\",\"message\":\"This Jason is 0.49.0, and the data in " + Json(_dir.Paths.Root)
            + " was changed by a newer Jason, so the runtime was not started and nothing was changed. Update Jason with \\u0060jason update apply\\u0060, or Check for Updates in Jason Desktop.\",\"retryable\":false}}",
            "Refused (data_newer): This Jason is 0.49.0, and the data in " + _dir.Paths.Root
            + " was changed by a newer Jason, so the runtime was not started and nothing was changed. Update Jason with `jason update apply`, or Check for Updates in Jason Desktop.");
    }

    [Theory]
    [InlineData("enable")]
    [InlineData("disable")]
    [InlineData("status")]
    public async Task Runtime_autostart_that_the_machine_refuses(string verb)
    {
        var registrar = new RecordingRegistrar { Refuses = new AutostartException(AutostartCodes.Refused, "schtasks refused: ERROR: Access is denied.") };

        await BothAsync(
            async human =>
            {
                var output = new StringWriter();
                var env = new CliEnvironment(output, new StringWriter(), _dir.Paths, RuntimeVerbs.NeverCalled(), Autostart: registrar);
                return (await CliApp.RunAsync(human ? ["runtime", "autostart", verb, "--human"] : ["runtime", "autostart", verb], env, Ct), output.ToString());
            },
            ExitCodes.ApiError,
            "{\"error\":{\"code\":\"autostart_refused\",\"message\":\"schtasks refused: ERROR: Access is denied.\",\"retryable\":false}}",
            "Refused (autostart_refused): schtasks refused: ERROR: Access is denied.");
    }

    // ---- ui open ----

    [Fact]
    public async Task Ui_open_refused_by_the_runtime()
    {
        _dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        const string refused = "{\"error\":{\"code\":\"ui_not_installed\",\"message\":\"No UI is installed.\",\"retryable\":false}}";

        await BothAsync(
            human => Cli(new FakeHandler(_ => RuntimeVerbs.Response(HttpStatusCode.Conflict, refused)), human, "ui", "open", "--no-open"),
            ExitCodes.ApiError,
            refused,
            "Refused (ui_not_installed): No UI is installed.");
    }

    // ---- a channel route asked of a runtime older than channel routes ----

    public static TheoryData<string[]> ChannelVerbs => new()
    {
        new[] { "route", "resolve", "--campaign", "cmp_A", "--operation", "message.send", "--channel", "linkedin" },
        new[] { "--actor", "human:ada", "route", "set", "--campaign", "cmp_A", "--operation", "message.send", "--channel", "linkedin", "--plugin", "social" },
        new[] { "--actor", "human:ada", "route", "unset", "--campaign", "cmp_A", "--operation", "message.send", "--channel", "linkedin" },
        new[] { "operation", "describe", "message.send", "--campaign", "cmp_A", "--channel", "linkedin" },
    };

    [Theory]
    [MemberData(nameof(ChannelVerbs))]
    public async Task A_channel_route_asked_of_a_runtime_older_than_channel_routes(string[] words)
    {
        _dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        var handler = new FakeHandler(_ => RuntimeVerbs.Response(HttpStatusCode.OK, RuntimeVerbs.InfoJson("rt_LIVE")));

        await BothAsync(
            human => Cli(handler, human, words),
            ExitCodes.ApiError,
            "{\"error\":{\"code\":\"runtime_too_old\",\"message\":\"The runtime that is running (0.1.0-dev) does not know channel routes, so it would ignore --channel and answer or change the operation\\u0027s own route instead. Nothing was sent. Restart the runtime if it was updated without a restart, or update Jason.\",\"retryable\":false}}",
            "Refused (runtime_too_old): The runtime that is running (0.1.0-dev) does not know channel routes, so it would ignore --channel and answer or change the operation's own route instead. Nothing was sent. Restart the runtime if it was updated without a restart, or update Jason.");
    }

    // ---- session open ----

    [Fact]
    public async Task Session_open_on_an_item_the_runtime_does_not_find()
    {
        const string notFound = "{\"error\":{\"code\":\"approval_not_found\",\"message\":\"The approval apr_X was not found.\",\"retryable\":false}}";
        _dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));

        await BothAsync(
            human => Cli(new FakeHandler(_ => RuntimeVerbs.Response(HttpStatusCode.NotFound, notFound)), human, "session", "open", "--approval", "apr_X"),
            ExitCodes.ApiError,
            notFound,
            "Refused (approval_not_found): The approval apr_X was not found.");
    }

    /// <summary>Without <c>--human</c> the envelope on stdout and the sentence on stderr, as before; with it, the sentence once, on stdout.</summary>
    [Fact]
    public async Task Session_open_setup_with_no_role_skills_folder()
    {
        var folder = Path.Combine(_dir.Paths.Root, "skills", "roles");
        var sentence = "The role skills folder " + folder + " is not there, so nothing was started; `jason skills install --roles-only` places the role skills there.";
        var errors = new List<string>();
        await BothAsync(
            async human =>
            {
                var output = new StringWriter();
                var error = new StringWriter();
                var env = new CliEnvironment(output, error, _dir.Paths, RuntimeVerbs.NeverCalled());
                var exit = await CliApp.RunAsync(human ? ["session", "open", "--setup", "--human"] : ["session", "open", "--setup"], env, Ct);
                errors.Add(error.ToString());
                return (exit, output.ToString());
            },
            ExitCodes.ApiError,
            "{\"error\":{\"code\":\"folder_missing\",\"message\":\"The role skills folder " + Json(folder) + " is not there, so nothing was started; \\u0060jason skills install --roles-only\\u0060 places the role skills there.\",\"retryable\":false}}",
            "Refused (folder_missing): " + sentence);

        Pin(sentence, errors[0]);
        Assert.Equal(string.Empty, errors[1]);
    }

    // ---- logs ----

    [Fact]
    public async Task Logs_whose_directory_cannot_be_read()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dir.Paths.LogsDirectory)!);
        await File.WriteAllTextAsync(_dir.Paths.LogsDirectory, "a file where the directory goes", Ct);

        var logs = _dir.Paths.LogsDirectory;

        await BothAsync(
            human => Cli(RuntimeVerbs.NeverCalled(), human, "logs"),
            ExitCodes.ApiError,
            "{\"error\":{\"code\":\"logs_unreadable\",\"message\":\"The log directory " + Json(logs) + " exists and could not be read: it is a file, not a directory.\",\"retryable\":false}}",
            "Refused (logs_unreadable): The log directory " + logs + " exists and could not be read: it is a file, not a directory.");
    }

    // ---- the machine ----

    /// <summary>A path as the envelope's JSON spells it.</summary>
    private static string Json(string path) => path.Replace("\\", "\\\\", StringComparison.Ordinal);

    private static string[] Words(FakeInstallation installation, string verb) =>
        verb == "apply" ? ["update", "apply", "--feed", installation.Feed.ToString(), "--drain-seconds", "0"] : ["update", "rollback"];

    private static async Task<(int Exit, string Out)> Installed(FakeInstallation installation, CliEnvironment env, bool human, string[] words)
    {
        installation.Out.GetStringBuilder().Clear();
        var exit = await CliApp.RunAsync(human ? [.. words, "--human"] : words, env, Ct);
        return (exit, installation.Out.ToString());
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
