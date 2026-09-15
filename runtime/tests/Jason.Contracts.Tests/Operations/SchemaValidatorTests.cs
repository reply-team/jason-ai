using System.Text.Json.Nodes;
using Jason.Contracts.Operations;

namespace Jason.Contracts.Tests.OperationContracts;

/// <summary>
/// The validator's own conformance corpus: one fact per keyword of the published dialect, each showing a value the
/// keyword accepts and a value it refuses with the exact pointer and reason code the refusal carries. The reason
/// codes travel to a plugin author as the field of an API error, so they are asserted literally rather than through
/// a helper that could drift with the implementation.
/// </summary>
public class SchemaValidatorTests
{
    private static JsonObject Schema(string json) => (JsonObject)JsonNode.Parse(json)!;

    private static void Accepts(string schema, string value) =>
        Assert.Empty(SchemaValidator.Validate(JsonNode.Parse(value), Schema(schema)));

    private static SchemaProblem Refuses(string schema, string value) =>
        Assert.Single(SchemaValidator.Validate(JsonNode.Parse(value), Schema(schema)));

    [Fact]
    public void Type_names_the_property_that_carries_the_wrong_kind_of_value()
    {
        const string schema = """{"type":"object","properties":{"n":{"type":"integer"}}}""";
        Accepts(schema, """{"n":7}""");

        var problem = Refuses(schema, """{"n":"7"}""");

        Assert.Equal("/n", problem.Pointer);
        Assert.Equal("type", problem.Reason);
    }

    [Fact]
    public void Type_may_be_a_list_and_the_whole_document_is_the_empty_pointer()
    {
        const string schema = """{"type":["string","null"]}""";
        Accepts(schema, "\"a moment\"");
        Accepts(schema, "null");

        var problem = Refuses(schema, "7");

        Assert.Equal("", problem.Pointer);
        Assert.Equal("type", problem.Reason);
    }

    [Fact]
    public void An_integer_is_a_number_without_a_fraction_rather_than_a_separate_json_type()
    {
        const string schema = """{"type":"integer"}""";
        Accepts(schema, "7");
        Accepts(schema, "7.0");

        Assert.Equal("type", Refuses(schema, "7.5").Reason);
    }

    [Fact]
    public void Required_points_at_the_property_that_is_missing()
    {
        const string schema = """
            {"type":"object","properties":{"args":{"type":"object","properties":{
              "list":{"type":"object","properties":{"external_id":{"type":"string"}},"required":["external_id"]}}}}}
            """;
        Accepts(schema, """{"args":{"list":{"external_id":"L-1"}}}""");

        var problem = Refuses(schema, """{"args":{"list":{}}}""");

        Assert.Equal("/args/list/external_id", problem.Pointer);
        Assert.Equal("required", problem.Reason);
    }

    [Fact]
    public void An_explicit_null_satisfies_required_because_it_was_written()
    {
        const string schema = """{"type":"object","properties":{"note":{"type":["string","null"]}},"required":["note"]}""";

        Accepts(schema, """{"note":null}""");
        Assert.Equal("required", Refuses(schema, "{}").Reason);
    }

    [Fact]
    public void Additional_properties_false_names_the_property_nobody_declared()
    {
        const string schema = """{"type":"object","properties":{"a":{"type":"string"}},"additionalProperties":false}""";
        Accepts(schema, """{"a":"x"}""");

        var problem = Refuses(schema, """{"a":"x","note":"planner chatter"}""");

        Assert.Equal("/note", problem.Pointer);
        Assert.Equal("additional_properties", problem.Reason);
    }

    [Fact]
    public void Enum_refuses_a_value_outside_the_published_vocabulary()
    {
        const string schema = """{"type":"object","properties":{"collision":{"enum":["skip","refuse"]}}}""";
        Accepts(schema, """{"collision":"skip"}""");

        var problem = Refuses(schema, """{"collision":"overwrite"}""");

        Assert.Equal("/collision", problem.Pointer);
        Assert.Equal("enum", problem.Reason);
    }

    [Fact]
    public void Const_pins_one_value()
    {
        const string schema = """{"type":"object","properties":{"channels":{"const":"consumed"}}}""";
        Accepts(schema, """{"channels":"consumed"}""");

        var problem = Refuses(schema, """{"channels":"all"}""");

        Assert.Equal("/channels", problem.Pointer);
        Assert.Equal("const", problem.Reason);
    }

    [Fact]
    public void Min_length_and_max_length_measure_a_string()
    {
        const string schema = """{"type":"object","properties":{"v":{"type":"string","minLength":2,"maxLength":4}}}""";
        Accepts(schema, """{"v":"abc"}""");

        Assert.Equal("min_length", Refuses(schema, """{"v":"a"}""").Reason);
        var tooLong = Refuses(schema, """{"v":"abcde"}""");
        Assert.Equal("/v", tooLong.Pointer);
        Assert.Equal("max_length", tooLong.Reason);
    }

