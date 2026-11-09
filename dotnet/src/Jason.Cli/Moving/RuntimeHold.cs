using Jason.Cli.Commands;
using Jason.Cli.Process;

namespace Jason.Cli.Moving;

/// <summary>
/// The runtime as the two verbs that move an installation need it: stopped while they read or place its files. They stop
/// and start it through <c>jason runtime stop</c> and <c>jason runtime start</c> rather than beside them, as
/// <c>uninstall</c> and <c>update apply</c> do, so "is it really gone" and "did it come up" are decided in one place each.
/// </summary>
internal static class RuntimeHold
{
    /// <summary>Whether a runtime is running on this data directory, by its descriptor and the process it names.</summary>
    public static bool Running(CliEnvironment env) =>
        DescriptorProcess.Live(env.Paths, env.Processes ?? RuntimeProcessControl.Instance) is not null;

    /// <summary>What a verb says when the runtime runs and it was not told to stop it.</summary>
    public static string RunningRefusal(CliEnvironment env, string verb) =>
        $"The runtime is running on {env.Paths.Root}, and 'jason {verb}' works on its files only while it is stopped. "
        + $"Stop it with 'jason runtime stop' and run this again, or give --stop and 'jason {verb}' stops it itself. Nothing was written.";

    /// <summary>Stops it; null when it stopped, otherwise what the stop said.</summary>
    public static async Task<string?> StopAsync(CliEnvironment env, TimeSpan? timeout, TimeSpan? poll, CancellationToken cancellationToken)
    {
        var said = new StringWriter();
        var exit = await RuntimeStopCommand.RunAsync(env with { Out = said }, human: false, cancellationToken, timeout, poll).ConfigureAwait(false);

        // The process table decides, as it does for uninstall: an answer of 3 can be a descriptor that went in between as
        // much as a runtime that would not answer.
        return exit == ExitCodes.Success || !Running(env) ? null : said.ToString().Trim();
    }

    /// <summary>Starts it and waits until it answers; null when it did, otherwise what the start said.</summary>
    public static async Task<string?> StartAsync(CliEnvironment env, TimeSpan? timeout, TimeSpan? poll, CancellationToken cancellationToken)
    {
        var said = new StringWriter();
        var exit = await RuntimeStartCommand.RunAsync(env with { Out = said }, human: false, cancellationToken, timeout, poll).ConfigureAwait(false);
        return exit == ExitCodes.Success ? null : said.ToString().Trim();
    }
}
