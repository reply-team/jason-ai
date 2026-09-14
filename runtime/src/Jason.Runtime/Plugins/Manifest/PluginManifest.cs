using Jason.Contracts.Plugins;

namespace Jason.Runtime.Plugins.Manifest;

/// <summary>
/// One program a plugin declares it needs. The name is what <c>host.exec</c> may start; the version command is
/// the only thing a reload will run on the user's machine, and only for an executable the user granted.
/// </summary>
public sealed record ExecutableRequest(string Name, string? MinVersion, IReadOnlyList<string>? VersionCommand);

public sealed record ExecRequestSpec(IReadOnlyList<ExecutableRequest> Executables);

public sealed record HttpRequestSpec(IReadOnlyList<string> Hosts);

public sealed record EnvRequestSpec(IReadOnlyList<string> Variables);

/// <summary>What the manifest asks for. Asking is not being given it: the grants decide that, at every reload.</summary>
public sealed record CapabilityRequests(ExecRequestSpec? Exec, HttpRequestSpec? Http, EnvRequestSpec? Env);

public sealed record ManifestContracts(IReadOnlyList<int> Protocol, IReadOnlyList<int> Operations);

/// <summary>What the plugin asks its invocations to run under; null means "whatever the installation decides".</summary>
public sealed record ManifestLimits(int? TimeoutMs, int? MemoryMb);

/// <summary>A <c>plugin.yaml</c> that passed every rule, as the rest of the runtime reads it.</summary>
public sealed record PluginManifest(
    string Id,
    string Version,
    PluginKind Kind,
    string? Name,
    string? Description,
    string? Homepage,
    ManifestContracts Contracts,
    IReadOnlyList<string> Operations,
    PluginEntry Entry,
    CapabilityRequests Capabilities,
    ManifestLimits Limits);

/// <summary>The ceilings of this installation, which a manifest may approach but never raise.</summary>
public sealed record ManifestBounds(int MaxTimeoutMs, int MaxMemoryMb);

/// <summary>
/// What one read made of a manifest. Every problem is reported, not just the first, so an author fixes the file
/// once; a manifest with any problem at all yields no manifest.
/// </summary>
public sealed record ManifestReadResult(PluginManifest? Manifest, IReadOnlyList<ManifestProblem> Problems)
{
    public bool IsValid => Manifest is not null && Problems.Count == 0;
}
