using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Channels;
using Jason.Contracts.Plugins;

namespace Jason.Contracts.Operations;

/// <summary>
/// One canonical operation, exactly as its published document states it. The document under
/// <c>docs/contracts/operations/</c> is the single source of truth: it is embedded into this assembly, so the
/// catalog the runtime enforces is the file a plugin author reads.
/// </summary>
/// <remarks>
/// The first block of properties is the vendor-neutral level-1 contract, copied word for word. The rest is Jason's
/// own: what the runtime composes before it calls a plugin, what it may do with the answer, and what it does when
/// an attempt ends without one.
/// </remarks>
public sealed record OperationContract(
    string Id,
    int Version,
    L1Origin L1,
    string Intent,
    ContractProperty Reach,
    ContractProperty Reversibility,
    ContractProperty Approval,
    string? ApprovalArtefact,
    bool ApprovalDeparts,
    bool ProtectiveFloor,
    string? ApprovalReason,
    ContractProperty BeforeRepeating,
    ContractProperty IdempotencyKey,
    ContractProperty PerItemResults,
    bool AcceptsCollection,
    ContractProperty Cost,
    string? CostBasis,
    string? Meter,
    IReadOnlyList<string> Invariants,
    Preflight Preflight,
    IReadOnlyList<string>? ChannelArguments,
    IReadOnlyList<string>? ChannelTemplates,
    string? RouteChannelArgument,
    DispatchRule Dispatch,
    ContactProjection? ContactProjection,
    string Precondition,
    OperationPreview? Preview,
    JsonObject InputSchema,
    JsonObject OutputSchema,
    IReadOnlyDictionary<string, ExternalIdKind> ExternalIds,
    RecoveryRead? RecoveryRead,
    RepeatAfterAmbiguous RepeatAfterAmbiguous,
    IReadOnlyList<FailureCode> FailureCodes,
    int TimeoutMs,
    IReadOnlyList<string> Conformance)
{
    /// <summary>The argument an operation whose <c>preflight.channel</c> is <c>from_args</c> names the channel it consumes in.</summary>
    private const string ConsumedChannelArgument = "channel";

    /// <summary>The value a person is reached at, in the composed input of an operation about one person.</summary>
    private const string ReachedValue = "/contacts/0/channels/0/value";

    /// <summary>
    /// The approvals this build understands, and the only ones a document may state. <c>auto</c> is the one approval a
    /// runtime may give itself. <c>confirm_once</c> and <c>confirm_each</c> are a person's, and the claim parks work for
    /// either: one decision per work item, bound to the composed input it would run with. A work item addresses one
    /// person in this version, so that is what <c>confirm_each</c> asks for — one decision per item, each its own act
    /// against a named person — and a document stating it is held to naming that person.
    /// </summary>
    public static IReadOnlyList<string> ApprovalWords { get; } = ["auto", "confirm_once", "confirm_each"];

    /// <summary>
    /// The schema the composed input is measured by. Setting it, in a constructor or in a <c>with</c>, derives
    /// <see cref="ArgumentSchema"/> from it, so the two never describe different documents.
    /// </summary>
    public JsonObject InputSchema
    {
        get;
        init
        {
            field = value;
            ArgumentSchema = ArgumentsOf(value);
        }
    } = InputSchema;

    /// <summary>
    /// The schema of a caller's own arguments — the <c>args</c> property of <see cref="InputSchema"/>, what a work
    /// item carries under <c>context.input</c>. Everything else in the input schema is what the runtime composes
    /// around the arguments at the claim: the contact, the campaign and the idempotency key.
    /// </summary>
    /// <remarks>
    /// An argument may be described by a definition the whole document shares — the account a message goes from, the
    /// value a contact field takes — and a reference resolves against the root of the schema it is read in, so the
    /// property carries the document's <c>$defs</c> beside it. Both are copies, made once as the input schema is
    /// set: the input schema is never written, and no request builds a schema of its own.
    /// </remarks>
    public JsonObject ArgumentSchema { get; private init; } = ArgumentsOf(InputSchema);

    /// <summary>
    /// The one key of a caller's arguments no document declares: the provider's own object, composed for its package by
    /// the provider's skills. <c>docs/contracts/README.md</c> states the rule; <see cref="CheckArguments"/> and
    /// <see cref="CheckInput"/> are where it is read, for every operation alike.
    /// </summary>
    public const string ProviderSlot = "provider";

    /// <summary>The composed input's key for the caller's arguments.</summary>
    private const string ArgumentsKey = "args";

    /// <summary>All the runtime asks of the slot: that it is an object, whatever the object holds.</summary>
    private static readonly JsonObject AnObject = new() { ["type"] = "object" };

    /// <summary>
    /// The channels a route may name under this operation, in the order the document lists them: the enum of the argument
    /// <see cref="RouteChannelArgument"/> names, or null where the document names none and a route names no channel here.
    /// </summary>
    public IReadOnlyList<string>? RouteChannels =>
        RouteChannelArgument is { } name && ArgumentSchema["properties"]?[name]?["enum"] is JsonArray offered
            ? [.. offered.Select(channel => channel!.GetValue<string>())]
            : null;

    /// <summary>
    /// The channel a call's arguments name for its route: the value of the argument <see cref="RouteChannelArgument"/>
    /// names, when it is one of <see cref="RouteChannels"/>. Null where the document names no argument, and where the
    /// arguments leave it out — a filter a caller may omit — or carry something no route could name: a call without a
    /// channel resolves by the operation's own route.
    /// </summary>
    /// <remarks>The name is matched as written, by walking the pairs, as the slot is.</remarks>
    public string? RouteChannel(JsonNode? arguments)
    {
        if (RouteChannels is not { } channels || arguments is not JsonObject map || !Readable(map))
        {
            return null;
        }

        foreach (var (name, value) in map)
        {
            if (string.Equals(name, RouteChannelArgument, StringComparison.Ordinal))
            {
                return value is JsonValue text
                    && text.GetValueKind() == JsonValueKind.String
                    && text.TryGetValue(out string? channel)
                    && channels.Contains(channel, StringComparer.Ordinal)
                    ? channel
                    : null;
            }
        }

        return null;
    }

    /// <summary>
    /// A caller's arguments checked against <see cref="ArgumentSchema"/>, with the provider's slot set aside: the slot is
    /// held to being an object, and every other argument to what the document says. Pointers are into the arguments.
    /// </summary>
    /// <remarks>
    /// What is checked is a copy without the slot, made only when the slot is there, so the arguments themselves are
    /// never touched and a plugin is handed them as they were written, in the order they were written.
    /// </remarks>
    public IReadOnlyList<SchemaProblem> CheckArguments(JsonNode? arguments, PatternTiming timing)
    {
        ArgumentNullException.ThrowIfNull(timing);

        if (!Slotted(arguments, out var slot))
        {
            return SchemaValidator.Validate(arguments, ArgumentSchema, timing);
        }

        var rest = Without(arguments!.AsObject(), ProviderSlot);
        return [.. SchemaValidator.Validate(rest, ArgumentSchema, timing), .. Slot(slot, "/" + ProviderSlot, timing)];
    }

    /// <summary>
    /// The composed input checked against <see cref="InputSchema"/>, the slot in its arguments set aside as
    /// <see cref="CheckArguments"/> sets it aside. Pointers are into the composed input.
    /// </summary>
    public IReadOnlyList<SchemaProblem> CheckInput(JsonNode? input, PatternTiming timing)
    {
        ArgumentNullException.ThrowIfNull(timing);

        var arguments = input is JsonObject document && Readable(document) ? document[ArgumentsKey] : null;
        if (!Slotted(arguments, out var slot))
        {
            return SchemaValidator.Validate(input, InputSchema, timing);
        }

        var rest = Without(arguments!.AsObject(), ProviderSlot);
        var checkedInput = new JsonObject();
        foreach (var (name, value) in input!.AsObject())
        {
            checkedInput[name] = string.Equals(name, ArgumentsKey, StringComparison.Ordinal) ? rest : value?.DeepClone();
        }

        return [.. SchemaValidator.Validate(checkedInput, InputSchema, timing), .. Slot(slot, "/" + ArgumentsKey + "/" + ProviderSlot, timing)];
    }

    /// <summary>
    /// Whether the arguments are an object that can be read and that names the slot, a null in it included. The name is
    /// matched as written, by walking the pairs: an object read with names that match in any case would also answer a
    /// lookup for <c>Provider</c>, which <see cref="Without"/> keeps as an argument.
    /// </summary>
    private static bool Slotted(JsonNode? arguments, out JsonNode? slot)
    {
        slot = null;
        if (arguments is not JsonObject map || !Readable(map))
        {
            return false;
        }

        foreach (var (name, value) in map)
        {
            if (string.Equals(name, ProviderSlot, StringComparison.Ordinal))
            {
                slot = value;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A copy of a readable object without one key. A value is copied unread, so a property written twice deeper down
    /// travels into the copy as written, and the schema reports it where it stands.
    /// </summary>
    private static JsonObject Without(JsonObject map, string key)
    {
        var copy = new JsonObject();
        foreach (var (name, value) in map)
        {
            if (!string.Equals(name, key, StringComparison.Ordinal))
            {
                copy[name] = value?.DeepClone();
            }
        }

        return copy;
    }

    private static IEnumerable<SchemaProblem> Slot(JsonNode? slot, string pointer, PatternTiming timing) =>
        SchemaValidator.Validate(slot, AnObject, timing).Select(problem => problem with { Pointer = pointer + problem.Pointer });

    /// <summary>Whether an object can be read at all: a parser may accept a property written twice and throw at the first read.</summary>
    private static bool Readable(JsonObject map)
    {
        try
        {
            _ = map.Count;
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads one published document. Absence is never a default: a field the document does not state is an error
    /// naming the file and the field, because the dangerous reading of a missing property is exactly the reading
    /// nobody intended to write down.
    /// </summary>
    /// <exception cref="InvalidOperationException">The document is not a complete contract.</exception>
    public static OperationContract Parse(string source, JsonNode? document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        if (document is not JsonObject fields)
        {
            throw new InvalidOperationException($"{source}: an operation contract is a JSON object.");
        }

        var reader = new DocumentReader(source, fields);
        var contract = new OperationContract(
            reader.Text("id"),
            reader.Integer("version"),
            ReadOrigin(reader.Object("l1"), reader),
            reader.Text("intent"),
            reader.Property("reach"),
            reader.Property("reversibility"),
            reader.Property("approval"),
            reader.OptionalText("approval_artefact"),
            reader.Flag("approval_departs"),
            reader.Flag("protective_floor"),
            reader.OptionalText("approval_reason"),
            reader.Property("before_repeating"),
            reader.Property("idempotency_key"),
            reader.Property("per_item_results"),
            reader.Flag("accepts_collection"),
            reader.Property("cost"),
            reader.OptionalText("cost_basis"),
            reader.OptionalText("meter"),
            reader.TextList("invariants"),
            ReadPreflight(reader.Object("preflight"), reader),
            reader.OptionalTextList("channel_arguments"),
            reader.OptionalTextList("channel_templates"),
            reader.OptionalText("route_channel_argument"),
            ReadDispatch(reader.Object("dispatch"), reader),
            ReadProjection(reader.OptionalObject("contact_projection"), reader),
            reader.Text("precondition"),
            ReadPreview(reader.OptionalObject("preview"), reader),
            reader.Object("input_schema"),
            reader.Object("output_schema"),
            ReadExternalIds(reader.Object("external_ids"), reader),
            ReadRecoveryRead(reader.OptionalObject("recovery_read"), reader),
            reader.Enumeration("repeat_after_ambiguous", RepeatAfterAmbiguousNames),
            ReadFailureCodes(reader.Array("failure_codes"), reader),
            reader.Integer("timeout_ms"),
            reader.TextList("conformance"));

        reader.RefuseAnythingElse();
        RefuseUnknownApproval(source, contract);
        RefuseUnsupportedFloor(source, contract);
        RefuseUnpairedChannels(source, contract);
        RefuseUncheckableChannelArguments(source, contract);
        RefuseUncheckableChannelTemplates(source, contract);
        RefuseUnroutableChannelArgument(source, contract);
        RefuseKeyedReadWithoutRepeat(source, contract);
        RefuseUncheckedReach(source, contract);
        RefuseUnpausableWork(source, contract);
        RefuseUnarchivableWork(source, contract);
        RefuseUnheldReach(source, contract);
        RefuseUnaddressablePreview(source, contract);
        RefuseUndeclaredContactMode(source, contract);
        return contract;
    }

    /// <summary>
    /// The approval is read as one of the words this build knows, or not at all: a word read as whichever of them it
    /// happened not to equal could ask a person for less than the document means. And <c>confirm_each</c> is a decision
    /// per person reached, shown the artefact that reaches them, so its document names one person — a required contact,
    /// no collection — and its preview highlights the value that person is reached at.
    /// </summary>
    private static void RefuseUnknownApproval(string source, OperationContract contract)
    {
        var approval = contract.Approval.Value;
        if (!ApprovalWords.Contains(approval, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"{source}: `approval` is `{approval}`, which this build does not know: an approval is {string.Join(", ", ApprovalWords.Select(word => $"`{word}`"))}, and a word read as one of them could ask a person for less than the document means.");
        }

        if (string.Equals(approval, "confirm_each", StringComparison.Ordinal)
            && (contract.Preflight.Contact != ContactRequirement.Required
                || contract.AcceptsCollection
                || contract.Preview is null
                || !contract.Preview.Highlights.Contains(ReachedValue, StringComparer.Ordinal)))
        {
            throw new InvalidOperationException(
                $"{source}: `approval` is `confirm_each`, one decision per person reached, so the document names one person — `preflight.contact` is `required` and `accepts_collection` is `false` — and its preview highlights `{ReachedValue}`, the value that person is reached at.");
        }
    }

    /// <summary>
    /// How the provider's own contact for the person is found is data, and each way declares what it implies. A document
    /// finds a provider's contact exactly when it takes one. A way that finds the person — creating them where the
    /// provider holds nobody, or never — has the plugin return the provider's identifier for whoever it found, so the
    /// document declares that kind. A way that works by the pin alone has a word for a person nobody pinned.
    /// </summary>
    private static void RefuseUndeclaredContactMode(string source, OperationContract contract)
    {
        var mode = contract.Preflight.ProviderContact;
        if ((mode == ProviderContact.NotUsed) != (contract.Preflight.Contact == ContactRequirement.NotUsed))
        {
            throw new InvalidOperationException(
                $"{source}: `preflight.provider_contact` is `{Word(ProviderContactNames, mode)}`, but `preflight.contact` is `{Word(ContactRequirementNames, contract.Preflight.Contact)}`; a document finds the provider's contact for a person exactly when it takes one, and says how.");
        }

        if (mode is ProviderContact.FindOrCreate or ProviderContact.FindOnly
            && !(contract.ExternalIds.TryGetValue("contact", out var kind) && kind.Entity == PinnedEntity.Contact))
        {
            throw new InvalidOperationException(
                $"{source}: `preflight.provider_contact` is `{Word(ProviderContactNames, mode)}`, so the plugin returns the provider's identifier for the person it finds, and `external_ids.contact` does not declare it.");
        }

        // Only the way that creates is paired with a channel. `find_only` finds the person "by that value, or through what
        // the operation names" (the contact proxy rule), so a document of that word may take no channel: the reply in a
        // thread finds the person through its thread, and a later one may do the same with `channel: none`.
        if (mode == ProviderContact.FindOrCreate && contract.Preflight.Channel != ChannelRequirement.FromArgs)
        {
            throw new InvalidOperationException(
                $"{source}: `preflight.provider_contact` is `find_or_create`, so the person is looked up by the channel the arguments name and created from it, and `preflight.channel` is not `from_args`.");
        }

        if (mode == ProviderContact.PinnedOnly
            && !contract.FailureCodes.Any(code => string.Equals(code.Code, "contact_not_linked", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"{source}: `preflight.provider_contact` is `pinned_only`, so a person nobody pinned fails before the provider is asked anything, and `failure_codes` does not declare `contact_not_linked`.");
        }
    }

    private static JsonObject ArgumentsOf(JsonObject inputSchema)
    {
        if ((inputSchema["properties"] as JsonObject)?["args"] is not JsonObject arguments)
        {
            return [];
        }

        var schema = arguments.DeepClone().AsObject();
        if (inputSchema["$defs"] is JsonObject definitions)
        {
            schema["$defs"] = definitions.DeepClone();
        }

        return schema;
    }

    private static string Word<TValue>(IReadOnlyDictionary<string, TValue> vocabulary, TValue value)
        where TValue : struct, Enum =>
        vocabulary.First(entry => EqualityComparer<TValue>.Default.Equals(entry.Value, value)).Key;

    /// <summary>
    /// Every pointer a preview renders or highlights has to be one the input schema can hold, or a misspelling would
    /// render a dash in front of a person deciding. The walk reads plain <c>properties</c> and <c>items</c>; what lies
    /// behind a reference or a combinator is proven by the preview fixtures instead.
    /// </summary>
    private static void RefuseUnaddressablePreview(string source, OperationContract contract)
    {
        if (contract.Preview is not { } preview)
        {
            return;
        }

        foreach (var pointer in preview.Summary.Pointers.Concat(preview.Highlights))
        {
            if (PreviewPointer.Parse(pointer).Unaddressable(contract.InputSchema) is { } reason)
            {
                throw new InvalidOperationException($"{source}: the preview's `{pointer}` names nothing the composed input can hold: {reason}.");
            }
        }
    }

    /// <summary>
    /// A channel taken from the arguments is one of the contact's channels, and the plugin consumes exactly that
    /// one. So an operation that may run without a contact has nothing to take a channel from, and a projection
    /// that consumes a channel while the arguments name none would hand the plugin a channel nobody chose.
    /// </summary>
    private static void RefuseUnpairedChannels(string source, OperationContract contract)
    {
        if (contract.Preflight.Contact == ContactRequirement.Optional && contract.Preflight.Channel == ChannelRequirement.FromArgs)
        {
            throw new InvalidOperationException(
                $"{source}: `preflight.contact` is `optional` and `preflight.channel` is `from_args`, but a channel is the contact's own, and an operation that may run without a contact has nothing to take it from.");
        }

        if (contract.ContactProjection?.Channels == "consumed" && contract.Preflight.Channel != ChannelRequirement.FromArgs)
        {
            throw new InvalidOperationException(
                $"{source}: `contact_projection.channels` is `consumed`, but only a channel named by the arguments is consumed, and `preflight.channel` is not `from_args`.");
        }
    }

    /// <summary>
    /// A recovery read that settles a lost answer by the work item's key is one a later attempt performs before it
    /// repeats, so a document says so only where it repeats after its recovery read: elsewhere nothing would repeat, and
    /// the claim would ask a manifest for a promise no attempt relies on.
    /// </summary>
    private static void RefuseKeyedReadWithoutRepeat(string source, OperationContract contract)
    {
        if (contract.RecoveryRead is { ByKey: true } && contract.RepeatAfterAmbiguous != RepeatAfterAmbiguous.AfterRecoveryRead)
        {
            throw new InvalidOperationException(
                $"{source}: `recovery_read.by_key` says a lost answer is settled by the work item's key, and a document says that only where it repeats after its recovery read; `repeat_after_ambiguous` is `{Word(RepeatAfterAmbiguousNames, contract.RepeatAfterAmbiguous)}`.");
        }
    }

    /// <summary>
    /// The arguments a plugin may take on some of an operation's channels and not on others are what a manifest lists per
    /// channel and what the claim checks against that list, so each is one the claim can check: an argument the arguments
    /// declare, named once, that a caller may leave out — a required one would be refused on every channel a plugin does
    /// not list — of an operation whose arguments name the channel it is listed against. A document that names none
    /// writes null.
    /// </summary>
    private static void RefuseUncheckableChannelArguments(string source, OperationContract contract)
    {
        if (contract.ChannelArguments is not { } names)
        {
            return;
        }

        if (names.Count == 0)
        {
            throw new InvalidOperationException(
                $"{source}: `channel_arguments` is an empty list; a document that names no argument a plugin takes per channel writes null.");
        }

        if (contract.Preflight.Channel != ChannelRequirement.FromArgs)
        {
            throw new InvalidOperationException(
                $"{source}: `channel_arguments` names arguments a plugin takes on some channels and not on others, but `preflight.channel` is not `from_args`, so the arguments name no channel to list them against.");
        }

        var declared = contract.ArgumentSchema["properties"] as JsonObject;
        var required = (contract.ArgumentSchema["required"] as JsonArray)?.Select(name => name?.GetValue<string>()).ToHashSet(StringComparer.Ordinal) ?? [];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            if (!seen.Add(name))
            {
                throw new InvalidOperationException($"{source}: `channel_arguments` names `{name}` twice.");
            }

            if (declared is null || !declared.ContainsKey(name))
            {
                throw new InvalidOperationException(
                    $"{source}: `channel_arguments` names `{name}`, which `input_schema.properties.args` does not declare, so no call could carry it.");
            }

            if (required.Contains(name))
            {
                throw new InvalidOperationException(
                    $"{source}: `channel_arguments` names `{name}`, which the arguments make required, so every channel a plugin does not list it for would be refused.");
            }
        }
    }

    /// <summary>
    /// The channels an operation's message may be a template on are what a manifest lists and what the claim checks
    /// against that list, so each is one the claim can check: a channel the arguments name, named once, of an operation
    /// whose arguments name the channel and whose message says whether it is a template. A document whose message is
    /// never a template writes null.
    /// </summary>
    private static void RefuseUncheckableChannelTemplates(string source, OperationContract contract)
    {
        if (contract.ChannelTemplates is not { } channels)
        {
            return;
        }

        if (channels.Count == 0)
        {
            throw new InvalidOperationException(
                $"{source}: `channel_templates` is an empty list; a document whose message is never a template writes null.");
        }

        if (contract.Preflight.Channel != ChannelRequirement.FromArgs)
        {
            throw new InvalidOperationException(
                $"{source}: `channel_templates` names channels a message may be a template on, but `preflight.channel` is not `from_args`, so the arguments name no channel to list them against.");
        }

        if (contract.ArgumentSchema["properties"]?["message"]?["properties"] is not JsonObject message || !message.ContainsKey("compose"))
        {
            throw new InvalidOperationException(
                $"{source}: `channel_templates` names channels a message may be a template on, but the arguments' `message` declares no `compose`, so no message says it is one.");
        }

        var offered = (contract.ArgumentSchema["properties"]?["channel"]?["enum"] as JsonArray)?
            .Select(channel => channel?.GetValue<string>())
            .ToHashSet(StringComparer.Ordinal) ?? [];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var channel in channels)
        {
            if (!seen.Add(channel))
            {
                throw new InvalidOperationException($"{source}: `channel_templates` names `{channel}` twice.");
            }

            if (!offered.Contains(channel))
            {
                throw new InvalidOperationException(
                    $"{source}: `channel_templates` names `{channel}`, which the arguments' `channel` does not offer, so no call could carry a template there.");
            }
        }
    }

    /// <summary>
    /// A route may name a channel under an operation only where the document names the argument the channel is read from,
    /// and a route is held to the channels that argument may take. So the argument is one the arguments declare, written
    /// as a string whose <c>enum</c> lists those channels — an open pattern offers no list to hold a route to, which is
    /// what the address a person is found by is — and every value is a channel: a name <see cref="ChannelRules"/> knows,
    /// spelled as it keeps one. The argument may be one a caller leaves out; such a call carries no channel
    /// (<see cref="RouteChannel"/>). Where the arguments name the channel the operation consumes, the route reads that
    /// same argument, so a call is never routed by one channel and sent on another. A document that names none writes null.
    /// </summary>
    private static void RefuseUnroutableChannelArgument(string source, OperationContract contract)
    {
        if (contract.RouteChannelArgument is not { } name)
        {
            return;
        }

        if (contract.ArgumentSchema["properties"] is not JsonObject declared || !declared.TryGetPropertyValue(name, out var argument))
        {
            throw new InvalidOperationException(
                $"{source}: `route_channel_argument` names `{name}`, which `input_schema.properties.args` does not declare, so no call could carry a channel there.");
        }

        if (argument is not JsonObject written
            || written["type"] is not JsonValue type
            || type.GetValueKind() != JsonValueKind.String
            || !string.Equals(type.GetValue<string>(), "string", StringComparison.Ordinal)
            || written["enum"] is not JsonArray offered
            || offered.Count == 0)
        {
            throw new InvalidOperationException(
                $"{source}: `route_channel_argument` names `{name}`, which is not written as a string with an `enum` of the channels it may take, so a route could not be held to them.");
        }

        foreach (var value in offered)
        {
            if (value is not JsonValue text
                || text.GetValueKind() != JsonValueKind.String
                || !text.TryGetValue(out string? channel)
                || !string.Equals(ChannelRules.NormalizeChannelName(channel, out _), channel, StringComparison.Ordinal)
                || !ChannelRules.IsKnown(channel))
            {
                throw new InvalidOperationException(
                    $"{source}: `route_channel_argument` names `{name}`, whose `enum` offers `{Shown(value)}`, which is not a channel: a channel a route names is one the channel rules know, spelled as they keep it.");
            }
        }

        if (contract.Preflight.Channel == ChannelRequirement.FromArgs && !string.Equals(name, ConsumedChannelArgument, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{source}: `route_channel_argument` names `{name}`, but `preflight.channel` is `from_args`, so the operation consumes the channel its arguments name in `{ConsumedChannelArgument}`, and a route reads that same argument.");
        }
    }

    /// <summary>An enum's value as a refusal names it: a string as written, anything else as its JSON.</summary>
    private static string Shown(JsonNode? value) =>
        value is JsonValue text && text.GetValueKind() == JsonValueKind.String ? text.GetValue<string>() : value?.ToJsonString() ?? "null";

    /// <summary>
    /// The protective floor is the catalogue's one lowering of an approval, and a checked one: the operation runs
    /// <c>auto</c>, says why, and nothing in Jason holds it back — neither the suppression register nor a paused
    /// campaign. Checked first, so a document that claims the floor is told what the floor requires.
    /// </summary>
    private static void RefuseUnsupportedFloor(string source, OperationContract contract)
    {
        if (!contract.ProtectiveFloor)
        {
            return;
        }

        var unmet = new List<string>();
        if (!string.Equals(contract.Approval.Value, "auto", StringComparison.Ordinal))
        {
            unmet.Add("`approval` is `auto`");
        }

        if (string.IsNullOrWhiteSpace(contract.ApprovalReason))
        {
            unmet.Add("`approval_reason` says why");
        }

        if (contract.Preflight.Suppression != SuppressionCheck.NotApplicable)
        {
            unmet.Add("`preflight.suppression` is `not_applicable`");
        }

        if (contract.Dispatch.InPausedCampaign != InPausedCampaign.Runs)
        {
            unmet.Add("`dispatch.in_paused_campaign` is `runs`");
        }

        if (unmet.Count > 0)
        {
            throw new InvalidOperationException(
                $"{source}: `protective_floor` is `true`, and the floor requires that {string.Join("; that ", unmet)}. This document does not.");
        }
    }

    /// <summary>
    /// The suppression register protects a person from being reached, so only an operation that cannot reach anybody
    /// — one whose reach is <c>control</c>, read as its dangerous value — may declare that the register does not
    /// apply to it.
    /// </summary>
    private static void RefuseUncheckedReach(string source, OperationContract contract)
    {
        if (contract.Preflight.Suppression == SuppressionCheck.NotApplicable
            && !string.Equals(contract.Reach.Value, "control", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{source}: `preflight.suppression` is `not_applicable`, but `reach` is `{contract.Reach.Value}`; only an operation whose reach is `control` takes a person out rather than reaching them.");
        }
    }

    /// <summary>
    /// Pausing a campaign stops its work. Only an operation that reaches nobody and that nobody is asked about —
    /// <c>control</c> and <c>auto</c>, each read as its dangerous value — may run past that: a read, an act or
    /// anything a person approves waits with its campaign.
    /// </summary>
    private static void RefuseUnpausableWork(string source, OperationContract contract)
    {
        if (contract.Dispatch.InPausedCampaign == InPausedCampaign.Runs
            && !(string.Equals(contract.Reach.Value, "control", StringComparison.Ordinal)
                && string.Equals(contract.Approval.Value, "auto", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"{source}: `dispatch.in_paused_campaign` is `runs`, but `reach` is `{contract.Reach.Value}` and `approval` is `{contract.Approval.Value}`; only an operation whose reach is `control` and whose approval is `auto` runs while its campaign is paused.");
        }
    }

    /// <summary>
    /// Archiving a campaign cancels its open work. Only work that runs past a pause may run past an archive too: a stop
    /// whose not running is the harm, such as a search that goes on spending until it is stopped.
    /// </summary>
    private static void RefuseUnarchivableWork(string source, OperationContract contract)
    {
        if (contract.Dispatch.InArchivedCampaign == InArchivedCampaign.Runs && contract.Dispatch.InPausedCampaign != InPausedCampaign.Runs)
        {
            throw new InvalidOperationException(
                $"{source}: `dispatch.in_archived_campaign` is `runs`, but `dispatch.in_paused_campaign` is not; only work that runs while its campaign is paused runs after it is archived.");
        }
    }

    /// <summary>
    /// A provider block is the provider refusing to let work reach a person, so an operation that reaches one —
    /// <c>act</c>, read as its dangerous value — waits behind it. Nothing is refused the other way: a control may wait.
    /// </summary>
    private static void RefuseUnheldReach(string source, OperationContract contract)
    {
        if (contract.Dispatch.UnderProviderBlock == UnderProviderBlock.Runs
            && string.Equals(contract.Reach.Value, "act", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{source}: `dispatch.under_provider_block` is `runs`, but `reach` is `act`; an operation that reaches a person waits while a provider block of its plugin and binding is open.");
        }
    }

    private static readonly IReadOnlyDictionary<string, RepeatAfterAmbiguous> RepeatAfterAmbiguousNames =
        new Dictionary<string, RepeatAfterAmbiguous>(StringComparer.Ordinal)
        {
            ["safe"] = RepeatAfterAmbiguous.Safe,
            ["after_recovery_read"] = RepeatAfterAmbiguous.AfterRecoveryRead,
            ["never"] = RepeatAfterAmbiguous.Never,
        };

    private static readonly IReadOnlyDictionary<string, ContactRequirement> ContactRequirementNames =
        new Dictionary<string, ContactRequirement>(StringComparer.Ordinal)
        {
            ["required"] = ContactRequirement.Required,
            ["not_used"] = ContactRequirement.NotUsed,
            ["optional"] = ContactRequirement.Optional,
        };

    /// <summary>Whether the plugin receives the channel the arguments name, or none of the person's channels.</summary>
    private static readonly IReadOnlyList<string> ProjectedChannelsNames = ["consumed", "none"];

    private static readonly IReadOnlyDictionary<string, SuppressionCheck> SuppressionCheckNames =
        new Dictionary<string, SuppressionCheck>(StringComparer.Ordinal)
        {
            ["checked"] = SuppressionCheck.Checked,
            ["not_applicable"] = SuppressionCheck.NotApplicable,
        };

    private static readonly IReadOnlyDictionary<string, ProviderContact> ProviderContactNames =
        new Dictionary<string, ProviderContact>(StringComparer.Ordinal)
        {
            ["find_or_create"] = ProviderContact.FindOrCreate,
            ["find_only"] = ProviderContact.FindOnly,
            ["pinned_only"] = ProviderContact.PinnedOnly,
            ["not_used"] = ProviderContact.NotUsed,
        };

    private static readonly IReadOnlyDictionary<string, InPausedCampaign> InPausedCampaignNames =
        new Dictionary<string, InPausedCampaign>(StringComparer.Ordinal)
        {
            ["waits"] = InPausedCampaign.Waits,
            ["runs"] = InPausedCampaign.Runs,
        };

    private static readonly IReadOnlyDictionary<string, InArchivedCampaign> InArchivedCampaignNames =
        new Dictionary<string, InArchivedCampaign>(StringComparer.Ordinal)
        {
            ["cancelled"] = InArchivedCampaign.Cancelled,
            ["runs"] = InArchivedCampaign.Runs,
        };

    private static readonly IReadOnlyDictionary<string, UnderProviderBlock> UnderProviderBlockNames =
        new Dictionary<string, UnderProviderBlock>(StringComparer.Ordinal)
        {
            ["waits"] = UnderProviderBlock.Waits,
            ["runs"] = UnderProviderBlock.Runs,
        };

    private static readonly IReadOnlyDictionary<string, ChannelRequirement> ChannelRequirementNames =
        new Dictionary<string, ChannelRequirement>(StringComparer.Ordinal)
        {
            ["from_args"] = ChannelRequirement.FromArgs,
            ["none"] = ChannelRequirement.None,
        };

    private static readonly IReadOnlyDictionary<string, PinnedEntity> PinnedEntityNames =
        new Dictionary<string, PinnedEntity>(StringComparer.Ordinal)
        {
            ["contact"] = PinnedEntity.Contact,
            ["campaign"] = PinnedEntity.Campaign,
            ["work_item"] = PinnedEntity.WorkItem,
        };

    private static readonly IReadOnlyDictionary<string, FailureClass> FailureClassNames =
        new Dictionary<string, FailureClass>(StringComparer.Ordinal)
        {
            ["transient"] = FailureClass.Transient,
            ["permanent"] = FailureClass.Permanent,
            ["validation"] = FailureClass.Validation,
            ["ambiguous"] = FailureClass.Ambiguous,
            ["blocked"] = FailureClass.Blocked,
        };

    private static L1Origin ReadOrigin(JsonObject origin, DocumentReader outer)
    {
        var reader = outer.Into("l1", origin);
        var value = new L1Origin(reader.Text("name"), reader.Text("contract_version"), reader.Integer("family"), reader.Flag("core"));
        reader.RefuseAnythingElse();
        return value;
    }

    private static Preflight ReadPreflight(JsonObject preflight, DocumentReader outer)
    {
        var reader = outer.Into("preflight", preflight);
        var value = new Preflight(
            reader.Enumeration("contact", ContactRequirementNames),
            reader.Enumeration("channel", ChannelRequirementNames),
            reader.Enumeration("suppression", SuppressionCheckNames),
            reader.Enumeration("provider_contact", ProviderContactNames));
        reader.RefuseAnythingElse();
        return value;
    }

    private static DispatchRule ReadDispatch(JsonObject dispatch, DocumentReader outer)
    {
        var reader = outer.Into("dispatch", dispatch);
        var value = new DispatchRule(
            reader.Enumeration("in_paused_campaign", InPausedCampaignNames),
            reader.Enumeration("in_archived_campaign", InArchivedCampaignNames),
            reader.Enumeration("under_provider_block", UnderProviderBlockNames));
        reader.RefuseAnythingElse();
        return value;
    }

    private static ContactProjection? ReadProjection(JsonObject? projection, DocumentReader outer)
    {
        if (projection is null)
        {
            return null;
        }

        var reader = outer.Into("contact_projection", projection);
        var value = new ContactProjection(reader.TextList("fields"), reader.Word("channels", ProjectedChannelsNames));
        reader.RefuseAnythingElse();
        return value;
    }

    private static OperationPreview? ReadPreview(JsonObject? preview, DocumentReader outer)
    {
        if (preview is null)
        {
            return null;
        }

        var reader = outer.Into("preview", preview);
        var summary = reader.Text("summary");
        var highlights = reader.TextList("highlights");
        reader.RefuseAnythingElse();

        PreviewTemplate template;
        try
        {
            template = PreviewTemplate.Parse(summary);
        }
        catch (FormatException error)
        {
            throw outer.Problem($"`preview.summary` is text with `{{/pointer}}` placeholders and nothing else: {error.Message}");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var highlight in highlights)
        {
            try
            {
                PreviewPointer.Parse(highlight);
            }
            catch (FormatException error)
            {
                throw outer.Problem($"`preview.highlights` lists JSON pointers into the composed input: {error.Message}");
            }

            if (!seen.Add(highlight))
            {
                throw outer.Problem($"`preview.highlights` names `{highlight}` twice.");
            }
        }

        return new OperationPreview(template, highlights);
    }

    private static RecoveryRead? ReadRecoveryRead(JsonObject? recoveryRead, DocumentReader outer)
    {
        if (recoveryRead is null)
        {
            return null;
        }

        var reader = outer.Into("recovery_read", recoveryRead);
        var value = new RecoveryRead(reader.Text("reads"), reader.Text("justification"), reader.OptionalFlag("by_key"));
        reader.RefuseAnythingElse();
        return value;
    }

    private static IReadOnlyDictionary<string, ExternalIdKind> ReadExternalIds(JsonObject declared, DocumentReader outer)
    {
        var kinds = new Dictionary<string, ExternalIdKind>(StringComparer.Ordinal);
        foreach (var (kind, node) in declared)
        {
            if (node is not JsonObject description)
            {
                throw outer.Problem($"`external_ids.{kind}` is an object naming the entity it identifies.");
            }

            var reader = outer.Into("external_ids." + kind, description);
            kinds[kind] = new ExternalIdKind(reader.Enumeration("entity", PinnedEntityNames), reader.Text("description"));
            reader.RefuseAnythingElse();
        }

        return kinds;
    }

    private static IReadOnlyList<FailureCode> ReadFailureCodes(JsonArray declared, DocumentReader outer)
    {
        var codes = new List<FailureCode>();
        for (var index = 0; index < declared.Count; index++)
        {
            if (declared[index] is not JsonObject entry)
            {
                throw outer.Problem(string.Create(CultureInfo.InvariantCulture, $"`failure_codes[{index}]` is an object."));
            }

            var reader = outer.Into(string.Create(CultureInfo.InvariantCulture, $"failure_codes[{index}]"), entry);
            codes.Add(new FailureCode(reader.Text("code"), reader.Enumeration("class", FailureClassNames), reader.Text("when")));
            reader.RefuseAnythingElse();
        }

        return codes;
    }

    /// <summary>
    /// Reads one object of a document, remembering what it was asked for so that anything left over — a typo, or a
    /// field from a newer version of the contract this build does not understand — is an error rather than silence.
    /// </summary>
    private sealed class DocumentReader(string source, JsonObject fields, string path = "")
    {
        private readonly HashSet<string> _read = new(StringComparer.Ordinal);

        public DocumentReader Into(string name, JsonObject nested) => new(source, nested, path.Length == 0 ? name : path + "." + name);

        public InvalidOperationException Problem(string message) => new($"{source}: {message}");

        public void RefuseAnythingElse()
        {
            foreach (var (name, _) in fields)
            {
                if (!_read.Contains(name))
                {
                    throw Problem($"`{Named(name)}` is not part of an operation contract.");
                }
            }
        }

        public string Text(string name) => Required(name) switch
        {
            JsonValue value when value.GetValueKind() == JsonValueKind.String && value.TryGetValue(out string? text) && text is not null => text,
            _ => throw Problem($"`{Named(name)}` is a string."),
        };

        public string? OptionalText(string name) => Required(name) switch
        {
            null => null,
            JsonValue value when value.GetValueKind() == JsonValueKind.String && value.TryGetValue(out string? text) && text is not null => text,
            _ => throw Problem($"`{Named(name)}` is a string, or null where it does not apply."),
        };

        public int Integer(string name) => Required(name) switch
        {
            JsonValue value when value.GetValueKind() == JsonValueKind.Number && value.TryGetValue(out int number) => number,
            _ => throw Problem($"`{Named(name)}` is a whole number."),
        };

        public bool Flag(string name) => Required(name) switch
        {
            JsonValue value when value.GetValueKind() is JsonValueKind.True or JsonValueKind.False && value.TryGetValue(out bool flag) => flag,
            _ => throw Problem($"`{Named(name)}` is true or false."),
        };

        /// <summary>
        /// A flag a document may leave out, which then says false: the one kind of field here whose absence is a default,
        /// kept for a promise only a few documents make.
        /// </summary>
        public bool OptionalFlag(string name) => fields.ContainsKey(name) && Flag(name);

        public JsonObject Object(string name) => Required(name) as JsonObject ?? throw Problem($"`{Named(name)}` is an object.");

        public JsonObject? OptionalObject(string name) => Required(name) switch
        {
            null => null,
            JsonObject value => value,
            _ => throw Problem($"`{Named(name)}` is an object, or null where it does not apply."),
        };

        public JsonArray Array(string name) => Required(name) as JsonArray ?? throw Problem($"`{Named(name)}` is an array.");

        public IReadOnlyList<string> TextList(string name)
        {
            var entries = Array(name);
            var values = new List<string>(entries.Count);
            foreach (var entry in entries)
            {
                values.Add(entry is JsonValue value && entry.GetValueKind() == JsonValueKind.String && value.TryGetValue(out string? text) && text is not null
                    ? text
                    : throw Problem($"`{Named(name)}` is an array of strings."));
            }

            return values;
        }

        public IReadOnlyList<string>? OptionalTextList(string name) => Required(name) switch
        {
            null => null,
            JsonArray => TextList(name),
            _ => throw Problem($"`{Named(name)}` is an array of strings, or null where it does not apply."),
        };

        public string Word(string name, IReadOnlyList<string> vocabulary)
        {
            var text = Text(name);
            return vocabulary.Contains(text, StringComparer.Ordinal)
                ? text
                : throw Problem($"`{Named(name)}` is one of {string.Join(", ", vocabulary)}, and `{text}` is none of them.");
        }

        public TValue Enumeration<TValue>(string name, IReadOnlyDictionary<string, TValue> vocabulary)
            where TValue : struct, Enum
        {
            var text = Text(name);
            return vocabulary.TryGetValue(text, out var value)
                ? value
                : throw Problem($"`{Named(name)}` is one of {string.Join(", ", vocabulary.Keys)}, and `{text}` is none of them.");
        }

        /// <summary>
        /// A property that is either a plain value or the level-1 conditional block. The value is always the
        /// dangerous reading, so every gate can read it without first asking whether a condition applies.
        /// </summary>
        public ContractProperty Property(string name) => Required(name) switch
        {
            JsonValue value when value.GetValueKind() == JsonValueKind.String && value.TryGetValue(out string? text) && text is not null =>
                new ContractProperty(text, false, null),
            JsonObject block => ReadConditional(name, block),
            _ => throw Problem($"`{Named(name)}` is a value, or the conditional block that states its dangerous reading."),
        };

        private ContractProperty ReadConditional(string name, JsonObject block)
        {
            var reader = Into(name, block);
            var value = reader.Text("value");
            var conditional = reader.Flag("conditional");
            var detail = reader.Text("detail");
            reader.RefuseAnythingElse();

            return conditional
                ? new ContractProperty(value, true, detail)
                : throw Problem($"`{Named(name)}` writes a block only when it is conditional; a plain value is written plainly.");
        }

        private JsonNode? Required(string name)
        {
            _read.Add(name);
            return fields.TryGetPropertyValue(name, out var node)
                ? node
                : throw Problem($"`{Named(name)}` is missing, and absence is not a default.");
        }

        private string Named(string name) => path.Length == 0 ? name : path + "." + name;
    }
}

/// <summary>
/// A property that is either a plain value or level-1's conditional block, whose <c>Value</c> is always the
/// dangerous reading — so an approval gate or a retry rule may read it without first resolving the condition.
/// </summary>
public sealed record ContractProperty(string Value, bool Conditional, string? Detail);

/// <summary>Where the operation comes from in the vendor-neutral contract this catalog is seeded from.</summary>
public sealed record L1Origin(string Name, string ContractVersion, int Family, bool Core);

/// <summary>
/// What the runtime must have in hand before it may compose this operation's input, whether the suppression register
/// refuses it for a person whose consumed channel value the register holds, and how the plugin finds the provider's own
/// contact for that person.
/// </summary>
public sealed record Preflight(ContactRequirement Contact, ChannelRequirement Channel, SuppressionCheck Suppression, ProviderContact ProviderContact);

/// <summary>
/// The contact fields an operation consumes. Declaring the projection is what makes widening it a visible change
/// rather than a plugin quietly receiving more of a person than the work needed. <c>Channels</c> is
/// <c>consumed</c>, the one channel the arguments name, or <c>none</c>.
/// </summary>
public sealed record ContactProjection(IReadOnlyList<string> Fields, string Channels);

/// <summary>
/// What a person deciding on this operation reads first: one line rendered from the composed input, and the values
/// in that input it singles out. Null for an operation nobody is asked to decide on.
/// </summary>
public sealed record OperationPreview(PreviewTemplate Summary, IReadOnlyList<string> Highlights)
{
    /// <summary>
    /// Each highlighted pointer and a copy of the value the composed input holds there, in the order the document
    /// lists them; a pointer that resolves to nothing carries null.
    /// </summary>
    public JsonObject Highlight(JsonNode? input)
    {
        var values = new JsonObject();
        foreach (var pointer in Highlights)
        {
            values[pointer] = PreviewPointer.Parse(pointer).Resolve(input)?.DeepClone();
        }

        return values;
    }

    /// <summary>
    /// One example of a message the composed input writes as a template, rendered by the template standard over the
    /// values the input holds: its subject, its text, and the names left for the provider to fill. Null when the
    /// input's message is final, or there is none.
    /// </summary>
    public JsonObject? Example(JsonNode? input) => TemplateExample.Of(input);
}

/// <summary>One kind of provider identifier this operation may return, and what it identifies.</summary>
public sealed record ExternalIdKind(PinnedEntity Entity, string Description);

/// <summary>
/// What a plugin must read before repeating this operation after an answer was lost, and why the reading is worth
/// the round trip — the justification is about money, because that is what a blind repeat costs.
/// </summary>
/// <param name="ByKey">
/// Whether the reading settles a lost answer by the work item's idempotency key, which only a plugin that makes every
/// attempt under the key can keep: then the runtime repeats a lost answer only under a plugin whose manifest declares
/// that it settles the operation by the key. Written <c>by_key</c>, and absent where it is false.
/// </param>
public sealed record RecoveryRead(string Reads, string Justification, bool ByKey = false);

/// <summary>One failure a plugin may report for this operation, under the class the runtime reads.</summary>
public sealed record FailureCode(string Code, FailureClass Class, string When);

/// <summary>What may be done with a work item whose attempt ended without a usable answer.</summary>
public enum RepeatAfterAmbiguous
{
    /// <summary>Repeating changes nothing and costs nothing, so the item is simply handed out again.</summary>
    Safe,

    /// <summary>Repeating is allowed because the contract obliges the plugin to read what already happened first.</summary>
    AfterRecoveryRead,

    /// <summary>Repeating is never allowed; the item ends for a person to look at.</summary>
    Never,
}

/// <summary>Whether the operation needs the work item's contact at all.</summary>
public enum ContactRequirement
{
    Required,
    NotUsed,

    /// <summary>
    /// The operation takes the work item's contact when the item names one and runs without it when it does not:
    /// a read about one person, or about the whole campaign.
    /// </summary>
    Optional,
}

/// <summary>Where the channel the operation acts on comes from.</summary>
public enum ChannelRequirement
{
    FromArgs,
    None,
}

/// <summary>
/// How the plugin finds the provider's own contact for the person an operation is about, when the person carries no pin
/// of the plugin's: the contact proxy rule, `docs/plugins.md` section 7. With a pin, every way works by the pin.
/// </summary>
public enum ProviderContact
{
    /// <summary>Look the person up by the consumed channel's value, and create them from it only where the provider holds nobody.</summary>
    FindOrCreate,

    /// <summary>Find the person — by the consumed channel's value, or through what the operation names — and create nobody.</summary>
    FindOnly,

    /// <summary>Work by the pin alone: a person nobody pinned is `contact_not_linked`, decided before the provider is asked anything.</summary>
    PinnedOnly,

    /// <summary>The operation names no person, so there is no contact to find.</summary>
    NotUsed,
}

/// <summary>How the dispatcher treats this operation's work beyond what every work item shares.</summary>
public sealed record DispatchRule(InPausedCampaign InPausedCampaign, InArchivedCampaign InArchivedCampaign, UnderProviderBlock UnderProviderBlock);

/// <summary>What archiving its campaign does to a work item naming this operation.</summary>
public enum InArchivedCampaign
{
    /// <summary>The archive cancels it, as it cancels all open work, and nothing of the campaign is claimed again.</summary>
    Cancelled,

    /// <summary>
    /// It runs: it is created, kept and claimed in an archived campaign, since its not running is the harm — a search's
    /// stop, without which the search goes on spending.
    /// </summary>
    Runs,
}

/// <summary>Whether a work item naming this operation is claimed while its campaign is paused.</summary>
public enum InPausedCampaign
{
    /// <summary>It waits with its campaign, as all work does.</summary>
    Waits,

    /// <summary>
    /// It runs: an operation whose not running is the harm — recording that somebody said stop, or pausing the
    /// provider's own campaign — still reaches the provider after somebody paused Jason's campaign first.
    /// </summary>
    Runs,
}

/// <summary>Whether a work item naming this operation is held while a provider block of its plugin and binding is open.</summary>
public enum UnderProviderBlock
{
    /// <summary>
    /// It waits: parked while the block is open and released when it clears. Every operation that reaches a person
    /// waits, and the block's trial is drawn from that work; a control waits where its one run must not be lost to the
    /// provider's refusal.
    /// </summary>
    Waits,

    /// <summary>It runs through the block and ends on its own answer: a read tells the truth, and a control may still succeed.</summary>
    Runs,
}

/// <summary>Whether the claim refuses a person whose consumed channel value is on the suppression register.</summary>
public enum SuppressionCheck
{
    /// <summary>It does: nobody is reached through a value the register protects.</summary>
    Checked,

    /// <summary>
    /// It does not, because the operation only takes a person out — records that they said stop, or removes them
    /// from a campaign — and refusing it for a suppressed person would refuse exactly the people it is for.
    /// </summary>
    NotApplicable,
}

/// <summary>What a returned provider identifier is an identifier of.</summary>
public enum PinnedEntity
{
    Contact,
    Campaign,

    /// <summary>
    /// The act the work item's attempt made the provider take — a send it took, by the reference it is read and cancelled
    /// by. One item is one act, so the identifier is kept on the attempt that learned it and in the item's result, and
    /// pinned to no contact and no campaign.
    /// </summary>
    WorkItem,
}
