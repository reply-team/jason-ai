using Jason.Contracts.Api;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Events;

/// <summary>One campaign an event was found to concern, by the rule that found it.</summary>
/// <param name="CampaignId">The campaign.</param>
/// <param name="Rule">One of <see cref="EventAttributionRules"/>.</param>
/// <param name="SaidFrom">
/// The moment before which the event is history to the campaign beside its own <c>events_from</c>: when the person the
/// contact rule found it by joined the campaign. Null for the campaign rule.
/// </param>
public sealed record Attributed(int CampaignId, string Rule, DateTime? SaidFrom = null);

/// <summary>A campaign holding a provider's identifier pinned at the plugin a page was read from.</summary>
public sealed record CampaignPin(int CampaignId, string Value);

/// <summary>A contact holding a provider's identifier pinned at the plugin a page was read from.</summary>
public sealed record ContactPin(int ContactId, string ContactPublicId, string Value);

/// <summary>
/// A contact's local membership — any state but <c>excluded</c> — in a campaign that is not archived, of a contact that
/// is not archived, and since when.
/// </summary>
public sealed record ContactMembership(int ContactId, int CampaignId, DateTime AddedAt);

/// <summary>
/// The pins a page's events can be found by: those of the plugin the page was read from, for the identifiers the page
/// names, and the local memberships of the contacts they name, read once before anything is written. Campaigns that are
/// archived are not among them — what is over is told nothing — and a value kept only as a divergence beside a pin is
/// not a pin.
/// </summary>
public sealed record PinFacts(IReadOnlyList<CampaignPin> Campaigns, IReadOnlyList<ContactPin> Contacts, IReadOnlyList<ContactMembership> Members)
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
/// The campaign rule: an event reaches a campaign by the provider's campaign identifier it names, when exactly one
/// campaign holds that identifier pinned at the event's plugin — and by nothing else: not a route, not a tag, not a name.
/// None or more than one, and this rule finds nobody.
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

        return CampaignPinRule.SoleHolder(pins.Campaigns, value) is { } campaign ? [new Attributed(campaign, EventAttributionRules.CampaignPin)] : [];
    }
}

/// <summary>
/// The campaign-pin rule in its two halves: which campaigns hold a provider campaign's identifier pinned at a plugin, and
/// which one of them, if exactly one does, an event naming it reaches. One rule, read by the store when it attributes an
/// event and by the inbound read when it decides whether a campaign can be left to the events, so the two cannot come to
/// disagree about which campaign an event would reach.
/// </summary>
public static class CampaignPinRule
{
    /// <summary>The kind of pin that names a campaign at a plugin.</summary>
    public const string Kind = "campaign";

    /// <summary>
    /// The campaigns holding any of these values pinned at the plugin. A campaign that is archived is told nothing and is
    /// not among them, and a value kept only as a divergence beside a pin is not a pin.
    /// </summary>
    public static async Task<List<CampaignPin>> HoldersAsync(JasonDbContext db, string pluginId, IReadOnlyCollection<string> values, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            return [];
        }

        return await db.ExternalIds
            .AsNoTracking()
            .Where(pin => pin.PluginId == pluginId
                && pin.Kind == Kind
                && pin.CampaignId != null
                && values.Contains(pin.Value)
                && pin.Campaign!.Status != CampaignStatus.Archived)
            .Select(pin => new CampaignPin(pin.CampaignId!.Value, pin.Value))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Whether an event naming the campaign's provider campaign at the plugin would reach it: the campaign is the one, of
    /// those not archived, holding that identifier pinned there. The inbound read asks this before it leaves a campaign
    /// to the events; the store attributes by the same two halves.
    /// </summary>
    public static async Task<bool> ReachesAsync(JasonDbContext db, string pluginId, int campaignId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var values = await db.ExternalIds
            .AsNoTracking()
            .Where(pin => pin.CampaignId == campaignId && pin.PluginId == pluginId && pin.Kind == Kind)
            .Select(pin => pin.Value)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var holders = await HoldersAsync(db, pluginId, values, ct).ConfigureAwait(false);
        return values.Any(value => SoleHolder(holders, value) == campaignId);
    }

    /// <summary>The one campaign holding the value among these pins, or null when none does or more than one does.</summary>
    public static int? SoleHolder(IReadOnlyList<CampaignPin> pins, string value)
    {
        ArgumentNullException.ThrowIfNull(pins);
        var pinned = pins
            .Where(pin => string.Equals(pin.Value, value, StringComparison.Ordinal))
            .Select(pin => pin.CampaignId)
            .Distinct()
            .Take(2)
            .ToList();
        return pinned.Count == 1 ? pinned[0] : null;
    }
}

/// <summary>
/// The contact rule: an event reaches every campaign that is not archived and holds, as a local member, the one contact
/// pinned with the event's contact identifier at the event's plugin — each said from when the person joined it. Nothing
/// else about the person counts: a contact pinned at another plugin, a value kept as a divergence, an identifier two
/// contacts are pinned with, an excluded membership and an archived contact all find nobody.
/// </summary>
public sealed class ContactPinAttribution : IEventAttribution
{
    public IReadOnlyList<Attributed> Attribute(ReadEvent read, PinFacts pins)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(pins);
        if (pins.ContactOf(read) is not { } contact)
        {
            return [];
        }

        return
        [
            .. pins.Members
                .Where(member => member.ContactId == contact.ContactId)
                .OrderBy(member => member.CampaignId)
                .Select(member => new Attributed(member.CampaignId, EventAttributionRules.ContactPin, member.AddedAt)),
        ];
    }
}

/// <summary>
/// Rules asked in their order, the first that finds any campaign deciding alone: a later rule is asked only where every
/// rule before it found nobody, so it is never a second audience beside a campaign already found.
/// </summary>
public sealed class EventAttributionSequence(params IEventAttribution[] rules) : IEventAttribution
{
    /// <summary>
    /// The rules of this version: the campaign rule, then the contact rule. A sequence's event is the campaign's that runs
    /// the sequence, as the inbound read, which lists a campaign's own threads, holds it; the person's campaigns are reached
    /// by an event that names no provider campaign, or one no live campaign or more than one holds.
    /// </summary>
    public static EventAttributionSequence OfThisVersion() => new(new CampaignPinAttribution(), new ContactPinAttribution());

    public IReadOnlyList<Attributed> Attribute(ReadEvent read, PinFacts pins)
    {
        foreach (var rule in rules)
        {
            var found = rule.Attribute(read, pins);
            if (found.Count > 0)
            {
                return found;
            }
        }

        return [];
    }
}
