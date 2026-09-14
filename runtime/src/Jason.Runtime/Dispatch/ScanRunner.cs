using Jason.Contracts.Api;
using Jason.Runtime.Execution;
using Jason.Runtime.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Dispatch;

/// <summary>What one scan did. Zeros are the normal answer: most ticks find nothing to do.</summary>
public sealed record ScanReport(int Expired, int Lost, int Claimed);

/// <summary>
/// One pass of the dispatcher: give up what is overdue, take back what is lost, hand out what is ready — in
/// that order and in three short transactions, so no step holds the writer while another thinks. The scan hands
/// claims to the pool and returns; waiting for a handler is what would turn a dispatcher into a queue of one.
/// </summary>
public sealed class ScanRunner(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    HandlerPool pool,
    DispatcherStatus status,
    ILogger<ScanRunner> logger)
{
    private static readonly Action<ILogger, long, int, int, int, Exception?> Scanned = LoggerMessage.Define<long, int, int, int>(
        LogLevel.Debug,
        new EventId(1, nameof(Scanned)),
        "Dispatcher scan {Scan}: {Expired} expired, {Lost} lost, {Claimed} claimed");

    private static readonly Action<ILogger, int, Exception?> Expired = LoggerMessage.Define<int>(
        LogLevel.Information,
        new EventId(2, nameof(Expired)),
        "Dispatcher expired {Count} work items whose due date had passed");

    private static readonly Action<ILogger, int, Exception?> Lost = LoggerMessage.Define<int>(
        LogLevel.Information,
        new EventId(3, nameof(Lost)),
        "Dispatcher took back {Count} attempts nobody was looking after");

    private static readonly Action<ILogger, string, string, Exception?> Claimed = LoggerMessage.Define<string, string>(
        LogLevel.Information,
        new EventId(4, nameof(Claimed)),
        "Dispatcher claimed work item {WorkItemId} as attempt {AttemptId}");

    public async Task<ScanReport> ScanOnceAsync(CancellationToken ct)
    {
        // Draining, stopped or disabled: a scan would claim work this process is not going to run.
        if (status.State != DispatcherState.Running)
        {
            return new ScanReport(0, 0, 0);
        }

        var expired = await InScopeAsync<Expirer, int>((step, db) => step.ExpireAsync(db, ct)).ConfigureAwait(false);
        var lost = await InScopeAsync<LeaseEnforcer, int>((step, db) => step.EnforceAsync(db, ct)).ConfigureAwait(false);
        var claimed = await InScopeAsync<Claimer, IReadOnlyList<ClaimedWork>>((step, db) => step.ClaimAsync(db, pool.FreeSlots, ct)).ConfigureAwait(false);

        foreach (var work in claimed)
        {
            Claimed(logger, work.WorkItemPublicId, work.AttemptPublicId, null);
            pool.Dispatch(work);
        }

        status.LastScanAt = clock.GetUtcNow();
        status.Scans++;
        if (expired > 0)
        {
            Expired(logger, expired, null);
        }

        if (lost > 0)
        {
            Lost(logger, lost, null);
        }

        Scanned(logger, status.Scans, expired, lost, claimed.Count, null);
        return new ScanReport(expired, lost, claimed.Count);
    }

    private async Task<TResult> InScopeAsync<TStep, TResult>(Func<TStep, JasonDbContext, Task<TResult>> step)
        where TStep : notnull
    {
        await using var scope = scopes.CreateAsyncScope();
        return await step(
            scope.ServiceProvider.GetRequiredService<TStep>(),
            scope.ServiceProvider.GetRequiredService<JasonDbContext>()).ConfigureAwait(false);
    }
}
