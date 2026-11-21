using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using Jason.Cli.Autostart;
using Jason.Cli.Discovery;
using Jason.Cli.Installation;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;

namespace Jason.Cli.Commands;

/// <summary>
/// <c>jason runtime autostart</c>: register the runtime to start when this account logs on, take that
/// registration away, and say what is registered.
/// </summary>
/// <remarks>
/// <para>
/// What is registered is the runtime itself — <c>runtime run</c> — and never something that starts one. Nothing
/// supervises it: a registration says "start this at logon", and a runtime that stops stays stopped until
/// somebody starts it. <c>enable</c> also starts the runtime now where none answers — on macOS the load does, since the
/// LaunchAgent carries <c>RunAtLoad</c>, and elsewhere it is started as <c>jason runtime start</c> starts it — and
/// <c>disable</c> stops nothing: stopping one is still <c>jason runtime stop</c>.
/// </para>
/// <para>
/// <c>disable</c> is the person's word that the runtime is not to start at logon, and it is recorded
/// (<see cref="AutostartOff"/>), so nothing that turns the start on by default turns it on again; <c>enable</c> takes it
/// back.
/// </para>
/// <para>
/// The registration carries the data directory as it stands when it is made. An operator working under a
/// <c>JASON_DATA_DIR</c> who registered without it would get a background runtime on <c>~/.jason</c> at every
/// logon and two runtimes disagreeing about which data is real.
/// </para>
/// </remarks>
public static class RuntimeAutostartCommands
{
    public static Command Build(CliEnvironment env, Option<string?> actor)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(actor);

        var autostart = new Command("autostart", "Register the runtime to start when you log on, or take that registration away.");

        autostart.Subcommands.Add(Verb(
            env,
            "enable",
            "Register this runtime to start at logon, carrying the data directory it is registered under, and on macOS and Linux "
            + "the search path (PATH) this command runs with, since launchd's own is " + LaunchdSearchPath
            + " and the systemd user manager's reads no shell profile. It takes back a turn-off disable recorded, and starts the "
            + "runtime now where none answers: on macOS the load starts it, elsewhere it is started as jason runtime start starts "
            + "it; a start that fails is said on stderr and leaves the registration in place.",
            actor,
            (human, cancellationToken) => EnableAsync(env, human, cancellationToken)));

        autostart.Subcommands.Add(Verb(
            env,
            "disable",
            "Take that registration away, so nothing starts at logon, and record that you turned it off (autostart/off.json in the "
            + "data directory), so nothing turns it on again by default; jason runtime autostart enable takes that back. A runtime that "
            + "is running keeps running; removing a registration that is not there is not an error. " + RestartWarning,
            actor,
            (human, _) => Task.FromResult(Disable(env, human))));

        autostart.Subcommands.Add(Verb(
            env,
            "status",
            "Say what is registered to start at logon: the data directory it names, the executable it runs, whether that "
            + "executable is still there, on macOS and Linux the search path it carries, under path, and under turned_off whether "
            + "you turned it off with jason runtime autostart disable.",
            actor,
            (human, _) => Task.FromResult(Status(env, human))));

