using System.Text.Json.Nodes;

namespace Jason.Contracts.Api;

/// <summary>
/// One named preference of the client, as the catalog under <c>docs/contracts/preferences/</c> names it. Where the key is
/// not set, <c>value</c> is null and so is everything said about it: the hash, the size, the moments and who set it. A
/// value is the person's word, so <c>updated_by</c> always names a person.
/// </summary>
public sealed record PreferenceDto(
    string Key,
    JsonObject? Value,
    string? ValueHash,
    long? ValueBytes,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt,
    ActorRef? UpdatedBy);

/// <summary>
/// Sets a key's value whole, checked against the key's schema. Only a person sets a preference, and the request names
/// them: an absent actor is refused, and so is one that is not a person.
/// </summary>
public sealed record PreferenceSetRequest(string? Key, JsonNode? Value, ActorRef? Actor, string? Reason);

/// <summary>Removes a key's value, so that it is gone rather than kept beside a newer one. A person's act, as setting it is.</summary>
public sealed record PreferenceUnsetRequest(string? Key, ActorRef? Actor, string? Reason);

/// <summary>Every key the catalog knows, by key, set or not: what is missing is read in the same call as what is there.</summary>
public sealed record PreferenceListRequest(int? Limit, string? Cursor);
