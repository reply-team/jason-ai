using Jason.Contracts.Notifications;
using Jason.Runtime.Notifications;
using Jason.Runtime.Notifications.Desktop;

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
    /// The item that waits is never in a toast on macOS or Linux: a message that carries one is said there by its counts
    /// and where to look alone, in no argument, on no stdin and in no value of its environment.
    /// </summary>
    [Theory]
    [InlineData("osascript")]
    [InlineData("notify-send")]
    [InlineData("gdbus")]
    public void A_message_carrying_an_item_puts_no_id_in_a_mac_os_or_linux_toast(string command)
    {
        var one = NotificationComposer.Compose(1, 0, item: "apr_01JBZ3X7Q9K2M4N6P8R0S2T4V6");
        Assert.Equal("apr_01JBZ3X7Q9K2M4N6P8R0S2T4V6", one.Item);

        var launch = command switch
        {
            "osascript" => ToastCommands.MacOs(one),
            "notify-send" => ToastCommands.LinuxNotifySend(one),
            _ => ToastCommands.LinuxGdbus(one),
        };

        Assert.Equal(command, launch.Command[0]);
        Assert.All(launch.Command, argument => Assert.DoesNotContain("apr_", argument, StringComparison.Ordinal));
        Assert.DoesNotContain("apr_", launch.Stdin ?? string.Empty, StringComparison.Ordinal);
        Assert.All(launch.Environment.Values, value => Assert.DoesNotContain("apr_", value, StringComparison.Ordinal));
    }

    /// <summary>The same executable may be a dll run by dotnet, as it is under test: the mode follows whatever starts it.</summary>
    [Fact]
    public void Windows_follows_the_command_that_starts_this_executable() =>
        Assert.Equal(["dotnet", "jason.dll", "toast-host", "--protocol", Protocol], ToastCommands.Windows(["dotnet", "jason.dll"], Message).Command);

    [Fact]
    public void Mac_os_says_it_with_osascript()
    {
        var launch = ToastCommands.MacOs(Message);

        Assert.Equal(
            ["osascript", "-e", $"display notification \"{Message.Summary} {Message.Look}\" with title \"Jason\""],
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

    [Fact]
    public void Linux_says_it_with_notify_send()
    {
        Assert.Equal(
            ["notify-send", "--app-name=Jason", "Jason", $"{Message.Summary}\n{Message.Look}"],
            ToastCommands.LinuxNotifySend(Message).Command);
    }

    [Fact]
    public void Linux_says_it_with_gdbus_where_notify_send_is_absent()
    {
        Assert.Equal(
            [
                "gdbus", "call", "--session",
                "--dest", "org.freedesktop.Notifications",
                "--object-path", "/org/freedesktop/Notifications",
                "--method", "org.freedesktop.Notifications.Notify",
                "'Jason'", "uint32 0", "''", "'Jason'", $"'{Message.Summary}\n{Message.Look}'", "@as []", "@a{sv} {}", "int32 -1",
            ],
            ToastCommands.LinuxGdbus(Message).Command);
    }

    [Fact]
    [Critical(Family.X3, "no injection into gdbus")]
    public void A_gvariant_string_escapes_its_quote_and_backslash()
    {
        var odd = new NotificationMessage("Jason", "it's c:\\x", "e", 1, 0);
        Assert.Equal("'it\\'s c:\\\\x\ne'", ToastCommands.LinuxGdbus(odd).Command[13]);
    }

    [Fact]
    public void Linux_asks_the_bus_whether_a_notification_daemon_is_there()
    {
        Assert.Equal(
            [
                "gdbus", "call", "--session",
                "--dest", "org.freedesktop.DBus",
                "--object-path", "/org/freedesktop/DBus",
                "--method", "org.freedesktop.DBus.NameHasOwner",
                "org.freedesktop.Notifications",
            ],
            ToastCommands.LinuxDaemonProbe().Command);
    }

    [Fact]
    public void Linux_asks_the_bus_which_names_it_can_start()
    {
        Assert.Equal(
            [
                "gdbus", "call", "--session",
                "--dest", "org.freedesktop.DBus",
                "--object-path", "/org/freedesktop/DBus",
                "--method", "org.freedesktop.DBus.ListActivatableNames",
            ],
            ToastCommands.LinuxActivatableProbe().Command);
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
