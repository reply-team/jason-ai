using Jason.Contracts.Notifications;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Notifications.Desktop;

/// <summary>
/// The operating system's own notification, shown by a short-lived program through the one door every child of this
/// product goes through, within five seconds: this executable's <c>toast-host</c> mode on Windows, <c>osascript</c> on
/// macOS, and <c>notify-send</c> or <c>gdbus</c> on Linux.
/// </summary>
/// <remarks>
/// <para>
/// Whether there is a desktop is decided once, when the runtime starts, from the session it runs in and its environment —
/// so <c>jason status</c> can say it before anything has ever been sent — and a runtime with none says so once in its log
/// and launches nothing.
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
    ILogger<ToastChannel> logger) : INotificationChannel
{
    /// <summary>The longest a toast launch may take before it is killed.</summary>
    public static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

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

    private int _said;

    private int _saidUnlinked;

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
        var gdbus = facts.Which("gdbus") is not null;

        // Where gdbus is there, the bus is asked whether a notification daemon owns its name, and, where none does yet,
        // whether it can start one: a daemon such as dunst owns its name only after its first notification. Without
        // gdbus nothing can be asked, and notify-send's own exit decides. A probe that cannot answer — it fails, or
        // outlives its bound — decides nothing either, the second as the first: only an answer says there is no daemon.
        if (gdbus)
        {
            var probe = await LaunchAsync(ToastCommands.LinuxDaemonProbe(), cancellationToken).ConfigureAwait(false);
            if (probe is { Started: true, TimedOut: false, ExitCode: 0 } && probe.Stdout.Contains("(false,)", StringComparison.Ordinal))
            {
                var activatable = await LaunchAsync(ToastCommands.LinuxActivatableProbe(), cancellationToken).ConfigureAwait(false);
                if (activatable is { Started: true, TimedOut: false, ExitCode: 0 }
                    && !activatable.Stdout.Contains("'org.freedesktop.Notifications'", StringComparison.Ordinal))
                {
                    return Skipped("no notification daemon");
                }
            }
        }

        var launch = facts.Which("notify-send") is not null ? ToastCommands.LinuxNotifySend(message) : ToastCommands.LinuxGdbus(message);
        var result = await LaunchAsync(launch, cancellationToken).ConfigureAwait(false);
        return Common(result) ?? (result.ExitCode == 0
            ? ChannelOutcome.Delivered(Name, Kind)
            : ChannelOutcome.Failed(Name, Kind, Failed, FailureClass.Permanent, result.LastStderrLine ?? $"exit {result.ExitCode}"));
    }

    /// <summary>A launch that never started, or was killed at its bound, means the same on every platform.</summary>
    private ChannelOutcome? Common(LaunchResult result) =>
        !result.Started ? ChannelOutcome.Failed(Name, Kind, ToastOutcomes.LaunchFailed, FailureClass.Transient, result.StartError)
        : result.TimedOut ? ChannelOutcome.Failed(Name, Kind, ToastOutcomes.Timeout, FailureClass.Ambiguous)
        : null;

    private Task<LaunchResult> LaunchAsync(ToastLaunch launch, CancellationToken cancellationToken) =>
        launcher.LaunchAsync(
            launch with { Environment = ToastCommands.Environment(new Dictionary<string, string>(facts.Environment), facts.Platform) },
            Bound,
            cancellationToken);

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
