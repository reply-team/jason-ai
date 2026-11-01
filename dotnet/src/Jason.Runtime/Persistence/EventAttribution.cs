namespace Jason.Runtime.Persistence;

/// <summary>
/// That one stored event concerns one campaign, and by which rule it was found to: a row per campaign, so an event may
/// concern several. Decided once, when the event is stored, by the pins as they stood then; nothing re-attributes an
/// event later. Append-only, as the event it belongs to is.
/// </summary>
public sealed class EventAttribution
{
    public int Id { get; init; }

    public int EventId { get; init; }

    public ProviderEvent? Event { get; init; }

    public int CampaignId { get; init; }

    public Campaign? Campaign { get; init; }

    /// <summary>One of <see cref="EventAttributionRules"/>: a word the code holds to its list and the schema does not.</summary>
    public required string Rule { get; init; }
}

/// <summary>The rules an event is found to concern a campaign by.</summary>
public static class EventAttributionRules
{
    /// <summary>The event names the provider's campaign, and exactly one campaign holds that identifier pinned at the event's plugin.</summary>
    public const string CampaignPin = "campaign_pin";

    /// <summary>
    /// The campaign rule found nobody, and the campaign holds, as a local member, the one contact pinned with the event's
    /// contact identifier at the event's plugin.
    /// </summary>
    public const string ContactPin = "contact_pin";
}
