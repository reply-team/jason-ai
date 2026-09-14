namespace Jason.Contracts.Tests;

/// <summary>
/// Guards a platform decision: the build keeps ICU-based globalization. Contact time zones are first-class in
/// the domain and are expressed as IANA ids, and on Windows <see cref="TimeZoneInfo.FindSystemTimeZoneById"/>
/// can map an IANA id only through ICU. Globalization-invariant mode would make these lookups throw.
/// </summary>
public class PlatformTests
{
    [Theory]
    [InlineData("Europe/Kyiv", 2)]
    [InlineData("America/Vancouver", -8)]
    public void Iana_time_zone_ids_resolve_on_every_platform(string ianaId, int baseUtcOffsetHours)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(ianaId);

        Assert.Equal(TimeSpan.FromHours(baseUtcOffsetHours), zone.BaseUtcOffset);
    }
}
