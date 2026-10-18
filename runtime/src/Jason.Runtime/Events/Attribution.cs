using Jason.Runtime.Persistence;

namespace Jason.Runtime.Events;

/// <summary>One campaign an event was found to concern, by the rule that found it.</summary>
public sealed record Attributed(int CampaignId, string Rule);

/// <summary>A campaign holding a provider's identifier pinned at the plugin a page was read from.</summary>
public sealed record CampaignPin(int CampaignId, string Value);

/// <summary>A contact holding a provider's identifier pinned at the plugin a page was read from.</summary>
public sealed record ContactPin(int ContactId, string ContactPublicId, string Value);

/// <summary>
/// The pins a page's events can be found by: those of the plugin the page was read from, for the identifiers the page
/// names, read once before anything is written. Campaigns that are archived are not among them — what is over is told
/// nothing — and a value kept only as a divergence beside a pin is not a pin.
/// </summary>
public sealed record PinFacts(IReadOnlyList<CampaignPin> Campaigns, IReadOnlyList<ContactPin> Contacts)
{
    /// <summary>The person an event names: the one contact pinned with the identifier the provider gave, and nobody when none is or more than one is.</summary>
    public ContactPin? ContactOf(ReadEvent read)
    {
        ArgumentNullException.ThrowIfNull(read);
        if (read.Contact is not { } value)
        {
            return null;
        }

        var pinned = Contacts.Where(pin => string.Equals(pin.Value, value, StringComparison.Ordinal)).Take(2).ToList();
        return pinned.Count == 1 ? pinned[0] : null;
    }
}

/// <summary>
/// Which campaigns an event concerns. A list, so that an event may concern several; deterministic, reading identifiers
/// and pins and nothing an event says about anything. A list that names one campaign twice — two rules that both find it
/// — is one campaign to the store, which keeps the first.
/// </summary>
public interface IEventAttribution
{
    IReadOnlyList<Attributed> Attribute(ReadEvent read, PinFacts pins);
}

/// <summary>
/// The rule of this version: an event reaches a campaign by the provider's campaign identifier it names, when exactly one
/// campaign holds that identifier pinned at the event's plugin — and by nothing else: not a route, not a membership, not
/// a tag, not a name. None or more than one, and the event is kept, concerning nobody.
/// </summary>
public sealed class CampaignPinAttribution : IEventAttribution
{
    public IReadOnlyList<Attributed> Attribute(ReadEvent read, PinFacts pins)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(pins);
        if (read.Campaign is not { } value)
        {
            return [];
        }

        var pinned = pins.Campaigns
            .Where(pin => string.Equals(pin.Value, value, StringComparison.Ordinal))
            .Select(pin => pin.CampaignId)
            .Distinct()
            .Take(2)
            .ToList();
        return pinned.Count == 1 ? [new Attributed(pinned[0], EventAttributionRules.CampaignPin)] : [];
    }
}
