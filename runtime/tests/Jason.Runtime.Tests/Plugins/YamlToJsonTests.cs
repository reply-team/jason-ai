using System.Text.Json.Nodes;
using Jason.Runtime.Plugins.Manifest;

namespace Jason.Runtime.Tests.Plugins;

/// <summary>
/// The manifest is YAML because a person writes it, but everything downstream reads JSON. What matters here is
/// that the conversion has no opinions of its own: the YAML 1.2 core schema, nothing mapped onto a CLR type, and
/// a clear refusal for the documents a manifest is not allowed to be.
/// </summary>
public class YamlToJsonTests
{
    [Fact]
    public void A_mapping_becomes_an_object_and_a_sequence_an_array()
    {
        var node = YamlToJson.Convert("""
            id: fake
            operations:
              - echo.run
              - other.run
            nested:
              a: 1
            """);

        var root = Assert.IsType<JsonObject>(node);
        Assert.Equal("fake", (string?)root["id"]);
        Assert.Equal(["echo.run", "other.run"], Assert.IsType<JsonArray>(root["operations"]).Select(e => (string?)e));
        Assert.Equal(1L, (long?)Assert.IsType<JsonObject>(root["nested"])["a"]);
    }

    [Fact]
    public void A_flow_sequence_is_the_same_array_as_a_block_one()
    {
        var flow = YamlToJson.Convert("operations: [a.b, c.d]");
        var block = YamlToJson.Convert("operations:\n  - a.b\n  - c.d");

        Assert.Equal(flow!.ToJsonString(), block!.ToJsonString());
    }

    [Theory]
    [InlineData("v: 1", "1")]
    [InlineData("v: -17", "-17")]
    [InlineData("v: 1.5", "1.5")]
    [InlineData("v: 0644", "644")]
    [InlineData("v: true", "true")]
    [InlineData("v: false", "false")]
    public void A_plain_scalar_is_read_by_the_core_schema(string yaml, string expected)
    {
        var value = Assert.IsType<JsonObject>(YamlToJson.Convert(yaml))["v"];

        Assert.Equal(expected, value!.ToJsonString());
    }

    [Theory]
    [InlineData("v: ~")]
    [InlineData("v: null")]
    [InlineData("v: Null")]
    [InlineData("v: NULL")]
    [InlineData("v:")]
    public void The_empty_and_null_scalars_are_null(string yaml)
    {
        Assert.Null(Assert.IsType<JsonObject>(YamlToJson.Convert(yaml))["v"]);
    }

    [Theory]
    [InlineData("v: \"1\"", "1")]
    [InlineData("v: '1'", "1")]
    [InlineData("v: yes", "yes")]
    [InlineData("v: no", "no")]
    [InlineData("v: on", "on")]
    [InlineData("v: 1.0.0", "1.0.0")]
    public void A_quoted_scalar_and_anything_outside_the_core_schema_stay_strings(string yaml, string expected)
    {
        // YAML 1.2, not 1.1: `yes` is the word, not a boolean, and a version is not an octal number.
        Assert.Equal(expected, (string?)Assert.IsType<JsonObject>(YamlToJson.Convert(yaml))["v"]);
    }

    [Fact]
    public void Comments_and_blank_lines_are_not_content()
    {
        var node = YamlToJson.Convert("""
            # this variable holds the token
            id: fake   # and this is the id

            version: 1.0.0
            """);

        var root = Assert.IsType<JsonObject>(node);
        Assert.Equal(2, root.Count);
        Assert.Equal("fake", (string?)root["id"]);
    }

    [Fact]
    public void A_duplicate_key_is_refused_where_it_is_written()
    {
        var error = Assert.Throws<YamlInvalidException>(() => YamlToJson.Convert("id: one\nkind: provider\nid: two\n"));

        Assert.Equal(3, error.Line);
        Assert.True(error.Column > 0);
    }

    [Fact]
    public void A_second_document_is_refused()
    {
        Assert.Throws<YamlInvalidException>(() => YamlToJson.Convert("id: one\n---\nid: two\n"));
    }

    [Fact]
    public void A_document_nested_deeper_than_the_limit_is_refused()
    {
        var yaml = string.Concat(Enumerable.Range(0, 40).Select(depth => new string(' ', depth * 2) + "a:\n")) + new string(' ', 80) + "b: 1\n";

        Assert.Throws<YamlInvalidException>(() => YamlToJson.Convert(yaml));
    }

    [Fact]
    public void Broken_syntax_is_refused_with_the_place_it_broke()
    {
        var error = Assert.Throws<YamlInvalidException>(() => YamlToJson.Convert("id: [one\nkind: provider\n"));

        Assert.True(error.Line > 0);
        Assert.NotEmpty(error.Message);
    }

    [Fact]
    public void An_alias_is_refused_rather_than_expanded()
    {
        // A manifest is read once, by people and by the runtime; an anchor that expands elsewhere would make
        // "what does this file declare" a question only a parser can answer.
        Assert.Throws<YamlInvalidException>(() => YamlToJson.Convert("a: &x 1\nb: *x\n"));
    }

    [Fact]
    public void An_empty_document_is_null()
    {
        Assert.Null(YamlToJson.Convert(string.Empty));
        Assert.Null(YamlToJson.Convert("# nothing but a comment\n"));
    }
}
