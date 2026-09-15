using System.Text.Json.Nodes;
using Jason.Contracts.Operations;

namespace Jason.Contracts.Tests.OperationContracts;

/// <summary>
/// What the runtime makes of an answer the protocol already accepted. A well-formed outcome whose content is
/// nonsense for the operation is a defect in the plugin, and pinning an identifier of a kind nobody declared would
/// put a value into Jason's record that nothing later knows how to read.
/// </summary>
public class OutcomeContractTests
{
    private static readonly OperationContract Add = OperationCatalog.Find("list_membership.add")!;
    private static readonly OperationContract Get = OperationCatalog.Find("campaign.get")!;

    private static JsonNode Result(string json) => JsonNode.Parse(json)!;

    [Fact]
    public void A_result_of_the_promised_shape_passes()
    {
        var result = Result("""
            {"items":[{"contact_id":"cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD","status":"added","external_ids":{"contact":"p_88421"}}]}
            """);

        Assert.Empty(OutcomeContract.CheckResult(Add, result));
    }

    [Fact]
    public void A_result_of_another_shape_is_reported_with_the_pointers_that_name_the_trouble()
    {
        var result = Result("""{"items":[{"contact_id":"cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD","status":"joined"}]}""");

        var problems = OutcomeContract.CheckResult(Add, result);

        Assert.Equal("/items/0/status", Assert.Single(problems).Pointer);
        Assert.Equal("enum", problems[0].Reason);
    }

    [Fact]
    public void A_missing_result_is_reported_rather_than_read_as_an_empty_answer()
    {
        var problems = OutcomeContract.CheckResult(Get, null);

        Assert.NotEmpty(problems);
        Assert.Equal("", problems[0].Pointer);
    }

    [Fact]
    public void An_identifier_of_a_declared_kind_is_accepted()
    {
        Assert.Empty(OutcomeContract.CheckExternalIds(Add, new JsonObject { ["contact"] = "p_88421" }));
    }

    [Fact]
    public void An_identifier_of_a_kind_the_operation_never_declared_is_refused()
    {
        var problems = OutcomeContract.CheckExternalIds(Add, new JsonObject { ["contact"] = "p_1", ["enrollment"] = "e_1" });

        var problem = Assert.Single(problems);
        Assert.Equal("/external_ids/enrollment", problem.Pointer);
        Assert.Equal("additional_properties", problem.Reason);
        Assert.Contains("list_membership.add", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_identifier_that_is_not_a_string_is_refused_because_it_travels_exactly_as_written()
    {
        var problems = OutcomeContract.CheckExternalIds(Add, new JsonObject { ["contact"] = 88421 });

        Assert.Equal("type", Assert.Single(problems).Reason);
    }

    [Fact]
    public void An_identifier_that_is_empty_or_longer_than_the_protocol_carries_is_refused()
    {
        Assert.Equal("min_length", Assert.Single(OutcomeContract.CheckExternalIds(Add, new JsonObject { ["contact"] = "" })).Reason);
        Assert.Equal(
            "max_length",
            Assert.Single(OutcomeContract.CheckExternalIds(Add, new JsonObject { ["contact"] = new string('p', 257) })).Reason);
    }

    [Fact]
    public void Returning_no_identifiers_at_all_is_an_ordinary_answer()
    {
        Assert.Empty(OutcomeContract.CheckExternalIds(Add, null));
        Assert.Empty(OutcomeContract.CheckExternalIds(Add, new JsonObject()));
    }
}
