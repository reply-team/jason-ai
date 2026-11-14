using Jason.Contracts.Api;
using Jason.Runtime.Approvals;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins;

namespace Jason.Runtime.Campaigns;

/// <summary>
/// The stored campaign in the two shapes the API answers with: a listing stays small, everything else carries
/// the context. The internal key is not part of either. Both name the campaign's project, which is read with the campaign:
/// a campaign answered without its project loaded is a read that forgot it, and fails here rather than answering without.
/// </summary>
public static class CampaignMapper
{
    /// <summary>
    /// One campaign whole: its own send modes as the row holds them, and the modes in force for it, the campaign's own
    /// over the installation's preferences in <paramref name="installation"/>.
    /// </summary>
    public static CampaignDto ToDto(Campaign campaign, InstallationModes installation)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        var project = ProjectOf(campaign);
        return new CampaignDto(
            campaign.PublicId,
            campaign.Name,
            campaign.Status,
            project.PublicId,
            project.Slug,
            campaign.Context,
            ExternalIdStore.ToDtos(campaign.ExternalIds),
            campaign.ExecutionProfile,
            campaign.ManagerReviewSeconds,
            Utc(campaign.CreatedAt),
            Utc(campaign.UpdatedAt),
            campaign.ArchivedAt is { } archivedAt ? Utc(archivedAt) : null,
            campaign.IdempotencyKey,
            campaign.FolderPath,
            campaign.AudienceThreshold,
            new SendModesDto(campaign.SendModeCold, campaign.SendModeWarm),
            SendModes.InForce(campaign, installation));
    }

    public static CampaignSummaryDto ToSummary(Campaign campaign)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        var project = ProjectOf(campaign);
        return new CampaignSummaryDto(
            campaign.PublicId,
            campaign.Name,
            campaign.Status,
            project.PublicId,
            project.Slug,
            Utc(campaign.CreatedAt),
            Utc(campaign.UpdatedAt),
            campaign.ArchivedAt is { } archivedAt ? Utc(archivedAt) : null,
            campaign.FolderPath);
    }

    private static Project ProjectOf(Campaign campaign) =>
        campaign.Project ?? throw new InvalidOperationException($"Campaign '{campaign.PublicId}' was read without its project.");

    /// <summary>Everything in the database is UTC; SQLite hands the kind back unset.</summary>
    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
