using Jason.Cli.Status;
using Jason.Cli.Tests.Commands;
using Jason.Contracts.Api;

namespace Jason.Cli.Tests.Status;

/// <summary>
/// The optional <c>notifications</c> check of <c>jason status</c>: whether anything tells a person when Jason starts
/// waiting for them. Read from the <c>system.info</c> the command already asks for, one fact per state. A person's own
/// choice — notifications switched off, a machine with no desktop — is never a failure; a runtime in session 0 is,
/// because on a desktop it is most likely a runtime a 0.1.0 logon task left running, and its remedy is acts in order.
/// </summary>
public sealed class NotificationsCheckTests
{
    private static readonly DateTimeOffset NineOhSix = new(2026, 10, 1, 9, 6, 0, TimeSpan.Zero);

    private const string SessionZero =
        "This runtime runs in session 0, where nothing it shows reaches a desktop. A service or an SSH session runs there, "
        + "and so does a runtime that 0.1.0's logon task started. If 0.1.0's task started it: from an ordinary prompt, run "
        + "`jason runtime autostart enable` (or `disable`) once — if that answers Access is denied, follow its message first — "
        + "then `jason runtime stop` once, then `jason runtime start` (or sign in again, with autostart enabled). "
        + "0.1.0's task ran the runtime for the whole machine, in session 0; this version's task runs it in your own session, "
        + "where what it shows reaches you.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static NotificationChannelInfo Toast(string? outcome = null, string? reason = null, string? detail = null, bool on = true, string presence = "available", string? presenceDetail = null) =>
        new("toast", "toast", on, outcome, reason, null, null, detail, outcome is null ? null : NineOhSix, presence, presenceDetail);

    private static NotificationChannelInfo Ntfy(string? outcome = null, string? reason = null, string? code = null, string? @class = null, string? detail = null) =>
        new("ntfy", "plugin", true, outcome, reason, code, @class, detail, outcome is null ? null : NineOhSix);

    /// <summary>A runtime started an hour after the outcomes the tests write: everything it reports is an earlier runtime's.</summary>
    private static readonly DateTimeOffset AnHourLater = NineOhSix.AddHours(1);

