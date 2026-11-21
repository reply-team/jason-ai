using Jason.Runtime.Harness;

namespace Jason.Runtime.Tests.Harness;

/// <summary>
/// A campaign folder's leaf: when the campaign was made, in the local time of the computer the runtime runs on, to the
/// second, and the last six characters of its public id, lowercase. Never its name.
/// </summary>
public sealed class CampaignLeafTests
{
    private const string Id = "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD";

    private static readonly DateTime Made = new(2026, 11, 21, 8, 45, 7, 640, DateTimeKind.Utc);

    private static TimeZoneInfo Offset(int hours) =>
        TimeZoneInfo.CreateCustomTimeZone($"test{hours:+0;-0}", TimeSpan.FromHours(hours), $"test {hours:+0;-0}", $"test {hours:+0;-0}");

    [Fact]
    public void The_leaf_is_the_moment_to_the_second_and_the_id_s_end_lowercase()
    {
        Assert.Equal("2026-11-21-084507-7h5nrd", CampaignLeaf.Of(Id, Made, TimeZoneInfo.Utc));
    }

    [Fact]
    public void The_moment_is_the_computer_s_local_time_never_UTC()
    {
        Assert.Equal("2026-11-21-134507-7h5nrd", CampaignLeaf.Of(Id, Made, Offset(5)));
        Assert.Equal("2026-11-20-234507-7h5nrd", CampaignLeaf.Of(Id, Made, Offset(-9)));
    }

    [Fact]
    public void Every_part_is_zero_padded()
    {
        var early = new DateTime(2027, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        Assert.Equal("2027-01-02-030405-7h5nrd", CampaignLeaf.Of(Id, early, TimeZoneInfo.Utc));
    }

    /// <summary>The moment as the database gives it back, with no kind, is the same UTC moment, so it makes the same leaf in the same zone.</summary>
    [Fact]
    public void A_moment_read_back_with_no_kind_is_read_as_UTC()
    {
        var stored = DateTime.SpecifyKind(Made, DateTimeKind.Unspecified);

        Assert.Equal(CampaignLeaf.Of(Id, Made, Offset(5)), CampaignLeaf.Of(Id, stored, Offset(5)));
    }

    /// <summary>Across a change of the clocks, each moment is its own local time; a repeated hour gives the same time twice, told apart by the id.</summary>
    [Fact]
    public void A_zone_that_changes_its_clocks_is_read_at_each_moment()
    {
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2026, 1, 1),
            new DateTime(2026, 12, 31),
            TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 29),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 25));
        var zone = TimeZoneInfo.CreateCustomTimeZone("test-dst", TimeSpan.Zero, "test dst", "test standard", "test summer", [rule]);

        Assert.Equal("2026-07-01-130000-7h5nrd", CampaignLeaf.Of(Id, new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc), zone));
        Assert.Equal("2026-12-01-120000-7h5nrd", CampaignLeaf.Of(Id, new DateTime(2026, 12, 1, 12, 0, 0, DateTimeKind.Utc), zone));
        var before = CampaignLeaf.Of(Id, new DateTime(2026, 10, 25, 1, 30, 0, DateTimeKind.Utc), zone);
        var after = CampaignLeaf.Of("cmp_01JB6K8TQ2W9V4MZ0C3Y7OTHER", new DateTime(2026, 10, 25, 2, 30, 0, DateTimeKind.Utc), zone);
        Assert.Equal("2026-10-25-023000-7h5nrd", before);
        Assert.Equal("2026-10-25-023000-7other", after);
    }

    [Theory]
    [InlineData("2026-11-21-084507-7h5nrd", true)]
    [InlineData("2026-11-21-084507-7H5NRD", false)]
    [InlineData("2026-11-21-084507-7h5nrd-2", false)]
    [InlineData("q3-marketing-leads", false)]
    [InlineData("2026-11-21-0845-7h5nrd", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void A_leaf_s_shape_is_known(string? leaf, bool expected)
    {
        Assert.Equal(expected, CampaignLeaf.IsLeaf(leaf));
    }

    [Fact]
    public void Every_leaf_made_has_a_leaf_s_shape()
    {
        Assert.True(CampaignLeaf.IsLeaf(CampaignLeaf.Of(Id, Made, Offset(-12))));
        Assert.True(CampaignLeaf.IsLeaf(CampaignLeaf.Of("cmp_01K7QN000000000000000000Q3", DateTime.UnixEpoch, TimeZoneInfo.Utc)));
    }
}
