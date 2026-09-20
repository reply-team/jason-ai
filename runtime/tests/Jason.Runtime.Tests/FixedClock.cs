namespace Jason.Runtime.Tests;

/// <summary>
/// A clock the test controls, and timers that fire only when the test moves it. Nothing here waits in real
/// time: a timer is due at a moment on this clock, and moving the clock fires every timer whose moment it
/// passes, in the order they fall due, before the move returns — so a service that sleeps through
/// <c>Task.Delay(…, clock, …)</c> wakes exactly when the test says the time has come, and a test that never
/// moves the clock has proved the service never woke.
/// </summary>
public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    private readonly Lock _gate = new();
    private readonly List<FakeTimer> _timers = [];
    private DateTimeOffset _now = now;
    private long _made;

    /// <summary>Setting it is a move like <see cref="Advance"/>: the timers on the way fire.</summary>
    public DateTimeOffset Now
    {
        get => _now;
        set => MoveTo(value);
    }

    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>
    /// How many timers are waiting for a moment this clock has not reached. A service between two waits holds
    /// none, which is how a test knows the next wait is armed before it moves time into it.
    /// </summary>
    public int Armed
    {
        get
        {
            lock (_gate)
            {
                return _timers.Count(timer => timer.Due is not null);
            }
        }
    }

    public void Advance(TimeSpan by) => MoveTo(_now.Add(by));

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var timer = new FakeTimer(this, callback, state);
        lock (_gate)
        {
            timer.Sequence = ++_made;
            _timers.Add(timer);
        }

        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>
    /// Moves the clock, firing timers on the way. Each fires with the clock reading its own due moment, which
    /// is what a callback that asks the time expects, and each fires outside the lock: a delay loop registers
    /// its next timer from inside the callback of the last one, and that must not deadlock. A timer made that
    /// way fires in this same move if the move reaches it, as it would have in real time.
    /// </summary>
    private void MoveTo(DateTimeOffset target)
    {
        while (true)
        {
            FakeTimer? next;
            lock (_gate)
            {
                next = _timers
                    .Where(timer => timer.Due is { } due && due <= target)
                    .OrderBy(timer => timer.Due)
                    .ThenBy(timer => timer.Sequence)
                    .FirstOrDefault();

                if (next is null)
                {
                    _now = target;
                    return;
                }

                _now = next.Due!.Value;
                next.Rearm(target);
            }

            next.Fire();
        }
    }

    /// <summary>
    /// One registration. Due is a moment on the owning clock or null for a timer that is not armed; a period
    /// of zero or infinite makes it one-shot, as the real timer's contract says.
    /// </summary>
    private sealed class FakeTimer(FixedClock clock, TimerCallback callback, object? state) : ITimer
    {
        private TimeSpan _period;
        private bool _disposed;

        public long Sequence { get; set; }

        public DateTimeOffset? Due { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(dueTime, Timeout.InfiniteTimeSpan);
            ArgumentOutOfRangeException.ThrowIfLessThan(period, Timeout.InfiniteTimeSpan);
            lock (clock._gate)
            {
                if (_disposed)
                {
                    return false;
                }

                Due = dueTime == Timeout.InfiniteTimeSpan ? null : clock._now + dueTime;
                _period = period;
                return true;
            }
        }

        /// <summary>
        /// Under the clock's lock, just before firing: the next due moment, or none for a one-shot. A periodic
        /// timer is re-armed from where this move ends rather than from the tick it is firing for, so a move
        /// that passes several of its periods fires it once — a real periodic timer does not hand a process
        /// that was not looking a callback for every period it missed.
        /// </summary>
        public void Rearm(DateTimeOffset target) =>
            Due = _period > TimeSpan.Zero ? target + _period : null;

        public void Fire() => callback(state);

        public void Dispose()
        {
            lock (clock._gate)
            {
                _disposed = true;
                Due = null;
                clock._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
