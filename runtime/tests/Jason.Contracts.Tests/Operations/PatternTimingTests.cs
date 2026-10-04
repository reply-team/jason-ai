using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Contracts.Notifications;
using Jason.Contracts.Operations;
using Jason.Runtime.Tests;

namespace Jason.Contracts.Tests.OperationContracts;

/// <summary>
/// A pattern's match timeout is a property of the pattern and the machine, never a verdict on the value. A cold engine
/// builds its automaton and compiles its matcher inside the first match's timeout, so a valid value could be refused
/// for nothing but the machine being busy. These hold the three things that stop that: the match is tried once more,
/// warm; a second timeout is reported as what it is rather than as a refusal; and a schema can be warmed outside any
/// request, and probed, so the cost a timeout measures is paid where nobody is waiting.
/// </summary>
public class PatternTimingTests
{
    private const string Channel = """{"type":"object","properties":{"channel":{"type":"string","pattern":"^[a-z][a-z0-9_]{0,31}$"}}}""";

    private static JsonObject Schema(string json) => (JsonObject)JsonNode.Parse(json)!;

    private static IReadOnlyList<SchemaProblem> Validate(string schema, string value, PatternTiming timing) =>
        SchemaValidator.Validate(JsonNode.Parse(value), Schema(schema), timing);

    [Fact]
    public void A_match_that_times_out_once_is_tried_again_warm_and_a_valid_value_passes()
    {
        var timing = new TimingOut(1);

        Assert.Empty(Validate(Channel, """{"channel":"email"}""", timing));
        Assert.Equal(2, timing.Calls);
    }

    [Fact]
    public void A_match_that_times_out_once_is_tried_again_warm_and_an_invalid_value_is_still_refused()
    {
        var problem = Assert.Single(Validate(Channel, """{"channel":"E-mail"}""", new TimingOut(1)));

        Assert.Equal(("/channel", "pattern"), (problem.Pointer, problem.Reason));
    }

    /// <summary>
    /// Two timeouts are a pattern that did not finish, said at the value's pointer under a code of its own: not
    /// <c>pattern</c>, which would say the value failed, and not <c>schema_pattern_invalid</c>, which would say the
    /// schema is wrong. Nothing is known about the value, and the code says exactly that.
    /// </summary>
    [Fact]
    public void A_match_that_times_out_twice_is_reported_as_unmeasured_and_never_as_a_verdict()
    {
        var timing = TimingOut.Always();

        var problem = Assert.Single(Validate(Channel, """{"channel":"email"}""", timing));

        Assert.Equal(("/channel", SchemaValidator.PatternTimedOut), (problem.Pointer, problem.Reason));
        Assert.Equal("pattern_timed_out", problem.Reason);
        Assert.Equal(2, timing.Calls);
    }

    [Fact]
    public void A_problem_list_says_whether_anything_in_it_is_a_verdict()
    {
        const string schema = """{"type":"object","properties":{"channel":{"type":"string","pattern":"^a$"},"n":{"type":"integer"}}}""";

        var unmeasured = Validate(schema, """{"channel":"a","n":1}""", TimingOut.Always());
        var mixed = Validate(schema, """{"channel":"a","n":"one"}""", TimingOut.Always());

        Assert.True(SchemaValidator.Unmeasured(unmeasured));
        Assert.Empty(SchemaValidator.Verdicts(unmeasured));
        Assert.False(SchemaValidator.Unmeasured(mixed));
        Assert.Equal(("/n", "type"), (Assert.Single(SchemaValidator.Verdicts(mixed)).Pointer, SchemaValidator.Verdicts(mixed)[0].Reason));
        Assert.False(SchemaValidator.Unmeasured([]));
    }

    /// <summary>
    /// A combinator reads its branches' verdicts, and a branch that could not be measured has none. Reading the
    /// timeout as a failed branch would turn it into a verdict one level up: <c>not</c> would accept the value it
    /// exists to exclude, and <c>anyOf</c> would refuse one an unmeasured branch might accept.
    /// </summary>
    [Theory]
    [InlineData("""{"not":{"type":"string","pattern":"^a$"}}""", "\"a\"")]
    [InlineData("""{"anyOf":[{"type":"integer"},{"type":"string","pattern":"^a$"}]}""", "\"a\"")]
    [InlineData("""{"allOf":[{"type":"string"},{"type":"string","pattern":"^a$"}]}""", "\"a\"")]
    public void A_combinator_over_an_unmeasured_branch_is_unmeasured_too(string schema, string value)
    {
        var problem = Assert.Single(Validate(schema, value, TimingOut.Always()));

        Assert.Equal(("", "pattern_timed_out"), (problem.Pointer, problem.Reason));
    }

