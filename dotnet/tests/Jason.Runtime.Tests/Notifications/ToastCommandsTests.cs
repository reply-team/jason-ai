using Jason.Contracts.Notifications;
using Jason.Runtime.Notifications;
using Jason.Runtime.Notifications.Desktop;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Notifications;

/// <summary>The exact command a toast is on each platform, and the environment it runs in: data, composed and asserted.</summary>
public sealed class ToastCommandsTests
{
    private static readonly NotificationMessage Message = NotificationComposer.Compose(3, 1);

    /// <summary>The protocol the toast host answers, by its constant: no test names it as a literal beside the mode.</summary>
    private static readonly string Protocol = ToastCommands.Protocol.ToString(System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void Windows_runs_this_executable_s_toast_host_mode_with_the_payload_on_stdin()
    {
        var launch = ToastCommands.Windows(["C:\\jason\\jason.exe"], Message);

        Assert.Equal(["C:\\jason\\jason.exe", "toast-host", "--protocol", Protocol], launch.Command);
        Assert.Equal(
            """{"protocol_version":1,"aumid":"ai.jason.runtime","display_name":"Jason","title":"Jason","lines":["3 approvals and 1 question are waiting in Jason.","Ask your agent what Jason is waiting for, or run jason approval list and jason decision list."]}""",
            launch.Stdin);
    }

    /// <summary>
    /// The one item that waits travels by its opaque id, and nothing else of it; a toast with no item is the payload it
    /// was before there was one, byte for byte.
    /// </summary>
    [Fact]
    public void Windows_carries_the_one_item_by_its_id_alone()
    {
        const string Approval = "apr_01JBZ3X7Q9K2M4N6P8R0S2T4V6";
        var one = NotificationComposer.Compose(1, 0, item: Approval);

        var launch = ToastCommands.Windows(["C:\\jason\\jason.exe"], one);

        Assert.Equal(
            """{"protocol_version":1,"aumid":"ai.jason.runtime","display_name":"Jason","title":"Jason","lines":["1 approval is waiting in Jason.","Ask your agent what Jason is waiting for, or run jason approval list and jason decision list."],"item":"apr_01JBZ3X7Q9K2M4N6P8R0S2T4V6"}""",
            launch.Stdin);
        Assert.DoesNotContain("\"item\"", ToastCommands.Windows(["C:\\jason\\jason.exe"], Message).Stdin, StringComparison.Ordinal);
    }

    /// <summary>
    /// The item that waits is never in a notification on macOS or Linux: a message that carries one is said there by its
    /// counts and where to look alone, in no argument, on no stdin and in no value of its environment. On Linux it reaches
    /// only the command a click starts (<see cref="Linux_a_click_starts_this_executable_with_the_toast_s_link"/>).
    /// </summary>
    [Theory]
    [InlineData("osascript")]
    [InlineData("notify-send")]
    [InlineData("notify-send plain")]
    [InlineData("gdbus")]
    public void A_message_carrying_an_item_puts_no_id_in_a_mac_os_or_linux_toast(string command)
    {
        var one = NotificationComposer.Compose(1, 0, item: "apr_01JBZ3X7Q9K2M4N6P8R0S2T4V6");
        Assert.Equal("apr_01JBZ3X7Q9K2M4N6P8R0S2T4V6", one.Item);

        var launch = command switch
        {
            "osascript" => ToastCommands.MacOs(one),
            "notify-send" => ToastCommands.LinuxNotifySend("/usr/bin/notify-send", one, token: true),
            "notify-send plain" => ToastCommands.LinuxNotifySendPlain("/usr/bin/notify-send", one),
            _ => ToastCommands.LinuxGdbus("/usr/bin/gdbus", one),
        };

        Assert.Equal(command.Split(' ')[0], Path.GetFileName(launch.Command[0]));
        Assert.All(launch.Command, argument => Assert.DoesNotContain("apr_", argument, StringComparison.Ordinal));
        Assert.DoesNotContain("apr_", launch.Stdin ?? string.Empty, StringComparison.Ordinal);
        Assert.All(launch.Environment.Values, value => Assert.DoesNotContain("apr_", value, StringComparison.Ordinal));
    }

    /// <summary>The same executable may be a dll run by dotnet, as it is under test: the mode follows whatever starts it.</summary>
    [Fact]
    public void Windows_follows_the_command_that_starts_this_executable() =>
        Assert.Equal(["dotnet", "jason.dll", "toast-host", "--protocol", Protocol], ToastCommands.Windows(["dotnet", "jason.dll"], Message).Command);

    /// <summary>
    /// Every toast is started by the full path of its program: on Windows this executable as the operating system names
    /// it, on macOS osascript where macOS keeps it, and on Linux notify-send or gdbus as the search path's full entries
    /// gave them, handed in rather than named again.
    /// </summary>
    [Fact]
    public void Every_toast_is_started_by_the_full_path_of_its_program()
    {
        Assert.True(Path.IsPathFullyQualified(ToastCommands.Windows(new ProcessPathLocator().Command, Message).Command[0]));
        Assert.Equal("/usr/bin/osascript", ToastCommands.MacOs(Message).Command[0]);
        Assert.Equal("/opt/tools/notify-send", ToastCommands.LinuxNotifySend("/opt/tools/notify-send", Message, token: true).Command[0]);
        Assert.Equal("/opt/tools/notify-send", ToastCommands.LinuxNotifySendHelp("/opt/tools/notify-send").Command[0]);
        Assert.Equal("/opt/tools/notify-send", ToastCommands.LinuxNotifySendPlain("/opt/tools/notify-send", Message).Command[0]);
        Assert.Equal("/opt/tools/notify-send", ToastCommands.LinuxSaid("/opt/tools/notify-send", "Jason", "words").Command[0]);
        Assert.Equal("/usr/bin/systemd-run", ToastCommands.LinuxClick(["/opt/jason/jason"], AttentionLinks.List, "/usr/bin/systemd-run")[0]);
        Assert.Equal("/opt/jason/jason", ToastCommands.LinuxClick(["/opt/jason/jason"], AttentionLinks.List, null)[0]);
        foreach (var launch in new[] { ToastCommands.LinuxGdbus("/opt/tools/gdbus", Message), ToastCommands.LinuxDaemonProbe("/opt/tools/gdbus"), ToastCommands.LinuxActivatableProbe("/opt/tools/gdbus") })
        {
            Assert.Equal("/opt/tools/gdbus", launch.Command[0]);
        }
    }

    [Fact]
    public void Mac_os_says_it_with_osascript()
    {
        var launch = ToastCommands.MacOs(Message);

        Assert.Equal(
            ["/usr/bin/osascript", "-e", $"display notification \"{Message.Summary} {Message.Look}\" with title \"Jason\""],
            launch.Command);
        Assert.Null(launch.Stdin);
    }

    /// <summary>The composer never makes a quote or a backslash; the escaping rule is held anyway.</summary>
    [Fact]
    [Critical(Family.X3, "no injection into osascript")]
    public void Apple_script_s_quote_and_backslash_are_escaped()
    {
        var odd = new NotificationMessage("Ja\"son", "a \"b\" c\\d", "e", 1, 0);
        Assert.Equal(
            "display notification \"a \\\"b\\\" c\\\\d e\" with title \"Ja\\\"son\"",
            ToastCommands.MacOs(odd).Command[2]);
    }

    /// <summary>
    /// On Linux notify-send shows the toast with a default action and stays on the bus until it is clicked or dismissed,
    /// printing its id once shown and the action on a click; where it takes libnotify 0.8.8's option, the activation token
    /// the click came with on a line of its stdout between them. Breaks if the sender leaves at once, which GNOME and KDE
    /// answer by delivering no click, or the click comes back by no name the channel reads.
    /// </summary>
    [Fact]
    public void Linux_says_it_with_notify_send_which_waits_for_the_click()
    {
        Assert.Equal(
            ["/usr/bin/notify-send", "--app-name=Jason", "--action=default=Open", "--wait", "--print-id", "Jason", $"{Message.Summary}\n{Message.Look}"],
            ToastCommands.LinuxNotifySend("/usr/bin/notify-send", Message, token: false).Command);
        Assert.Equal(
            ["/usr/bin/notify-send", "--app-name=Jason", "--action=default=Open", "--wait", "--print-id", "--activation-token-fd=1", "Jason", $"{Message.Summary}\n{Message.Look}"],
            ToastCommands.LinuxNotifySend("/usr/bin/notify-send", Message, token: true).Command);
        Assert.Empty(ToastCommands.LinuxNotifySend("/usr/bin/notify-send", Message, token: true).Environment);
        Assert.Equal(
            ["/usr/bin/notify-send", "--app-name=Jason", "Jason", $"{Message.Summary}\n{Message.Look}"],
            ToastCommands.LinuxNotifySendPlain("/usr/bin/notify-send", Message).Command);
        Assert.Equal(["/usr/bin/notify-send", "--help"], ToastCommands.LinuxNotifySendHelp("/usr/bin/notify-send").Command);
    }

    /// <summary>
    /// notify-send's help says by its options' names which toast it can show: libnotify 0.8.9's waits and hands on the
    /// token, 0.8.3's waits without it, and 0.7.9's, which takes none of the three, is the plain toast. Breaks if an older
    /// notify-send is given options it refuses, which would show nothing at all.
    /// </summary>
    [Theory]
    [InlineData("0.8.9", true, true)]
    [InlineData("0.8.3", true, false)]
    [InlineData("0.7.9", false, false)]
    public void Notify_send_s_help_says_which_toast_it_shows(string version, bool waits, bool token) =>
        Assert.Equal(new NotifySendForm(waits, token), ToastCommands.LinuxForm(Help(version)));

    /// <summary>notify-send's help as each libnotify prints it, by its options; the words beside them are its own.</summary>
    internal static string Help(string version)
    {
        var options = new List<string>
        {
            "Usage:",
            "  notify-send [OPTION…] <SUMMARY> [BODY] - create a notification",
            string.Empty,
            "Help Options:",
            "  -?, --help                        Show help options",
            string.Empty,
            "Application Options:",
            "  -u, --urgency=LEVEL               Specifies the urgency level (low, normal, critical).",
            "  -t, --expire-time=TIME            Specifies the timeout in milliseconds at which to expire the notification.",
            "  -a, --app-name=APP_NAME           Specifies the app name for the icon",
            "  -h, --hint=TYPE:NAME:VALUE        Specifies basic extra data to pass.",
        };
        if (version != "0.7.9")
        {
            options.Add("  -p, --print-id                    Print the notification ID.");
            options.Add("  -w, --wait                        Wait for the notification to be closed before exiting.");
            options.Add("  -A, --action=[NAME=]Text...       Specifies the actions to display to the user. Implies --wait to wait for user input.");
        }

        if (version == "0.8.9")
        {
            options.Add("  --activation-token-fd=FD          Write the activation token to the file descriptor");
        }

        options.Add("  -v, --version                     Version of the package.");
        return string.Join('\n', options) + "\n";
    }

    /// <summary>
    /// A click starts this executable with the toast's own <c>jason:</c> link and nothing else, as the shell starts it for a
    /// Windows toast; where the runtime runs in a systemd unit, inside a scope of its own, so the Jason Desktop it starts
    /// does not end with the runtime's unit. Breaks if the click names anything but a link of the table, or starts the
    /// desktop inside the runtime's unit.
    /// </summary>
    [Fact]
    public void Linux_a_click_starts_this_executable_with_the_toast_s_link()
    {
        var link = AttentionLinks.Item("apr_01JBZ3X7Q9K2M4N6P8R0S2T4V6")!;

        Assert.Equal(["/opt/jason/jason", "jason://attention/approvals/apr_01JBZ3X7Q9K2M4N6P8R0S2T4V6"], ToastCommands.LinuxClick(["/opt/jason/jason"], link, null));
        Assert.Equal(
            ["/usr/bin/systemd-run", "--user", "--scope", "--quiet", "--collect", "--", "/usr/bin/dotnet", "jason.dll", "jason://attention"],
            ToastCommands.LinuxClick(["/usr/bin/dotnet", "jason.dll"], AttentionLinks.List, "/usr/bin/systemd-run"));
        Assert.NotNull(AttentionLinks.Parse(ToastCommands.LinuxClick(["/opt/jason/jason"], link, null)[^1]));
    }

    /// <summary>
    /// The session's environment as the user manager holds it now, read from <c>systemctl --user show-environment</c>: a
    /// plain value as it is, one systemd quotes as <c>$'…'</c> with its escapes undone, and a line that is no assignment
    /// left out. Breaks if a click's handler is given the environment a runtime started at logon had, before the session
    /// said its display, which leaves the handler with no display and the click with nothing opened.
    /// </summary>
    [Fact]
    public void The_session_s_environment_is_read_as_systemctl_prints_it()
    {
        Assert.Equal(["/usr/bin/systemctl", "--user", "show-environment"], ToastCommands.LinuxSessionEnvironment("/usr/bin/systemctl").Command);

        var read = ToastCommands.SessionEnvironment("WAYLAND_DISPLAY=wayland-1\nXDG_CURRENT_DESKTOP=Hyprland\nNAME=$'two words\\tand \\'quote\\' \\\\ \\x41'\nnot an assignment\nEMPTY=\n");

        Assert.Equal(
            new Dictionary<string, string>
            {
                ["WAYLAND_DISPLAY"] = "wayland-1",
                ["XDG_CURRENT_DESKTOP"] = "Hyprland",
                ["NAME"] = "two words\tand 'quote' \\ A",
                ["EMPTY"] = string.Empty,
            },
            read);
    }

    /// <summary>
    /// The click's handler runs in the runtime's environment with the session's as it stands now laid over it, as a
    /// launcher starts an application, and with an activation token only where the click brought a fresh one: a token the
    /// session's own environment kept from its start is stale and never travels. Breaks if a stale token is handed on, or
    /// the session's display does not reach the handler.
    /// </summary>
    [Theory]
    [InlineData("fresh-token")]
    [InlineData(null)]
    public void A_click_s_environment_is_the_session_s_now_with_only_a_fresh_token(string? token)
    {
        var runtime = new Dictionary<string, string> { ["PATH"] = "/usr/bin", ["JASON_DATA_DIR"] = "/data", ["XDG_ACTIVATION_TOKEN"] = "from-the-runtime-s-start" };
        var session = new Dictionary<string, string> { ["WAYLAND_DISPLAY"] = "wayland-1", ["XDG_ACTIVATION_TOKEN"] = "from-the-session-s-start", ["DESKTOP_STARTUP_ID"] = "old" };

        var environment = ToastCommands.ClickEnvironment(runtime, session, token);

        Assert.Equal(("/usr/bin", "/data", "wayland-1"), (environment["PATH"], environment["JASON_DATA_DIR"], environment["WAYLAND_DISPLAY"]));
        Assert.Equal(token, environment.GetValueOrDefault("XDG_ACTIVATION_TOKEN"));
        Assert.False(environment.ContainsKey("DESKTOP_STARTUP_ID"));
    }

    /// <summary>
    /// What notify-send prints is read for three things: the id it printed once the notification was shown, the action a
    /// click sent, and the activation token its option wrote on the line between them; an empty line there is no token, nor
    /// is one with a space or a control character. Breaks if a token is read where none was given, or the click is missed.
    /// </summary>
    [Theory]
    [InlineData("42\n", "42", false, null)]
    [InlineData("42\ngnome-shell/Jason/4242-6-host_TIME123\ndefault\n", "42", true, "gnome-shell/Jason/4242-6-host_TIME123")]
    [InlineData("42\n\ndefault\n", "42", true, null)]
    [InlineData("42\ndefault\n", "42", true, null)]
    [InlineData("0\n", "0", false, null)]
    [InlineData("42\ntwo words\ndefault\n", "42", true, null)]
    [InlineData("", null, false, null)]
    public void What_notify_send_prints_says_the_id_the_click_and_the_token(string stdout, string? id, bool clicked, string? token)
    {
        Assert.Equal(id, ToastCommands.ShownId(stdout));
        Assert.Equal(clicked, ToastCommands.Clicked(stdout));
        Assert.Equal(token, ToastCommands.ActivationToken(stdout));
    }

    [Fact]
    public void Linux_says_it_with_gdbus_where_notify_send_is_absent()
    {
        Assert.Equal(
            [
                "/usr/bin/gdbus", "call", "--session",
                "--dest", "org.freedesktop.Notifications",
                "--object-path", "/org/freedesktop/Notifications",
                "--method", "org.freedesktop.Notifications.Notify",
                "'Jason'", "uint32 0", "''", "'Jason'", $"'{Message.Summary}\n{Message.Look}'", "@as []", "@a{sv} {}", "int32 -1",
            ],
            ToastCommands.LinuxGdbus("/usr/bin/gdbus", Message).Command);
    }

    [Fact]
    [Critical(Family.X3, "no injection into gdbus")]
    public void A_gvariant_string_escapes_its_quote_and_backslash()
    {
        var odd = new NotificationMessage("Jason", "it's c:\\x", "e", 1, 0);
        Assert.Equal("'it\\'s c:\\\\x\ne'", ToastCommands.LinuxGdbus("/usr/bin/gdbus", odd).Command[13]);
    }

    [Fact]
    public void Linux_asks_the_bus_whether_a_notification_daemon_is_there()
    {
        Assert.Equal(
            [
                "/usr/bin/gdbus", "call", "--session",
                "--dest", "org.freedesktop.DBus",
                "--object-path", "/org/freedesktop/DBus",
                "--method", "org.freedesktop.DBus.NameHasOwner",
                "org.freedesktop.Notifications",
            ],
            ToastCommands.LinuxDaemonProbe("/usr/bin/gdbus").Command);
    }

    [Fact]
    public void Linux_asks_the_bus_which_names_it_can_start()
    {
        Assert.Equal(
            [
                "/usr/bin/gdbus", "call", "--session",
                "--dest", "org.freedesktop.DBus",
                "--object-path", "/org/freedesktop/DBus",
                "--method", "org.freedesktop.DBus.ListActivatableNames",
            ],
            ToastCommands.LinuxActivatableProbe("/usr/bin/gdbus").Command);
    }

    /// <summary>
    /// The child's environment is the base the plugin host gets, plus on Linux what reaching a notification daemon
    /// needs. Never a <c>JASON_</c> name: not the data directory, and not anything shaped like the capability token.
    /// </summary>
    [Theory]
    [InlineData(DesktopPlatform.Windows, false)]
    [InlineData(DesktopPlatform.MacOs, false)]
    [InlineData(DesktopPlatform.Linux, true)]
    public void The_environment_is_the_base_plus_what_a_desktop_needs_and_never_a_jason_name(DesktopPlatform platform, bool desktopNames)
    {
        var source = new Dictionary<string, string>
        {
            ["PATH"] = "/usr/bin",
            ["DBUS_SESSION_BUS_ADDRESS"] = "unix:path=/run/user/1000/bus",
            ["XDG_RUNTIME_DIR"] = "/run/user/1000",
            ["DISPLAY"] = ":0",
            ["WAYLAND_DISPLAY"] = "wayland-0",
            ["JASON_DATA_DIR"] = "/home/someone/.jason",
            ["JASON_TOKEN"] = "jtk_0123456789abcdef0123456789abcdef",
            ["UNRELATED"] = "x",
        };

        var environment = ToastCommands.Environment(source, platform);

        Assert.Equal("/usr/bin", environment["PATH"]);
        Assert.DoesNotContain(environment.Keys, name => name.StartsWith("JASON_", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("UNRELATED", environment.Keys);
        foreach (var name in (string[])["DBUS_SESSION_BUS_ADDRESS", "XDG_RUNTIME_DIR", "DISPLAY", "WAYLAND_DISPLAY"])
        {
            Assert.Equal(desktopNames, environment.ContainsKey(name));
        }
    }

    /// <summary>The registration's names, once, for the child that writes the key and the uninstall that removes it.</summary>
    [Fact]
    public void The_registration_is_named_once()
    {
        Assert.Equal("ai.jason.runtime", ToastRegistration.Aumid);
        Assert.Equal("Jason", ToastRegistration.DisplayName);
        Assert.Equal(
            [
                @"Software\Classes\AppUserModelId\ai.jason.runtime",
                @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\ai.jason.runtime",
                @"Software\Microsoft\Windows\CurrentVersion\PushNotifications\Backup\ai.jason.runtime",
            ],
            ToastRegistration.Keys);
    }
}
