using Jason.Runtime.Notifications.Desktop;

namespace Jason.Runtime.Tests.Notifications;

/// <summary>
/// Whether there is a desktop to show a toast on, decided from facts about this process before anything is launched:
/// the session it runs in on Windows, and on Linux a session bus and a program to talk to the notifier with.
/// </summary>
public sealed class DesktopPresenceTests
{
    private static DesktopFacts Facts(
        DesktopPlatform platform,
        int session = 2,
        Dictionary<string, string>? environment = null,
        string[]? programs = null,
        string[]? files = null) =>
        new(
            platform,
            session,
            environment ?? [],
            program => (programs ?? []).Contains(program) ? "/usr/bin/" + program : null,
            path => (files ?? []).Contains(path));

    [Fact]
    [Critical(Family.N3, "session 0")]
    public void A_windows_process_in_session_0_has_no_desktop()
    {
        // One word for it, in the contracts: the runtime says it, and jason status keys its advice on it.
        Assert.Equal(Presence.NoDesktop(Jason.Contracts.Notifications.ChannelPresence.SessionZero), DesktopPresence.Find(Facts(DesktopPlatform.Windows, session: 0)));
        Assert.Equal("session 0", Jason.Contracts.Notifications.ChannelPresence.SessionZero);
    }

    /// <summary>
    /// Every reason the runtime gives for a missing desktop is one of the contract's own words, with the values a runtime
    /// has always written: the client tells a word of the runtime's from text nobody controls by that list, and nothing
    /// else.
    /// </summary>
    [Fact]
    public void Every_reason_for_a_missing_desktop_is_one_of_the_contracts_own_words()
    {
        var words = Jason.Contracts.Notifications.ChannelPresence.Details;
        var given = new[]
        {
            DesktopPresence.Find(Facts(DesktopPlatform.Windows, session: 0)),
            DesktopPresence.Find(Facts(DesktopPlatform.Linux)),
            DesktopPresence.Find(Facts(DesktopPlatform.Linux, environment: new() { ["DBUS_SESSION_BUS_ADDRESS"] = "unix:path=/run/user/1000/bus" })),
        };

        Assert.Equal(["session 0", "no session bus", "no notification program"], words);
        Assert.Equal("no session bus", Jason.Contracts.Notifications.ChannelPresence.NoSessionBus);
        Assert.Equal("no notification program", Jason.Contracts.Notifications.ChannelPresence.NoNotificationProgram);
        Assert.Equal(words, given.Select(presence => presence.Detail));
    }

    [Fact]
    public void A_windows_process_in_a_user_session_has_one() =>
        Assert.Equal(Presence.Desktop, DesktopPresence.Find(Facts(DesktopPlatform.Windows, session: 2)));

    [Fact]
    public void A_linux_process_with_no_session_bus_has_no_desktop() =>
        Assert.Equal(
            Presence.NoDesktop("no session bus"),
            DesktopPresence.Find(Facts(DesktopPlatform.Linux, environment: new() { ["XDG_RUNTIME_DIR"] = "/run/user/1000" }, programs: ["notify-send"])));

    [Fact]
    public void A_linux_process_with_a_bus_and_no_notification_program_has_no_desktop() =>
        Assert.Equal(
            Presence.NoDesktop("no notification program"),
            DesktopPresence.Find(Facts(DesktopPlatform.Linux, environment: new() { ["DBUS_SESSION_BUS_ADDRESS"] = "unix:path=/run/user/1000/bus" })));

    /// <summary>The bus's socket in the runtime directory is a bus, as much as the variable naming it.</summary>
    [Theory]
    [InlineData("notify-send")]
    [InlineData("gdbus")]
    public void A_linux_process_with_a_bus_and_a_program_has_one(string program) =>
        Assert.Equal(
            Presence.Desktop,
            DesktopPresence.Find(Facts(
                DesktopPlatform.Linux,
                environment: new() { ["XDG_RUNTIME_DIR"] = "/run/user/1000" },
                programs: [program],
                files: ["/run/user/1000/bus"])));

    /// <summary>On macOS nothing can be known before trying: an osascript that fails says so when it is run.</summary>
    [Fact]
    public void A_mac_is_taken_to_have_a_desktop_until_osascript_says_otherwise() =>
        Assert.Equal(Presence.Desktop, DesktopPresence.Find(Facts(DesktopPlatform.MacOs, session: 0)));

    /// <summary>The facts this process really has can be read without launching anything or touching WinRT.</summary>
    [Fact]
    public void This_process_s_own_facts_are_read_without_launching_anything()
    {
        var facts = DesktopFacts.Current();
        Assert.Equal(
            OperatingSystem.IsWindows() ? DesktopPlatform.Windows : OperatingSystem.IsMacOS() ? DesktopPlatform.MacOs : DesktopPlatform.Linux,
            facts.Platform);
        Assert.Equal(System.Diagnostics.Process.GetCurrentProcess().SessionId, facts.SessionId);
    }
}
