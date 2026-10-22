using System.Text.Json.Nodes;
using Jason.Contracts.Operations;
using Jason.Runtime.Tests;

namespace Jason.Contracts.Tests.OperationContracts;

/// <summary>
/// The preview a person reads before deciding is one line rendered from the composed input by a template that has
/// placeholders and nothing else: no conditionals, no escapes, no logic. Each rule of the rendering is one test, and
/// so is each thing the load refuses, because a template that a plugin author misspells has to fail in this
/// repository rather than render a dash in front of somebody approving a send.
/// </summary>
public class PreviewTemplateTests
{
    private static string Render(string template, string input) =>
        PreviewTemplate.Parse(template).Render(JsonNode.Parse(input));

    [Fact]
    public void A_string_renders_bare()
    {
        Assert.Equal("Enroll Marta now", Render("Enroll {/name} now", """{"name":"Marta"}"""));
    }

    [Fact]
    public void A_number_renders_as_its_literal()
    {
        Assert.Equal("step 3 of 2.5", Render("step {/step} of {/ratio}", """{"step":3,"ratio":2.5}"""));
    }

    [Fact]
    public void A_boolean_renders_as_its_literal()
    {
        Assert.Equal("primary: true", Render("primary: {/primary}", """{"primary":true}"""));
    }

    [Fact]
    public void A_null_renders_as_a_dash()
    {
        Assert.Equal("Enroll Marta —", Render("Enroll {/first} {/last}", """{"first":"Marta","last":null}"""));
    }

    [Theory]
    [InlineData("{/missing}")]
    [InlineData("{/campaign/name}")]
    [InlineData("{/contacts/0/first_name}")]
    [InlineData("{/contacts/1/first_name}")]
    [InlineData("{/contacts/00/first_name}")]
    [InlineData("{/name/length}")]
    public void Something_absent_renders_as_a_dash(string template)
    {
        Assert.Equal("—", Render(template, """{"name":"Marta","contacts":[{"last_name":"Silva"}]}"""));
    }

    [Fact]
    public void An_object_renders_as_compact_json_with_its_text_left_as_written()
    {
        Assert.Equal(
            """start at {"position":"step","step":3,"city":"Київ <b>"}""",
            Render("start at {/start}", """{"start":{"position":"step","step":3,"city":"Київ <b>"}}"""));
    }

    [Fact]
    public void An_array_renders_as_compact_json()
    {
        Assert.Equal("""to ["a","b"]""", Render("to {/to}", """{"to":["a","b"]}"""));
    }

    /// <summary>
    /// A summary is one line in a terminal. A line break inside a person's name would let a value forge a second
    /// line of the preview — "Approved by the team lead" under the real one — so every control character in a
    /// value, and both Unicode line separators, become a space.
    /// </summary>
    [Fact]
    [Critical(Family.S2, "what a person approves reads as it is")]
    public void A_control_character_in_a_value_becomes_a_space()
    {
        Assert.Equal(
            "Enroll Marta Approved  by a lead ok",
            Render("Enroll {/name} ok", """{"name":"Marta\nApproved\t\u0000by\u2028a\u2029lead"}"""));
    }

