using System.Globalization;
using System.Text.RegularExpressions;
using Jason.Contracts.Notifications;
using Jason.Runtime.Journal;
using Jason.Runtime.Notifications;
using Jason.Runtime.Notifications.Desktop;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The page that says what notifies, when, what it says, where it can appear and what it leaves — and the pages around
/// it that used to say nothing notifies anybody. Every number is read from the code that decides it and every word from
/// the constants a line carries, so a changed default or a new outcome fails here rather than on a page.
/// </summary>
public sealed partial class NotificationsDocTests
{
    private static string Page() => Flat(Source("docs/notifications.md"));

    [Theory]
    [InlineData("A notification is a pointer: how many approvals and questions are waiting, how many protective actions failed, how many accounts have events Jason did not read, and where to look. It decides nothing, and nothing is approved by reacting to it.")]
    [InlineData("Four things, and only these:")]
    [InlineData("**a protective action that failed** — work the catalogue runs even in a paused campaign, recording an opt-out or pausing a campaign, that ended failed or expired, in a campaign of any status.")]
    [InlineData("**What never travels:** the campaign's name, a contact's name or address, what an approval is about or its preview, a question's words or its options, the error a failed action ended with, the operation and its arguments, and any id of a work item or attempt. A campaign's public id travels only in the sentence about failed protective actions, at most three of them, to say where to look: an opaque `cmp_…` that names nothing by itself. Of an account whose events were not read, nothing travels but how many such accounts there are. The pointer is made from four counts, those ids, and nothing else.")]
    [InlineData("A first start walks no history: a protective failure journaled before the first start that ever wrote a line is not counted by a pointer — the work-item list is where it shows.")]
    [InlineData("A notification that reached nobody carries `delivered_through` forward unchanged, as it carries `last_delivered_at`, so the next one counts those failures again with any new ones. Neither key is sent to a channel: `delivered_through` is a position in the journal, and the count reaches a channel only as words in the message.")]
    [InlineData("Everything here is computed at a dispatcher scan from what the journal records, never from a timer inside the runtime, which ends at every sign-out.")]
    [InlineData("never less than two of the dispatcher's ticks")]
    [InlineData("Windows' Do not disturb is invisible to Jason: the notification goes to the notification center without a banner, and Jason records it delivered.")]
    [InlineData("Switched off in Settings → System → Notifications, the notification is not shown, and Jason records it silenced — not delivered and not failed.")]
    [InlineData("A notification is a pointer. Each channel's outcome is one of `delivered`; `failed`, with its code and class — a server still unreachable after its one retry is failed, and so is a delivery whose input the runtime's own half of the delivery contract refuses before the plugin runs, `input_invalid` (`validation`); or `not_attempted`, with its reason, for a channel that cannot reach anybody: `off`, `no_desktop`, `not_declared`, `unavailable` or `binding_invalid`. The toast has a fourth, `silenced`, for a notification Windows did not show because the person switched Jason off. No outcome is ever a failure of the parked item, the approval or the decision, and a delivery never reads, writes or re-times any of them.")]
    [InlineData("jason journal list --kind notification_sent")]
    [InlineData("`attempts` is written for every outcome but `not_attempted` — `delivered`, `silenced` and `failed` each say how many times the channel was tried.")]
    public void The_page_says_it(string sentence) => Assert.Contains(sentence, Page(), StringComparison.Ordinal);

    /// <summary>Where a pointer about failed protective work sends a person, as the composer writes it.</summary>
    [Fact]
    public void The_page_shows_where_a_pointer_about_failed_work_sends_a_person() =>
        Assert.Contains($"1 protective action failed in Jason, in cmp_01K…. {Jason.Runtime.Notifications.NotificationComposer.LookAtFailures}", Page(), StringComparison.Ordinal);

    /// <summary>Where a pointer about an account whose events were not read sends a person, as the composer writes it.</summary>
    [Fact]
    public void The_page_shows_where_a_pointer_about_unread_events_sends_a_person() =>
        Assert.Contains(Jason.Runtime.Notifications.NotificationComposer.Compose(0, 0, eventGaps: 1).Text, Page(), StringComparison.Ordinal);

