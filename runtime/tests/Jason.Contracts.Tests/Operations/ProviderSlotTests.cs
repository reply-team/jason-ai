using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Operations;

namespace Jason.Contracts.Tests.OperationContracts;

/// <summary>
/// The one key of an operation's arguments no document declares: <c>provider</c>, the provider's own object. The
/// runtime admits it for every operation, holds it to being an object and to nothing else, and hands it on as written;
/// every other argument is checked exactly as the document says, and no document is rewritten to make room for it.
/// </summary>
public class ProviderSlotTests
{
    private const string Get = "campaign.get";

    private static OperationContract Contract(string id) => OperationCatalog.Find(id)!;

    private static JsonObject Arguments(JsonNode? provider) => new()
    {
        ["campaign"] = new JsonObject { ["external_id"] = "c_7714" },
        ["provider"] = provider,
    };

    private static JsonObject Composed(JsonObject arguments) => new()
    {
        ["args"] = arguments,
        ["campaign"] = new JsonObject
        {
            ["id"] = "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRE",
            ["name"] = "Q3 LatAm founders",
            ["status"] = "active",
            ["external_ids"] = new JsonObject { ["campaign"] = "c_7714" },
        },
    };

    private static JsonObject Nested() => new()
    {
        ["send_window"] = new JsonObject { ["days"] = new JsonArray("mon", "tue"), ["label"] = "café de mañana" },
        ["notes"] = new JsonArray(1, true, null),
    };

    [Fact]
    public void An_object_in_the_slot_passes_an_operation_whose_arguments_admit_nothing_else()
    {
        var contract = Contract(Get);
        Assert.True(contract.ArgumentSchema["additionalProperties"]?.GetValue<bool>() == false, $"{Get} no longer closes its arguments.");

        Assert.Empty(contract.CheckArguments(Arguments(Nested()), PatternTiming.Default));
        Assert.Empty(contract.CheckInput(Composed(Arguments(Nested())), PatternTiming.Default));
    }

