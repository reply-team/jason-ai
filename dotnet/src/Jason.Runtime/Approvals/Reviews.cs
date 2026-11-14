using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Api;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Approvals;

/// <summary>
/// The review that stands for a send's text as it is now: the newest doubting review under the item's current review
/// hash, or, where none doubts, the newest pass under it. Null where no review was recorded for this text, which includes
/// every review recorded before an edit of the input.
/// </summary>
/// <param name="Result">Whether it passes the send or doubts it.</param>
/// <param name="Code">The doubt's code; null for a pass.</param>
/// <param name="Line">The <c>workitem_reviewed</c> line that recorded it.</param>
/// <param name="By">Who reviewed: the reviewing attempt, or the named person.</param>
/// <param name="Role">The reviewing run's role; null for a person.</param>
public sealed record ReviewStanding(ReviewResult Result, string? Code, string Line, ActorRef By, string? Role);

/// <summary>
/// Reviews of a send: <c>workitem.review</c>, which records one as the reserved line <c>workitem_reviewed</c>, and the read
/// the claim makes of them. Who may review: a running attempt of the same campaign's role work, other than any attempt of
/// the work that created the send, or a named person who did not create it. Inside a run a review speaks as its run.
/// </summary>
/// <param name="run">The attempt the request says it comes from; null outside a request and for a request that names none.</param>
public sealed class ReviewService(JasonDbContext db, JournalWriter journal, RequestRun? run = null)
{
    public async Task<JournalEntryDto> ReviewAsync(WorkItemReviewRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new ValidationErrors();
        if (request.Reason is not null)
        {
            errors.Add("reason", "not_allowed", "workitem.review takes no reason: a reason is journaled, and a doubt's sentence never is; give it as note.");
        }

        if (string.IsNullOrWhiteSpace(request.WorkItemId))
        {
            errors.Add("work_item_id", "required", "work_item_id is required.");
        }

        if (string.IsNullOrWhiteSpace(request.ReviewHash))
        {
            errors.Add("review_hash", "required", "review_hash is required: the item's review_hash as workitem.get answered it.");
        }

        string? note = null;
        if (request.Result is null)
        {
            errors.Add("result", "required", "result is required: passed or doubts.");
        }
        else if (request.Result == ReviewResult.Passed)
        {
            if (request.Code is not null)
            {
                errors.Add("code", "not_allowed", "a review that passes carries no code.");
            }

            if (request.Note is not null)
            {
                errors.Add("note", "not_allowed", "a review that passes carries no note.");
            }
        }
        else
        {
            if (request.Code is null)
            {
                errors.Add("code", "required", $"a review that doubts carries its code: one of {string.Join(", ", DoubtCodes.All)}.");
            }
            else
            {
                Doubts.Code(request.Code, "code", errors);
            }

            note = Doubts.Note(request.Note, "note", errors);
        }

        ValidateActor(request.Actor, errors);
        errors.ThrowIfAny();

        var actor = Actors.Resolve(request.Actor);
        if (run?.AttemptId is { } header && !(actor.Type == ActorType.Attempt && string.Equals(actor.Id, header, StringComparison.Ordinal)))
        {
            throw new ValidationException([new ErrorDetail("actor", "invalid", "Inside a run a review speaks as its run: actor is the attempt the request comes from.")]);
        }

        await ActorVerification.VerifyAsync(db, actor, cancellationToken).ConfigureAwait(false);

        var item = await WorkItemService.LoadAsync(db, request.WorkItemId, cancellationToken).ConfigureAwait(false);
        if (!ReviewSubject.IsReviewable(item))
        {
            throw DomainErrors.NotReviewable(item.PublicId, "it is not a send a person decides each one of");
        }

        if (item.Status is not (WorkItemStatus.Created or WorkItemStatus.Waiting))
        {
            throw DomainErrors.NotReviewable(item.PublicId, $"it is {SnakeCaseEnumConverter<WorkItemStatus>.Format(item.Status)}, and a review is made while the send is still queued or waiting");
        }

        var hash = ReviewSubject.Hash(item);
        if (!string.Equals(hash, request.ReviewHash!.Trim(), StringComparison.Ordinal))
        {
            throw DomainErrors.ReviewStale(item.PublicId);
        }

        var (attempt, role) = await ReviewerAsync(item, actor, cancellationToken).ConfigureAwait(false);

        var entry = journal.Append(
            db,
            actor,
            JournalKinds.WorkItemReviewed,
            item.Campaign,
            key: hash,
            updated: new JsonObject
            {
                ["result"] = JsonSerializer.SerializeToNode(request.Result!.Value, JasonJson.Options),
                ["code"] = request.Result == ReviewResult.Doubts ? request.Code : null,
                ["operation"] = item.Operation,
                ["role"] = role,
            },
            workItem: item,
            attempt: attempt);

        // The newest doubt's sentence, at rest on the row until the park moves it onto the preview; a pass leaves the doubt
        // that stands, and its sentence, as they are.
        if (request.Result == ReviewResult.Doubts)
        {
            item.ReviewNote = note;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return JournalWriter.ToDto(entry, item.Campaign!.PublicId) with { ContactId = item.Contact?.PublicId };
    }

    /// <summary>A review is made by an attempt or by a person who names themselves; a role's name, a plugin or nobody is refused.</summary>
    private static void ValidateActor(ActorRef? actor, ValidationErrors errors)
    {
        if (actor is null)
        {
            errors.Add("actor", "required", "actor is required: a running attempt of the campaign's role work, or a person by name.");
            return;
        }

        if (actor.Type is not (ActorType.Attempt or ActorType.Human))
        {
            errors.Add("actor.type", "not_allowed", "a review is made by an attempt of the campaign's role work or by a person by name.");
        }
        else if (string.IsNullOrWhiteSpace(actor.Id))
        {
            errors.Add("actor.id", "required", actor.Type == ActorType.Human ? "a person reviews under their own name: actor.id names them." : "an attempt actor must carry its attempt id.");
        }
    }

    /// <summary>
    /// The reviewing attempt and its role, checked: running, of role work in the send's campaign, and not of the work that
    /// created the send; or, for a person, one who did not create it.
    /// </summary>
    private async Task<(Attempt? Attempt, string? Role)> ReviewerAsync(WorkItem item, ActorRef actor, CancellationToken cancellationToken)
    {
        if (actor.Type == ActorType.Human)
        {
            if (item.CreatedByType == ActorType.Human && string.Equals(item.CreatedById?.Trim(), actor.Id!.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw DomainErrors.ReviewByCreator(item.PublicId);
            }

            return (null, null);
        }

        var attempt = await db.Attempts.Include(a => a.WorkItem)
            .SingleAsync(a => a.PublicId == actor.Id, cancellationToken).ConfigureAwait(false);
        var work = attempt.WorkItem!;
        if (work.Kind != WorkItemKind.AiRole || work.CampaignId != item.CampaignId)
        {
            throw new ValidationException([new ErrorDetail("actor.id", "invalid", "actor.id must name an attempt of role work in the send's campaign.")]);
        }

        if (attempt.Status != AttemptStatus.Running)
        {
            throw new ValidationException([new ErrorDetail("actor.id", "invalid", "actor.id must name an attempt that is running: a review is made by a run in progress.")]);
        }

        if (item.CreatedByType == ActorType.Attempt)
        {
            var creator = item.CreatedById ?? string.Empty;
            var created = await db.Attempts.AsNoTracking()
                .Where(a => a.PublicId == creator)
                .Select(a => (int?)a.WorkItemId)
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (created == work.Id)
            {
                throw DomainErrors.ReviewByCreator(item.PublicId);
            }
        }

        return (attempt, work.Role);
    }
}

/// <summary>The read of the reviews of a send, which the claim makes.</summary>
public static class Reviews
{
    /// <summary>
    /// The review that stands for <paramref name="item"/>'s text as it is now: a doubt over any pass, the newest of each;
    /// null where none was recorded under its current review hash. Read from the send's own lines through the index of the
    /// journal by work item, so it is bounded by them.
    /// </summary>
    public static async Task<ReviewStanding?> StandingAsync(JasonDbContext db, WorkItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(item);
        var lines = await Lines(db, item.PublicId, ReviewSubject.Hash(item)).AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        var standing = lines.LastOrDefault(line => (string?)line.New?["result"] == Doubted) ?? lines.LastOrDefault();
        if (standing is null)
        {
            return null;
        }

        return new ReviewStanding(
            (string?)standing.New!["result"] == Doubted ? ReviewResult.Doubts : ReviewResult.Passed,
            (string?)standing.New["code"],
            standing.PublicId,
            new ActorRef(standing.ActorType, standing.ActorId),
            (string?)standing.New["role"]);
    }

    /// <summary>The reviews of one item under one hash, oldest first, through the index of the journal by work item.</summary>
    internal static IQueryable<JournalEntry> Lines(JasonDbContext db, string workItemId, string hash) =>
        db.Journal.FromSqlInterpolated($"""
            SELECT * FROM journal INDEXED BY ix_journal_work_item_id_public_id
            WHERE work_item_id = {workItemId} AND kind = 'workitem_reviewed' AND key = {hash}
            ORDER BY id
            """);

    private const string Doubted = "doubts";
}
