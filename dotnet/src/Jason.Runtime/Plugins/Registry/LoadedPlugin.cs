using System.Collections.Frozen;
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
    /// <summary>What the settings said under <c>Plugins:Config:&lt;id&gt;</c> at this load: a key to its text, matched as a configuration key is.</summary>
    public IReadOnlyDictionary<string, string> Config { get; init; } = FrozenDictionary<string, string>.Empty;

    public bool Supports(string operation) => Manifest.Operations.Contains(operation, StringComparer.Ordinal);
}
