using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Configuration;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;

namespace Jason.Runtime.Execution;

/// <summary>
/// The one place an attempt ends and its work item moves on. The dispatcher, the executor operations and the
/// canceller all come through here, so "what happens after an attempt" is decided once and journaled the same
/// way every time. Nothing here saves: the caller commits the outcome together with whatever else it changed.
/// </summary>
public sealed class AttemptOutcomes(JournalWriter journal, TimeProvider clock, LiveSettings<DispatcherOptions> settings)
{
    /// <summary>The attempt and its item both succeed; a result, when given, replaces whatever the item carried.</summary>
    public void Succeed(JasonDbContext db, WorkItem item, Attempt attempt, JsonNode? result, ActorRef actor, string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(attempt);
        var now = clock.GetUtcNow().UtcDateTime;

        attempt.Status = AttemptStatus.Succeeded;
        attempt.FinishedAt = now;
        WorkItemTransitions.Apply(item, WorkItemStatus.Succeeded, now);
        if (result is not null)
        {
            item.Result = result.DeepClone();
        }

        journal.Append(
            db,
            actor,
            JournalKinds.WorkItemSucceeded,
            campaign: null,
            key: "result_present",
            updated: JsonValue.Create(item.Result is not null),
            reason: reason,
            workItem: item,
            attempt: attempt);
    }

    /// <summary>
    /// The attempt fails. Whether the item follows it depends on one question — is the failure worth another
    /// attempt, and are there attempts left — and the answer is the item's new status, which is returned.
    /// </summary>
    /// <param name="failureClass">
    /// What kind of failure this was, where something could say: a plugin answering for a provider knows things
    /// the code table cannot. Null for everything the runtime classifies itself, and the error carries no class.
    /// </param>
    /// <param name="retriable">
    /// A verdict from the same source, overriding <see cref="FailureClassifier"/> for this one failure. Null
    /// leaves the decision exactly where it was: the code decides, as it does for every agent attempt.
    /// </param>
    /// <param name="block">
    /// What a provider block says, kept on the error where a person reads it; only a blocked failure whose block the
    /// published contract accepts carries one.
    /// </param>
    public WorkItemStatus Fail(
        JasonDbContext db,
        WorkItem item,
        Attempt attempt,
        string code,
        string message,
        string? trace,
        IReadOnlyList<ErrorDetail>? details,
        ActorRef actor,
        string? reason = null,
        FailureClass? failureClass = null,
        bool? retriable = null,
        JsonObject? block = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(attempt);
        var now = clock.GetUtcNow().UtcDateTime;
        var worthRepeating = retriable ?? FailureClassifier.IsRetriable(code);
        var error = new AttemptErrorDto(code, message, worthRepeating, trace, details, failureClass, block?.DeepClone().AsObject());

        attempt.Status = AttemptStatus.Failed;
        attempt.FinishedAt = now;
        attempt.Error = error;
        item.AttemptCount++;

        // One read for the whole decision: two reads of a live monitor can disagree with each other, and an
        // attempt that was worth repeating by one of them and delayed by the other is a bug waiting for a slow edit.
        var current = settings.Current;
        var limits = EffectiveLimits.For(item, current);
        if (worthRepeating && item.AttemptCount < limits.MaxAttempts)
        {
            WorkItemTransitions.Apply(item, WorkItemStatus.Created, now);

            // A linear back-off owned by the dispatcher; not_before belongs to whoever planned the work.
            var delay = current.RetryDelaySeconds;
            item.RetryAfter = delay == 0 ? null : now.AddSeconds((long)delay * item.AttemptCount);
            journal.Append(
                db,
                actor,
                JournalKinds.WorkItemReleased,
                campaign: null,
                key: attempt.PublicId,
                updated: new JsonObject
                {
                    ["code"] = JsonValue.Create(code),
                    ["retry_after"] = item.RetryAfter is { } retryAfter ? JsonSerializer.SerializeToNode(WorkItemMapper.Utc(retryAfter), JasonJson.Options) : null,
                },
                reason: reason,
                workItem: item,
                attempt: attempt);
            return WorkItemStatus.Created;
        }

        WorkItemTransitions.Apply(item, WorkItemStatus.Failed, now);

        // The item carries the verdict, the attempt keeps the evidence: a trace can hold a whole stderr tail.
        item.LastError = error with { Trace = null };
        journal.Append(
            db,
            actor,
            JournalKinds.WorkItemFailed,
            campaign: null,
            key: attempt.PublicId,
            updated: JsonSerializer.SerializeToNode(item.LastError, JasonJson.Options),
            reason: reason,
            workItem: item,
            attempt: attempt);
        return WorkItemStatus.Failed;
    }