    /// <summary>How a count past four digits is said, as the composer says it.</summary>
    [Fact]
    public void The_page_says_how_a_count_past_four_digits_is_said() =>
        Assert.Contains(
            $"A count past {Jason.Runtime.Notifications.NotificationComposer.MaxSaid.ToString(CultureInfo.InvariantCulture)} is said as \"{Jason.Runtime.Notifications.NotificationComposer.PastFourDigits}\", so the longest pointer fits whatever the counts are",
            Page(),
            StringComparison.Ordinal);

    /// <summary>The settings table is the code's own: every name the section binds, with its default.</summary>
    [Fact]
    public void The_settings_table_prints_the_settings_this_build_has_with_their_defaults()
    {
        var page = Source("docs/notifications.md");
        var defaults = new NotificationsOptions();
        (string Name, string Default)[] rows =
        [
            (nameof(NotificationsOptions.Enabled), "true"),
            (nameof(NotificationsOptions.Toast), "true"),
            (nameof(NotificationsOptions.QuietSeconds), defaults.QuietSeconds.ToString(CultureInfo.InvariantCulture)),
            (nameof(NotificationsOptions.MaxWaitSeconds), defaults.MaxWaitSeconds.ToString(CultureInfo.InvariantCulture)),
            (nameof(NotificationsOptions.MinIntervalMinutes), defaults.MinIntervalMinutes.ToString(CultureInfo.InvariantCulture)),
        ];

        Assert.True(defaults.Enabled && defaults.Toast);
        foreach (var (name, value) in rows)
        {
            Assert.Contains($"| `{NotificationsOptions.Section}:{name}` | `{value}` |", page, StringComparison.Ordinal);
        }

        Assert.Contains($"| `{NotificationsOptions.Section}:{nameof(NotificationsOptions.Plugins)}:<id>` |", page, StringComparison.Ordinal);
    }

    /// <summary>The ranges the table prints are the validator's own, read from the constants it checks against.</summary>
    [Fact]
    public void The_settings_table_prints_the_ranges_the_validator_holds()
    {
        var page = Page();
        static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
        Assert.Contains($"how long the newest line of a burst must be quiet, {Number(NotificationsOptions.QuietSecondsMin)} to {Number(NotificationsOptions.QuietSecondsMax)} |", page, StringComparison.Ordinal);
        Assert.Contains($"may wait, from `QuietSeconds` to {Number(NotificationsOptions.MaxWaitSecondsMax)} |", page, StringComparison.Ordinal);
        Assert.Contains($"reached anybody, {Number(NotificationsOptions.MinIntervalMinutesMin)} to {Number(NotificationsOptions.MinIntervalMinutesMax)}; 0 is no floor |", page, StringComparison.Ordinal);
    }

    /// <summary>"At most so many a day" is the floor's arithmetic at the defaults, said from the code.</summary>
    [Fact]
    public void The_page_says_how_many_a_day_the_floor_allows_at_the_defaults()
    {
        var perDay = 24 * 60 / new NotificationsOptions().MinIntervalMinutes;
        Assert.Contains($"at most {perDay.ToString(CultureInfo.InvariantCulture)} a day at the defaults", Page(), StringComparison.Ordinal);
    }

    /// <summary>The words a line can carry, each printed on the page, from the constants the line is written with.</summary>
    [Fact]
    public void The_page_prints_every_word_a_line_can_carry()
    {
        var page = Page();
        foreach (var word in ChannelResults.All.Concat(NotAttemptedReasons.All).Concat(SilencedReasons.All))
        {
            Assert.Contains($"`{word}`", page, StringComparison.Ordinal);
        }
    }

