using Jason.Runtime.Persistence;

namespace Jason.Runtime.Routing;

/// <summary>
/// Everything a provider work item's pre-flight decides on, read once inside the claim transaction. The decision
/// itself is then a pure function over this record: no database, no clock, no options — which is what lets the
/// order of the checks be a unit test, and what keeps the claim to a handful of indexed reads.
/// </summary>
/// <param name="Item">The work item as it stands at claim, with its context — the arguments come from there.</param>
/// <param name="Campaign">The campaign the item belongs to, loaded rather than reached through the navigation.</param>
/// <param name="Contact">The person the item is about, with their channels, or null where the item names nobody.</param>
/// <param name="ContactPins">
/// The routed plugin's own identifiers for that person, as <c>{kind: value}</c>. Another plugin's identifiers are
/// never here: what one provider calls somebody is not another provider's business.
/// </param>
/// <param name="CampaignPins">The same for the campaign.</param>
/// <param name="Suppressed">
/// The verdict, not the list: whether the value of the channel this operation consumes is on the do-not-contact
/// register. A pre-flight that carried the list would have to query, and then it could not be a pure function.
/// </param>
public sealed record PreflightFacts(
    WorkItem Item,
    Campaign Campaign,
    Contact? Contact,
    IReadOnlyDictionary<string, string> ContactPins,
    IReadOnlyDictionary<string, string> CampaignPins,
    bool Suppressed)
{
    /// <summary>What an entity no plugin has pinned yet has: nothing, stated once rather than allocated per claim.</summary>
    public static IReadOnlyDictionary<string, string> NoPins { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
}
