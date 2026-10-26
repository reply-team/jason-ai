using System.Text.Json.Nodes;
using Jason.Contracts.Operations;

namespace Jason.Contracts.Tests.OperationContracts;

/// <summary>
/// The one example a person approving a template reads: the template rendered over the composed input as the standard
/// says, a name the input does not hold left for the provider and listed, and nothing escaped. It is the runtime's
/// rendering, read from the same input the plugin receives.
/// </summary>
public class TemplateExampleTests
{
    private static readonly OperationPreview Preview = OperationCatalog.Find("message.send")!.Preview!;

    [Fact]
    public void A_name_the_input_holds_is_filled_with_its_value()
    {
        var example = Example("The Lisbon meetup, {{contact.first_name}}", "Hi {{contact.first_name}} {{contact.last_name}} of {{contact.company}}, from {{sender.address}} to {{contact.email}}.")!;

        Assert.Equal("The Lisbon meetup, Marta", (string?)example["subject"]);
        Assert.Equal("Hi Marta Alvarez of Puerto Analytics, from ann@example.com to marta@example.com.", (string?)example["body"]);
        Assert.Empty(example["unfilled"]!.AsArray());
    }

    [Fact]
    public void A_name_with_no_value_and_no_fallback_is_shown_by_its_name_and_listed()
    {
        var example = Example(null, "Hi {{contact.title}}, and{{#if contact.title}} never{{/if}} more.", title: "  ")!;

        Assert.Equal("Hi [contact.title], and more.", (string?)example["body"]);
        Assert.Equal("""["contact.title"]""", example["unfilled"]!.ToJsonString());
    }

    [Fact]
    public void A_name_with_no_value_takes_its_fallback_and_is_not_listed()
    {
        var example = Example(null, "Hi {{contact.title | 'there'}}.", title: null)!;

        Assert.Equal("Hi there.", (string?)example["body"]);
        Assert.Empty(example["unfilled"]!.AsArray());
    }

    [Fact]
    public void A_name_the_input_does_not_hold_takes_its_fallback_or_its_name_and_is_listed_once_in_the_order_read()
    {
        var example = Example(
            "For {{sender.first_name}}",
            "{{contact.custom.team | 'your team'}} and {{sender.first_name}}, {{sender.name}} and {{contact.custom.team}}.")!;

        Assert.Equal("For [sender.first_name]", (string?)example["subject"]);
        Assert.Equal("your team and [sender.first_name], [sender.name] and [contact.custom.team].", (string?)example["body"]);
        Assert.Equal("""["sender.first_name","contact.custom.team","sender.name"]""", example["unfilled"]!.ToJsonString());
    }

    [Fact]
    public void A_condition_on_a_name_the_input_does_not_hold_takes_its_other_branch_and_is_listed()
    {
        var example = Example(null, "{{#if sender.last_name}}a{{else}}b{{/if}}")!;

        Assert.Equal("b", (string?)example["body"]);
        Assert.Equal("""["sender.last_name"]""", example["unfilled"]!.ToJsonString());
    }

    [Theory]
    [InlineData("Head of Growth", "{{#if contact.title == 'Head of Growth'}}yes{{else}}no{{/if}}", "yes")]
    [InlineData("head of growth", "{{#if contact.title == 'Head of Growth'}}yes{{else}}no{{/if}}", "no")]
    [InlineData("Head of Growth", "{{#if contact.title != 'Head of Growth'}}yes{{else}}no{{/if}}", "no")]
    [InlineData(null, "{{#if contact.title == ''}}empty{{else}}no{{/if}}", "empty")]
    [InlineData(" ", "{{#if contact.title != ''}}set{{else}}empty{{/if}}", "empty")]
    [InlineData("CEO", "{{#if contact.title}}{{#if contact.company}}both{{else}}title{{/if}}{{/if}}", "both")]
    public void A_comparison_is_exact_and_reads_no_value_as_empty_text(string? title, string body, string expected)
    {
        var example = Example(null, body, title: title)!;

        Assert.Equal(expected, (string?)example["body"]);
    }

    [Fact]
    public void A_value_is_written_as_the_field_holds_it_and_nothing_is_escaped()
    {
        var example = Example(null, "<p>{{contact.company}} & co</p>", company: "A&B <i>x</i> 'q'")!;

        Assert.Equal("<p>A&B <i>x</i> 'q' & co</p>", (string?)example["body"]);
    }

    [Fact]
    public void A_message_without_a_subject_has_none_in_its_example()
    {
        var example = Example(null, "Hi {{contact.first_name}}.")!;

        Assert.Null(example["subject"]);
        Assert.Equal("Hi Marta.", (string?)example["body"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("text")]
    public void A_final_message_has_no_example(string? compose)
    {
        var input = Input("Hi", "Hi {{contact.first_name}}.", "Head of Growth", "Puerto Analytics");
        var message = input["args"]!["message"]!.AsObject();
        message.Remove("compose");
        if (compose is not null)
        {
            message["compose"] = compose;
        }

        Assert.Null(Preview.Example(input));
    }

    [Fact]
    public void An_input_with_no_message_has_no_example()
    {
        Assert.Null(Preview.Example(new JsonObject { ["args"] = new JsonObject() }));
        Assert.Null(Preview.Example(null));
    }

    private static JsonObject? Example(string? subject, string body, string? title = "Head of Growth", string? company = "Puerto Analytics") =>
        Preview.Example(Input(subject, body, title, company));

    private static JsonObject Input(string? subject, string body, string? title, string? company)
    {
        var message = new JsonObject { ["format"] = "email", ["compose"] = "template", ["body"] = body };
        if (subject is not null)
        {
            message["subject"] = subject;
        }

        return new JsonObject
        {
            ["args"] = new JsonObject
            {
                ["channel"] = "email",
                ["sender"] = new JsonObject { ["external_id"] = "s_12", ["address"] = "ann@example.com" },
                ["message"] = message,
            },
            ["contacts"] = new JsonArray(new JsonObject
            {
                ["id"] = "cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
                ["first_name"] = "Marta",
                ["last_name"] = "Alvarez",
                ["company"] = company,
                ["title"] = title,
                ["time_zone"] = null,
                ["channels"] = new JsonArray(new JsonObject { ["channel"] = "email", ["value"] = "marta@example.com", ["primary"] = true }),
                ["external_ids"] = new JsonObject { ["contact"] = "p_88421" },
            }),
            ["idempotency_key"] = "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRF",
        };
    }
}
