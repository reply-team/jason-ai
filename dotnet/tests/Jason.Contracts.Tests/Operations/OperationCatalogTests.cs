using System.Text.Json.Nodes;
using Jason.Contracts.Operations;
using Jason.Runtime.Tests;

namespace Jason.Contracts.Tests.OperationContracts;

/// <summary>
/// The catalog is the published documents, read once. These facts are about the reading — that a document which
/// says less than the contract requires is a build-time failure naming its file rather than a silent default, and
/// that asking the catalog about an operation nobody published is an ordinary "no".
/// </summary>
public class OperationCatalogTests
{
    [Fact]
    [Critical(Family.R3, "the catalog's shape")]
    public void Every_published_document_appears_once_and_in_a_settled_order()
    {
        var ids = OperationCatalog.All.Select(c => c.Id).ToList();

        Assert.Equal(ids.Distinct(StringComparer.Ordinal).Count(), ids.Count);
        Assert.Equal(ids.OrderBy(id => id, StringComparer.Ordinal).ToList(), ids);
    }

    /// <summary>
    /// What a provider block holds is read from the documents and nowhere else: every operation whose
    /// <c>dispatch.under_provider_block</c> is <c>waits</c> — the acts, the pause and the opt-out — and nothing beside
    /// them; the take-back of a send, every other control and every read run through a block. The block's trial is
    /// drawn first from the held work that reaches a person, its reach read as the published value.
    /// </summary>
    [Fact]
    public void A_provider_block_holds_the_work_whose_document_waits_and_nothing_else()
    {
        foreach (var contract in OperationCatalog.All)
        {
            var held = contract.Dispatch.UnderProviderBlock == UnderProviderBlock.Waits;
            Assert.True(
                held == OperationCatalog.HeldByProviderBlock.Contains(contract.Id),
                $"'{contract.Id}' (under a provider block: {contract.Dispatch.UnderProviderBlock}) is held by a block: {OperationCatalog.HeldByProviderBlock.Contains(contract.Id)}; expected {held}.");

            var tried = held && string.Equals(contract.Reach.Value, "act", StringComparison.Ordinal);
            Assert.True(
                tried == OperationCatalog.TriedByProviderBlock.Contains(contract.Id),
                $"'{contract.Id}' (reach {contract.Reach.Value}) is a block's trial first: {OperationCatalog.TriedByProviderBlock.Contains(contract.Id)}; expected {tried}.");
        }

        Assert.Contains("message.send", OperationCatalog.HeldByProviderBlock);
        Assert.Contains("campaign.pause", OperationCatalog.HeldByProviderBlock);
        Assert.Contains("optout.record", OperationCatalog.HeldByProviderBlock);
        Assert.DoesNotContain("message.cancel", OperationCatalog.HeldByProviderBlock);
        Assert.DoesNotContain("message.status", OperationCatalog.HeldByProviderBlock);
        Assert.DoesNotContain("conversation.list", OperationCatalog.HeldByProviderBlock);
        Assert.Contains("message.cancel", OperationCatalog.RunsInPausedCampaign);
        Assert.Equal(
            ["campaign.activate", "campaign.enroll", "conversation.reply", "message.send", "social_invitation.send"],
            OperationCatalog.TriedByProviderBlock.Order(StringComparer.Ordinal));
        Assert.Contains(OperationCatalog.All, contract => string.Equals(contract.Reach.Value, "control", StringComparison.Ordinal) && !OperationCatalog.HeldByProviderBlock.Contains(contract.Id));
    }

