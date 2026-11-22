using System.Globalization;
using Jason.Runtime.Contacts;

namespace Jason.Runtime.Configuration;

/// <summary>
/// The installation's day: from one midnight to the next in the installation's time zone, <c>Runtime:TimeZone</c> where
/// the settings name one and the system's own zone, as the runtime's clock reads it, where they do not. The day
/// <c>Launches:MaxPerDay</c> counts and a lift of it covers, so the count never starts holding work again at a UTC
/// midnight that falls in the middle of the person's afternoon. A day of 23 or 25 hours, where daylight saving moves the
/// clock, is counted from midnight to midnight as it is.
/// </summary>
/// <remarks>The zone is read through its seam at each call, so an edit applies at the next scan, as the limits do.</remarks>
public sealed class InstallationDay(TimeProvider clock, LiveSettings<RuntimeOptions> runtime)
{
    /// <summary>The day <paramref name="utcNow"/> falls in, in the installation's zone as it stands now.</summary>
    public LocalDay Of(DateTime utcNow)
    {
        var configured = runtime.Current.TimeZone;
        if (!string.IsNullOrWhiteSpace(configured) && ZoneOf(configured) is { } named)
        {
            return LocalDay.Of(utcNow, named, configured.Trim());
        }

        var local = clock.LocalTimeZone;
        return LocalDay.Of(utcNow, local, NameOf(local));
    }

    /// <summary>
    /// The zone an id names, or null where it names none this system knows as an IANA zone: the rule a contact's zone is
    /// held to (<see cref="TimeZoneRules"/>), so a Windows id, which names a different vocabulary on every machine, is none.
    /// </summary>
    public static TimeZoneInfo? ZoneOf(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var trimmed = id.Trim();
        if (TimeZoneRules.IsUtc(trimmed))
        {
            return TimeZoneInfo.Utc;
        }

        return TimeZoneInfo.TryFindSystemTimeZoneById(trimmed, out var zone) && zone.HasIanaId ? zone : null;
    }

    /// <summary>
    /// A zone's name as a person reads it on every system: its IANA id, a Windows id put into IANA's words where the system
    /// knows them, and <c>UTC</c> for UTC, which Windows names with no IANA id.
    /// </summary>
    public static string NameOf(TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (TimeZoneRules.IsUtc(zone.Id))
        {
            return "UTC";
        }

        if (zone.HasIanaId)
        {
            return zone.Id;
        }

        return TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) ? iana : zone.Id;
    }
}

/// <summary>
/// One day of the installation: its local date, the zone it was taken in, and the UTC instants of its midnight and the
/// next one, which it runs from and to.
/// </summary>
public readonly record struct LocalDay(DateOnly Date, string TimeZone, DateTime Start, DateTime End)
{
    /// <summary>The date as a person writes it, <c>yyyy-MM-dd</c>.</summary>
    public string DateText => Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The day <paramref name="utcNow"/> falls in, in <paramref name="zone"/>, named <paramref name="name"/>.</summary>
    public static LocalDay Of(DateTime utcNow, TimeZoneInfo zone, string name)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), zone);
        var date = DateOnly.FromDateTime(local);
        return new LocalDay(date, name, Midnight(date, zone), Midnight(date.AddDays(1), zone));
    }

    /// <summary>
    /// The first UTC instant of <paramref name="date"/> in <paramref name="zone"/>. Where daylight saving skips the local
    /// midnight, the day starts where the clock jumps, the instant the offset before the jump makes of that midnight; where
    /// it repeats it, at the midnight's first occurrence, under the larger of the two offsets.
    /// </summary>
    private static DateTime Midnight(DateOnly date, TimeZoneInfo zone)
    {
        var midnight = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(midnight))
        {
            return DateTime.SpecifyKind(midnight - zone.GetUtcOffset(midnight.AddHours(-3)), DateTimeKind.Utc);
        }

        if (zone.IsAmbiguousTime(midnight))
        {
            return DateTime.SpecifyKind(midnight - zone.GetAmbiguousTimeOffsets(midnight).Max(), DateTimeKind.Utc);
        }

        return TimeZoneInfo.ConvertTimeToUtc(midnight, zone);
    }
}
