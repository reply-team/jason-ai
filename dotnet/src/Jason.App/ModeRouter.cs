using System.Runtime.CompilerServices;
using Jason.Cli;
using Jason.Cli.Link;
using Jason.Contracts;
using Jason.Contracts.Discovery;
using Jason.Contracts.Notifications;
using Jason.Contracts.Update;
using Jason.PluginHost;
using Jason.Runtime.Hosting;
using Jason.Runtime.Notifications.Desktop;

namespace Jason.App;

public enum Mode
{
    Version,
    RuntimeService,
    PluginHost,
    ToastHost,
    Link,
    Cli,
}

/// <summary>
/// One executable, several modes, chosen from the leading arguments. Each mode is entered through its own
/// non-inlined method so the CLI path never JIT-compiles — and therefore never loads — the server side.
/// </summary>
public static class ModeRouter
{
    /// <summary>What a link the shell hands this executable starts with: no verb does, so a click is never read as one.</summary>
    private const string LinkPrefix = AttentionLinks.Scheme + ":";

    public static Mode Select(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args switch
        {
            ["--version"] => Mode.Version,
            ["runtime", "run", ..] => Mode.RuntimeService,
            ["plugin-host", ..] => Mode.PluginHost,
            ["toast-host", ..] => Mode.ToastHost,
            [var first, ..] when first.StartsWith(LinkPrefix, StringComparison.OrdinalIgnoreCase) => Mode.Link,
            _ => Mode.Cli,
        };
    }

    public static Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default) => Select(args) switch
    {
        Mode.Version => RunVersion(),
        Mode.RuntimeService => RunRuntimeService(args, cancellationToken),
        Mode.PluginHost => RunPluginHost(args, cancellationToken),
        Mode.ToastHost => RunToastHost(args, cancellationToken),
        Mode.Link => RunLink(args, cancellationToken),
        _ => RunCli(args, cancellationToken),
    };

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> RunVersion()
    {
        Console.Out.WriteLine(JasonVersion.Current);
        return Task.FromResult(0);
    }

    /// <summary>
    /// Which data directory a run owns: the one named on the command line, or the one the environment names, or
    /// the default. The flag wins, because the one thing that names it has no environment to say it in — a
    /// Windows logon task carries none — and a registration that carries its data directory must not be
    /// overruled by whatever the session it happens to start in has set.
    /// </summary>
    public static JasonPaths PathsFor(RuntimeRunArguments arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return arguments.DataDirectory is { } named ? new JasonPaths(named) : JasonPaths.FromEnvironment();
    }

    /// <summary>
    /// The update's mark on this run, taken out of this process's environment: the runtime decides from it whether it may
    /// start around an update left part-way, and nothing it starts — a plugin host, an agent, a toast — may carry it on to
    /// a runtime of its own. Taken before anything reads the environment, the configuration's <c>JASON_</c> variables
    /// included.
    /// </summary>
    public static string? TakeUpdateLaunch()
    {
        var mark = Environment.GetEnvironmentVariable(UpdateLaunch.Variable);
        Environment.SetEnvironmentVariable(UpdateLaunch.Variable, null);
        return mark;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> RunRuntimeService(string[] args, CancellationToken cancellationToken)
    {
        var updateLaunch = TakeUpdateLaunch();

        RuntimeRunArguments arguments;
        try
        {
            arguments = RuntimeRunArguments.Parse(args);
        }
        catch (RuntimeRunUsageException error)
        {
            Console.Error.WriteLine($"usage: jason runtime run [--detached] [--data-dir <path>] - {error.Message}");
            return Task.FromResult(2);
        }

        TextWriter? refusals = null;
        ConsoleAtStart? console = null;
        if (arguments.Detached)
        {
            // The one thing a detached runtime may still say to whoever started it: why it will not start. Kept
            // before the standard streams are let go of, and closed by the host as soon as the runtime has
            // started or refused -- so `jason runtime start` hears a data directory that cannot be prepared
            // rather than an exit code and an empty log directory.
            refusals = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };

            // Before anything opens a log file or a socket: from here on the process owns no console. What it found
            // is handed to the host, which writes it into the log once there is one.
            console = ProcessDetacher.Detach();
        }

        return RuntimeHost.RunAsync(
            PathsFor(arguments),
            new RuntimeHostOptions(
                ShippedSettingsDirectory: AppContext.BaseDirectory,
                ConsoleLogging: !arguments.Detached,
                Refusals: refusals,
                ConsoleAtStart: console)
            {
                UpdateLaunch = updateLaunch,
            },
            cancellationToken);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> RunPluginHost(string[] args, CancellationToken cancellationToken) =>
        PluginHostMode.RunAsync(args[1..], Console.In, Console.Out, Console.Error, cancellationToken);

    /// <summary>
    /// The short-lived child that shows one toast on Windows: its payload on stdin, one outcome on stdout, and the
    /// interop it calls inside this child rather than inside the runtime.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> RunToastHost(string[] args, CancellationToken cancellationToken) =>
        ToastHost.RunAsync(args[1..], Console.In, Console.Out, Console.Error, cancellationToken);

    /// <summary>
    /// A click on a <c>jason:</c> link: the shell starts this executable with the link as its one argument, and the CLI
    /// reads it against the link table. Its own mode, so the link is never parsed as a verb.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> RunLink(string[] args, CancellationToken cancellationToken) =>
        LinkHandler.RunAsync(args, CliEnvironment.Default(), cancellationToken);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> RunCli(string[] args, CancellationToken cancellationToken) =>
        CliApp.RunAsync(args, CliEnvironment.Default(), cancellationToken);
}
