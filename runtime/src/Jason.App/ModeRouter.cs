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
        Mode.PluginHost => RunPluginHost(args),
        _ => RunCli(args, cancellationToken),
    };

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> RunVersion()
    {
        Console.Out.WriteLine(JasonVersion.Current);
        return Task.FromResult(0);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> RunRuntimeService(string[] args, CancellationToken cancellationToken)
    {
        var arguments = RuntimeRunArguments.Parse(args);
        if (arguments.Detached)
        {
            // Before anything opens a log file or a socket: from here on the process owns no console.
            ProcessDetacher.Detach();
        }

        return RuntimeHost.RunAsync(
            JasonPaths.FromEnvironment(),
            new RuntimeHostOptions(ShippedSettingsDirectory: AppContext.BaseDirectory, ConsoleLogging: !arguments.Detached),
            cancellationToken);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> RunPluginHost(string[] args) =>
        Task.FromResult(PluginHostMode.Run(args[1..], Console.Error));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> RunCli(string[] args, CancellationToken cancellationToken) =>
        CliApp.RunAsync(args, CliEnvironment.Default(), cancellationToken);
}
