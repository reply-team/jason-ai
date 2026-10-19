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
    [InlineData("telegram", "  @jane  ", "@jane")]
    public void A_value_is_normalized_by_the_rule_of_its_channel(string channel, string raw, string expected)
    {
        var errors = new ValidationErrors();

        Assert.Equal(expected, ChannelRules.NormalizeValue(channel, raw, "channels[0].value", errors));
        Assert.False(errors.Any);
    }

    /// <summary>
    /// How a LinkedIn value is keyed: as it is written in, what the rule before this one stored, and what it stores now.
    /// The case of the scheme and the host, a default port, any user name, one trailing slash and a fragment never count.
    /// On linkedin.com and its subdomains a profile link names a person by its slug whatever its case, so the path is
    /// lowercased there and nowhere else; the query keeps its case everywhere. The middle column is what a stored value
    /// looked like before, which the migration that lowercases the stored keys is tested against, and the plugin's own
    /// comparison is held to the same pairs. A letter written as a percent-escape keeps its case: `É` and `é` are two
    /// escapes, and lowercasing an escape is not lowercasing the letter.
    /// </summary>
    public static TheoryData<string, string, string> LinkedInKeys { get; } = new()
    {
        { "HTTPS://WWW.LinkedIn.com/in/Jane/#x", "https://www.linkedin.com/in/Jane", "https://www.linkedin.com/in/jane" },
        { "https://www.linkedin.com/in/Marta-Example", "https://www.linkedin.com/in/Marta-Example", "https://www.linkedin.com/in/marta-example" },
        { "https://uk.linkedin.com/in/Marta-Example/", "https://uk.linkedin.com/in/Marta-Example", "https://uk.linkedin.com/in/marta-example" },
        { "https://linkedin.com/in/Marta-Example", "https://linkedin.com/in/Marta-Example", "https://linkedin.com/in/marta-example" },
        { "https://www.linkedin.com/in/Marta-Example?Trk=Profile", "https://www.linkedin.com/in/Marta-Example?Trk=Profile", "https://www.linkedin.com/in/marta-example?Trk=Profile" },
        { "https://www.linkedin.com:443/in/Marta-Example", "https://www.linkedin.com/in/Marta-Example", "https://www.linkedin.com/in/marta-example" },
        { "https://www.linkedin.com:8443/in/Marta-Example", "https://www.linkedin.com:8443/in/Marta-Example", "https://www.linkedin.com:8443/in/marta-example" },
        { "http://www.linkedin.com/in/Marta-Example", "http://www.linkedin.com/in/Marta-Example", "http://www.linkedin.com/in/marta-example" },
        { "https://marta@www.linkedin.com/in/Marta-Example", "https://www.linkedin.com/in/Marta-Example", "https://www.linkedin.com/in/marta-example" },
        { "https://www.linkedin.com/in/Marta-Example//", "https://www.linkedin.com/in/Marta-Example/", "https://www.linkedin.com/in/marta-example/" },
        { "https://www.linkedin.com/in/JOSÉ-Example","https://www.linkedin.com/in/JOS%C3%89-Example", "https://www.linkedin.com/in/jos%c3%89-example" },
        { "https://LinkedIn.com/", "https://linkedin.com", "https://linkedin.com" },
        { "https://www.linkedin.com/?Ref=Feed", "https://www.linkedin.com?Ref=Feed", "https://www.linkedin.com?Ref=Feed" },
        { "https://notlinkedin.com/in/Marta-Example", "https://notlinkedin.com/in/Marta-Example", "https://notlinkedin.com/in/Marta-Example" },
        { "https://linkedin.com.example.org/in/Marta-Example", "https://linkedin.com.example.org/in/Marta-Example", "https://linkedin.com.example.org/in/Marta-Example" },
        { "http://x.example.com:8080/A?b=1#z", "http://x.example.com:8080/A?b=1", "http://x.example.com:8080/A?b=1" },
    };

    [Theory]
    [MemberData(nameof(LinkedInKeys))]
    public void A_linkedin_value_is_keyed_by_its_lowercased_slug_on_linkedin_and_as_written_elsewhere(string raw, string before, string now)
    {
        var errors = new ValidationErrors();

        var keyed = ChannelRules.NormalizeValue("linkedin", raw, "channels[0].value", errors);

        Assert.False(errors.Any, $"'{raw}' was refused.");
        Assert.True(keyed == now, $"'{raw}' is keyed '{keyed}', not '{now}' (the rule before stored '{before}').");
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
