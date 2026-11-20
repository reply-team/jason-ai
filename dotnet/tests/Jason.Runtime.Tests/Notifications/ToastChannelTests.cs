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

    /// <summary>Where the facts below find each Linux program: by its full path, as the search path gave it.</summary>
    private const string NotifySend = "/usr/bin/notify-send";

    private const string Gdbus = "/usr/bin/gdbus";

    private static readonly NotificationMessage Message = NotificationComposer.Compose(2, 0);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Self : IPluginHostLocator
    {
        public IReadOnlyList<string> Command { get; } = ["C:\\jason\\jason.exe"];
    }

    /// <summary>
    /// Answers each launch by the program it names, a click by <c>click</c>, and a waiting launch by <paramref name="waiting"/>
    /// where it names the program, else as one that ended with its answer; remembers every launch and its bound.
    /// </summary>
    private sealed class RecordingLauncher(Dictionary<string, Func<ToastLaunch, LaunchResult>> answers, Dictionary<string, Func<ToastLaunch, WaitingLaunch>>? waiting = null) : IToastLauncher
    {
        private readonly ConcurrentQueue<(ToastLaunch Launch, TimeSpan Bound)> _launches = new();

        private readonly List<(Func<ToastLaunch, bool> Match, TaskCompletionSource<(ToastLaunch, TimeSpan)> Seen)> _awaited = [];

        public IReadOnlyList<(ToastLaunch Launch, TimeSpan Bound)> Launches => [.. _launches];

        public Task<LaunchResult> LaunchAsync(ToastLaunch launch, TimeSpan bound, CancellationToken cancellationToken)
        {
            Record(launch, bound);
            var key = Key(launch);
            return Task.FromResult(key == "help" && !answers.ContainsKey(key) ? Exit(0, ToastCommandsTests.Help("0.8.9")) : answers[key](launch));
        }

        public Task<WaitingLaunch> LaunchWaitingAsync(ToastLaunch launch, TimeSpan bound, Func<string, bool> shown, CancellationToken cancellationToken)
        {
            Record(launch, bound);
            var key = Key(launch);
            return Task.FromResult(waiting is not null && waiting.TryGetValue(key, out var stays) ? stays(launch) : new WaitingLaunch(answers[key](launch), null, () => { }));
        }

        /// <summary>The first launch <paramref name="match"/> takes, made already or to come.</summary>
        public Task<(ToastLaunch Launch, TimeSpan Bound)> Launched(Func<ToastLaunch, bool> match)
        {
            var seen = new TaskCompletionSource<(ToastLaunch, TimeSpan)>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_awaited)
            {
                if (_launches.FirstOrDefault(made => match(made.Launch)) is { Launch: not null } made)
                {
                    seen.SetResult(made);
                }
                else
                {
                    _awaited.Add((match, seen));
                }
            }

            return seen.Task;
        }

        private void Record(ToastLaunch launch, TimeSpan bound)
        {
            lock (_awaited)
            {
                _launches.Enqueue((launch, bound));
                foreach (var (match, seen) in _awaited.Where(awaited => awaited.Match(launch)))
                {
                    seen.TrySetResult((launch, bound));
                }
            }
        }

        private static string Key(ToastLaunch launch)
        {
            if (launch.Command.Any(argument => argument.StartsWith(AttentionLinks.Scheme + ":", StringComparison.Ordinal)))
            {
                return "click";
            }

            if (launch.Command.Contains("--help"))
            {
                return "help";
            }

            var program = launch.Command[0] == "C:\\jason\\jason.exe" ? "toast-host" : Path.GetFileName(launch.Command[0]);
            return program != "gdbus" ? program
                : launch.Command.Contains("org.freedesktop.DBus.NameHasOwner") ? "probe"
                : launch.Command.Contains("org.freedesktop.DBus.ListActivatableNames") ? "activatable"
                : program;
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
        Dictionary<string, Func<ToastLaunch, LaunchResult>>? answers = null,
        Dictionary<string, Func<ToastLaunch, WaitingLaunch>>? waiting = null)
    {
        var launcher = new RecordingLauncher(answers ?? [], waiting);
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
            [ToastCommands.LinuxDaemonProbe(Gdbus).Command, ToastCommands.LinuxActivatableProbe(Gdbus).Command],
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
            [ToastCommands.LinuxDaemonProbe(Gdbus).Command, ToastCommands.LinuxActivatableProbe(Gdbus).Command, Help, Shown(Message)],
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
            ? [ToastCommands.LinuxDaemonProbe(Gdbus).Command, Help, Shown(Message)]
            : [ToastCommands.LinuxDaemonProbe(Gdbus).Command, ToastCommands.LinuxActivatableProbe(Gdbus).Command, Help, Shown(Message)];
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

        Assert.Equal([ToastCommands.LinuxDaemonProbe(Gdbus).Command, Help, Shown(Message)], launcher.Launches.Select(l => l.Launch.Command));
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
        Assert.Equal([Help, Shown(Message)], launcher.Launches.Select(l => l.Launch.Command));
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
        Assert.Equal(ToastCommands.LinuxGdbus(Gdbus, Message).Command, launcher.Launches[^1].Launch.Command);
    }

    /// <summary>
    /// On Linux a toast is started by the path the search path gave for its program, never by the bare name again, which
    /// the operating system would look up anew.
    /// </summary>
    [Fact]
    public async Task On_linux_the_toast_is_started_by_the_path_the_search_path_gave()
    {
        var (toast, launcher, _) = Channel(
            Facts(DesktopPlatform.Linux, 2, "notify-send", "gdbus"),
            new() { ["probe"] = _ => Exit(0, "(true,)\n"), ["notify-send"] = _ => Exit(0) });

        Is(await toast.DeliverAsync(Message, "ntf_test", Ct), ChannelResult.Delivered);
        Assert.Equal([Gdbus, NotifySend, NotifySend], launcher.Launches.Select(l => l.Launch.Command[0]));
    }

    /// <summary>
    /// A Linux desktop whose search path gives neither notify-send nor gdbus by a full path starts nothing, and answers as
    /// a program that could not be started answers: a launch that failed, worth trying again.
    /// </summary>
    [Fact]
    public async Task A_linux_desktop_with_neither_program_starts_nothing_and_answers_as_a_launch_that_failed()
    {
        var facts = Facts(DesktopPlatform.Linux, 2);
        var launcher = new RecordingLauncher([]);
        var toast = new ToastChannel(launcher, new Self(), facts, Presence.Desktop, new Logger<ToastChannel>(LoggerFactory.Create(_ => { })));

        Is(await toast.DeliverAsync(Message, "ntf_test", Ct), ChannelResult.Failed, code: ToastOutcomes.LaunchFailed, @class: FailureClass.Transient);
        Assert.Empty(launcher.Launches);
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
    // --- Linux: the click -----------------------------------------------------------------------------------------

    private const string Approval = "apr_01JBZ3X7Q9K2M4N6P8R0S2T4V6";

    private const string Refusal = "Nothing was opened: no Jason Desktop that takes its links is installed for this account.";

    /// <summary>Linux facts whose session environment carries what the row adds.</summary>
    private static DesktopFacts LinuxFacts(Dictionary<string, string> added, params string[] programs)
    {
        var facts = Facts(DesktopPlatform.Linux, 2, programs);
        var environment = new Dictionary<string, string>(facts.Environment, StringComparer.Ordinal);
        foreach (var (name, value) in added)
        {
            environment[name] = value;
        }

        return facts with { Environment = environment };
    }

    /// <summary>notify-send that showed the toast and waits: its end is the row's to give; closed, it ends as a killed program.</summary>
    private static WaitingLaunch Stays(TaskCompletionSource<LaunchResult> end, List<int>? closed = null, int number = 0, string said = "42\n") =>
        new(
            new LaunchResult(true, false, null, said, string.Empty),
            end.Task,
            () =>
            {
                closed?.Add(number);
                end.TrySetResult(new LaunchResult(true, false, 143, said, string.Empty));
            });

    /// <summary>notify-send's help, asked before the first toast of a run.</summary>
    private static IReadOnlyList<string> Help => ToastCommands.LinuxNotifySendHelp(NotifySend).Command;

    /// <summary>The waiting toast, as a notify-send whose help names every option shows it.</summary>
    private static IReadOnlyList<string> Shown(NotificationMessage message) => ToastCommands.LinuxNotifySend(NotifySend, message, token: true).Command;

    private static bool IsClick(ToastLaunch launch) => launch.Command.Any(argument => argument.StartsWith("jason:", StringComparison.Ordinal));

    /// <summary>
    /// On Linux the toast waits on the bus, and its click starts this executable with the toast's link for the one item, in
    /// the session's own environment with the activation token the server gave; where the runtime runs in a systemd unit,
    /// inside a scope of its own. Nothing starts before the click. Breaks if the click is lost, opens anything but the item,
    /// drops the token that lets the window come forward, or starts the desktop inside the runtime's unit.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task On_linux_a_shown_toast_waits_and_its_click_opens_jason_desktop_on_the_item(bool inUnit)
    {
        var end = new TaskCompletionSource<LaunchResult>();
        var facts = LinuxFacts(inUnit ? new() { ["INVOCATION_ID"] = "0123abcd" } : [], "notify-send", "gdbus", "systemd-run", "systemctl");
        var (toast, launcher, logs) = Channel(
            facts,
            new()
            {
                ["probe"] = _ => Exit(0, "(true,)\n"),
                ["click"] = _ => Exit(0, stderr: "Opened in Jason Desktop."),
                ["systemctl"] = _ => Exit(0, "WAYLAND_DISPLAY=wayland-1\nXDG_CURRENT_DESKTOP=Hyprland\nXDG_ACTIVATION_TOKEN=from-the-session-s-start\n"),
            },
            new() { ["notify-send"] = _ => Stays(end) });
        var one = NotificationComposer.Compose(1, 0, item: Approval);

        Is(await toast.DeliverAsync(one, "ntf_test", Ct), ChannelResult.Delivered);
        var clicked = launcher.Launched(IsClick);
        Assert.False(clicked.IsCompleted, "nothing starts before the click");
        Assert.Equal(Shown(one), launcher.Launches[^1].Launch.Command);

        end.SetResult(Exit(0, "42\ngnome-shell/Jason/4242-6_TIME9\ndefault\n"));
        var (click, bound) = await clicked.WaitAsync(Hang, Ct);

        Assert.Equal(ToastCommands.LinuxClick(new Self().Command, "jason://attention/approvals/" + Approval, inUnit ? "/usr/bin/systemd-run" : null), click.Command);
        Assert.Equal("gnome-shell/Jason/4242-6_TIME9", click.Environment["XDG_ACTIVATION_TOKEN"]);
        Assert.Equal("/home/someone/.jason", click.Environment["JASON_DATA_DIR"]);
        Assert.Equal(("wayland-1", "Hyprland"), (click.Environment["WAYLAND_DISPLAY"], click.Environment["XDG_CURRENT_DESKTOP"]));
        Assert.Contains(launcher.Launches, launch => launch.Launch.Command.SequenceEqual(ToastCommands.LinuxSessionEnvironment("/usr/bin/systemctl").Command));
        Assert.Equal(ToastChannel.ClickBound, bound);
        await WhenLogged(logs, "A notification's click was handed to Jason Desktop");
    }

    /// <summary>
    /// A toast dismissed opens nothing; one for several things opens the list, and one clicked within the bound is still
    /// clicked through. Breaks if a dismissal starts anything, or a pointer to several opens one item.
    /// </summary>
    [Fact]
    public async Task On_linux_a_dismissed_toast_opens_nothing_and_one_of_several_opens_the_list()
    {
        var dismissed = new TaskCompletionSource<LaunchResult>();
        var clickedLater = new TaskCompletionSource<LaunchResult>();
        var ends = new Queue<TaskCompletionSource<LaunchResult>>([dismissed, clickedLater]);
        var (toast, launcher, _) = Channel(
            Facts(DesktopPlatform.Linux, 2, "notify-send"),
            new() { ["click"] = _ => Exit(0), ["notify-send"] = _ => Exit(0, "7\ndefault\n") },
            new() { ["notify-send"] = _ => Stays(ends.Dequeue()) });

        Is(await toast.DeliverAsync(Message, "ntf_1", Ct), ChannelResult.Delivered);
        Is(await toast.DeliverAsync(Message, "ntf_2", Ct), ChannelResult.Delivered);
        dismissed.SetResult(Exit(0, "42\n"));
        clickedLater.SetResult(Exit(0, "43\ndefault\n"));
        var (click, _) = await launcher.Launched(IsClick).WaitAsync(Hang, Ct);

        Assert.Equal(AttentionLinks.List, click.Command[^1]);
        Assert.Single(launcher.Launches, launch => IsClick(launch.Launch));
    }

    /// <summary>
    /// A click whose handler opened nothing — no Jason Desktop that takes links, the links elsewhere, the opener refusing —
    /// is said in a notification of the handler's own sentence, and the log says only that it opened nothing, never the
    /// sentence. Nothing else opens. Breaks if a click that opened nothing leaves the person with nothing said.
    /// </summary>
    [Fact]
    public async Task On_linux_a_click_that_opened_nothing_is_said_in_a_notification()
    {
        var end = new TaskCompletionSource<LaunchResult>();
        var (toast, launcher, logs) = Channel(
            Facts(DesktopPlatform.Linux, 2, "notify-send"),
            new() { ["click"] = _ => Exit(1, stderr: Refusal), ["notify-send"] = _ => Exit(0) },
            new() { ["notify-send"] = _ => Stays(end) });

        Is(await toast.DeliverAsync(Message, "ntf_test", Ct), ChannelResult.Delivered);
        var said = launcher.Launched(launch => launch.Command.SequenceEqual(ToastCommands.LinuxSaid(NotifySend, "Jason", Refusal).Command));
        end.SetResult(Exit(0, "42\ndefault\n"));
        await said.WaitAsync(Hang, Ct);

        await WhenLogged(logs, "A notification's click opened nothing (exit 1); a notification said why");
        Assert.False(logs.Mentions(Refusal), "the handler's sentence reached the log");
    }

    /// <summary>
    /// notify-send that cannot show the toast says the id 0 and would wait on the bus for nothing: it is ended, and the
    /// toast failed. Breaks if a toast nobody saw is recorded delivered.
    /// </summary>
    [Fact]
    public async Task On_linux_a_toast_notify_send_could_not_show_is_ended_and_failed()
    {
        var end = new TaskCompletionSource<LaunchResult>();
        var closed = new List<int>();
        var (toast, _, _) = Channel(
            Facts(DesktopPlatform.Linux, 2, "notify-send"),
            [],
            new() { ["notify-send"] = _ => Stays(end, closed, said: "0\n") });

        Is(await toast.DeliverAsync(Message, "ntf_test", Ct), ChannelResult.Failed, code: "toast_failed", @class: FailureClass.Permanent);
        Assert.Equal([0], closed);
    }

    /// <summary>
    /// The runtime holds at most <see cref="ToastChannel.MaxWaiting"/> waiting toasts, ending the oldest before it shows
    /// another, and ends every one when it stops; a click that comes after the stop starts nothing. Breaks if waiting
    /// senders pile up without end, or outlive the runtime that would hand on their clicks.
    /// </summary>
    [Fact]
    public async Task On_linux_waiting_toasts_are_bounded_and_end_with_the_runtime()
    {
        var closed = new List<int>();
        var ends = new List<TaskCompletionSource<LaunchResult>>();
        var (toast, launcher, _) = Channel(
            Facts(DesktopPlatform.Linux, 2, "notify-send"),
            new() { ["click"] = _ => Exit(0) },
            new()
            {
                ["notify-send"] = _ =>
                {
                    var end = new TaskCompletionSource<LaunchResult>();
                    ends.Add(end);
                    return Stays(end, closed, ends.Count);
                },
            });

        for (var shown = 0; shown < ToastChannel.MaxWaiting; shown++)
        {
            Is(await toast.DeliverAsync(Message, "ntf_" + shown, Ct), ChannelResult.Delivered);
        }

        Assert.Empty(closed);
        Is(await toast.DeliverAsync(Message, "ntf_more", Ct), ChannelResult.Delivered);
        Assert.Equal([1], closed);

        toast.Dispose();
        Assert.Equal(Enumerable.Range(1, ToastChannel.MaxWaiting + 1), closed.Order());
        ends[^1].TrySetResult(Exit(0, "52\ndefault\n"));
        Assert.DoesNotContain(launcher.Launches, launch => IsClick(launch.Launch));
    }

    /// <summary>
    /// notify-send's help is asked once per run and decides the toast: one that waits and hands on the click's token
    /// where it takes both options, one that waits without the token on 0.8.3, and the plain toast, which no click reaches,
    /// where it takes neither. Breaks if an older notify-send is given options it refuses, and shows nothing at all.
    /// </summary>
    [Theory]
    [InlineData("0.8.9")]
    [InlineData("0.8.3")]
    [InlineData("0.7.9")]
    public async Task On_linux_notify_send_s_help_is_asked_once_and_decides_the_toast(string version)
    {
        var (toast, launcher, _) = Channel(
            Facts(DesktopPlatform.Linux, 2, "notify-send"),
            new() { ["help"] = _ => Exit(0, ToastCommandsTests.Help(version)), ["notify-send"] = _ => Exit(0) });

        Is(await toast.DeliverAsync(Message, "ntf_1", Ct), ChannelResult.Delivered);
        Is(await toast.DeliverAsync(Message, "ntf_2", Ct), ChannelResult.Delivered);

        var shown = version switch
        {
            "0.8.9" => ToastCommands.LinuxNotifySend(NotifySend, Message, token: true).Command,
            "0.8.3" => ToastCommands.LinuxNotifySend(NotifySend, Message, token: false).Command,
            _ => ToastCommands.LinuxNotifySendPlain(NotifySend, Message).Command,
        };
        Assert.Equal([Help, shown, shown], launcher.Launches.Select(l => l.Launch.Command));
    }

    /// <summary>How long a row waits for what a click starts before it fails rather than hangs: a guard, never a measure.</summary>
    private static readonly TimeSpan Hang = TimeSpan.FromSeconds(30);

    /// <summary>A line the channel writes from a click, which comes after the delivery has answered.</summary>
    private static Task WhenLogged(RecordingLogs logs, string message) =>
        Task.Run(
            async () =>
            {
                while (logs.Count(message) == 0)
                {
                    await Task.Delay(10, Ct);
                }
            },
            Ct).WaitAsync(Hang, Ct);
}
