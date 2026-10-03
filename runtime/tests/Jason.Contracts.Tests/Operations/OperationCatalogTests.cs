using System.Text.Json.Nodes;
using Jason.Contracts.Operations;

namespace Jason.Contracts.Tests.OperationContracts;

/// <summary>
/// The catalog is the published documents, read once. These facts are about the reading — that a document which
/// says less than the contract requires is a build-time failure naming its file rather than a silent default, and
/// that asking the catalog about an operation nobody published is an ordinary "no".
/// </summary>
public class OperationCatalogTests
{
    [Fact]
    public void Every_published_document_appears_once_and_in_a_settled_order()
    {
        var ids = OperationCatalog.All.Select(c => c.Id).ToList();

        Assert.Equal(ids.Distinct(StringComparer.Ordinal).Count(), ids.Count);
        Assert.Equal(ids.OrderBy(id => id, StringComparer.Ordinal).ToList(), ids);
    }

    [Fact]
    public void Find_and_knows_give_the_same_answer_and_an_unpublished_operation_is_simply_unknown()
    {
        foreach (var contract in OperationCatalog.All)
        {
            Assert.Same(contract, OperationCatalog.Find(contract.Id));
            Assert.True(OperationCatalog.Knows(contract.Id));
        }

        Assert.Null(OperationCatalog.Find("campaign.invent"));
        Assert.False(OperationCatalog.Knows("campaign.invent"));
        Assert.False(OperationCatalog.Knows(""));
    }

