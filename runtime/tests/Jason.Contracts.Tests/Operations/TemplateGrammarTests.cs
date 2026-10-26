using System.Text.Json.Nodes;
using Jason.Contracts.Operations;
using Jason.Runtime.Tests;

namespace Jason.Contracts.Tests.OperationContracts;

/// <summary>
/// The template standard, held through what a template's author meets: every construct the standard refuses is refused
/// with its own named reason, at the character it starts on, the first one met reading from the start; what the
/// standard writes is accepted; and a refusal never quotes the template.
/// </summary>
public class TemplateGrammarTests
{
    /// <summary>Every construct the standard refuses, each in a template that is otherwise ordinary, and where it starts.</summary>
    [Theory]
    [InlineData("Hi {{{contact.first_name}}}", "raw", 4)]
    [InlineData("{{! a note }}Hi", "comment", 1)]
    [InlineData("Hi{{> footer}}", "partial", 3)]
    [InlineData("{{#each contact.custom.items}}x{{/each}}", "block", 1)]
    [InlineData("{{#with contact}}x{{/with}}", "block", 1)]
    [InlineData("{{#unless contact.title}}x{{/unless}}", "block", 1)]
    [InlineData("x{{/each}}", "block", 2)]
    [InlineData("{{#if contact.title}}a{{else if contact.company}}b{{/if}}", "elseif", 23)]
    [InlineData("Hi {{toLower contact.first_name}}", "helper", 4)]
    [InlineData("{{Random | 'Hi' | 'Hello'}} there", "helper", 1)]
    [InlineData("{{days_from_now 5}}", "helper", 1)]
    [InlineData("Hi @prompt[[ write a line ]]", "prompt", 4)]
    [InlineData("{{contact.first_name | '@prompt[[ a line ]]'}}", "prompt", 25)]
    [InlineData("{{#if contact.title = 'CEO'}}x{{/if}}", "comparison", 1)]
    [InlineData("{{#if contact.title <> 'CEO'}}x{{/if}}", "comparison", 1)]
    [InlineData("{{#if contact.title < 'CEO'}}x{{/if}}", "comparison", 1)]
    [InlineData("{{#if contact.title > 'CEO'}}x{{/if}}", "comparison", 1)]
    [InlineData("{{#if contact.title <= 'CEO'}}x{{/if}}", "comparison", 1)]
    [InlineData("{{#if contact.title >= 'CEO'}}x{{/if}}", "comparison", 1)]
    [InlineData("{{#if contact.title && contact.company}}x{{/if}}", "logic", 1)]
    [InlineData("{{#if contact.title == 'CEO' || contact.company}}x{{/if}}", "logic", 1)]
    [InlineData("{{contact.first_name | contact.last_name}}", "variable", 1)]
    [InlineData("{{#if contact.title == contact.company}}x{{/if}}", "variable", 1)]
    [InlineData("{{#if contact.title != sender.name}}x{{/if}}", "variable", 1)]
    [InlineData("Hi {{contact.phone}}", "name", 4)]
    [InlineData("{{today}}", "name", 1)]
    [InlineData("{{FirstName}}", "name", 1)]
    [InlineData("{{contact.custom.9lives}}", "name", 1)]
    [InlineData("{{contact.custom.}}", "name", 1)]
    [InlineData("{{#if sender.phone}}x{{/if}}", "name", 1)]
    [InlineData("{{contact.first_name | 'it's'}}", "quoted", 24)]
    [InlineData("{{contact.first_name | 'never closed}}", "quoted", 24)]
    [InlineData("{{ contact.first_name }}", "spacing", 1)]
    [InlineData("{{contact.first_name|'there'}}", "spacing", 1)]
    [InlineData("{{#if  contact.title}}x{{/if}}", "spacing", 1)]
    [InlineData("{{#if contact.title}}x{{ /if }}", "spacing", 23)]
    [InlineData("{{contact.first_name\t}}", "spacing", 1)]
    [InlineData("{{}}", "unknown", 1)]
    [InlineData("{{#if}}x{{/if}}", "unknown", 1)]
    [InlineData("{{contact.first_name == 'x'}}", "unknown", 1)]
    [InlineData("{{#if contact.title | 'x'}}y{{/if}}", "unknown", 1)]
    [InlineData("{{'text'}}", "unknown", 1)]
    [InlineData("{{#if contact.title}}x", "unbalanced", 1)]
    [InlineData("x{{/if}}", "unbalanced", 2)]
    [InlineData("a {{else}} b", "unbalanced", 3)]
    [InlineData("{{#if contact.title}}a{{else}}b{{else}}c{{/if}}", "unbalanced", 32)]
    [InlineData("{{#if contact.title}}{{#if contact.title}}{{#if contact.title}}{{#if contact.title}}{{#if contact.title}}{{#if contact.title}}{{#if contact.title}}{{#if contact.title}}{{#if contact.title}}x{{/if}}{{/if}}{{/if}}{{/if}}{{/if}}{{/if}}{{/if}}{{/if}}{{/if}}", "depth", 169)]
    [InlineData("Hi {{contact.first_name", "unclosed", 4)]
    [InlineData("Hi {{contact.first_name {{contact.last_name}}", "unclosed", 4)]
    [InlineData("Hi }} there", "stray", 4)]
    [InlineData("Hi {{contact.first_name}}}", "stray", 26)]
    [InlineData("{{#if contact.title}}x{{/if}}}", "stray", 30)]
    [InlineData("Hi \\{{contact.first_name}}", "escaped", 4)]
    public void A_construct_the_standard_refuses_is_refused_by_name_where_it_starts(string template, string key, int position)
    {
        var refusal = TemplateGrammar.Check(template);

        Assert.NotNull(refusal);
        Assert.Equal((key, position), (refusal.Row.Key, refusal.Position));
        Assert.Equal(TemplateGrammar.Kind.Refused, refusal.Row.Kind);
    }

