using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    ContractProperty BeforeRepeating,
    ContractProperty IdempotencyKey,
    ContractProperty PerItemResults,
    bool AcceptsCollection,
    ContractProperty Cost,
    string? CostBasis,
    string? Meter,
    IReadOnlyList<string> Invariants,
    Preflight Preflight,
    ContactProjection? ContactProjection,
    string Precondition,
    JsonObject InputSchema,
    JsonObject OutputSchema,
    IReadOnlyDictionary<string, ExternalIdKind> ExternalIds,
    RecoveryRead? RecoveryRead,
    RepeatAfterAmbiguous RepeatAfterAmbiguous,
    IReadOnlyList<FailureCode> FailureCodes,
    int TimeoutMs,
    IReadOnlyList<string> Conformance)
{
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
            reader.Property("before_repeating"),
            reader.Property("idempotency_key"),
            reader.Property("per_item_results"),
            reader.Flag("accepts_collection"),
            reader.Property("cost"),
            reader.OptionalText("cost_basis"),
            reader.OptionalText("meter"),
            reader.TextList("invariants"),
            ReadPreflight(reader.Object("preflight"), reader),
            ReadProjection(reader.OptionalObject("contact_projection"), reader),
            reader.Text("precondition"),
            reader.Object("input_schema"),
            reader.Object("output_schema"),
            ReadExternalIds(reader.Object("external_ids"), reader),
            ReadRecoveryRead(reader.OptionalObject("recovery_read"), reader),
            reader.Enumeration("repeat_after_ambiguous", RepeatAfterAmbiguousNames),
            ReadFailureCodes(reader.Array("failure_codes"), reader),
            reader.Integer("timeout_ms"),
            reader.TextList("conformance"));

        reader.RefuseAnythingElse();
        return contract;
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
        };

    private static readonly IReadOnlyDictionary<string, FailureClass> FailureClassNames =
        new Dictionary<string, FailureClass>(StringComparer.Ordinal)
        {
            ["transient"] = FailureClass.Transient,
            ["permanent"] = FailureClass.Permanent,
            ["validation"] = FailureClass.Validation,
            ["ambiguous"] = FailureClass.Ambiguous,
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
            reader.Enumeration("channel", ChannelRequirementNames));
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
        var value = new ContactProjection(reader.TextList("fields"), reader.Text("channels"));
        reader.RefuseAnythingElse();
        return value;
    }

    private static RecoveryRead? ReadRecoveryRead(JsonObject? recoveryRead, DocumentReader outer)
    {
        if (recoveryRead is null)
        {
            return null;
        }

        var reader = outer.Into("recovery_read", recoveryRead);
        var value = new RecoveryRead(reader.Text("reads"), reader.Text("justification"));
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

/// <summary>What the runtime must have in hand before it may compose this operation's input.</summary>
public sealed record Preflight(ContactRequirement Contact, ChannelRequirement Channel);

/// <summary>
/// The contact fields an operation consumes. Declaring the projection is what makes widening it a visible change
/// rather than a plugin quietly receiving more of a person than the work needed.
/// </summary>
public sealed record ContactProjection(IReadOnlyList<string> Fields, string Channels);

/// <summary>One kind of provider identifier this operation may return, and what it identifies.</summary>
public sealed record ExternalIdKind(PinnedEntity Entity, string Description);

/// <summary>
/// What a plugin must read before repeating this operation after an answer was lost, and why the reading is worth
/// the round trip — the justification is about money, because that is what a blind repeat costs.
/// </summary>
public sealed record RecoveryRead(string Reads, string Justification);

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
}

/// <summary>Where the channel the operation acts on comes from.</summary>
public enum ChannelRequirement
{
    FromArgs,
    None,
}

/// <summary>What a returned provider identifier is an identifier of.</summary>
public enum PinnedEntity
{
    Contact,
    Campaign,
}