    /// <summary>
    /// A format character reorders or hides what a person reads: a right-to-left override inside a contact's name
    /// turns the rest of the line around, and a zero-width one hides where a value ends. Every format character in a
    /// value becomes a space, as a control character does, one outside the Basic Multilingual Plane included.
    /// </summary>
    [Theory]
    // Critical: a hidden format character cannot disguise a preview.
    [InlineData(0x202A, Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.S2)])]
    [InlineData(0x202B)]
    [InlineData(0x202C)]
    [InlineData(0x202D)]
    [InlineData(0x202E)]
    [InlineData(0x2066)]
    [InlineData(0x2067)]
    [InlineData(0x2068)]
    [InlineData(0x2069)]
    [InlineData(0x200B)]
    [InlineData(0x200C)]
    [InlineData(0x200D)]
    [InlineData(0x200E)]
    [InlineData(0x200F)]
    [InlineData(0xFEFF)]
    [InlineData(0x061C)]
    [InlineData(0x2060)]
    [InlineData(0xE0041)]
    public void A_format_character_in_a_value_becomes_a_space(int code)
    {
        var input = new JsonObject
        {
            ["name"] = "Marta" + char.ConvertFromUtf32(code) + "Alvarez",
            ["start"] = new JsonObject { ["step"] = "two" + char.ConvertFromUtf32(code) },
        };

        Assert.Equal(
            """Enroll Marta Alvarez at {"step":"two "}""",
            PreviewTemplate.Parse("Enroll {/name} at {/start}").Render(input));
    }

    [Fact]
    public void A_pointer_escapes_a_slash_and_a_tilde_as_json_pointer_does()
    {
        Assert.Equal("1 2", Render("{/a~1b} {/c~0d}", """{"a/b":1,"c~d":2}"""));
    }

    [Fact]
    public void A_template_without_a_placeholder_renders_as_written()
    {
        Assert.Equal("Read the campaign.", Render("Read the campaign.", "{}"));
    }

    [Fact]
    public void The_template_keeps_its_text_and_lists_its_pointers_in_order()
    {
        var template = PreviewTemplate.Parse("Enroll {/contacts/0/first_name} in {/campaign/name}");

        Assert.Equal("Enroll {/contacts/0/first_name} in {/campaign/name}", template.Text);
        Assert.Equal(["/contacts/0/first_name", "/campaign/name"], template.Pointers);
    }

    [Theory]
    [InlineData("Enroll } now")]
    [InlineData("Enroll { now")]
    [InlineData("Enroll {name} now")]
    [InlineData("Enroll {/name now")]
    [InlineData("Enroll {/name{/x}} now")]
    [InlineData("Enroll {/na~2me} now")]
    [InlineData("Enroll {/name~} now")]
    [InlineData("Enroll {} now")]
    [InlineData("")]
    [InlineData("   ")]
    public void A_template_that_is_not_text_and_placeholders_is_refused(string template)
    {
        Assert.Throws<FormatException>(() => PreviewTemplate.Parse(template));
    }

    [Fact]
    public void A_highlight_carries_the_value_it_points_at_or_null_when_it_points_at_nothing()
    {
        var preview = new OperationPreview(PreviewTemplate.Parse("Enroll {/name}"), ["/name", "/args/start", "/missing", "/gone"]);

        var highlights = preview.Highlight(JsonNode.Parse("""{"name":"Marta","args":{"start":{"position":"first_step"}},"gone":null}"""));

        Assert.Equal(
            """{"/name":"Marta","/args/start":{"position":"first_step"},"/missing":null,"/gone":null}""",
            highlights.ToJsonString());
    }

    [Fact]
    public void A_highlight_is_a_copy_and_never_the_input_itself()
    {
        var input = JsonNode.Parse("""{"args":{"start":{"position":"first_step"}}}""")!;
        var preview = new OperationPreview(PreviewTemplate.Parse("x"), ["/args/start"]);

        var highlights = preview.Highlight(input);
        highlights["/args/start"]!["position"] = "step";

        Assert.Equal("first_step", (string?)input["args"]!["start"]!["position"]);
    }

    // -----------------------------------------------------------------------------------------------------------
    // What the load refuses: the walk over the input schema (plain `properties` and `items` only)
    // -----------------------------------------------------------------------------------------------------------

    private const string Schema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["args", "contacts", "campaign"],
          "properties": {
            "args": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "collision": { "type": "string", "enum": ["skip", "refuse"] },
                "window": { "type": "object", "anyOf": [ { "required": ["from"] }, { "required": ["preset"] } ] },
                "extra": { "type": "object" }
              }
            },
            "contacts": { "type": "array", "minItems": 1, "maxItems": 1, "items": { "$ref": "#/$defs/contact" } },
            "campaign": { "$ref": "#/$defs/campaign" }
          },
          "anyOf": [ { "required": ["args"] }, { "required": ["campaign"] } ],
          "$defs": {
            "contact": { "type": "object", "additionalProperties": false, "properties": { "first_name": { "type": "string" } } },
            "campaign": { "type": "object", "additionalProperties": false, "properties": { "name": { "type": "string" } } }
          }
        }
        """;

    private static OperationContract Contract(string summary, params string[] highlights)
    {
        var document = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(ContractFiles.Operations, "campaign.get.json")))!;
        document["input_schema"] = JsonNode.Parse(Schema);
        document["preview"] = new JsonObject
        {
            ["summary"] = summary,
            ["highlights"] = new JsonArray([.. highlights.Select(pointer => (JsonNode)pointer)]),
        };

        return OperationContract.Parse("campaign.get.json", document);
    }

    [Theory]
    [InlineData("Enroll {/contacts/0/first_name}")]
    [InlineData("Enroll {/contacts/0/surname_behind_a_ref}")]
    [InlineData("in {/campaign/name}")]
    [InlineData("in {/campaign/anything_behind_a_ref}")]
    [InlineData("{/args/collision} if they take part")]
    [InlineData("{/args/window/from} to {/args/window/to}")]
    [InlineData("{/args/extra/whatever}")]
    [InlineData("{/args}")]
    public void A_pointer_the_schema_can_address_is_accepted_and_one_behind_a_reference_is_left_to_the_fixtures(string summary)
    {
        Assert.NotNull(Contract(summary).Preview);
    }

    [Theory]
    [InlineData("Enroll {/contacts/1/first_name}", "/contacts/1/first_name")]
    [InlineData("Enroll {/contacts/first/first_name}", "/contacts/first/first_name")]
    [InlineData("{/args/colision} if they take part", "/args/colision")]
    [InlineData("{/campaing/name}", "/campaing/name")]
    [InlineData("{/args/collision/word}", "/args/collision/word")]
    public void A_pointer_the_schema_cannot_address_is_refused_by_name(string summary, string pointer)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Contract(summary));

        Assert.Contains("campaign.get.json", error.Message, StringComparison.Ordinal);
        Assert.Contains($"`{pointer}`", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_highlight_is_walked_like_a_placeholder()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Contract("Enroll {/args/collision}", "/args/colision"));

        Assert.Contains("`/args/colision`", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_highlight_named_twice_is_refused()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Contract("Enroll {/args/collision}", "/args/collision", "/args/collision"));

        Assert.Contains("`/args/collision`", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Enroll } now")]
    [InlineData("Enroll {name}")]
    public void A_summary_that_does_not_parse_is_refused_naming_the_document(string summary)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Contract(summary));

        Assert.Contains("campaign.get.json", error.Message, StringComparison.Ordinal);
        Assert.Contains("preview.summary", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_highlight_that_is_not_a_pointer_is_refused_naming_the_document()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Contract("Enroll {/args/collision}", "args/collision"));

        Assert.Contains("campaign.get.json", error.Message, StringComparison.Ordinal);
        Assert.Contains("preview.highlights", error.Message, StringComparison.Ordinal);
    }
}