    /// <summary>The pages that used to say nothing notifies anybody, or that notification plugins are only reserved, say what is true now.</summary>
    [Theory]
    [InlineData("docs/plugins.md", "`kind: notification` plugins are invoked for the delivery contract, never for a canonical operation: §14")]
    [InlineData("docs/plugins.md", "a `notification` plugin was asked for a canonical operation, or a provider for a delivery")]
    [InlineData("docs/plugins.md", "**Notification plugins are not routed**: a route to one is refused at activation with `route_plugin_kind_not_invocable`; they are reached through the `Notifications` settings instead (§14).")]
    [InlineData("docs/plugins.md", "## 14. Writing a notification plugin")]
    [InlineData("docs/routing.md", "the plugin is a notification plugin, which performs no canonical operation; it is reached through the `Notifications` settings instead")]
    [InlineData("docs/routing.md", "rather than with what it is; it is reached through the `Notifications` settings instead.")]
    [InlineData("docs/work-execution.md", "A parked approval and a raised question are notified as a pointer ([notifications.md](notifications.md)); pull is still the floor: a person finds out what is waiting, and decides, by asking (`jason approval list`).")]
    [InlineData("docs/contracts/README.md", "## The delivery contract")]
    [InlineData("plugins/README.md", "| [`ntfy/`](ntfy/README.md) |")]
    [InlineData("docs/INSTALL.md", "**Three things are this account's rather than this installation's**: the logon registration, the notification registration on Windows, and the agent harnesses.")]
    [InlineData("docs/release-and-update.md", "A toast comes only from a runtime in the person's own session")]
    [InlineData("docs/release-and-update.md", "a runtime that 0.1.0's logon task started in session 0 shows none")]
    [InlineData("docs/release-and-update.md", "| on Windows, the notification registration | after the runtime, so its next toast cannot write it back")]
    [InlineData("CLAUDE.md", "Notifications: a parked approval, a raised question, failed protective work and an account whose events were not read are notified as a pointer — counts and where to look — by a scan step after the dispatch, delivered off the scan and recorded in a reserved `notification_sent` line. `docs/notifications.md` is the contract.")]
    [InlineData("CLAUDE.md", "The delivery contract, `notification.send`, is a family of its own under `docs/contracts/notifications/`, embedded the same way and never in the operation catalog.")]
    [InlineData("docs/contracts/notifications/notification.send.md", "sends **one pointer**: how many approvals and questions are waiting, whether protective actions failed, and where to look.")]
    [InlineData("docs/contracts/notifications/notification.send.md", "What the plugin receives is `title`, `message` and the two counts, `approvals` and `decisions`. The message may also say that protective actions failed, and name up to three campaigns by public id — an opaque `cmp_…` that names nothing by itself. Nothing else about the work travels: no campaign's name, no contact, no subject, no question's words, no error.")]
    [InlineData("docs/contracts/notifications/notification.send.json", "\"intent\": \"Put a pointer in front of a person: how many approvals and questions are waiting in Jason, whether protective actions failed, and where to look. Nothing else about the work travels.\"")]
    [InlineData("plugins/ntfy/README.md", "**Nothing else about the work travels**: the runtime makes the pointer from counts and, when protective actions failed, at most three campaign ids, and the delivery contract, [`notification.send`](../../docs/contracts/notifications/notification.send.md), says exactly what a pointer may carry.")]
    [InlineData("plugins/README.md", "the delivery contract `notification.send`: Jason's pointer — the counts the runtime composes, and where to look — published to an ntfy topic.")]
    [InlineData("plugins/ntfy/plugin.yaml", "description: Delivers Jason's notifications to an ntfy topic - the pointer the runtime composes, and nothing else.")]
    [InlineData("docs/plugins.md", "A notification plugin carries Jason's pointer — how many approvals and questions are waiting, whether protective actions failed, and where to look — to a service a person reads.")]
    [InlineData("docs/contracts/README.md", "put a pointer in front of a person: how many approvals and questions wait, whether protective actions failed, and where to look.")]
    public void The_pages_around_it_say_what_is_true_now(string page, string sentence) =>
        Assert.Contains(sentence, Flat(Source(page)), StringComparison.Ordinal);

    /// <summary>
    /// The architecture's register of accepted decisions records what a notification is. The journal kind the row
    /// names is the one the sender writes, and it is reserved: only the runtime writes it.
    /// </summary>
    [Fact]
    public void The_decision_register_records_what_a_notification_is()
    {
        var architecture = Source("docs/architecture.md");
        var start = architecture.IndexOf("\n## 18. Accepted decision register", StringComparison.Ordinal);
        var end = architecture.IndexOf("\n## 19. ", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "docs/architecture.md no longer has §18 followed by §19.");

        Assert.Contains(
            $"| DEC-NOTIFY-001 | A notification is a pointer (counts and where to look), never a decision surface; made by a deterministic scan step with no work item and no AI in the path; recorded as the reserved journal line `{JournalKinds.NotificationSent}` with no campaign; a channel that cannot reach anybody is recorded and skipped, never a failure of the parked item. |",
            architecture[start..end],
            StringComparison.Ordinal);
        Assert.Contains(JournalKinds.NotificationSent, JournalKinds.Reserved);
    }

