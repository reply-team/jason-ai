using System.Text.Json.Nodes;
using Jason.Cli.Logs;

namespace Jason.Cli.Tests.Logs;

/// <summary>
/// A line of the runtime's log carries the template it was written from and the values that went into it, not the
/// sentence a person reads. Putting them back together is a small, forgiving grammar: whatever the template says that
/// this does not understand is left as it was written, because a line that cannot be read is worth more shown
/// plainly than hidden.
/// </summary>
public sealed class MessageTemplateTests
{
    private static JsonObject Properties(string json) => (JsonObject)JsonNode.Parse(json)!;

    [Fact]
    public void A_hole_is_replaced_by_its_value_inserted_plainly()
    {
        var text = MessageTemplate.Render(
            "Jason runtime {Version} starting as {InstanceId} (pid {Pid})",
            Properties("""{"Version":"0.3.0","InstanceId":"inst_01","Pid":4242}"""));

        Assert.Equal("Jason runtime 0.3.0 starting as inst_01 (pid 4242)", text);
    }

    [Theory]
    [InlineData("{@Name}")]
    [InlineData("{$Name}")]
    [InlineData("{Name:l}")]
    [InlineData("{Name:000}")]
    [InlineData("{Name,12}")]
    [InlineData("{Name,-12}")]
    [InlineData("{Name,12:l}")]
    public void A_hole_is_found_whatever_operator_alignment_or_format_it_carries_and_none_of_them_is_applied(string hole)
    {
        // The value goes in as it is: no quotes, no padding, no number formatting. What a person reads is the value.
        Assert.Equal("[ada]", MessageTemplate.Render("[" + hole + "]", Properties("""{"Name":"ada"}""")));
        Assert.Equal("[7]", MessageTemplate.Render("[" + hole + "]", Properties("""{"Name":7}""")));
    }

    [Fact]
    public void A_doubled_brace_is_a_literal_brace_and_is_never_the_start_of_a_hole()
    {
        var properties = Properties("""{"Name":"ada"}""");

        Assert.Equal("{Name} is {not} a hole: ada", MessageTemplate.Render("{{Name}} is {{not}} a hole: {Name}", properties));
        Assert.Equal("{ada}", MessageTemplate.Render("{{{Name}}}", properties));
        Assert.Equal("}{", MessageTemplate.Render("}}{{", properties));
    }

    [Fact]
    public void A_hole_with_no_property_stays_exactly_as_written()
    {
        var properties = Properties("""{"Known":"yes"}""");

        Assert.Equal("{Missing} and yes", MessageTemplate.Render("{Missing} and {Known}", properties));
        Assert.Equal("{@Missing} {$Missing} {Missing:000} {Missing,8} {Missing,-8:l}", MessageTemplate.Render("{@Missing} {$Missing} {Missing:000} {Missing,8} {Missing,-8:l}", properties));
    }

    [Fact]
    public void A_property_is_found_by_its_exact_name()
    {
        Assert.Equal("{name} ada", MessageTemplate.Render("{name} {Name}", Properties("""{"Name":"ada"}""")));
    }

    [Fact]
    public void A_hole_never_reads_a_key_the_format_keeps_for_itself()
    {
        // `@t`, `@mt`, `@l` and `@x` are the format's own keys. `{@t}` is the name `t` with the destructuring operator, so
        // it asks for a property called t and finds none, however the line is built.
        var evt = Properties("""{"@t":"2026-10-03T20:05:31.0000000Z","@mt":"{@t}","t":"mine"}""");

        Assert.Equal("mine", MessageTemplate.Render("{@t}", evt));
        Assert.Equal("{@mt}", MessageTemplate.Render("{@mt}", evt));
        Assert.Equal("{@@t}", MessageTemplate.Render("{@@t}", evt));
    }

    [Fact]
    public void What_is_not_text_is_written_as_the_json_it_is_and_a_null_is_null()
    {
        var properties = Properties("""{"Count":3,"Ratio":0.5,"Done":true,"Nothing":null,"Tags":["a","b"],"Who":{"Name":"ada","Age":36}}""");

        Assert.Equal("3 0.5 true null", MessageTemplate.Render("{Count} {Ratio} {Done} {Nothing}", properties));
        Assert.Equal("""["a","b"] {"Name":"ada","Age":36}""", MessageTemplate.Render("{Tags} {@Who}", properties));
    }

    [Fact]
    public void A_value_is_inserted_as_it_is_even_when_it_looks_like_a_template()
    {
        // Rendering is one pass over the template: a value that contains braces is not read for holes of its own.
        var properties = Properties("""{"A":"{B}","B":"never"}""");

        Assert.Equal("{B}", MessageTemplate.Render("{A}", properties));
    }

    [Fact]
    public void Text_outside_a_hole_is_kept_whatever_it_holds()
    {
        var properties = Properties("""{"Name":"ada"}""");

        Assert.Equal("naïve 日本語 \"quoted\" \\ ada", MessageTemplate.Render("naïve 日本語 \"quoted\" \\ {Name}", properties));
        Assert.Equal(string.Empty, MessageTemplate.Render(string.Empty, properties));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("a { b")]
    [InlineData("{Name")]
    [InlineData("{Name:fmt")]
    [InlineData("{}")]
    [InlineData("{ Name}")]
    [InlineData("{Na me}")]
    [InlineData("{Name,}")]
    [InlineData("{Name,x}")]
    [InlineData("{Name,1,2}")]
    [InlineData("{@}")]
    [InlineData("{@@Name}")]
    [InlineData("{-}")]
    [InlineData("}")]
    [InlineData("a } b")]
    public void A_brace_that_does_not_make_a_hole_is_text(string template)
    {
        Assert.Equal(template, MessageTemplate.Render(template, Properties("""{"Name":"ada"}""")));
    }

    [Fact]
    public void A_lone_closing_brace_after_a_hole_is_text()
    {
        // The hole ends at the first closing brace; the one after it has nothing to pair with.
        Assert.Equal("ada}", MessageTemplate.Render("{Name}}", Properties("""{"Name":"ada"}""")));
    }

    [Fact]
    public void A_positional_name_is_a_name_like_any_other()
    {
        Assert.Equal("ada", MessageTemplate.Render("{0}", Properties("""{"0":"ada"}""")));
        Assert.Equal("{1}", MessageTemplate.Render("{1}", Properties("""{"0":"ada"}""")));
    }

    [Fact]
    public void Non_ascii_values_and_names_are_kept_as_they_are()
    {
        Assert.Equal("Привіт, Ада — 日本語", MessageTemplate.Render("Привіт, {Ім_я} — {語}", Properties("""{"Ім_я":"Ада","語":"日本語"}""")));
    }
}
