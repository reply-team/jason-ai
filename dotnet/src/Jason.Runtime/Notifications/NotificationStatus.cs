using Jason.Contracts.Api;
using Jason.Contracts.Notifications;
using Jason.Runtime.Notifications.Desktop;

namespace Jason.Runtime.Notifications;

/// <summary>
/// What <c>system.info</c> says about notifications: the last one this runtime knows of, and what each channel did
/// with the last notification it was given. Seeded at start from the last line, so a restart does not forget.
/// </summary>
public sealed class NotificationStatus
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, ChannelStatus> _channels = new(StringComparer.Ordinal);
    private string? _lastNotificationId;
    private DateTimeOffset? _lastNotificationAt;
    private DateTimeOffset? _nothingWaitedAt;

    /// <summary>A notification was sent, and this line records what happened to it.</summary>
    public void Record(NotificationLine line, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(line);
        lock (_gate)
        {
            Keep(line, at);
        }
    }

    /// <summary>The last line read at start, which is what the status was before this process existed.</summary>
    public void Seed(NotificationLine line, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(line);
        lock (_gate)
        {
            if (_lastNotificationAt is null)
            {
                Keep(line, at, seeding: true);
            }
        }
    }

    /// <summary>A notification was due and nothing was waiting by the time it was sent, so nothing was said.</summary>
    public void NothingWaited(DateTimeOffset at)
    {
        lock (_gate)
        {
            _nothingWaitedAt = at;
        }
    }

    private void Keep(NotificationLine line, DateTimeOffset at, bool seeding = false)
    {
        _lastNotificationId = line.NotificationId;
        _lastNotificationAt = at;
        foreach (var outcome in line.Channels)
        {
            // Whether there is a desktop is a property of the process that tried, and this process decided its own
            // when it started: an earlier runtime's "no desktop" would contradict it, so it is not carried over.
            if (seeding && outcome.Kind == ChannelKinds.Toast && outcome.Result == ChannelResult.NotAttempted && outcome.Reason == NotAttemptedReasons.NoDesktop)
            {
                continue;
            }

            _channels[outcome.Channel] = new ChannelStatus(outcome, at);
        }
    }

    /// <summary>
    /// What <c>system.info</c> answers: notifications on or off, the toast and every named plugin with whether it is on
    /// and its last outcome, and — for the toast — whether there is a desktop, decided when the runtime started.
    /// </summary>
    public NotificationsInfo Info(NotificationsOptions options, Presence presence)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(presence);
        var snapshot = Snapshot();
        var last = snapshot.Channels.ToDictionary(channel => channel.Outcome.Channel, StringComparer.Ordinal);

        NotificationChannelInfo Channel(string name, string kind, bool on, string? presenceWord, string? presenceDetail) =>
            last.TryGetValue(name, out var status)
                ? new NotificationChannelInfo(
                    name,
                    kind,
                    on,
                    ChannelOutcome.Word(status.Outcome.Result),
                    status.Outcome.Reason,
                    status.Outcome.Code,
                    status.Outcome.Class?.ToString().ToLowerInvariant(),
                    status.Outcome.Detail,
                    status.At,
                    presenceWord,
                    presenceDetail)
                : new NotificationChannelInfo(name, kind, on, null, null, null, null, null, null, presenceWord, presenceDetail);

        return new NotificationsInfo(
            options.Enabled,
            [
                Channel(
                    "toast",
                    ChannelKinds.Toast,
                    options.Enabled && options.Toast,
                    presence.Available ? ChannelPresence.Available : ChannelPresence.NoDesktop,
                    presence.Detail),
                .. options.Plugins
                    .OrderBy(plugin => plugin.Key, StringComparer.Ordinal)
                    .Select(plugin => Channel(plugin.Key, ChannelKinds.Plugin, options.Enabled && plugin.Value.Enabled, null, null)),
            ],
            snapshot.LastNotificationId,
            snapshot.LastNotificationAt);
    }

    public NotificationStatusSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new NotificationStatusSnapshot(_lastNotificationId, _lastNotificationAt, _nothingWaitedAt, [.. _channels.Values]);
        }
    }
}

/// <summary>One channel's last outcome, and when.</summary>
public sealed record ChannelStatus(ChannelOutcome Outcome, DateTimeOffset At);

/// <summary>What the status held at one moment.</summary>
public sealed record NotificationStatusSnapshot(
    string? LastNotificationId,
    DateTimeOffset? LastNotificationAt,
    DateTimeOffset? NothingWaitedAt,
    IReadOnlyList<ChannelStatus> Channels);
