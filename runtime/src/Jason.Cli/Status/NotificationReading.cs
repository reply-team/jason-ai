using Jason.Contracts.Api;
using Jason.Contracts.Notifications;

namespace Jason.Cli.Status;

/// <summary>
/// How the CLI reads one notification channel from <c>system.info</c>, for <c>jason status</c> and for
/// <c>jason runtime status --human</c> alike. Whether the toast has a desktop is the answering runtime's own fact:
/// decided when it started, and found again by each of its own sends. A toast outcome an earlier runtime wrote never
/// outweighs it; one this runtime wrote is what happened.
/// </summary>
internal static class NotificationReading
{
    /// <summary>Why this toast has no desktop, or null when it has one or the channel is not the toast.</summary>
    /// <remarks>
    /// The runtime's presence where it says there is none. Where it says there is one, this runtime's own last send
    /// can still have found none — a Linux session bus with no notification daemon, an osascript that would not run —
    /// and that outcome, written since <paramref name="startedAt"/>, is said. A runtime older than presence is read from
    /// its last outcome alone.
    /// </remarks>
    public static string? NoDesktop(NotificationChannelInfo channel, DateTimeOffset startedAt)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (channel.Kind != ChannelKinds.Toast)
        {
            return null;
        }

        if (channel.Presence == ChannelPresence.NoDesktop)
        {
            return channel.PresenceDetail ?? "unknown";
        }

        return IsNoDesktop(channel) && (channel.Presence is null || Since(channel, startedAt)) ? channel.Detail ?? "unknown" : null;
    }

    /// <summary>
    /// The channel's last outcome as this runtime stands behind it, or null where nothing has been sent through the
    /// channel as it is now: a toast's "no desktop" written by an earlier runtime describes that process, not this one;
    /// and "off" on a channel that is on again was the person's own earlier choice, not something that happened since.
    /// </summary>
    public static string? Outcome(NotificationChannelInfo channel, DateTimeOffset startedAt)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (OnAgain(channel))
        {
            return null;
        }

        return channel.Kind == ChannelKinds.Toast && channel.Presence is not null && IsNoDesktop(channel) && !Since(channel, startedAt)
            ? null
            : channel.Outcome;
    }

    /// <summary>
    /// Whether the channel is on again after the person turned it off: its last line still says <c>off</c>. Whichever
    /// runtime wrote that line, it was the person's own choice, and nothing has been sent through the channel since.
    /// </summary>
    public static bool OnAgain(NotificationChannelInfo channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        return channel.On && channel.Outcome == ChannelResults.NotAttempted && channel.Reason == NotAttemptedReasons.Off;
    }

    /// <summary>
    /// Whether nothing has been sent through the channel since this runtime started, though a notification went out
    /// before it: the channel's last outcome is one this runtime does not stand behind — a toast's "no desktop" an
    /// earlier runtime wrote — or it has none at all beside a last notification older than this runtime. The second is
    /// what a restart hands over: a runtime does not carry an earlier one's "no desktop" into its own status, so its
    /// toast comes with no outcome. Either way that is not "nothing yet".
    /// </summary>
    public static bool Hidden(NotificationChannelInfo channel, DateTimeOffset startedAt, DateTimeOffset? lastAt)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (OnAgain(channel))
        {
            return false;
        }

        return channel.Outcome is null
            ? lastAt is { } last && last < startedAt
            : Outcome(channel, startedAt) is null;
    }

    private static bool IsNoDesktop(NotificationChannelInfo channel) =>
        channel.Outcome == ChannelResults.NotAttempted && channel.Reason == NotAttemptedReasons.NoDesktop;

    /// <summary>Whether the outcome was written by the answering runtime: at or after the moment it started.</summary>
    private static bool Since(NotificationChannelInfo channel, DateTimeOffset startedAt) => channel.At is { } at && at >= startedAt;
}
