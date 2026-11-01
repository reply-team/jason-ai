using System.Text.Json.Nodes;
using Jason.Runtime.Contacts;
using Jason.Runtime.Domain;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Tests.Contacts;

public class MatchByTests
{
    private static ContactDraft Draft(JsonObject? custom, params ContactChannel[] channels) =>
        new(null, null, null, null, null, channels, custom ?? []);

    private static ContactChannel Channel(string channel, string value) => new() { Channel = channel, Value = value };

    [Theory]
    [InlineData("email", "email")]
    [InlineData("Email", "email")]
    [InlineData("  linkedin ", "linkedin")]
    [InlineData("crm_handle", "crm_handle")]
    public void A_channel_key_is_the_normalized_channel_name(string raw, string expected)
    {
        var errors = new ValidationErrors();

        var key = Assert.IsType<MatchKey.ByChannel>(MatchKey.Parse(raw, errors));

        Assert.Equal(expected, key.Channel);
        Assert.False(errors.Any);
    }

    [Fact]
    public void A_custom_key_names_the_field_it_reads()
    {
        var errors = new ValidationErrors();

        var key = Assert.IsType<MatchKey.ByCustomField>(MatchKey.Parse("custom:crm_id", errors));

        Assert.Equal("crm_id", key.Field);
        Assert.False(errors.Any);
    }

    [Theory]
    [InlineData("custom:")]
    [InlineData("custom:a b")]
    [InlineData("custom:a.b")]
    [InlineData("custom:$.a")]
    [InlineData("e-mail")]
    [InlineData("1")]
    public void A_malformed_match_by_is_recorded_on_its_own_field(string raw)
    {
        var errors = new ValidationErrors();

        Assert.Null(MatchKey.Parse(raw, errors));

        var detail = Assert.Single(Assert.Throws<ValidationException>(errors.ThrowIfAny).Details!);
        Assert.Equal("match_by", detail.Field);
        Assert.Equal("invalid", detail.Code);
    }

    [Fact]
    public void A_custom_field_longer_than_the_limit_is_refused()
    {
        var errors = new ValidationErrors();

        Assert.Null(MatchKey.Parse("custom:" + new string('f', 65), errors));

        Assert.True(errors.Any);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_absent_match_by_leaves_every_item_creating_its_own_contact(string? raw)
    {
        var errors = new ValidationErrors();

        Assert.Null(MatchKey.Parse(raw, errors));
        Assert.False(errors.Any);
    }

    [Fact]
    public void A_channel_key_reads_the_first_entry_of_that_channel()
    {
        var key = new MatchKey.ByChannel("email");

        Assert.Equal("a@b.co", key.ValueOf(Draft(null, Channel("linkedin", "https://example.com/in/a"), Channel("email", "a@b.co"), Channel("email", "c@d.co"))));
        Assert.Null(key.ValueOf(Draft(null, Channel("linkedin", "https://example.com/in/a"))));
        Assert.Null(key.ValueOf(Draft(null)));
    }

    [Fact]
    public void A_custom_key_reads_its_field_as_text_and_ignores_anything_that_is_not_a_scalar()
    {
        var key = new MatchKey.ByCustomField("crm_id");

        Assert.Equal("42", key.ValueOf(Draft(new JsonObject { ["crm_id"] = 42 })));
        Assert.Equal("abc", key.ValueOf(Draft(new JsonObject { ["crm_id"] = "abc" })));
        Assert.Null(key.ValueOf(Draft(new JsonObject { ["crm_id"] = new JsonObject() })));
        Assert.Null(key.ValueOf(Draft(new JsonObject { ["crm_id"] = null })));
        Assert.Null(key.ValueOf(Draft(new JsonObject { ["other"] = "abc" })));
    }
}
