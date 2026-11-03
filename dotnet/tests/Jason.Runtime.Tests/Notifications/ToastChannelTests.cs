using System.Collections.Concurrent;
using Jason.Contracts.Notifications;
using Jason.Contracts.Plugins;
using Jason.Runtime.Notifications;
using Jason.Runtime.Notifications.Desktop;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Tests.Dispatch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Tests.Notifications;

/// <summary>
/// The toast channel: whether there is a desktop, which program shows the toast on each platform, and what its answer
/// means. Nothing is launched here; a recording launcher answers as each program would.
/// </summary>
public sealed class ToastChannelTests
{
    private const string UnreadableAnswer = "SENTINEL_TOAST_ANSWER";

    private static readonly NotificationMessage Message = NotificationComposer.Compose(2, 0);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Self : IPluginHostLocator
    {
        public IReadOnlyList<string> Command { get; } = ["C:\\jason\\jason.exe"];
    }

    /// <summary>Answers each launch by the program it names, and remembers every launch and its bound.</summary>
    private sealed class RecordingLauncher(Dictionary<string, Func<ToastLaunch, LaunchResult>> answers) : IToastLauncher
    {
        private readonly ConcurrentQueue<(ToastLaunch Launch, TimeSpan Bound)> _launches = new();

        public IReadOnlyList<(ToastLaunch Launch, TimeSpan Bound)> Launches => [.. _launches];

        public Task<LaunchResult> LaunchAsync(ToastLaunch launch, TimeSpan bound, CancellationToken cancellationToken)
        {
            _launches.Enqueue((launch, bound));
            var program = launch.Command[0] == "C:\\jason\\jason.exe" ? "toast-host" : launch.Command[0];
            var key = program != "gdbus" ? program
                : launch.Command.Contains("org.freedesktop.DBus.NameHasOwner") ? "probe"
                : launch.Command.Contains("org.freedesktop.DBus.ListActivatableNames") ? "activatable"
                : program;
            return Task.FromResult(answers[key](launch));
        }
    }

    private static LaunchResult Exit(int code, string stdout = "", string stderr = "") => new(true, false, code, stdout, stderr);

    /// <summary>The channels with no plugin named: the toast alone.</summary>
    private static NotificationChannels Channels(ToastChannel toast) => new(
        toast,
        new Jason.Runtime.Plugins.Registry.PluginRegistry(TimeProvider.System),
        new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());

    private static DesktopFacts Facts(DesktopPlatform platform, int session = 2, params string[] programs) => new(
        platform,
        session,
        new Dictionary<string, string>
        {
            ["PATH"] = "/usr/bin",
            ["DBUS_SESSION_BUS_ADDRESS"] = "unix:path=/run/user/1000/bus",
            ["JASON_DATA_DIR"] = "/home/someone/.jason",
        },
        program => programs.Contains(program) ? "/usr/bin/" + program : null,
        _ => false);

    private static (ToastChannel Channel, RecordingLauncher Launcher, RecordingLogs Logs) Channel(
        DesktopFacts facts,
        Dictionary<string, Func<ToastLaunch, LaunchResult>>? answers = null)
    {
        var launcher = new RecordingLauncher(answers ?? []);
        var logs = new RecordingLogs();
        var logger = new Logger<ToastChannel>(LoggerFactory.Create(builder => builder.AddProvider(logs)));
        var channel = new ToastChannel(launcher, new Self(), facts, DesktopPresence.Find(facts), logger);
        return (channel, launcher, logs);
    }

    private static void Is(ChannelOutcome outcome, ChannelResult result, string? reason = null, string? code = null, FailureClass? @class = null, string? detail = null)
    {
        Assert.Equal(("toast", ChannelKinds.Toast), (outcome.Channel, outcome.Kind));
        Assert.Equal((result, reason, code, @class), (outcome.Result, outcome.Reason, outcome.Code, outcome.Class));
        if (detail is not null)
        {
            Assert.Equal(detail, outcome.Detail);
        }
    }

    /// <summary>The toast turned off while a plugin is named: the line says so, and nothing is launched.</summary>
    [Fact]
    public async Task A_toast_turned_off_is_not_attempted_and_launches_nothing()
    {
        var (toast, launcher, _) = Channel(Facts(DesktopPlatform.Windows));
        var channels = Channels(toast).Resolve(new NotificationsOptions { Toast = false });

        Is(await Assert.Single(channels).DeliverAsync(Message, "ntf_test", Ct), ChannelResult.NotAttempted, NotAttemptedReasons.Off);
        Assert.Empty(launcher.Launches);
    }

    [Fact]
    public void A_toast_turned_on_is_the_toast_channel_itself()
    {
        var (toast, _, _) = Channel(Facts(DesktopPlatform.Windows));
        Assert.Same(toast, Assert.Single(Channels(toast).Resolve(new NotificationsOptions())));
    }

