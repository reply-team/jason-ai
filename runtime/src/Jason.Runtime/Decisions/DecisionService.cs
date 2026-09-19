using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Decisions;

/// <summary>
/// The four verbs a question travels through. A running role raises one and then ends its attempt; the
/// question outlives the attempt, which is the whole reason it is a row; and the answer comes from a person
/// and from nobody else.
/// </summary>
/// <remarks>
/// Nothing inside the runtime answers a decision. The dispatcher reads that one was answered — a kind and an
/// identifier — and creates the review that continues the work; the deciding is a request that arrives here
/// naming the person who made it.
/// </remarks>
public sealed class DecisionService(JasonDbContext db, JournalWriter journal, TimeProvider clock)
{
    public async Task<DecisionDto> RaiseAsync(DecisionRaiseRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var workItemId = Required(request.WorkItemId, "work_item_id");
        var attemptId = Required(request.AttemptId, "attempt_id");
        var question = Question(request.Question);
        Validate(request.Options, request.References);

        var now = clock.GetUtcNow().UtcDateTime;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // The same guarded statement workitem.set_result stands behind, so a role whose lease was lost is told
        // to stop rather than allowed to leave a question in somebody's queue.
        await ExecutorService.RequireAttemptAsync(db, workItemId, attemptId, now, cancellationToken).ConfigureAwait(false);

        var item = await db.WorkItems
            .Include(w => w.Campaign)
            .FirstAsync(w => w.PublicId == workItemId, cancellationToken)
            .ConfigureAwait(false);
        var attempt = await db.Attempts
            .FirstAsync(a => a.PublicId == attemptId, cancellationToken)
            .ConfigureAwait(false);

        await ResolveAsync(item.CampaignId, request.References, cancellationToken).ConfigureAwait(false);

        var decision = new Decision
        {
            PublicId = PublicId.New("dec"),
            CampaignId = item.CampaignId,
            WorkItemId = item.Id,
            AttemptId = attempt.Id,
            Question = question,
            Options = DecisionMapper.ToJson(request.Options),
            References = DecisionMapper.ToJson(request.References),
            Status = DecisionStatus.Pending,
            RaisedAt = now,
        };
        db.Decisions.Add(decision);

        // Identifiers, and the actor is the attempt because that is who asked. What was asked is in the row;
        // the chronicle is the one table nobody can edit afterwards, and a question is somebody's half-formed
        // thought about an account.
        journal.Append(
            db,
            new ActorRef(ActorType.Attempt, attempt.PublicId),
            JournalKinds.DecisionRaised,
            campaign: null,
            key: "decision",
            updated: new JsonObject { ["decision_id"] = decision.PublicId },
            reason: Reason(request.Reason),
            workItem: item,
            attempt: attempt);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return DecisionMapper.ToDto(decision, item.Campaign!.PublicId, item.PublicId, attempt.PublicId);
    }