    /// <summary>
    /// What a restart finds: an attempt that was claimed but never started. It was nobody's fault and is not
    /// counted, so the item simply becomes claimable again.
    /// </summary>
    public void Interrupt(JasonDbContext db, WorkItem item, Attempt attempt, ActorRef actor)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(attempt);
        var now = clock.GetUtcNow().UtcDateTime;

        attempt.Status = AttemptStatus.Interrupted;
        attempt.FinishedAt = now;
        WorkItemTransitions.Apply(item, WorkItemStatus.Created, now);
        item.RetryAfter = null;
        journal.Append(
            db,
            actor,
            JournalKinds.WorkItemReleased,
            campaign: null,
            key: attempt.PublicId,
            updated: new JsonObject { ["code"] = JsonValue.Create(AttemptErrors.Interrupted) },
            workItem: item,
            attempt: attempt);
    }

    /// <summary>
    /// The attempt ends waiting, and its item waits: for the items it names to end — all of them, or any one — for a
    /// moment, or for whichever of the two comes first. The attempt is <c>yielded</c>: finished, no longer live, and not
    /// counted against the item's attempts, because nothing failed. The wait is bounded whatever it names: by its moment,
    /// or else by the ceiling counted from now. The bounds are held here, and refused rather than bent: a moment beyond the
    /// ceiling (<c>wait_too_long</c>), more than <see cref="WorkItemWaitRequest.MaxWaitItems"/> items (<c>wait_target_invalid</c>), and an item
    /// that has already waited as often as it may (<c>wait_limit_reached</c>), counted by its yielded attempts, so the
    /// item's attempts must be loaded. A refused wait changes nothing. Which items a wait may name at all is the caller's
    /// to decide; this only writes it.
    /// </summary>
    public WorkItemWait Wait(
        JasonDbContext db,
        WorkItem item,
        Attempt attempt,
        IReadOnlyList<WorkItem> children,
        WaitMode? mode,
        DateTime? until,
        ActorRef actor,
        string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(children);
        if (children.Count == 0 && until is null)
        {
            throw new ArgumentException("A wait names at least one work item or a moment.", nameof(children));
        }

        if ((children.Count == 0) != (mode is null))
        {
            throw new ArgumentException($"A wait on {children.Count} work items cannot have the mode {mode?.ToString() ?? "none"}.", nameof(mode));
        }

        if (children.Select(child => child.Id).Distinct().Count() != children.Count)
        {
            throw new ArgumentException("A wait names each work item once.", nameof(children));
        }

        if (!item.Attempts.Contains(attempt))
        {
            throw new ArgumentException($"Attempt {attempt.PublicId} is not among the loaded attempts of work item {item.PublicId}.", nameof(item));
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var limits = settings.Current.AiRole;
        var ceiling = now.AddSeconds(limits.MaxWaitSeconds);
        if (children.Count > WorkItemWaitRequest.MaxWaitItems)
        {
            throw DomainErrors.WaitTargetInvalid(children.Count);
        }

        if (until > ceiling)
        {
            throw DomainErrors.WaitTooLong(limits.MaxWaitSeconds);
        }

        if (item.Attempts.Count(a => a.Status == AttemptStatus.Yielded) >= limits.MaxWaits)
        {
            throw DomainErrors.WaitLimitReached(item.PublicId, limits.MaxWaits);
        }

        var deadline = until ?? ceiling;

        attempt.Status = AttemptStatus.Yielded;
        attempt.FinishedAt = now;
        WorkItemTransitions.Apply(item, WorkItemStatus.Waiting, now);

        var wait = new WorkItemWait
        {
            WorkItemId = item.Id,
            AttemptId = attempt.Id,
            Mode = mode,
            Until = until,
            Deadline = deadline,
            Status = WaitStatus.Open,
            OpenedAt = now,
            Items = [.. children.Select((child, position) => new WorkItemWaitItem { ChildWorkItemId = child.Id, Position = position })],
        };
        db.WorkItemWaits.Add(wait);

        journal.Append(
            db,
            actor,
            JournalKinds.WorkItemWaiting,
            campaign: null,
            key: attempt.PublicId,
            updated: new JsonObject
            {
                ["on"] = new JsonArray([.. children.Select(child => JsonValue.Create(child.PublicId))]),
                ["mode"] = mode is { } named ? JsonValue.Create(SnakeCaseEnumConverter<WaitMode>.Format(named)) : null,
                ["until"] = until is { } moment ? JsonSerializer.SerializeToNode(WorkItemMapper.Utc(moment), JasonJson.Options) : null,
                ["deadline"] = JsonSerializer.SerializeToNode(WorkItemMapper.Utc(deadline), JasonJson.Options),
            },
            reason: reason,
            workItem: item,
            attempt: attempt);
        return wait;
    }

    /// <summary>
    /// Writes a new item already waiting on the items it needs: the wait a role's run would write with
    /// <c>workitem.wait</c>, all of <paramref name="children"/> and bounded by the same ceiling, but written before any
    /// attempt exists, so the item is never claimed until they have ended and spends no launch waiting for them. The
    /// resume step releases it as it releases any wait — the items ended, or the ceiling came — and it is then ordinary
    /// queued work. Its callers are a create's <c>wait_on</c> and the respond step, whose responder waits on the read of its
    /// thread.
    /// </summary>
    /// <param name="runtimeItem">
    /// Work the runtime adds to the wait after the items a caller named, the critic of a send under an automatic mode: one
    /// item beyond the caller's bound, so a wait of a caller's hundred items and the runtime's one holds a hundred and one.
    /// </param>
    public WorkItemWait WaitFromCreation(JasonDbContext db, WorkItem item, IReadOnlyList<WorkItem> children, ActorRef actor, WorkItem? runtimeItem = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(children);
        if (item.Status != WorkItemStatus.Created || item.Attempts.Count > 0)
        {
            throw new ArgumentException($"Work item {item.PublicId} has already been queued or tried; only a new item waits from its creation.", nameof(item));
        }

        if (children.Count > WorkItemWaitRequest.MaxWaitItems
            || children.Select(child => child.Id).Distinct().Count() != children.Count
            || (runtimeItem is not null && children.Contains(runtimeItem)))
        {
            throw new ArgumentException($"A wait from creation names at most {WorkItemWaitRequest.MaxWaitItems} work items a caller named, each once, and the runtime's one beyond them.", nameof(children));
        }

        children = runtimeItem is null ? children : [.. children, runtimeItem];
        if (children.Count == 0)
        {
            throw new ArgumentException("A wait from creation names at least one work item.", nameof(children));
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var deadline = now.AddSeconds(settings.Current.AiRole.MaxWaitSeconds);
        WorkItemTransitions.Apply(item, WorkItemStatus.Waiting, now);

        var wait = new WorkItemWait
        {
            WorkItem = item,
            Mode = WaitMode.All,
            Deadline = deadline,
            Status = WaitStatus.Open,
            OpenedAt = now,
            Items = [.. children.Select((child, position) => new WorkItemWaitItem { ChildWorkItem = child, Position = position })],
        };
        db.WorkItemWaits.Add(wait);

        journal.Append(
            db,
            actor,
            JournalKinds.WorkItemWaiting,
            campaign: null,
            key: null,
            updated: new JsonObject
            {
                ["on"] = new JsonArray([.. children.Select(child => JsonValue.Create(child.PublicId))]),
                ["mode"] = SnakeCaseEnumConverter<WaitMode>.Format(WaitMode.All),
                ["until"] = null,
                ["deadline"] = JsonSerializer.SerializeToNode(WorkItemMapper.Utc(deadline), JasonJson.Options),
            },
            workItem: item);
        return wait;
    }

    /// <summary>
    /// Parks <paramref name="item"/> on a provider block: it waits on <paramref name="block"/> until the block clears, a
    /// trial of it gets past, or its due date passes. Its two entries are the claim, which parks work a block already
    /// holds before any attempt exists, and an attempt that answered <c>blocked</c>, which ends <c>blocked</c> keeping the
    /// error and the block it met. Either way the item's last error is <paramref name="error"/> — the block, its link
    /// included, is where a person reads it — with the trace left on the attempt, as a failure leaves it. Neither is
    /// counted against the item's attempts: the provider refused for a reason outside the work, so the work has not been
    /// tried.
    /// </summary>
    public WorkItemWait Park(JasonDbContext db, WorkItem item, Attempt? attempt, ProviderBlock block, AttemptErrorDto? error, ActorRef actor)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(block);
        var now = clock.GetUtcNow().UtcDateTime;
        if (attempt is not null)
        {
            attempt.Status = AttemptStatus.Blocked;
            attempt.FinishedAt = now;
            attempt.Error = error;
        }

        if (error is not null)
        {
            item.LastError = error with { Trace = null };
        }

        WorkItemTransitions.Apply(item, WorkItemStatus.Waiting, now);

        // No mode, no moment and no deadline: a block is not a role's wait, and what bounds it is the item's own due date.
        var wait = new WorkItemWait { WorkItem = item, Attempt = attempt, ProviderBlock = block, Status = WaitStatus.Open, OpenedAt = now };
        db.WorkItemWaits.Add(wait);

        journal.Append(
            db,
            actor,
            JournalKinds.WorkItemWaiting,
            campaign: null,
            key: attempt?.PublicId,
            updated: new JsonObject
            {
                ["on"] = new JsonArray(),
                ["mode"] = null,
                ["until"] = null,
                ["deadline"] = null,
                ["blocked_by"] = ProviderBlocks.BlockedBy(block),
            },
            workItem: item,
            attempt: attempt);
        return wait;
    }

    /// <summary>
    /// Holds <paramref name="item"/> at a limit the claim met: it is held at the claim, with no attempt of its own, and
    /// waits until <paramref name="until"/>, the moment the limit lets it go. The counterpart of <see cref="Park"/> for a
    /// limit rather than a block, with one entry, the claim. The item's last error is <paramref name="error"/>, which
    /// names the limit, the moment and the ways out, and the line says which limit held it and at what number. Nothing is
    /// counted against the item's attempts, since the hold starts none; a wait an earlier attempt ended in stays the one
    /// the item's next run resumes from (<see cref="Jason.Runtime.Dispatch.Resumptions"/>).
    /// </summary>
    public WorkItemWait Hold(JasonDbContext db, WorkItem item, DateTime until, AttemptErrorDto error, int limit, ActorRef actor)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(error);
        var now = clock.GetUtcNow().UtcDateTime;
        item.LastError = error;
        WorkItemTransitions.Apply(item, WorkItemStatus.Waiting, now);

        // No attempt, no items and no mode: the moment is the whole wait, and it is its own deadline.
        var wait = new WorkItemWait { WorkItem = item, Until = until, Deadline = until, Status = WaitStatus.Open, OpenedAt = now };
        db.WorkItemWaits.Add(wait);

        var moment = JsonSerializer.SerializeToNode(WorkItemMapper.Utc(until), JasonJson.Options);
        journal.Append(
            db,
            actor,
            JournalKinds.WorkItemWaiting,
            campaign: null,
            key: null,
            updated: new JsonObject
            {
                ["on"] = new JsonArray(),
                ["mode"] = null,
                ["until"] = moment,
                ["deadline"] = moment?.DeepClone(),
                ["held_by"] = new JsonObject { ["code"] = error.Code, ["limit"] = limit, ["role"] = item.Role },
            },
            workItem: item);
        return wait;
    }

    /// <summary>
    /// Keeps <paramref name="item"/>, a search, waiting behind <paramref name="search"/>, another search of its campaign
    /// that has not ended: held at the claim with no attempt of its own until <paramref name="until"/>, a search cadence
    /// off, when the claim asks again. The item's last error is <paramref name="error"/>, which names the search in the
    /// way, and the line says what it waits behind. Nothing is counted against the item's attempts.
    /// </summary>
    public WorkItemWait Behind(JasonDbContext db, WorkItem item, DateTime until, AttemptErrorDto error, string search, ActorRef actor)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(error);
        var now = clock.GetUtcNow().UtcDateTime;
        item.LastError = error;
        WorkItemTransitions.Apply(item, WorkItemStatus.Waiting, now);

        var wait = new WorkItemWait { WorkItem = item, Until = until, Deadline = until, Status = WaitStatus.Open, OpenedAt = now };
        db.WorkItemWaits.Add(wait);

        var moment = JsonSerializer.SerializeToNode(WorkItemMapper.Utc(until), JasonJson.Options);
        journal.Append(
            db,
            actor,
            JournalKinds.WorkItemWaiting,
            campaign: null,
            key: null,
            updated: new JsonObject
            {
                ["on"] = new JsonArray(),
                ["mode"] = null,
                ["until"] = moment,
                ["deadline"] = moment?.DeepClone(),
                ["behind_search"] = search,
            },
            workItem: item);
        return wait;
    }

    /// <summary>
    /// What an attempt of an operation a provider block holds says about the blocks of its plugin and binding, after its
    /// verdict. <see cref="BlockAnswer.GotPast"/> — it succeeded, or the provider judged the work itself — clears every
    /// open block that covers it and every block it was the trial of. <see cref="BlockAnswer.Blocked"/> opens the block <paramref name="said"/> names or sees
    /// it again, and parks the item on it: a trial of the account that met a sender's block has got past the account,
    /// so the account clears and the item parks on the sender. <see cref="BlockAnswer.Undecided"/> says nothing, and a
    /// trial it was puts the block's next trial a cadence off. Every block it cleared is said to have cleared.
    /// <para>
    /// Only work that reaches a person is the block's judge. A held control got past nothing that does: its success, or
    /// the provider's judgement of it, is <see cref="BlockAnswer.Undecided"/>, and its <see cref="BlockAnswer.Blocked"/>
    /// sees the block again and clears none, so no control ever clears a block.
    /// </para>
    /// </summary>
    /// <param name="input">The input the claim composed: the one place a sender is read from.</param>
    /// <param name="error">The error a blocked attempt keeps; only <see cref="BlockAnswer.Blocked"/> reads it.</param>
    public async Task AnsweredAsync(
        JasonDbContext db,
        WorkItem item,
        Attempt attempt,
        string pluginId,
        JsonObject? input,
        BlockAnswer answer,
        JsonObject? said,
        AttemptErrorDto? error,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(attempt);
        var now = clock.GetUtcNow().UtcDateTime;
        var trialSeconds = settings.Current.Blocks.TrialSeconds;
        var identity = ProviderBlocks.Identity(attempt.Provenance?.BindingIdentity);
        var open = await ProviderBlocks.OpenAsync(db, pluginId, identity, cancellationToken).ConfigureAwait(false);
        var cleared = new List<ProviderBlock>();
        var decides = OperationCatalog.TriedByProviderBlock.Contains(item.Operation ?? string.Empty);
        if (answer == BlockAnswer.GotPast && !decides)
        {
            answer = BlockAnswer.Undecided;
        }

        switch (answer)
        {
            case BlockAnswer.GotPast:
                var sender = ProviderBlocks.Sender(input);
                foreach (var block in open.Where(block => block.Scope == ProviderBlockScope.Account
                    || block.TrialWorkItemId == item.Id
                    || (sender is not null && string.Equals(block.SenderExternalId, sender, StringComparison.Ordinal))))
                {
                    ProviderBlocks.Clear(block, attempt, now);
                    cleared.Add(block);
                }

                Untried(open, item, now, trialSeconds);
                break;

            case BlockAnswer.Blocked:
                ArgumentNullException.ThrowIfNull(said);
                var met = ProviderBlocks.Meet(db, open, pluginId, identity, said, attempt, now, trialSeconds);
                foreach (var trial in open.Where(block => block.TrialWorkItemId == item.Id && !ReferenceEquals(block, met)))
                {
                    if (trial.Scope == ProviderBlockScope.Account && met.Scope == ProviderBlockScope.Sender && decides)
                    {
                        ProviderBlocks.Clear(trial, attempt, now);
                        cleared.Add(trial);
                    }
                    else if (trial.Scope == ProviderBlockScope.Account && met.Scope == ProviderBlockScope.Sender)
                    {
                        ProviderBlocks.Undecided(trial, now, trialSeconds);
                    }
                    else
                    {
                        trial.TrialWorkItemId = null;
                    }
                }

                met.TrialWorkItemId = null;
                Park(db, item, attempt, met, error, Actors.Dispatcher);
                await ProviderBlocks.SayBlockedAsync(db, journal, item, met, attempt, cancellationToken).ConfigureAwait(false);
                break;

            default:
                Untried(open, item, now, trialSeconds);
                break;
        }

        foreach (var block in cleared)
        {
            await ProviderBlocks.SayClearedAsync(db, journal, block, attempt, cancellationToken).ConfigureAwait(false);
        }

        static void Untried(IEnumerable<ProviderBlock> open, WorkItem item, DateTime now, int trialSeconds)
        {
            foreach (var trial in open.Where(block => block.Status == ProviderBlockStatus.Open && block.TrialWorkItemId == item.Id))
            {
                ProviderBlocks.Undecided(trial, now, trialSeconds);
            }
        }
    }

    /// <summary>Only the attempt: the caller transitions and journals the item, because it is the one being cancelled.</summary>
    public void CancelAttempt(Attempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        var now = clock.GetUtcNow().UtcDateTime;
        attempt.Status = AttemptStatus.Cancelled;
        attempt.FinishedAt = now;
        attempt.Error = new AttemptErrorDto(AttemptErrors.Cancelled, "The work item was cancelled while this attempt was live.", Retriable: false);
    }
}
