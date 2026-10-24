using Jason.Contracts.Api;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Domain;

namespace Jason.Runtime.Tests.Campaigns;

/// <summary>How a window is cut into the hours, days or weeks of a zone, as campaign.activity_by_period counts them.</summary>
public class ActivityBucketsTests
{
    private static readonly TimeZoneInfo Kyiv = TimeZoneInfo.FindSystemTimeZoneById("Europe/Kyiv");

    private static DateTime Utc(int month, int day, int hour) => new(2026, month, day, hour, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Days_are_cut_in_the_zone_and_clipped_to_the_window()
    {
        // Kyiv falls back from +03 to +02 at 01:00 UTC on 25 October 2026: that day is 25 hours long.
        var buckets = ActivityBuckets.Cut(Utc(10, 23, 21), Utc(10, 26, 6), ActivityPeriod.Day, Kyiv);

        Assert.Equal(
            [(Utc(10, 23, 21), Utc(10, 24, 21)), (Utc(10, 24, 21), Utc(10, 25, 22)), (Utc(10, 25, 22), Utc(10, 26, 6))],
            buckets);
    }

    [Fact]
    public void Hours_across_the_fall_back_are_each_present_once()
    {
        var buckets = ActivityBuckets.Cut(Utc(10, 24, 21), Utc(10, 25, 22), ActivityPeriod.Hour, Kyiv);

        Assert.Equal(25, buckets.Count);
        Assert.All(buckets, bucket => Assert.Equal(TimeSpan.FromHours(1), bucket.End - bucket.Start));
        Assert.Equal(buckets.Select(bucket => bucket.Start).Distinct().Count(), buckets.Count);
    }

    [Fact]
    public void A_day_whose_midnight_is_skipped_begins_at_the_change_and_one_whose_midnight_repeats_at_the_first()
    {
        // Santiago goes from -04 to -03 at its midnight on 6 September 2026: that day begins at 01:00, 04:00 UTC.
        var santiago = TimeZoneInfo.FindSystemTimeZoneById("America/Santiago");

        var buckets = ActivityBuckets.Cut(Utc(9, 5, 4), Utc(9, 7, 3), ActivityPeriod.Day, santiago);

        Assert.Equal([(Utc(9, 5, 4), Utc(9, 6, 4)), (Utc(9, 6, 4), Utc(9, 7, 3))], buckets);

        // Havana goes back from 01:00 -04 to 00:00 -05 on 1 November 2026: its midnight comes at 04:00 and again at 05:00
        // UTC, and the day begins at the first, so it is 25 hours long.
        var havana = TimeZoneInfo.FindSystemTimeZoneById("America/Havana");
        Assert.Equal(
            [(Utc(10, 31, 4), Utc(11, 1, 4)), (Utc(11, 1, 4), Utc(11, 2, 5))],
            ActivityBuckets.Cut(Utc(10, 31, 4), Utc(11, 2, 5), ActivityPeriod.Day, havana));
    }

    [Fact]
    public void Weeks_start_on_monday_and_the_first_is_clipped_to_since()
    {
        // 18 October 2026 is a Sunday.
        var buckets = ActivityBuckets.Cut(Utc(10, 18, 0), Utc(10, 27, 0), ActivityPeriod.Week, TimeZoneInfo.Utc);

        Assert.Equal([(Utc(10, 18, 0), Utc(10, 19, 0)), (Utc(10, 19, 0), Utc(10, 26, 0)), (Utc(10, 26, 0), Utc(10, 27, 0))], buckets);
    }

    [Fact]
    public void A_window_that_ends_on_a_boundary_has_no_empty_bucket_after_it()
    {
        var buckets = ActivityBuckets.Cut(Utc(10, 10, 0), Utc(10, 12, 0), ActivityPeriod.Day, TimeZoneInfo.Utc);

        Assert.Equal([(Utc(10, 10, 0), Utc(10, 11, 0)), (Utc(10, 11, 0), Utc(10, 12, 0))], buckets);
    }

    [Fact]
    public void More_than_744_buckets_or_a_window_at_the_calendar_s_edge_is_refused()
    {
        var refused = Assert.Throws<ValidationException>(() => ActivityBuckets.Cut(Utc(10, 1, 0), Utc(11, 2, 0), ActivityPeriod.Hour, TimeZoneInfo.Utc));

        var detail = Assert.Single(refused.Details!);
        Assert.Equal(("period", "invalid", "period cuts the window into 768 buckets; at most 744."), (detail.Field, detail.Code, detail.Message));

        // Behind UTC, the calendar's first hour begins before the calendar does; in its last days, a week ends after it.
        var newYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var early = Assert.Throws<ValidationException>(() => ActivityBuckets.Cut(DateTime.MinValue, DateTime.MinValue.AddHours(5), ActivityPeriod.Hour, newYork));
        Assert.Equal([("since", "invalid")], early.Details!.Select(fault => (fault.Field, fault.Code)));
        var late = Assert.Throws<ValidationException>(() => ActivityBuckets.Cut(DateTime.MaxValue.AddDays(-3), DateTime.MaxValue.AddDays(-1), ActivityPeriod.Week, TimeZoneInfo.Utc));
        Assert.Equal([("until", "invalid")], late.Details!.Select(fault => (fault.Field, fault.Code)));
    }
}