    /// <summary>The refusal is the first construct met reading from the start, never a later one.</summary>
    [Theory]
    [InlineData("a }} b {{> c}}", "stray", 3)]
    [InlineData("{{toLower contact.first_name}} {{> c}}", "helper", 1)]
    [InlineData("{{#if contact.title}}x{{> c}}", "partial", 23)]
    [InlineData("{{toLower contact.first_name}}}", "helper", 1)]
    public void The_first_construct_met_is_the_refusal(string template, string key, int position)
    {
        var refusal = TemplateGrammar.Check(template);

        Assert.NotNull(refusal);
        Assert.Equal((key, position), (refusal.Row.Key, refusal.Position));
    }

    /// <summary>What the standard writes is accepted: values, fallbacks, conditions and comparisons, every listed name.</summary>
    [Theory]
    [InlineData("Hi {{contact.first_name}},")]
    [InlineData("Hi {{contact.first_name | 'there'}},")]
    [InlineData("Hi{{contact.first_name | ''}},")]
    [InlineData("{{#if contact.title}}As {{contact.title}}{{else}}Hello{{/if}}")]
    [InlineData("{{#if contact.title == 'CEO'}}x{{/if}}")]
    [InlineData("{{#if contact.company != 'Acme, Inc.'}}x{{else}}y{{/if}}")]
    [InlineData("{{#if contact.custom.tier == 'gold'}}x{{/if}}")]
    [InlineData("{{contact.custom.Tier_2}} {{contact.custom._x}}")]
    [InlineData("{{sender.first_name}} {{sender.last_name}} {{sender.name}} {{sender.address}} {{contact.email}} {{contact.last_name}} {{contact.company}}")]
    [InlineData("A single { brace }, a lone } and [square brackets] are text.")]
    [InlineData("A backslash \\ alone, and @prompt without its brackets.")]
    [InlineData("Line one\nline two")]
    [InlineData("{{#if contact.title}}{{#if contact.title}}{{#if contact.title}}{{#if contact.title}}{{#if contact.title}}{{#if contact.title}}{{#if contact.title}}{{#if contact.title}}x{{/if}}{{/if}}{{/if}}{{/if}}{{/if}}{{/if}}{{/if}}{{/if}}")]
    [InlineData("No tag at all.")]
    public void What_the_standard_writes_is_accepted(string template)
    {
        Assert.Null(TemplateGrammar.Check(template));
    }

    /// <summary>
    /// The dialect asserts the format: a schema may name `jason-template`, and a value it names is held to the standard
    /// with the format's own reason, at its own pointer.
    /// </summary>
    [Fact]
    public void A_template_format_holds_a_value_to_the_standard()
    {
        var schema = new JsonObject { ["type"] = "string", ["format"] = "jason-template" };

        Assert.Empty(SchemaValidator.CheckDialect(schema));
        Assert.Empty(SchemaValidator.Validate(JsonValue.Create("Hi {{contact.first_name | 'there'}}"), schema));
        var problem = Assert.Single(SchemaValidator.Validate(JsonValue.Create("Hi {{toLower contact.first_name}}"), schema));
        Assert.Equal(("format", ""), (problem.Reason, problem.Pointer));
    }

    /// <summary>
    /// A refusal names the construct and where it starts, and never the template: a template is somebody's message, and
    /// the refusal travels to answers and records that are not theirs to fill.
    /// </summary>
    [Fact]
    [Critical(Family.B3, "a template refusal never quotes the template")]
    public void A_refusal_names_the_construct_and_where_it_starts_and_never_the_template()
    {
        var schema = new JsonObject { ["type"] = "string", ["format"] = "jason-template" };

        var problem = Assert.Single(SchemaValidator.Validate(JsonValue.Create("SENTINEL_TPL {{toLower contact.first_name}}"), schema));

        Assert.DoesNotContain("SENTINEL_TPL", problem.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("first_name", problem.Message, StringComparison.Ordinal);
        Assert.Contains("a helper or a macro at character 14", problem.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A combinator's message carries what its first failing alternative said, so a refused construct inside one is
    /// named rather than only its reason code.
    /// </summary>
    [Fact]
    public void An_any_of_says_what_its_first_alternative_said()
    {
        var schema = new JsonObject
        {
            ["anyOf"] = new JsonArray(
                new JsonObject { ["type"] = "string", ["format"] = "jason-template" },
                new JsonObject { ["type"] = "integer" }),
        };

        var problem = Assert.Single(SchemaValidator.Validate(JsonValue.Create("{{> footer}}"), schema));

        Assert.Equal("any_of", problem.Reason);
        Assert.Contains("the first reported `format` at `/`", problem.Message, StringComparison.Ordinal);
        Assert.Contains("a partial at character 1", problem.Message, StringComparison.Ordinal);
    }

    /// <summary>An <c>allOf</c> says what its first failing requirement said, in that requirement's own words, as an <c>anyOf</c> does.</summary>
    [Fact]
    public void An_all_of_says_what_its_first_failing_requirement_said()
    {
        var schema = new JsonObject
        {
            ["allOf"] = new JsonArray(
                new JsonObject { ["type"] = "string" },
                new JsonObject { ["type"] = "string", ["format"] = "jason-template" }),
        };

        var problem = Assert.Single(SchemaValidator.Validate(JsonValue.Create("{{> footer}}"), schema));

        Assert.Equal("all_of", problem.Reason);
        Assert.Contains("requirement 2 reported `format` at `/`", problem.Message, StringComparison.Ordinal);
        Assert.Contains("a partial at character 1", problem.Message, StringComparison.Ordinal);
    }
}
