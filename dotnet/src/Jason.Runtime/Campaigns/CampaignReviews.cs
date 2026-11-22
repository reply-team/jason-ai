using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Campaigns;

/// <summary>
/// A person's word that a campaign's manager reviews it now (<c>campaign.review</c>). The check-in it makes is the summon's
/// own, created by <see cref="ManagerCheckIn.CreateAsync"/> as the dispatcher, so the one-open index, the summon's look, the
/// filter of a check-in's own lines and the refusal of a check-in's wait hold it as they hold one the summon made: its entry
/// is the depth's, <c>quick_look</c> or <c>deep_review</c>, its brief says <c>review_intent: requested</c>, and it is root
/// work. It moves the quick look's anchor, as every check-in does, and never the deep review's grid, so the daily deep
/// review still comes at its moment. The person's act is one <c>review_requested</c> line, which is no manager trigger.
/// </summary>
public sealed class CampaignReviews(
    JasonDbContext db,
    WorkItemService items,
    JournalWriter journal,
    TimeProvider clock,
    LiveSettings<ManagerOptions> manager,
    LiveSettings<LaunchesOptions> launches,
    InstallationDay installation)
{
    /// <summary>
    /// Makes the check-in, in one transaction with its line, or refuses, in this order once the request's fields are read:
    /// no named actor (<c>actor_required</c>), an actor that is no person (<c>review_not_human</c>), no such campaign, a
    /// campaign not active (<c>campaign_not_active</c>), a check-in open, a held one included (<c>review_already_open</c>,
    /// naming it), and the manager's daily launches spent (<c>launches_spent</c>). A request from inside a run was refused
    /// before it reached here (<see cref="Api.InsideARun"/>).
    /// </summary>
    public async Task<WorkItemDto> ReviewAsync(CampaignReviewRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new ValidationErrors();
        WorkItemValidation.ValidateReason(request.Reason, errors);
        if (request.Depth is not null && !ReviewDepths.All.Contains(request.Depth, StringComparer.Ordinal))
        {
            errors.Add("depth", "invalid", $"depth must be {string.Join(" or ", ReviewDepths.All)}; leave it out for a quick look.");
        }

        errors.ThrowIfAny();
        var actor = Person(request.Actor);
        var depth = request.Depth ?? ReviewDepths.Quick;
        var entry = depth == ReviewDepths.Deep ? ManagerEntryChoice.DeepReview : ManagerEntryChoice.QuickLook;
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        var options = manager.Current;
        var limits = launches.Current.MaxPerDay;

        // BEGIN IMMEDIATE, as the summon's own transaction begins: from the look at the open check-in to the commit no scan
        // writes, so the look is the answer, held ones included, which the one-open index does not see.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var campaign = await CampaignService.LoadAsync(db, request.CampaignId, cancellationToken).ConfigureAwait(false);
        if (campaign.Status != CampaignStatus.Active)
        {
            throw DomainErrors.CampaignNotActive(campaign.PublicId, campaign.Status);
        }

        if (await Summoner.OpenCheckInIdAsync(db, campaign.Id, cancellationToken).ConfigureAwait(false) is { } open)
        {
            throw DomainErrors.ReviewAlreadyOpen(campaign.PublicId, open);
        }

        var day = installation.Of(clock.GetUtcNow().UtcDateTime);
        if (await Claimer.SpentTodayAsync(db, campaign.Id, ManagerCheckIn.Role, limits, day, cancellationToken).ConfigureAwait(false))
        {
            throw DomainErrors.ReviewLaunchesSpent(campaign.PublicId, limits[ManagerCheckIn.Role], Zulu(day.Start), day.TimeZone);
        }

        WorkItem item;
        try
        {
            item = await ManagerCheckIn.CreateAsync(items, db, campaign, cause: null, options, cancellationToken, entry, requested: true).ConfigureAwait(false);
            journal.Append(
                db,
                actor,
                JournalKinds.ReviewRequested,
                campaign,
                key: depth,
                updated: new JsonObject { ["work_item_id"] = item.PublicId, ["entry"] = entry },
                reason: reason,
                workItem: item);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            db.ChangeTracker.Clear();

            // The database decides that one check-in is open, as it does for the summon: an insert refused while one is open is
            // the race lost, answered as the look would have answered it; refused while none is, it is something else.
            if (await Summoner.OpenCheckInIdAsync(db, campaign.Id, cancellationToken).ConfigureAwait(false) is { } won)
            {
                throw DomainErrors.ReviewAlreadyOpen(campaign.PublicId, won);
            }

            throw;
        }

        return WorkItemMapper.ToDto(item, item.CreatedAt, [], includeSnapshots: false);
    }

    /// <summary>
    /// Who asks. An absent actor is an anonymous human, which is right for creating work and wrong here: the line names the
    /// person whose word it is, as a lift's does, and a role, an attempt or the system is no person.
    /// </summary>
    private static ActorRef Person(ActorRef? claimed)
    {
        var actor = Actors.Resolve(claimed);
        if (actor.Type != ActorType.Human)
        {
            throw DomainErrors.ReviewNotHuman(actor.Type);
        }

        return string.IsNullOrWhiteSpace(actor.Id) ? throw DomainErrors.ReviewActorRequired() : actor;
    }

    private static string Zulu(DateTime moment) => moment.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
