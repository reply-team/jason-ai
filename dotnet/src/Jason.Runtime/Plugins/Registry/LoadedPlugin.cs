using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Manifest;

namespace Jason.Runtime.Plugins.Registry;

/// <summary>What an invocation of this plugin actually runs under, after the manifest met the installation's caps.</summary>
public sealed record EffectivePluginLimits(int TimeoutMs, int MemoryMb);

/// <summary>
/// One plugin as the load left it: the manifest, where it lives, what it hashes to, what the machine answered
/// for its programs, what the user granted, and whether anything holds it back. Immutable, like the snapshot it
/// belongs to — a running invocation is never affected by a later reload, because its envelope was written at
/// launch.
/// </summary>
public sealed record LoadedPlugin(
    PluginManifest Manifest,
    string Root,
    string Digest,
    IReadOnlyList<ResolvedExecutable> Executables,
    ResolvedGrants Grants,
    EffectivePluginLimits Limits,
    PluginStatus Status,
    IReadOnlyList<ManifestProblem> Problems)
{
    public bool Supports(string operation) => Manifest.Operations.Contains(operation, StringComparer.Ordinal);
}
