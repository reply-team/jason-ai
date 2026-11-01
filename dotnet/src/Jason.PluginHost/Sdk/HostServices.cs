using Jason.Contracts.Plugins;

namespace Jason.PluginHost.Sdk;

/// <summary>
/// Everything the host functions that reach outside the engine share: the envelope they enforce, the one stderr
/// they write to, the call budget they spend, the kills the host must see end before it exits, how much of the
/// invocation's time is left, and where a child process starts. Nothing here is reachable from JavaScript — the
/// functions close over it, the <c>host</c> object does not expose it.
/// </summary>
public sealed class HostServices(
    PluginInvocation invocation,
    HostDiagnostics diagnostics,
    CallBudget budget,
    UnfinishedKills kills,
    Func<TimeSpan> remaining,
    string workingDirectory,
    CancellationToken deadline)
{
    public PluginInvocation Invocation { get; } = invocation;

    public HostDiagnostics Diagnostics { get; } = diagnostics;

    public CallBudget Budget { get; } = budget;

    /// <summary>The kills a call stopped waiting for; the host waits for them once its outcome is written.</summary>
    public UnfinishedKills Kills { get; } = kills;

    /// <summary>What is left of the invocation's budget: every call the plugin makes is capped by it.</summary>
    public Func<TimeSpan> Remaining { get; } = remaining;

    /// <summary>The per-invocation directory the runtime made: the cwd of this process and of every child.</summary>
    public string WorkingDirectory { get; } = workingDirectory;

    public CancellationToken Deadline { get; } = deadline;

    public InvocationGrants Grants => Invocation.Grants;

    public InvocationLimits Limits => Invocation.Limits;
}