    [Fact]
    public void A_document_that_leaves_a_field_out_names_its_file_rather_than_defaulting()
    {
        var document = (JsonObject)JsonNode.Parse(Minimal())!;
        document.Remove("reach");

        var error = Assert.Throws<InvalidOperationException>(() => OperationContract.Parse("campaign.get.json", document));

        Assert.Contains("campaign.get.json", error.Message, StringComparison.Ordinal);
        Assert.Contains("reach", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The floor, its reason and the preview were added to a contract that already had published documents, which
    /// is exactly when a reader would be tempted to treat a missing field as "the old behaviour". It is not.
    /// </summary>
    [Theory]
    [InlineData("protective_floor")]
    [InlineData("approval_reason")]
    [InlineData("preview")]
    public void A_document_that_leaves_out_a_field_added_since_the_first_three_is_refused_by_name(string field)
    {
        var document = (JsonObject)JsonNode.Parse(Minimal())!;
        document.Remove(field);

        var error = Assert.Throws<InvalidOperationException>(() => OperationContract.Parse("campaign.get.json", document));

        Assert.Contains("campaign.get.json", error.Message, StringComparison.Ordinal);
        Assert.Contains($"`{field}` is missing", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("protective_floor", "\"no\"")]
    [InlineData("protective_floor", "null")]
    [InlineData("approval_reason", "false")]
    [InlineData("preview", "\"Enroll somebody\"")]
    [InlineData("preview", "{\"highlights\": []}")]
    [InlineData("preview", "{\"summary\": \"Enroll somebody\"}")]
    [InlineData("preview", "{\"summary\": \"Enroll somebody\", \"highlights\": [1]}")]
    [InlineData("preview", "{\"summary\": \"Enroll somebody\", \"highlights\": [], \"title\": \"x\"}")]
    public void A_new_field_of_the_wrong_shape_is_refused_by_name(string field, string value)
    {
        var document = (JsonObject)JsonNode.Parse(Minimal())!;
        document[field] = JsonNode.Parse(value);

        var error = Assert.Throws<InvalidOperationException>(() => OperationContract.Parse("campaign.get.json", document));

        Assert.Contains(field, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_preview_is_read_as_its_summary_and_the_values_it_highlights()
    {
        var document = (JsonObject)JsonNode.Parse(Minimal())!;
        document["preview"] = JsonNode.Parse("""{"summary": "Read the campaign.", "highlights": ["/campaign/name"]}""");

        var contract = OperationContract.Parse("campaign.get.json", document);

        Assert.NotNull(contract.Preview);
        Assert.Equal(["/campaign/name"], contract.Preview.Highlights);
    }

    /// <summary>
    /// A read that may be about one person or about a whole campaign takes the work item's contact when there is
    /// one and runs without it when there is not. It never takes that person's channels: it reads by the pin.
    /// </summary>
    [Fact]
    public void An_operation_may_take_the_contact_when_there_is_one_and_none_of_its_channels()
    {
        var document = (JsonObject)JsonNode.Parse(Minimal())!;
        document["preflight"] = JsonNode.Parse("""{ "contact": "optional", "channel": "none" }""");
        document["contact_projection"] = JsonNode.Parse("""{ "fields": ["id"], "channels": "none" }""");

        var contract = OperationContract.Parse("conversation.list.json", document);

        Assert.Equal(ContactRequirement.Optional, contract.Preflight.Contact);
        Assert.Equal("none", contract.ContactProjection!.Channels);
    }

    /// <summary>
    /// A channel taken from the arguments is a channel of the contact, so an operation that may run without a
    /// contact has nothing to take it from.
    /// </summary>
    [Fact]
    public void An_optional_contact_with_a_channel_from_the_arguments_is_refused()
    {
        var document = (JsonObject)JsonNode.Parse(Minimal())!;
        document["preflight"] = JsonNode.Parse("""{ "contact": "optional", "channel": "from_args" }""");
        document["contact_projection"] = JsonNode.Parse("""{ "fields": ["id"], "channels": "consumed" }""");

        var error = Assert.Throws<InvalidOperationException>(() => OperationContract.Parse("conversation.list.json", document));

        Assert.Contains("conversation.list.json", error.Message, StringComparison.Ordinal);
        Assert.Contains("optional", error.Message, StringComparison.Ordinal);
        Assert.Contains("from_args", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The one channel a plugin consumes is the one the arguments name. A projection that consumes channels while
    /// the arguments name none would hand the plugin a channel nobody chose.
    /// </summary>
    [Fact]
    public void A_projection_that_consumes_a_channel_the_arguments_do_not_name_is_refused()
    {
        var document = (JsonObject)JsonNode.Parse(Minimal())!;
        document["preflight"] = JsonNode.Parse("""{ "contact": "required", "channel": "none" }""");
        document["contact_projection"] = JsonNode.Parse("""{ "fields": ["id"], "channels": "consumed" }""");

        var error = Assert.Throws<InvalidOperationException>(() => OperationContract.Parse("enrollment.list.json", document));

        Assert.Contains("enrollment.list.json", error.Message, StringComparison.Ordinal);
        Assert.Contains("consumed", error.Message, StringComparison.Ordinal);
        Assert.Contains("from_args", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "contact": "sometimes", "channel": "none" }""", """{ "fields": ["id"], "channels": "none" }""", "preflight.contact")]
    [InlineData("""{ "contact": "optional", "channel": "none" }""", """{ "fields": ["id"], "channels": "all" }""", "contact_projection.channels")]
    public void A_word_outside_either_vocabulary_is_refused_by_name(string preflight, string projection, string field)
    {
        var document = (JsonObject)JsonNode.Parse(Minimal())!;
        document["preflight"] = JsonNode.Parse(preflight);
        document["contact_projection"] = JsonNode.Parse(projection);

        var error = Assert.Throws<InvalidOperationException>(() => OperationContract.Parse("conversation.list.json", document));

        Assert.Contains(field, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_property_may_be_a_plain_value_or_the_conditional_block_whose_value_is_the_dangerous_reading()
    {
        var document = (JsonObject)JsonNode.Parse(Minimal())!;
        document["reach"] = new JsonObject
        {
            ["value"] = "act",
            ["conditional"] = true,
            ["detail"] = "bookkeeping while the campaign is not live, `act` the moment it is.",
        };

        var contract = OperationContract.Parse("campaign.get.json", document);

        Assert.Equal("act", contract.Reach.Value);
        Assert.True(contract.Reach.Conditional);
        Assert.NotNull(contract.Reach.Detail);
        Assert.False(contract.Reversibility.Conditional);
        Assert.Null(contract.Reversibility.Detail);
    }

    [Fact]
    public void A_conditional_block_without_its_detail_is_refused_because_the_detail_is_what_an_agent_reads()
    {
        var document = (JsonObject)JsonNode.Parse(Minimal())!;
        document["cost"] = new JsonObject { ["value"] = "metered", ["conditional"] = true };

        Assert.Throws<InvalidOperationException>(() => OperationContract.Parse("campaign.get.json", document));
    }

    [Fact]
    public void A_document_is_read_into_the_record_the_runtime_reasons_with()
    {
        var contract = OperationContract.Parse("campaign.get.json", (JsonObject)JsonNode.Parse(Minimal())!);

        Assert.Equal("campaign.get", contract.Id);
        Assert.Equal(1, contract.Version);
        Assert.Equal("2.0.0", contract.L1.ContractVersion);
        Assert.Equal(5, contract.L1.Family);
        Assert.True(contract.L1.Core);
        Assert.Equal(ContactRequirement.NotUsed, contract.Preflight.Contact);
        Assert.Equal(ChannelRequirement.None, contract.Preflight.Channel);
        Assert.Null(contract.ContactProjection);
        Assert.Null(contract.RecoveryRead);
        Assert.Equal(RepeatAfterAmbiguous.Safe, contract.RepeatAfterAmbiguous);
        Assert.Equal(PinnedEntity.Campaign, contract.ExternalIds["campaign"].Entity);
        Assert.False(contract.ProtectiveFloor);
        Assert.Null(contract.ApprovalReason);
        Assert.Null(contract.Preview);
        Assert.Equal(60000, contract.TimeoutMs);
        Assert.Equal("provider_answer_lost", Assert.Single(contract.FailureCodes).Code);
    }

    /// <summary>The smallest document that satisfies every field, used to prove what happens when one is taken away.</summary>
    private static string Minimal() =>
        """
        {
          "id": "campaign.get",
          "version": 1,
          "l1": { "name": "campaign.get", "contract_version": "2.0.0", "family": 5, "core": true },
          "intent": "\"Show me this campaign\".",
          "reach": "read",
          "reversibility": "reversible",
          "approval": "auto",
          "approval_artefact": null,
          "approval_departs": false,
          "protective_floor": false,
          "approval_reason": null,
          "before_repeating": "nothing at stake",
          "idempotency_key": "none",
          "per_item_results": "not_applicable",
          "accepts_collection": false,
          "cost": "none",
          "cost_basis": null,
          "meter": null,
          "invariants": [],
          "preflight": { "contact": "not_used", "channel": "none" },
          "contact_projection": null,
          "precondition": "none",
          "preview": null,
          "input_schema": { "type": "object" },
          "output_schema": { "type": "object" },
          "external_ids": { "campaign": { "entity": "campaign", "description": "The provider's own campaign id." } },
          "recovery_read": null,
          "repeat_after_ambiguous": "safe",
          "failure_codes": [
            { "code": "provider_answer_lost", "class": "ambiguous", "when": "The provider was called and no answer arrived." }
          ],
          "timeout_ms": 60000,
          "conformance": [ "A read changes nothing, so a repeat is free." ]
        }
        """;
}
