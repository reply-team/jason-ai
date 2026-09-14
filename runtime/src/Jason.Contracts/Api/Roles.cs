using System.Text.Json.Nodes;

namespace Jason.Contracts.Api;

/// <summary>
/// A role is a job description, not a process: the registry says which roles exist and, when one is launchable,
/// what command starts an agent host for it.
/// </summary>
public sealed record RoleDto(
    string Id,
    string Name,
    bool Builtin,
    string? Description,
    IReadOnlyList<string> EntryCommand,
    JsonObject ProfileDefaults,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record RoleListRequest(int? Limit, string? Cursor);

public sealed record RoleAddRequest(
    string? Name,
    IReadOnlyList<string>? EntryCommand,
    JsonObject? ProfileDefaults,
    string? Description,
    ActorRef? Actor,
    string? Reason);
