using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Jason.Contracts.Json;

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

/// <summary>
/// A new profile and its first revision, written together.
/// <para>
/// <c>host</c> is a string and the two lists are nodes, rather than the enum and the string lists they become.
/// Every one of them is a value a person typed, and a caller who typed the wrong thing is owed a field-level
/// refusal that names the field and says what was expected — not a binding failure naming a CLR type.
/// </para>
/// </summary>
public sealed record ProfileCreateRequest(
    string? Name,
    string? Description,
    string? Host,
    string? Program,
    IReadOnlyList<JsonNode?>? Args,
    IReadOnlyList<JsonNode?>? Deny,
    string? CliCommand,
    string? HostVersionVerified,
    ActorRef? Actor,
    string? Reason);

/// <summary>
/// An edit. <c>name</c> says which profile, and is not itself editable: work, campaigns, roles and settings all
/// name a profile by it, so a rename would quietly break every one of them.
/// <para>
/// Every other field is a patch: absent leaves the current revision's value, present replaces it. What the patch
/// does not name is copied forward, so the revision it appends is a whole statement rather than a delta.
/// </para>
/// </summary>
public sealed record ProfileUpdateRequest(
    string? Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Optional<string?> Description,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Optional<string?> Host,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Optional<string?> Program,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Optional<IReadOnlyList<JsonNode?>?> Args,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Optional<IReadOnlyList<JsonNode?>?> Deny,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Optional<string?> CliCommand,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Optional<string?> HostVersionVerified,
    ActorRef? Actor,
    string? Reason);

/// <summary>One profile by name; <c>revision</c> reads an earlier one instead of the one in force.</summary>
public sealed record ProfileGetRequest(string? Name, int? Revision);

/// <summary>Disabled profiles are out of service, so a listing leaves them out unless they are asked for.</summary>
public sealed record ProfileListRequest(bool? IncludeDisabled, int? Limit, string? Cursor);

/// <summary>The body of <c>profile.disable</c> and <c>profile.enable</c>.</summary>
public sealed record ProfileToggleRequest(string? Name, ActorRef? Actor, string? Reason);
