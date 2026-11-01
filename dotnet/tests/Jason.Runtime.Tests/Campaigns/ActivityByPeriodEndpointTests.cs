using System.Net;
using Jason.Contracts.Api;

namespace Jason.Runtime.Tests.Campaigns;

/// <summary>
/// campaign.activity_by_period over CampaignWorld's week: each bucket is the count with the bucket as its period, people
/// once within it, so a person counted on two days stands in two buckets and once in the week.
/// </summary>
public class ActivityByPeriodEndpointTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateTimeOffset Instant(DateTime moment) => new(moment, TimeSpan.Zero);

    private static async Task<CampaignActivityByPeriodResponse> SeriesAsync(RuntimeApiFixture api, string count, string? zone = null, string[]? campaigns = null) =>
        await api.PostOkAsync<CampaignActivityByPeriodResponse>(
            Operations.CampaignActivityByPeriod,
            new { CampaignIds = campaigns, Count = count, Period = "day", Since = Instant(CampaignWorld.Since), Until = Instant(CampaignWorld.Until), Zone = zone },
            Ct);

    [Theory]
    [InlineData("people")]
    [InlineData("added")]
    [InlineData("worked_on")]
    [InlineData("contacted")]
    [InlineData("replied")]
    [InlineData("opted_out")]
    [InlineData("removed")]
    public async Task Each_bucket_is_the_count_with_the_bucket_as_its_period(string count)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));
        var world = await CampaignWorld.WriteAsync(api, Ct);
        string[] both = [world.Spring.PublicId, world.Autumn.PublicId];
        // Hal worked on in both campaigns on the same day: one person in that day's bar. Pia's research ends 0.4 ms before
        // the 15th: in the 14th's bar, as the count of the 14th has it. The SQLite this runtime ships reads that moment as
        // 23:59:59.999; one that rounded it to the next millisecond would put it in the 15th's.
        await CampaignWorld.WorkedOnAsync(api, world.People["Hal"], Ct, (world.Spring, 13), (world.Autumn, 13));
        await CampaignWorld.WorkedOnAsync(api, world.People["Pia"], Ct, (world.Spring, new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc).AddTicks(-4_000)));

        var series = await SeriesAsync(api, count, campaigns: both);

        foreach (var bucket in series.Buckets)
        {
            var activity = await api.PostOkAsync<CampaignActivityResponse>(
                Operations.CampaignActivity,
                new { CampaignIds = both, Since = Instant(bucket.Start.UtcDateTime), Until = Instant(bucket.End.UtcDateTime) },
                Ct);
            var total = activity.Total;
            int[] counts = [total.People, total.Added, total.WorkedOn, total.Contacted, total.Replied, total.OptedOut, total.Removed];
            string[] names = ["people", "added", "worked_on", "contacted", "replied", "opted_out", "removed"];
            Assert.Equal(counts[Array.IndexOf(names, count)], bucket.Value);
        }
    }

    [Fact]
    public async Task Buckets_are_the_days_of_the_zone_the_caller_names_and_of_UTC_when_none()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));
        await CampaignWorld.WriteAsync(api, Ct);

        // 11: Uma opted out. 12: Ada added, Ivy and Tess contacted, Rae replied. 13: Ben, Pia, Uma and Wes worked on,
        // Oli removed. 14: Cleo and Lou worked on, Quinn added back, Vic contacted, Oli replied. 15: Kit opted out.
        var series = await SeriesAsync(api, "people");
        Assert.Equal((ActivityPeriod.Day, "UTC"), (series.Period, series.Zone));
        Assert.Equal(
            [new DateTimeOffset(CampaignWorld.Since), new DateTimeOffset(CampaignWorld.Since.AddDays(7))],
            [series.Buckets[0].Start, series.Buckets[^1].Start]);
        Assert.Equal(new DateTimeOffset(CampaignWorld.Until), series.Buckets[^1].End);
        Assert.Equal([0, 0, 1, 4, 5, 5, 1, 0], series.Buckets.Select(bucket => bucket.Value));

        // A browser on a machine set to UTC names it so, or Etc/UTC; either is the same day as no zone at all, on every system.
        foreach (var name in new[] { "UTC", "Etc/UTC" })
        {
            var utc = await SeriesAsync(api, "people", zone: name);
            Assert.Equal(name, utc.Zone);
            Assert.Equal(series.Buckets, utc.Buckets);
        }

        // Fourteen hours ahead of UTC, what happened at ten in the morning UTC happened just after the next local midnight;
        // Ada's addition at nine, and the replies at eight, stay on their own local day.
        var ahead = await SeriesAsync(api, "people", zone: "Pacific/Kiritimati");
        Assert.Equal("Pacific/Kiritimati", ahead.Zone);
        Assert.Equal([0, 0, 0, 3, 2, 5, 4, 1], ahead.Buckets.Select(bucket => bucket.Value));
    }

    [Fact]
    public async Task A_count_and_a_period_are_required_and_a_zone_must_be_known()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));

        var error = await api.PostErrorAsync(
            Operations.CampaignActivityByPeriod,
            new { Since = Instant(CampaignWorld.Since), Until = Instant(CampaignWorld.Until), Zone = "Mars/Olympus" },
            HttpStatusCode.BadRequest,
            Ct);

        Assert.Equal("validation_failed", error.Code);
        Assert.Equal(
            [("count", "required"), ("period", "required"), ("zone", "invalid")],
            error.Details!.Select(detail => (detail.Field, detail.Code)));
    }
}
