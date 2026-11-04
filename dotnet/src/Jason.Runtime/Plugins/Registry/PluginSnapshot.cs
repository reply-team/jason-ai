using Jason.Contracts.Ids;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Subscriptions;

namespace Jason.Runtime.Plugins.Registry;

/// <summary>
/// The whole registry at one moment: every plugin the last activated load produced, under one id. It lives in
/// memory only — it is derived from the files on disk plus the settings, and re-derived at every start, so there
/// is nothing here a migration could ever be about.
/// <para>
/// The chains each journal kind runs through belong to the same moment: <see cref="Subscriptions"/> is set by the load
/// before the swap, so plugins, their grants and the chains built from them change together.
/// </para>
/// </summary>
public sealed record PluginSnapshot(string Id, DateTime LoadedAt, SnapshotSource Source, IReadOnlyList<LoadedPlugin> Plugins)
{
    /// <summary>Which plugins handle each journal kind, in order, as this snapshot's load froze them.</summary>
    public SubscriptionChains Subscriptions { get; init; } = SubscriptionChains.Empty;

    public static PluginSnapshot Empty(DateTime now, SnapshotSource source) =>
        new(PublicId.New(PluginProtocol.SnapshotIdPrefix), now, source, []);

    public LoadedPlugin? Find(string id) =>
        Plugins.FirstOrDefault(plugin => string.Equals(plugin.Manifest.Id, id, StringComparison.Ordinal));
}
