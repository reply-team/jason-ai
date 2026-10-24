using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
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
