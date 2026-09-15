using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Operations;

namespace Jason.Contracts.Tests.OperationContracts;

/// <summary>
/// A binding schema arrives from a third party's manifest, so the validator is fed hostile schemas as a matter of
/// course. Every rule here exists because the alternative is a plugin package that stops the runtime: a pattern
/// that never finishes, a reference that loops, a document deep enough to exhaust the stack, or a keyword we would
/// silently ignore and thereby not enforce.
/// </summary>
public class SchemaHostileInputTests
{
    private static JsonObject Schema(string json) => (JsonObject)JsonNode.Parse(json)!;

    /// <summary>A schema deep enough to exercise the limit is deeper than <c>JsonNode</c> parses by default.</summary>
    private static JsonObject DeepSchema(string json) =>
        (JsonObject)JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { MaxDepth = 256 })!;

    [Fact]
    public void A_pattern_built_to_backtrack_finishes_and_reports_instead_of_hanging()
    {
        var schema = Schema("""{"type":"string","pattern":"^(a+)+$"}""");
        var value = JsonNode.Parse("\"" + new string('a', 4000) + "b\"");

        var watch = Stopwatch.StartNew();
        var problems = SchemaValidator.Validate(value, schema);
        watch.Stop();

        Assert.Equal("pattern", Assert.Single(problems).Reason);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"the match took {watch.Elapsed}.");
    }

    [Theory]
    [InlineData("^(?=a)a$")]
    [InlineData(@"^(a)\1$")]
    [InlineData("^(?<!x)a$")]
    [InlineData("^[a-")]
    public void A_pattern_that_cannot_run_in_linear_time_is_a_schema_problem_rather_than_an_exception(string pattern)
    {
        var schema = new JsonObject { ["type"] = "string", ["pattern"] = pattern };

        Assert.Equal("schema_pattern_invalid", Assert.Single(SchemaValidator.CheckDialect(schema)).Reason);
        Assert.Equal("schema_pattern_invalid", Assert.Single(SchemaValidator.Validate(JsonNode.Parse("\"a\""), schema)).Reason);
    }

    [Fact]
    public void A_reference_that_loops_is_refused_by_both_entry_points()
    {
        var schema = Schema("""{"$ref":"#/$defs/a","$defs":{"a":{"$ref":"#/$defs/b"},"b":{"$ref":"#/$defs/a"}}}""");

        Assert.Contains(SchemaValidator.CheckDialect(schema), p => p.Reason == "schema_cyclic");
        Assert.Contains(SchemaValidator.Validate(JsonNode.Parse("{}"), schema), p => p.Reason == "schema_cyclic");
    }

    [Fact]
    public void A_reference_that_leaves_the_document_is_refused()
    {
        var schema = Schema("""{"$ref":"https://example.invalid/schema.json"}""");

        Assert.Equal("schema_ref_unresolved", Assert.Single(SchemaValidator.CheckDialect(schema)).Reason);
    }

    [Fact]
    public void A_document_nested_past_the_depth_limit_stops_rather_than_exhausting_the_stack()
    {
        var problems = SchemaValidator.Validate(JsonNode.Parse(Nested("{\"x\":", "}", 40, "1")), DeepSchema(NestedSchema(40)));

        Assert.Contains(problems, p => p.Reason == "schema_too_deep");
    }

    [Fact]
    public void A_schema_nested_past_the_depth_limit_is_refused_by_the_dialect_check()
    {
        Assert.Contains(SchemaValidator.CheckDialect(DeepSchema(NestedSchema(40))), p => p.Reason == "schema_too_deep");
    }

    [Theory]
    [InlineData("if")]
    [InlineData("dependentSchemas")]
    [InlineData("patternProperties")]
    [InlineData("unevaluatedProperties")]
    [InlineData("prefixItems")]
    [InlineData("contains")]
    public void A_keyword_outside_the_dialect_is_named_rather_than_ignored(string keyword)
    {
        var schema = new JsonObject { ["type"] = "object", [keyword] = new JsonObject() };

        var problem = Assert.Single(SchemaValidator.CheckDialect(schema));

        Assert.Equal("schema_keyword_unknown", problem.Reason);
        Assert.Equal("/" + keyword, problem.Pointer);
        Assert.Contains(keyword, problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_keyword_deeper_in_the_document_carries_its_own_pointer()
    {
        var schema = Schema("""{"type":"object","properties":{"x":{"type":"string","patternProperties":{}}}}""");

        var problem = Assert.Single(SchemaValidator.CheckDialect(schema));

        Assert.Equal("/properties/x/patternProperties", problem.Pointer);
    }

    [Theory]
    [InlineData("email")]
    [InlineData("uri")]
    [InlineData("hostname")]
    public void Only_date_time_survives_of_the_format_vocabulary(string format)
    {
        var schema = new JsonObject { ["type"] = "string", ["format"] = format };

        Assert.Equal("format", Assert.Single(SchemaValidator.CheckDialect(schema)).Reason);
        Assert.Empty(SchemaValidator.CheckDialect(new JsonObject { ["type"] = "string", ["format"] = "date-time" }));
    }

    [Fact]
    public void A_schema_larger_than_the_manifest_allows_is_refused_unread()
    {
        var properties = new JsonObject();
        for (var i = 0; properties.ToJsonString().Length < 100 * 1024; i++)
        {
            properties["property_with_a_long_enough_name_to_grow_the_document_" + i] = new JsonObject { ["type"] = "string" };
        }

        var problem = Assert.Single(SchemaValidator.CheckDialect(new JsonObject { ["type"] = "object", ["properties"] = properties }));

        Assert.Equal("schema_too_large", problem.Reason);
        Assert.Equal("", problem.Pointer);
    }

    [Theory]
    [InlineData("""{"type":"object","properties":{"x":true}}""")]
    [InlineData("""{"type":"object","required":"x"}""")]
    [InlineData("""{"type":"nonsense"}""")]
    [InlineData("""{"type":"object","additionalProperties":{"type":"string"}}""")]
    [InlineData("""{"minLength":"three"}""")]
    [InlineData("""{"anyOf":[]}""")]
    [InlineData("""{"enum":[]}""")]
    public void A_schema_whose_keyword_has_the_wrong_shape_is_refused_rather_than_half_applied(string schema)
    {
        Assert.NotEmpty(SchemaValidator.CheckDialect(Schema(schema)));
    }

    [Theory]
    [InlineData("""{"type":"object","properties":{"x":true}}""", "{}")]
    [InlineData("""{"type":"object","required":"x"}""", """{"y":1}""")]
    [InlineData("""{"minimum":"three"}""", "1")]
    [InlineData("""{"$ref":"#/$defs/a","$defs":{"a":{"$ref":"#/$defs/a"}}}""", """{"x":[1,2]}""")]
    [InlineData("""{"uniqueItems":true}""", """[[[[[[1]]]]]]""")]
    [InlineData("""{"multipleOf":0}""", "5")]
    public void Neither_entry_point_ever_throws_whatever_the_schema_says(string schema, string value)
    {
        var parsed = Schema(schema);

        Assert.NotNull(SchemaValidator.CheckDialect(parsed));
        Assert.NotNull(SchemaValidator.Validate(JsonNode.Parse(value), parsed));
    }

    private static string Nested(string open, string close, int depth, string leaf)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < depth; i++)
        {
            builder.Append(open);
        }

        builder.Append(leaf);
        for (var i = 0; i < depth; i++)
        {
            builder.Append(close);
        }

        return builder.ToString();
    }

    private static string NestedSchema(int depth) =>
        Nested("""{"type":"object","properties":{"x":""", "}}", depth, """{"type":"integer"}""");
}
