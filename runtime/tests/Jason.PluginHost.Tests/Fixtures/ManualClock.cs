namespace Jason.PluginHost.Tests.Fixtures;

/// <summary>
/// A clock that moves only when the test moves it, so a deadline can be read as an exact value and a wait for it seen
/// not to end a millisecond early. Its timers fire on the thread that advances it, in the order they fall due, or
/// before they fall due when the test says so.
/// </summary>
public sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    private readonly Lock _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    /// <summary>How many timers are waiting for the clock to reach them.</summary>
    public int Waiting
    {
        get
        {
            lock (_gate)
            {
                return _timers.Count;
            }
        }
    }

    public override long GetTimestamp() => GetUtcNow().UtcTicks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>When the first of the waiting timers falls due, or nothing when none is waiting.</summary>
    public DateTimeOffset? NextDue
    {
        get
        {
            lock (_gate)
            {
                return _timers.Count == 0 ? null : _timers.Min(timer => timer.Due);
            }
        }
    }

    /// <summary>Moves the clock on and fires every timer that has fallen due by then.</summary>
    public void Advance(TimeSpan by) => Fire(by, early: false);

    /// <summary>
    /// Fires every waiting timer now, before it has fallen due, as a real timer's tick can come before the moment it was
    /// set for; the clock does not move.
    /// </summary>
    public void FireEarly() => Fire(TimeSpan.Zero, early: true);

    private void Fire(TimeSpan by, bool early)
    {
        List<ManualTimer> due;
        lock (_gate)
        {
            _now += by;
            due = [.. _timers.Where(timer => early || timer.Due <= _now).OrderBy(timer => timer.Due)];
            foreach (var timer in due)
            {
                _timers.Remove(timer);
            }
        }

        foreach (var timer in due)
        {
            timer.Fire();
        }
    }

    private void Schedule(ManualTimer timer, DateTimeOffset? due)
    {
        lock (_gate)
        {
            _timers.Remove(timer);
            if (due is { } at)
            {
                timer.Due = at;
                _timers.Add(timer);
            }
        }
    }

    /// <summary>A one-shot timer on the manual clock; a period is not needed by anything that waits here.</summary>
    private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset Due { get; set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            clock.Schedule(this, dueTime == Timeout.InfiniteTimeSpan ? null : clock.GetUtcNow() + dueTime);
            return true;
        }

        public void Fire() => callback(state);

        public void Dispose() => clock.Schedule(this, null);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
