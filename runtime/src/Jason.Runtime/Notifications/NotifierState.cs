using Jason.Contracts.Ids;

namespace Jason.Runtime.Notifications;

/// <summary>
/// What the notifier knows between scans, for the life of the process: where it has read the journal up to, the
/// burst that has not been said yet, whether a notification is being sent, and when one last reached anybody. All of
/// it under one lock, because the scan writes it and the sender releases it from another thread.
/// </summary>
/// <remarks>
/// Nothing here is durable, and nothing has to be: the <c>notification_sent</c> line is the watermark a new process
/// starts from, and a burst this process had not said yet is read again from the lines above it.
/// </remarks>
public sealed class NotifierState
{
    private readonly Lock _gate = new();
    private bool _started;
    private int _cursor;
    private string? _cursorPublicId;
    private Burst? _pending;
    private string? _inFlightId;
    private DateTime? _lastDeliveredAt;

    public bool Started
    {
        get
        {
            lock (_gate)
            {
                return _started;
            }
        }
    }

    /// <summary>The journal's internal key of the last line read; the next read starts above it.</summary>
    public int Cursor
    {
        get
        {
            lock (_gate)
            {
                return _cursor;
            }
        }
    }

    /// <summary>The public id of the last line read, which is what a notification says it covers through.</summary>
    public string? CursorPublicId
    {
        get
        {
            lock (_gate)
            {
                return _cursorPublicId;
            }
        }
    }

    /// <summary>The lines read since the last hand-over that something needs a person, or null when there are none.</summary>
    public Burst? Pending
    {
        get
        {
            lock (_gate)
            {
                return _pending;
            }
        }
    }

    /// <summary>Whether a notification has been handed over and its sender has not finished with it; which one is kept, so its end frees only itself.</summary>
    public bool InFlight
    {
        get
        {
            lock (_gate)
            {
                return _inFlightId is not null;
            }
        }
    }

    /// <summary>When a notification last reached anybody on any channel; the floor counts from here.</summary>
    public DateTime? LastDeliveredAt
    {
        get
        {
            lock (_gate)
            {
                return _lastDeliveredAt;
            }
        }
    }

    /// <summary>Once per process: where to read from, the last delivery the watermark carried, and a burst to seed.</summary>
    public void Start(int cursor, string? cursorPublicId, DateTime? lastDeliveredAt, Burst? seed)
    {
        lock (_gate)
        {
            _cursor = cursor;
            _cursorPublicId = cursorPublicId;
            _lastDeliveredAt = lastDeliveredAt;
            _pending = seed;
            _started = true;
        }
    }

    /// <summary>Lines were read up to this one; each trigger among them joins the pending burst.</summary>
    public void Advance(int cursor, string cursorPublicId, IEnumerable<DateTime> triggers)
    {
        ArgumentNullException.ThrowIfNull(triggers);
        lock (_gate)
        {
            foreach (var at in triggers)
            {
                _pending = _pending is null ? new Burst(1, at, at) : _pending.With(at);
            }

            _cursor = cursor;
            _cursorPublicId = cursorPublicId;
        }
    }

    /// <summary>Moves the cursor to a line far ahead, with what lay between folded into one burst.</summary>
    public void Advance(int cursor, string cursorPublicId, Burst? folded)
    {
        lock (_gate)
        {
            if (folded is not null)
            {
                _pending = Merge(_pending, folded);
            }

            _cursor = cursor;
            _cursorPublicId = cursorPublicId;
        }
    }

    /// <summary>
    /// If nothing is in flight and the pending burst is due, marks one in flight, clears the burst, and hands back the
    /// request to send. The test of "due" is given the last delivery, read under the same lock.
    /// </summary>
    public bool TryHandOver(Func<Burst, DateTime?, bool> due, out NotificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(due);
        lock (_gate)
        {
            if (_inFlightId is not null || _pending is not { } burst || !due(burst, _lastDeliveredAt))
            {
                request = null!;
                return false;
            }

            _pending = null;
            request = new NotificationRequest(PublicId.New("ntf"), _cursorPublicId, _lastDeliveredAt, burst);
            _inFlightId = request.NotificationId;
            return true;
        }
    }

    /// <summary>
    /// The sender is done with this notification. The mark is cleared only if it is this one's: a notification handed
    /// over after this one was returned is still being sent.
    /// </summary>
    public void Release(string notificationId)
    {
        ArgumentNullException.ThrowIfNull(notificationId);
        lock (_gate)
        {
            if (string.Equals(_inFlightId, notificationId, StringComparison.Ordinal))
            {
                _inFlightId = null;
            }
        }
    }

    /// <summary>
    /// A hand-over that was not sent: the burst goes back, joined with whatever arrived since, so the next scan hands
    /// it over again rather than losing what it was about; and the mark is cleared if it is this notification's.
    /// </summary>
    public void Return(NotificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            _pending = Merge(_pending, request.Burst);
            if (string.Equals(_inFlightId, request.NotificationId, StringComparison.Ordinal))
            {
                _inFlightId = null;
            }
        }
    }

    /// <summary>A notification reached somebody at this time; the floor counts from here from now on.</summary>
    public void Delivered(DateTime at)
    {
        lock (_gate)
        {
            if (_lastDeliveredAt is not { } last || at > last)
            {
                _lastDeliveredAt = at;
            }
        }
    }

    private static Burst Merge(Burst? pending, Burst more) =>
        pending is null
            ? more
            : new Burst(
                pending.Lines + more.Lines,
                more.Oldest < pending.Oldest ? more.Oldest : pending.Oldest,
                more.Newest > pending.Newest ? more.Newest : pending.Newest);
}
