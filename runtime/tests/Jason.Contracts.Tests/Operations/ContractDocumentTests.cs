using System.Text.Json.Nodes;
using Jason.Contracts.Operations;

namespace Jason.Contracts.Tests.OperationContracts;

/// <summary>
/// The rules a published contract has to satisfy to be worth enforcing. Each one is separate so that a failure
/// names the operation and the rule rather than "a document is wrong somewhere".
/// </summary>
public class ContractDocumentTests
{
    /// <summary>Every member an operation document states, and nothing else: absence is not a default.</summary>
    private static readonly string[] Members =
    [
        "id", "version", "l1", "intent",
        "reach", "reversibility", "approval", "approval_artefact", "approval_departs", "protective_floor",
        "approval_reason", "before_repeating", "idempotency_key", "per_item_results", "accepts_collection", "cost",
        "cost_basis", "meter", "invariants", "preflight", "contact_projection", "precondition", "preview",
        "input_schema", "output_schema", "external_ids",
        "recovery_read", "repeat_after_ambiguous", "failure_codes", "timeout_ms", "conformance",
    ];

    public static TheoryData<string> Published()
    {
        var data = new TheoryData<string>();
        foreach (var contract in OperationCatalog.All)
        {
            data.Add(contract.Id);
        }

        return data;
    }

    private static OperationContract Contract(string id) => OperationCatalog.Find(id)!;