    /// <summary>What a page claims has to be as narrow as what the code does.</summary>
    [Theory]
    [InlineData("docs/notifications.md", "the next sign-in sends what is still waiting and was not said yet — once, after the start grace")]
    [InlineData("docs/notifications.md", "A pointer already sent is not sent again at the next sign-in: there are no reminders.")]
    [InlineData("docs/notifications.md", "The floor allows at most 48 a day at the defaults that reach somebody")]
    [InlineData("docs/notifications.md", "A Linux runtime with `notify-send` and a session bus but no notification daemon and no `gdbus` cannot ask whether a daemon is there, so its toast is recorded `failed`, `toast_failed`; `Notifications:Toast` set to `false` turns it off.")]
    [InlineData("docs/notifications.md", "a Linux runtime with no session bus, or with no notification daemon running and none the bus can start on demand, record the toast as `not_attempted`, `no_desktop`.")]
    [InlineData("plugins/ntfy/plugin.yaml", "canonical operation, so this manifest has no `operations` list.")]
    [InlineData("docs/notifications.md", "At every toast the toast host ensures the key")]
    [InlineData("docs/notifications.md", "with `DisplayName` `Jason` — written the first time, only read after that — from an ordinary prompt.")]
    [InlineData("plugins/ntfy/README.md", "`http_scheme_not_allowed`")]
    [InlineData("plugins/ntfy/README.md", "a server other than on the loopback interface is reached over `https`")]
    [InlineData("plugins/README.md", "**A provider plugin here is reached because a route sends work to it.**")]
    [InlineData("plugins/README.md", "A notification plugin is reached because the `Notifications` settings name it")]
    public void The_pages_claim_no_more_than_the_code_does(string page, string sentence) =>
        Assert.Contains(sentence, Flat(Source(page)), StringComparison.Ordinal);

