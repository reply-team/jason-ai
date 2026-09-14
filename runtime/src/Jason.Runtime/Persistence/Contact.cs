using System.Text.Json.Nodes;

namespace Jason.Runtime.Persistence;

/// <summary>
/// A person the campaigns reach out to. No natural field is unique: an email is neither mandatory nor an
/// identity, and a contact with no channel at all is a legitimate research-stage lead.
/// </summary>
public sealed class Contact
{
    public int Id { get; set; }

    /// <summary><c>cnt_</c> + ULID.</summary>
    public required string PublicId { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? Company { get; set; }

    public string? Title { get; set; }

    /// <summary>IANA time-zone id, validated on write.</summary>
    public string? TimeZone { get; set; }

    /// <summary>Whatever else the caller knows about the contact; schema-less by design.</summary>
    public JsonObject Custom { get; set; } = new();

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? ArchivedAt { get; set; }

    public List<ContactChannel> Channels { get; } = [];
}

/// <summary>One way to reach a contact. A child table rather than a JSON array: matching and suppression look up (channel, value).</summary>
public sealed class ContactChannel
{
    public int Id { get; set; }

    public int ContactId { get; set; }

    public Contact? Contact { get; set; }

    public required string Channel { get; set; }

    /// <summary>Normalized canonical address in the channel: the only thing matching and suppression operate on.</summary>
    public required string Value { get; set; }

    public string? Label { get; set; }

    public bool IsPrimary { get; set; }

    public JsonObject? Data { get; set; }
}
