using Jason.Contracts.Api;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins;

namespace Jason.Runtime.Campaigns;

/// <summary>
/// The stored campaign in the two shapes the API answers with: a listing stays small, everything else carries
/// the context. The internal key is not part of either.
/// </summary>
public static class CampaignMapper
{
    public static CampaignDto ToDto(Campaign campaign)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        return new CampaignDto(
            campaign.PublicId,
            campaign.Name,
            campaign.Status,
            campaign.Context,
            ExternalIdStore.ToDtos(campaign.ExternalIds),
            Utc(campaign.CreatedAt),
            Utc(campaign.UpdatedAt),
            campaign.ArchivedAt is { } archivedAt ? Utc(archivedAt) : null);
    }

    public static CampaignSummaryDto ToSummary(Campaign campaign)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        return new CampaignSummaryDto(
            campaign.PublicId,
            campaign.Name,
            campaign.Status,
            Utc(campaign.CreatedAt),
            Utc(campaign.UpdatedAt),
            campaign.ArchivedAt is { } archivedAt ? Utc(archivedAt) : null);
    }

    /// <summary>Everything in the database is UTC; SQLite hands the kind back unset.</summary>
    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
