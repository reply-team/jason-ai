using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Json;
using Jason.Contracts.Operations;
using Jason.Runtime.Json;
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

    /// <summary>
    /// The key, in the arguments and at the root alike, of the send an operation acts on by its reference: the caller names
    /// the work item under <c>args.send</c>, and the runtime composes the send under <c>send</c>.
    /// </summary>
    private const string SendKey = "send";

    /// <summary>
    /// The key, in the arguments and at the root alike, of the search an operation acts on by its reference: the caller
    /// names the work item under <c>args.search</c>, and the runtime composes the search under <c>search</c>.
    /// </summary>
    private const string SearchKey = "search";

    /// <summary>
    /// The root key of an audience's definition, the provider's own shape: the runtime composes it from
    /// <c>args.definition</c>, or from the audience <c>args.audience</c> names, and the plugin reads it there and nowhere
    /// else.
    /// </summary>
    public const string DefinitionKey = "definition";

    /// <summary>The arguments key of an audience the campaign holds, named by its number.</summary>
    private const string AudienceKey = "audience";

    /// <summary>The member of a composed search where the next status read starts, where the document declares one.</summary>
    private const string CursorKey = "cursor";

    private const string RefPrefix = "#/$defs/";

    /// <summary>The root key of the campaign the work belongs to.</summary>
    private const string CampaignKey = "campaign";

    /// <summary>The root key of the people the work is about: the one person, where the operation reads one.</summary>
    private const string ContactsKey = "contacts";

    /// <summary>The key of an entity's pins, under the person and the campaign alike.</summary>
    private const string ExternalIdsKey = "external_ids";

    /// <summary>The campaign's status, the one member of the composed input its digest leaves out.</summary>
    private const string CampaignStatusKey = "status";

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

        // The send is the runtime's to name, from the attempt that made it: nothing the caller wrote is read for its
        // reference, and a send the claim did not find travels as none, which the schema then refuses.
        if (NamesASend(contract) && facts.Send is { Reference: { } reference } send)
        {
            input[SendKey] = new JsonObject
            {
                ["work_item"] = JsonValue.Create(send.WorkItem),
                ["operation"] = JsonValue.Create(send.Operation),
                ["reference"] = JsonValue.Create(reference),
            };
        }

        // The search the same way, and where the place a status read starts belongs to the document, that too: the
        // plugin's own cursor, which the runtime stores and never parses, and null where it keeps none yet.
        if (NamesASearch(contract) && facts.Search is { Reference: { } searched } search)
        {
            var composed = new JsonObject
            {
                ["work_item"] = JsonValue.Create(search.WorkItem),
                ["reference"] = JsonValue.Create(searched),
            };
            if (Declares(contract, SearchKey, CursorKey))
            {
                composed[CursorKey] = search.Cursor is null ? null : JsonValue.Create(search.Cursor);
            }

            input[SearchKey] = composed;
        }

        // The definition is the runtime's to place, at the root, so the plugin reads it in one place whichever way the
        // work named it: as written, or as the audience it names by number was searched under.
        if (CarriesADefinition(contract) && Definition(input["args"], facts.Audience) is { } definition)
        {
            input[DefinitionKey] = definition;
        }

        // A required contact is always here by the time anything is composed — the pre-flight refused the item
        // otherwise — and an optional one travels exactly when the work item names somebody.
        if (contract.Preflight.Contact != ContactRequirement.NotUsed && facts.Contact is { } contact)
        {
            input[ContactsKey] = new JsonArray(Person(contract, contact, facts.ContactPins, ConsumedChannel(contract, facts.Item)));
        }

        input[CampaignKey] = Campaign(facts.Campaign, facts.CampaignPins);

        if (string.Equals(contract.IdempotencyKey.Value, Required, StringComparison.Ordinal))
        {
            input["idempotency_key"] = JsonValue.Create(facts.Item.PublicId);
        }

        return input;
    }

    /// <summary>
    /// What a composed input is called: the canonical <c>sha256:</c> of the document, keys sorted in ordinal order, so the
    /// same members in another order are the same name. A claim records it on the attempt it lets run, and a repeat after
    /// a lost answer is the same request only under the same name.
    /// </summary>
    /// <remarks>
    /// The campaign's status is left out of the name, and nothing else is. A pause and a resume between the attempt that
    /// lost its answer and its repeat ask nothing else of the provider; another address, another field of the person, a
    /// pin added, a campaign renamed or an argument edited does, and a key that settles only the request it went with
    /// would answer such a repeat with the send the first request made.
    /// </remarks>
    public static string Digest(JsonObject input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var named = input.DeepClone().AsObject();
        if (named[CampaignKey] is JsonObject campaign)
        {
            campaign.Remove(CampaignStatusKey);
        }

        return CanonicalJson.Hash(named);
    }

    /// <summary>
    /// A composed input without the pins one attempt <paramref name="learned"/>: what the same facts composed before that
    /// attempt recorded them, since a pin is written once, by the attempt that learned it, and every pin travels in the
    /// input under the kind it is known by.
    /// </summary>
    public static JsonObject Without(JsonObject input, PinsLearned learned)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(learned);

        var before = input.DeepClone().AsObject();
        if (before[ContactsKey] is JsonArray { Count: > 0 } contacts && contacts[0] is JsonObject person)
        {
            Remove(person, learned.Contact);
        }

        if (before[CampaignKey] is JsonObject campaign)
        {
            Remove(campaign, learned.Campaign);
        }

        return before;

        static void Remove(JsonObject entity, IReadOnlySet<string> kinds)
        {
            if (entity[ExternalIdsKey] is JsonObject pins)
            {
                foreach (var kind in kinds)
                {
                    pins.Remove(kind);
                }
            }
        }
    }

    /// <summary>
    /// The input of a read made on a person's request rather than for a work item: the arguments the caller gave, and the
    /// person exactly as <see cref="Compose"/> projects them where the operation reads one. No campaign: the contracts a read
    /// on request performs let the campaign go unnamed.
    /// </summary>
    public static JsonObject ComposeRead(OperationContract contract, Contact? contact, IReadOnlyDictionary<string, string> contactPins, JsonObject args)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(contactPins);
        ArgumentNullException.ThrowIfNull(args);

        var input = new JsonObject { ["args"] = args };
        if (contract.Preflight.Contact != ContactRequirement.NotUsed && contact is not null)
        {
            input[ContactsKey] = new JsonArray(Person(contract, contact, contactPins, channel: null));
        }

        return input;
    }

    /// <summary>
    /// The input of a read made for a campaign on a person's or an agent's request rather than for a work item: the
    /// arguments the caller gave, the definition where the operation carries one, and the campaign with the routed
    /// plugin's own pins, exactly as <see cref="Compose"/> places them. No person: the reads a campaign's route answers
    /// name nobody.
    /// </summary>
    public static JsonObject ComposeCampaignRead(
        OperationContract contract,
        Campaign campaign,
        IReadOnlyDictionary<string, string> campaignPins,
        JsonObject args,
        JsonObject? definition)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(campaign);
        ArgumentNullException.ThrowIfNull(campaignPins);
        ArgumentNullException.ThrowIfNull(args);

        var input = new JsonObject { ["args"] = args };
        if (CarriesADefinition(contract) && definition is not null)
        {
            input[DefinitionKey] = definition;
        }

        input[CampaignKey] = Campaign(campaign, campaignPins);
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

    /// <summary>Whether the operation acts on a send by its reference: its input carries the send the runtime composes.</summary>
    public static bool NamesASend(OperationContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        return contract.InputSchema["properties"] is JsonObject properties && properties.ContainsKey(SendKey);
    }

    /// <summary>Whether the operation acts on a search by its reference: its input carries the search the runtime composes.</summary>
    public static bool NamesASearch(OperationContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        return contract.InputSchema["properties"] is JsonObject properties && properties.ContainsKey(SearchKey);
    }

    /// <summary>
    /// Whether the operation carries an audience's definition: its input declares the definition the runtime composes at
    /// the root, which the routed plugin's <c>audience_definition</c> is the schema of.
    /// </summary>
    public static bool CarriesADefinition(OperationContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        return contract.InputSchema["properties"] is JsonObject properties && properties.ContainsKey(DefinitionKey);
    }

    /// <summary>
    /// Whether the operation makes a send a later read or take-back can name: it declares the provider's reference for the
    /// send as an identifier of the work item itself.
    /// </summary>
    public static bool MakesASend(string? operation) => Makes(operation, SendKey);

    /// <summary>
    /// Whether the operation makes a search a later status read or stop can name: it declares the provider's reference for
    /// the search as an identifier of the work item itself.
    /// </summary>
    public static bool MakesASearch(string? operation) => Makes(operation, SearchKey);

    /// <summary>
    /// The work item the arguments name under <c>args.send</c>, or null where they name none: creation held the arguments
    /// to the operation's schema, so a value there is a work item's id.
    /// </summary>
    public static string? NamedSend(WorkItem item) => Named(item, SendKey);

    /// <summary>The work item the arguments name under <c>args.search</c>, or null where they name none.</summary>
    public static string? NamedSearch(WorkItem item) => Named(item, SearchKey);

    /// <summary>
    /// The audience the arguments name by its number under <c>args.audience</c>, or null where they name none: creation
    /// held the arguments to the operation's schema, so a value there is a whole number.
    /// </summary>
    public static int? NamedAudience(WorkItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.Context[WorkItemService.InputKey] is JsonObject arguments
            && arguments[AudienceKey] is JsonValue named
            && named.GetValueKind() == JsonValueKind.Number
            && named.TryGetValue(out int number)
            ? number
            : null;
    }

    private static bool Makes(string? operation, string key) =>
        operation is not null
        && OperationCatalog.Find(operation) is { } contract
        && contract.ExternalIds.TryGetValue(key, out var kind)
        && kind.Entity == PinnedEntity.WorkItem;

    private static string? Named(WorkItem item, string key)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.Context[WorkItemService.InputKey] is JsonObject arguments
            && arguments[key] is JsonValue named
            && named.GetValueKind() == JsonValueKind.String
            ? named.GetValue<string>()
            : null;
    }

    /// <summary>
    /// The definition the work carries, copied: the one the arguments write, or, where they write none, the one the audience
    /// they name was searched under; null where neither is there, which the schema then refuses by pointer.
    /// </summary>
    private static JsonObject? Definition(JsonNode? args, NamedAudience? audience) =>
        args?[DefinitionKey] is JsonObject written
            ? written.DeepClone().AsObject()
            : audience?.Definition.DeepClone().AsObject();

    /// <summary>
    /// Whether the schema the input declares at the root under <paramref name="key"/> — directly, or through one reference
    /// into its <c>$defs</c> — declares <paramref name="member"/>.
    /// </summary>
    private static bool Declares(OperationContract contract, string key, string member)
    {
        if (contract.InputSchema["properties"]?[key] is not JsonObject declared)
        {
            return false;
        }

        if (declared["$ref"] is JsonValue reference
            && reference.TryGetValue(out string? target)
            && target.StartsWith(RefPrefix, StringComparison.Ordinal))
        {
            declared = contract.InputSchema["$defs"]?[target[RefPrefix.Length..]] as JsonObject ?? declared;
        }

        return declared["properties"] is JsonObject members && members.ContainsKey(member);
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

        person[ExternalIdsKey] = Pins(pins);
        return person;
    }

    private static JsonObject Campaign(Campaign campaign, IReadOnlyDictionary<string, string> pins) => new()
    {
        ["id"] = JsonValue.Create(campaign.PublicId),
        ["name"] = JsonValue.Create(campaign.Name),
        [CampaignStatusKey] = JsonSerializer.SerializeToNode(campaign.Status, JasonJson.Options),
        [ExternalIdsKey] = Pins(pins),
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
