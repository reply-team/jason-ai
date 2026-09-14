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
    public static IInvocationRunner Runner { get; set; } = new NotImplementedRunner();

    public static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdin);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

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
        var diagnostics = new HostDiagnostics(stderr, Redactor.None, EarlyLogLimits, arguments.PluginId, "unknown", TimeProvider.System);

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
            GrantedSecrets(invocation),
            invocation.Limits.Log,
            invocation.Plugin.Id,
            invocation.InvocationId,
            TimeProvider.System);

        PluginOutcome outcome;
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            // A second beyond the engine's own timeout: the runner is expected to end the invocation itself and
            // report a timed-out outcome, and this is only the backstop for a runner that does not.
            deadline.CancelAfter(TimeSpan.FromMilliseconds((long)invocation.Limits.TimeoutMs + 1000));

            try
            {
                outcome = Runner.Run(invocation, diagnostics, deadline.Token);
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

    /// <summary>
    /// The values of the granted variables, read from this process's own environment — the only place they exist,
    /// since no secret travels in the envelope. They are known here so that nothing the host writes can leak one.
    /// </summary>
    private static Redactor GrantedSecrets(PluginInvocation invocation) =>
        new((invocation.Grants.Env?.Variables ?? []).Select(Environment.GetEnvironmentVariable));
}