    /// <summary>
    /// A person's answer. Nothing inside the runtime reaches this: the dispatcher reads that a decision was
    /// answered and creates the review that continues the work, and the only way one is answered is a request
    /// that arrives here naming the person who answered it.
    /// </summary>
    /// <remarks>
    /// What this cannot do is tell a person from a process holding that person's own command line — the same
    /// limit an approval has, and the operator's trust to give. The guarantee is that nothing inside the
    /// runtime can give it.
    /// </remarks>
    public async Task<DecisionDto> AnswerAsync(DecisionAnswerRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = Person(request.Actor);
        var answer = Answer(request.Answer);
        var reason = Reason(request.Reason);
        var decision = await LoadAsync(request.DecisionId, tracking: true, cancellationToken).ConfigureAwait(false);

        if (decision.Status != DecisionStatus.Pending)
        {
            throw DomainErrors.DecisionNotPending(decision.PublicId, decision.Status);
        }

        var chosen = Chosen(decision, request.Option);
        await ActorVerification.VerifyAsync(db, actor, cancellationToken).ConfigureAwait(false);
        var now = clock.GetUtcNow().UtcDateTime;

        decision.Status = DecisionStatus.Answered;
        decision.Answer = answer;
        decision.ChosenOption = chosen;
        decision.AnsweredAt = now;
        decision.AnsweredByType = actor.Type;
        decision.AnsweredById = actor.Id;

        // The line the continuation is read from. It names the campaign, the work and the attempt that asked —
        // so the review this summons inherits that attempt's chain — and carries the person as its actor, which
        // is what tells the summon that this is somebody acting on a question rather than the loop feeding
        // itself. What it carries for a reader is the identifier; what was answered is in the row.
        var entry = journal.Append(
            db,
            actor,
            JournalKinds.DecisionAnswered,
            campaign: null,
            key: "decision",
            updated: new JsonObject { ["decision_id"] = decision.PublicId, ["chosen_option"] = chosen },
            reason: reason,
            workItem: decision.WorkItem,
            attempt: decision.Attempt);

        // And the row remembers the line, which is how the summon names this decision without reading one.
        decision.AnswerJournalEntryId = entry.PublicId;

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The row in front of us says what this caller wanted rather than what happened, so the decision
            // that stands is only readable by asking the database again.
            db.ChangeTracker.Clear();
            throw DomainErrors.DecisionNotPending(
                decision.PublicId,
                await StandsAsync(decision.PublicId, cancellationToken).ConfigureAwait(false));
        }

