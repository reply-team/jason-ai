using Jason.Contracts.Api;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// When a campaign is due for a review nothing in its chronicle asked for — the cadence, read as a function of
/// the campaign, the installation's default and the clock.
/// </summary>
public class ReviewScheduleTests
{
    private static readonly DateTime Noon = new(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc);

    private const int Hourly = 3600;

    /// <summary>
    /// A campaign that has never been live has no moment to measure from: nothing has been reviewed, nothing has
    /// happened since, and a review of a backlog would be a review of nothing.
    /// </summary>
    [Fact]
    public void A_campaign_that_has_never_been_live_is_never_due()
    {
        var campaign = Campaign(anchor: null);

        Assert.False(ReviewSchedule.IsDue(campaign, Hourly, Noon.AddYears(1)));
    }

    [Fact]
    public void An_active_campaign_is_due_once_the_interval_has_passed_since_its_anchor()
    {
        var campaign = Campaign(anchor: Noon);

        Assert.False(ReviewSchedule.IsDue(campaign, Hourly, Noon.AddMinutes(30)));
        Assert.True(ReviewSchedule.IsDue(campaign, Hourly, Noon.AddHours(2)));
    }

    /// <summary>A draft is a backlog, a paused campaign was stopped on purpose, and an archived one is over.</summary>
    [Theory]
    [InlineData(CampaignStatus.Draft)]
    [InlineData(CampaignStatus.Paused)]
    [InlineData(CampaignStatus.Archived)]
    public void Only_an_active_campaign_is_ever_due(CampaignStatus status)
    {
        var campaign = Campaign(anchor: Noon, status: status);

        Assert.False(ReviewSchedule.IsDue(campaign, Hourly, Noon.AddYears(1)));
    }

    /// <summary>
    /// Proved in both directions, because a rule that took the shorter of the two would pass the first half
    /// and is not the rule: the campaign's own cadence is what it says, longer or shorter than the house's.
    /// </summary>
    [Fact]
    public void The_campaigns_own_cadence_beats_the_default_in_either_direction()
    {
        var sooner = Campaign(anchor: Noon, reviewSeconds: 600);
        var later = Campaign(anchor: Noon, reviewSeconds: 7200);

        Assert.True(ReviewSchedule.IsDue(sooner, Hourly, Noon.AddMinutes(10)));
        Assert.False(ReviewSchedule.IsDue(later, Hourly, Noon.AddMinutes(90)));
    }

    [Fact]
    public void Exactly_at_the_interval_is_due_and_one_second_before_is_not()
    {
        var campaign = Campaign(anchor: Noon);

        Assert.False(ReviewSchedule.IsDue(campaign, Hourly, Noon.AddSeconds(Hourly - 1)));
        Assert.True(ReviewSchedule.IsDue(campaign, Hourly, Noon.AddSeconds(Hourly)));
    }

    private static Campaign Campaign(DateTime? anchor, int? reviewSeconds = null, CampaignStatus status = CampaignStatus.Active)
        => new()
        {
            PublicId = "cmp_A",
            Name = "a",
            Status = status,
            ManagerReviewAnchor = anchor,
            ManagerReviewSeconds = reviewSeconds,
            CreatedAt = Noon.AddDays(-1),
            UpdatedAt = Noon.AddDays(-1),
        };
}