    [Fact]
    public void Pattern_anchors_a_string_to_a_shape()
    {
        const string schema = """{"type":"object","properties":{"id":{"type":"string","pattern":"^wi_[0-9A-Z]{4}$"}}}""";
        Accepts(schema, """{"id":"wi_01AB"}""");

        var problem = Refuses(schema, """{"id":"wi_lower"}""");

        Assert.Equal("/id", problem.Pointer);
        Assert.Equal("pattern", problem.Reason);
    }

    [Fact]
    public void Minimum_and_maximum_bound_a_number_inclusively()
    {
        const string schema = """{"type":"object","properties":{"step":{"type":"integer","minimum":1,"maximum":10}}}""";
        Accepts(schema, """{"step":1}""");
        Accepts(schema, """{"step":10}""");

        Assert.Equal("minimum", Refuses(schema, """{"step":0}""").Reason);
        Assert.Equal("maximum", Refuses(schema, """{"step":11}""").Reason);
    }

    [Fact]
    public void Exclusive_minimum_and_exclusive_maximum_leave_the_bound_out()
    {
        const string schema = """{"type":"number","exclusiveMinimum":0,"exclusiveMaximum":1}""";
        Accepts(schema, "0.5");

        Assert.Equal("exclusive_minimum", Refuses(schema, "0").Reason);
        Assert.Equal("exclusive_maximum", Refuses(schema, "1").Reason);
    }

    [Fact]
    public void Multiple_of_divides_a_number_exactly()
    {
        const string schema = """{"type":"object","properties":{"cents":{"type":"number","multipleOf":0.05}}}""";
        Accepts(schema, """{"cents":0.15}""");

        var problem = Refuses(schema, """{"cents":0.17}""");

        Assert.Equal("/cents", problem.Pointer);
        Assert.Equal("multiple_of", problem.Reason);
    }

    [Fact]
    public void An_integer_too_large_for_a_decimal_is_still_an_integer()
    {
        // Whether a number has a fractional part is not a comparison, so it is not confined to the range the
        // comparisons run in. `1e40` is a whole number, and answering "this is number" where the schema asked for
        // an integer told a caller something untrue about their own value.
        const string schema = """{"type":"integer"}""";
        Accepts(schema, "7");
        Accepts(schema, "1e40");
        Accepts(schema, "-1e40");

        Assert.Equal("type", Refuses(schema, "7.5").Reason);
        Assert.Equal("type", Refuses(schema, "\"7\"").Reason);
    }

    // Reading `1e-40` as a decimal answers zero, and a zero answers questions the number never would: it is a
    // whole number, and it is not greater than zero. Both are false about the value in front of the validator, and
    // a confident wrong answer is worse than a missing one — nothing anywhere said a comparison had not run.
    [Fact]
    public void A_number_too_small_for_a_decimal_is_not_a_whole_number()
    {
        Assert.Equal("type", Refuses("""{"type":"integer"}""", "1e-40").Reason);
    }

    [Fact]
    public void A_number_too_small_for_a_decimal_is_not_refused_for_failing_to_exceed_zero()
    {
        Assert.Equal("number_not_comparable", Refuses("""{"exclusiveMinimum":0}""", "1e-40").Reason);

        // Every rule that could not run says so, rather than one of them answering for all of them.
        var problems = SchemaValidator.Validate(JsonNode.Parse("-1e-40"), Schema("""{"type":"number","minimum":0,"maximum":1}"""));

        Assert.Equal(2, problems.Count);
        Assert.All(problems, problem => Assert.Equal("number_not_comparable", problem.Reason));
    }

    [Fact]
    public void A_bound_too_small_for_a_decimal_is_refused_rather_than_read_as_zero()
    {
        const string schema = """{"type":"number","multipleOf":1e-40}""";

        var declared = Assert.Single(SchemaValidator.CheckDialect(Schema(schema)));

        // Read as zero it is not greater than zero, so the old answer was "multipleOf must be a number greater
        // than zero" about a number that is exactly that.
        Assert.Equal("number_not_comparable", declared.Reason);
        Assert.Equal("/multipleOf", declared.Pointer);
    }

    [Fact]
    public void A_number_that_narrows_exactly_still_takes_part_in_every_comparison()
    {
        // The guard must cost the ordinary numbers a contract is written in nothing at all.
        Accepts("""{"type":"number","minimum":0,"maximum":1}""", "0.1");
        Accepts("""{"type":"number","multipleOf":0.05}""", "0.15");
        Accepts("""{"type":"number","maximum":1e20}""", "1e20");
        Accepts("""{"type":"integer","minimum":-9007199254740993}""", "9007199254740993");
    }

