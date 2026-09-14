namespace Jason.Runtime.Execution;

/// <summary>
/// Which attempts are running in this process right now, and how to stop each one. The database says what a
/// work item is; this says what is still breathing, which is the only thing a cancellation or a lost lease can
/// actually act on.
/// </summary>
public sealed class RunningAttemptRegistry
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Entry> _running = new(StringComparer.Ordinal);

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _running.Count;
            }
        }
    }

    public IReadOnlyCollection<string> AttemptIds
    {
        get
        {
            lock (_gate)
            {
                return [.. _running.Keys];
            }
        }
    }

    /// <summary>Registers a running attempt; disposing the registration removes it, whatever the attempt's fate.</summary>
    public IDisposable Register(string attemptId, Action kill)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attemptId);
        ArgumentNullException.ThrowIfNull(kill);
        lock (_gate)
        {
            _running[attemptId] = new Entry(kill);
        }

        return new Registration(this, attemptId);
    }

    /// <summary>Asks the attempt to stop. The kill runs at most once; false means nothing here is running under that id.</summary>
    public bool TryKill(string attemptId)
    {
        Action? kill = null;
        lock (_gate)
        {
            if (!_running.TryGetValue(attemptId, out var entry))
            {
                return false;
            }

            if (!entry.Killed)
            {
                entry.Killed = true;
                kill = entry.Kill;
            }
        }

        // Outside the lock: a kill cancels a token, and cancellation runs continuations on this thread.
        kill?.Invoke();
        return true;
    }

    private void Remove(string attemptId)
    {
        lock (_gate)
        {
            _running.Remove(attemptId);
        }
    }

    private sealed class Entry(Action kill)
    {
        public Action Kill { get; } = kill;

        public bool Killed { get; set; }
    }

    private sealed class Registration(RunningAttemptRegistry registry, string attemptId) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            registry.Remove(attemptId);
        }
    }
}