        return Detail(decision);
    }

    public async Task<DecisionDto> GetAsync(DecisionGetRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var decision = await LoadAsync(request.DecisionId, tracking: false, cancellationToken).ConfigureAwait(false);
        return Detail(decision);
    }

    /// <summary>
    /// What is waiting, oldest question first. Pending by default, because the question somebody opens this
    /// with is "what is waiting on me?" — the other statuses are the history of questions already settled.
    /// </summary>
    public async Task<Page<DecisionSummaryDto>> ListAsync(DecisionListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var limit = Paging.ResolveLimit(request.Limit);
        var after = Paging.DecodeCursor(request.Cursor);

        var query = db.Decisions
            .AsNoTracking()
            .Include(d => d.Campaign)
            .Include(d => d.WorkItem)
            .Where(d => d.Status == (request.Status ?? DecisionStatus.Pending));

        if (request.CampaignId is not null)
        {
            var campaign = await CampaignService.LoadAsync(db, request.CampaignId, cancellationToken).ConfigureAwait(false);
            query = query.Where(d => d.CampaignId == campaign.Id);
        }

        if (request.WorkItemId is not null)
        {
            var item = await db.WorkItems.AsNoTracking()
                .FirstOrDefaultAsync(w => w.PublicId == request.WorkItemId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw DomainErrors.WorkItemNotFound(request.WorkItemId);
            query = query.Where(d => d.WorkItemId == item.Id);
        }

        if (after is not null)
        {
            query = query.Where(d => string.Compare(d.PublicId, after) > 0);
        }

        var fetched = await query.OrderBy(d => d.PublicId).Take(limit + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
        return Paging.ToPage(
            fetched,
            limit,
            d => d.PublicId,
            d => DecisionMapper.ToSummary(d, d.Campaign!.PublicId, d.WorkItem!.PublicId));
    }

    private static string Required(string? value, string field) =>
        string.IsNullOrWhiteSpace(value) ? throw DomainErrors.Required(field) : value.Trim();

    /// <summary>
    /// Who answered, which is always a person. An absent actor is an anonymous human, which is right for
    /// creating work and wrong for deciding it: what is recorded has to name somebody.
    /// </summary>
    private static ActorRef Person(ActorRef? claimed)
    {
        var actor = Actors.Resolve(claimed);
        if (actor.Type != ActorType.Human)
        {
            throw DomainErrors.DecisionNotHuman(actor.Type);
        }

        return string.IsNullOrWhiteSpace(actor.Id) ? throw DomainErrors.ActorRequired() : actor;
    }

    private static string Answer(string? answer)
    {
        var errors = new ValidationErrors();
        var trimmed = answer?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            errors.Add("answer", "required", "answer is required: what is recorded is what the role reads next time.");
        }
        else if (trimmed.Length > DecisionLimits.MaxAnswerLength)
        {
            errors.Add(
                "answer",
                "too_long",
                string.Create(CultureInfo.InvariantCulture, $"answer must be at most {DecisionLimits.MaxAnswerLength} characters."));
        }

        errors.ThrowIfAny();
        return trimmed!;
    }

    /// <summary>
    /// The named option somebody picked, where the asker named any. An option is optional — a question with
    /// choices can still be answered in words — but a label nobody offered is not an answer to this question.
    /// </summary>
    private static string? Chosen(Decision decision, string? option)
    {
        var chosen = option?.Trim();
        if (string.IsNullOrEmpty(chosen))
        {
            return null;
        }

        var offered = DecisionMapper.FromJson<DecisionOption>(decision.Options) ?? [];
        return offered.Any(named => string.Equals(named.Label, chosen, StringComparison.Ordinal))
            ? chosen
            : throw DomainErrors.DecisionOptionUnknown(chosen);
    }

    /// <summary>
    /// The decision as it stands now, read afresh. Nothing deletes a decision, so the only way this finds
    /// nothing is a database that lost it, and <c>pending</c> is then the honest answer: whatever refused the
    /// write, it was not an answer somebody had already given.
    /// </summary>
    private async Task<DecisionStatus> StandsAsync(string publicId, CancellationToken cancellationToken) =>
        await db.Decisions.AsNoTracking()
            .Where(d => d.PublicId == publicId)
            .Select(d => (DecisionStatus?)d.Status)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false) ?? DecisionStatus.Pending;

    private static string Question(string? question)
    {
        var errors = new ValidationErrors();
        var trimmed = question?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            errors.Add("question", "required", "question is required: a decision is the question somebody has to answer.");
        }
        else if (trimmed.Length > DecisionLimits.MaxQuestionLength)
        {
            errors.Add(
                "question",
                "too_long",
                string.Create(CultureInfo.InvariantCulture, $"question must be at most {DecisionLimits.MaxQuestionLength} characters."));
        }

        errors.ThrowIfAny();
        return trimmed!;
    }

    private static void Validate(IReadOnlyList<DecisionOption>? options, IReadOnlyList<DecisionReference>? references)
    {
        var errors = new ValidationErrors();
        if (options is not null)
        {
            if (options.Count > DecisionLimits.MaxOptions)
            {
                errors.Add(
                    "options",
                    "too_many",
                    string.Create(CultureInfo.InvariantCulture, $"at most {DecisionLimits.MaxOptions} options; a question with more is one nobody can answer at a glance."));
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < options.Count; index++)
            {
                var field = string.Create(CultureInfo.InvariantCulture, $"options[{index}]");
                var label = options[index].Label?.Trim();
                if (string.IsNullOrEmpty(label))
                {
                    errors.Add(field, "required", "an option needs a label: the label is what an answer records.");
                    continue;
                }

                if (label.Length > DecisionLimits.MaxOptionLabelLength)
                {
                    errors.Add(field, "too_long", string.Create(CultureInfo.InvariantCulture, $"an option label must be at most {DecisionLimits.MaxOptionLabelLength} characters."));
                }

                // The label is what a chosen answer records, so two the same would record an answer nobody
                // could read back.
                if (!seen.Add(label))
                {
                    errors.Add(field, "duplicate", $"two options are labelled '{label}'; an answer records the label, so it has to name one of them.");
                }

                if (options[index].Detail is { } detail && detail.Trim().Length > DecisionLimits.MaxOptionDetailLength)
                {
                    errors.Add(field, "too_long", string.Create(CultureInfo.InvariantCulture, $"an option's detail must be at most {DecisionLimits.MaxOptionDetailLength} characters."));
                }
            }
        }

        if (references is not null)
        {
            if (references.Count > DecisionLimits.MaxReferences)
            {
                errors.Add(
                    "references",
                    "too_many",
                    string.Create(CultureInfo.InvariantCulture, $"at most {DecisionLimits.MaxReferences} references; more than that is a database dump rather than a reading list."));
            }

            for (var index = 0; index < references.Count; index++)
            {
                var field = string.Create(CultureInfo.InvariantCulture, $"references[{index}]");
                if (references[index].Kind is null)
                {
                    errors.Add(
                        string.Create(CultureInfo.InvariantCulture, $"{field}.kind"),
                        "required",
                        "a reference says what it points at: work_item, attempt, journal_entry, report or approval.");
                }

                var id = references[index].Id?.Trim();
                if (string.IsNullOrEmpty(id))
                {
                    errors.Add(field, "required", "a reference names a row by its public id.");
                }
                else if (id.Length > DecisionLimits.MaxReferenceIdLength)
                {
                    errors.Add(field, "too_long", string.Create(CultureInfo.InvariantCulture, $"a public id is at most {DecisionLimits.MaxReferenceIdLength} characters, so this is not one."));
                }
            }
        }

        errors.ThrowIfAny();
    }

    /// <summary>
    /// Every reference points at one of this campaign's own rows, checked before anything is written. A
    /// reference is an identifier rather than a copy precisely so that whoever reads it later reads what is
    /// true then — and one that resolves to nothing never could.
    /// </summary>
    private async Task ResolveAsync(int campaignId, IReadOnlyList<DecisionReference>? references, CancellationToken cancellationToken)
    {
        if (references is null)
        {
            return;
        }

        foreach (var reference in references)
        {
            var id = reference.Id.Trim();

            // Validated before this ran: a reference with no kind never reaches a lookup.
            var resolved = reference.Kind!.Value switch
            {
                DecisionReferenceKind.WorkItem => await db.WorkItems.AsNoTracking()
                    .AnyAsync(w => w.PublicId == id && w.CampaignId == campaignId, cancellationToken).ConfigureAwait(false),
                DecisionReferenceKind.Attempt => await db.Attempts.AsNoTracking()
                    .AnyAsync(a => a.PublicId == id && a.WorkItem!.CampaignId == campaignId, cancellationToken).ConfigureAwait(false),
                DecisionReferenceKind.JournalEntry => await db.Journal.AsNoTracking()
                    .AnyAsync(e => e.PublicId == id && e.CampaignId == campaignId, cancellationToken).ConfigureAwait(false),
                DecisionReferenceKind.Report => await db.Reports.AsNoTracking()
                    .AnyAsync(r => r.PublicId == id && r.CampaignId == campaignId, cancellationToken).ConfigureAwait(false),
                DecisionReferenceKind.Approval => await db.Approvals.AsNoTracking()
                    .AnyAsync(a => a.PublicId == id && a.CampaignId == campaignId, cancellationToken).ConfigureAwait(false),
                _ => false,
            };

            if (!resolved)
            {
                throw DomainErrors.DecisionReferenceUnresolved(
                    SnakeCaseEnumConverter<DecisionReferenceKind>.Format(reference.Kind.Value),
                    id);
            }
        }
    }

    private static string? Reason(string? reason)
    {
        if (reason is not null && reason.Trim().Length > DecisionLimits.MaxReasonLength)
        {
            throw new ValidationException([new ErrorDetail("reason", "too_long", $"reason must be at most {DecisionLimits.MaxReasonLength} characters.")]);
        }

        return string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    private async Task<Decision> LoadAsync(string? id, bool tracking, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw DomainErrors.Required("decision_id");
        }

        var query = db.Decisions
            .Include(d => d.Campaign)
            .Include(d => d.WorkItem)
            .Include(d => d.Attempt)
            .AsQueryable();

        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(d => d.PublicId == id, cancellationToken).ConfigureAwait(false)
            ?? throw DomainErrors.DecisionNotFound(id);
    }

    private static DecisionDto Detail(Decision decision) =>
        DecisionMapper.ToDto(decision, decision.Campaign!.PublicId, decision.WorkItem!.PublicId, decision.Attempt!.PublicId);
}
