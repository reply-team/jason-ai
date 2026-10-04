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
    [InlineData("dispatch")]
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
    [InlineData("dispatch", "\"waits\"")]
    [InlineData("dispatch", "{}")]
    [InlineData("dispatch", "{\"in_paused_campaign\": \"sometimes\"}")]
    [InlineData("dispatch", "{\"in_paused_campaign\": \"waits\", \"in_draft_campaign\": \"waits\"}")]
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
        document["preflight"] = JsonNode.Parse("""{ "contact": "optional", "channel": "none", "suppression": "checked" }""");
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
        document["preflight"] = JsonNode.Parse("""{ "contact": "optional", "channel": "from_args", "suppression": "checked" }""");
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
        document["preflight"] = JsonNode.Parse("""{ "contact": "required", "channel": "none", "suppression": "checked" }""");
        document["contact_projection"] = JsonNode.Parse("""{ "fields": ["id"], "channels": "consumed" }""");

        var error = Assert.Throws<InvalidOperationException>(() => OperationContract.Parse("enrollment.list.json", document));

        Assert.Contains("enrollment.list.json", error.Message, StringComparison.Ordinal);
        Assert.Contains("consumed", error.Message, StringComparison.Ordinal);
        Assert.Contains("from_args", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "contact": "sometimes", "channel": "none", "suppression": "checked" }""", """{ "fields": ["id"], "channels": "none" }""", "preflight.contact")]
    [InlineData("""{ "contact": "optional", "channel": "none", "suppression": "checked" }""", """{ "fields": ["id"], "channels": "all" }""", "contact_projection.channels")]
    [InlineData("""{ "contact": "optional", "channel": "none", "suppression": "sometimes" }""", """{ "fields": ["id"], "channels": "none" }""", "preflight.suppression")]
    public void A_word_outside_either_vocabulary_is_refused_by_name(string preflight, string projection, string field)
    {
        var document = (JsonObject)JsonNode.Parse(Minimal())!;
        document["preflight"] = JsonNode.Parse(preflight);
        document["contact_projection"] = JsonNode.Parse(projection);

        var error = Assert.Throws<InvalidOperationException>(() => OperationContract.Parse("conversation.list.json", document));

        Assert.Contains(field, error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the suppression register refuses this operation for a suppressed person was added to documents that
    /// already existed, so it is stated on every one of them: a missing word is not "checked".
    /// </summary>
    [Fact]
    public void A_preflight_that_does_not_say_whether_the_suppression_register_applies_is_refused_by_name()
    {
        var document = (JsonObject)JsonNode.Parse(Minimal())!;
        document["preflight"] = JsonNode.Parse("""{ "contact": "not_used", "channel": "none" }""");

        var error = Assert.Throws<InvalidOperationException>(() => OperationContract.Parse("campaign.get.json", document));

        Assert.Contains("campaign.get.json", error.Message, StringComparison.Ordinal);
        Assert.Contains("preflight.suppression", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An operation that only takes a person out — records that they said stop, or removes them from a campaign —
    /// declares that the register does not apply to it, because refusing it for a suppressed person would refuse
    /// exactly the people it is for.
    /// </summary>
    [Fact]
    public void An_operation_that_reaches_nobody_may_declare_that_the_suppression_register_does_not_apply()
    {
        var document = (JsonObject)JsonNode.Parse(Minimal())!;
        document["reach"] = "control";
        document["preflight"] = JsonNode.Parse("""{ "contact": "required", "channel": "from_args", "suppression": "not_applicable" }""");
        document["contact_projection"] = JsonNode.Parse("""{ "fields": ["id"], "channels": "consumed" }""");

        var contract = OperationContract.Parse("enrollment.stop.json", document);

        Assert.Equal(SuppressionCheck.NotApplicable, contract.Preflight.Suppression);
    }

    /// <summary>
    /// The register protects a person from being reached. An operation that may reach somebody — a read of what
    /// they wrote, or an act whose reach is `act`, even conditionally — is checked against it, whatever it declares.
    /// </summary>
    [Theory]
    [InlineData("\"read\"")]
    [InlineData("\"act\"")]
    [InlineData("""{ "value": "act", "conditional": true, "detail": "`control` while the campaign holds nobody." }""")]
    public void Only_an_operation_whose_reach_is_control_may_set_the_suppression_register_aside(string reach)
    {
        var document = (JsonObject)JsonNode.Parse(Minimal())!;
        document["reach"] = JsonNode.Parse(reach);
        document["preflight"] = JsonNode.Parse("""{ "contact": "required", "channel": "from_args", "suppression": "not_applicable" }""");
        document["contact_projection"] = JsonNode.Parse("""{ "fields": ["id"], "channels": "consumed" }""");

        var error = Assert.Throws<InvalidOperationException>(() => OperationContract.Parse("campaign.enroll.json", document));

        Assert.Contains("preflight.suppression", error.Message, StringComparison.Ordinal);
        Assert.Contains("control", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The incident response and the record that somebody said stop must still reach the provider after somebody
    /// paused Jason's campaign first; an operation that may run there says so, and it is `control` and `auto`.
    /// </summary>
    [Fact]
    public void A_control_operation_nobody_is_asked_about_may_run_in_a_paused_campaign()
    {
        var document = (JsonObject)JsonNode.Parse(Minimal())!;
        document["reach"] = "control";
        document["dispatch"] = JsonNode.Parse("""{ "in_paused_campaign": "runs" }""");

        var contract = OperationContract.Parse("campaign.pause.json", document);

        Assert.Equal(InPausedCampaign.Runs, contract.Dispatch.InPausedCampaign);
    }

    /// <summary>
    /// Pausing a campaign stops its work, and only an operation that reaches nobody and asks nobody may run past
    /// that: a read, an act, or anything a person must approve waits with its campaign.
    /// </summary>
    [Theory]
    [InlineData("\"read\"", "\"auto\"")]
    [InlineData("\"act\"", "\"auto\"")]
    [InlineData("""{ "value": "act", "conditional": true, "detail": "`control` while the campaign holds nobody." }""", "\"auto\"")]
    [InlineData("\"control\"", "\"confirm_once\"")]
    [InlineData("\"control\"", """{ "value": "confirm_once", "conditional": true, "detail": "auto for one person." }""")]
    public void Only_a_control_operation_nobody_approves_may_run_in_a_paused_campaign(string reach, string approval)
    {
        var document = (JsonObject)JsonNode.Parse(Minimal())!;
        document["reach"] = JsonNode.Parse(reach);
        document["approval"] = JsonNode.Parse(approval);
        document["dispatch"] = JsonNode.Parse("""{ "in_paused_campaign": "runs" }""");

        var error = Assert.Throws<InvalidOperationException>(() => OperationContract.Parse("campaign.activate.json", document));

        Assert.Contains("dispatch.in_paused_campaign", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The catalogue's one lowering of an approval: an operation whose not running is the harm runs `auto`, says why,
    /// is not refused for a suppressed person and runs while its campaign is paused.
    /// </summary>
    [Fact]
    public void An_operation_on_the_protective_floor_is_read_with_everything_the_floor_implies()
    {
        var contract = OperationContract.Parse("optout.record.json", Floor());

        Assert.True(contract.ProtectiveFloor);
        Assert.Equal(SuppressionCheck.NotApplicable, contract.Preflight.Suppression);
        Assert.Equal(InPausedCampaign.Runs, contract.Dispatch.InPausedCampaign);
    }

    /// <summary>
    /// The floor is a checked lowering, not a phrase: it is refused where the approval is not `auto`, where no reason
    /// is stated, and where the register or a pause would still hold the operation back.
    /// </summary>
    [Theory]
    [InlineData("approval", "\"confirm_once\"")]
    [InlineData("approval", """{ "value": "confirm_once", "conditional": true, "detail": "auto for one person." }""")]
    [InlineData("approval_reason", "null")]
    [InlineData("approval_reason", "\"   \"")]
    [InlineData("preflight", """{ "contact": "required", "channel": "from_args", "suppression": "checked" }""")]
    [InlineData("dispatch", """{ "in_paused_campaign": "waits" }""")]
    public void The_protective_floor_is_refused_where_anything_it_implies_does_not_hold(string field, string value)
    {
        var document = Floor();
        document[field] = JsonNode.Parse(value);

        var error = Assert.Throws<InvalidOperationException>(() => OperationContract.Parse("optout.record.json", document));

        Assert.Contains("optout.record.json", error.Message, StringComparison.Ordinal);
        Assert.Contains("protective_floor", error.Message, StringComparison.Ordinal);
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

    /// <summary>
    /// Not only the four a gate reads: the catalogue itself makes a key and a per-item report conditional ("when a
    /// collection is given"), so any of the seven properties is read as its dangerous value, its condition and its
    /// detail.
    /// </summary>
    [Theory]
    [InlineData("reach", "act")]
    [InlineData("reversibility", "irreversible")]
    [InlineData("approval", "confirm_once")]
    [InlineData("before_repeating", "the campaign's live state")]
    [InlineData("idempotency_key", "required")]
    [InlineData("per_item_results", "required")]
    [InlineData("cost", "metered")]
    public void Any_of_the_seven_properties_may_be_a_conditional_block(string field, string value)
    {
        var document = (JsonObject)JsonNode.Parse(Minimal())!;
        document[field] = new JsonObject { ["value"] = value, ["conditional"] = true, ["detail"] = "when a collection is given." };

        var contract = OperationContract.Parse("campaign.get.json", document);
        var property = field switch
        {
            "reach" => contract.Reach,
            "reversibility" => contract.Reversibility,
            "approval" => contract.Approval,
            "before_repeating" => contract.BeforeRepeating,
            "idempotency_key" => contract.IdempotencyKey,
            "per_item_results" => contract.PerItemResults,
            _ => contract.Cost,
        };

        Assert.Equal(value, property.Value);
        Assert.True(property.Conditional);
        Assert.Equal("when a collection is given.", property.Detail);
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
        Assert.Equal(SuppressionCheck.Checked, contract.Preflight.Suppression);
        Assert.Equal(InPausedCampaign.Waits, contract.Dispatch.InPausedCampaign);
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

    /// <summary>The smallest document on the protective floor: everything the floor implies, and nothing else changed.</summary>
    private static JsonObject Floor()
    {
        var document = (JsonObject)JsonNode.Parse(Minimal())!;
        document["reach"] = "control";
        document["approval_departs"] = true;
        document["protective_floor"] = true;
        document["approval_reason"] = "the protective floor: waiting for permission to stop is the destructive choice.";
        document["preflight"] = JsonNode.Parse("""{ "contact": "required", "channel": "from_args", "suppression": "not_applicable" }""");
        document["dispatch"] = JsonNode.Parse("""{ "in_paused_campaign": "runs" }""");
        document["contact_projection"] = JsonNode.Parse("""{ "fields": ["id"], "channels": "consumed" }""");
        return document;
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
          "preflight": { "contact": "not_used", "channel": "none", "suppression": "checked" },
          "dispatch": { "in_paused_campaign": "waits" },
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
