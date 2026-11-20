using Jason.Contracts.Notifications;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Notifications.Desktop;

/// <summary>
/// The operating system's own notification, shown by a program through the one door every child of this product goes
/// through, within five seconds: this executable's <c>toast-host</c> mode on Windows, <c>osascript</c> on macOS, and
/// <c>notify-send</c> or <c>gdbus</c> on Linux.
/// </summary>
/// <remarks>
/// <para>
/// Whether there is a desktop is decided once, when the runtime starts, from the session it runs in and its environment —
/// so <c>jason status</c> can say it before anything has ever been sent — and a runtime with none says so once in its log
/// and launches nothing.
/// </para>
/// <para>
/// On Linux notify-send stays on the bus with the notification it showed, until the person clicks it or dismisses it, so
/// that the click reaches it: GNOME and KDE hand a click only to a sender still there. A click starts this executable with
/// the toast's <c>jason:</c> link, as a Windows toast's click does, which opens Jason Desktop on the item or the list, or
/// opens nothing and says why; where it opened nothing, a notification says why, and nothing else opens. The runtime holds
/// at most <see cref="MaxWaiting"/> such senders, ending the oldest before it shows another, and ends every one when it
/// stops: a notification shown by a runtime that stopped is no longer clicked through. gdbus, where notify-send is not
/// there, shows a notification no click reaches.
/// </para>
/// <para>
/// A toast shown while the person has switched Jason off is <c>silenced</c>, not <c>delivered</c>: nobody saw it, nothing
/// broke, and Show was called — so it is none of the three outcomes a delivery reports. Do not disturb is invisible to
/// Jason: Windows lists such a toast in the notification center, and it is recorded delivered.
/// </para>
/// </remarks>
public sealed class ToastChannel(
    IToastLauncher launcher,
    IPluginHostLocator self,
    DesktopFacts facts,
    Presence presence,
    ILogger<ToastChannel> logger) : INotificationChannel, IDisposable
{
    /// <summary>The longest a toast launch may take before it is killed.</summary>
    public static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

    /// <summary>How long a click's handler is given: its own hand-on to the system's opener is ten seconds.</summary>
    public static readonly TimeSpan ClickBound = TimeSpan.FromSeconds(30);

    /// <summary>The most Linux toasts whose click this runtime still takes; another ends the oldest first.</summary>
    public const int MaxWaiting = 10;

    /// <summary>What a program that ran and said it could not show the toast is recorded as.</summary>
    public const string Failed = "toast_failed";

    private static readonly Action<ILogger, string, Exception?> NoDesktop = LoggerMessage.Define<string>(
        LogLevel.Information,
        new EventId(1, nameof(NoDesktop)),
        "No desktop: toasts are not shown by this runtime ({Detail})");

    private static readonly Action<ILogger, string, string, Exception?> Unlinked = LoggerMessage.Define<string, string>(
        LogLevel.Warning,
        new EventId(2, nameof(Unlinked)),
        "The jason: link of this account's toasts is {Scheme} ({Hr}): a click on a toast opens nothing of Jason's");

    private static readonly Action<ILogger, Exception?> ClickHandedOn = LoggerMessage.Define(
        LogLevel.Information,
        new EventId(3, nameof(ClickHandedOn)),
        "A notification's click was handed to Jason Desktop");

    private static readonly Action<ILogger, int, Exception?> ClickOpenedNothing = LoggerMessage.Define<int>(
        LogLevel.Warning,
        new EventId(4, nameof(ClickOpenedNothing)),
        "A notification's click opened nothing (exit {ExitCode}); a notification said why");

    private readonly List<WaitingLaunch> _waiting = [];

    private readonly CancellationTokenSource _stopping = new();

    /// <summary>Which toast notify-send shows, asked of its help once the first toast is shown and kept for the run.</summary>
    private NotifySendForm? _form;

    private int _said;

    private int _saidUnlinked;

    private int _stopped;

    public string Name => "toast";

    public string Kind => ChannelKinds.Toast;

    /// <summary>Whether this runtime has a desktop to show a toast on, decided when it started.</summary>
    public Presence Presence => presence;

    /// <summary>A stand-in for the toast while it is turned off and a plugin is named: the line says so.</summary>
    public static INotificationChannel Off { get; } = new OffChannel();

    public async Task<ChannelOutcome> DeliverAsync(NotificationMessage message, string notificationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!presence.Available)
        {
            if (Interlocked.Exchange(ref _said, 1) == 0)
            {
                NoDesktop(logger, presence.Detail ?? "unknown", null);
            }

            return Skipped(presence.Detail);
        }

        return facts.Platform switch
        {
            DesktopPlatform.Windows => await WindowsAsync(message, cancellationToken).ConfigureAwait(false),
            DesktopPlatform.MacOs => await MacOsAsync(message, cancellationToken).ConfigureAwait(false),
            _ => await LinuxAsync(message, cancellationToken).ConfigureAwait(false),
        };
    }

    /// <summary>
    /// The toast host's answer, and once per run a line saying the toasts' <c>jason:</c> link is not Jason's: the toast
    /// is delivered all the same, and the line is the one place a click that opens nothing is explained.
    /// </summary>
    private async Task<ChannelOutcome> WindowsAsync(NotificationMessage message, CancellationToken cancellationToken)
    {
        var result = await LaunchAsync(ToastCommands.Windows(self.Command, message), cancellationToken).ConfigureAwait(false);
        if (ToastOutcomes.Unlinked(result) is { } link && Interlocked.Exchange(ref _saidUnlinked, 1) == 0)
        {
            Unlinked(logger, link.Scheme, link.Hr, null);
        }

        return ToastOutcomes.Read(result);
    }

    private async Task<ChannelOutcome> MacOsAsync(NotificationMessage message, CancellationToken cancellationToken)
    {
        var result = await LaunchAsync(ToastCommands.MacOs(message), cancellationToken).ConfigureAwait(false);
        return Common(result) ?? (result.ExitCode == 0
            ? ChannelOutcome.Delivered(Name, Kind)
            : Skipped($"osascript failed: {result.LastStderrLine ?? $"exit {result.ExitCode}"}"));
    }

    private async Task<ChannelOutcome> LinuxAsync(NotificationMessage message, CancellationToken cancellationToken)
    {
        // Each program by the full path the search path gives for it, and started by that path.
        var gdbus = facts.Which("gdbus");
        var notifySend = facts.Which("notify-send");

        // Where gdbus is there, the bus is asked whether a notification daemon owns its name, and, where none does yet,
        // whether it can start one: a daemon such as dunst owns its name only after its first notification. Without
        // gdbus nothing can be asked, and notify-send's own exit decides. A probe that cannot answer — it fails, or
        // outlives its bound — decides nothing either, the second as the first: only an answer says there is no daemon.
        if (gdbus is not null)
        {
            var probe = await LaunchAsync(ToastCommands.LinuxDaemonProbe(gdbus), cancellationToken).ConfigureAwait(false);
            if (probe is { Started: true, TimedOut: false, ExitCode: 0 } && probe.Stdout.Contains("(false,)", StringComparison.Ordinal))
            {
                var activatable = await LaunchAsync(ToastCommands.LinuxActivatableProbe(gdbus), cancellationToken).ConfigureAwait(false);
                if (activatable is { Started: true, TimedOut: false, ExitCode: 0 }
                    && !activatable.Stdout.Contains("'org.freedesktop.Notifications'", StringComparison.Ordinal))
                {
                    return Skipped("no notification daemon");
                }
            }
        }

        // Neither there: what a program that could not be started answers, with nothing started.
        if (notifySend is null && gdbus is null)
        {
            return ChannelOutcome.Failed(Name, Kind, ToastOutcomes.LaunchFailed, FailureClass.Transient, "Neither notify-send nor gdbus is a program on the search path.");
        }

        if (notifySend is null)
        {
            return Ended(await LaunchAsync(ToastCommands.LinuxGdbus(gdbus!, message), cancellationToken).ConfigureAwait(false));
        }

        var form = await FormAsync(notifySend, cancellationToken).ConfigureAwait(false);
        if (form is not { Waits: true })
        {
            // A notify-send before 0.7.10, or one whose help said nothing: the toast as it takes it, which no click reaches.
            return Ended(await LaunchAsync(ToastCommands.LinuxNotifySendPlain(notifySend, message), cancellationToken).ConfigureAwait(false));
        }

        // The item a click opens stays here, made into the toast's link from the counted id; the notification never
        // carries it.
        var link = AttentionLinks.Item(message.Item) ?? AttentionLinks.List;
        MakeRoom();
        var launch = ToastCommands.LinuxNotifySend(notifySend, message, form.Token);
        var waiting = await launcher.LaunchWaitingAsync(launch with { Environment = Environment(launch) }, Bound, line => ToastCommands.ShownId(line) is not null, cancellationToken).ConfigureAwait(false);
        if (waiting.End is not { } end)
        {
            // Ended within the bound: dismissed or clicked already, or refused.
            var said = waiting.Said;
            if (said is { Started: true, ExitCode: 0 } && ToastCommands.ShownId(said.Stdout) != "0" && ToastCommands.Clicked(said.Stdout))
            {
                _ = ClickAsync(link, said, notifySend);
            }

            return ToastCommands.ShownId(said.Stdout) == "0" ? NotShown(said) : Ended(said);
        }

        if (ToastCommands.ShownId(waiting.Said.Stdout) == "0")
        {
            // notify-send could not show it, and would wait on the bus for nothing.
            waiting.Close();
            return NotShown(await end.ConfigureAwait(false));
        }

        Hold(waiting, link, notifySend);
        return ChannelOutcome.Delivered(Name, Kind);
    }

    /// <summary>
    /// Which toast this notify-send shows, from its help (<see cref="ToastCommands.LinuxForm"/>), asked once and kept for
    /// the run; a help that did not answer is asked again at the next toast, which is shown plain meanwhile.
    /// </summary>
    private async Task<NotifySendForm?> FormAsync(string notifySend, CancellationToken cancellationToken)
    {
        if (_form is { } known)
        {
            return known;
        }

        var help = await LaunchAsync(ToastCommands.LinuxNotifySendHelp(notifySend), cancellationToken).ConfigureAwait(false);
        return help is { Started: true, TimedOut: false, ExitCode: 0 } ? _form = ToastCommands.LinuxForm(help.Stdout) : null;
    }

    /// <summary>A Linux toast's program that ended: delivered where it exited 0, else failed by what it said last.</summary>
    private ChannelOutcome Ended(LaunchResult result) =>
        Common(result) ?? (result.ExitCode == 0
            ? ChannelOutcome.Delivered(Name, Kind)
            : ChannelOutcome.Failed(Name, Kind, Failed, FailureClass.Permanent, result.LastStderrLine ?? $"exit {result.ExitCode}"));

    /// <summary>notify-send that said the id 0: it showed nothing.</summary>
    private ChannelOutcome NotShown(LaunchResult result) =>
        ChannelOutcome.Failed(Name, Kind, Failed, FailureClass.Permanent, result.LastStderrLine ?? "notify-send showed no notification");

    /// <summary>Room for one more waiting toast: the oldest ended while there are <see cref="MaxWaiting"/>.</summary>
    private void MakeRoom()
    {
        List<WaitingLaunch> oldest;
        lock (_waiting)
        {
            oldest = [.. _waiting.Take(Math.Max(0, _waiting.Count - MaxWaiting + 1))];
            _waiting.RemoveRange(0, oldest.Count);
        }

        // Outside the lock: an end can come at once, and its watcher takes the lock.
        foreach (var waiting in oldest)
        {
            waiting.Close();
        }
    }

    /// <summary>A shown toast held until its program ends: a click then opens what <paramref name="link"/> names.</summary>
    private void Hold(WaitingLaunch waiting, string link, string notifySend)
    {
        lock (_waiting)
        {
            _waiting.Add(waiting);
        }

        _ = WatchAsync(waiting, link, notifySend);
    }

    private async Task WatchAsync(WaitingLaunch waiting, string link, string notifySend)
    {
        var end = await waiting.End!.ConfigureAwait(false);
        lock (_waiting)
        {
            _waiting.Remove(waiting);
        }

        if (Volatile.Read(ref _stopped) == 0 && end.ExitCode == 0 && ToastCommands.Clicked(end.Stdout))
        {
            await ClickAsync(link, end, notifySend).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// A click: this executable started with the toast's link, in the session's environment as the user manager holds it
    /// now (<see cref="ToastCommands.ClickEnvironment"/>), as a launcher would start it, with the activation token the
    /// server gave where it gave one; and where it opened nothing, a notification of what it said last, its sentence of
    /// why. Its words are the handler's own and reach no log.
    /// </summary>
    private async Task ClickAsync(string link, LaunchResult clicked, string notifySend)
    {
        try
        {
            var session = new Dictionary<string, string>(StringComparer.Ordinal);
            if (facts.Which("systemctl") is { } systemctl
                && await LaunchAsync(ToastCommands.LinuxSessionEnvironment(systemctl), _stopping.Token).ConfigureAwait(false) is { Started: true, TimedOut: false, ExitCode: 0 } read)
            {
                session = ToastCommands.SessionEnvironment(read.Stdout);
            }

            var environment = ToastCommands.ClickEnvironment(facts.Environment, session, ToastCommands.ActivationToken(clicked.Stdout));

            var scope = facts.Environment.ContainsKey("INVOCATION_ID") ? facts.Which("systemd-run") : null;
            var result = await launcher.LaunchAsync(new ToastLaunch(ToastCommands.LinuxClick(self.Command, link, scope), null, environment), ClickBound, _stopping.Token).ConfigureAwait(false);
            if (result is { Started: true, TimedOut: false, ExitCode: 0 })
            {
                ClickHandedOn(logger, null);
                return;
            }

            ClickOpenedNothing(logger, result.ExitCode ?? -1, null);
            var why = result.ExitCode == 1 && result.LastStderrLine is { } line ? line : "Nothing was opened: Jason could not hand the click to Jason Desktop.";
            await LaunchAsync(ToastCommands.LinuxSaid(notifySend, ToastRegistration.DisplayName, why), _stopping.Token).ConfigureAwait(false);
        }
        catch (Exception stopped) when (stopped is OperationCanceledException or ObjectDisposedException && Volatile.Read(ref _stopped) == 1)
        {
            // The runtime stops: a click it had not handed on is the stop's.
        }
    }

    /// <summary>Every toast still waiting ended with the runtime, and no click started after.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 1)
        {
            return;
        }

        _stopping.Cancel();
        List<WaitingLaunch> all;
        lock (_waiting)
        {
            all = [.. _waiting];
            _waiting.Clear();
        }

        foreach (var waiting in all)
        {
            waiting.Close();
        }

        _stopping.Dispose();
    }

    /// <summary>A launch that never started, or was killed at its bound, means the same on every platform.</summary>
    private ChannelOutcome? Common(LaunchResult result) =>
        !result.Started ? ChannelOutcome.Failed(Name, Kind, ToastOutcomes.LaunchFailed, FailureClass.Transient, result.StartError)
        : result.TimedOut ? ChannelOutcome.Failed(Name, Kind, ToastOutcomes.Timeout, FailureClass.Ambiguous)
        : null;

    private Task<LaunchResult> LaunchAsync(ToastLaunch launch, CancellationToken cancellationToken) =>
        launcher.LaunchAsync(launch with { Environment = Environment(launch) }, Bound, cancellationToken);

    /// <summary>The child's environment (<see cref="ToastCommands.Environment"/>), with what the launch adds of its own.</summary>
    private Dictionary<string, string> Environment(ToastLaunch launch)
    {
        var environment = ToastCommands.Environment(new Dictionary<string, string>(facts.Environment), facts.Platform);
        foreach (var (name, value) in launch.Environment)
        {
            environment[name] = value;
        }

        return environment;
    }

    private ChannelOutcome Skipped(string? detail) =>
        ChannelOutcome.NotAttempted(Name, Kind, NotAttemptedReasons.NoDesktop, detail);

    private sealed class OffChannel : INotificationChannel
    {
        public string Name => "toast";

        public string Kind => ChannelKinds.Toast;

        public Task<ChannelOutcome> DeliverAsync(NotificationMessage message, string notificationId, CancellationToken cancellationToken) =>
            Task.FromResult(ChannelOutcome.NotAttempted(Name, Kind, NotAttemptedReasons.Off));
    }
}
