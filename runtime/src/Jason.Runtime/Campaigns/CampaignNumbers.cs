using Jason.Contracts.Api;
using Jason.Runtime.Contacts;
using Jason.Runtime.Domain;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Campaigns;

/// <summary>
/// <c>campaign.summary</c> and <c>campaign.activity</c>: each count is one of <see cref="CampaignPeople"/>'s definitions
/// summed per campaign, every count of every campaign asked in one read, and the same counts across them, a person once.
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
        var t = await CampaignPeople.SummaryTotalAsync(db, ids, cancellationToken).ConfigureAwait(false);
        return new CampaignSummaryResponse(
            [.. campaigns.Select(campaign => counts.TryGetValue(campaign.Id, out var c)
                ? new CampaignSummaryRow(campaign.PublicId, c.People, c.Waiting, c.Failed, c.Working, c.New, c.Idle, c.Sequence, c.Out, c.Removed, pinned.Contains(campaign.Id))
                : new CampaignSummaryRow(campaign.PublicId, 0, 0, 0, 0, 0, 0, 0, 0, 0, pinned.Contains(campaign.Id)))],
            new CampaignSummaryTotal(t.People, t.Waiting, t.Failed, t.Working, t.New, t.Idle, t.Sequence, t.Out, t.Removed));
    }

    public async Task<CampaignActivityResponse> ActivityAsync(CampaignActivityRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (since, until) = Period(request.Since, request.Until);
        var campaigns = await CampaignsAsync(request.CampaignIds, cancellationToken).ConfigureAwait(false);
        var ids = campaigns.Select(campaign => campaign.Id).ToList();
        var counts = await CampaignPeople.ActivityCountsAsync(db, ids, since, until, cancellationToken).ConfigureAwait(false);
        var t = await CampaignPeople.ActivityTotalAsync(db, ids, since, until, cancellationToken).ConfigureAwait(false);
        return new CampaignActivityResponse(
            [.. campaigns.Select(campaign => counts.TryGetValue(campaign.Id, out var c)
                ? new CampaignActivityRow(campaign.PublicId, c.People, c.Added, c.WorkedOn, c.Contacted, c.Replied, c.OptedOut, c.Removed)
                : new CampaignActivityRow(campaign.PublicId, 0, 0, 0, 0, 0, 0, 0))],
            new CampaignActivityTotal(t.People, t.Added, t.WorkedOn, t.Contacted, t.Replied, t.OptedOut, t.Removed));
    }

    /// <summary>
    /// One count across the campaigns asked, a bucket at a time: the window cut into the zone's hours, days or weeks, and
    /// every happening of the count in the window read once and put in its bucket, a person once per bucket.
    /// </summary>
    public async Task<CampaignActivityByPeriodResponse> ActivityByPeriodAsync(CampaignActivityByPeriodRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new ValidationErrors();
        if (request.Count is null)
        {
            errors.Add("count", "required", "count is required.");
        }

        if (request.Period is null)
        {
            errors.Add("period", "required", "period is required.");
        }

        var zoneId = TimeZoneRules.Normalize(request.Zone, "zone", errors);
        var (since, until) = Period(request.Since, request.Until, errors);
        errors.ThrowIfAny();

        var zone = TimeZoneRules.Find(zoneId);
        var buckets = ActivityBuckets.Cut(since, until, request.Period!.Value, zone);
        var campaigns = await CampaignsAsync(request.CampaignIds, cancellationToken).ConfigureAwait(false);
        var moments = await CampaignPeople.MomentsAsync(db, [.. campaigns.Select(campaign => campaign.Id)], request.Count!.Value, since, until, cancellationToken)
            .ConfigureAwait(false);

        var starts = buckets.Select(bucket => Milliseconds(bucket.Start)).ToArray();
        var people = buckets.Select(_ => new HashSet<int>()).ToArray();
        foreach (var moment in moments)
        {
            var at = Array.BinarySearch(starts, moment.Ms);
            var index = at >= 0 ? at : ~at - 1;
            if (index >= 0 && moment.Ms < Milliseconds(buckets[index].End))
            {
                people[index].Add(moment.ContactId);
            }
        }

        return new CampaignActivityByPeriodResponse(
            request.Period.Value,
            zoneId ?? "UTC",
            [.. buckets.Select((bucket, index) => new ActivityBucket(ContactMapper.ToOffset(bucket.Start), ContactMapper.ToOffset(bucket.End), people[index].Count))]);
    }

    private static long Milliseconds(DateTime utc) => new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeMilliseconds();

    /// <summary>Both ends required, and a period that ends before it starts is refused rather than read as empty.</summary>
    internal static (DateTime Since, DateTime Until) Period(DateTimeOffset? since, DateTimeOffset? until)
    {
        var errors = new ValidationErrors();
        var period = Period(since, until, errors);
        errors.ThrowIfAny();
        return period;
    }

    /// <summary>The same rules, their problems added to the request's others.</summary>
    private static (DateTime Since, DateTime Until) Period(DateTimeOffset? since, DateTimeOffset? until, ValidationErrors errors)
    {
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

        return (since?.UtcDateTime ?? default, until?.UtcDateTime ?? default);
    }

    private Task<List<(int Id, string PublicId)>> CampaignsAsync(IReadOnlyList<string>? requested, CancellationToken cancellationToken) =>
        ResolveAsync(db, requested, cancellationToken);

    /// <summary>The campaigns a request names, in the order it names them; absent, every campaign campaign.list shows, in its order.</summary>
    internal static async Task<List<(int Id, string PublicId)>> ResolveAsync(JasonDbContext db, IReadOnlyList<string>? requested, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
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
