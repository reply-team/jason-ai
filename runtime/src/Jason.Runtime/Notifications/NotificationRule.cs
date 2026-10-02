using Jason.Runtime.Configuration;

namespace Jason.Runtime.Notifications;

/// <summary>
/// When a burst of things that need a person has settled enough to say so once. Every input is a timestamp from the
/// journal or the runtime's own start — durable state, read at a scan — so a sign-out and the next sign-in continue
/// the same coalescing, and nothing here is an in-process timer that a sign-out would kill.
/// </summary>
public static class NotificationRule
{
    /// <summary>
    /// The quiet window in force: the configured one, but never less than two scans, because a window shorter than
    /// the scan could not be seen to have passed.
    /// </summary>
    public static TimeSpan EffectiveQuiet(NotificationsOptions notifications, DispatcherOptions dispatcher)
    {
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(dispatcher);
        return TimeSpan.FromSeconds(Math.Max(notifications.QuietSeconds, 2 * dispatcher.TickSeconds));
    }

    /// <summary>
    /// (a) the newest line is a quiet window old, or (b) the oldest has waited the cap; and (c) the floor since the
    /// last notification that reached anybody has passed; and (d) a quiet window has passed since this runtime started.
    /// </summary>
    public static bool IsDue(Burst burst, DateTime now, DateTime startedAt, DateTime? lastDeliveredAt, TimeSpan quiet, TimeSpan maxWait, TimeSpan minInterval)
    {
        ArgumentNullException.ThrowIfNull(burst);
        var settled = now - burst.Newest >= quiet || now - burst.Oldest >= maxWait;
        var spaced = lastDeliveredAt is not { } last || now - last >= minInterval;
        var graced = now - startedAt >= quiet;
        return settled && spaced && graced;
    }
}

/// <summary>The lines read since the last notification that something needs a person: how many, and when the first and last were written.</summary>
public sealed record Burst(int Lines, DateTime Oldest, DateTime Newest)
{
    public Burst With(DateTime at) => new(Lines + 1, at < Oldest ? at : Oldest, at > Newest ? at : Newest);
}