    [Fact]
    public void A_value_outside_the_range_this_dialect_compares_in_is_refused_rather_than_waved_through()
    {
        // The comparison runs in decimal, and neither of these fits in one. The rule cannot run, so what it would
        // have measured is refused: returning quietly here had `{"maximum":100}` accepting 1e40.
        const string schema = """{"type":"number","maximum":100}""";
        Accepts(schema, "100");

        Assert.Equal("number_not_comparable", Refuses(schema, "1e40").Reason);
        Assert.Equal("number_not_comparable", Refuses(schema, "-1e40").Reason);
    }

    [Fact]
    public void A_bound_outside_that_range_is_refused_by_both_entry_points()
    {
        const string schema = """{"type":"number","maximum":1e40}""";

        var declared = Assert.Single(SchemaValidator.CheckDialect(Schema(schema)));

        Assert.Equal("number_not_comparable", declared.Reason);
        Assert.Equal("/maximum", declared.Pointer);
        Assert.Equal("number_not_comparable", Refuses(schema, "1").Reason);
    }

    [Fact]
    public void A_keyword_that_takes_no_number_at_all_still_says_so()
    {
        // The two refusals are different things and must stay so: `"three"` is the wrong kind of value, while a
        // number too large to compare is the right kind and still leaves the rule unable to run.
        Assert.Equal("type", Refuses("""{"maximum":"three"}""", "1").Reason);
        Assert.Equal("type", Assert.Single(SchemaValidator.CheckDialect(Schema("""{"maximum":"three"}"""))).Reason);
    }

    [Fact]
    public void Items_reports_the_index_of_the_element_that_failed()
    {
        const string schema = """
            {"type":"object","properties":{"contacts":{"type":"array","items":{"type":"object",
              "properties":{"channel":{"type":"string"}},"required":["channel"]}}}}
            """;
        Accepts(schema, """{"contacts":[{"channel":"email"}]}""");

        var problem = Refuses(schema, """{"contacts":[{"channel":"email"},{"channel":7}]}""");

        Assert.Equal("/contacts/1/channel", problem.Pointer);
        Assert.Equal("type", problem.Reason);
    }

    [Fact]
    public void Min_items_and_max_items_fix_the_cardinality_of_a_collection()
    {
        const string schema = """{"type":"object","properties":{"contacts":{"type":"array","minItems":1,"maxItems":1}}}""";
        Accepts(schema, """{"contacts":[{}]}""");

        Assert.Equal("min_items", Refuses(schema, """{"contacts":[]}""").Reason);
        var tooMany = Refuses(schema, """{"contacts":[{},{}]}""");
        Assert.Equal("/contacts", tooMany.Pointer);
        Assert.Equal("max_items", tooMany.Reason);
    }

    [Fact]
    public void Unique_items_compares_whole_values_whatever_the_order_of_their_keys()
    {
        const string schema = """{"type":"array","uniqueItems":true}""";
        Accepts(schema, """[{"a":1,"b":2},{"a":1,"b":3}]""");

        var problem = Refuses(schema, """[{"a":1,"b":2},{"b":2,"a":1}]""");

        Assert.Equal("", problem.Pointer);
        Assert.Equal("unique_items", problem.Reason);
    }

    [Fact]
    public void Any_of_reports_the_outer_pointer_once_rather_than_every_branch_it_tried()
    {
        const string schema = """
            {"type":"object","properties":{"args":{"type":"object","properties":{"campaign":{"type":"object"}}}},
             "anyOf":[{"type":"object","properties":{"args":{"type":"object","required":["campaign"]}},"required":["args"]},
                      {"type":"object","required":["campaign"]}]}
            """;
        Accepts(schema, """{"args":{"campaign":{}}}""");
        Accepts(schema, """{"args":{},"campaign":{}}""");

        var problem = Refuses(schema, """{"args":{}}""");

        Assert.Equal("", problem.Pointer);
        Assert.Equal("any_of", problem.Reason);
    }