    [Theory]
    [InlineData("\"send on Tuesdays\"", "string")]
    [InlineData("[\"mon\"]", "array")]
    [InlineData("7", "number")]
    [InlineData("null", "null")]
    [InlineData("true", "boolean")]
    public void Anything_but_an_object_in_the_slot_is_refused_at_the_slot_and_nowhere_else(string written, string kind)
    {
        var contract = Contract(Get);

        var arguments = Assert.Single(contract.CheckArguments(Arguments(JsonNode.Parse(written)), PatternTiming.Default));
        var composed = Assert.Single(contract.CheckInput(Composed(Arguments(JsonNode.Parse(written))), PatternTiming.Default));

        Assert.Equal(("/provider", "type"), (arguments.Pointer, arguments.Reason));
        Assert.Equal(("/args/provider", "type"), (composed.Pointer, composed.Reason));
        Assert.Contains("object", arguments.Message, StringComparison.Ordinal);
        Assert.Contains(kind, arguments.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Tuesdays", arguments.Message, StringComparison.Ordinal);
    }

    /// <summary>An empty object is an object: the slot holds nothing the runtime asks for.</summary>
    [Fact]
    public void An_empty_object_in_the_slot_passes()
    {
        var contract = Contract(Get);

        Assert.Empty(contract.CheckArguments(Arguments(new JsonObject()), PatternTiming.Default));
        Assert.Empty(contract.CheckInput(Composed(Arguments(new JsonObject())), PatternTiming.Default));
    }

    /// <summary>A valid slot answers for nothing else: an argument the document requires is still missing, where it is.</summary>
    [Fact]
    public void A_valid_slot_beside_a_missing_required_argument_leaves_the_argument_missing()
    {
        var contract = Contract("list_membership.add");
        var arguments = new JsonObject
        {
            ["list"] = new JsonObject { ["external_id"] = "L-1129" },
            ["provider"] = Nested(),
        };

        var problem = Assert.Single(contract.CheckArguments(arguments, PatternTiming.Default));
        Assert.Equal(("/channel", "required"), (problem.Pointer, problem.Reason));
    }

    /// <summary>
    /// The slot is a key of the arguments themselves. A <c>provider</c> inside another argument is nothing reserved: it is a
    /// property that argument's schema does not declare, refused at its own pointer.
    /// </summary>
    [Fact]
    public void A_provider_inside_another_argument_is_refused_at_its_own_pointer()
    {
        var contract = Contract(Get);
        var arguments = new JsonObject
        {
            ["campaign"] = new JsonObject { ["external_id"] = "c_7714", ["provider"] = Nested() },
        };

        var problem = Assert.Single(contract.CheckArguments(arguments, PatternTiming.Default));
        Assert.Equal(("/campaign/provider", "additional_properties"), (problem.Pointer, problem.Reason));
    }

    /// <summary>Setting the slot aside sets nothing else aside: an argument the document does not declare is still refused.</summary>
    [Fact]
    public void The_rest_of_the_arguments_is_checked_as_the_document_says()
    {
        var contract = Contract(Get);
        var arguments = Arguments(Nested());
        arguments["include_steps"] = true;

        var problem = Assert.Single(contract.CheckArguments(arguments, PatternTiming.Default));
        Assert.Equal(("/include_steps", "additional_properties"), (problem.Pointer, problem.Reason));

        var composed = Assert.Single(contract.CheckInput(Composed(arguments), PatternTiming.Default));
        Assert.Equal(("/args/include_steps", "additional_properties"), (composed.Pointer, composed.Reason));
    }

    /// <summary>
    /// The slot is <c>provider</c> as written. A request body is read with names that match in any case, so arguments
    /// can arrive as such a node; <c>Provider</c> there is an argument the document does not declare, refused once, at
    /// its own pointer, and never taken for the slot.
    /// </summary>
    [Fact]
    public void A_name_that_differs_from_the_slot_only_in_case_is_not_the_slot()
    {
        var contract = Contract(Get);
        var arguments = JsonNode.Parse(
            """{"campaign":{"external_id":"c_7714"},"Provider":"send on Tuesdays"}""",
            new JsonNodeOptions { PropertyNameCaseInsensitive = true });

        var problem = Assert.Single(contract.CheckArguments(arguments, PatternTiming.Default));
        Assert.Equal(("/Provider", "additional_properties"), (problem.Pointer, problem.Reason));

        var composed = Assert.Single(contract.CheckInput(Composed(arguments!.AsObject()), PatternTiming.Default));
        Assert.Equal(("/args/Provider", "additional_properties"), (composed.Pointer, composed.Reason));
    }

    /// <summary>
    /// The slot's own member names are distinct, as every object a request carries must be: read with names that match
    /// in any case, a slot whose members differ only in case writes one property twice, and is refused at the slot.
    /// </summary>
    [Fact]
    public void A_slot_whose_own_members_differ_only_in_case_is_refused_at_the_slot()
    {
        var contract = Contract(Get);
        var arguments = JsonNode.Parse(
            """{"campaign":{"external_id":"c_7714"},"provider":{"Days":["mon"],"days":["tue"]}}""",
            new JsonNodeOptions { PropertyNameCaseInsensitive = true });

        var problem = Assert.Single(contract.CheckArguments(arguments, PatternTiming.Default));
        Assert.Equal(("/provider", "duplicate_property"), (problem.Pointer, problem.Reason));
        Assert.DoesNotContain("mon", problem.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A property written twice beside a valid slot is refused once, where it stands: setting the slot aside copies the
    /// object holding it unread, and the slot draws no problem of its own.
    /// </summary>
    [Fact]
    public void A_property_written_twice_beside_a_valid_slot_is_refused_once_where_it_stands()
    {
        var contract = Contract(Get);
        static JsonNode? Twice() => JsonNode.Parse(
            """{"campaign":{"external_id":"c_7714","external_id":"c_7715"},"provider":{}}""",
            documentOptions: new JsonDocumentOptions { AllowDuplicateProperties = true });

        var problem = Assert.Single(contract.CheckArguments(Twice(), PatternTiming.Default));
        Assert.Equal(("/campaign", "duplicate_property"), (problem.Pointer, problem.Reason));

        var composed = Assert.Single(contract.CheckInput(Composed(Twice()!.AsObject()), PatternTiming.Default));
        Assert.Equal(("/args/campaign", "duplicate_property"), (composed.Pointer, composed.Reason));

        // A reader before the check that already met the duplicate leaves the object as unread as it found it.
        var read = Twice();
        Assert.Throws<ArgumentException>(() => read!["campaign"]!.AsObject().Count);
        var again = Assert.Single(contract.CheckArguments(read, PatternTiming.Default));
        Assert.Equal(("/campaign", "duplicate_property"), (again.Pointer, again.Reason));
    }

    /// <summary>What is checked is a copy: the arguments a plugin is handed are the ones written, in the order written.</summary>
    [Fact]
    public void The_arguments_are_left_exactly_as_written()
    {
        var contract = Contract(Get);
        var arguments = Arguments(Nested());
        var input = Composed(Arguments(Nested()));
        var before = (arguments.ToJsonString(), input.ToJsonString());

        contract.CheckArguments(arguments, PatternTiming.Default);
        contract.CheckInput(input, PatternTiming.Default);

        Assert.Equal(before, (arguments.ToJsonString(), input.ToJsonString()));
        Assert.Equal(["campaign", "provider"], arguments.Select(pair => pair.Key));
    }

    /// <summary>Arguments that are not an object have no slot to set aside, and are refused as they always were.</summary>
    [Fact]
    public void Arguments_that_are_not_an_object_are_refused_as_before()
    {
        var contract = Contract(Get);

        Assert.Equal(
            SchemaValidator.Validate(JsonValue.Create("provider"), contract.ArgumentSchema),
            contract.CheckArguments(JsonValue.Create("provider"), PatternTiming.Default));
        var composed = new JsonObject { ["args"] = new JsonArray(), ["campaign"] = Composed([])["campaign"]!.DeepClone() };
        Assert.Equal(
            SchemaValidator.Validate(composed, contract.InputSchema),
            contract.CheckInput(composed, PatternTiming.Default));
    }

    /// <summary>
    /// Every operation admits the slot, and none declares an argument of that name: the key is reserved, so a document
    /// that gave it a meaning of its own would be read two ways.
    /// </summary>
    [Fact]
    public void Every_published_operation_admits_the_slot_and_none_declares_it()
    {
        Assert.All(OperationCatalog.All, contract =>
        {
            Assert.False(
                (contract.ArgumentSchema["properties"] as JsonObject)?.ContainsKey(OperationContract.ProviderSlot) == true,
                $"{contract.Id} declares an argument named `{OperationContract.ProviderSlot}`, which is reserved.");
            Assert.DoesNotContain(
                contract.CheckArguments(new JsonObject { [OperationContract.ProviderSlot] = Nested() }, PatternTiming.Default),
                problem => problem.Pointer.StartsWith("/" + OperationContract.ProviderSlot, StringComparison.Ordinal));
        });
    }
}
