namespace Jason.Contracts.Api;

/// <summary>
/// One complete statement of how a host is launched, as it was written. Revisions are numbered from 1 and never
/// change, so an attempt that names revision 3 means the same thing a year later.
/// </summary>
public sealed record ExecutionProfileRevisionDto(
    int Number,
    AgentHostKind Host,
    string Program,
    IReadOnlyList<string> Args,
    IReadOnlyList<string> Deny,
    string CliCommand,
    string? HostVersionVerified,
    DateTimeOffset CreatedAt);

/// <summary>
/// A named, non-secret description of a launchable agent host. <c>revision</c> is the one in force; an earlier
/// one is fetched by asking for it. There is no field for a credential: the host is already authenticated by
/// whoever installed it.
/// </summary>
public sealed record ExecutionProfileDto(
    string Id,
    string Name,
    string? Description,
    bool Disabled,
    int CurrentRevision,
    ExecutionProfileRevisionDto Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
