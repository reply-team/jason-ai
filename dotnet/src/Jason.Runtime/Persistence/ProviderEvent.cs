using System.Text.Json.Nodes;

namespace Jason.Runtime.Persistence;

/// <summary>
/// One event a provider said happened in an account, kept as it was read: its kind, when it happened, the person it
/// names where one contact is pinned with the identifier the provider gave, and the event itself — identifiers, moments
/// and the provider's own flags, nothing anybody wrote. Append-only at three levels, as the journal is: no API operation
/// changes or removes one, this type is init-only, and the database's triggers abort an UPDATE or a DELETE.
/// </summary>
/// <remarks>
/// An event a read stores twice — the same page read again after a crash, a range read again by a person's request — is
/// stored once: its <see cref="Key"/> is unique, and a page whose events the store already holds adds nothing. Which
/// campaigns an event concerns is kept beside it, one <see cref="EventAttribution"/> each, and an event no campaign's
/// pins match is kept all the same.
/// </remarks>
public sealed class ProviderEvent
{
    public int Id { get; init; }

    /// <summary><c>evt_</c> + ULID.</summary>
    public required string PublicId { get; init; }

    public int SourceId { get; init; }

    public EventSource? Source { get; init; }

    /// <summary>The plugin whose vocabulary the event's identifiers belong to.</summary>
    public required string PluginId { get; init; }

    public required string Kind { get; init; }

    /// <summary>When it happened, as the provider stamps it.</summary>
    public DateTime OccurredAt { get; init; }

    /// <summary>The runtime's clock when the page that held it was stored.</summary>
    public DateTime StoredAt { get; init; }

    /// <summary>
    /// <c>sha256:</c> over the plugin, the kind, the kind's identifiers and the moment, and who moved a moved thread: the
    /// same event, read twice, is one.
    /// </summary>
    public required string Key { get; init; }

    public int? ContactId { get; init; }

    public Contact? Contact { get; init; }

    /// <summary>The event as the events contract's output schema accepted it.</summary>
    public required JsonObject Data { get; init; }

    /// <summary>The public id of the read that stored it, as plain text: a record of where it came from, not a reference.</summary>
    public string? ReadId { get; init; }
}
