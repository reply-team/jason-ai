namespace Jason.Runtime.Persistence;

/// <summary>
/// What one plugin calls one of our entities. The pin is written once, by the attempt that learned it, and is
/// never overwritten: a provider that later answers with a different identifier does not get to rewrite history,
/// so the disagreement is recorded beside the original and stays visible until somebody decides which is right.
/// A pin belongs to exactly one entity — a contact or a campaign — and carries no public id of its own, the way
/// <see cref="ContactChannel"/> and <see cref="CampaignContact"/> carry none: nothing addresses a pin from outside.
/// </summary>
public sealed class ExternalId
{
    public int Id { get; set; }

    public int? ContactId { get; set; }

    public Contact? Contact { get; set; }

    public int? CampaignId { get; set; }

    public Campaign? Campaign { get; set; }

    /// <summary>The plugin whose vocabulary the value belongs to: the same person has a different id at each provider.</summary>
    public required string PluginId { get; set; }

    /// <summary>What kind of thing the value names on the provider's side — a contact, a sequence, an account.</summary>
    public required string Kind { get; set; }

    public required string Value { get; set; }

    public DateTime RecordedAt { get; set; }

    /// <summary>The attempt's public id as plain text, the way the journal keeps one: the record outlives the run.</summary>
    public required string RecordedByAttemptId { get; set; }

    /// <summary>A later, different answer for the same pin. Null while the provider keeps agreeing with itself.</summary>
    public string? DivergedValue { get; set; }

    public DateTime? DivergedAt { get; set; }

    public string? DivergedByAttemptId { get; set; }
}
