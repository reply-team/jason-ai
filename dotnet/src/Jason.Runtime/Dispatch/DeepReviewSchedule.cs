using Jason.Contracts.Api;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Dispatch;

/// <summary>
/// When a campaign is due for its deep review, and where its grid goes next. Pure — a moment, an interval and the clock go
/// in, a yes or a moment comes out — so the grid is one readable rule rather than arithmetic spread over the summon, the
/// check-in and the start.
/// <para>
/// The grid is a campaign's own column, <c>deep_review_due_at</c>: written at every going live to one interval after it,
/// and moved by a deep review, and by nothing else, by whole intervals to the first grid moment after now. So a runtime that
/// was down for three days runs one deep review rather than three, and the grid stays on the moments it started from.
/// </para>
/// </summary>
public static class DeepReviewSchedule
{
    /// <summary>Whether an active campaign's grid moment has come. A campaign that is not active, or has no grid, is never due.</summary>
    public static bool IsDue(Campaign campaign, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        return campaign.Status == CampaignStatus.Active && IsDue(campaign.DeepReviewDueAt, now);
    }

    /// <summary>The same rule from the one column it needs, for the summon, which reads columns and has asked whether the campaign is active.</summary>
    public static bool IsDue(DateTime? dueAt, DateTime now) => dueAt is { } at && at <= now;

    /// <summary>The grid's first moment for a campaign going live at <paramref name="liveAt"/>.</summary>
    public static DateTime AtGoingLive(DateTime liveAt, int intervalSeconds) => liveAt.AddSeconds(intervalSeconds);

    /// <summary>
    /// Where a deep review created at <paramref name="now"/> moves the grid: on from <paramref name="dueAt"/> by the fewest
    /// whole intervals that put it after now. A grid still ahead of now stays where it is, and a campaign with none is given
    /// one an interval from now.
    /// </summary>
    public static DateTime Next(DateTime? dueAt, int intervalSeconds, DateTime now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(intervalSeconds);
        if (dueAt is not { } at)
        {
            return now.AddSeconds(intervalSeconds);
        }

        if (at > now)
        {
            return at;
        }

        var interval = TimeSpan.FromSeconds(intervalSeconds);
        var passed = (now - at).Ticks / interval.Ticks;
        return at + (interval * (passed + 1));
    }
}
