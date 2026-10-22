using Jason.Contracts.Channels;
using Jason.Runtime.Contacts;
using Jason.Runtime.Domain;

namespace Jason.Runtime.Tests.Contacts;

/// <summary>
/// The channel rules as a request meets them: each refusal recorded on the field it was given in, with its code and the
/// rule, and never the value. The rules themselves are held by their own tests beside them.
/// </summary>
public class ChannelValidationTests
{
    [Fact]
    public void A_channel_name_and_a_value_the_rules_accept_record_nothing()
    {
        var errors = new ValidationErrors();

        Assert.Equal("linkedin", ChannelValidation.Name(" LinkedIn ", "channel", errors));
        Assert.Equal("https://www.linkedin.com/in/marta-example", ChannelValidation.Value("linkedin", "uk.linkedin.com/in/Marta-Example/", "value", errors));
        Assert.False(errors.Any);
    }

    [Theory]
    [InlineData(null, "required")]
    [InlineData("  ", "required")]
    [InlineData("e-mail", "invalid")]
    public void A_channel_name_the_rules_refuse_is_recorded_on_its_field(string? raw, string code)
    {
        var errors = new ValidationErrors();

        Assert.Null(ChannelValidation.Name(raw, "channels[0].channel", errors));

        var detail = Assert.Single(Assert.Throws<ValidationException>(errors.ThrowIfAny).Details!);
        Assert.Equal(("channels[0].channel", code), (detail.Field, detail.Code));
    }

    [Theory]
    [InlineData("email", "a@b", "invalid")]
    [InlineData("phone", "6045550100", "invalid")]
    [InlineData("linkedin", "linkedin.com/company/example", "invalid")]
    [InlineData("linkedin", "https://example.com/in/x", "invalid")]
    [InlineData("telegram", "   ", "required")]
    public void A_value_its_channel_cannot_accept_is_recorded_on_its_field(string channel, string raw, string code)
    {
        var errors = new ValidationErrors();

        Assert.Null(ChannelValidation.Value(channel, raw, "channels[0].value", errors));

        var detail = Assert.Single(Assert.Throws<ValidationException>(errors.ThrowIfAny).Details!);
        Assert.Equal(("channels[0].value", code), (detail.Field, detail.Code));
        if (code == "invalid")
        {
            Assert.Equal(ChannelRules.RuleOf(channel), detail.Message);
        }
    }

    [Theory]
    // Critical: a refusal never echoes an address.
    [InlineData("email", "ada.lovelace@analyticalengine", Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.B3)])]
    [InlineData("phone", "0987654321")]
    [InlineData("linkedin", "in/ada-lovelace")]
    [InlineData("linkedin", "https://www.linkedin.com/pub/ada-lovelace/1/2/3")]
    [InlineData("telegram", "")]
    public void A_refusal_names_the_rule_and_never_the_submitted_value(string channel, string raw)
    {
        var errors = new ValidationErrors();

        ChannelValidation.Value(channel, raw, "channels[0].value", errors);

        var detail = Assert.Single(Assert.Throws<ValidationException>(errors.ThrowIfAny).Details!);
        Assert.DoesNotContain("ada", detail.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("0987654321", detail.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_longer_than_the_column_is_refused_as_too_long()
    {
        var errors = new ValidationErrors();

        Assert.Null(ChannelValidation.Value("telegram", new string('x', ChannelRules.MaxValueLength + 1), "channels[0].value", errors));

        var detail = Assert.Single(Assert.Throws<ValidationException>(errors.ThrowIfAny).Details!);
        Assert.Equal("too_long", detail.Code);
    }
}
