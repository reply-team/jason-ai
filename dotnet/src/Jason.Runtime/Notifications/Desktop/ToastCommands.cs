using System.Globalization;
using System.Text;
using System.Text.Json;
using Jason.Contracts.Notifications;
using Jason.Contracts.Plugins;

namespace Jason.Runtime.Notifications.Desktop;

/// <summary>One toast launch: the command, what goes on its stdin, and the environment it runs in.</summary>
public sealed record ToastLaunch(IReadOnlyList<string> Command, string? Stdin, IReadOnlyDictionary<string, string> Environment)
{
    /// <summary>
    /// Whether ending the launch at its bound, or at the runtime's stop, ends what it started too: true for every toast, false
    /// for a click's handler, under which xdg-open may run the Jason Desktop the click asked for.
    /// </summary>
    public bool KillsTree { get; init; } = true;
}

/// <summary>Which toast a notify-send can show: one that waits for its click, and whether it hands on the click's activation token.</summary>
public sealed record NotifySendForm(bool Waits, bool Token);

/// <summary>
/// The exact command a toast is on each platform. Composed here and asserted by tests, never shown by one: on Windows
/// it is this executable's own <c>toast-host</c> mode with the payload on stdin, on macOS <c>osascript</c>, and on
/// Linux <c>notify-send</c>, or <c>gdbus</c> where it is absent. Nothing reaches a notification but the pointer
/// itself, which is counts and where to look: on Linux the item a click opens stays with the runtime, and reaches only
/// the command a click starts, this executable's handler of the toast's <c>jason:</c> link.
/// </summary>
public static class ToastCommands
{
    /// <summary>osascript, where macOS keeps it: every toast is started by the full path of its program.</summary>
    public const string Osascript = "/usr/bin/osascript";

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
        return new ToastLaunch([Osascript, "-e", script], null, Empty);
    }

    /// <summary>The action a click on the notification's body sends: the name every notification server takes as the default.</summary>
    public const string DefaultAction = "default";

    /// <summary>notify-send's own options, as its help names them: what each form of the toast needs.</summary>
    public static IReadOnlyList<string> WaitingOptions { get; } = ["--action", "--wait", "--print-id"];

    /// <summary>libnotify 0.8.8's option that writes the activation token a click came with to a file descriptor.</summary>
    public const string TokenOption = "--activation-token-fd";

    /// <summary>notify-send's help, which names the options this notify-send takes.</summary>
    public static ToastLaunch LinuxNotifySendHelp(string program) => new([program, "--help"], null, Empty);

    /// <summary>
    /// Which toast this notify-send can show, read from its help by the options' names, which are its interface: one that
    /// waits for its click where it takes the three options that needs (libnotify 0.7.10 and later), passing the click's
    /// activation token on where it takes <see cref="TokenOption"/> (0.8.8 and later); the plain toast otherwise.
    /// </summary>
    public static NotifySendForm LinuxForm(string help)
    {
        ArgumentNullException.ThrowIfNull(help);
        static bool Names(string help, string option) =>
            help.Split((char[])[' ', '\n', '\t', ',', '='], StringSplitOptions.RemoveEmptyEntries).Contains(option, StringComparer.Ordinal);
        return new(WaitingOptions.All(option => Names(help, option)), Names(help, TokenOption));
    }

    /// <summary>
    /// The toast on Linux, clickable: notify-send with a default action, waiting on the bus until the person clicks the
    /// notification or dismisses it (GNOME and KDE deliver a click only to a sender still there), printing the
    /// notification's id once it is shown and the action's name on a click; with <paramref name="token"/>, the activation
    /// token the server gave with the click on a line between them, written to its stdout by its own option.
    /// </summary>
    public static ToastLaunch LinuxNotifySend(string program, NotificationMessage message, bool token)
    {
        ArgumentNullException.ThrowIfNull(message);
        string[] tokenOption = token ? [TokenOption + "=1"] : [];
        return new ToastLaunch(
            [program, "--app-name=" + message.Title, $"--action={DefaultAction}=Open", "--wait", "--print-id", .. tokenOption, message.Title, message.Summary + "\n" + message.Look],
            null,
            Empty);
    }

    /// <summary>The toast as notify-send before 0.7.10 takes it, with no action, no wait and no id: shown, and no click reaches it.</summary>
    public static ToastLaunch LinuxNotifySendPlain(string program, NotificationMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return LinuxSaid(program, message.Title, message.Summary + "\n" + message.Look);
    }

    /// <summary>A notification that only says something, with no action: why a click opened nothing.</summary>
    public static ToastLaunch LinuxSaid(string program, string title, string text) => new([program, "--app-name=" + title, title, text], null, Empty);

    /// <summary>
    /// What a click on a Linux toast starts: this executable with the toast's <c>jason:</c> link and nothing else, as the
    /// shell starts it for a Windows toast's click, which opens Jason Desktop there or says why it did not. Where the
    /// runtime runs in a systemd unit, inside a scope of its own (<c>systemd-run --user --scope</c>), as a desktop starts an
    /// application: else Jason Desktop, started from it, would be in the unit's group and end with the runtime.
    /// </summary>
    public static IReadOnlyList<string> LinuxClick(IReadOnlyList<string> self, string link, string? systemdRun)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentException.ThrowIfNullOrEmpty(link);
        // A scope's description is its command unless one is given, and the user journal keeps it: the link names the item.
        return systemdRun is null ? [.. self, link] : [systemdRun, "--user", "--scope", "--quiet", "--collect", "--description=Jason Desktop, from a notification", "--", .. self, link];
    }

    /// <summary>The session's environment as the user manager holds it now, which a desktop fills once its session has started.</summary>
    public static ToastLaunch LinuxSessionEnvironment(string systemctl) => new([systemctl, "--user", "show-environment"], null, Empty);

    /// <summary>
    /// The assignments <c>systemctl --user show-environment</c> printed: a value as it stands, or, where systemd quoted it as
    /// <c>$'…'</c>, with the escapes that quoting uses undone; a line that is no assignment is left out.
    /// </summary>
    public static Dictionary<string, string> SessionEnvironment(string stdout)
    {
        ArgumentNullException.ThrowIfNull(stdout);
        var read = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in Lines(stdout))
        {
            var equals = line.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0 || !line[..equals].All(character => char.IsAsciiLetterOrDigit(character) || character == '_') || char.IsAsciiDigit(line[0]))
            {
                continue;
            }

            var value = line[(equals + 1)..];
            read[line[..equals]] = value.StartsWith("$'", StringComparison.Ordinal) && value.EndsWith('\'') && value.Length >= 3 ? Unquoted(value[2..^1]) : value;
        }

        return read;
    }

    /// <summary>
    /// The environment a click's handler runs in: the runtime's own with the session's as it stands now laid over it, and
    /// without what systemd sets for a unit's processes, as a launcher starts an application, so a runtime started at
    /// logon before the session said its display still hands the handler one; and an activation token only where the click
    /// brought a fresh one, since a token either environment kept from its own start has long been used or expired.
    /// </summary>
    public static Dictionary<string, string> ClickEnvironment(IReadOnlyDictionary<string, string> runtime, IReadOnlyDictionary<string, string> session, string? token)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(session);
        var environment = new Dictionary<string, string>(runtime, StringComparer.Ordinal);
        foreach (var (name, value) in session)
        {
            environment[name] = value;
        }

        foreach (var name in UnitNames)
        {
            environment.Remove(name);
        }

        environment.Remove("XDG_ACTIVATION_TOKEN");
        environment.Remove("DESKTOP_STARTUP_ID");
        if (token is not null)
        {
            environment["XDG_ACTIVATION_TOKEN"] = token;
        }

        return environment;
    }

    /// <summary>
    /// What systemd gives the processes of a unit it runs, the runtime's logon unit among them, and a launcher never gives
    /// an application (systemd.exec(5), "Environment Variables Set or Propagated by the Service Manager"): a Jason Desktop
    /// that carried them would take itself for part of that unit, and GLib's memory monitor would watch the unit's cgroup.
    /// </summary>
    private static readonly string[] UnitNames =
    [
        "INVOCATION_ID", "JOURNAL_STREAM", "LOG_NAMESPACE", "MAINPID", "MANAGERPID", "SYSTEMD_EXEC_PID", "NOTIFY_SOCKET",
        "LISTEN_PID", "LISTEN_FDS", "LISTEN_FDNAMES", "WATCHDOG_PID", "WATCHDOG_USEC", "FDSTORE", "PIDFILE",
        "MEMORY_PRESSURE_WATCH", "MEMORY_PRESSURE_WRITE", "CREDENTIALS_DIRECTORY", "RUNTIME_DIRECTORY", "STATE_DIRECTORY",
        "CACHE_DIRECTORY", "LOGS_DIRECTORY", "CONFIGURATION_DIRECTORY",
    ];

    /// <summary>The inside of a <c>$'…'</c> value with its backslash escapes undone; an escape it does not know is kept as written.</summary>
    private static string Unquoted(string quoted)
    {
        var value = new StringBuilder(quoted.Length);
        for (var at = 0; at < quoted.Length; at++)
        {
            if (quoted[at] != '\\' || at + 1 == quoted.Length)
            {
                value.Append(quoted[at]);
                continue;
            }

            var next = quoted[++at];
            switch (next)
            {
                case 'n': value.Append('\n'); break;
                case 't': value.Append('\t'); break;
                case 'r': value.Append('\r'); break;
                case '\\' or '\'' or '"': value.Append(next); break;
                case 'x' when at + 2 < quoted.Length && int.TryParse(quoted.AsSpan(at + 1, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var code):
                    value.Append((char)code);
                    at += 2;
                    break;
                default: value.Append('\\').Append(next); break;
            }
        }

        return value.ToString();
    }

    /// <summary>The id notify-send printed once it had shown the notification, its first line of digits; null where none is there yet.</summary>
    public static string? ShownId(string stdout) => Lines(stdout).FirstOrDefault(IsId);

    /// <summary>Whether notify-send printed that the person clicked the notification.</summary>
    public static bool Clicked(string stdout) => Lines(stdout).Contains(DefaultAction, StringComparer.Ordinal);

    /// <summary>
    /// The activation token the server gave with the click: the one line after the id that is not the action, which
    /// <see cref="TokenOption"/> writes straight to the descriptor while the action may still wait in notify-send's buffer,
    /// so it comes before the action or after it; null where there is none, the line is empty, or it is not a token: a short
    /// run of printable characters with no space.
    /// </summary>
    public static string? ActivationToken(string stdout)
    {
        var lines = Lines(stdout).ToList();
        var id = lines.FindIndex(IsId);
        if (id < 0 || !lines.Contains(DefaultAction))
        {
            return null;
        }

        var rest = lines.Skip(id + 1).Where(line => line.Length > 0 && line != DefaultAction).ToList();
        return rest is [var token] && token.Length <= 512 && token.All(character => character is > ' ' and < '\u007f') ? token : null;
    }

    private static bool IsId(string line) => line.Length is > 0 and <= 10 && line.All(char.IsAsciiDigit);

    private static IEnumerable<string> Lines(string text) => text.Split('\n').Select(line => line.TrimEnd('\r'));

    public static ToastLaunch LinuxGdbus(string program, NotificationMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return new ToastLaunch(
            [
                program, "call", "--session",
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
    public static ToastLaunch LinuxDaemonProbe(string gdbus) => new(
        [
            gdbus, "call", "--session",
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
    public static ToastLaunch LinuxActivatableProbe(string gdbus) => new(
        [
            gdbus, "call", "--session",
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