    /// <summary>
    /// Session 0 is decided before anything is launched, and said once per start: a second notification adds no
    /// second line to the log.
    /// </summary>
    [Fact]
    public async Task A_windows_service_session_has_no_desktop_and_says_so_once()
    {
        var (toast, launcher, logs) = Channel(Facts(DesktopPlatform.Windows, session: 0));

        Is(await toast.DeliverAsync(Message, "ntf_test", Ct), ChannelResult.NotAttempted, NotAttemptedReasons.NoDesktop, detail: "session 0");
        Is(await toast.DeliverAsync(Message, "ntf_test", Ct), ChannelResult.NotAttempted, NotAttemptedReasons.NoDesktop, detail: "session 0");

        Assert.Empty(launcher.Launches);
        Assert.Equal(1, logs.Count("No desktop: toasts are not shown by this runtime (session 0)"));
        Assert.Equal(Presence.NoDesktop("session 0"), toast.Presence);
    }

    [Fact]
    public async Task A_windows_desktop_session_runs_the_toast_host_and_reads_its_outcome()
    {
        var outcome = """{"protocol_version":1,"registration":"written","registration_hr":null,"calls":[],"failed_step":null,"show_hr":"0x00000000","setting_after_hr":"0x00000000","setting_after":1}""";
        var (toast, launcher, _) = Channel(Facts(DesktopPlatform.Windows), new() { ["toast-host"] = _ => Exit(0, outcome) });

        Is(await toast.DeliverAsync(Message, "ntf_test", Ct), ChannelResult.Silenced, SilencedReasons.DisabledForApplication);

        var (launch, bound) = Assert.Single(launcher.Launches);
        var expected = ToastCommands.Windows(new Self().Command, Message);
        Assert.Equal(expected.Command, launch.Command);
        Assert.Equal(expected.Stdin, launch.Stdin);
        Assert.Equal(ToastChannel.Bound, bound);
        Assert.Equal(TimeSpan.FromSeconds(5), bound);
        Assert.DoesNotContain(launch.Environment.Keys, name => name.StartsWith("JASON_", StringComparison.OrdinalIgnoreCase));
    }

    private static string Linked(string scheme, string? hr) =>
        """{"protocol_version":1,"registration":"present","registration_hr":null,"calls":[],"failed_step":null,"show_hr":"0x00000000","setting_after_hr":"0x00000000","setting_after":0,"scheme":"""
        + $"\"{scheme}\",\"scheme_hr\":" + (hr is null ? "null" : $"\"{hr}\"") + "}";

    /// <summary>
    /// A toast whose <c>jason:</c> link is another program's, or could not be written, is still delivered; the runtime's
    /// log says so once per run, by the state and the HRESULT alone, so a click that opens nothing has a reason to read.
    /// </summary>
    [Theory]
    [InlineData("foreign", null, "The jason: link of this account's toasts is foreign (none): a click on a toast opens nothing of Jason's")]
    [InlineData("failed", "0x80070005", "The jason: link of this account's toasts is failed (0x80070005): a click on a toast opens nothing of Jason's")]
    public async Task A_link_that_is_not_jason_s_is_said_once_per_run(string scheme, string? hr, string line)
    {
        var (toast, _, logs) = Channel(Facts(DesktopPlatform.Windows), new() { ["toast-host"] = _ => Exit(0, Linked(scheme, hr)) });

        Is(await toast.DeliverAsync(Message, "ntf_test", Ct), ChannelResult.Delivered);
        Is(await toast.DeliverAsync(Message, "ntf_test", Ct), ChannelResult.Delivered);

        Assert.Equal(1, logs.Count(line));
        Assert.Single(logs.Lines);
    }

    /// <summary>A link that is Jason's says nothing, and a state or an HRESULT not of the host's own words is never repeated.</summary>
    [Theory]
    [InlineData("present", null)]
    [InlineData("written", null)]
    [InlineData("SENTINEL", null)]
    public async Task A_link_that_is_jason_s_or_unknown_says_nothing(string scheme, string? hr)
    {
        var (toast, _, logs) = Channel(Facts(DesktopPlatform.Windows), new() { ["toast-host"] = _ => Exit(0, Linked(scheme, hr)) });

        Is(await toast.DeliverAsync(Message, "ntf_test", Ct), ChannelResult.Delivered);

        Assert.Empty(logs.Lines);
    }

    [Fact]
    public async Task An_hresult_that_is_not_one_is_said_as_none()
    {
        var (toast, _, logs) = Channel(Facts(DesktopPlatform.Windows), new() { ["toast-host"] = _ => Exit(0, Linked("failed", "SENTINEL")) });

        await toast.DeliverAsync(Message, "ntf_test", Ct);

        Assert.Equal(1, logs.Count("The jason: link of this account's toasts is failed (none): a click on a toast opens nothing of Jason's"));
    }

