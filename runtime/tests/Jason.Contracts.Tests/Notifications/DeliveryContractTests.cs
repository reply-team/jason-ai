using System.Text.Json.Nodes;
using Jason.Contracts.Notifications;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;

namespace Jason.Contracts.Tests.Notifications;

/// <summary>
/// The delivery contract: a family of its own beside the canonical operations, never among them. The operation
/// catalog has no notion of kind, so a delivery published there would become a provider operation a work item could
/// name, a route could carry, and a lease would be sized for.
/// </summary>
public sealed class DeliveryContractTests
{
    [Fact]
    public void The_family_publishes_one_contract_notification_send_at_version_1()
    {
        var contract = Assert.Single(DeliveryCatalog.All);

        Assert.Equal("notification.send", contract.Id);
        Assert.Equal(1, contract.Version);
        Assert.Equal(DeliveryContract.CurrentVersion, contract.Version);
        Assert.Equal(15_000, contract.TimeoutMs);
        Assert.Equal(
            [
                ("notification_unreachable", FailureClass.Transient),
                ("notification_refused", FailureClass.Permanent),
                ("notification_unauthorized", FailureClass.Permanent),
            ],
            contract.FailureCodes.Select(code => (code.Code, code.Class)));
        Assert.Same(contract, DeliveryCatalog.Find("notification.send"));
        Assert.True(DeliveryCatalog.Knows("notification.send"));
    }

    [Fact]
    public void A_delivery_is_never_a_canonical_operation()
    {
        Assert.False(OperationCatalog.Knows("notification.send"));
        Assert.DoesNotContain(OperationCatalog.All, contract => contract.Id == "notification.send");
        Assert.Equal(OperationCatalog.All.Max(contract => contract.TimeoutMs), OperationCatalog.Slowest!.TimeoutMs);
        Assert.Equal(3, OperationCatalog.All.Count);
        Assert.False(DeliveryCatalog.Knows("campaign.enroll"));
    }

    [Fact]
    public void Both_of_its_schemas_are_in_the_published_dialect()
    {
        var contract = DeliveryCatalog.Find("notification.send")!;
        Assert.Empty(SchemaValidator.CheckDialect(contract.InputSchema));
        Assert.Empty(SchemaValidator.CheckDialect(contract.OutputSchema));
    }

    [Fact]
    public void A_document_with_a_field_of_its_own_is_refused()
    {
        var document = Document();
        document["reach"] = "write";

        var refused = Assert.Throws<InvalidOperationException>(() => DeliveryContract.Parse("test.json", document));
        Assert.Contains("`reach` is not part of a delivery contract", refused.Message, StringComparison.Ordinal);
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

        var refused = Assert.Throws<InvalidOperationException>(() => DeliveryContract.Parse("test.json", document));
        Assert.Contains($"`{field}` is missing", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_schema_keyword_outside_the_dialect_is_refused()
    {
        var document = Document();
        document["input_schema"]!["patternProperties"] = new JsonObject();

        var refused = Assert.Throws<InvalidOperationException>(() => DeliveryContract.Parse("test.json", document));
        Assert.Contains("input_schema", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>The published document, as a node a test may change.</summary>
    private static JsonObject Document() => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(
        Jason.Contracts.Tests.OperationContracts.ContractFiles.Package, "notifications", "notification.send.json")))!;
}
