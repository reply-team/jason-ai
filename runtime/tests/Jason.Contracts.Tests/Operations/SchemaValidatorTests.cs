using System.Text.Json.Nodes;
using Jason.Contracts.Operations;
using Jason.Runtime.Tests;

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
    [Critical(Family.R3, "required")]
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
    [Critical(Family.R3, "closed objects")]
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

    /// <summary>
    /// A value is checked against a pattern written as the schema's author wrote it, so the pattern goes in through
    /// the object model, which does the quoting, rather than through a JSON string that would need it twice.
    /// </summary>
    private static IReadOnlyList<SchemaProblem> CheckPattern(string pattern, string value) =>
        SchemaValidator.Validate(JsonValue.Create(value), new JsonObject { ["type"] = "string", ["pattern"] = pattern });

    /// <summary>
    /// `$` is the end of the value and nothing else, as ECMA-262 has it. The engine that runs the patterns reads it
    /// as "the end, or just before a final newline", so an identifier that is meant to be a single token passed with
    /// a newline after it and went on to whatever read the value next.
    /// </summary>
    [Fact]
    [Critical(Family.R3, "the classic $ trap")]
    public void A_dollar_anchors_the_end_of_the_value_and_a_final_newline_does_not_satisfy_it()
    {
        Assert.Empty(CheckPattern("^[a-z]+$", "abc"));

        var problem = Assert.Single(CheckPattern("^[a-z]+$", "abc\n"));

        Assert.Equal("", problem.Pointer);
        Assert.Equal("pattern", problem.Reason);
    }

    /// <summary>Inside a class, and escaped: neither is an anchor, and both still anchor the real one at the end.</summary>
    [Theory]
    [InlineData("^a[$]b$")]
    [InlineData(@"^a\$b$")]
    public void A_dollar_that_is_a_literal_character_is_left_alone(string pattern)
    {
        Assert.Empty(CheckPattern(pattern, "a$b"));
        Assert.Equal("pattern", Assert.Single(CheckPattern(pattern, "a$b\n")).Reason);
        Assert.Equal("pattern", Assert.Single(CheckPattern(pattern, "ab")).Reason);
    }

    [Fact]
    public void An_escaped_backslash_does_not_escape_the_dollar_after_it()
    {
        // `^a\\$` is a, a backslash, then the end of the value.
        Assert.Empty(CheckPattern(@"^a\\$", "a\\"));
        Assert.Equal("pattern", Assert.Single(CheckPattern(@"^a\\$", "a\\\n")).Reason);
    }

    [Fact]
    public void Every_alternative_that_ends_in_a_dollar_ends_the_value()
    {
        Assert.Empty(CheckPattern("^a$|^b$", "a"));
        Assert.Empty(CheckPattern("^a$|^b$", "b"));
        Assert.Equal("pattern", Assert.Single(CheckPattern("^a$|^b$", "a\n")).Reason);
        Assert.Equal("pattern", Assert.Single(CheckPattern("^a$|^b$", "b\n")).Reason);
    }

    [Fact]
    public void A_closing_bracket_that_is_escaped_does_not_end_the_class_a_dollar_could_hide_in()
    {
        Assert.Empty(CheckPattern(@"^[\]]$", "]"));
        Assert.Equal("pattern", Assert.Single(CheckPattern(@"^[\]]$", "]\n")).Reason);

        // Past the escaped bracket the class is still open, so this `$` is a character and the last one is the anchor.
        Assert.Empty(CheckPattern(@"^[\]$]$", "$"));
        Assert.Equal("pattern", Assert.Single(CheckPattern(@"^[\]$]$", "$\n")).Reason);
    }

    /// <summary>
    /// A class that begins with `]` has no single meaning: ECMA-262 reads `[]` as a class of nothing and `[^]` as any
    /// character, the engine reads the bracket as a member. So it is refused, and the bracket is written escaped — which
    /// both read as a member, with the class still open after it.
    /// </summary>
    [Fact]
    public void A_closing_bracket_first_in_a_class_is_refused_and_written_escaped_is_a_member()
    {
        Assert.Equal("schema_pattern_invalid", Assert.Single(SchemaValidator.CheckDialect(Patterned("^[]a$]$"))).Reason);
        Assert.Equal("schema_pattern_invalid", Assert.Single(SchemaValidator.CheckDialect(Patterned("^[^]a$]$"))).Reason);

        Assert.Empty(CheckPattern(@"^[\]a$]$", "]"));
        Assert.Empty(CheckPattern(@"^[\]a$]$", "$"));
        Assert.Equal("pattern", Assert.Single(CheckPattern(@"^[\]a$]$", "$\n")).Reason);

        Assert.Empty(CheckPattern(@"^[^\]a$]$", "b"));
        Assert.Equal("pattern", Assert.Single(CheckPattern(@"^[^\]a$]$", "]")).Reason);
        Assert.Equal("pattern", Assert.Single(CheckPattern(@"^[^\]a$]$", "b\n")).Reason);
    }

    [Fact]
    public void A_pattern_that_ends_in_a_lone_backslash_is_still_a_pattern_this_dialect_cannot_run()
    {
        var schema = new JsonObject { ["type"] = "string", ["pattern"] = @"^a\" };

        Assert.Equal("schema_pattern_invalid", Assert.Single(SchemaValidator.CheckDialect(schema)).Reason);
    }

    [Fact]
    public void A_refused_value_is_reported_against_the_pattern_as_its_author_wrote_it()
    {
        var problem = Assert.Single(CheckPattern("^[a-z]+$", "abc\n"));

        Assert.Contains("`^[a-z]+$`", problem.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(@"\z", problem.Message, StringComparison.Ordinal);
    }

    private static JsonObject Patterned(string pattern) => new() { ["type"] = "string", ["pattern"] = pattern };

    /// <summary>
    /// <c>\A</c>, <c>\Z</c>, <c>\z</c> and <c>\G</c> are anchors to the engine that runs the patterns and the letters A,
    /// Z, z and G to ECMA-262, which is the dialect JSON Schema names. A pattern written with one means one thing to
    /// its author and another to the runtime, and either could be the one the author had in mind, so the dialect
    /// refuses the escape rather than choose. The refusal names the escape and never the pattern around it.
    /// </summary>
    [Theory]
    // Critical: escapes with two meanings.
    [InlineData(@"^[a-z]+\Z", @"\Z", Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.R3)])]
    [InlineData(@"^[a-z]+\z", @"\z")]
    [InlineData(@"\A[a-z]+$", @"\A")]
    [InlineData(@"^[a-z]+\G", @"\G")]
    [InlineData(@"^(a|b\Z)$", @"\Z")]
    [InlineData(@"^[\Z]+$", @"\Z")]
    [InlineData(@"^[a\Gb]+$", @"\G")]
    [InlineData(@"^[^\A]+$", @"\A")]
    [InlineData(@"^[a-z]+\\\Z", @"\Z")]
    public void An_escape_that_is_an_anchor_to_the_engine_and_a_letter_to_ecma_262_is_refused_wherever_it_stands(string pattern, string escape)
    {
        var problem = Assert.Single(SchemaValidator.CheckDialect(Patterned(pattern)));

        Assert.Equal("/pattern", problem.Pointer);
        Assert.Equal("schema_pattern_invalid", problem.Reason);
        Assert.DoesNotContain(pattern, problem.Message, StringComparison.Ordinal);
        Assert.Contains($"`{escape}`", problem.Message, StringComparison.Ordinal);
        Assert.Contains("ECMA-262", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_is_never_measured_against_a_pattern_whose_escape_has_two_meanings()
    {
        // The engine would take the end of the value here, and accept this one; ECMA-262 would take the letter Z.
        var problem = Assert.Single(CheckPattern(@"^[a-z]+\Z", "abc\n"));

        Assert.Equal("schema_pattern_invalid", problem.Reason);
        Assert.Contains(@"`\Z`", problem.Message, StringComparison.Ordinal);
        Assert.Equal("schema_pattern_invalid", Assert.Single(CheckPattern(@"^[a-z]+\Z", "abc")).Reason);
    }

    [Fact]
    public void An_escaped_backslash_before_one_of_those_letters_is_a_backslash_and_a_letter()
    {
        foreach (var pattern in new[] { @"^a\\Z$", @"^a\\z$", @"^a\\A$", @"^a\\G$" })
        {
            Assert.Empty(SchemaValidator.CheckDialect(Patterned(pattern)));
        }

        // `^a\\Z$` is a, a backslash, a Z, then the end of the value.
        Assert.Empty(CheckPattern(@"^a\\Z$", "a\\Z"));
        Assert.Equal("pattern", Assert.Single(CheckPattern(@"^a\\Z$", "a")).Reason);
        Assert.Equal("pattern", Assert.Single(CheckPattern(@"^a\\Z$", "a\\Z\n")).Reason);
    }

    [Fact]
    public void The_check_reads_the_pattern_as_its_author_wrote_it_and_not_as_the_runtime_rewrites_it()
    {
        // A dollar that anchors is turned into `\z` for the engine; that must not be mistaken for the author's own.
        Assert.Empty(SchemaValidator.CheckDialect(Patterned("^[a-z]+$")));
        Assert.Empty(SchemaValidator.CheckDialect(Patterned("^a$|^b$")));
        Assert.Empty(SchemaValidator.CheckDialect(Patterned(@"^a[$]b$")));

        // And it is still the end of the value, and only that: the refusal changed no pattern that was accepted.
        Assert.Empty(CheckPattern("^[a-z]+$", "abc"));
        Assert.Equal("pattern", Assert.Single(CheckPattern("^[a-z]+$", "abc\n")).Reason);
    }

    /// <summary>
    /// In ECMA-262 a dot is any character but the four line terminators, <c>\n</c>, <c>\r</c>, U+2028 and U+2029. The
    /// engine that runs the patterns leaves out only <c>\n</c>, so <c>^.+$</c> would take a value that ends in a
    /// carriage return, and the text a schema's author wrote as one line would reach whatever reads it next as two.
    /// </summary>
    [Fact]
    [Critical(Family.R3, "ECMA-262 dot semantics")]
    public void A_dot_stops_at_each_of_the_four_line_terminators_ecma_262_names()
    {
        Assert.Empty(CheckPattern("^.+$", "abc"));
        Assert.Empty(CheckPattern("^.+$", "a b\tc.é"));

        foreach (var terminator in new[] { "\n", "\r", "\u2028", "\u2029" })
        {
            Assert.Equal("pattern", Assert.Single(CheckPattern("^.+$", "abc" + terminator)).Reason);
            Assert.Equal("pattern", Assert.Single(CheckPattern("^.+$", terminator + "abc")).Reason);
        }
    }

    /// <summary>
    /// The dot is the same class in every form it is written in: under each quantifier, counted, inside a group and as
    /// one side of an alternation. Each form takes an ordinary character in the dot's place and refuses each of the four
    /// line terminators there.
    /// </summary>
    [Theory]
    [InlineData("^.*$", "ab", "a{0}b")]
    [InlineData("^a.?$", "ab", "a{0}")]
    [InlineData("^.{2}$", "ab", "a{0}")]
    [InlineData("^(a.)$", "ab", "a{0}")]
    [InlineData("^(x|.)$", "y", "{0}")]
    public void A_dot_in_every_form_it_is_written_in_stops_at_each_line_terminator(string pattern, string accepted, string refused)
    {
        Assert.Empty(CheckPattern(pattern, accepted));

        foreach (var terminator in new[] { "\n", "\r", "\u2028", "\u2029" })
        {
            Assert.Equal("pattern", Assert.Single(CheckPattern(pattern, refused.Replace("{0}", terminator, StringComparison.Ordinal))).Reason);
        }
    }

    [Fact]
    public void A_dot_that_is_a_literal_character_is_left_alone()
    {
        // Inside a class, and escaped: neither is "any character".
        foreach (var pattern in new[] { "^[.]$", @"^\.$", "^[a.]$", @"^[\]\.]$" })
        {
            Assert.Empty(CheckPattern(pattern, "."));
            Assert.Equal("pattern", Assert.Single(CheckPattern(pattern, "x")).Reason);
        }

        Assert.Empty(CheckPattern("^[^.]+$", "abc"));
        Assert.Equal("pattern", Assert.Single(CheckPattern("^[^.]+$", "a.c")).Reason);

        // A class that opens with an escaped closing bracket keeps going, so the dot after it is still a member of it.
        Assert.Empty(CheckPattern(@"^[\]a.]+$", "]a."));
        Assert.Equal("pattern", Assert.Single(CheckPattern(@"^[\]a.]+$", "x")).Reason);
    }

    [Fact]
    public void An_escaped_backslash_does_not_escape_the_dot_after_it()
    {
        // `^a\\.$` is a, a backslash, then any character but a line terminator.
        Assert.Empty(CheckPattern(@"^a\\.$", @"a\x"));
        Assert.Equal("pattern", Assert.Single(CheckPattern(@"^a\\.$", "a\\\r")).Reason);
    }

    [Fact]
    public void A_refused_value_is_reported_against_the_dot_as_its_author_wrote_it()
    {
        var problem = Assert.Single(CheckPattern("^.+$", "abc\r"));

        Assert.Contains("`^.+$`", problem.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(@"\u2028", problem.Message, StringComparison.Ordinal);
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
        Assert.Equal("schema_keyword_malformed", Refuses("""{"maximum":"three"}""", "1").Reason);
        Assert.Equal("schema_keyword_malformed", Assert.Single(SchemaValidator.CheckDialect(Schema("""{"maximum":"three"}"""))).Reason);
    }

    /// <summary>
    /// Whose document is wrong. A keyword the schema wrote badly was reported as `type` at the caller's own
    /// pointer — the code that means "the value here is the wrong kind" — so the caller read "your value is
    /// wrong" about a value that was fine, and went looking in the wrong document for it.
    /// </summary>
    [Theory]
    [InlineData("""{"type":"object","properties":{"a":{"type":"string"}},"required":"a"}""", "required")]
    [InlineData("""{"type":"object","properties":"a"}""", "properties")]
    [InlineData("""{"type":"object","additionalProperties":{"type":"string"}}""", "additionalProperties")]
    [InlineData("""{"type":"nonsense"}""", "type")]
    [InlineData("""{"enum":[]}""", "enum")]
    [InlineData("""{"minLength":"three"}""", "minLength")]
    [InlineData("""{"anyOf":[]}""", "anyOf")]
    public void A_malformed_keyword_says_the_schema_is_wrong_rather_than_the_value(string schema, string keyword)
    {
        var applied = Refuses(schema, """{"a":"x"}""");

        Assert.Equal("schema_keyword_malformed", applied.Reason);
        Assert.Contains(keyword, applied.Message, StringComparison.Ordinal);

        Assert.Contains(
            SchemaValidator.CheckDialect(Schema(schema)),
            problem => problem.Reason == "schema_keyword_malformed");
    }

    /// <summary>The value's own failure keeps `type`, which is the code a caller acts on.</summary>
    [Fact]
    public void A_value_of_the_wrong_kind_is_still_the_value_s_own_failure()
    {
        Assert.Equal("type", Refuses("""{"type":"integer"}""", "\"seven\"").Reason);
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
    [Critical(Family.R3, "date-time format")]
    public void Format_date_time_refuses_a_timestamp_without_an_offset_and_an_impossible_day()
    {
        const string schema = """{"type":"string","format":"date-time"}""";

        Assert.Equal("format", Refuses(schema, "\"2026-09-15T12:00:00\"").Reason);
        Assert.Equal("format", Refuses(schema, "\"2026-02-30T12:00:00Z\"").Reason);
    }

    /// <summary>
    /// The year 0000 is four digits like any other, and no date this dialect admits: it is refused by name, as an
    /// impossible day is, where it used to throw out of the validator from every <c>date-time</c> field of every family.
    /// </summary>
    [Fact]
    public void Format_date_time_refuses_the_year_zero_by_name()
    {
        const string schema = """{"type":"string","format":"date-time"}""";

        Assert.Equal("format", Refuses(schema, "\"0000-01-01T00:00:00Z\"").Reason);
        Accepts(schema, "\"0001-01-01T00:00:00Z\"");
        Accepts(schema, "\"9999-12-31T23:59:59Z\"");
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
    [Critical(Family.R3, "all failures at once")]
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
