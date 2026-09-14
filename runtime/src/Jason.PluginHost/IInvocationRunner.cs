using Jason.Contracts.Plugins;

namespace Jason.PluginHost;

/// <summary>
/// What turns a validated envelope into an outcome: one engine, one call into the plugin, one answer. It is an
/// interface so the mode around it — arguments, envelope, exit codes — can be tested without an engine, and so
/// the one thing that must never happen (a second outcome, or none) has a single implementation to audit.
/// </summary>
public interface IInvocationRunner
{
    PluginOutcome Run(PluginInvocation invocation, HostDiagnostics diagnostics, CancellationToken deadline);
}

/// <summary>
/// The stand-in until the engine is wired in: it answers honestly rather than pretending to have run anything.
/// A failed outcome, not an exception, because the protocol did complete.
/// </summary>
internal sealed class NotImplementedRunner : IInvocationRunner
{
    public PluginOutcome Run(PluginInvocation invocation, HostDiagnostics diagnostics, CancellationToken deadline)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        return new PluginOutcome(
            PluginProtocol.CurrentVersion,
            invocation.InvocationId,
            OutcomeStatus.Failed,
            Result: null,
            ExternalIds: null,
            new OutcomeError(FailureClass.Permanent, "not_implemented", "This build cannot run a plugin's JavaScript yet.", null, null),
            new OutcomeDiagnostics(0, 0, 0, diagnostics?.LogLines ?? 0));
    }
}
