using Jason.Contracts.Api;
using Jason.Contracts.Notifications;

namespace Jason.Cli.Human;

/// <summary>
/// <c>--human</c> rendering for the notification verbs: the channels as the runtime's report says them once the switch is
/// in force. A body that is not that report renders as null, and the runner prints it as it came.
/// </summary>
public static class NotificationRenderers
{
    /// <summary>
    /// Whether notifications are on as a whole, then one line per channel: its name, on or off, and what keeps it from
    /// reaching anybody — a plugin channel that could not deliver now, or a toast with no desktop or one the operating
    /// system did not show, which is the person's own switch there and not this one.
    /// </summary>
    public static string? Channels(string json)
    {
        var report = RenderText.Read<NotificationsInfo>(json);
        if (report?.Channels is null)
        {
            return null;
        }

        var width = report.Channels.Select(channel => channel.Channel.Length).DefaultIfEmpty(0).Max() + 3;
        return RenderText.Lines(
        [
            report.Enabled ? "Notifications: on" : "Notifications: off (Notifications:Enabled is false in the settings file)",
            .. report.Channels.Select(channel => (channel.Channel.PadRight(width) + (channel.On ? "on" : "off").PadRight(5) + Note(channel)).TrimEnd()),
        ]);
    }

    private static string Note(NotificationChannelInfo channel) =>
        channel.Readiness is { } readiness && readiness != ChannelReadiness.Ready
            ? channel.ReadinessDetail is { } missing ? $"cannot deliver: {readiness} ({missing})" : $"cannot deliver: {readiness}"
            : channel.Presence == ChannelPresence.NoDesktop
                ? channel.PresenceDetail is { } why ? $"no desktop to show it on ({why})" : "no desktop to show it on"
                : channel.Outcome == ChannelResults.Silenced
                    ? $"the operating system did not show the last one ({channel.Reason})"
                    : string.Empty;
}
