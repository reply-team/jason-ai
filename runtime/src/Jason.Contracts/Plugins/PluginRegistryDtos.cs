using Jason.Contracts.Api;

namespace Jason.Contracts.Plugins;

/// <summary>One thing wrong with a candidate, named where it is wrong: <c>plugin.yaml#capabilities.exec</c>.</summary>
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
    PluginStatus Status,
    IReadOnlyList<PluginProblemDto> Problems);

public sealed record SnapshotDto(string Id, DateTimeOffset LoadedAt, SnapshotSource Source, int PluginCount);

public sealed record CandidateDto(string Directory, string? Id, CandidateStatus Status, IReadOnlyList<PluginProblemDto> Problems);

/// <summary>What the last load made of every candidate, whether or not it ended in a swap.</summary>
public sealed record ReloadReportDto(DateTimeOffset At, SnapshotSource Source, bool Activated, IReadOnlyList<CandidateDto> Candidates);

/// <summary>The whole registry: a small thing, answered in one piece and never paged.</summary>
public sealed record PluginRegistryDto(SnapshotDto Snapshot, IReadOnlyList<PluginDto> Plugins, ReloadReportDto? LastReload, bool Activated);

public sealed record PluginListRequest();

public sealed record PluginReloadRequest(ActorRef? Actor, string? Reason);
