using Jason.Runtime.Configuration;
using Jason.Runtime.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Dispatch;

/// <summary>
/// How much work this runtime runs at once. The size is fixed when the process starts, because a pool that
/// grew under a running scan would let the claim hand out slots that do not exist; the scan claims only what
/// the pool can take, so a queued item never sits watching its own lease expire.
/// </summary>
public sealed class HandlerPool(
    IOptions<DispatcherOptions> options,
    IServiceScopeFactory scopes,
    RunningAttemptRegistry registry,
    ILogger<HandlerPool> logger)
{
    private static readonly Action<ILogger, string, Exception?> HandlerFailed = LoggerMessage.Define<string>(
        LogLevel.Error,
        new EventId(1, nameof(HandlerFailed)),
        "The handler of attempt {AttemptId} ended unexpectedly");

    private readonly Lock _gate = new();
    private readonly Dictionary<string, Task> _inFlight = new(StringComparer.Ordinal);

    public int MaxParallel { get; } = options.Value.MaxParallel;

    public int FreeSlots
    {
        get
        {
            lock (_gate)
            {
                return MaxParallel - _inFlight.Count;
            }
        }
    }

    public IReadOnlyCollection<string> InFlightAttemptIds
    {
        get
        {
            lock (_gate)
            {
                return [.. _inFlight.Keys];
            }
        }
    }

    /// <summary>Takes a slot and runs the attempt. Never awaited: a scan that waited on a handler would stop scanning.</summary>
    public void Dispatch(ClaimedWork work)
    {
        ArgumentNullException.ThrowIfNull(work);
        var handler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_inFlight.Count >= MaxParallel)
            {
                throw new InvalidOperationException($"The handler pool is full ({MaxParallel} handlers); the claim asked for more than the free slots.");
            }

            _inFlight.Add(work.AttemptPublicId, handler.Task);
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await AttemptHandler.RunAsync(scopes, work, registry, logger).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // A handler is the last line: whatever it failed at, the lease still ends the attempt.
                HandlerFailed(logger, work.AttemptPublicId, ex);
            }
            finally
            {
                lock (_gate)
                {
                    _inFlight.Remove(work.AttemptPublicId);
                }

                handler.SetResult();
            }
        });
    }

    /// <summary>
    /// Waits for the handlers in flight, and no longer than the timeout. Nothing is cancelled: a child that
    /// outlives the runtime keeps its lease and is decided after the restart.
    /// </summary>
    public async Task<bool> DrainAsync(TimeSpan timeout)
    {
        Task[] pending;
        lock (_gate)
        {
            pending = [.. _inFlight.Values];
        }

        if (pending.Length == 0)
        {
            return true;
        }

        var all = Task.WhenAll(pending);
        using var expiry = new CancellationTokenSource();
        var finished = await Task.WhenAny(all, Task.Delay(timeout, expiry.Token)).ConfigureAwait(false);
        await expiry.CancelAsync().ConfigureAwait(false);
        return finished == all;
    }
}