    private static JsonObject Document(string id) =>
        (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(ContractFiles.Operations, id + ".json")))!;

    [Fact]
    public void The_catalog_publishes_exactly_the_operations_of_this_version()
    {
        Assert.Equal(
            ["campaign.enroll", "campaign.get", "conversation.get", "conversation.list", "engagement.summarize", "enrollment.list", "list_membership.add"],
            OperationCatalog.All.Select(c => c.Id));
        Assert.All(OperationCatalog.All, contract => Assert.Equal(1, contract.Version));
    }

    /// <summary>
    /// A failure code is one word in one vocabulary: the runtime reads the class, so a code filed as permanent in one
    /// document and transient in another would be repeated or given up on depending on which operation said it.
    /// </summary>
    [Fact]
    public void A_failure_code_has_the_same_class_in_every_document_that_declares_it()
    {
        var classes = OperationCatalog.All
            .SelectMany(contract => contract.FailureCodes.Select(code => (contract.Id, code.Code, code.Class)))
            .GroupBy(entry => entry.Code, StringComparer.Ordinal);

        Assert.All(classes, code => Assert.True(
            code.Select(entry => entry.Class).Distinct().Count() == 1,
            $"`{code.Key}` is declared as " + string.Join(", ", code.Select(entry => $"{entry.Class} by {entry.Id}")) + "."));
    }

    [Theory]
    [MemberData(nameof(Published))]
    public void Every_document_states_every_field_and_nothing_else(string id)
    {
        var document = Document(id);

        Assert.Equal(Members.Order(StringComparer.Ordinal), document.Select(member => member.Key).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The catalogue lets an operation lower its approval only as a protective floor and raise it only by a stated
    /// departure, and either one needs a reason. No operation of this version does either, so each says so in the
    /// two fields rather than leaving the reading of their absence to whoever reads the document next.
    /// </summary>
    [Theory]
    [MemberData(nameof(Published))]
    public void No_operation_of_this_version_lowers_its_approval_or_raises_it(string id)
    {
        var contract = Contract(id);

        Assert.False(contract.ProtectiveFloor);
        Assert.False(contract.ApprovalDeparts);
        Assert.Null(contract.ApprovalReason);
    }

    /// <summary>
    /// A preview is what a person reads before deciding, so an operation declares one exactly when a person decides
    /// on it: one that runs <c>auto</c> renders none, and one a person approves never leaves them reading the raw
    /// document alone.
    /// </summary>
    [Theory]
    [MemberData(nameof(Published))]
    public void An_operation_declares_a_preview_exactly_when_a_person_approves_it(string id)
    {
        var contract = Contract(id);

        Assert.True(
            (contract.Approval.Value != "auto") == (contract.Preview is not null),
            contract.Preview is null
                ? $"{id} is approved `{contract.Approval.Value}` and declares no preview for the person approving it."
                : $"{id} runs `auto` and declares a preview nobody will read.");
    }

    [Theory]
    [MemberData(nameof(Published))]
    public void The_file_on_disk_is_the_document_the_catalog_enforces(string id)
    {
        using var embedded = typeof(OperationContract).Assembly.GetManifestResourceStream("Jason.Contracts.Operations." + id + ".json");
        Assert.NotNull(embedded);
        using var reader = new StreamReader(embedded);

        var published = File.ReadAllText(Path.Combine(ContractFiles.Operations, id + ".json"));

        Assert.Equal(published.ReplaceLineEndings("\n"), reader.ReadToEnd().ReplaceLineEndings("\n"));
    }

    [Theory]
    [MemberData(nameof(Published))]
    public void A_repeat_is_only_safe_where_the_operation_reads_and_costs_nothing(string id)
    {
        var contract = Contract(id);
        if (contract.RepeatAfterAmbiguous != RepeatAfterAmbiguous.Safe)
        {
            return;
        }

        Assert.Equal("read", contract.Reach.Value);
        Assert.Equal("none", contract.Cost.Value);
    }

    [Theory]
    [MemberData(nameof(Published))]
    public void A_repeat_after_a_recovery_read_declares_what_that_read_is(string id)
    {
        var contract = Contract(id);
        if (contract.RepeatAfterAmbiguous != RepeatAfterAmbiguous.AfterRecoveryRead)
        {
            Assert.Null(contract.RecoveryRead);
            return;
        }

        Assert.NotNull(contract.RecoveryRead);
        Assert.NotEmpty(contract.RecoveryRead.Reads);
        Assert.NotEmpty(contract.RecoveryRead.Justification);
    }

    [Theory]
    [MemberData(nameof(Published))]
    public void An_operation_that_acts_on_a_channel_takes_it_as_an_argument(string id)
    {
        var contract = Contract(id);
        var arguments = contract.InputSchema["properties"]?["args"]?["properties"] as JsonObject;

        if (contract.Preflight.Channel == ChannelRequirement.FromArgs)
        {
            Assert.NotNull(arguments);
            Assert.True(arguments.ContainsKey("channel"), $"{id} consumes a channel but its args do not declare one.");
            Assert.Contains("channel", Required(contract.InputSchema["properties"]!["args"]!));
        }
        else
        {
            Assert.False(arguments?.ContainsKey("channel"), $"{id} declares no channel but its args carry one.");
        }
    }

    [Theory]
    [MemberData(nameof(Published))]
    public void An_operation_that_takes_the_contact_takes_exactly_one(string id)
    {
        var contract = Contract(id);
        var contacts = contract.InputSchema["properties"]?["contacts"];

        if (contract.Preflight.Contact == ContactRequirement.NotUsed)
        {
            Assert.Null(contacts);
            return;
        }

        Assert.NotNull(contacts);
        Assert.Equal(1, (int)contacts["minItems"]!);
        Assert.Equal(1, (int)contacts["maxItems"]!);

        // An optional contact is there when the work item names one and absent when it does not, never empty.
        Assert.Equal(contract.Preflight.Contact == ContactRequirement.Required, Required(contract.InputSchema).Contains("contacts"));
    }

    /// <summary>
    /// The declared projection and the schema the plugin is actually handed are one statement, so they are held
    /// against each other rather than each against a list written here: a hardcoded list agrees with a copy of
    /// itself while the two documents drift apart. The declared fields plus the two the projection does not name
    /// — the channels it consumes, when it consumes any, and the pins it may see — are exactly the contact a plugin
    /// receives.
    /// </summary>
    [Theory]
    [MemberData(nameof(Published))]
    public void The_declared_contact_projection_follows_the_pre_flight(string id)
    {
        var contract = Contract(id);

        if (contract.Preflight.Contact == ContactRequirement.NotUsed)
        {
            Assert.Null(contract.ContactProjection);
            Assert.Null(contract.InputSchema["$defs"]?["contact"]);
            return;
        }

        Assert.NotNull(contract.ContactProjection);

        var contact = Assert.IsType<JsonObject>(contract.InputSchema["$defs"]?["contact"]);
        var properties = Assert.IsType<JsonObject>(contact["properties"]);
        var consumed = contract.ContactProjection.Channels == "consumed";

        Assert.Equal(
            contract.ContactProjection.Fields.Concat(consumed ? ["channels", "external_ids"] : ["external_ids"]).Order(StringComparer.Ordinal).ToList(),
            properties.Select(property => property.Key).Order(StringComparer.Ordinal).ToList());

        if (consumed)
        {
            // `consumed` is one channel, not a person's whole reachability, and the schema is what enforces it.
            Assert.Equal(1, (int)properties["channels"]!["maxItems"]!);
        }
    }

    [Theory]
    [MemberData(nameof(Published))]
    public void An_operation_that_requires_an_idempotency_key_takes_one(string id)
    {
        var contract = Contract(id);
        var declared = (contract.InputSchema["properties"] as JsonObject)?.ContainsKey("idempotency_key") == true;

        if (contract.IdempotencyKey.Value == "required")
        {
            Assert.True(declared, $"{id} requires an idempotency key but its input does not carry one.");
            Assert.Contains("idempotency_key", Required(contract.InputSchema));
        }
        else
        {
            Assert.False(declared, $"{id} needs no idempotency key but its input carries one.");
        }
    }

    /// <summary>
    /// An operation that pins something says what each pin identifies. One may pin nothing at all: a thread read by
    /// the identifier the caller already holds links nothing new.
    /// </summary>
    [Theory]
    [MemberData(nameof(Published))]
    public void Every_declared_identifier_says_what_it_identifies(string id)
    {
        var contract = Contract(id);

        Assert.All(contract.ExternalIds, kind =>
        {
            Assert.Matches("^[a-z][a-z0-9_]*$", kind.Key);
            Assert.NotEmpty(kind.Value.Description);
        });
    }

    [Theory]
    [MemberData(nameof(Published))]
    public void Both_schemas_are_written_in_the_published_dialect(string id)
    {
        var contract = Contract(id);

        Assert.Empty(SchemaValidator.CheckDialect(contract.InputSchema));
        Assert.Empty(SchemaValidator.CheckDialect(contract.OutputSchema));
        Assert.Equal("https://json-schema.org/draft/2020-12/schema", (string?)contract.InputSchema["$schema"]);
        Assert.Equal("https://json-schema.org/draft/2020-12/schema", (string?)contract.OutputSchema["$schema"]);
    }

    [Theory]
    [MemberData(nameof(Published))]
    public void The_timeout_is_a_budget_a_person_chose(string id)
    {
        var timeout = Contract(id).TimeoutMs;

        Assert.InRange(timeout, 1_000, 3_600_000);
    }

    [Theory]
    [MemberData(nameof(Published))]
    public void The_origin_names_a_family_of_the_contract_this_catalog_is_seeded_from(string id)
    {
        var origin = Contract(id).L1;

        Assert.Equal("2.0.0", origin.ContractVersion);
        Assert.InRange(origin.Family, 1, 21);
        Assert.NotEmpty(origin.Name);
    }

    [Theory]
    [MemberData(nameof(Published))]
    public void Every_failure_a_plugin_may_report_is_named_with_its_class_and_its_moment(string id)
    {
        var contract = Contract(id);

        Assert.NotEmpty(contract.FailureCodes);
        Assert.All(contract.FailureCodes, failure =>
        {
            Assert.Matches("^[a-z][a-z0-9_]*$", failure.Code);
            Assert.NotEmpty(failure.When);
        });
        Assert.Equal(contract.FailureCodes.Select(f => f.Code).Distinct(StringComparer.Ordinal).Count(), contract.FailureCodes.Count);
    }

    [Theory]
    [MemberData(nameof(Published))]
    public void A_metered_operation_says_what_it_is_metered_on(string id)
    {
        var contract = Contract(id);

        if (contract.Cost.Value == "metered")
        {
            Assert.NotNull(contract.CostBasis);
            Assert.NotNull(contract.Meter);
        }
        else
        {
            Assert.Null(contract.CostBasis);
            Assert.Null(contract.Meter);
        }
    }

    private static IEnumerable<string> Required(JsonNode schema) =>
        schema["required"] is JsonArray names ? names.Select(name => (string)name!) : [];
}
