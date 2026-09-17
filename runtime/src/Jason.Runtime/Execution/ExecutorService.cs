using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Configuration;
using Jason.Runtime.Domain;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Execution;

/// <summary>
/// The three operations a running executor calls. Each one is fenced by the attempt id: the runtime asks the
/// database, in a single guarded statement, whether this attempt still owns this work item, and an executor
/// that lost its lease is told to stop rather than allowed to write over the run that replaced it.
/// </summary>
public sealed partial class ExecutorService(
    JasonDbContext db,
    TimeProvider clock,
    AttemptOutcomes outcomes,
    DispatcherSettings settings)
{
    /// <summary>A result is read back into an agent's context; a megabyte is already more than that can hold.</summary>
    public const int MaxResultBytes = 1024 * 1024;

    public const int MaxErrorCodeLength = 64;

    public const int MaxErrorMessageLength = 2000;

    public const int MaxReasonLength = 2000;

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
            throw DomainErrors.SettingsUnreadable();
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
            throw DomainErrors.SettingsUnreadable();
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
    /// One UPDATE decides ownership. A read followed by a write would leave a window in which the lease could
    /// change hands, so the guard lives in the WHERE clause and the row count is the answer.
    /// </summary>
    private async Task FenceAsync(string workItemId, string attemptId, DateTime now, CancellationToken cancellationToken)
    {
        var touched = await FenceQuery(db, workItemId, attemptId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(a => a.LastHeartbeatAt, now), cancellationToken)
            .ConfigureAwait(false);
        if (touched == 0)
        {
            throw DomainErrors.StaleAttempt(attemptId);
        }
    }

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

    private static void ValidateReason(string? reason, ValidationErrors errors)
    {
        if (reason is not null && reason.Trim().Length > MaxReasonLength)
        {
            errors.Add("reason", "too_long", string.Create(CultureInfo.InvariantCulture, $"reason must be at most {MaxReasonLength} characters."));
        }
    }

    private static void EnsureResultWithinLimits(JsonNode? result)
    {
        if (result is not null && JsonSerializer.SerializeToUtf8Bytes(result, JasonJson.Options).Length > MaxResultBytes)
        {
            throw DomainErrors.ResultTooLarge(MaxResultBytes);
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9_]{0,63}$")]
    private static partial Regex ErrorCode();
}