    [Fact]
    public void A_combinator_decided_by_a_measured_branch_keeps_its_verdict()
    {
        // One branch accepts, so the timeout beside it decides nothing.
        Assert.Empty(Validate("""{"anyOf":[{"type":"string"},{"type":"string","pattern":"^a$"}]}""", "\"a\"", TimingOut.Always()));

        // One requirement fails on its own, so the unmeasured one beside it cannot rescue the value.
        var allOf = Assert.Single(Validate("""{"allOf":[{"type":"integer"},{"type":"string","pattern":"^a$"}]}""", "\"a\"", TimingOut.Always()));
        Assert.Equal("all_of", allOf.Reason);

        // The excluded schema fails on its own, so the value is not the one excluded.
        Assert.Empty(Validate("""{"not":{"type":"integer","pattern":"^a$"}}""", "\"a\"", TimingOut.Always()));
    }

    [Fact]
    public void The_default_timing_matches_for_real_under_the_published_timeout()
    {
        var recorded = new Recording();

        Assert.Empty(Validate(Channel, """{"channel":"email"}""", recorded));
        Assert.Single(Validate(Channel, """{"channel":"E"}""", PatternTiming.Default));
        Assert.Equal(100, SchemaValidator.PatternTimeoutMilliseconds);

        // One match, or two where a cold engine's first ran out of time and the warm one answered: either way every
        // pattern was compiled with the published timeout.
        Assert.InRange(recorded.Timeouts.Count, 1, 2);
        Assert.All(recorded.Timeouts, timeout => Assert.Equal(TimeSpan.FromMilliseconds(SchemaValidator.PatternTimeoutMilliseconds), timeout));
    }

    [Fact]
    public void The_two_argument_form_is_the_default_timing()
    {
        Assert.Empty(SchemaValidator.Validate(JsonNode.Parse("""{"channel":"email"}"""), Schema(Channel)));
        Assert.Single(SchemaValidator.Validate(JsonNode.Parse("""{"channel":"E"}"""), Schema(Channel)));
    }

