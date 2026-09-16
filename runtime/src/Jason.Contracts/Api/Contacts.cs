using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Jason.Contracts.Json;

namespace Jason.Contracts.Api;

/// <summary>One way to reach a contact. The vocabulary of channels is open; <c>value</c> is always the normalized form.</summary>
public sealed record ChannelDto(string Channel, string Value, string? Label, bool Primary, JsonObject? Data);

/// <summary>
/// One contact as the API answers with it. <c>external_ids</c> is always a list, possibly empty: an absent field
/// and an empty list would read alike to a client, and one of the two would be a lie about what is known.
/// </summary>
public sealed record ContactDto(
    string Id,
    string? FirstName,
    string? LastName,
    string? Company,
    string? Title,
    string? TimeZone,
    IReadOnlyList<ChannelDto> Channels,
    JsonObject Custom,
    IReadOnlyList<ExternalIdDto> ExternalIds,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ArchivedAt);

/// <summary>A channel as submitted: the runtime normalizes and validates it before storing.</summary>
public sealed record ChannelInput(string? Channel, string? Value, string? Label, bool? Primary, JsonObject? Data);

/// <summary>The contact fields any operation may submit; shared by <c>contact.create</c> and the items of <c>campaign.add_contacts</c>.</summary>
public record ContactInput(
    string? FirstName,
    string? LastName,
    string? Company,
    string? Title,
    string? TimeZone,
    IReadOnlyList<ChannelInput>? Channels,
    JsonObject? Custom);

public sealed record ContactCreateRequest(
    string? FirstName,
    string? LastName,
    string? Company,
    string? Title,
    string? TimeZone,
    IReadOnlyList<ChannelInput>? Channels,
    JsonObject? Custom,
    ActorRef? Actor,
    string? Reason)
    : ContactInput(FirstName, LastName, Company, Title, TimeZone, Channels, Custom);

public sealed record ContactGetRequest(string? ContactId);

/// <summary><c>value</c> without <c>channel</c> is a usage error; <c>channel</c> alone lists contacts reachable on that channel.</summary>
public sealed record ContactListRequest(string? Channel, string? Value, bool? IncludeArchived, int? Limit, string? Cursor);

/// <summary>
/// A partial patch: absent fields stay as they are, an explicit null clears one. <c>channels</c> and <c>custom</c>
/// are replaced wholesale — the caller states the desired state rather than merging.
/// </summary>
public sealed record ContactUpdateRequest(
    string? ContactId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Optional<string?> FirstName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Optional<string?> LastName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Optional<string?> Company,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Optional<string?> Title,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Optional<string?> TimeZone,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Optional<IReadOnlyList<ChannelInput>?> Channels,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Optional<JsonObject?> Custom,
    ActorRef? Actor,
    string? Reason);

public sealed record ContactArchiveRequest(string? ContactId, ActorRef? Actor, string? Reason);
