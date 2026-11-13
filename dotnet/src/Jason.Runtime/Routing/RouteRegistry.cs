using Jason.Runtime.Plugins.Registry;

namespace Jason.Runtime.Routing;

/// <summary>
/// The one active route snapshot, behind one reference. An activation swaps it whole or leaves it alone;
/// nothing edits a snapshot in place, so a claim that has already resolved its route is never affected by what
/// an activation decides afterwards. Exactly the shape <see cref="PluginRegistry"/> has, for the same reason.
/// </summary>
public sealed class RouteRegistry
{
    private RouteSnapshot _snapshot;

    /// <param name="plugins">
    /// The plugin registry, so that even the snapshot a runtime holds before its first load names the plugin
    /// snapshot it belongs to. A route snapshot is always pinned to one: that pairing is what an attempt
    /// records, and an unpinned snapshot would make it optional for exactly the first moments of a process.
    /// </param>
    public RouteRegistry(TimeProvider clock, PluginRegistry plugins)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(plugins);
        _snapshot = RouteSnapshot.Empty(clock.GetUtcNow(), plugins.Snapshot.Id);
    }

    private int _activated;

    public RouteSnapshot Snapshot => Volatile.Read(ref _snapshot);

    /// <summary>
    /// Whether a snapshot has been activated in this process: false only before the first load activates one, the start,
    /// whose routes in force are the empty snapshot the registry was made with.
    /// </summary>
    public bool Activated => Volatile.Read(ref _activated) != 0;

    public void Replace(RouteSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Volatile.Write(ref _snapshot, snapshot);
        Volatile.Write(ref _activated, 1);
    }
}
