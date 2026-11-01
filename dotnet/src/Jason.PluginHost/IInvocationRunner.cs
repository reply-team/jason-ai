using Jason.Contracts.Plugins;

namespace Jason.PluginHost;

/// <summary>
/// What turns a validated envelope into an outcome: one engine, one call into the plugin, one answer. It is an
/// interface so the mode around it — arguments, envelope, exit codes — can be tested without an engine, and so
/// the one thing that must never happen (a second outcome, or none) has a single implementation to audit.
/// </summary>
public interface IInvocationRunner
{
    /// <summary>
    /// Runs the invocation to its one outcome. A kill a call stopped waiting for is left in <paramref name="kills"/>,
    /// for the host to see end before it exits.
    /// </summary>
    PluginOutcome Run(PluginInvocation invocation, HostDiagnostics diagnostics, UnfinishedKills kills, CancellationToken deadline);
}
