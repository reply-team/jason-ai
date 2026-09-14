using Jason.Contracts.Ids;
using Jason.Contracts.Plugins;

namespace Jason.Runtime.Plugins.Registry;

/// <summary>
/// The whole registry at one moment: every plugin the last activated load produced, under one id. It lives in
/// memory only — it is derived from the files on disk plus the settings, and re-derived at every start, so there
/// is nothing here a migration could ever be about.
/// </summary>
public sealed record PluginSnapshot(string Id, DateTime LoadedAt, SnapshotSource Source, IReadOnlyList<LoadedPlugin> Plugins)
{
    public static PluginSnapshot Empty(DateTime now, SnapshotSource source) =>
        new(PublicId.New(PluginProtocol.SnapshotIdPrefix), now, source, []);

    public LoadedPlugin? Find(string id) =>
        Plugins.FirstOrDefault(plugin => string.Equals(plugin.Manifest.Id, id, StringComparison.Ordinal));
}