    /// <summary>
    /// An answer the runtime cannot read back is no outcome, said by the exit code alone: the child's own text is not
    /// repeated, in the outcome or in the log. The parser's message can quote what it read, and the line is a child's.
    /// </summary>
    [Fact]
    public async Task A_toast_host_answer_that_cannot_be_read_is_no_outcome_and_is_quoted_nowhere()
    {
        var unreadable = "{\"registration\":\"" + UnreadableAnswer + "\",\"calls\":";
        var (toast, _, logs) = Channel(Facts(DesktopPlatform.Windows), new() { ["toast-host"] = _ => Exit(0, unreadable) });

        var outcome = await toast.DeliverAsync(Message, "ntf_test", Ct);

        Is(outcome, ChannelResult.Failed, code: ToastOutcomes.NoOutcome, @class: FailureClass.Ambiguous, detail: "exit 0");
        Assert.False(logs.Mentions(UnreadableAnswer), "the toast host's answer reached the log");
    }

    /// <summary>No daemon owns the name, and none the bus could start is installed: nothing can show a notification.</summary>
    [Fact]
    public async Task A_linux_bus_with_no_notification_daemon_has_no_desktop()
    {
        var (toast, launcher, _) = Channel(
            Facts(DesktopPlatform.Linux, 2, "notify-send", "gdbus"),
            new()
            {
                ["probe"] = _ => Exit(0, "(false,)\n"),
                ["activatable"] = _ => Exit(0, "(['org.freedesktop.DBus', 'org.freedesktop.systemd1'],)\n"),
            });

        Is(await toast.DeliverAsync(Message, "ntf_test", Ct), ChannelResult.NotAttempted, NotAttemptedReasons.NoDesktop, detail: "no notification daemon");
        Assert.Equal(
            [ToastCommands.LinuxDaemonProbe().Command, ToastCommands.LinuxActivatableProbe().Command],
            launcher.Launches.Select(l => l.Launch.Command));
    }

    /// <summary>
    /// A daemon the bus starts on demand — dunst, mako, MATE's — owns its name only after its first notification. Listed
    /// as activatable, it is told, and the bus starts it.
    /// </summary>
    [Fact]
    public async Task A_linux_daemon_the_bus_starts_on_demand_is_told()
    {
        var (toast, launcher, _) = Channel(
            Facts(DesktopPlatform.Linux, 2, "notify-send", "gdbus"),
            new()
            {
                ["probe"] = _ => Exit(0, "(false,)\n"),
                ["activatable"] = _ => Exit(0, "(['org.freedesktop.DBus', 'org.freedesktop.Notifications'],)\n"),
                ["notify-send"] = _ => Exit(0),
            });

        Is(await toast.DeliverAsync(Message, "ntf_test", Ct), ChannelResult.Delivered);
        Assert.Equal(
            [ToastCommands.LinuxDaemonProbe().Command, ToastCommands.LinuxActivatableProbe().Command, ToastCommands.LinuxNotifySend(Message).Command],
            launcher.Launches.Select(l => l.Launch.Command));
    }

    /// <summary>
    /// A probe that cannot answer — it fails, or outlives its bound — decides nothing, the second as the first: the
    /// notification is sent, and its own exit decides.
    /// </summary>
    [Theory]
    [InlineData("probe", false)]
    [InlineData("probe", true)]
    [InlineData("activatable", false)]
    [InlineData("activatable", true)]
    public async Task A_linux_probe_that_cannot_answer_decides_nothing(string which, bool timedOut)
    {
        Func<ToastLaunch, LaunchResult> unanswered = _ => timedOut ? new LaunchResult(true, true, null, string.Empty, string.Empty) : Exit(1, stderr: "gdbus: error");
        Func<ToastLaunch, LaunchResult> unowned = _ => Exit(0, "(false,)\n");
        var (toast, launcher, _) = Channel(
            Facts(DesktopPlatform.Linux, 2, "notify-send", "gdbus"),
            new() { ["probe"] = which == "probe" ? unanswered : unowned, ["activatable"] = unanswered, ["notify-send"] = _ => Exit(0) });

        Is(await toast.DeliverAsync(Message, "ntf_test", Ct), ChannelResult.Delivered);
        IReadOnlyList<string>[] launched = which == "probe"
            ? [ToastCommands.LinuxDaemonProbe().Command, ToastCommands.LinuxNotifySend(Message).Command]
            : [ToastCommands.LinuxDaemonProbe().Command, ToastCommands.LinuxActivatableProbe().Command, ToastCommands.LinuxNotifySend(Message).Command];
        Assert.Equal(launched, launcher.Launches.Select(l => l.Launch.Command));
    }

