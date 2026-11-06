using System.Globalization;
using System.Text;
using System.Text.Json;
using Jason.Contracts.Notifications;
using Jason.Contracts.Plugins;

namespace Jason.Runtime.Notifications.Desktop;

/// <summary>One toast launch: the command, what goes on its stdin, and the environment it runs in.</summary>
public sealed record ToastLaunch(IReadOnlyList<string> Command, string? Stdin, IReadOnlyDictionary<string, string> Environment);

/// <summary>
/// The exact command a toast is on each platform. Composed here and asserted by tests, never shown by one: on Windows
/// it is this executable's own <c>toast-host</c> mode with the payload on stdin, on macOS <c>osascript</c>, and on
/// Linux <c>notify-send</c>, or <c>gdbus</c> where it is absent. Nothing reaches a command line but the pointer
/// itself, which is counts and where to look.
/// </summary>
public static class ToastCommands
{
    /// <summary>The protocol version the toast host reads.</summary>
    public const int Protocol = 1;

    /// <summary>The names a Linux child needs to reach the user's notification daemon over their session bus.</summary>
    public static IReadOnlyList<string> LinuxDesktopNames { get; } = ["DBUS_SESSION_BUS_ADDRESS", "XDG_RUNTIME_DIR", "DISPLAY", "WAYLAND_DISPLAY"];

    /// <param name="self">How this executable is started: the same command the plugin host is started with.</param>
    public static ToastLaunch Windows(IReadOnlyList<string> self, NotificationMessage message)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(message);
        var payload = new ToastHostPayload(Protocol, ToastRegistration.Aumid, ToastRegistration.DisplayName, message.Title, [message.Summary, message.Look], message.Item);
        return new ToastLaunch([.. self, "toast-host", "--protocol", Protocol.ToString(CultureInfo.InvariantCulture)], JsonSerializer.Serialize(payload, ToastHostJson.Options), Empty);
    }

    public static ToastLaunch MacOs(NotificationMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var script = $"display notification \"{AppleScript(message.Summary + " " + message.Look)}\" with title \"{AppleScript(message.Title)}\"";
        return new ToastLaunch(["osascript", "-e", script], null, Empty);
    }

    public static ToastLaunch LinuxNotifySend(NotificationMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return new ToastLaunch(["notify-send", "--app-name=" + message.Title, message.Title, message.Summary + "\n" + message.Look], null, Empty);
    }

    public static ToastLaunch LinuxGdbus(NotificationMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return new ToastLaunch(
            [
                "gdbus", "call", "--session",
                "--dest", "org.freedesktop.Notifications",
                "--object-path", "/org/freedesktop/Notifications",
                "--method", "org.freedesktop.Notifications.Notify",
                GVariant(message.Title), "uint32 0", "''", GVariant(message.Title), GVariant(message.Summary + "\n" + message.Look),
                "@as []", "@a{sv} {}", "int32 -1",
            ],
            null,
            Empty);
    }

    /// <summary>Whether a notification daemon owns its name on the session bus; it answers <c>(true,)</c> or <c>(false,)</c>.</summary>
    public static ToastLaunch LinuxDaemonProbe() => new(
        [
            "gdbus", "call", "--session",
            "--dest", "org.freedesktop.DBus",
            "--object-path", "/org/freedesktop/DBus",
            "--method", "org.freedesktop.DBus.NameHasOwner",
            "org.freedesktop.Notifications",
        ],
        null,
        Empty);

    /// <summary>
    /// The names the bus can start on demand. A daemon such as dunst, mako or MATE's owns its name only after its first
    /// notification; listed here, it is there to be told, and the bus starts it.
    /// </summary>
    public static ToastLaunch LinuxActivatableProbe() => new(
        [
            "gdbus", "call", "--session",
            "--dest", "org.freedesktop.DBus",
            "--object-path", "/org/freedesktop/DBus",
            "--method", "org.freedesktop.DBus.ListActivatableNames",
        ],
        null,
        Empty);

    /// <summary>
    /// The child's environment: the base the plugin host's child gets — so neither the capability token nor the data
    /// directory, nor any other <c>JASON_</c> name — plus, on Linux, what reaching the notification daemon needs.
    /// </summary>
    public static Dictionary<string, string> Environment(IDictionary<string, string> source, DesktopPlatform platform) =>
        BaseEnvironment.Build(source, platform == DesktopPlatform.Linux ? LinuxDesktopNames : []);

    private static readonly Dictionary<string, string> Empty = [];

    /// <summary>A string inside AppleScript's double quotes: a backslash and a quote are each escaped with a backslash.</summary>
    private static string AppleScript(string text) => text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    /// <summary>A GVariant text-format string: single-quoted, with a backslash and a single quote each escaped.</summary>
    private static string GVariant(string text)
    {
        var quoted = new StringBuilder(text.Length + 2).Append('\'');
        foreach (var character in text)
        {
            if (character is '\\' or '\'')
            {
                quoted.Append('\\');
            }

            quoted.Append(character);
        }

        return quoted.Append('\'').ToString();
    }
}
