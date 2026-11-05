using System.Text.Json.Nodes;
using Jason.Contracts.Events;
using Jason.Contracts.Notifications;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Contracts.Subscriptions;

namespace Jason.Contracts.Tests.Subscriptions;

/// <summary>
/// The subscription contract: a family of its own beside the canonical operations, the delivery contract and the events
/// contract, never among them. A handler is handed one journal line after the runtime wrote it, so it is no work a work
/// item could name, a route could carry or a lease would be sized for, and nothing it answers changes what was done.
/// </summary>
public sealed class SubscriptionContractTests
{
    [Fact]
    public void The_family_publishes_one_contract_subscription_handle_at_version_1()
    {
        var contract = Assert.Single(SubscriptionCatalog.All);

        Assert.Equal("subscription.handle", contract.Id);
        Assert.Equal(1, contract.Version);
        Assert.Equal(SubscriptionContract.CurrentVersion, contract.Version);
        Assert.Equal(3_600_000, contract.TimeoutMs);
        Assert.Equal(
            [
                ("subscription_answer_invalid", FailureClass.Validation),
                ("subscription_envelope_too_large", FailureClass.Validation),
            ],
            contract.FailureCodes.Select(code => (code.Code, code.Class)));
        Assert.Same(contract, SubscriptionCatalog.Find("subscription.handle"));
        Assert.True(SubscriptionCatalog.Knows("subscription.handle"));
    }

    [Fact]
    public void A_handler_is_neither_a_canonical_operation_a_delivery_nor_an_events_read()
    {
        Assert.False(OperationCatalog.Knows("subscription.handle"));
        Assert.False(DeliveryCatalog.Knows("subscription.handle"));
        Assert.False(EventsCatalog.Knows("subscription.handle"));
        Assert.False(SubscriptionCatalog.Knows("events.read"));
        Assert.False(SubscriptionCatalog.Knows("notification.send"));
        Assert.False(SubscriptionCatalog.Knows("conversation.list"));
        Assert.Equal(Directory.EnumerateFiles(Path.Combine(OperationContracts.ContractFiles.Package, "subscriptions"), "*.json").Count(), SubscriptionCatalog.All.Count);
    }

    /// <summary>The two codes the runtime records against a handler whose answer breaks the contract are the document's.</summary>
    [Fact]
    public void The_codes_the_runtime_records_are_the_two_the_document_declares()
    {
        var contract = SubscriptionCatalog.Find("subscription.handle")!;

        Assert.Equal(
            [SubscriptionContract.AnswerInvalid, SubscriptionContract.EnvelopeTooLarge],
            contract.FailureCodes.Select(code => code.Code));
    }

    [Fact]
    public void Both_of_its_schemas_are_in_the_published_dialect()
    {
        var contract = SubscriptionCatalog.Find("subscription.handle")!;
        Assert.Empty(SchemaValidator.CheckDialect(contract.InputSchema));
        Assert.Empty(SchemaValidator.CheckDialect(contract.OutputSchema));
    }

