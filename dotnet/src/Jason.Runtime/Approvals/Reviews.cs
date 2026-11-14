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
/// <remarks>
/// The invariant: a review is refused exactly where the claim would not read it. The claim reads a send's reviews
/// whenever it decides about the send, and runs it without reading them only under a live approved row whose subject is
/// the send as it now stands (<see cref="ApprovalGate.Authorizes"/>), so a review is refused there, asked of the claim
/// itself (<see cref="IClaimReading.RunsUnderAsync"/>), and accepted everywhere else: after a park and an edit, after an expiry
/// and a reopen, after an approval whose text was edited since, after an attempt that failed before any decision.
/// </remarks>
/// <param name="claim">The claim whose question a review asks; required, so no composition reviews without the rule.</param>
/// <param name="run">The attempt the request says it comes from; null outside a request and for a request that names none.</param>
public sealed class ReviewService(JasonDbContext db, JournalWriter journal, IClaimReading claim, RequestRun? run = null)
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
            throw DomainErrors.NotReviewable(item.PublicId, "it is not a send to a person");
        }

        if (item.Status is not (WorkItemStatus.Created or WorkItemStatus.Waiting))
        {
            throw DomainErrors.NotReviewable(item.PublicId, $"it is {SnakeCaseEnumConverter<WorkItemStatus>.Format(item.Status)}, and a review is made while the send is still queued or waiting");
        }

        await HeardByTheClaimAsync(item, cancellationToken).ConfigureAwait(false);

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

        // The row is written on every review, whatever it changes, so the save is guarded by the status the review read: a
        // claim that parked or took the send meanwhile wins, and the review, with its line, is refused rather than landing
        // after the person was asked. A claim that came and went meanwhile — released, run, failed and queued again — leaves
        // the status as it was read, so the claim's question is asked once more inside the save's own transaction, after
        // its first write, when no other writer can add an approval or an attempt before the commit.
        db.Entry(item).Property(w => w.Status).IsModified = true;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw DomainErrors.NotReviewable(item.PublicId, "it changed while the review was made; read it again with workitem.get, and while it is still queued or waiting it may be reviewed again");
        }

        await HeardByTheClaimAsync(item, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return JournalWriter.ToDto(entry, item.Campaign!.PublicId) with { ContactId = item.Contact?.PublicId };
    }

    /// <summary>
    /// Refuses a review the claim would never read: where the claim would run the send now under an approval that stands
    /// for it as it would now go, it runs under that approval and no review is heard. Where the claim cannot say, because
    /// the send cannot be claimed right now while an approved row is live, the review is refused too, failing closed.
    /// Named by the approval's id alone.
    /// </summary>
    private async Task HeardByTheClaimAsync(WorkItem item, CancellationToken cancellationToken)
    {
        var reading = await claim.RunsUnderAsync(db, item, cancellationToken).ConfigureAwait(false);
        switch (reading.Hearing)
        {
            case ReviewHearing.NotRead:
                throw DomainErrors.NotReviewable(item.PublicId, $"approval '{reading.Approval!.PublicId}' stands for the send as it would now go, and the claim would run it under that approval without reading a review");
            case ReviewHearing.Unjudged:
                throw DomainErrors.NotReviewable(item.PublicId, $"approval '{reading.Approval!.PublicId}' is live and the send cannot be claimed right now, so whether the claim would read a review cannot be told yet; once the send can be claimed it may be reviewed again, and is refused then too while that approval stands for the send as it would go");
        }
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
    /// created the send or wrote its input since; or, for a person, one who did neither.
    /// </summary>
    private async Task<(Attempt? Attempt, string? Role)> ReviewerAsync(WorkItem item, ActorRef actor, CancellationToken cancellationToken)
    {
        // Whoever wrote the input since the send was made is one of its authors, as its creator is.
        var editors = (await Reviews.InputWrites(db, item.PublicId).AsNoTracking()
                .Select(line => new { line.ActorType, line.ActorId })
                .ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(line => new ActorRef(line.ActorType, line.ActorId))
            .Distinct()
            .ToList();
        if (actor.Type == ActorType.Human)
        {
            if (item.CreatedByType == ActorType.Human && string.Equals(item.CreatedById?.Trim(), actor.Id!.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw DomainErrors.ReviewByCreator(item.PublicId);
            }

            if (editors.Any(editor => editor.Type == ActorType.Human && string.Equals(editor.Id?.Trim(), actor.Id!.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                throw DomainErrors.ReviewByEditor(item.PublicId);
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

        var editing = editors.Where(editor => editor.Type == ActorType.Attempt && editor.Id is not null).Select(editor => editor.Id!).ToList();
        if (editing.Count > 0
            && await db.Attempts.AsNoTracking().AnyAsync(a => editing.Contains(a.PublicId) && a.WorkItemId == work.Id, cancellationToken).ConfigureAwait(false))
        {
            throw DomainErrors.ReviewByEditor(item.PublicId);
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

    /// <summary>
    /// The edits of one item's input since it was made — its <c>workitem_context_updated</c> lines keyed by the input —
    /// through the index of the journal by work item, so the read is bounded by the item's own lines.
    /// </summary>
    internal static IQueryable<JournalEntry> InputWrites(JasonDbContext db, string workItemId) =>
        db.Journal.FromSqlInterpolated($"""
            SELECT * FROM journal INDEXED BY ix_journal_work_item_id_public_id
            WHERE work_item_id = {workItemId} AND kind = 'workitem_context_updated' AND key = {WorkItemService.InputKey}
            """);

    private const string Doubted = "doubts";
}
