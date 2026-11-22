using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Configuration;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Contracts.Json;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Launches;

/// <summary>
/// The person's word that the daily limits of launches do not hold for the rest of the installation's day. Said once, it
/// covers every campaign and every role: what is held at a limit now is let go at once, and the claim holds nothing new
/// until the installation's next midnight, when the lift no longer covers the day it counts and nothing is deleted.
/// </summary>
public sealed class LaunchesService(JasonDbContext db, JournalWriter journal, TimeProvider clock, InstallationDay installation)
{
    /// <summary>
    /// Lifts today's limits, in one transaction: the row that says so, every open hold at a daily limit in every campaign
    /// released as a person's resume, each item back through the transition table, and one line. A second lift the same
    /// day writes no row and no line and answers what stands, and still lets go any hold that appeared since.
    /// </summary>
    public async Task<LaunchesLiftDto> LiftAsync(LaunchesLiftRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new ValidationErrors();
        WorkItemValidation.ValidateReason(request.Reason, errors);
        errors.ThrowIfAny();
        var actor = Person(request.Actor);
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();

        var now = clock.GetUtcNow().UtcDateTime;
        var day = installation.Of(now);

        // BEGIN IMMEDIATE, as the claim begins: from the read of the day's lift to the commit no scan runs, so no scan holds
        // an item after the holds were read and before the lift that would have let it go is committed.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var already = await LaunchLimitLifts.CoverAsync(db, day, cancellationToken).ConfigureAwait(false);
        var released = await ReleaseAsync(actor, reason, now, cancellationToken).ConfigureAwait(false);
        if (!already)
        {
            db.LaunchLimitLifts.Add(new LaunchLimitLift
            {
                LocalDate = day.Date,
                TimeZone = day.TimeZone,
                ActorType = actor.Type,
                ActorId = actor.Id,
                Reason = reason,
                CreatedAt = now,
            });
            journal.Append(
                db,
                actor,
                JournalKinds.LaunchLimitLifted,
                campaign: null,
                key: day.DateText,
                updated: new JsonObject
                {
                    ["until"] = JsonSerializer.SerializeToNode(WorkItemMapper.Utc(day.End), JasonJson.Options),
                    ["time_zone"] = day.TimeZone,
                    ["released"] = released,
                },
                reason: reason);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new LaunchesLiftDto(WorkItemMapper.Utc(day.End), day.TimeZone, released, already);
    }

    /// <summary>
    /// Lets go every item held at a daily limit, in every campaign: a wait with no attempt and no block, on a waiting item
    /// whose last error is the hold's, under either word. Each wait is released as a person's resume, through the one routine
    /// that releases a wait, so each item goes back to <c>created</c> through the transition table and says who let it go.
    /// The error is a converted column, so the word is read here rather than in the query.
    /// </summary>
    private async Task<int> ReleaseAsync(ActorRef actor, string? reason, DateTime now, CancellationToken cancellationToken)
    {
        var waits = await db.WorkItemWaits
            .Where(w => w.Status == WaitStatus.Open && w.AttemptId == null && w.ProviderBlockId == null
                && w.WorkItem!.Status == WorkItemStatus.Waiting)
            .OrderBy(w => w.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (waits.Count == 0)
        {
            return 0;
        }

        var ids = waits.Select(w => w.WorkItemId).ToList();
        var items = await db.WorkItems
            .WithNavigation()
            .Where(w => ids.Contains(w.Id))
            .ToDictionaryAsync(w => w.Id, cancellationToken)
            .ConfigureAwait(false);

        var released = 0;
        foreach (var wait in waits)
        {
            var item = items[wait.WorkItemId];
            if (!AttemptErrors.IsDailyHold(item.LastError?.Code))
            {
                continue;
            }

            OpenWaits.Release(db, journal, wait, item, WaitReleaseReason.ResumedByActor, actor, reason, now);
            released++;
        }

        return released;
    }

    /// <summary>
    /// Who lifts. An absent actor is an anonymous human, which is right for creating work and wrong for a lift: a lift names
    /// the person whose word it is, as a block's clear does, and a role, an attempt or the system is no person.
    /// </summary>
    private static ActorRef Person(ActorRef? claimed)
    {
        var actor = Actors.Resolve(claimed);
        if (actor.Type != ActorType.Human)
        {
            throw DomainErrors.LiftNotHuman(actor.Type);
        }

        return string.IsNullOrWhiteSpace(actor.Id) ? throw DomainErrors.LiftActorRequired() : actor;
    }
}
