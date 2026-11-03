using System.Text.Json.Nodes;
using Jason.Contracts.Notifications;
using Jason.Contracts.Plugins;
using Jason.Runtime.Configuration;
using Jason.Runtime.Journal;
using Jason.Runtime.Notifications;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Dispatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Tests.Notifications;

/// <summary>
/// The sender: off the scan, one notification at a time, every channel side by side, a transient failure tried once
/// more after thirty seconds, and one <c>notification_sent</c> line for what happened.
/// </summary>
public sealed class NotificationSenderTests
{
    private static readonly DateTime Start = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DispatchHarness Harness()
    {
        var harness = new DispatchHarness(Start);
        harness.NotificationsOn();
        return harness;
    }

    private static NotificationSender Sender(DispatchHarness harness, params INotificationChannel[] channels) => new(
        harness.Scopes,
        new FakeChannels(channels),
        harness.NotifierState,
        harness.NotificationStatus,
        harness.Resolve<LiveSettings<NotificationsOptions>>(),
        harness.Clock,
        harness.Resolve<ILogger<NotificationSender>>());

    private static NotificationRequest Request(string? coveredThrough = "jrn_covered", DateTime? lastDeliveredAt = null) =>
        new("ntf_test", coveredThrough, lastDeliveredAt, new Burst(5, Start, Start));

