using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Contracts.Operations;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Execution;

/// <summary>
/// The four operations a running executor calls. Each one is fenced by the attempt id: the runtime asks the
/// database, in a single guarded statement, whether this attempt still owns this work item, and an executor
/// that lost its lease is told to stop rather than allowed to write over the run that replaced it.
/// </summary>
public sealed partial class ExecutorService(
    JasonDbContext db,
    TimeProvider clock,
    AttemptOutcomes outcomes,
    LiveSettings<DispatcherOptions> settings,
    PatternTiming? timing = null)
{
    private readonly PatternTiming _timing = timing ?? PatternTiming.Default;

    /// <summary>A result is read back into an agent's context; a megabyte is already more than that can hold.</summary>
    public const int MaxResultBytes = 1024 * 1024;

    public const int MaxErrorCodeLength = 64;

    public const int MaxErrorMessageLength = 2000;

    public const int MaxReasonLength = 2000;

    /// <summary>The prefix of a decision's public id, which a wait names only to be refused.</summary>
    private const string DecisionPrefix = "dec_";

    /// <summary>
    /// The fence as a query, so what the runtime guards on can be read rather than trusted: the attempt is the
    /// named one, it is running, and the item it belongs to is the named one and is processing.
    /// </summary>
    public static IQueryable<Attempt> FenceQuery(JasonDbContext db, string workItemId, string attemptId)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Attempts.Where(a => a.PublicId == attemptId
            && a.Status == AttemptStatus.Running
            && a.WorkItem!.PublicId == workItemId
            && a.WorkItem.Status == WorkItemStatus.Processing);
    }

    /// <summary>"I am still here." No journal entry: liveness is not history, and there would be one per minute.</summary>
    public async Task<HeartbeatResponse> HeartbeatAsync(WorkItemHeartbeatRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (workItemId, attemptId) = RequireIds(request.WorkItemId, request.AttemptId);
        var now = clock.GetUtcNow().UtcDateTime;

        await FenceAsync(workItemId, attemptId, now, cancellationToken).ConfigureAwait(false);

        var attempt = await db.Attempts.AsNoTracking()
            .Include(a => a.WorkItem)
            .FirstAsync(a => a.PublicId == attemptId, cancellationToken)
            .ConfigureAwait(false);
        // The fence has already written down that this executor is alive, which is true whatever the settings
        // say. What is missing is the interval to answer with, and there is nothing left to read it from.
        if (!settings.TryCurrent(out var current))
        {
            throw DomainErrors.SettingsUnreadable(DispatcherOptions.Section);
        }

        var limits = EffectiveLimits.For(attempt.WorkItem!, current);

        // Twice the interval, so one late heartbeat is not a lost executor; the dispatcher enforces the same rule.
        var dueBy = limits.HeartbeatSeconds == 0 ? (DateTimeOffset?)null : WorkItemMapper.Utc(now.AddSeconds(2L * limits.HeartbeatSeconds));
        return new HeartbeatResponse(workItemId, attemptId, WorkItemMapper.Utc(attempt.LockUntil), dueBy);
    }

    /// <summary>
    /// Progress worth keeping before the work is done: the whole result is replaced, so an executor that is
    /// killed halfway leaves behind what it had rather than nothing.
    /// </summary>
    public async Task<WorkItemDto> SetResultAsync(WorkItemSetResultRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (workItemId, attemptId) = RequireIds(request.WorkItemId, request.AttemptId);
        EnsureResultWithinLimits(request.Result);
        var now = clock.GetUtcNow().UtcDateTime;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await FenceAsync(workItemId, attemptId, now, cancellationToken).ConfigureAwait(false);

        var item = await LoadAsync(workItemId, cancellationToken).ConfigureAwait(false);

        // Deliberately not held to the item's result_format. This is progress on unfinished work — the point of it
        // is that an executor killed halfway leaves behind what it had — and half of an answer is not the shape of
        // a whole one. What must satisfy the shape is what stands when the work is called done, and completion is
        // where that is decided, including when it is this value that stands.
        item.Result = request.Result?.DeepClone();
        item.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return WorkItemMapper.ToDto(item, now, item.Attempts, includeSnapshots: false);
    }

    /// <summary>The attempt says how it ended; what that means for the work item is <see cref="AttemptOutcomes"/>' decision.</summary>
    public async Task<WorkItemDto> CompleteAsync(WorkItemCompleteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (workItemId, attemptId) = RequireIds(request.WorkItemId, request.AttemptId, errors =>
        {
            ValidateCompletion(request, errors);
            ValidateReason(request.Reason, errors);
        });

        EnsureResultWithinLimits(request.Result);

        // A failure is where the settings decide what happens next — how many attempts this kind of work gets,
        // and how long before the next one. Asked before anything is touched, and asked only of the branch that
        // needs an answer: a successful completion reads nothing from them and is never held up by them.
        if (request.Status == CompletionStatus.Failed && !settings.TryCurrent(out _))
        {
            throw DomainErrors.SettingsUnreadable(DispatcherOptions.Section);
        }

        var now = clock.GetUtcNow().UtcDateTime;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await FenceAsync(workItemId, attemptId, now, cancellationToken).ConfigureAwait(false);

        var item = await LoadAsync(workItemId, cancellationToken).ConfigureAwait(false);

        // The fence just proved there is exactly one, and that it is this caller's.
        var attempt = WorkItemQueries.LiveAttempt(item)!;
        var actor = Actors.ForAttempt(attempt);
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();

        if (request.Status == CompletionStatus.Succeeded)
        {
            // What is judged is the result that will stand: the one given here, or — where this call carries none —
            // whatever the last set_result left behind, because that is what the work will be remembered by.
            // Only a success is judged at all. A failure has an error to report and no result to be measured
            // against, and holding it to a shape would leave an executor that could not do the job with nothing
            // it could say.
            EnsureResultSatisfiesFormat(item, request.Result ?? item.Result);
            outcomes.Succeed(db, item, attempt, request.Result, actor, reason);
        }
        else
        {
            outcomes.Fail(db, item, attempt, request.Error!.Code!.Trim(), request.Error.Message!.Trim(), trace: null, request.Error.Details, actor, reason);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return WorkItemMapper.ToDto(item, now, item.Attempts, includeSnapshots: false);
    }

    /// <summary>
    /// The attempt ends by waiting, and its item waits until what it names has happened; the next run is a new attempt,
    /// told in its envelope what it waited for. The refusals come in a fixed order, each one after the fence, so a caller
    /// that lost its attempt is told only that: the fence; a check-in, or provider work, that may not wait
    /// (<c>wait_not_allowed</c>); the named items, each raised by an attempt of this item in its campaign and none of them
    /// a decision (<c>wait_target_invalid</c>); a moment already past; and last the bounds, which
    /// <see cref="AttemptOutcomes.Wait"/> holds where the wait is written.
    /// </summary>
    public async Task<WorkItemDto> WaitAsync(WorkItemWaitRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        IReadOnlyList<string> on = [];
        var (workItemId, attemptId) = RequireIds(request.WorkItemId, request.AttemptId, errors =>
        {
            on = ValidateWait(request, errors);
            ValidateReason(request.Reason, errors);
        });

        // Items named and no mode is the common wait, on everything this run raised, so it is the default; a mode
        // without items was refused above.
        var mode = on.Count == 0 ? (WaitMode?)null : request.Mode ?? WaitMode.All;
        var until = request.Until?.UtcDateTime;
        var now = clock.GetUtcNow().UtcDateTime;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await FenceAsync(workItemId, attemptId, now, cancellationToken).ConfigureAwait(false);

        var item = await LoadAsync(workItemId, cancellationToken).ConfigureAwait(false);
        if (ManagerCheckIn.Is(item))
        {
            throw DomainErrors.WaitNotAllowed(item.PublicId, "a check-in waits for its cadence; finish it, and the next review comes round.");
        }

        if (item.Kind != WorkItemKind.AiRole)
        {
            throw DomainErrors.WaitNotAllowed(item.PublicId, "only a role's work waits; a provider operation ends through its plugin.");
        }

        var children = await TargetsAsync(item, on, cancellationToken).ConfigureAwait(false);
        if (until <= now)
        {
            var errors = new ValidationErrors();
            errors.Add("until", "in_the_past", "until must be later than now; a moment already past is nothing to wait for.");
            errors.ThrowIfAny();
        }

        // The bounds are read from the settings, so they are asked last: a wait refused for what it named never
        // depended on them.
        if (!settings.TryCurrent(out _))
        {
            throw DomainErrors.SettingsUnreadable(DispatcherOptions.Section);
        }

        var attempt = WorkItemQueries.LiveAttempt(item)!;
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        var wait = outcomes.Wait(db, item, attempt, children, mode, until, Actors.ForAttempt(attempt), reason);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return WorkItemMapper.ToDto(item, now, item.Attempts, includeSnapshots: false) with
        {
            Wait = WaitBlocks.Of(wait, attempt.PublicId, children),
        };
    }

    /// <summary>
    /// The named items, in the order they were named, each one checked in that order, so a refusal names the first one
    /// refused: not a decision, raised by an attempt of this item and in its campaign. Because only the item's own runs raise what it may wait for, no wait can name an ancestor, and no chain
    /// of waits can close on itself.
    /// </summary>
    private async Task<List<WorkItem>> TargetsAsync(WorkItem item, IReadOnlyList<string> on, CancellationToken cancellationToken)
    {
        if (on.Count > WorkItemWaitRequest.MaxWaitItems)
        {
            throw DomainErrors.WaitTargetInvalid(on.Count);
        }

        var raisedBy = item.Attempts.Select(a => a.PublicId).ToHashSet(StringComparer.Ordinal);
        var found = await db.WorkItems
            .Where(w => on.Contains(w.PublicId))
            .ToDictionaryAsync(w => w.PublicId, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        var children = new List<WorkItem>(on.Count);
        foreach (var id in on)
        {
            if (id.StartsWith(DecisionPrefix, StringComparison.Ordinal))
            {
                throw DomainErrors.WaitTargetDecision(id);
            }

            if (!found.TryGetValue(id, out var child))
            {
                throw DomainErrors.WaitTargetNotRaised(id, item.PublicId, "no such work item exists");
            }

            if (child.CampaignId != item.CampaignId)
            {
                throw DomainErrors.WaitTargetNotRaised(id, item.PublicId, "it is in another campaign");
            }

            if (child.CreatedByType != ActorType.Attempt || child.CreatedById is not { } by || !raisedBy.Contains(by))
            {
                throw DomainErrors.WaitTargetNotRaised(id, item.PublicId, "no attempt of this item raised it");
            }

            children.Add(child);
        }

        return children;
    }

    /// <summary>
    /// The fence as one statement, for every caller a launched role reaches through: the attempt is the named
    /// one, it is running, it owns the named item, and the item is processing. One UPDATE decides ownership —
    /// a read followed by a write would leave a window in which the lease could change hands, so the guard
    /// lives in the WHERE clause and the row count is the answer.
    /// </summary>
    /// <remarks>
    /// Raising a question goes through this too, which is why raising one says the role is still alive: the
    /// same statement that proves the claim records the heartbeat.
    /// </remarks>
    public static async Task RequireAttemptAsync(
        JasonDbContext db,
        string workItemId,
        string attemptId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var touched = await FenceQuery(db, workItemId, attemptId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(a => a.LastHeartbeatAt, now), cancellationToken)
            .ConfigureAwait(false);
        if (touched == 0)
        {
            throw DomainErrors.StaleAttempt(attemptId);
        }
    }

    /// <summary>The fence, for this service's own four operations: one implementation, named twice.</summary>
    private Task FenceAsync(string workItemId, string attemptId, DateTime now, CancellationToken cancellationToken) =>
        RequireAttemptAsync(db, workItemId, attemptId, now, cancellationToken);

    private async Task<WorkItem> LoadAsync(string workItemId, CancellationToken cancellationToken) =>
        await db.WorkItems.WithNavigation().Include(w => w.Attempts)
            .FirstAsync(w => w.PublicId == workItemId, cancellationToken)
            .ConfigureAwait(false);

    private static (string WorkItemId, string AttemptId) RequireIds(string? workItemId, string? attemptId, Action<ValidationErrors>? also = null)
    {
        var errors = new ValidationErrors();
        if (string.IsNullOrWhiteSpace(workItemId))
        {
            errors.Add("work_item_id", "required", "work_item_id is required.");
        }

        if (string.IsNullOrWhiteSpace(attemptId))
        {
            errors.Add("attempt_id", "required", "attempt_id is required.");
        }

        also?.Invoke(errors);
        errors.ThrowIfAny();
        return (workItemId!.Trim(), attemptId!.Trim());
    }

    private static void ValidateCompletion(WorkItemCompleteRequest request, ValidationErrors errors)
    {
        if (request.Status is not { } status)
        {
            errors.Add("status", "required", "status is required and is either succeeded or failed.");
            return;
        }

        if (status == CompletionStatus.Succeeded)
        {
            if (request.Error is not null)
            {
                errors.Add("error", "not_allowed", "error belongs to a failed completion.");
            }

            return;
        }

        if (request.Error is null)
        {
            errors.Add("error.code", "required", "a failed completion must name the error code.");
            errors.Add("error.message", "required", "a failed completion must carry an error message.");
            return;
        }

        ValidateErrorCode(request.Error.Code, errors);
        ValidateErrorMessage(request.Error.Message, errors);
    }

    private static void ValidateErrorCode(string? code, ValidationErrors errors)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            errors.Add("error.code", "required", "a failed completion must name the error code.");
            return;
        }

        // The code is classified and stored; a free-form sentence there would make the rule set unreadable.
        if (!ErrorCode().IsMatch(code.Trim()))
        {
            errors.Add("error.code", "invalid", string.Create(CultureInfo.InvariantCulture, $"error.code must be lowercase snake_case of at most {MaxErrorCodeLength} characters."));
        }
    }

    private static void ValidateErrorMessage(string? message, ValidationErrors errors)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            errors.Add("error.message", "required", "a failed completion must carry an error message.");
            return;
        }

        if (message.Trim().Length > MaxErrorMessageLength)
        {
            errors.Add("error.message", "too_long", string.Create(CultureInfo.InvariantCulture, $"error.message must be at most {MaxErrorMessageLength} characters."));
        }
    }

    /// <summary>
    /// The shape of a wait, before anything is read: something to wait for, a mode only beside items, and each item named
    /// once. Returns the named ids, trimmed, in the order given.
    /// </summary>
    private static IReadOnlyList<string> ValidateWait(WorkItemWaitRequest request, ValidationErrors errors)
    {
        var on = request.On ?? [];
        if (on.Count == 0 && request.Until is null)
        {
            errors.Add("on", "required", "a wait names at least one work item in on, a moment in until, or both.");
        }

        if (on.Count == 0 && request.Mode is not null)
        {
            errors.Add("mode", "not_allowed", "mode says how a wait on work items ends; a wait on a moment alone has none.");
        }

        if (on.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add("on", "invalid", "on holds work item ids, as wi_..., none of them blank.");
            return [];
        }

        var trimmed = on.Select(id => id.Trim()).ToList();
        if (trimmed.Distinct(StringComparer.Ordinal).Count() != trimmed.Count)
        {
            errors.Add("on", "duplicate", "on names each work item once.");
        }

        return trimmed;
    }

    private static void ValidateReason(string? reason, ValidationErrors errors)
    {
        if (reason is not null && reason.Trim().Length > MaxReasonLength)
        {
            errors.Add("reason", "too_long", string.Create(CultureInfo.InvariantCulture, $"reason must be at most {MaxReasonLength} characters."));
        }
    }

    /// <summary>
    /// A work item may declare the shape of the answer it wants, and a declaration nothing enforces is only
    /// decoration: an agent asked for findings could answer with a paragraph about how thoroughly it had looked
    /// and the work would be recorded as a success. The schema is the published dialect, checked when the item
    /// was written, so applying it here is bounded work over a document that has already been weighed.
    /// </summary>
    /// <remarks>
    /// A pattern in that schema that did not finish matching decides nothing about the answer. Nothing has been
    /// recorded and the executor still holds its attempt, so the completion is answered as one to send again rather
    /// than refused — unless the answer is wrong somewhere else, which it is whatever the pattern would have said.
    /// </remarks>
    private void EnsureResultSatisfiesFormat(WorkItem item, JsonNode? result)
    {
        if (item.ResultFormat is not JsonObject schema)
        {
            return;
        }

        var problems = SchemaValidator.Validate(result, schema, _timing);
        if (SchemaValidator.Verdicts(problems) is { Count: > 0 } verdicts)
        {
            throw DomainErrors.ResultInvalid(ErrorDetail.From(verdicts));
        }

        if (SchemaValidator.Unmeasured(problems))
        {
            throw DomainErrors.ValidationTimedOut(ErrorDetail.From(problems));
        }
    }

    private static void EnsureResultWithinLimits(JsonNode? result)
    {
        if (result is not null && JsonSerializer.SerializeToUtf8Bytes(result, JasonJson.Options).Length > MaxResultBytes)
        {
            throw DomainErrors.ResultTooLarge(MaxResultBytes);
        }
    }

    [GeneratedRegex(@"^[a-z][a-z0-9_]{0,63}\z")]
    private static partial Regex ErrorCode();
}
