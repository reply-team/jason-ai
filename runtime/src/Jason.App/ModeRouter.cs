using System.Runtime.CompilerServices;
using Jason.Cli;
using Jason.Contracts;
using Jason.Contracts.Discovery;
using Jason.PluginHost;
using Jason.Runtime.Hosting;

namespace Jason.App;

public enum Mode
{
    Version,
    RuntimeService,
    PluginHost,
    Cli,
}

/// <summary>
/// One executable, several modes, chosen from the leading arguments. Each mode is entered through its own
/// non-inlined method so the CLI path never JIT-compiles — and therefore never loads — the server side.
/// </summary>
public static class ModeRouter
{
    public static Mode Select(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args switch
        {
            ["--version"] => Mode.Version,
            ["runtime", "run", ..] => Mode.RuntimeService,
            ["plugin-host", ..] => Mode.PluginHost,
            _ => Mode.Cli,
        };
    }

    public static Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default) => Select(args) switch
    {
        Mode.Version => RunVersion(),
        Mode.RuntimeService => RunRuntimeService(args, cancellationToken),
        Mode.PluginHost => RunPluginHost(args, cancellationToken),
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

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> RunRuntimeService(string[] args, CancellationToken cancellationToken)
    {
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
        if (arguments.Detached)
        {
            // The one thing a detached runtime may still say to whoever started it: why it will not start. Kept
            // before the standard streams are let go of, and closed by the host as soon as the runtime has
            // started or refused -- so `jason runtime start` hears a data directory that cannot be prepared
            // rather than an exit code and an empty log directory.
            refusals = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };

            // Before anything opens a log file or a socket: from here on the process owns no console.
            ProcessDetacher.Detach();
        }

        return RuntimeHost.RunAsync(
            PathsFor(arguments),
            new RuntimeHostOptions(ShippedSettingsDirectory: AppContext.BaseDirectory, ConsoleLogging: !arguments.Detached, Refusals: refusals),
            cancellationToken);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> RunPluginHost(string[] args, CancellationToken cancellationToken) =>
        PluginHostMode.RunAsync(args[1..], Console.In, Console.Out, Console.Error, cancellationToken);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> RunCli(string[] args, CancellationToken cancellationToken) =>
        CliApp.RunAsync(args, CliEnvironment.Default(), cancellationToken);
}