    private static async Task<StatusCheck> CheckAsync(NotificationsInfo? notifications, bool running = true, DateTimeOffset? startedAt = null)
    {
        using var dir = new TempPaths();
        if (running)
        {
            StatusCommandTests.Running(dir, notifications: notifications, startedAt: startedAt);
        }

        var output = new StringWriter();
        await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), Ct);

        var report = StatusCommandTests.Read(output);
        var check = Assert.Single(report.Checks, check => check.Name == "notifications");
        Assert.False(check.Required);
        Assert.Null(check.Fix);
        return check;
    }

    private static void Is(StatusCheck check, CheckState state, string fact) =>
        Assert.Equal((state, fact), (check.State, check.Fact));

    [Fact]
    public async Task A_runtime_that_did_not_answer_is_not_known() =>
        Is(await CheckAsync(null, running: false), CheckState.Unknown, "The runtime did not answer, so whether its notifications reach anybody is not known.");

    [Fact]
    public async Task A_runtime_older_than_notifications_does_not_say() =>
        Is(await CheckAsync(null), CheckState.Unknown, "This runtime does not say what its notifications did.");

    [Fact]
    public async Task Notifications_off_is_absent() =>
        Is(
            await CheckAsync(new NotificationsInfo(false, [Toast(on: false)], null, null)),
            CheckState.Absent,
            "Notifications are off: nothing tells anybody when Jason starts waiting for them.");

    [Fact]
    public async Task No_channel_on_is_absent() =>
        Is(
            await CheckAsync(new NotificationsInfo(true, [Toast(on: false)], null, null)),
            CheckState.Absent,
            "Notifications are off: nothing tells anybody when Jason starts waiting for them.");

    /// <summary>
    /// A toast that found session 0 is a failure, and its remedy is the acts §8 names, in order. It never advises
    /// stopping the runtime alone: that would leave the old logon task to start another one at the next sign-in; and it
    /// says to start one again afterwards.
    /// </summary>
    [Fact]
    public async Task A_toast_that_found_session_0_fails_and_names_both_acts_in_order()
    {
        var check = await CheckAsync(new NotificationsInfo(true, [Toast("not_attempted", "no_desktop", "session 0", presence: "no_desktop", presenceDetail: "session 0")], "ntf_01K", NineOhSix));

        Is(check, CheckState.Failed, SessionZero);
        BothActsInOrder(check.Fact);
    }

    /// <summary>
    /// And before anything has been sent: whether there is a desktop is decided when the runtime starts, so a person
    /// upgrading from 0.1.0 sees it the first time they ask rather than after something has parked.
    /// </summary>
    [Fact]
    public async Task A_fresh_runtime_in_session_0_that_has_sent_nothing_fails_the_same_way()
    {
        var check = await CheckAsync(new NotificationsInfo(true, [Toast(presence: "no_desktop", presenceDetail: "session 0")], null, null));

        Is(check, CheckState.Failed, SessionZero);
        BothActsInOrder(check.Fact);
    }

    [Fact]
    public async Task A_machine_with_no_desktop_by_design_is_absent()
    {
        Is(
            await CheckAsync(new NotificationsInfo(true, [Toast("not_attempted", "no_desktop", "no session bus", presence: "no_desktop", presenceDetail: "no session bus")], "ntf_01K", NineOhSix)),
            CheckState.Absent,
            "No desktop for this runtime to show a notification on (no session bus). `Notifications:Toast` set to `false` turns the toast off.");

        Is(
            await CheckAsync(new NotificationsInfo(true, [Toast(presence: "no_desktop", presenceDetail: "no notification program")], null, null)),
            CheckState.Absent,
            "No desktop for this runtime to show a notification on (no notification program). `Notifications:Toast` set to `false` turns the toast off.");
    }

    [Theory]
    [InlineData("disabled_for_application", "Windows has Jason's notifications switched off (Settings → System → Notifications → Jason).")]
    [InlineData("disabled_for_user", "Windows has notifications switched off for every app.")]
    [InlineData("disabled_by_group_policy", "A group policy on this machine switches Windows' notifications off; an administrator sets it.")]
    [InlineData("disabled_by_manifest", "Windows says this program's own manifest switches its notifications off.")]
    public async Task A_toast_the_person_switched_off_is_absent(string reason, string fact) =>
        Is(await CheckAsync(new NotificationsInfo(true, [Toast("silenced", reason)], "ntf_01K", NineOhSix)), CheckState.Absent, fact);

    [Fact]
    public async Task A_channel_that_failed_fails_and_says_how() =>
        Is(
            await CheckAsync(new NotificationsInfo(true, [Toast("delivered"), Ntfy("failed", code: "notification_unreachable", @class: "transient", detail: "the server did not answer")], "ntf_01K", NineOhSix)),
            CheckState.Failed,
            "ntfy: notification_unreachable (transient) at 2026-10-01 09:06 UTC: the server did not answer.");

    [Theory]
    [InlineData("not_declared")]
    [InlineData("unavailable")]
    [InlineData("binding_invalid")]
    public async Task A_named_plugin_that_was_not_attempted_fails_and_says_why(string reason) =>
        Is(
            await CheckAsync(new NotificationsInfo(true, [Toast("delivered"), Ntfy("not_attempted", reason, detail: "the detail")], "ntf_01K", NineOhSix)),
            CheckState.Failed,
            $"ntfy was not attempted at 2026-10-01 09:06 UTC: {reason}: the detail.");

    [Fact]
    public async Task Channels_that_reach_you_are_ok() =>
        Is(
            await CheckAsync(new NotificationsInfo(true, [Toast("delivered"), Ntfy("delivered")], "ntf_01K", NineOhSix)),
            CheckState.Ok,
            "Notifications reach you by toast and ntfy; the last one was delivered at 2026-10-01 09:06 UTC.");

    [Fact]
    public async Task Channels_that_have_sent_nothing_yet_are_ok() =>
        Is(
            await CheckAsync(new NotificationsInfo(true, [Toast()], null, null)),
            CheckState.Ok,
            "Notifications reach you by toast; nothing has needed one yet.");

    /// <summary>
    /// Whether there is a desktop is this runtime's own fact, decided when it started. A toast outcome a line from an
    /// earlier runtime carried never outweighs it: after the person has stopped the runtime a 0.1.0 task left in session
    /// 0, the runtime that replaced it is not told to do it again.
    /// </summary>
    [Fact]
    public async Task A_session_0_outcome_from_an_earlier_runtime_does_not_fail_a_runtime_with_a_desktop() =>
        Is(
            await CheckAsync(new NotificationsInfo(true, [Toast("not_attempted", "no_desktop", "session 0")], "ntf_01K", NineOhSix), startedAt: AnHourLater),
            CheckState.Ok,
            "Notifications reach you by toast; none has reached you since this runtime started.");

    /// <summary>
    /// What a restart actually hands over: the runtime does not carry an earlier one's "no desktop" into its own status,
    /// so the toast comes with no outcome at all, beside a last notification older than this runtime. Nothing has
    /// reached the person since it started — which is not "yet".
    /// </summary>
    [Fact]
    public async Task A_toast_with_no_outcome_after_a_restart_has_sent_nothing_since_this_runtime_started() =>
        Is(
            await CheckAsync(new NotificationsInfo(true, [Toast()], "ntf_01K", NineOhSix), startedAt: AnHourLater),
            CheckState.Ok,
            "Notifications reach you by toast; none has reached you since this runtime started.");

    /// <summary>
    /// The other side of that line: a channel with no outcome beside a notification this runtime sent itself — a plugin
    /// named after it went out — has sent nothing yet.
    /// </summary>
    [Fact]
    public async Task A_channel_with_no_outcome_beside_this_runtimes_own_notification_has_sent_nothing_yet() =>
        Is(
            await CheckAsync(new NotificationsInfo(true, [Ntfy()], "ntf_01K", NineOhSix), startedAt: NineOhSix.AddHours(-1)),
            CheckState.Ok,
            "Notifications reach you by ntfy; none has reached you yet.");

    /// <summary>
    /// And on the line itself: a last notification sent at the very moment this runtime started is its own, as an
    /// outcome written then is, so a channel with no outcome beside it has sent nothing yet.
    /// </summary>
    [Fact]
    public async Task A_last_notification_sent_as_the_runtime_started_is_its_own() =>
        Is(
            await CheckAsync(new NotificationsInfo(true, [Toast()], "ntf_01K", NineOhSix), startedAt: NineOhSix),
            CheckState.Ok,
            "Notifications reach you by toast; none has reached you yet.");

    /// <summary>
    /// A desktop at start is no promise of one at each send: a Linux session bus with no notification daemon on it, or
    /// an osascript that would not run, is this runtime's own finding, and nothing reaches anybody.
    /// </summary>
    [Fact]
    public async Task This_runtimes_own_send_that_found_no_desktop_is_absent_and_says_why() =>
        Is(
            await CheckAsync(new NotificationsInfo(true, [Toast("not_attempted", "no_desktop", "no notification daemon")], "ntf_01K", NineOhSix)),
            CheckState.Absent,
            "No desktop for this runtime to show a notification on (no notification daemon). `Notifications:Toast` set to `false` turns the toast off.");

    /// <summary>
    /// A toast the person turned off, then on again: the last line still says it was off, which was their own choice,
    /// and it is on now. Nothing has been sent through it since — which is not "yet": toasts may have reached the person
    /// before it was turned off.
    /// </summary>
    [Fact]
    public async Task A_toast_turned_back_on_whose_last_line_says_off_has_sent_nothing_since() =>
        Is(
            await CheckAsync(new NotificationsInfo(true, [Toast("not_attempted", "off")], "ntf_01K", NineOhSix)),
            CheckState.Ok,
            "Notifications reach you by toast; none has reached you since the toast was turned on again.");

    /// <summary>
    /// And after a restart: the person's own earlier "off" is theirs whichever runtime wrote it, so a runtime that
    /// started after the line reads it the same way.
    /// </summary>
    [Fact]
    public async Task A_toast_turned_back_on_reads_the_same_after_a_restart() =>
        Is(
            await CheckAsync(new NotificationsInfo(true, [Toast("not_attempted", "off")], "ntf_01K", NineOhSix), startedAt: AnHourLater),
            CheckState.Ok,
            "Notifications reach you by toast; none has reached you since the toast was turned on again.");

    /// <summary>
    /// The plural, which is defensive and unreachable today: only the toast is ever written as off, and a plugin turned
    /// off is not a channel at all, so no runtime produces two channels on again. Were one ever to, both are named.
    /// </summary>
    [Fact]
    public async Task Two_channels_turned_back_on_are_named_together()
    {
        NotificationChannelInfo pushover = new("pushover", "plugin", true, "not_attempted", "off", null, null, null, NineOhSix);
        Is(
            await CheckAsync(new NotificationsInfo(true, [Toast("not_attempted", "no_desktop", "session 0"), Ntfy("not_attempted", "off"), pushover], "ntf_01K", NineOhSix), startedAt: AnHourLater),
            CheckState.Ok,
            "Notifications reach you by toast and ntfy and pushover; none has reached you since ntfy and pushover were turned on again.");
    }

    /// <summary>An outcome written at the very moment this runtime started is this runtime's own.</summary>
    [Fact]
    public async Task An_outcome_written_as_the_runtime_started_is_its_own() =>
        Is(
            await CheckAsync(new NotificationsInfo(true, [Toast("not_attempted", "no_desktop", "no notification daemon")], "ntf_01K", NineOhSix), startedAt: NineOhSix),
            CheckState.Absent,
            "No desktop for this runtime to show a notification on (no notification daemon). `Notifications:Toast` set to `false` turns the toast off.");

    /// <summary>And the other way: a runtime in session 0 fails whatever the last line it inherited said.</summary>
    [Fact]
    public async Task A_runtime_in_session_0_fails_whatever_the_last_toast_it_inherited_said()
    {
        var check = await CheckAsync(new NotificationsInfo(true, [Toast("delivered", presence: "no_desktop", presenceDetail: "session 0")], "ntf_01K", NineOhSix));

        Is(check, CheckState.Failed, SessionZero);
        BothActsInOrder(check.Fact);
    }

    /// <summary>The time in the fact is when a channel that reaches the person last delivered, not when anything was last tried.</summary>
    [Fact]
    public async Task The_last_delivery_named_is_a_delivery_by_a_channel_that_reaches_the_person() =>
        Is(
            await CheckAsync(new NotificationsInfo(true, [Toast("silenced", "disabled_for_application"), Ntfy()], "ntf_01K", NineOhSix)),
            CheckState.Ok,
            "Notifications reach you by ntfy; none has reached you yet. "
            + "Windows has Jason's notifications switched off (Settings → System → Notifications → Jason).");

    /// <summary>A toast switched off beside a plugin that delivers still reaches the person, through the plugin.</summary>
    [Fact]
    public async Task A_silenced_toast_beside_a_delivering_plugin_is_ok_and_says_so() =>
        Is(
            await CheckAsync(new NotificationsInfo(true, [Toast("silenced", "disabled_for_application"), Ntfy("delivered")], "ntf_01K", NineOhSix)),
            CheckState.Ok,
            "Notifications reach you by ntfy; the last one was delivered at 2026-10-01 09:06 UTC. "
            + "Windows has Jason's notifications switched off (Settings → System → Notifications → Jason).");

    /// <summary>An optional check never fails readiness, whatever its state.</summary>
    [Fact]
    public async Task A_failed_notifications_check_does_not_make_the_installation_unready()
    {
        using var dir = new TempPaths();
        StatusCommandTests.Running(dir, notifications: new NotificationsInfo(true, [Toast(presence: "no_desktop", presenceDetail: "session 0")], null, null));
        var output = new StringWriter();

        var exit = await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), Ct);

        Assert.True(StatusCommandTests.Read(output).Ready);
        Assert.Equal(0, exit);
    }

    [Fact]
    public async Task Help_names_the_check_among_the_optional_ones()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();

        await CliApp.RunAsync(["status", "--help"], new CliEnvironment(output, new StringWriter(), dir.Paths), Ct);

        var text = string.Join(' ', output.ToString().Split((char[])[' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains("autostart is registered · notifications reach you", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The acts in their order: the registration first, with what to do when it is refused; the stop never without it
    /// before; and a runtime started again after the stop, or the person would be left with none.
    /// </summary>
    private static void BothActsInOrder(string fact)
    {
        var enable = fact.IndexOf("`jason runtime autostart enable`", StringComparison.Ordinal);
        var denied = fact.IndexOf("Access is denied", StringComparison.Ordinal);
        var stop = fact.IndexOf("`jason runtime stop`", StringComparison.Ordinal);
        var start = fact.IndexOf("`jason runtime start`", StringComparison.Ordinal);
        Assert.True(enable >= 0 && denied > enable && stop > denied && start > stop, $"the acts are not named in order: {fact}");
    }
}
