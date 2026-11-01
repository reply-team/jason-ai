namespace Jason.Runtime.Tests;

/// <summary>
/// Work done on a thread of its own, so that a test can see the moment that thread waits — for a lock another thread
/// holds, say — and act then, rather than after a guess at how long it takes to get there. The 30 seconds a wait for it
/// is capped at are a guard against a hang, never a margin.
/// </summary>
public sealed class OnItsOwnThread<T>
{
    private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(30);

    private readonly TaskCompletionSource<T> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _thread;

    public OnItsOwnThread(Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        _thread = new Thread(() =>
        {
            try
            {
                _done.TrySetResult(work());
            }
            catch (Exception failed)
            {
                _done.TrySetException(failed);
            }
        })
        {
            IsBackground = true,
        };
        _thread.Start();
    }

    /// <summary>Waits until the thread waits, or has finished because it had nothing to wait for.</summary>
    public async Task WaitingOrDoneAsync(CancellationToken cancellationToken)
    {
        var started = TimeProvider.System.GetTimestamp();
        while (!_done.Task.IsCompleted && (_thread.ThreadState & ThreadState.WaitSleepJoin) == 0)
        {
            if (TimeProvider.System.GetElapsedTime(started) > HangGuard)
            {
                throw new TimeoutException($"The thread neither waited nor finished in {HangGuard.TotalSeconds} seconds.");
            }

            await Task.Delay(1, cancellationToken);
        }
    }

    /// <summary>Whether the work has finished, answered or failed, by now.</summary>
    public bool IsDone => _done.Task.IsCompleted;

    /// <summary>What the work answered, once it has finished, or how it failed.</summary>
    public Task<T> DoneAsync(CancellationToken cancellationToken) => _done.Task.WaitAsync(HangGuard, cancellationToken);
}
