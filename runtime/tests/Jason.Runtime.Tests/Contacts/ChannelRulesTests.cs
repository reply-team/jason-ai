using Jason.Runtime.Contacts;
using Jason.Runtime.Domain;

namespace Jason.Runtime.Tests.Contacts;

public class ChannelRulesTests
{
    [Theory]
    [InlineData("Email", "email")]
    [InlineData("  LinkedIn  ", "linkedin")]
    [InlineData("whatsapp", "whatsapp")]
    [InlineData("crm_note", "crm_note")]
    public void A_channel_name_is_lower_cased_and_kept(string raw, string expected)
    {
        var errors = new ValidationErrors();

        Assert.Equal(expected, ChannelRules.NormalizeChannelName(raw, "channel", errors));
        Assert.False(errors.Any);
    }

    [Theory]
    [InlineData("e-mail")]
    [InlineData("1email")]
    [InlineData("_email")]
    [InlineData("e mail")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_channel_name_outside_the_vocabulary_shape_is_refused(string? raw)
    {
        var errors = new ValidationErrors();

        Assert.Null(ChannelRules.NormalizeChannelName(raw, "channel", errors));
        Assert.True(errors.Any);
    }

    [Fact]
    public void A_channel_name_longer_than_the_column_is_refused()
    {
        var errors = new ValidationErrors();

        Assert.Null(ChannelRules.NormalizeChannelName("c" + new string('x', 32), "channel", errors));
        Assert.True(errors.Any);
    }

    [Fact]
    public void The_known_channels_are_the_ones_with_a_value_rule()
    {
        Assert.True(ChannelRules.IsKnown("email"));
        Assert.True(ChannelRules.IsKnown("phone"));
        Assert.True(ChannelRules.IsKnown("whatsapp"));
        Assert.True(ChannelRules.IsKnown("linkedin"));
        Assert.False(ChannelRules.IsKnown("telegram"));
    }

    [Theory]
    [InlineData("email", "  Foo@Bar.COM ", "foo@bar.com")]
    [InlineData("email", "jane.doe+tag@sub.example.co.uk", "jane.doe+tag@sub.example.co.uk")]
    [InlineData("phone", "+1 (604) 555-0100", "+16045550100")]
    [InlineData("whatsapp", " +380.44.123.45.67 ", "+380441234567")]
    [InlineData("linkedin", "HTTPS://WWW.LinkedIn.com/in/Jane/#x", "https://www.linkedin.com/in/Jane")]
    [InlineData("linkedin", "http://x.example.com:8080/a?b=1#z", "http://x.example.com:8080/a?b=1")]
    [InlineData("linkedin", "https://LinkedIn.com/", "https://linkedin.com")]
    [InlineData("telegram", "  @jane  ", "@jane")]
    public void A_value_is_normalized_by_the_rule_of_its_channel(string channel, string raw, string expected)
    {
        var errors = new ValidationErrors();

        Assert.Equal(expected, ChannelRules.NormalizeValue(channel, raw, "channels[0].value", errors));
        Assert.False(errors.Any);
    }

    [Theory]
    [InlineData("email", "a@b")]
    [InlineData("email", "\"Name\" <a@b.co>")]
    [InlineData("email", "a b@c.co")]
    [InlineData("email", "not-an-address")]
    [InlineData("phone", "6045550100")]
    [InlineData("phone", "+0445550100")]
    [InlineData("whatsapp", "+")]
    [InlineData("linkedin", "linkedin.com/in/jane")]
    [InlineData("linkedin", "mailto:jane@example.com")]
    [InlineData("telegram", "   ")]
    public void A_value_its_channel_cannot_accept_is_refused(string channel, string raw)
    {
        var errors = new ValidationErrors();

        Assert.Null(ChannelRules.NormalizeValue(channel, raw, "channels[0].value", errors));

        var detail = Assert.Single(Assert.Throws<ValidationException>(errors.ThrowIfAny).Details!);
        Assert.Equal("channels[0].value", detail.Field);
    }

    [Theory]
    [InlineData("email", "ada.lovelace@analyticalengine")]
    [InlineData("phone", "0987654321")]
    [InlineData("linkedin", "in/ada-lovelace")]
    [InlineData("telegram", "")]
    public void A_refusal_names_the_rule_and_never_the_submitted_value(string channel, string raw)
    {
        var errors = new ValidationErrors();

        ChannelRules.NormalizeValue(channel, raw, "channels[0].value", errors);

        var detail = Assert.Single(Assert.Throws<ValidationException>(errors.ThrowIfAny).Details!);
        Assert.DoesNotContain("ada", detail.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("0987654321", detail.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_longer_than_the_column_is_refused_as_too_long()
    {
        var errors = new ValidationErrors();

        Assert.Null(ChannelRules.NormalizeValue("telegram", new string('x', ChannelRules.MaxValueLength + 1), "channels[0].value", errors));

        var detail = Assert.Single(Assert.Throws<ValidationException>(errors.ThrowIfAny).Details!);
        Assert.Equal("too_long", detail.Code);
    }
}
