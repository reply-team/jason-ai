using Jason.Contracts.Api;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Launches;

/// <summary>
/// The daily limits of launches as they stand now (<c>launches.list</c>): the installation's day, the lift that stands, each
/// role's limit and what waits on one. Every fact is read through the function the claim reads it by — the day through
/// <see cref="InstallationDay"/>, the limits through the seam the dispatcher reads each scan, so a live edit of the settings
/// shows at the next read, the runs through the count the claim holds a role by, and the holds through
/// <see cref="LaunchHolds"/> — so a read never disagrees with the claim about the same moment. A read: it changes nothing.
/// </summary>
public sealed class LaunchLimitsReader(JasonDbContext db, TimeProvider clock, InstallationDay installation, LiveSettings<LaunchesOptions> launches)
{
    /// <summary>The limits Jason ships, which a number is told apart from as the settings' own.</summary>
    private static readonly IReadOnlyDictionary<string, int> Shipped = new LaunchesOptions().MaxPerDay;

    public async Task<LaunchesListDto> ListAsync(LaunchesListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var campaign = request.CampaignId is null
            ? null
            : await CampaignService.LoadAsync(db, request.CampaignId, cancellationToken).ConfigureAwait(false);

        var limits = launches.Current.MaxPerDay;
        var day = installation.Of(clock.GetUtcNow().UtcDateTime);
        var lift = await LaunchLimitLifts.OfAsync(db, day, cancellationToken).ConfigureAwait(false);

        // The roles the installation has, in the order role.list answers them: the shipped roster first, then the rest as added.
        var names = await db.Roles.AsNoTracking().OrderBy(r => r.PublicId).Select(r => r.Name).ToListAsync(cancellationToken).ConfigureAwait(false);
        var roles = new List<LaunchLimitDto>(names.Count);
        foreach (var name in names)
        {
            int? limit = limits.TryGetValue(name, out var set) ? set : null;
            var source = Shipped.TryGetValue(name, out var shipped) ? (limit == shipped ? LaunchLimitSource.Default : LaunchLimitSource.Settings)
                : limit is null ? LaunchLimitSource.Default : LaunchLimitSource.Settings;
            int? started = campaign is null
                ? null
                : await Claimer.StartedSinceAsync(db, campaign.Id, name, day.Start, cancellationToken).ConfigureAwait(false);
            roles.Add(new LaunchLimitDto(name, limit, source, started));
        }

        var held = await LaunchHolds.CountAsync(db, campaign?.Id, cancellationToken).ConfigureAwait(false);
        return new LaunchesListDto(
            new LaunchesDayDto(WorkItemMapper.Utc(day.Start), WorkItemMapper.Utc(day.End), day.TimeZone),
            lift is null
                ? null
                : new LaunchesStandingLiftDto(WorkItemMapper.Utc(day.End), new ActorRef(lift.ActorType, lift.ActorId), WorkItemMapper.Utc(lift.CreatedAt)),
            roles,
            held);
    }
}
