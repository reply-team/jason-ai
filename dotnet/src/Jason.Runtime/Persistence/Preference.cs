using System.Text.Json.Nodes;
using Jason.Contracts.Api;

namespace Jason.Runtime.Persistence;

/// <summary>
/// A named preference of the client: a structured fact with a schema of its own, such as who messages are written as, so
/// that a skill can check that it is set and a screen can show it. One row per key, at the client level only; a project
/// or a campaign that needs something different says so in its own words, never in a row here.
/// </summary>
/// <remarks>
/// The value is the person's, and it can be taken back: unsetting a key deletes its row. The chronicle names a value by
/// its size and hash only, because it could never forget the value itself.
/// </remarks>
public sealed class Preference
{
    public int Id { get; set; }

    /// <summary>The preference's name, as its catalog document spells it.</summary>
    public required string Key { get; set; }

    /// <summary>
    /// The value exactly as it was set: a JSON object, as the key's schema in the catalog holds it to be. The database
    /// itself holds only that it is JSON.
    /// </summary>
    public required JsonObject Value { get; set; }

    /// <summary>The canonical <c>sha256:</c> of <see cref="Value"/>.</summary>
    public required string ValueHash { get; set; }

    /// <summary>The size of the value's canonical form.</summary>
    public long ValueBytes { get; set; }

    /// <summary>Who last set it, as they claimed to be.</summary>
    public ActorType UpdatedByType { get; set; }

    public string? UpdatedById { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
