using Jason.Contracts.Plugins;

namespace Jason.Runtime.Plugins.Registry;

/// <summary>
/// The one active snapshot, behind one reference. A reload swaps it whole or leaves it alone; nothing edits a
/// snapshot in place, so an invocation that is already running is never affected by what a reload decides.
/// </summary>
public sealed class PluginRegistry
{
    private PluginSnapshot _snapshot;
    private ReloadReport? _lastReload;

    public PluginRegistry(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _snapshot = PluginSnapshot.Empty(clock.GetUtcNow().UtcDateTime, SnapshotSource.Startup);
    }

    public PluginSnapshot Snapshot => Volatile.Read(ref _snapshot);

    /// <summary>The diagnostics of the last load, activated or not — the only account of a reload that changed nothing.</summary>
    public ReloadReport? LastReload => Volatile.Read(ref _lastReload);

    public void Replace(PluginSnapshot snapshot, ReloadReport report)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(report);
        Volatile.Write(ref _snapshot, snapshot);
        Volatile.Write(ref _lastReload, report);
    }

    /// <summary>A load that could not be activated: the previous snapshot stays, and the report says why.</summary>
    public void Record(ReloadReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        Volatile.Write(ref _lastReload, report);
    }
}
