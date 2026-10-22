using Jason.Contracts.Api;
using Jason.Runtime.Domain;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Campaigns;

/// <summary>
/// <c>campaign.summary</c> and <c>campaign.activity</c>: each count is one of <see cref="CampaignPeople"/>'s definitions
/// summed per campaign, every count of every campaign asked in one read.
/// </summary>
public sealed class CampaignNumbers(JasonDbContext db)
{
    public const int MaxCampaigns = 100;

    public async Task<CampaignSummaryResponse> SummaryAsync(CampaignSummaryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var campaigns = await CampaignsAsync(request.CampaignIds, cancellationToken).ConfigureAwait(false);
        var ids = campaigns.Select(campaign => campaign.Id).ToList();
        var counts = await CampaignPeople.SummaryCountsAsync(db, ids, cancellationToken).ConfigureAwait(false);
        var pinned = await CampaignPeople.PinnedAsync(db, ids, cancellationToken).ConfigureAwait(false);
        return new CampaignSummaryResponse([.. campaigns.Select(campaign => counts.TryGetValue(campaign.Id, out var c)
            ? new CampaignSummaryRow(campaign.PublicId, c.People, c.Waiting, c.Failed, c.Working, c.New, c.Idle, c.Sequence, c.Out, c.Removed, pinned.Contains(campaign.Id))
            : new CampaignSummaryRow(campaign.PublicId, 0, 0, 0, 0, 0, 0, 0, 0, 0, pinned.Contains(campaign.Id)))]);
    }

    public async Task<CampaignActivityResponse> ActivityAsync(CampaignActivityRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (since, until) = Period(request.Since, request.Until);
        var campaigns = await CampaignsAsync(request.CampaignIds, cancellationToken).ConfigureAwait(false);
        var counts = await CampaignPeople.ActivityCountsAsync(db, [.. campaigns.Select(campaign => campaign.Id)], since, until, cancellationToken)
            .ConfigureAwait(false);
        return new CampaignActivityResponse([.. campaigns.Select(campaign => counts.TryGetValue(campaign.Id, out var c)
            ? new CampaignActivityRow(campaign.PublicId, c.People, c.Added, c.WorkedOn, c.Contacted, c.Replied, c.OptedOut, c.Removed)
            : new CampaignActivityRow(campaign.PublicId, 0, 0, 0, 0, 0, 0, 0))]);
    }

    /// <summary>Both ends required, and a period that ends before it starts is refused rather than read as empty.</summary>
    internal static (DateTime Since, DateTime Until) Period(DateTimeOffset? since, DateTimeOffset? until)
    {
        var errors = new ValidationErrors();
        if (since is null)
        {
            errors.Add("since", "required", "since is required.");
        }

        if (until is null)
        {
            errors.Add("until", "required", "until is required.");
        }

        if (since is { } from && until is { } to && to < from)
        {
            errors.Add("until", "invalid", "until must not be before since.");
        }

        errors.ThrowIfAny();
        return (since!.Value.UtcDateTime, until!.Value.UtcDateTime);
    }

    private async Task<List<(int Id, string PublicId)>> CampaignsAsync(IReadOnlyList<string>? requested, CancellationToken cancellationToken)
    {
        if (requested is null)
        {
            var shown = await db.Campaigns.AsNoTracking()
                .Where(campaign => campaign.Status != CampaignStatus.Archived)
                .OrderBy(campaign => campaign.PublicId)
                .Select(campaign => new { campaign.Id, campaign.PublicId })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            return [.. shown.Select(campaign => (campaign.Id, campaign.PublicId))];
        }

        if (requested.Count is 0 or > MaxCampaigns)
        {
            var errors = new ValidationErrors();
            errors.Add("campaign_ids", "invalid", $"campaign_ids names 1 to {MaxCampaigns} campaigns; got {requested.Count}.");
            errors.ThrowIfAny();
        }

        var asked = requested.Select(id => id?.Trim() ?? string.Empty).Distinct(StringComparer.Ordinal).ToList();
        var found = await db.Campaigns.AsNoTracking()
            .Where(campaign => asked.Contains(campaign.PublicId))
            .Select(campaign => new { campaign.Id, campaign.PublicId })
            .ToDictionaryAsync(campaign => campaign.PublicId, campaign => campaign.Id, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);
        var missing = asked.FirstOrDefault(id => !found.ContainsKey(id));
        if (missing is not null)
        {
            throw DomainErrors.CampaignNotFound(missing);
        }

        return [.. asked.Select(id => (found[id], id))];
    }
}