    [Theory]
    [InlineData(0, "", ChannelResult.Delivered, null)]
    [InlineData(1, "first\nnotify-send: cannot connect", ChannelResult.Failed, "notify-send: cannot connect")]
    public async Task A_linux_daemon_is_told_with_notify_send_and_its_exit_decides(int exit, string stderr, ChannelResult result, string? detail)
    {
        var (toast, launcher, _) = Channel(
            Facts(DesktopPlatform.Linux, 2, "notify-send", "gdbus"),
            new() { ["probe"] = _ => Exit(0, "(true,)\n"), ["notify-send"] = _ => Exit(exit, stderr: stderr) });

        var outcome = await toast.DeliverAsync(Message, "ntf_test", Ct);

        if (result == ChannelResult.Delivered)
        {
            Is(outcome, ChannelResult.Delivered);
        }
        else
        {
            Is(outcome, ChannelResult.Failed, code: "toast_failed", @class: FailureClass.Permanent, detail: detail);
        }

        Assert.Equal(
            [ToastCommands.LinuxDaemonProbe().Command, ToastCommands.LinuxNotifySend(Message).Command],
            launcher.Launches.Select(l => l.Launch.Command));
        Assert.All(launcher.Launches, l => Assert.True(l.Launch.Environment.ContainsKey("DBUS_SESSION_BUS_ADDRESS")));
    }

    /// <summary>Without gdbus the daemon cannot be asked about, so notify-send's own exit decides.</summary>
    [Theory]
    [InlineData(0, ChannelResult.Delivered)]
    [InlineData(1, ChannelResult.Failed)]
    public async Task Notify_send_without_gdbus_is_decided_by_its_own_exit(int exit, ChannelResult result)
    {
        var (toast, launcher, _) = Channel(Facts(DesktopPlatform.Linux, 2, "notify-send"), new() { ["notify-send"] = _ => Exit(exit, stderr: "no daemon") });

        Assert.Equal(result, (await toast.DeliverAsync(Message, "ntf_test", Ct)).Result);
        Assert.Equal(ToastCommands.LinuxNotifySend(Message).Command, Assert.Single(launcher.Launches).Launch.Command);
    }

    [Fact]
    public async Task Gdbus_alone_tells_the_daemon_itself()
    {
        var (toast, launcher, _) = Channel(
            Facts(DesktopPlatform.Linux, 2, "gdbus"),
            new()
            {
                ["probe"] = _ => Exit(0, "(true,)\n"),
                ["gdbus"] = _ => Exit(0, "(uint32 7,)\n"),
            });

        Is(await toast.DeliverAsync(Message, "ntf_test", Ct), ChannelResult.Delivered);
        Assert.Equal(ToastCommands.LinuxGdbus(Message).Command, launcher.Launches[^1].Launch.Command);
    }

    [Fact]
    public async Task A_mac_whose_osascript_fails_has_no_desktop()
    {
        var (toast, _, _) = Channel(Facts(DesktopPlatform.MacOs), new() { ["osascript"] = _ => Exit(1, stderr: "execution error: not allowed (-1743)") });

        Is(await toast.DeliverAsync(Message, "ntf_test", Ct), ChannelResult.NotAttempted, NotAttemptedReasons.NoDesktop, detail: "osascript failed: execution error: not allowed (-1743)");
    }

    [Fact]
    public async Task A_mac_whose_osascript_answers_is_delivered()
    {
        var (toast, launcher, _) = Channel(Facts(DesktopPlatform.MacOs), new() { ["osascript"] = _ => Exit(0) });

        Is(await toast.DeliverAsync(Message, "ntf_test", Ct), ChannelResult.Delivered);
        var launch = Assert.Single(launcher.Launches).Launch;
        Assert.Equal(ToastCommands.MacOs(Message).Command, launch.Command);
        Assert.Null(launch.Stdin);
    }

    [Theory]
    [InlineData(DesktopPlatform.Linux)]
    [InlineData(DesktopPlatform.MacOs)]
    public async Task A_program_killed_at_the_bound_is_ambiguous_and_one_that_never_started_is_transient(DesktopPlatform platform)
    {
        var program = platform == DesktopPlatform.Linux ? "notify-send" : "osascript";
        var (timedOut, _, _) = Channel(Facts(platform, 2, program), new() { [program] = _ => new LaunchResult(true, true, null, "", "") });
        var (missing, _, _) = Channel(Facts(platform, 2, program), new() { [program] = _ => new LaunchResult(false, false, null, "", "", "not found") });

        Is(await timedOut.DeliverAsync(Message, "ntf_test", Ct), ChannelResult.Failed, code: "toast_timeout", @class: FailureClass.Ambiguous);
        Is(await missing.DeliverAsync(Message, "ntf_test", Ct), ChannelResult.Failed, code: "toast_launch_failed", @class: FailureClass.Transient);
    }
}
