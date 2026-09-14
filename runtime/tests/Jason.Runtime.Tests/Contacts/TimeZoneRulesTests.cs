using Jason.Runtime.Contacts;
using Jason.Runtime.Domain;

namespace Jason.Runtime.Tests.Contacts;

public class TimeZoneRulesTests
{
    [Theory]
    [InlineData("Europe/Kyiv", "Europe/Kyiv")]
    [InlineData("America/Vancouver", "America/Vancouver")]
    [InlineData("Etc/UTC", "Etc/UTC")]
    [InlineData("  Asia/Tokyo  ", "Asia/Tokyo")]
    public void An_iana_id_is_accepted_and_stored_as_given(string raw, string expected)
    {
        var errors = new ValidationErrors();

        Assert.Equal(expected, TimeZoneRules.Normalize(raw, "time_zone", errors));
        Assert.False(errors.Any);
    }

    [Theory]
    [InlineData("Pacific Standard Time")]
    [InlineData("FLE Standard Time")]
    [InlineData("Mars/Olympus")]
    [InlineData("+02:00")]
    public void A_windows_id_or_an_unknown_id_is_refused(string raw)
    {
        var errors = new ValidationErrors();

        Assert.Null(TimeZoneRules.Normalize(raw, "time_zone", errors));

        var detail = Assert.Single(Assert.Throws<ValidationException>(errors.ThrowIfAny).Details!);
        Assert.Equal("time_zone", detail.Field);
        Assert.Equal("invalid", detail.Code);
        Assert.DoesNotContain(raw, detail.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_absent_time_zone_is_simply_absent(string? raw)
    {
        var errors = new ValidationErrors();

        Assert.Null(TimeZoneRules.Normalize(raw, "time_zone", errors));
        Assert.False(errors.Any);
    }
}
