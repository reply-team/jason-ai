using System.Text.Json.Nodes;
using Jason.Contracts.Api;

namespace Jason.Runtime.Persistence;

/// <summary>
/// One line of the chronicle. The table is append-only at three levels — no API operation rewrites an entry,
/// this type is init-only so nothing in the runtime can change one after insert, and the database has triggers
/// that abort any UPDATE or DELETE.
/// </summary>
public sealed class JournalEntry
{
    public int Id { get; init; }

    /// <summary><c>jrn_</c> + ULID; monotonic, so entries of the same millisecond still read in order.</summary>
    public required string PublicId { get; init; }

    /// <summary>The runtime clock at insert, millisecond precision.</summary>
    public DateTime Ts { get; init; }

    public ActorType ActorType { get; init; }

    public string? ActorId { get; init; }

    public required string Kind { get; init; }

    /// <summary>Null for global entries such as suppression changes.</summary>
    public int? CampaignId { get; init; }

    public Campaign? Campaign { get; init; }

    /// <summary>The PUBLIC id of the work item the entry is about; plain text, indexed, deliberately not a foreign key.</summary>
    public string? WorkItemId { get; init; }

    /// <summary>The PUBLIC id of the attempt the entry is about.</summary>
    public string? AttemptId { get; init; }

    public string? Key { get; init; }

    public JsonNode? Old { get; init; }

    public JsonNode? New { get; init; }

    public string? Reason { get; init; }
}
