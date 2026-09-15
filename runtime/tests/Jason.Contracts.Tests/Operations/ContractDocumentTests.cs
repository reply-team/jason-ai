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
        "reach", "reversibility", "approval", "approval_artefact", "approval_departs", "before_repeating",
        "idempotency_key", "per_item_results", "accepts_collection", "cost", "cost_basis", "meter", "invariants",
        "preflight", "contact_projection", "precondition", "input_schema", "output_schema", "external_ids",
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
    public void The_catalog_publishes_exactly_the_three_operations_of_this_version()
    {
        Assert.Equal(["campaign.enroll", "campaign.get", "list_membership.add"], OperationCatalog.All.Select(c => c.Id));
        Assert.All(OperationCatalog.All, contract => Assert.Equal(1, contract.Version));
    }

    [Theory]
    [MemberData(nameof(Published))]
    public void Every_document_states_every_field_and_nothing_else(string id)
    {
        var document = Document(id);

        Assert.Equal(Members.Order(StringComparer.Ordinal), document.Select(member => member.Key).Order(StringComparer.Ordinal));
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
    public void An_operation_that_needs_the_contact_takes_exactly_one(string id)
    {
        var contract = Contract(id);
        var contacts = contract.InputSchema["properties"]?["contacts"];

        if (contract.Preflight.Contact == ContactRequirement.Required)
        {
            Assert.NotNull(contacts);
            Assert.Equal(1, (int)contacts["minItems"]!);
            Assert.Equal(1, (int)contacts["maxItems"]!);
            Assert.Contains("contacts", Required(contract.InputSchema));
        }
        else
        {
            Assert.Null(contacts);
        }
    }

    [Theory]
    [MemberData(nameof(Published))]
    public void The_declared_contact_projection_follows_the_pre_flight(string id)
    {
        var contract = Contract(id);

        if (contract.Preflight.Contact == ContactRequirement.NotUsed)
        {
            Assert.Null(contract.ContactProjection);
            return;
        }

        Assert.NotNull(contract.ContactProjection);
        Assert.Equal(["id", "first_name", "last_name", "company", "title", "time_zone"], contract.ContactProjection.Fields);
        Assert.Equal("consumed", contract.ContactProjection.Channels);
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

    [Theory]
    [MemberData(nameof(Published))]
    public void Every_declared_identifier_says_what_it_identifies(string id)
    {
        var contract = Contract(id);

        Assert.NotEmpty(contract.ExternalIds);
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