        return autostart;
    }

    /// <summary>
    /// The one sentence <c>disable</c> says on stderr, and its help, about what a turn-off means: nothing of Jason starts by
    /// itself after the computer restarts.
    /// </summary>
    internal const string RestartWarning = "After a restart of this computer nothing of Jason runs until `jason runtime start`, or until you open Jason Desktop.";

    /// <summary>
    /// How long <c>enable</c> waits on macOS for the runtime the load started (<c>RunAtLoad</c>) to answer: the wait
    /// <c>jason runtime start</c> gives the runtime it launches. Past it nothing is launched, since the load's runtime may
    /// still be starting, a first start that migrates the database say, and a second one would fight it for the data.
    /// </summary>
    internal static readonly TimeSpan RunAtLoadWait = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan RunAtLoadPoll = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Registers it, takes back a turn-off, reads the machine back and prints what is really there, then starts the runtime
    /// where none answers and says on stderr how that went.
    /// </summary>
    public static async Task<int> EnableAsync(CliEnvironment env, bool human, CancellationToken cancellationToken, TimeSpan? loadWait = null)
    {
        ArgumentNullException.ThrowIfNull(env);

        var registrar = Registrar(env);
        string? before;
        int exit;
        try
        {
            if (registrar.Platform is AutostartPlatform.Unsupported)
            {
                throw AutostartRegistrars.CannotRegister();
            }

            // The instance recorded before the load, stale or not: on macOS the load's runtime is the one that answers with
            // another, whether a runtime ran before (a re-enable's unload stops launchd's) or a stale file was left.
            before = new DescriptorReader(env.Paths).Read()?.InstanceId;
            registrar.Apply(Registration(env, registrar.Platform));
            if (!AutostartOff.Remove(env.Paths))
            {
                env.Error.WriteLine($"The record that you turned the start at logon off could not be removed at {env.Paths.AutostartOffFile}: delete it, or the start still reads as turned off.");
            }

            exit = Print(env, human, registrar);
        }
        catch (AutostartException refusal)
        {
            return Fail(env, human, refusal);
        }

        env.Error.WriteLine(await StartNowAsync(env, registrar.Platform, before, loadWait ?? RunAtLoadWait, cancellationToken).ConfigureAwait(false));
        return exit;
    }

    /// <summary>
    /// The runtime started now, where none answers, and the one sentence that says how it went. On macOS the load starts it
    /// (<c>RunAtLoad</c>), and <c>enable</c> never launches one: it waits for a runtime that answers as another instance than
    /// the one recorded before the load, and past the wait says that one may still be starting. Elsewhere
    /// <c>jason runtime start</c>'s own routine, which reports a runtime that answers and starts one otherwise. An environment
    /// that names no process table starts nothing, as one that names no registrar registers nothing.
    /// </summary>
    private static async Task<string> StartNowAsync(CliEnvironment env, AutostartPlatform platform, string? before, TimeSpan loadWait, CancellationToken cancellationToken)
    {
        if (env.Processes is null)
        {
            return "This environment starts no runtime, so none was started now: `jason runtime start` starts one.";
        }

        if (platform is AutostartPlatform.MacOs)
        {
            return await LoadStartedAsync(env, before, loadWait, cancellationToken).ConfigureAwait(false)
                ? Running
                : "The runtime starts at each logon from here on; launchd's load starts it now, and it did not answer yet, so it may still be starting: `jason runtime status` says when it runs.";
        }

        using var said = new StringWriter(CultureInfo.InvariantCulture);
        var started = await RuntimeStartCommand.RunAsync(env with { Out = said }, human: false, cancellationToken).ConfigureAwait(false);
        return started == ExitCodes.Success
            ? Running
            : $"The runtime starts at each logon from here on, but none runs now: {Refusal(said.ToString())}";
    }

    private const string Running = "The runtime runs now, and starts at each logon from here on.";

    /// <summary>
    /// macOS: whether, within the wait, a runtime answers as an instance other than <paramref name="before"/>, the one the
    /// load started; asked as <c>jason runtime status</c> asks, which launches nothing. Where the wait runs out, a runtime that
    /// answers as the instance before it, one launchd did not start and the load left running, still counts.
    /// </summary>
    private static async Task<bool> LoadStartedAsync(CliEnvironment env, string? before, TimeSpan loadWait, CancellationToken cancellationToken)
    {
        var reader = new DescriptorReader(env.Paths);
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            if (reader.Read() is { } descriptor && !string.Equals(descriptor.InstanceId, before, StringComparison.Ordinal) && await AnswersAsync(env, cancellationToken).ConfigureAwait(false))
            {
                return true;
            }

            if (waited.Elapsed >= loadWait)
            {
                return before is not null && reader.Read() is { } last && string.Equals(last.InstanceId, before, StringComparison.Ordinal) && await AnswersAsync(env, cancellationToken).ConfigureAwait(false);
            }

            await Task.Delay(RunAtLoadPoll, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<bool> AnswersAsync(CliEnvironment env, CancellationToken cancellationToken)
    {
        using var said = new StringWriter(CultureInfo.InvariantCulture);
        return await RuntimeStatusCommand.RunAsync(env with { Out = said }, human: false, cancellationToken).ConfigureAwait(false) == ExitCodes.Success;
    }

    /// <summary>The sentence of a refusal <c>jason runtime start</c> printed, or what it printed where that is no refusal.</summary>
    private static string Refusal(string printed)
    {
        try
        {
            using var document = JsonDocument.Parse(printed);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String
                && message.GetString() is { Length: > 0 } text)
            {
                return text;
            }
        }
        catch (JsonException)
        {
            // Not a document: what it printed is said as it came.
        }

        return printed.Trim();
    }

    /// <summary>
    /// Takes it away and records the turn-off. A machine that registers nothing has nothing registered, so this is not the
    /// verb that refuses on one: there is nothing for a person to do about it, and the turn-off is recorded all the same.
    /// </summary>
    public static int Disable(CliEnvironment env, bool human)
    {
        ArgumentNullException.ThrowIfNull(env);

        var registrar = Registrar(env);
        int exit;
        try
        {
            if (registrar.Platform is not AutostartPlatform.Unsupported)
            {
                registrar.Remove(Removal(env, registrar.Platform));
            }

            exit = Print(env, human, registrar, recordOff: true);
        }
        catch (AutostartException refusal)
        {
            return Fail(env, human, refusal);
        }

        env.Error.WriteLine(RestartWarning);
        return exit;
    }

    public static int Status(CliEnvironment env, bool human)
    {
        ArgumentNullException.ThrowIfNull(env);

        try
        {
            return Print(env, human, Registrar(env));
        }
        catch (AutostartException refusal)
        {
            return Fail(env, human, refusal);
        }
    }

    /// <summary>
    /// The machine, or the refusal. Null is the refusal on purpose: this is the one seam here whose real
    /// implementation acts on the machine whatever data directory it was pointed at, so an environment that
    /// forgot to name one must not get it by accident.
    /// </summary>
    /// <summary>
    /// The registrar this environment names, or the one that refuses. Internal because <c>jason uninstall</c>
    /// takes a registration away as its first step and must do it the same way <c>disable</c> does: a second
    /// place that knows how this machine registers things is a second place to be wrong about it.
    /// </summary>
    internal static IAutostartRegistrar Registrar(CliEnvironment env) => env.Autostart ?? AutostartRegistrars.Unsupported;

    /// <remarks>
    /// Where a package manager installed this Jason, what is registered is the manager's stable entry and never the file
    /// this process resolved to: under Homebrew that is a keg the next <c>brew upgrade</c> deletes, and a registration
    /// naming it fails at every logon from then on without saying anything to anybody.
    /// </remarks>
    internal static AutostartRegistration Registration(CliEnvironment env, AutostartPlatform platform, bool namesDataDirectory = true) =>
        Composed(env, platform, env.SearchPath ?? Environment.GetEnvironmentVariable("PATH"), namesDataDirectory);

    /// <summary>
    /// The registration as a removal composes it: by its file and its name, with no search path. A search path differs
    /// from terminal to terminal and from one version to the next, and is not what makes a registration this one, so a
    /// PATH a new registration could not carry never stops <c>disable</c> or <c>jason uninstall</c> taking away what is there.
    /// </summary>
    /// <param name="leavesNoMark">
    /// True for <c>jason uninstall</c>, whose removal leaves nothing standing for the name, the disabled state on macOS
    /// included (<see cref="AutostartArtifacts.LeavingNoMark"/>); false for <c>disable</c>, which keeps it.
    /// </param>
    internal static AutostartRegistration Removal(CliEnvironment env, AutostartPlatform platform, bool leavesNoMark = false) =>
        leavesNoMark
            ? AutostartArtifacts.LeavingNoMark(Composed(env, platform, searchPath: null), AccountDatabase.UserId())
            : Composed(env, platform, searchPath: null);

    private static AutostartRegistration Composed(CliEnvironment env, AutostartPlatform platform, string? searchPath, bool namesDataDirectory = true) =>
        AutostartArtifacts.Compose(
            platform,
            PackageOwner.Of(env) is { } owner ? [owner.StableEntry] : env.InstallPath is { } installed ? [installed] : SelfExecutable.Command,
            env.Paths.Root,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            AutostartRegistrars.ThisAccount(),
            searchPath,
            namesDataDirectory);

    /// <summary>The search path launchd starts an agent on when the agent carries none of its own.</summary>
    internal const string LaunchdSearchPath = "/usr/bin:/bin:/usr/sbin:/sbin";

    private static int Print(CliEnvironment env, bool human, IAutostartRegistrar registrar, bool recordOff = false)
    {
        var state = registrar.Read();
        if (recordOff)
        {
            // Written once the machine has been read back, so a refusal to read it leaves no record of a turn-off that may
            // not have happened; a record that cannot be written is said, and the start may then be turned on again by default.
            try
            {
                AutostartOff.Write(env.Paths, DateTimeOffset.UtcNow, AutostartOff.Version(env));
            }
            catch (Exception unwritten) when (unwritten is IOException or UnauthorizedAccessException)
            {
                env.Error.WriteLine($"The record that you turned the start at logon off could not be written at {env.Paths.AutostartOffFile} ({unwritten.GetType().Name}), so a later install or update may turn it on again: run this again once that folder can be written.");
            }
        }

        var answer = new AutostartStatusResponse(
            state.Registered,
            Name(registrar.Platform),
            DataDirectoryIn(state.Run),
            state.Run.Count > 0 ? state.Run[0] : null,
            state.Run.Count > 0 ? File.Exists(state.Run[0]) : null,
            Document(env, registrar, state),
            registrar.Platform is AutostartPlatform.MacOs or AutostartPlatform.Linux ? state.SearchPath : null,
            AutostartOff.Stands(env.Paths, registrar));

        env.Out.WriteLine(human ? Sentence(answer) : JsonSerializer.Serialize(answer, JasonJson.Options));
        return ExitCodes.Success;
    }

    private static int Fail(CliEnvironment env, bool human, AutostartException refusal)
    {
        CliErrors.Print(env.Out, human, ExitCodes.ApiError, CliErrors.Serialize(refusal.Code, refusal.Message, retryable: false));
        return ExitCodes.ApiError;
    }

    /// <summary>
    /// Where the document that was registered lives. Two of the three platforms read a file, and the registrar
    /// names it. Windows does not: the Task Scheduler took a copy when the task was made, and what stays behind
    /// under this data directory is the file that was handed over — which is worth printing when it is there,
    /// because it is what a person looking at the task would want to read, and worth leaving out when it is
    /// not, because a registration made under another data directory left its own file somewhere else.
    /// </summary>
    private static string? Document(CliEnvironment env, IAutostartRegistrar registrar, AutostartState state)
    {
        if (state.ArtifactPath is { } named)
        {
            return named;
        }

        if (!state.Registered || registrar.Platform is AutostartPlatform.Unsupported)
        {
            return null;
        }

        var handed = AutostartArtifacts.ArtifactPath(
            registrar.Platform,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            env.Paths.Root);

        return File.Exists(handed) ? handed : null;
    }

    /// <summary>The data directory a registered command line names, which need not be this session's.</summary>
    internal static string? DataDirectoryIn(IReadOnlyList<string> run)
    {
        for (var word = 0; word < run.Count - 1; word++)
        {
            if (string.Equals(run[word], "--data-dir", StringComparison.Ordinal))
            {
                return run[word + 1];
            }
        }

        return null;
    }

    internal static string Name(AutostartPlatform platform) => platform switch
    {
        AutostartPlatform.Windows => "windows",
        AutostartPlatform.MacOs => "macos",
        AutostartPlatform.Linux => "linux",
        _ => "unsupported",
    };

    /// <summary>What <c>status</c> says on a machine with no way to register, and <c>jason import</c> says the same way.</summary>
    internal const string CannotRegister = "No logon registration, and this machine has no way to register one.";

    private static string Sentence(AutostartStatusResponse answer)
    {
        if (!answer.Registered)
        {
            return answer.Platform == "unsupported"
                ? CannotRegister + (answer.TurnedOff ? " You turned the start at logon off with `jason runtime autostart disable`." : string.Empty)
                : answer.TurnedOff
                    ? "No logon registration: you turned it off with `jason runtime autostart disable`. " + RestartWarning
                    : "No logon registration.";
        }

        var missing = answer.ExecutableExists == false
            ? $" The executable it runs is not there any more: {answer.Executable}."
            : string.Empty;

        // On macOS and Linux the search path is the half of the registration a plugin's program depends on: launchd's own
        // holds none a package manager installed, and the systemd user manager's reads no shell profile, so a
        // registration that carries none is said to start on theirs.
        var searched = answer.Platform is not ("macos" or "linux")
            ? string.Empty
            : answer.Path is { } path
                ? $", with the search path {path}"
                : answer.Platform == "macos"
                    ? $", with launchd's default search path, {LaunchdSearchPath}"
                    : ", with the systemd user manager's search path";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"The runtime starts at logon from {answer.Executable}, on the data directory {answer.DataDirectory}{searched}.{missing}");
    }

    private static Command Verb(CliEnvironment env, string name, string description, Option<string?> actor, Func<bool, CancellationToken, Task<int>> run)
    {
        var command = new Command(name, description);
        var human = VerbOptions.Human();
        command.Options.Add(human);
        command.SetAction((parseResult, cancellationToken) =>
        {
            // About this machine rather than about business state, so no claim travels with it — but a
            // malformed claim is still refused here, so --actor behaves the same on every verb.
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            return run(parseResult.GetValue(human), cancellationToken);
        });

        return command;
    }
}