    private static async Task<List<JournalEntry>> LinesAsync(DispatchHarness harness)
    {
        await using var db = harness.Open();
        return await db.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.NotificationSent).OrderBy(e => e.Id).ToListAsync(Ct);
    }

    [Fact]
    [Critical(Family.N3, "notification_sent line")]
    public async Task One_notification_is_one_line_with_every_channel_s_outcome()
    {
        using var harness = Harness();
        await Waiting.ApprovalsAsync(harness, 3, Ct);
        await Waiting.DecisionAsync(harness, Ct);
        await harness.WriteLineAsync(JournalKinds.ApprovalRequested, Ct);
        var toast = FakeChannel.Delivering();
        var other = FakeChannel.Skipped(NotAttemptedReasons.NoDesktop, name: "second");

        var line = await Sender(harness, toast, other).SendAsync(Request(), Ct);

        var entry = Assert.Single(await LinesAsync(harness));
        Assert.Equal("notification", entry.Key);
        Assert.Equal(Jason.Runtime.Domain.Actors.Dispatcher, new Jason.Contracts.Api.ActorRef(entry.ActorType, entry.ActorId));
        Assert.Null(entry.CampaignId);
        Assert.Null(entry.WorkItemId);
        Assert.Null(entry.AttemptId);

        var written = entry.New!.AsObject();
        Assert.Equal("ntf_test", written["notification_id"]!.GetValue<string>());
        Assert.Equal(3, written["counts"]!["approvals"]!.GetValue<int>());
        Assert.Equal(1, written["counts"]!["decisions"]!.GetValue<int>());
        Assert.Equal("jrn_covered", written["covered_through"]!.GetValue<string>());
        Assert.Equal(["delivered", "not_attempted"], written["channels"]!.AsArray().Select(c => c!["outcome"]!.GetValue<string>()));
        Assert.Equal(line!.ToJson().ToJsonString(), written.ToJsonString());

        // Every channel was given the same pointer, made of the counts at send time.
        Assert.Equal("3 approvals and 1 question are waiting in Jason.", Assert.Single(toast.Messages).Summary);
        Assert.Equal(toast.Messages, other.Messages);
    }

    /// <summary>The one approval that waits is read beside the counts, and travels to the channels by its public id alone.</summary>
    [Fact]
    public async Task The_one_approval_that_waits_is_given_by_its_id()
    {
        using var harness = Harness();
        await Waiting.ApprovalsAsync(harness, 1, Ct);
        string approval;
        await using (var db = harness.Open())
        {
            approval = (await db.Approvals.AsNoTracking().SingleAsync(Ct)).PublicId;
        }

        var toast = FakeChannel.Delivering();
        await Sender(harness, toast).SendAsync(Request(), Ct);

        var message = Assert.Single(toast.Messages);
        Assert.Equal(approval, message.Item);
        Assert.Equal(NotificationComposer.Compose(1, 0).Text, message.Text);
    }

    [Fact]
    public async Task The_one_question_that_waits_is_given_by_its_id()
    {
        using var harness = Harness();
        var question = await Waiting.DecisionAsync(harness, Ct);
        var toast = FakeChannel.Delivering();

        await Sender(harness, toast).SendAsync(Request(), Ct);

        Assert.Equal(question.Id, Assert.Single(toast.Messages).Item);
    }

    /// <summary>More than one thing waiting is no item: a click opens the list, and no id is read.</summary>
    [Fact]
    public async Task Two_things_waiting_give_no_item()
    {
        using var harness = Harness();
        await Waiting.ApprovalsAsync(harness, 1, Ct);
        await Waiting.DecisionAsync(harness, Ct);
        var toast = FakeChannel.Delivering();

        await Sender(harness, toast).SendAsync(Request(), Ct);

        Assert.Null(Assert.Single(toast.Messages).Item);
    }

    /// <summary>
    /// Everything was answered between the hand-over and the send: nothing waits, so nothing is said, nothing goes back,
    /// and the notification is released.
    /// </summary>
    [Fact]
    public async Task The_counts_are_read_when_it_is_sent()
    {
        using var harness = Harness();
        await Waiting.ApprovalsAsync(harness, 2, Ct);
        await Waiting.AnswerEverythingAsync(harness, Ct);
        var toast = FakeChannel.Delivering();
        var sender = Sender(harness, toast);
        harness.NotifierState.Start(0, null, null, new Burst(2, Start, Start));
        Assert.True(harness.NotifierState.TryHandOver((_, _) => true, out var request));
        Assert.True(harness.NotifierState.InFlight);

        await sender.StartAsync(Ct);
        sender.Enqueue(request);
        Assert.True(await DispatchHarness.EventuallyAsync(() => !harness.NotifierState.InFlight, Ct), "the notification was never released");
        await sender.StopAsync(Ct);

        Assert.Empty(await LinesAsync(harness));
        Assert.Equal(0, toast.Calls);
        Assert.Null(harness.NotifierState.Pending);
    }

    /// <summary>
    /// Every channel was turned off between the hand-over and the send: nothing is said and no line is written, and the
    /// burst goes back, so it is said once a channel is on again.
    /// </summary>
    [Fact]
    public async Task Everything_turned_off_before_the_send_says_nothing_and_keeps_the_burst()
    {
        using var harness = Harness();
        await Waiting.ApprovalsAsync(harness, 1, Ct);
        var toast = FakeChannel.Delivering();
        var sender = Sender(harness, toast);
        harness.NotifierState.Start(0, null, null, new Burst(1, Start, Start));
        Assert.True(harness.NotifierState.TryHandOver((_, _) => true, out var request));
        harness.NotificationsOn(options => options.Enabled = false);

        var line = await sender.SendAsync(request, Ct);

        Assert.Null(line);
        Assert.Empty(await LinesAsync(harness));
        Assert.Equal(0, toast.Calls);
        Assert.False(harness.NotifierState.InFlight);
        Assert.Equal(1, harness.NotifierState.Pending?.Lines);
    }

    [Fact]
    public async Task A_transient_failure_is_tried_once_more_after_thirty_seconds()
    {
        using var harness = Harness();
        await Waiting.ApprovalsAsync(harness, 1, Ct);
        var ntfy = new FakeChannel(
            "ntfy",
            ChannelKinds.Plugin,
            ChannelOutcome.Failed("ntfy", ChannelKinds.Plugin, "notification_unreachable", FailureClass.Transient),
            ChannelOutcome.Delivered("ntfy", ChannelKinds.Plugin, receipt: "msg-1"));

        var sending = Sender(harness, ntfy).SendAsync(Request(), Ct);
        Assert.True(await DispatchHarness.EventuallyAsync(() => harness.Clock.Armed >= 1, Ct), "the retry never armed its wait");
        Assert.Equal(1, ntfy.Calls);

        harness.Clock.Advance(TimeSpan.FromSeconds(29));
        Assert.Equal(1, ntfy.Calls);
        harness.Clock.Advance(TimeSpan.FromSeconds(1));
        var line = await sending;

        var outcome = Assert.Single(line!.Channels);
        Assert.Equal((ChannelResult.Delivered, 2, "msg-1"), (outcome.Result, outcome.Attempts, outcome.Receipt));
        Assert.Equal(2, ntfy.Calls);
    }

    [Fact]
    public async Task A_server_still_unreachable_after_its_retry_is_failed()
    {
        using var harness = Harness();
        await Waiting.ApprovalsAsync(harness, 1, Ct);
        var ntfy = FakeChannel.Failing("notification_unreachable", FailureClass.Transient);

        var sending = Sender(harness, ntfy).SendAsync(Request(), Ct);
        Assert.True(await DispatchHarness.EventuallyAsync(() => harness.Clock.Armed >= 1, Ct));
        harness.Clock.Advance(NotificationSender.RetryDelay);
        var line = await sending;

        var outcome = Assert.Single(line!.Channels);
        Assert.Equal((ChannelResult.Failed, "notification_unreachable", FailureClass.Transient, 2), (outcome.Result, outcome.Code, outcome.Class, outcome.Attempts));
        Assert.Equal(["Notification channel ntfy failed (notification_unreachable, transient)"], harness.Logs.Warnings);
    }

    [Theory]
    [InlineData(FailureClass.Permanent)]
    [InlineData(FailureClass.Validation)]
    [InlineData(FailureClass.Ambiguous)]
    public async Task Permanent_validation_and_ambiguous_failures_are_final(FailureClass @class)
    {
        using var harness = Harness();
        await Waiting.ApprovalsAsync(harness, 1, Ct);
        var ntfy = FakeChannel.Failing("notification_rejected", @class);

        var line = await Sender(harness, ntfy).SendAsync(Request(), Ct);

        Assert.Equal(1, ntfy.Calls);
        Assert.Equal((ChannelResult.Failed, 1), (Assert.Single(line!.Channels).Result, line.Channels[0].Attempts));
    }

    /// <summary>A channel that throws is a defect in that channel: it is recorded, and the others' outcomes stand.</summary>
    [Fact]
    [Critical(Family.N3, "channel isolation")]
    public async Task A_channel_that_throws_is_one_failed_outcome_and_the_others_stand()
    {
        using var harness = Harness();
        await Waiting.ApprovalsAsync(harness, 1, Ct);
        var broken = new FakeChannel("broken", ChannelKinds.Plugin) { Throws = true };

        var line = await Sender(harness, broken, FakeChannel.Delivering()).SendAsync(Request(), Ct);

        Assert.Equal(
            [(ChannelResult.Failed, "notification_channel_fault", (FailureClass?)FailureClass.Ambiguous), (ChannelResult.Delivered, null, null)],
            line!.Channels.Select(o => (o.Result, o.Code, o.Class)));
    }

    /// <summary>Channels exist but none could reach anybody: the line is still written, and the next start continues above it.</summary>
    [Fact]
    public async Task When_every_channel_was_skipped_the_line_is_still_written()
    {
        using var harness = Harness();
        await Waiting.ApprovalsAsync(harness, 1, Ct);

        var line = await Sender(harness, FakeChannel.Skipped(NotAttemptedReasons.NoDesktop)).SendAsync(Request(), Ct);

        Assert.NotNull(line);
        Assert.Single(await LinesAsync(harness));
        Assert.Equal(ChannelResult.NotAttempted, Assert.Single(line.Channels).Result);
    }

    [Fact]
    public async Task The_floor_counts_from_the_last_notification_that_reached_anybody()
    {
        using var harness = Harness();
        await Waiting.ApprovalsAsync(harness, 1, Ct);
        var earlier = Start.AddMinutes(-45);

        var skipped = await Sender(harness, FakeChannel.Skipped(NotAttemptedReasons.NoDesktop)).SendAsync(Request(lastDeliveredAt: earlier), Ct);
        Assert.Equal(earlier, skipped!.LastDeliveredAt);
        Assert.Equal(earlier, harness.NotifierState.LastDeliveredAt);

        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        var delivered = await Sender(harness, FakeChannel.Delivering()).SendAsync(Request(lastDeliveredAt: earlier), Ct);
        Assert.Equal(harness.Clock.Now.UtcDateTime, delivered!.LastDeliveredAt);
        Assert.Equal(harness.Clock.Now.UtcDateTime, harness.NotifierState.LastDeliveredAt);
    }

    /// <summary>
    /// A runtime that stops while it sends writes no line. Its successor reads the same trigger lines above the last
    /// line written and, after its start grace, sends once: at least once, never silently none.
    /// </summary>
    [Fact]
    public async Task A_stop_while_sending_writes_no_line_and_the_next_start_sends_once()
    {
        using var harness = Harness();
        var covered = await harness.WriteLineAsync(JournalKinds.WorkItemCreated, Ct);
        await harness.WriteLineAsync(JournalKinds.NotificationSent, Ct, new JsonObject { ["covered_through"] = covered.PublicId });
        await Waiting.ApprovalsAsync(harness, 1, Ct);

        var channel = new FakeChannel("toast", ChannelKinds.Toast, ChannelOutcome.Delivered("toast", ChannelKinds.Toast)) { WaitUntilCancelled = true };
        var sender = Sender(harness, channel);
        harness.NotifierState.Start(0, covered.PublicId, null, new Burst(1, Start, Start));
        Assert.True(harness.NotifierState.TryHandOver((_, _) => true, out var request));
        await sender.StartAsync(Ct);
        sender.Enqueue(request);
        Assert.True(await DispatchHarness.EventuallyAsync(() => channel.Calls == 1, Ct), "the channel was never asked");
        Assert.True(harness.NotifierState.InFlight);

        await sender.StopAsync(Ct);
        Assert.Single(await LinesAsync(harness));
        Assert.False(harness.NotifierState.InFlight);

        // The successor: a state that has read nothing, over the same database.
        var successor = new NotifierState();
        await harness.NotifyAsync(successor, Ct);
        Assert.Equal(1, successor.Pending!.Lines);

        harness.Clock.Advance(TimeSpan.FromSeconds(60));
        Assert.Equal(1, await harness.NotifyAsync(successor, Ct));
    }

    /// <summary>
    /// A line that could not be written after a channel delivered still keeps the floor: the person was told, and the
    /// next pointer waits its interval rather than following at once.
    /// </summary>
    [Fact]
    public async Task A_delivery_whose_line_could_not_be_written_still_keeps_the_floor()
    {
        using var harness = Harness();
        await Waiting.ApprovalsAsync(harness, 1, Ct);
        var channel = FakeChannel.Delivering();
        var sender = Sender(harness, channel);
        harness.InterfereOnceBeforeSaving(() => throw new InvalidOperationException("the disk is full"));
        harness.NotifierState.Start(0, null, null, new Burst(1, Start, Start));
        Assert.True(harness.NotifierState.TryHandOver((_, _) => true, out var request));

        await sender.StartAsync(Ct);
        sender.Enqueue(request);
        Assert.True(await DispatchHarness.EventuallyAsync(() => channel.Calls == 1 && !harness.NotifierState.InFlight, Ct));
        await sender.StopAsync(Ct);

        Assert.Equal(harness.Clock.Now.UtcDateTime, harness.NotifierState.LastDeliveredAt);
        Assert.Null(harness.NotifierState.Pending);
    }

    /// <summary>
    /// A notification that reached nobody and could not be recorded is not lost: its burst goes back, and the next scan
    /// hands it over again.
    /// </summary>
    [Fact]
    public async Task A_notification_that_reached_nobody_and_could_not_be_recorded_is_handed_over_again()
    {
        using var harness = Harness();
        await Waiting.ApprovalsAsync(harness, 1, Ct);
        var channel = FakeChannel.Skipped(NotAttemptedReasons.NoDesktop);
        var sender = Sender(harness, channel);
        harness.InterfereOnceBeforeSaving(() => throw new InvalidOperationException("the disk is full"));
        harness.NotifierState.Start(0, null, null, new Burst(3, Start, Start));
        Assert.True(harness.NotifierState.TryHandOver((_, _) => true, out var request));

        await sender.StartAsync(Ct);
        sender.Enqueue(request);
        Assert.True(await DispatchHarness.EventuallyAsync(() => channel.Calls == 1 && !harness.NotifierState.InFlight, Ct));
        await sender.StopAsync(Ct);

        Assert.Equal(3, harness.NotifierState.Pending?.Lines);
        Assert.Null(harness.NotifierState.LastDeliveredAt);
    }

    /// <summary>The sender is the queue: a second notification while one is being sent is refused, not lost silently.</summary>
    [Fact]
    public void A_second_notification_while_one_waits_is_refused()
    {
        using var harness = Harness();
        var sender = Sender(harness);
        sender.Enqueue(Request());
        Assert.Throws<InvalidOperationException>(() => sender.Enqueue(Request()));
    }

    /// <summary>What <c>system.info</c> will show: the last notification and each channel's last outcome.</summary>
    [Fact]
    public async Task The_status_keeps_the_last_outcome_of_each_channel()
    {
        using var harness = Harness();
        await Waiting.ApprovalsAsync(harness, 1, Ct);

        await Sender(harness, FakeChannel.Delivering(), FakeChannel.Failing("notification_rejected", FailureClass.Permanent)).SendAsync(Request(), Ct);

        var status = harness.NotificationStatus.Snapshot();
        Assert.Equal("ntf_test", status.LastNotificationId);
        Assert.Equal(Start, status.LastNotificationAt!.Value.UtcDateTime);
        Assert.Equal(
            [("ntfy", ChannelResult.Failed), ("toast", ChannelResult.Delivered)],
            status.Channels.OrderBy(c => c.Outcome.Channel, StringComparer.Ordinal).Select(c => (c.Outcome.Channel, c.Outcome.Result)));
    }
}
