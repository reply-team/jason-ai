using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Jason.Contracts.Json;

namespace Jason.Contracts.Api;

/// <summary>
/// A role is a job description, not a process: the registry says which roles exist and, when one is launchable,
/// what command starts an agent host for it. <c>execution_profile</c> is this role's policy on which host its
/// work uses where the item and its campaign say nothing, and null means it has none of its own.
/// </summary>
public sealed record RoleDto(
    string Id,
    string Name,
    bool Builtin,
    string? Description,
    IReadOnlyList<string> EntryCommand,
    JsonObject ProfileDefaults,
    string? ExecutionProfile,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record RoleListRequest(int? Limit, string? Cursor);

public sealed record RoleAddRequest(
    string? Name,
    IReadOnlyList<string>? EntryCommand,
    JsonObject? ProfileDefaults,
    string? Description,
    string? ExecutionProfile,
    ActorRef? Actor,
    string? Reason);

/// <summary>
/// The one thing a registered role can be told to change. Roles have no general update verb: which host a kind
/// of worker uses is a decision somebody revisits, and the rest of a job description is not. An absent
/// <c>execution_profile</c> leaves the policy alone, an explicit null takes it away.
/// </summary>
public sealed record RoleSetProfileRequest(
    string? Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Optional<string?> ExecutionProfile,
    ActorRef? Actor,
    string? Reason);