    [Fact]
    public void All_of_reports_the_outer_pointer_once_and_names_the_branch_that_failed()
    {
        const string schema = """{"allOf":[{"type":"string"},{"minLength":3}]}""";
        Accepts(schema, "\"abc\"");

        var problem = Refuses(schema, "\"ab\"");

        Assert.Equal("", problem.Pointer);
        Assert.Equal("all_of", problem.Reason);
        Assert.Contains("min_length", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Not_refuses_the_value_its_sub_schema_accepts()
    {
        const string schema = """{"type":"object","properties":{"v":{"not":{"type":"null"}}}}""";
        Accepts(schema, """{"v":"something"}""");

        var problem = Refuses(schema, """{"v":null}""");

        Assert.Equal("/v", problem.Pointer);
        Assert.Equal("not", problem.Reason);
    }

    [Fact]
    public void Format_date_time_accepts_an_iso_timestamp_and_refuses_prose()
    {
        const string schema = """{"type":"object","properties":{"at":{"type":"string","format":"date-time"}}}""";
        Accepts(schema, """{"at":"2026-09-15T12:00:00Z"}""");
        Accepts(schema, """{"at":"2026-09-15T12:00:00.250+02:00"}""");

        var problem = Refuses(schema, """{"at":"15 September"}""");

        Assert.Equal("/at", problem.Pointer);
        Assert.Equal("format", problem.Reason);
    }

    [Fact]
    public void Format_date_time_refuses_a_timestamp_without_an_offset_and_an_impossible_day()
    {
        const string schema = """{"type":"string","format":"date-time"}""";

        Assert.Equal("format", Refuses(schema, "\"2026-09-15T12:00:00\"").Reason);
        Assert.Equal("format", Refuses(schema, "\"2026-02-30T12:00:00Z\"").Reason);
    }

    [Fact]
    public void A_local_ref_is_resolved_against_the_documents_own_defs()
    {
        const string schema = """
            {"type":"object","properties":{"list":{"$ref":"#/$defs/reference"}},
             "$defs":{"reference":{"type":"object","properties":{"external_id":{"type":"string"}},"required":["external_id"]}}}
            """;
        Accepts(schema, """{"list":{"external_id":"L-1"}}""");

        var problem = Refuses(schema, """{"list":{}}""");

        Assert.Equal("/list/external_id", problem.Pointer);
        Assert.Equal("required", problem.Reason);
    }

    [Fact]
    public void A_ref_that_names_nothing_is_reported_rather_than_ignored()
    {
        const string schema = """{"$ref":"#/$defs/missing"}""";

        Assert.Equal("schema_ref_unresolved", Refuses(schema, "{}").Reason);
    }

    [Fact]
    public void Annotations_are_carried_and_never_enforced()
    {
        const string schema = """
            {"$schema":"https://json-schema.org/draft/2020-12/schema","$id":"urn:jason:test:1",
             "title":"A campaign reference","description":"What the planner names.","examples":[{"external_id":"L-1"}],
             "type":"object"}
            """;

        Accepts(schema, """{"anything":true}""");
        Assert.Empty(SchemaValidator.CheckDialect(Schema(schema)));
    }

    [Fact]
    public void Every_failure_of_a_document_is_reported_at_once_rather_than_the_first()
    {
        const string schema = """
            {"type":"object","additionalProperties":false,
             "properties":{"a":{"type":"string"},"b":{"type":"integer"}},"required":["a","b"]}
            """;

        var problems = SchemaValidator.Validate(JsonNode.Parse("""{"b":"no","c":1}"""), Schema(schema));

        Assert.Equal(3, problems.Count);
        Assert.Contains(problems, p => p is { Pointer: "/a", Reason: "required" });
        Assert.Contains(problems, p => p is { Pointer: "/b", Reason: "type" });
        Assert.Contains(problems, p => p is { Pointer: "/c", Reason: "additional_properties" });
    }

    [Fact]
    public void A_schema_whose_numbers_were_built_rather_than_parsed_means_the_same_thing()
    {
        // A manifest arrives as YAML, so the reader hands the validator numbers that are plain CLR values rather
        // than parsed elements. The two spellings of "1" have to mean the same rule, or a plugin's binding schema
        // would be refused for saying what a published document is allowed to say.
        var built = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["size"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1L, ["maximum"] = 10L },
                ["name"] = new JsonObject { ["type"] = "string", ["minLength"] = 1L },
                ["ratio"] = new JsonObject { ["type"] = "number", ["multipleOf"] = 0.5d },
            },
        };

        Assert.Empty(SchemaValidator.CheckDialect(built));
        Assert.Empty(SchemaValidator.Validate(JsonNode.Parse("""{"size":5,"name":"x","ratio":1.5}"""), built));

        var problems = SchemaValidator.Validate(JsonNode.Parse("""{"size":0,"name":"","ratio":1.2}"""), built);

        Assert.Equal(["min_length", "minimum", "multiple_of"], problems.Select(problem => problem.Reason).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_pointer_escapes_the_two_characters_json_pointer_reserves()
    {
        const string schema = """{"type":"object","properties":{"a/b":{"type":"string"},"c~d":{"type":"string"}}}""";

        var problems = SchemaValidator.Validate(JsonNode.Parse("""{"a/b":1,"c~d":2}"""), Schema(schema));

        Assert.Contains(problems, p => p.Pointer == "/a~1b");
        Assert.Contains(problems, p => p.Pointer == "/c~0d");
    }
}