    [Fact]
    public void A_document_with_a_field_of_its_own_is_refused()
    {
        var document = Document();
        document["reach"] = "none";

        var refused = Assert.Throws<InvalidOperationException>(() => SubscriptionContract.Parse("test.json", document));
        Assert.Contains("`reach` is not part of a subscription contract", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("version")]
    [InlineData("intent")]
    [InlineData("input_schema")]
    [InlineData("output_schema")]
    [InlineData("failure_codes")]
    [InlineData("timeout_ms")]
    [InlineData("conformance")]
    public void A_document_missing_a_field_is_refused(string field)
    {
        var document = Document();
        document.Remove(field);

        var refused = Assert.Throws<InvalidOperationException>(() => SubscriptionContract.Parse("test.json", document));
        Assert.Contains($"`{field}` is missing", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_schema_outside_the_dialect_is_refused_as_the_document_loads()
    {
        var document = Document();
        document["output_schema"]!["oneOf"] = new JsonArray();

        var refused = Assert.Throws<InvalidOperationException>(() => SubscriptionContract.Parse("test.json", document));
        Assert.Contains("`output_schema` is outside the published dialect", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>The answer is the envelope for the next handler and nothing else: an object, always present.</summary>
    [Fact]
    public void An_answer_is_an_envelope_object_and_nothing_beside_it()
    {
        var contract = SubscriptionCatalog.Find("subscription.handle")!;

        Assert.Empty(contract.CheckResult(new JsonObject { ["envelope"] = new JsonObject { ["seen"] = 1 } }));
        Assert.Empty(contract.CheckResult(new JsonObject { ["envelope"] = new JsonObject() }));
        Assert.Equal([("/envelope", "required")], contract.CheckResult(new JsonObject()).Select(Problem));
        Assert.Equal([("/envelope", "type")], contract.CheckResult(new JsonObject { ["envelope"] = new JsonArray() }).Select(Problem));
        Assert.Equal([("/envelope", "type")], contract.CheckResult(new JsonObject { ["envelope"] = "text" }).Select(Problem));
        Assert.Equal(
            [("/note", "additional_properties")],
            contract.CheckResult(new JsonObject { ["envelope"] = new JsonObject(), ["note"] = "x" }).Select(Problem));
    }

    /// <summary>A handler pins nothing: any identifier beside its answer is refused, whatever its kind.</summary>
    [Fact]
    public void An_answer_carrying_identifiers_is_refused_whatever_their_kind()
    {
        var problems = SubscriptionContract.CheckExternalIds(new JsonObject { ["campaign"] = "p_1", ["contact"] = "c_1" });

        Assert.Equal(["/external_ids/campaign", "/external_ids/contact"], problems.Select(problem => problem.Pointer));
        Assert.All(problems, problem => Assert.Equal("additional_properties", problem.Reason));
        Assert.Empty(SubscriptionContract.CheckExternalIds(null));
        Assert.Empty(SubscriptionContract.CheckExternalIds([]));
    }

    /// <summary>
    /// The input is the line, the envelope the previous handler returned and the attempt, from one: the line as
    /// <c>journal.list</c> serializes it, so the four members every line has are required and the rest is the line's own.
    /// </summary>
    [Fact]
    public void The_input_is_a_line_an_envelope_and_an_attempt_from_one()
    {
        var contract = SubscriptionCatalog.Find("subscription.handle")!;
        var line = new JsonObject { ["id"] = "jrn_7", ["ts"] = "2026-10-16T09:00:00.000Z", ["actor"] = new JsonObject { ["type"] = "system" }, ["kind"] = "workitem_failed", ["campaign_id"] = null };

        Assert.Empty(contract.CheckInput(new JsonObject { ["line"] = line.DeepClone(), ["envelope"] = new JsonObject(), ["attempt"] = 1 }));
        Assert.Equal(
            [("/attempt", "minimum")],
            contract.CheckInput(new JsonObject { ["line"] = line.DeepClone(), ["envelope"] = new JsonObject(), ["attempt"] = 0 }).Select(Problem));
        Assert.Equal(
            [("/line/kind", "required")],
            contract.CheckInput(new JsonObject { ["line"] = new JsonObject { ["id"] = "jrn_7", ["ts"] = "2026-10-16T09:00:00.000Z", ["actor"] = new JsonObject() }, ["envelope"] = new JsonObject(), ["attempt"] = 1 }).Select(Problem));
    }

    /// <summary>
    /// Only a transient failure is tried again, by the same handler with the envelope as it stood before it; every other
    /// class ends the chain at that handler. An ambiguous end is not repeated by the runtime: the handler's own
    /// <c>attempt</c> is how it tells a repeat apart, and a repeat comes only from a restart or a rewind.
    /// </summary>
    [Theory]
    [InlineData(FailureClass.Transient, true)]
    [InlineData(FailureClass.Permanent, false)]
    [InlineData(FailureClass.Validation, false)]
    [InlineData(FailureClass.Blocked, false)]
    [InlineData(FailureClass.Ambiguous, false)]
    public void Only_a_transient_failure_is_tried_again(FailureClass failureClass, bool retriable)
    {
        Assert.Equal(retriable, SubscriptionContract.Retriable(failureClass));
    }

    private static (string Pointer, string Reason) Problem(SchemaProblem problem) => (problem.Pointer, problem.Reason);

    /// <summary>The published document, as a node a test may change.</summary>
    private static JsonObject Document() => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(
        OperationContracts.ContractFiles.Package, "subscriptions", "subscription.handle.json")))!;
}
