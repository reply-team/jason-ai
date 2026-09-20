using System.CommandLine;
using System.Globalization;
using System.Security.Principal;
using System.Text.Json;
using Jason.Cli.Autostart;
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
/// somebody starts it. <c>enable</c> starts nothing now and <c>disable</c> stops nothing; those are still
/// <c>jason runtime start</c> and <c>jason runtime stop</c>.
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
            "enable",
            "Register this runtime to start at logon, carrying the data directory it is registered under. Nothing is started now.",
            actor,
            human => Enable(env, human)));

        autostart.Subcommands.Add(Verb(
            "disable",
            "Take that registration away, so nothing starts at logon. A runtime that is running keeps running; removing a registration that is not there is not an error.",
            actor,
            human => Disable(env, human)));

        autostart.Subcommands.Add(Verb(
            "status",
            "Say what is registered to start at logon: the data directory it names, the executable it runs, and whether that executable is still there.",
            actor,
            human => Status(env, human)));

        return autostart;
    }

    /// <summary>Registers it, then reads the machine back and prints what is really there.</summary>
    public static int Enable(CliEnvironment env, bool human)
    {
        ArgumentNullException.ThrowIfNull(env);

        var registrar = Registrar(env);
        try
        {
            if (registrar.Platform is AutostartPlatform.Unsupported)
            {
                throw AutostartRegistrars.CannotRegister();
            }

            registrar.Apply(Registration(env, registrar.Platform));
            return Print(env, human, registrar);
        }
        catch (AutostartException refusal)
        {
            return Fail(env, refusal);
        }
    }

    /// <summary>
    /// Takes it away. A machine that registers nothing has nothing registered, so this is not the verb that
    /// refuses on one: there is nothing for a person to do about it.
    /// </summary>
    public static int Disable(CliEnvironment env, bool human)
    {
        ArgumentNullException.ThrowIfNull(env);

        var registrar = Registrar(env);
        try
        {
            if (registrar.Platform is not AutostartPlatform.Unsupported)
            {
                registrar.Remove(Registration(env, registrar.Platform));
            }

            return Print(env, human, registrar);
        }
        catch (AutostartException refusal)
        {
            return Fail(env, refusal);
        }
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
            return Fail(env, refusal);
        }
    }

    /// <summary>
    /// The machine, or the refusal. Null is the refusal on purpose: this is the one seam here whose real
    /// implementation acts on the machine whatever data directory it was pointed at, so an environment that
    /// forgot to name one must not get it by accident.
    /// </summary>
    private static IAutostartRegistrar Registrar(CliEnvironment env) => env.Autostart ?? AutostartRegistrars.Unsupported;

    private static AutostartRegistration Registration(CliEnvironment env, AutostartPlatform platform) =>
        AutostartArtifacts.Compose(
            platform,
            env.InstallPath is { } installed ? [installed] : SelfExecutable.Command,
            env.Paths.Root,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Account());

    /// <summary>
    /// Who the registration runs as. On Windows that is this account's SID: a Microsoft account's display name
    /// is not what the Task Scheduler stores, and a name with a domain in front of it is not what it compares.
    /// Everywhere else a document under the account's own home directory says whose it is by being there.
    /// </summary>
    private static string Account() =>
        OperatingSystem.IsWindows()
            ? WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName
            : Environment.UserName;

    private static int Print(CliEnvironment env, bool human, IAutostartRegistrar registrar)
    {
        var state = registrar.Read();
        var answer = new AutostartStatusResponse(
            state.Registered,
            Name(registrar.Platform),
            DataDirectoryIn(state.Run),
            state.Run.Count > 0 ? state.Run[0] : null,
            state.Run.Count > 0 ? File.Exists(state.Run[0]) : null,
            state.ArtifactPath);

        env.Out.WriteLine(human ? Sentence(answer) : JsonSerializer.Serialize(answer, JasonJson.Options));
        return ExitCodes.Success;
    }

    private static int Fail(CliEnvironment env, AutostartException refusal)
    {
        env.Out.WriteLine(CliErrors.Serialize(refusal.Code, refusal.Message, retryable: false));
        return ExitCodes.ApiError;
    }

    /// <summary>The data directory a registered command line names, which need not be this session's.</summary>
    private static string? DataDirectoryIn(IReadOnlyList<string> run)
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

    private static string Name(AutostartPlatform platform) => platform switch
    {
        AutostartPlatform.Windows => "windows",
        AutostartPlatform.MacOs => "macos",
        AutostartPlatform.Linux => "linux",
        _ => "unsupported",
    };

    private static string Sentence(AutostartStatusResponse answer)
    {
        if (!answer.Registered)
        {
            return answer.Platform == "unsupported"
                ? "Nothing is registered to start at logon, and this machine has no way to register anything."
                : "Nothing is registered to start at logon.";
        }

        var missing = answer.ExecutableExists == false
            ? $" The executable it runs is not there any more: {answer.Executable}."
            : string.Empty;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"The runtime starts at logon from {answer.Executable}, on the data directory {answer.DataDirectory}.{missing}");
    }

    private static Command Verb(string name, string description, Option<string?> actor, Func<bool, int> run)
    {
        var command = new Command(name, description);
        var human = VerbOptions.Human();
        command.Options.Add(human);
        command.SetAction(parseResult =>
        {
            // About this machine rather than about business state, so no claim travels with it — but a
            // malformed claim is still refused here, so --actor behaves the same on every verb.
            ActorOption.Parse(parseResult.GetValue(actor));
            return run(parseResult.GetValue(human));
        });

        return command;
    }
}
