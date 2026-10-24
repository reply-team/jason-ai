using System.Globalization;
using Jason.Contracts.Api;
using Jason.Runtime.Domain;

namespace Jason.Runtime.Campaigns;

/// <summary>
/// A window cut into the hours, days or weeks of a zone, each bucket clipped to the window, so no bucket counts anything
/// the window's own count does not. SQLite has no zone data, so the edges are cut here and the moments bucketed against
/// them.
/// </summary>
internal static class ActivityBuckets
{
    /// <summary>A month of hours: a series longer than that is refused, not answered with a page nobody can draw.</summary>
    public const int MaxBuckets = 744;

    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    /// <summary>What a zone's widest offset and a week's bucket need beyond a window to be cut: a week and a day.</summary>
    internal static readonly TimeSpan Edge = TimeSpan.FromDays(8);

    public static IReadOnlyList<(DateTime Start, DateTime End)> Cut(DateTime since, DateTime until, ActivityPeriod period, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (until <= since)
        {
            return [];
        }

        var errors = new ValidationErrors();
        if (since - DateTime.MinValue < Edge)
        {
            errors.Add("since", "invalid", "since is too close to the start of the calendar to be cut into a zone's hours, days or weeks.");
        }

        if (DateTime.MaxValue - until < Edge)
        {
            errors.Add("until", "invalid", "until is too close to the end of the calendar to be cut into a zone's hours, days or weeks.");
        }

        errors.ThrowIfAny();
        var count = Count(since, until, period, zone);
        if (count > MaxBuckets)
        {
            throw new ValidationException(
            [
                new ErrorDetail("period", "invalid", string.Create(CultureInfo.InvariantCulture, $"period cuts the window into {count} buckets; at most {MaxBuckets}.")),
            ]);
        }

        var buckets = new List<(DateTime Start, DateTime End)>(count);
        if (period == ActivityPeriod.Hour)
        {
            for (var start = HourOf(since, zone); start < until; start += Hour)
            {
                buckets.Add((Max(start, since), Min(start + Hour, until)));
            }

            return buckets;
        }

        var step = period == ActivityPeriod.Week ? 7 : 1;
        for (var day = FirstDay(since, period, zone); ; day = day.AddDays(step))
        {
            var start = Midnight(day, zone);
            if (start >= until)
            {
                return buckets;
            }

            buckets.Add((Max(start, since), Min(Midnight(day.AddDays(step), zone), until)));
        }
    }

    private static int Count(DateTime since, DateTime until, ActivityPeriod period, TimeZoneInfo zone)
    {
        if (period == ActivityPeriod.Hour)
        {
            return (int)Math.Ceiling((until - HourOf(since, zone)) / Hour);
        }

        var first = FirstDay(since, period, zone);
        var last = FirstDay(until.AddTicks(-1), period, zone);
        return (last.DayNumber - first.DayNumber) / (period == ActivityPeriod.Week ? 7 : 1) + 1;
    }

    /// <summary>The instant the zone's wall clock last read a whole hour at or before this one.</summary>
    private static DateTime HourOf(DateTime at, TimeZoneInfo zone)
    {
        var offset = zone.GetUtcOffset(at);
        var wall = at + offset;
        return new DateTime(wall.Year, wall.Month, wall.Day, wall.Hour, 0, 0, DateTimeKind.Utc) - offset;
    }

    /// <summary>The local date the bucket holding this instant begins on: its own day, or the Monday of its week.</summary>
    private static DateOnly FirstDay(DateTime at, ActivityPeriod period, TimeZoneInfo zone)
    {
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(at, zone));
        return period == ActivityPeriod.Week ? day.AddDays(-(((int)day.DayOfWeek + 6) % 7)) : day;
    }

    /// <summary>
    /// The instant a local date begins in the zone: its midnight, the earlier one where midnight repeats, or the change of
    /// clocks where midnight is skipped.
    /// </summary>
    private static DateTime Midnight(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(1);
        }

        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
}