    /// <summary>
    /// The warm-up matches every pattern a schema holds, wherever the dialect lets one stand, and keeps matching past
    /// a timeout until the match finishes: it is paying the cold engine's cost, and a timeout there is that cost
    /// being paid rather than anything to report.
    /// </summary>
    [Fact]
    public void Warming_a_schema_matches_every_pattern_it_holds_past_any_timeout()
    {
        var schema = Schema("""
            {
              "type": "object",
              "properties": {
                "a": { "type": "string", "pattern": "^a$" },
                "b": { "type": "array", "items": { "type": "string", "pattern": "^b$" } },
                "c": { "anyOf": [ { "type": "string", "pattern": "^c$" }, { "type": "integer" } ] },
                "d": { "not": { "pattern": "^d$" } },
                "e": { "$ref": "#/$defs/e" }
              },
              "$defs": { "e": { "type": "string", "pattern": "^e$" } }
            }
            """);
        var timing = new TimingOut(2);

        SchemaValidator.Warm(schema, timing);

        Assert.Equal(["^a$", "^b$", "^c$", "^d$", "^e$"], timing.Matched.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Warming_a_schema_that_never_finishes_gives_up_quietly()
    {
        var timing = TimingOut.Always();

        SchemaValidator.Warm(Schema(Channel), timing);

        Assert.Empty(timing.Matched);
        Assert.InRange(timing.Calls, 2, 10);
    }

    [Fact]
    public void Warming_a_schema_with_no_pattern_matches_nothing()
    {
        var timing = TimingOut.Never();

        SchemaValidator.Warm(Schema("""{"type":"object","properties":{"n":{"type":"integer"}}}"""), timing);

        Assert.Equal(0, timing.Calls);
    }

    /// <summary>
    /// A warm-up says what it did: how many distinct patterns finished warming, and how many it gave up on and left to
    /// a request's own second match — counted once each, however many schemas repeat them.
    /// </summary>
    [Fact]
    public void Warming_says_how_many_patterns_finished_and_how_many_were_left_unfinished()
    {
        JsonObject[] schemas =
        [
            Schema("""{"type":"object","properties":{"a":{"type":"string","pattern":"^a$"},"b":{"type":"string","pattern":"^b$"}}}"""),
            Schema("""{"type":"object","properties":{"a":{"type":"string","pattern":"^a$"}}}"""),
        ];

        Assert.Equal(new WarmedPatterns(Finished: 2, Unfinished: 0), SchemaValidator.Warm(schemas, TimingOut.Never()));
        Assert.Equal(new WarmedPatterns(Finished: 0, Unfinished: 2), SchemaValidator.Warm(schemas, TimingOut.Always()));
        Assert.Equal(new WarmedPatterns(Finished: 1, Unfinished: 0), SchemaValidator.Warm(schemas[1], TimingOut.Never()));
    }

    /// <summary>
    /// An untrusted schema's patterns are held to the dialect's time at load, on a warm engine: one probe of each,
    /// tried once more past a timeout so a loaded machine does not refuse a good package, and a second timeout is a
    /// pattern that cannot run in the time this dialect allows — the schema's fault, said at the pattern.
    /// </summary>
    [Fact]
    public void Probing_refuses_a_pattern_that_times_out_twice_and_names_where_it_is()
    {
        var problem = Assert.Single(SchemaValidator.ProbePatterns(Schema(Channel), TimingOut.Always()));

        Assert.Equal(("/properties/channel/pattern", "schema_pattern_invalid"), (problem.Pointer, problem.Reason));
    }

    [Fact]
    public void Probing_tries_a_timed_out_pattern_once_more_and_accepts_it()
    {
        var timing = new TimingOut(1);

        Assert.Empty(SchemaValidator.ProbePatterns(Schema(Channel), timing));
        Assert.Equal(2, timing.Calls);
    }

    [Fact]
    public void Probing_an_ordinary_pattern_on_the_default_timing_finds_nothing()
    {
        Assert.Empty(SchemaValidator.ProbePatterns(Schema(Channel), PatternTiming.Default));
    }

    [Fact]
    public void An_operation_s_result_is_measured_under_the_timing_it_is_given()
    {
        var contract = OperationCatalog.Find("list_membership.add")!;
        var result = JsonNode.Parse("""{"items":[{"contact_id":"cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD","status":"added"}]}""");

        Assert.Empty(OutcomeContract.CheckResult(contract, result, new TimingOut(1)));
        Assert.Equal("pattern_timed_out", Assert.Single(OutcomeContract.CheckResult(contract, result, TimingOut.Always())).Reason);
    }

    [Fact]
    public void A_delivery_is_measured_under_the_timing_it_is_given()
    {
        var contract = DeliveryContract.Parse("test", JsonNode.Parse("""
            {
              "id": "notification.send",
              "version": 1,
              "intent": "a pointer in front of a person",
              "input_schema": { "type": "object", "properties": { "title": { "type": "string", "pattern": "^J" } } },
              "output_schema": { "type": "object", "properties": { "receipt": { "type": "string", "pattern": "^r" } } },
              "failure_codes": [],
              "timeout_ms": 1000,
              "conformance": []
            }
            """));

        Assert.Empty(contract.CheckInput(JsonNode.Parse("""{"title":"Jason"}"""), new TimingOut(1)));
        Assert.Equal("pattern_timed_out", Assert.Single(contract.CheckInput(JsonNode.Parse("""{"title":"Jason"}"""), TimingOut.Always())).Reason);
        Assert.Equal("pattern_timed_out", Assert.Single(contract.CheckResult(JsonNode.Parse("""{"receipt":"r1"}"""), TimingOut.Always())).Reason);
    }

    [Fact]
    public void Compiling_a_pattern_this_process_has_not_seen_is_counted()
    {
        var before = PatternTiming.PatternsCompiled;
        var unseen = "^" + Guid.NewGuid().ToString("N") + "$";

        SchemaValidator.Validate(JsonNode.Parse("\"x\""), new JsonObject { ["type"] = "string", ["pattern"] = unseen });

        Assert.True(PatternTiming.PatternsCompiled > before, "compiling a new pattern did not move the count");
    }

    /// <summary>The default match, recording the timeout every pattern it is handed was compiled with.</summary>
    private sealed class Recording : PatternTiming
    {
        public List<TimeSpan> Timeouts { get; } = [];

        public override bool IsMatch(Regex pattern, string value)
        {
            Timeouts.Add(pattern.MatchTimeout);
            return base.IsMatch(pattern, value);
        }
    }
}
