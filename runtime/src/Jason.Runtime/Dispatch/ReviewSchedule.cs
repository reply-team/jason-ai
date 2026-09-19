using Jason.Contracts.Api;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Dispatch;

/// <summary>
/// When a campaign is due for a review nothing in its chronicle asked for. Pure — the campaign, the installation's
/// default and the clock go in, a yes or a no comes out — so the cadence is one readable rule rather than a
/// comparison buried in the scan.
/// <para>
/// Only an active campaign is ever due: a draft is a backlog, a paused campaign was stopped on purpose, and an
/// archived one is over. And a campaign is measured from its anchor — its going live, and every check-in since —
/// so one that has never been live has no moment to measure from and is never due, rather than due at once.
/// </para>
/// </summary>
public static class ReviewSchedule
{
    /// <param name="defaultReviewSeconds">The installation's cadence, which the campaign's own outranks when it has one.</param>
    public static bool IsDue(Campaign campaign, int defaultReviewSeconds, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(campaign);

        if (campaign.Status != CampaignStatus.Active || campaign.ManagerReviewAnchor is not { } anchor)
        {
            return false;
        }

        var interval = campaign.ManagerReviewSeconds ?? defaultReviewSeconds;
        return anchor.AddSeconds(interval) <= now;
    }
}