    /// <summary>
    /// The rule the documents keep: every published operation that reaches a person — its <c>reach</c> read as the
    /// dangerous value, never a list kept by hand — waits under a provider block, and beside them exactly two controls
    /// wait, the pause and the record that somebody said stop, each of which must happen once and keeps its place
    /// rather than end on the provider's refusal. The take-back of a send runs: its moment passes while it waits.
    /// A third control that waits is a change made in this row or not at all.
    /// </summary>
    [Fact]
    public void Every_operation_that_reaches_a_person_waits_under_a_provider_block_and_the_pause_and_the_stop_wait_beside_them()
    {
        foreach (var contract in OperationCatalog.All.Where(contract => string.Equals(contract.Reach.Value, "act", StringComparison.Ordinal)))
        {
            Assert.True(
                contract.Dispatch.UnderProviderBlock == UnderProviderBlock.Waits,
                $"'{contract.Id}' reaches a person and declares `dispatch.under_provider_block: {contract.Dispatch.UnderProviderBlock}`.");
        }

        Assert.Equal(
            ["campaign.pause", "optout.record"],
            OperationCatalog.All
                .Where(contract => !string.Equals(contract.Reach.Value, "act", StringComparison.Ordinal) && contract.Dispatch.UnderProviderBlock == UnderProviderBlock.Waits)
                .Select(contract => contract.Id));
        Assert.Equal(UnderProviderBlock.Runs, OperationCatalog.Find("message.cancel")!.Dispatch.UnderProviderBlock);
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

    /// <summary>
    /// A caller's arguments are measured against the <c>args</c> property alone, and a reference inside it resolves
    /// against the schema it is read in. So the argument schema is that property with the document's definitions
    /// beside it — copies, so the input schema stays exactly the one the document publishes.
    /// </summary>
    [Fact]
    public void The_argument_schema_is_the_arguments_with_the_documents_definitions_beside_them()
    {
        Assert.All(OperationCatalog.All, contract =>
        {
            var published = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(ContractFiles.Operations, contract.Id + ".json")))!;
            var input = published["input_schema"]!;
            var arguments = contract.ArgumentSchema.DeepClone().AsObject();
            var definitions = arguments["$defs"];
            arguments.Remove("$defs");

            Assert.True(
                JsonNode.DeepEquals(input, contract.InputSchema),
                $"{contract.Id}'s input schema is no longer the one its document publishes: {contract.InputSchema.ToJsonString()}");
            Assert.True(
                JsonNode.DeepEquals(input["properties"]!["args"], arguments),
                $"{contract.Id}'s argument schema is not its `args` property: {arguments.ToJsonString()}");
            Assert.True(
                JsonNode.DeepEquals(input["$defs"], definitions),
                $"{contract.Id}'s argument schema does not carry the document's definitions: {definitions?.ToJsonString() ?? "none"}");
            if (contract.InputSchema["$defs"] is { } shared)
            {
                Assert.NotSame(shared, contract.ArgumentSchema["$defs"]);
            }
        });
    }

    /// <summary>
    /// The argument schema is built once, as the contract is, and every request reads that one object: a schema built
    /// on each read would be built on each request.
    /// </summary>
    [Fact]
    public void The_argument_schema_is_built_once_per_contract()
    {
        Assert.All(OperationCatalog.All, contract => Assert.True(
            ReferenceEquals(contract.ArgumentSchema, contract.ArgumentSchema),
            $"{contract.Id} builds its argument schema again on every read."));
    }

    /// <summary>
    /// A copy of a contract with another input schema measures arguments by that schema, not by the original's: the
    /// argument schema follows the input schema it was derived from, and carries none of the old definitions.
    /// </summary>
    [Fact]
    public void A_copy_with_another_input_schema_measures_arguments_by_that_schema()
    {
        var original = OperationCatalog.Find("message.send")!;
        Assert.NotNull(original.ArgumentSchema["$defs"]);

        var other = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject { ["args"] = new JsonObject { ["type"] = "object", ["required"] = new JsonArray("only") } },
        };
        var copy = original with { InputSchema = other };

        Assert.Same(other, copy.InputSchema);
        Assert.True(
            copy.ArgumentSchema["$defs"] is null,
            $"The copy kept the original's definitions: {copy.ArgumentSchema.ToJsonString()}");
        Assert.True(
            JsonNode.DeepEquals(other["properties"]!["args"], copy.ArgumentSchema),
            $"The copy's argument schema is not its own `args` property: {copy.ArgumentSchema.ToJsonString()}");
        Assert.Same(copy.ArgumentSchema, copy.ArgumentSchema);
        Assert.NotSame(original.ArgumentSchema, copy.ArgumentSchema);
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
    [InlineData("channel_arguments")]
    [InlineData("channel_templates")]
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
    [InlineData("dispatch", "{\"in_paused_campaign\": \"sometimes\", \"under_provider_block\": \"runs\"}")]
    [InlineData("dispatch", "{\"in_paused_campaign\": \"waits\"}")]
    [InlineData("dispatch", "{\"in_paused_campaign\": \"waits\", \"in_archived_campaign\": \"sometimes\", \"under_provider_block\": \"runs\"}")]
    [InlineData("dispatch", "{\"in_paused_campaign\": \"waits\", \"in_archived_campaign\": \"cancelled\", \"under_provider_block\": \"sometimes\"}")]
    [InlineData("dispatch", "{\"in_paused_campaign\": \"waits\", \"in_archived_campaign\": \"cancelled\", \"under_provider_block\": \"runs\", \"in_draft_campaign\": \"waits\"}")]
    [InlineData("channel_arguments", "\"send_after\"")]
    [InlineData("channel_arguments", "[1]")]
    [InlineData("channel_arguments", "{}")]
    [InlineData("channel_templates", "\"email\"")]
    [InlineData("channel_templates", "[1]")]
    [InlineData("channel_templates", "{}")]
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
    /// The loader reads a document's codes as words and knows no reserved ones: a document that declares the plugin
    /// host's own code, or the code reserved for a provider's object, loads, and the declared class is then the one the
    /// outcome check holds it to. What keeps the published documents clear of both is a test over them, not the reading.
    /// </summary>
    [Fact]
    public void A_document_that_declares_a_reserved_code_loads_and_its_declared_class_is_the_one_held()
    {
        var document = (JsonObject)JsonNode.Parse(Minimal())!;
        document["failure_codes"]!.AsArray().Add(JsonNode.Parse($$"""{ "code": "{{Jason.Contracts.Plugins.UnjudgedOutcomeCodes.ProviderInputInvalid}}", "class": "permanent", "when": "A document that claims the word." }"""));
        document["failure_codes"]!.AsArray().Add(JsonNode.Parse($$"""{ "code": "{{Jason.Contracts.Plugins.HostOutcomeCodes.PluginException}}", "class": "transient", "when": "A document that claims the host's word." }"""));

        var contract = OperationContract.Parse("campaign.get.json", document);

        Assert.Null(OutcomeContract.CheckFailure(contract, new(Jason.Contracts.Plugins.FailureClass.Permanent, Jason.Contracts.Plugins.UnjudgedOutcomeCodes.ProviderInputInvalid, "m", null, null)));
        Assert.Equal("/error/class", OutcomeContract.CheckFailure(contract, new(Jason.Contracts.Plugins.FailureClass.Validation, Jason.Contracts.Plugins.UnjudgedOutcomeCodes.ProviderInputInvalid, "m", null, null))?.Pointer);
        Assert.Equal("/error/class", OutcomeContract.CheckFailure(contract, new(Jason.Contracts.Plugins.FailureClass.Permanent, Jason.Contracts.Plugins.HostOutcomeCodes.PluginException, "m", null, null))?.Pointer);
    }

    /// <summary>
    /// A read that may be about one person or about a whole campaign takes the work item's contact when there is
    /// one and runs without it when there is not. It never takes that person's channels: it reads by the pin.
    /// </summary>
    [Fact]
    public void An_operation_may_take_the_contact_when_there_is_one_and_none_of_its_channels()
    {
        var document = (JsonObject)JsonNode.Parse(Minimal())!;
        document["preflight"] = JsonNode.Parse("""{ "contact": "optional", "channel": "none", "suppression": "checked", "provider_contact": "pinned_only" }""");
        document["contact_projection"] = JsonNode.Parse("""{ "fields": ["id"], "channels": "none" }""");
        DeclaresContactNotLinked(document);

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
        document["preflight"] = JsonNode.Parse("""{ "contact": "optional", "channel": "from_args", "suppression": "checked", "provider_contact": "pinned_only" }""");
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
        document["preflight"] = JsonNode.Parse("""{ "contact": "required", "channel": "none", "suppression": "checked", "provider_contact": "find_only" }""");
        document["contact_projection"] = JsonNode.Parse("""{ "fields": ["id"], "channels": "consumed" }""");

        var error = Assert.Throws<InvalidOperationException>(() => OperationContract.Parse("enrollment.list.json", document));

        Assert.Contains("enrollment.list.json", error.Message, StringComparison.Ordinal);
        Assert.Contains("consumed", error.Message, StringComparison.Ordinal);
        Assert.Contains("from_args", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "contact": "sometimes", "channel": "none", "suppression": "checked", "provider_contact": "pinned_only" }""", """{ "fields": ["id"], "channels": "none" }""", "preflight.contact")]
    [InlineData("""{ "contact": "optional", "channel": "none", "suppression": "checked", "provider_contact": "pinned_only" }""", """{ "fields": ["id"], "channels": "all" }""", "contact_projection.channels")]
    [InlineData("""{ "contact": "optional", "channel": "none", "suppression": "sometimes", "provider_contact": "pinned_only" }""", """{ "fields": ["id"], "channels": "none" }""", "preflight.suppression")]
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
        document["preflight"] = JsonNode.Parse("""{ "contact": "not_used", "channel": "none", "provider_contact": "not_used" }""");

        var error = Assert.Throws<InvalidOperationException>(() => OperationContract.Parse("campaign.get.json", document));

        Assert.Contains("campaign.get.json", error.Message, StringComparison.Ordinal);
        Assert.Contains("preflight.suppression", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An argument a plugin may take on some of an operation's channels and not on others is named by the document, so a
    /// manifest can say where its plugin takes it and the claim can refuse it everywhere else. The name is read as the
    /// document writes it.
    /// </summary>
    [Fact]
    public void A_document_names_the_arguments_a_plugin_takes_on_some_channels_and_not_on_others()
    {
        var contract = OperationContract.Parse("message.send.json", ChannelArguments("""["send_after"]"""));

        Assert.Equal(["send_after"], contract.ChannelArguments);
        Assert.Null(OperationContract.Parse("campaign.get.json", (JsonObject)JsonNode.Parse(Minimal())!).ChannelArguments);
    }

    /// <summary>
    /// What a manifest lists per channel has to be an argument the document declares, one a caller may leave out, of an
    /// operation whose arguments name the channel: a name the arguments do not carry could never be checked, a required
    /// one would be refused on every channel a plugin does not list, and without a channel in the arguments there is
    /// nothing to list it against.
    /// </summary>
    [Theory]
    [InlineData("""["send_at"]""", "from_args", false, "`send_at`")]
    [InlineData("""["send_after"]""", "none", false, "`from_args`")]
    [InlineData("""["send_after"]""", "from_args", true, "required")]
    [InlineData("""["channel"]""", "from_args", false, "`channel`")]
    [InlineData("""["send_after", "send_after"]""", "from_args", false, "twice")]
    [InlineData("""[]""", "from_args", false, "null")]
    public void An_argument_per_channel_that_the_arguments_cannot_carry_is_refused(string names, string channel, bool required, string expected)
    {
        var document = ChannelArguments(names, required);
        document["preflight"]!["channel"] = channel;
        if (channel == "none")
        {
            document["contact_projection"]!["channels"] = "none";
            document["preflight"]!["provider_contact"] = "find_only";
        }

        var error = Assert.Throws<InvalidOperationException>(() => OperationContract.Parse("message.send.json", document));

        Assert.Contains("message.send.json", error.Message, StringComparison.Ordinal);
        Assert.Contains("channel_arguments", error.Message, StringComparison.Ordinal);
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The channels a message may be a template on are named by the document, so a manifest can say where its plugin
    /// writes one and the claim can refuse one everywhere else. They are read as the document writes them.
    /// </summary>
    [Fact]
    public void A_document_names_the_channels_its_message_may_be_a_template_on()
    {
        var contract = OperationContract.Parse("message.send.json", ChannelTemplates("""["linkedin"]"""));

        Assert.Equal(["linkedin"], contract.ChannelTemplates);
        Assert.Null(OperationContract.Parse("campaign.get.json", (JsonObject)JsonNode.Parse(Minimal())!).ChannelTemplates);
    }

    /// <summary>
    /// What a manifest lists as the channels its plugin writes a template on has to be a channel the arguments name, named
    /// once, of an operation whose message says whether it is a template: a channel the arguments never name could never
    /// be checked, without a channel in the arguments there is nothing to list it against, and a message with no
    /// `compose` is never a template.
    /// </summary>
    [Theory]
    [InlineData("""["fax"]""", "from_args", true, "`fax`")]
    [InlineData("""["email"]""", "none", true, "`from_args`")]
    [InlineData("""["email", "email"]""", "from_args", true, "twice")]
    [InlineData("""[]""", "from_args", true, "null")]
    [InlineData("""["email"]""", "from_args", false, "`compose`")]
    public void A_channel_a_template_is_offered_on_that_the_arguments_cannot_carry_is_refused(string names, string channel, bool compose, string expected)
    {
        var document = ChannelTemplates(names, compose);
        document["preflight"]!["channel"] = channel;
        if (channel == "none")
        {
            document["contact_projection"]!["channels"] = "none";
            document["preflight"]!["provider_contact"] = "find_only";
        }

        var error = Assert.Throws<InvalidOperationException>(() => OperationContract.Parse("message.send.json", document));

        Assert.Contains("message.send.json", error.Message, StringComparison.Ordinal);
        Assert.Contains("channel_templates", error.Message, StringComparison.Ordinal);
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The smallest document about one person whose arguments name the channel and whose message may be a template, with
    /// <paramref name="names"/> as the channels it may be one on; without <paramref name="compose"/> the message never
    /// says whether it is one.
    /// </summary>
    private static JsonObject ChannelTemplates(string names, bool compose = true)
    {
        var document = ChannelArguments("null");
        document["input_schema"]!["properties"]!["args"]!["properties"]!["message"] = JsonNode.Parse(
            compose
                ? """{ "type": "object", "properties": { "compose": { "type": "string", "enum": ["text", "template"] } } }"""
                : """{ "type": "object", "properties": { "body": { "type": "string" } } }""");
        document["channel_templates"] = JsonNode.Parse(names);
        return document;
    }

    /// <summary>
    /// The smallest document about one person whose arguments name the channel and may carry a moment, with
    /// <paramref name="names"/> as its arguments per channel.
    /// </summary>
    private static JsonObject ChannelArguments(string names, bool required = false)
    {
        var document = (JsonObject)JsonNode.Parse(Minimal())!;
        document["preflight"] = JsonNode.Parse("""{ "contact": "required", "channel": "from_args", "suppression": "checked", "provider_contact": "find_or_create" }""");
        document["contact_projection"] = JsonNode.Parse("""{ "fields": ["id"], "channels": "consumed" }""");
        document["input_schema"] = JsonNode.Parse(
            $$"""
            {
              "type": "object",
              "properties": {
                "args": {
                  "type": "object",
                  "required": {{(required ? """["channel", "send_after"]""" : """["channel"]""")}},
                  "properties": {
                    "channel": { "type": "string", "enum": ["email", "linkedin"] },
                    "send_after": { "type": "string", "format": "date-time" }
                  },
                  "additionalProperties": false
                }
              }
            }
            """);
        document["channel_arguments"] = JsonNode.Parse(names);
        DeclaresAContactIdentifier(document);
        return document;
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
        document["preflight"] = JsonNode.Parse("""{ "contact": "required", "channel": "from_args", "suppression": "not_applicable", "provider_contact": "find_only" }""");
        document["contact_projection"] = JsonNode.Parse("""{ "fields": ["id"], "channels": "consumed" }""");
        DeclaresAContactIdentifier(document);

        var contract = OperationContract.Parse("enrollment.stop.json", document);

        Assert.Equal(SuppressionCheck.NotApplicable, contract.Preflight.Suppression);
    }

    /// <summary>
    /// The register protects a person from being reached. An operation that may reach somebody — a read of what
    /// they wrote, or an act whose reach is `act`, even conditionally — is checked against it, whatever it declares.
    /// </summary>
    [Theory]
    [Critical(Family.S1, "register bypass only for control reach")]
    [InlineData("\"read\"")]
    [InlineData("\"act\"")]
    [InlineData("""{ "value": "act", "conditional": true, "detail": "`control` while the campaign holds nobody." }""")]
    public void Only_an_operation_whose_reach_is_control_may_set_the_suppression_register_aside(string reach)
    {
        var document = (JsonObject)JsonNode.Parse(Minimal())!;
        document["reach"] = JsonNode.Parse(reach);
        document["preflight"] = JsonNode.Parse("""{ "contact": "required", "channel": "from_args", "suppression": "not_applicable", "provider_contact": "find_or_create" }""");
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
        document["dispatch"] = JsonNode.Parse("""{ "in_paused_campaign": "runs", "in_archived_campaign": "cancelled", "under_provider_block": "waits" }""");

        var contract = OperationContract.Parse("campaign.pause.json", document);

        Assert.Equal(InPausedCampaign.Runs, contract.Dispatch.InPausedCampaign);
    }

    /// <summary>
    /// Archiving a campaign cancels its work, and only work that runs past a pause may run past an archive: a search's
    /// stop, without which the search goes on spending. Anything else that says it runs in an archived campaign is refused.
    /// </summary>
    [Theory]
    [InlineData("waits", false)]
    [InlineData("runs", true)]
    public void Only_work_that_runs_in_a_paused_campaign_may_run_in_an_archived_one(string paused, bool parses)
    {
        var document = (JsonObject)JsonNode.Parse(Minimal())!;
        document["reach"] = "control";
        document["dispatch"] = JsonNode.Parse($$"""{ "in_paused_campaign": "{{paused}}", "in_archived_campaign": "runs", "under_provider_block": "runs" }""");

        if (parses)
        {
            Assert.Equal(InArchivedCampaign.Runs, OperationContract.Parse("audience.search_cancel.json", document).Dispatch.InArchivedCampaign);
            return;
        }

        var error = Assert.Throws<InvalidOperationException>(() => OperationContract.Parse("audience.search_cancel.json", document));
        Assert.Contains("dispatch.in_archived_campaign", error.Message, StringComparison.Ordinal);
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
        document["dispatch"] = JsonNode.Parse("""{ "in_paused_campaign": "runs", "in_archived_campaign": "cancelled", "under_provider_block": "waits" }""");

        var error = Assert.Throws<InvalidOperationException>(() => OperationContract.Parse("campaign.activate.json", document));

        Assert.Contains("dispatch.in_paused_campaign", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// What an open provider block does to an operation's work is the document's own word, read as written: a read or a
    /// control may run through a block or wait behind it, because a control whose one run must not be lost — a pause, an
    /// opt-out — keeps its place while the provider refuses the account.
    /// </summary>
    [Theory]
    [InlineData("\"read\"", "runs", UnderProviderBlock.Runs)]
    [InlineData("\"control\"", "runs", UnderProviderBlock.Runs)]
    [InlineData("\"control\"", "waits", UnderProviderBlock.Waits)]
    [InlineData("\"act\"", "waits", UnderProviderBlock.Waits)]
    public void A_document_says_whether_its_work_waits_under_a_provider_block(string reach, string value, UnderProviderBlock expected)
    {
        var document = (JsonObject)JsonNode.Parse(Minimal())!;
        document["reach"] = JsonNode.Parse(reach);
        document["dispatch"] = new JsonObject { ["in_paused_campaign"] = "waits", ["in_archived_campaign"] = "cancelled", ["under_provider_block"] = value };

        var contract = OperationContract.Parse("campaign.get.json", document);

        Assert.Equal(expected, contract.Dispatch.UnderProviderBlock);
    }

    /// <summary>
    /// A provider block is the provider refusing to let work reach a person, so an operation that reaches one — its
    /// reach read as the dangerous value — waits behind it, and a document that says it runs through is refused.
    /// </summary>
    [Theory]
    [InlineData("\"act\"")]
    [InlineData("""{ "value": "act", "conditional": true, "detail": "`control` while the campaign holds nobody." }""")]
    public void An_operation_that_reaches_a_person_is_refused_where_it_would_run_through_a_provider_block(string reach)
    {
        var document = (JsonObject)JsonNode.Parse(Minimal())!;
        document["reach"] = JsonNode.Parse(reach);
        document["dispatch"] = JsonNode.Parse("""{ "in_paused_campaign": "waits", "in_archived_campaign": "cancelled", "under_provider_block": "runs" }""");

        var error = Assert.Throws<InvalidOperationException>(() => OperationContract.Parse("campaign.activate.json", document));

        Assert.Contains("campaign.activate.json", error.Message, StringComparison.Ordinal);
        Assert.Contains("`dispatch.under_provider_block` is `runs`, but `reach` is `act`", error.Message, StringComparison.Ordinal);
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
    // Critical: the protective floor's implications.
    [InlineData("approval", "\"confirm_once\"", Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.S2)])]
    [InlineData("approval", """{ "value": "confirm_once", "conditional": true, "detail": "auto for one person." }""")]
    [InlineData("approval_reason", "null")]
    [InlineData("approval_reason", "\"   \"")]
    [InlineData("preflight", """{ "contact": "required", "channel": "from_args", "suppression": "checked", "provider_contact": "find_or_create" }""")]
    [InlineData("dispatch", """{ "in_paused_campaign": "waits", "in_archived_campaign": "cancelled", "under_provider_block": "waits" }""")]
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

    /// <summary>
    /// A recovery read says whether it settles a lost answer by the work item's key, and says nothing where it does not:
    /// absent is false, so a document that recovers by a read that never sends is written as it always was.
    /// </summary>
    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData(null, false)]
    public void A_recovery_read_says_whether_it_settles_a_lost_answer_by_the_key(string? byKey, bool expected)
    {
        var contract = OperationContract.Parse("campaign.get.json", RecoveredBy(byKey, "after_recovery_read"));

        Assert.Equal(expected, contract.RecoveryRead!.ByKey);
    }

    /// <summary>
    /// The key settles a lost answer only for an operation that repeats after its recovery read: where nothing repeats,
    /// the promise asks a manifest for something no attempt relies on, so it is refused; and it is a flag, nothing else.
    /// </summary>
    [Theory]
    [InlineData("true", "safe", "`safe`")]
    [InlineData("true", "never", "`never`")]
    [InlineData("[1]", "after_recovery_read", "true or false")]
    public void A_recovery_read_by_the_key_is_refused_where_the_operation_does_not_repeat_after_it(string byKey, string repeat, string expected)
    {
        var error = Assert.Throws<InvalidOperationException>(() => OperationContract.Parse("campaign.get.json", RecoveredBy(byKey, repeat)));

        Assert.Contains("campaign.get.json", error.Message, StringComparison.Ordinal);
        Assert.Contains("by_key", error.Message, StringComparison.Ordinal);
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    /// <summary>The smallest document with a recovery read, its <c>by_key</c> as written or left out, and its repeat rule.</summary>
    private static JsonObject RecoveredBy(string? byKey, string repeat)
    {
        var document = (JsonObject)JsonNode.Parse(Minimal())!;
        var recoveryRead = new JsonObject { ["reads"] = "the provider's record", ["justification"] = "a second effect costs" };
        if (byKey is not null)
        {
            recoveryRead["by_key"] = JsonNode.Parse(byKey);
        }

        document["recovery_read"] = recoveryRead;
        document["repeat_after_ambiguous"] = repeat;
        return document;
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
        Assert.Equal(ProviderContact.NotUsed, contract.Preflight.ProviderContact);
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

    /// <summary>
    /// How the provider's own contact for the person is found is stated on every document, in one of four words: a word
    /// left out is not the old behaviour, and a word this build does not know is refused rather than read as the nearest
    /// one.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("find")]
    [InlineData("FIND_ONLY")]
    public void How_the_provider_s_contact_is_found_is_one_of_four_words_and_never_left_out(string? word)
    {
        var document = (JsonObject)JsonNode.Parse(Minimal())!;
        var preflight = document["preflight"]!.AsObject();
        preflight.Remove("provider_contact");
        if (word is not null)
        {
            preflight["provider_contact"] = word;
        }

        var error = Assert.Throws<InvalidOperationException>(() => OperationContract.Parse("campaign.get.json", document));

        Assert.Contains("campaign.get.json", error.Message, StringComparison.Ordinal);
        Assert.Contains("`preflight.provider_contact`", error.Message, StringComparison.Ordinal);
        Assert.Contains(word is null ? "is missing" : "find_or_create, find_only, pinned_only, not_used", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A document that works by the pin alone has to say what a person nobody pinned is: `contact_not_linked`, decided
    /// before the provider is asked anything. Without the word a plugin would invent one, or find the person by address.
    /// </summary>
    [Fact]
    public void A_document_that_works_by_the_pin_alone_and_declares_no_contact_not_linked_is_refused()
    {
        var document = (JsonObject)JsonNode.Parse(Minimal())!;
        document["preflight"] = JsonNode.Parse("""{ "contact": "optional", "channel": "none", "suppression": "checked", "provider_contact": "pinned_only" }""");
        document["contact_projection"] = JsonNode.Parse("""{ "fields": ["id"], "channels": "none" }""");

        var error = Assert.Throws<InvalidOperationException>(() => OperationContract.Parse("conversation.list.json", document));

        Assert.Contains("conversation.list.json", error.Message, StringComparison.Ordinal);
        Assert.Contains("`pinned_only`", error.Message, StringComparison.Ordinal);
        Assert.Contains("`contact_not_linked`", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A document that finds the person — creating them where the provider holds nobody, or never — has the plugin return
    /// the provider's identifier for whoever it found, so it declares that kind; otherwise the one trace of who the work
    /// was about would be refused as an identifier the operation never declared.
    /// </summary>
    [Theory]
    [InlineData("find_or_create", ProviderContact.FindOrCreate)]
    [InlineData("find_only", ProviderContact.FindOnly)]
    public void A_document_that_finds_the_person_and_declares_no_contact_identifier_is_refused(string mode, ProviderContact read)
    {
        var document = (JsonObject)JsonNode.Parse(Minimal())!;
        document["reach"] = "control";
        document["preflight"] = JsonNode.Parse($$"""{ "contact": "required", "channel": "from_args", "suppression": "checked", "provider_contact": "{{mode}}" }""");
        document["contact_projection"] = JsonNode.Parse("""{ "fields": ["id"], "channels": "consumed" }""");

        var error = Assert.Throws<InvalidOperationException>(() => OperationContract.Parse("list_membership.add.json", document));

        Assert.Contains("list_membership.add.json", error.Message, StringComparison.Ordinal);
        Assert.Contains($"`{mode}`", error.Message, StringComparison.Ordinal);
        Assert.Contains("`external_ids.contact`", error.Message, StringComparison.Ordinal);

        DeclaresAContactIdentifier(document);
        Assert.Equal(read, OperationContract.Parse("list_membership.add.json", document).Preflight.ProviderContact);
    }

    /// <summary>
    /// A way that looks the person up by a value, and creates them where nobody matches, needs the value: the channel the
    /// arguments name. A document that creates the person and takes no channel would hand the plugin nothing to look up
    /// or to create from. <c>find_only</c> is not paired: it finds by that value or through what the operation names, a
    /// thread for one, so a document of that word may take no channel and still loads.
    /// </summary>
    [Theory]
    [InlineData("none", "find_or_create", ProviderContact.FindOrCreate, false)]
    [InlineData("from_args", "find_or_create", ProviderContact.FindOrCreate, true)]
    [InlineData("none", "find_only", ProviderContact.FindOnly, true)]
    public void A_document_that_creates_the_person_names_the_channel_it_looks_them_up_by(string channel, string mode, ProviderContact read, bool loads)
    {
        var document = (JsonObject)JsonNode.Parse(Minimal())!;
        document["reach"] = "control";
        document["preflight"] = JsonNode.Parse($$"""{ "contact": "required", "channel": "{{channel}}", "suppression": "checked", "provider_contact": "{{mode}}" }""");
        document["contact_projection"] = JsonNode.Parse($$"""{ "fields": ["id"], "channels": "{{(channel == "none" ? "none" : "consumed")}}" }""");
        DeclaresAContactIdentifier(document);

        if (loads)
        {
            Assert.Equal(read, OperationContract.Parse("list_membership.add.json", document).Preflight.ProviderContact);
            return;
        }

        var error = Assert.Throws<InvalidOperationException>(() => OperationContract.Parse("list_membership.add.json", document));

        Assert.Contains("list_membership.add.json", error.Message, StringComparison.Ordinal);
        Assert.Contains("`find_or_create`", error.Message, StringComparison.Ordinal);
        Assert.Contains("`preflight.channel`", error.Message, StringComparison.Ordinal);
        Assert.Contains("`from_args`", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The identifier a finding document declares is the provider's contact for the person. A kind named
    /// <c>contact</c> that declares another entity — a campaign's identifier — is no declaration of it, and is refused as
    /// the document that declares none is.
    /// </summary>
    [Theory]
    [InlineData("find_or_create")]
    [InlineData("find_only")]
    public void A_document_that_finds_the_person_and_declares_its_contact_identifier_for_another_entity_is_refused(string mode)
    {
        var document = (JsonObject)JsonNode.Parse(Minimal())!;
        document["reach"] = "control";
        document["preflight"] = JsonNode.Parse($$"""{ "contact": "required", "channel": "from_args", "suppression": "checked", "provider_contact": "{{mode}}" }""");
        document["contact_projection"] = JsonNode.Parse("""{ "fields": ["id"], "channels": "consumed" }""");
        document["external_ids"]!["contact"] = JsonNode.Parse("""{ "entity": "campaign", "description": "The provider's campaign, under the person's kind." }""");

        var error = Assert.Throws<InvalidOperationException>(() => OperationContract.Parse("list_membership.add.json", document));

        Assert.Contains("list_membership.add.json", error.Message, StringComparison.Ordinal);
        Assert.Contains($"`{mode}`", error.Message, StringComparison.Ordinal);
        Assert.Contains("`external_ids.contact`", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A document says it finds no provider's contact exactly when it takes no contact: one about nobody has nobody to
    /// find, and one about a person has to say how the provider's record of that person is found.
    /// </summary>
    [Theory]
    [InlineData("not_used", "find_only")]
    [InlineData("not_used", "pinned_only")]
    [InlineData("required", "not_used")]
    [InlineData("optional", "not_used")]
    public void Not_used_is_said_of_exactly_the_documents_that_take_no_contact(string contact, string mode)
    {
        var document = (JsonObject)JsonNode.Parse(Minimal())!;
        document["preflight"] = JsonNode.Parse($$"""{ "contact": "{{contact}}", "channel": "none", "suppression": "checked", "provider_contact": "{{mode}}" }""");
        document["contact_projection"] = contact == "not_used" ? null : JsonNode.Parse("""{ "fields": ["id"], "channels": "none" }""");
        DeclaresAContactIdentifier(document);
        DeclaresContactNotLinked(document);

        var error = Assert.Throws<InvalidOperationException>(() => OperationContract.Parse("enrollment.list.json", document));

        Assert.Contains("enrollment.list.json", error.Message, StringComparison.Ordinal);
        Assert.Contains($"`preflight.provider_contact` is `{mode}`", error.Message, StringComparison.Ordinal);
        Assert.Contains($"`preflight.contact` is `{contact}`", error.Message, StringComparison.Ordinal);
    }

    /// <summary>The kind a document declares when its plugin returns the provider's identifier for the person.</summary>
    private static void DeclaresAContactIdentifier(JsonObject document) =>
        document["external_ids"]!["contact"] = JsonNode.Parse("""{ "entity": "contact", "description": "The provider's own contact id." }""");

    /// <summary>The word a document declares for a person nobody pinned, where it works by the pin alone.</summary>
    private static void DeclaresContactNotLinked(JsonObject document) =>
        document["failure_codes"]!.AsArray().Add(JsonNode.Parse("""{ "code": "contact_not_linked", "class": "permanent", "when": "Nobody pinned the person, and a read creates nobody." }"""));

    /// <summary>The smallest document on the protective floor: everything the floor implies, and nothing else changed.</summary>
    private static JsonObject Floor()
    {
        var document = (JsonObject)JsonNode.Parse(Minimal())!;
        document["reach"] = "control";
        document["approval_departs"] = true;
        document["protective_floor"] = true;
        document["approval_reason"] = "the protective floor: waiting for permission to stop is the destructive choice.";
        document["preflight"] = JsonNode.Parse("""{ "contact": "required", "channel": "from_args", "suppression": "not_applicable", "provider_contact": "find_or_create" }""");
        document["dispatch"] = JsonNode.Parse("""{ "in_paused_campaign": "runs", "in_archived_campaign": "cancelled", "under_provider_block": "waits" }""");
        document["contact_projection"] = JsonNode.Parse("""{ "fields": ["id"], "channels": "consumed" }""");
        DeclaresAContactIdentifier(document);
        return document;
    }

    /// <summary>
    /// The approval is a closed vocabulary: `auto`, which a runtime may give itself, and the two a person gives. A word
    /// outside it is refused by name rather than read as whichever of them it happens not to equal, because reading a
    /// stricter word as a looser one would ask a person for less than the document means.
    /// </summary>
    [Theory]
    [Critical(Family.S2, "approval vocabulary is closed")]
    [InlineData("\"confirm_twice\"")]
    [InlineData("\"none\"")]
    [InlineData("""{ "value": "confirm_every_attempt", "conditional": true, "detail": "a word this build has never read." }""")]
    public void An_approval_outside_the_three_words_this_build_knows_is_refused_by_name(string approval)
    {
        var document = (JsonObject)JsonNode.Parse(Minimal())!;
        document["approval"] = JsonNode.Parse(approval);

        var error = Assert.Throws<InvalidOperationException>(() => OperationContract.Parse("campaign.get.json", document));

        Assert.Contains("`approval`", error.Message, StringComparison.Ordinal);
        Assert.Contains("`confirm_each`", error.Message, StringComparison.Ordinal);
        Assert.Equal(["auto", "confirm_once", "confirm_each"], OperationContract.ApprovalWords);
    }

    /// <summary>
    /// `confirm_each` is one decision per item, each its own act against a named person, shown the artefact that reaches
    /// them. So a document that says it names one person — a required contact, no collection — and its preview highlights
    /// the value that person is reached at. The published reply does; take either away and the document is refused.
    /// </summary>
    [Theory]
    [Critical(Family.S2, "confirm_each names the one person a decision reaches")]
    [InlineData("contact")]
    [InlineData("accepts_collection")]
    [InlineData("preview")]
    public void A_decision_per_person_names_the_one_person_it_reaches(string taken)
    {
        var document = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(ContractFiles.Operations, "conversation.reply.json")))!;
        Assert.Equal("confirm_each", (string?)document["approval"]);
        Assert.NotNull(OperationContract.Parse("conversation.reply.json", document.DeepClone()));

        if (taken == "contact")
        {
            document["preflight"]!["contact"] = "optional";
        }
        else if (taken == "accepts_collection")
        {
            document["accepts_collection"] = true;
        }
        else
        {
            var highlights = document["preview"]!["highlights"]!.AsArray();
            highlights.Remove(highlights.Single(pointer => (string?)pointer == "/contacts/0/channels/0/value"));
        }

        var error = Assert.Throws<InvalidOperationException>(() => OperationContract.Parse("conversation.reply.json", document));

        Assert.Contains("`confirm_each`", error.Message, StringComparison.Ordinal);
        Assert.Contains("/contacts/0/channels/0/value", error.Message, StringComparison.Ordinal);
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
          "preflight": { "contact": "not_used", "channel": "none", "suppression": "checked", "provider_contact": "not_used" },
          "channel_arguments": null,
          "channel_templates": null,
          "dispatch": { "in_paused_campaign": "waits", "in_archived_campaign": "cancelled", "under_provider_block": "waits" },
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
