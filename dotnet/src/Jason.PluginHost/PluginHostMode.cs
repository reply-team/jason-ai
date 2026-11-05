using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;

namespace Jason.PluginHost;

/// <summary>
/// The <c>jason plugin-host</c> mode: a short-lived child process that runs exactly one invocation of one
/// plugin and exits. Four values on the command line, one envelope on stdin, one outcome on stdout, JSON Lines
/// on stderr, and an exit code that says whether the protocol completed — nothing else crosses the boundary.
/// </summary>
public static class PluginHostMode
{
    private const string Usage = "usage: jason plugin-host --protocol 1 --plugin <id> --operation <op> --correlation <id>";

    /// <summary>What the log is bounded by until the envelope says otherwise.</summary>
    private static readonly LogLimits EarlyLogLimits = new(LineBytes: 16_384, TotalBytes: 4_194_304);

    /// <summary>
    /// The seam the mode's own tests run against, and the one place the engine is reached from. Set by the
    /// process, never by a plugin: there is no way to reach it from JavaScript.
    /// </summary>
    public static IInvocationRunner Runner { get; set; } = new InvocationRunner();

    public static Task<int> RunAsync(
        IReadOnlyList<string> args,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken) =>
        RunAsync(args, stdin, stdout, stderr, TimeProvider.System, cancellationToken);

    /// <summary>The mode on a clock a test can hold still: the one the host's deadline and its lines are read from.</summary>
    internal static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdin);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);
        ArgumentNullException.ThrowIfNull(clock);

        // The invocation's own deadline is counted from the host's start: no later than the deadline the plugin is told,
        // and below the runtime's kill of this process by the runtime's grace less the time this process took to start.
        var startedAt = clock.GetUtcNow();

        PluginHostArguments arguments;
        try
        {
            arguments = PluginHostArguments.Parse(args);
            if (arguments.Protocol != PluginProtocol.CurrentVersion)
            {
                throw new HostUsageException(
                    $"protocol {arguments.Protocol} is not supported; this host speaks protocol {PluginProtocol.CurrentVersion}.");
            }
        }
        catch (HostUsageException ex)
        {
            await stderr.WriteLineAsync($"jason plugin-host: {ex.Message} {Usage}").ConfigureAwait(false);
            await stderr.FlushAsync(cancellationToken).ConfigureAwait(false);
            return HostExitCodes.Usage;
        }

        // Until the envelope has been read the host knows of no secrets, so it masks nothing; from then on it
        // masks the values of every variable the plugin was granted.
        var diagnostics = new HostDiagnostics(stderr, Redactor.None, EarlyLogLimits, arguments.PluginId, "unknown", clock);

        PluginInvocation invocation;
        try
        {
            invocation = await InvocationReader.ReadAsync(stdin, arguments, diagnostics, cancellationToken).ConfigureAwait(false);
        }
        catch (InvocationRejectedException ex)
        {
            diagnostics.Host("error", "invocation_rejected", new JsonObject { ["code"] = ex.Code, ["detail"] = ex.Message });
            return HostExitCodes.Rejected;
        }

        diagnostics = new HostDiagnostics(
            stderr,
            KnownSecrets(invocation),
            invocation.Limits.Log,
            invocation.Plugin.Id,
            invocation.InvocationId,
            clock);

        var kills = new UnfinishedKills();
        try
        {
            PluginOutcome outcome;
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                // A second beyond the engine's own timeout: the runner is expected to end the invocation itself and
                // report a timed-out outcome, and this is only the backstop for a runner that does not.
                deadline.CancelAfter(TimeSpan.FromMilliseconds((long)invocation.Limits.TimeoutMs + 1000));

                try
                {
                    outcome = Runner.Run(invocation, diagnostics, kills, deadline.Token);
                }
#pragma warning disable CA1031 // The host's last line of defence: anything unexpected is reported and exits 4.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    await stderr.WriteLineAsync(ex.ToString()).ConfigureAwait(false);
                    await stderr.FlushAsync(cancellationToken).ConfigureAwait(false);
                    return HostExitCodes.HostFailure;
                }
            }

            await OutcomeWriter.WriteAsync(stdout, outcome).ConfigureAwait(false);
            return HostExitCodes.Completed;
        }
        finally
        {
            // A kill an exec stopped waiting for is still ending its program, and this process does not exit before it
            // has, whichever way the invocation ended. An exit would cut it short: on Unix .NET stops the program
            // (SIGSTOP), finds its children and only then kills (SIGKILL), so a cut between the two leaves the program
            // stopped for good; on Windows the program outlives its parent, out of reach of a kill of this tree. And the
            // runtime, seeing this process exit, would never use its own kill. Past the invocation's deadline the wait is
            // said and goes on: should the runtime's lifetime kill come first, it ends this process's whole tree, the
            // program still in it, and the attempt is a timeout rather than what this process wrote. A host told to stop
            // by its own token or a console signal exits without waiting for its kill; that is not the runtime's path,
            // which cancels nothing and ends the tree, and the shipped executable passes no token.
            await kills.WaitAsync(startedAt.AddMilliseconds(invocation.Limits.TimeoutMs), clock, diagnostics, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The values of the granted variables, read from this process's own environment — the only place they exist,
    /// since no secret travels in the envelope. They are known here so that nothing the host writes can leak one.
    /// </summary>
    /// <summary>
    /// The values this invocation must never write: those of the variables it was granted, and the door's capability
    /// where it carries one.
    /// </summary>
    internal static Redactor KnownSecrets(PluginInvocation invocation) =>
        new((invocation.Grants.Env?.Variables ?? [])
            .Select(Environment.GetEnvironmentVariable)
            .Append(invocation.Door?.Capability));
}
