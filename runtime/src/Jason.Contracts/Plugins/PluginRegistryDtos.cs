using System.Text.Json.Nodes;
using Jason.Contracts.Api;

namespace Jason.Contracts.Plugins;

/// <summary>
/// One thing wrong with a candidate, named where it is wrong. <c>Path</c> is the place inside the manifest
/// (<c>kind</c>, <c>capabilities.exec.executables[0].name</c>) with no file name in front of it — the only
/// exception is a YAML syntax error, which has no field to point at and reads <c>plugin.yaml#line:column</c>.
/// Whoever renders a problem adds the package directory and the file once, so it is never named twice.
/// </summary>
public sealed record PluginProblemDto(string Code, string Path, string Message);

/// <summary>
/// A declared executable as the machine answered for it: the path it resolved to, the version it reported when
/// the user granted it (an ungranted one is never run, so its version stays null), and the minimum asked for.
/// </summary>
public sealed record ExecutableDto(string Name, string? Path, string? Version, string? MinVersion);

public sealed record ExecCapabilityDto(IReadOnlyList<ExecutableDto> Requested, IReadOnlyList<string> Granted);

public sealed record ListCapabilityDto(IReadOnlyList<string> Requested, IReadOnlyList<string> Granted);

/// <summary>Requested against granted, for every capability: declaration is not permission, and it shows.</summary>
public sealed record PluginCapabilitiesDto(ExecCapabilityDto? Exec, ListCapabilityDto? Http, ListCapabilityDto? Env);

public sealed record PluginContractsDto(IReadOnlyList<int> Protocol, IReadOnlyList<int> Operations);

public sealed record PluginLimitsDto(int TimeoutMs, int MemoryMb);

/// <summary>
/// One plugin as the registry answers for it. <c>BindingSchema</c> is the schema a route to this plugin must
/// satisfy, exactly as the manifest declared it, so an operator can see what a route has to carry before writing
/// one; it is null when the plugin asks a route for nothing.
/// </summary>
public sealed record PluginDto(
    string Id,
    string Version,
    PluginKind Kind,
    string? Name,
    string? Description,
    string? Homepage,
    string Root,
    string Digest,
    PluginContractsDto Contracts,
    IReadOnlyList<string> Operations,
    PluginEntry Entry,
    PluginCapabilitiesDto Capabilities,
    PluginLimitsDto Limits,
    JsonObject? BindingSchema,
    PluginStatus Status,
    IReadOnlyList<PluginProblemDto> Problems);

public sealed record SnapshotDto(string Id, DateTimeOffset LoadedAt, SnapshotSource Source, int PluginCount);

public sealed record CandidateDto(string Directory, string? Id, CandidateStatus Status, IReadOnlyList<PluginProblemDto> Problems);

/// <summary>
/// One route the last load refused, named the way an operator edits it: <c>Routes:Default</c>,
/// <c>Routes:Operations:&lt;operation&gt;</c> or <c>campaign:&lt;cmp_id&gt;/routes/&lt;operation|default&gt;</c>.
/// </summary>
public sealed record RouteProblemDto(string Route, string Code, string Message);

/// <summary>
/// What the last load made of every candidate, whether or not it ended in a swap. <c>Routes</c> is its own list
/// rather than a candidate named "routes": a candidate is a package, and a reader of this report should not have
/// to know that one entry in a list of packages is a different kind of thing.
/// </summary>
public sealed record ReloadReportDto(
    DateTimeOffset At,
    SnapshotSource Source,
    bool Activated,
    IReadOnlyList<CandidateDto> Candidates,
    IReadOnlyList<RouteProblemDto> Routes);

/// <summary>
/// The whole registry: a small thing, answered in one piece and never paged. <c>RoutingSnapshotId</c> is the
/// route snapshot that was frozen against this plugin snapshot — the two travel together, because which plugin
/// performs an operation is only answerable from both.
/// </summary>
public sealed record PluginRegistryDto(
    SnapshotDto Snapshot,
    string RoutingSnapshotId,
    IReadOnlyList<PluginDto> Plugins,
    ReloadReportDto? LastReload,
    bool Activated);

public sealed record PluginListRequest();

public sealed record PluginReloadRequest(ActorRef? Actor, string? Reason);