    /// <summary>
    /// The log file is compact JSON: a failed channel's line carries the message template under <c>"@mt"</c> and the
    /// values as properties, so that is what the page shows — the template the sender logs with, not a rendering of it.
    /// </summary>
    [Fact]
    public void The_page_shows_a_failed_channel_s_log_line_as_the_file_holds_it()
    {
        var page = Page();
        Assert.Contains($"\"@mt\":\"{NotificationSender.ChannelFailedTemplate}\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Notification channel <name> failed", page, StringComparison.Ordinal);

        // And the rest of the line as the file holds it: the event's id and name, and the logger it came from.
        var @event = NotificationSender.ChannelFailedEvent;
        Assert.Contains($"\"EventId\":{{\"Id\":{@event.Id},\"Name\":\"{@event.Name}\"}}", page, StringComparison.Ordinal);
        Assert.Contains($"\"SourceContext\":\"{typeof(NotificationSender).FullName}\"", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// A Linux toast's worst case, as the page states it: a probe that cannot answer decides nothing, so one toast can
    /// take three launches, each at the channel's own bound, and none of them is on the scan.
    /// </summary>
    [Fact]
    public void The_page_states_a_linux_toast_s_worst_case_at_the_channel_s_bound()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), ToastChannel.Bound);
        var page = Page();
        Assert.Contains("A probe that cannot answer — it fails, or outlives its bound — decides nothing, and the notification is sent; so one Linux toast can take three launches, each bounded at five seconds, fifteen in all, none of them on the scan.", page, StringComparison.Ordinal);
    }

    /// <summary>Every plugin in the marketplace's index is a row of one table: a blank line ends a Markdown table.</summary>
    [Fact]
    public void The_marketplace_index_is_one_table()
    {
        var lines = Source("plugins/README.md").Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var ntfy = Array.FindIndex(lines, line => line.StartsWith("| [`ntfy/`]", StringComparison.Ordinal));
        Assert.True(ntfy > 0, "the index has no ntfy row");
        Assert.StartsWith("|", lines[ntfy - 1], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("docs/plugins.md", "invoking `kind: notification` plugins")]
    [InlineData("docs/plugins.md", "`kind: notification` is reserved")]
    [InlineData("docs/plugins.md", "its operation contract is not written yet")]
    [InlineData("docs/work-execution.md", "nothing notifies anybody")]
    [InlineData("plugins/README.md", "A reference notification plugin will land beside it.")]
    [InlineData("plugins/README.md", "and, later, deliver notifications")]
    [InlineData("plugins/ntfy/plugin.yaml", "under `operations` below")]
    [InlineData("docs/notifications.md", "Before its first toast the runtime writes")]
    public void The_pages_no_longer_say_what_stopped_being_true(string page, string retired) =>
        Assert.DoesNotContain(retired, Flat(Source(page)), StringComparison.Ordinal);

    /// <summary>
    /// What the walk on a standard account proved, and only that: the record names the platform and the build, says
    /// each outcome in the words the code writes, and ends with what was not checked. The section's claims about the
    /// toast host say no more than the walk saw.
    /// </summary>
    [Fact]
    public void The_page_records_what_the_walk_proved_and_what_it_did_not()
    {
        var page = Page();
        var from = page.IndexOf("Checked by hand so far:", StringComparison.Ordinal);
        var to = page.IndexOf("**Not checked:**", from + 1, StringComparison.Ordinal);
        Assert.True(from >= 0 && to > from, "The page has no hand-check block.");
        var record = page[from..to].Replace("> ", string.Empty, StringComparison.Ordinal);

        Assert.Contains("Windows, on 2026-10-01, on a standard account", record, StringComparison.Ordinal);
        Assert.Contains("Windows 11 24H2 (build 26100)", record, StringComparison.Ordinal);

        // The build walked was a development build of the change, not a release: the record says so.
        Assert.Contains("with a development build of 0.2.0 (`0.2.0-dev`), the change as it stood when walked, installed", record, StringComparison.Ordinal);
        Assert.DoesNotContain("this version", record, StringComparison.Ordinal);
        Assert.Contains("over 0.1.0, which came from 0.1.0's own release assets", record, StringComparison.Ordinal);
        Assert.Contains("installing 0.1.0 over the network was not measured while the repository is private", record, StringComparison.Ordinal);
        Assert.Contains("one toast and one ntfy message per burst", record, StringComparison.Ordinal);
        Assert.Contains($"\"{NotificationComposer.Compose(5, 0).Summary}\"", record, StringComparison.Ordinal);
        Assert.Contains("no window came from Jason's process tree", record, StringComparison.Ordinal);
        Assert.Contains("A raised question was notified", record, StringComparison.Ordinal);
        Assert.Contains("A sign-out inside the quiet window sent nothing", record, StringComparison.Ordinal);
        Assert.Contains($"`{ChannelResults.Failed}`, `http_host_not_allowed`, `permanent`", record, StringComparison.Ordinal);
        Assert.Contains($"recorded `{ChannelResults.Silenced}` (`{SilencedReasons.DisabledForApplication}`)", record, StringComparison.Ordinal);
        Assert.Contains("the check stayed ok, with the Settings sentence, because ntfy still reached the person", record, StringComparison.Ordinal);
        Assert.Contains($"left none of the {(ToastRegistration.Keys.Count == 3 ? "three" : "?")} keys", record, StringComparison.Ordinal);
        Assert.Contains("under WSL (Ubuntu 26.04, kernel 6.6)", record, StringComparison.Ordinal);
        Assert.Contains($"`{ChannelResults.NotAttempted}`, `{NotAttemptedReasons.NoDesktop}`, \"no notification daemon\"", record, StringComparison.Ordinal);
        Assert.Contains(
            "**Not checked:** macOS, which is composed and asserted only; Windows before 24H2; a runtime in session 0, and the "
                + "remedy for one that 0.1.0's logon task started; a toast actually shown on Linux, where the walk's machine had no "
                + "notification daemon; and Do not disturb.",
            page.Replace("> ", string.Empty, StringComparison.Ordinal),
            StringComparison.Ordinal);

        // The toast host's own section claims no more than the walk saw: no lifetime it did not measure.
        Assert.DoesNotContain("lives about 140 ms", page, StringComparison.Ordinal);
    }

    private static string Flat(string text) => Whitespace().Replace(text, " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    private static string Source(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory.FullName, relative.Replace('/', Path.DirectorySeparatorChar)));
    }
}
