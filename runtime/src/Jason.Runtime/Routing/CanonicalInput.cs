using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Json;
using Jason.Contracts.Operations;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;

namespace Jason.Runtime.Routing;

/// <summary>
/// The document a plugin is handed, composed from the operation's own contract and from nothing else. The
/// planner's arguments travel as written; the person travels only as far as the declared projection reaches, with
/// the one channel the operation consumes rather than their whole reachability; the campaign travels with the
/// identifiers this plugin itself recorded; and the key that makes a repeat answerable is the work item's own,
/// because it is the one thing that is stable across every attempt.
/// </summary>
/// <remarks>
/// Pure, like the pre-flight it serves: it reads the facts and the contract and touches nothing else. Because the
/// projection is a published document rather than a decision taken here, widening what a provider sees of a
/// person is a visible change to a contract instead of an edit nobody reviews.
/// </remarks>
public static class CanonicalInput
{
    /// <summary>The level-1 spelling of a property every call of an operation must carry.</summary>
    private const string Required = "required";

    /// <summary>The one reading of <c>contact_projection.channels</c>: the channel the arguments name, and only it.</summary>
    private const string Consumed = "consumed";

    /// <summary>The arguments key of a contact-facing operation: which of the person's channels the call consumes.</summary>
    private const string ChannelArgument = "channel";

    private static readonly IReadOnlyDictionary<string, Func<Contact, JsonNode?>> Fields =
        new Dictionary<string, Func<Contact, JsonNode?>>(StringComparer.Ordinal)
        {
            ["id"] = contact => JsonValue.Create(contact.PublicId),
            ["first_name"] = contact => Text(contact.FirstName),
            ["last_name"] = contact => Text(contact.LastName),
            ["company"] = contact => Text(contact.Company),
            ["title"] = contact => Text(contact.Title),
            ["time_zone"] = contact => Text(contact.TimeZone),
        };

    /// <summary>Every contact field a contract may project. A published projection naming anything else is a defect.</summary>
    public static IReadOnlyCollection<string> ProjectedFields => (IReadOnlyCollection<string>)Fields.Keys;

    /// <summary>The whole document, in the order the contract's schema describes it.</summary>
    public static JsonObject Compose(OperationContract contract, PreflightFacts facts)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(facts);

        var input = new JsonObject { ["args"] = Arguments(facts.Item) };

        if (contract.Preflight.Contact == ContactRequirement.Required && facts.Contact is { } contact)
        {
            input["contacts"] = new JsonArray(Person(contract, contact, facts.ContactPins, ConsumedChannel(contract, facts.Item)));
        }

        input["campaign"] = Campaign(facts.Campaign, facts.CampaignPins);

        if (string.Equals(contract.IdempotencyKey.Value, Required, StringComparison.Ordinal))
        {
            input["idempotency_key"] = JsonValue.Create(facts.Item.PublicId);
        }

        return input;
    }

    /// <summary>
    /// The arguments as the planner wrote them, copied rather than shared: an edit to the item's context after
    /// the claim belongs to the next attempt, never to the one about to run.
    /// </summary>
    public static JsonNode Arguments(WorkItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.Context[WorkItemService.InputKey]?.DeepClone() ?? new JsonObject();
    }

    /// <summary>
    /// The channel this operation consumes, as the arguments name it, or null where the operation consumes none —
    /// and also where the arguments do not name one, which the input schema itself then refuses by pointer.
    /// </summary>
    public static string? ConsumedChannel(OperationContract contract, WorkItem item)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(item);

        if (contract.Preflight.Channel != ChannelRequirement.FromArgs)
        {
            return null;
        }

        return item.Context[WorkItemService.InputKey] is JsonObject arguments
            && arguments[ChannelArgument] is JsonValue named
            && named.GetValueKind() == JsonValueKind.String
            && named.TryGetValue(out string? channel)
            && !string.IsNullOrEmpty(channel)
            ? channel
            : null;
    }

    /// <summary>
    /// How the person is reachable on that channel. Somebody can be reachable twice on one channel and the
    /// operation acts once, so the primary address wins — the one everything else in the campaign would use.
    /// </summary>
    public static ContactChannel? Reachable(Contact contact, string channel)
    {
        ArgumentNullException.ThrowIfNull(contact);
        ArgumentNullException.ThrowIfNull(channel);

        return contact.Channels
            .Where(candidate => string.Equals(candidate.Channel, channel, StringComparison.Ordinal))
            .OrderByDescending(candidate => candidate.IsPrimary)
            .ThenBy(candidate => candidate.Id)
            .FirstOrDefault();
    }

    private static JsonObject Person(
        OperationContract contract,
        Contact contact,
        IReadOnlyDictionary<string, string> pins,
        string? channel)
    {
        var person = new JsonObject();
        foreach (var field in contract.ContactProjection?.Fields ?? [])
        {
            if (Fields.TryGetValue(field, out var read))
            {
                person[field] = read(contact);
            }
        }

        if (string.Equals(contract.ContactProjection?.Channels, Consumed, StringComparison.Ordinal))
        {
            var reachable = channel is null ? null : Reachable(contact, channel);
            person["channels"] = reachable is null
                ? new JsonArray()
                : new JsonArray(new JsonObject
                {
                    ["channel"] = JsonValue.Create(reachable.Channel),
                    ["value"] = JsonValue.Create(reachable.Value),
                    ["primary"] = JsonValue.Create(reachable.IsPrimary),
                });
        }

        person["external_ids"] = Pins(pins);
        return person;
    }

    private static JsonObject Campaign(Campaign campaign, IReadOnlyDictionary<string, string> pins) => new()
    {
        ["id"] = JsonValue.Create(campaign.PublicId),
        ["name"] = JsonValue.Create(campaign.Name),
        ["status"] = JsonSerializer.SerializeToNode(campaign.Status, JasonJson.Options),
        ["external_ids"] = Pins(pins),
    };

    /// <summary>
    /// One plugin's identifiers for one entity, in a stable order. They are not narrowed to the kinds this
    /// operation may return: what a plugin may be told about an entity is what its input schema states, and a
    /// contract that stated less would refuse the composed document by pointer rather than be quietly obeyed here.
    /// </summary>
    private static JsonObject Pins(IReadOnlyDictionary<string, string> pins)
    {
        var identifiers = new JsonObject();
        foreach (var (kind, value) in pins.OrderBy(pin => pin.Key, StringComparer.Ordinal))
        {
            identifiers[kind] = JsonValue.Create(value);
        }

        return identifiers;
    }

    private static JsonNode? Text(string? value) => value is null ? null : JsonValue.Create(value);
}
