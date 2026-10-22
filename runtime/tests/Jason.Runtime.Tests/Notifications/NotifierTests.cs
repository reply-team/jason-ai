using System.Text.Json.Nodes;
using Jason.Runtime.Journal;
using Jason.Runtime.Notifications;
using Jason.Runtime.Tests.Approvals;
using Jason.Runtime.Tests.Dispatch;

namespace Jason.Runtime.Tests.Notifications;

/// <summary>
/// The scan's last step: what it reads, when it hands a notification over, and what it never does. Each test seeds
/// journal lines at the harness clock's times and drives the scan the dispatcher runs.
/// </summary>
public sealed class NotifierTests
{
    private static readonly DateTime Start = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A harness with the notifier on and its state started over an empty journal.</summary>
    private static async Task<DispatchHarness> StartedAsync(Action<NotificationsOptions>? configure = null)
    {
        var harness = new DispatchHarness(Start);
        harness.NotificationsOn(configure);
        await harness.Runner.ScanOnceAsync(Ct);
        Assert.True(harness.NotifierState.Started);
        return harness;
    }

    [Fact]
    public async Task A_park_and_a_raise_are_noticed_and_nothing_else_is()
    {
        using var harness = await StartedAsync();
        foreach (var kind in (string[])[
            JournalKinds.ApprovalRequested,
            JournalKinds.DecisionRaised,
            JournalKinds.WorkItemAwaitingApproval,
            JournalKinds.ApprovalApproved,
            JournalKinds.DecisionAnswered,
            JournalKinds.WorkItemCreated])
        {
            await harness.WriteLineAsync(kind, Ct);
        }

        harness.Clock.Advance(TimeSpan.FromSeconds(60));
        var report = await harness.Runner.ScanOnceAsync(Ct);

        Assert.Equal(1, report.Notified);
        var request = Assert.Single(harness.Queue.Requests);
        Assert.Equal(2, request.Burst.Lines);
        Assert.StartsWith("ntf_", request.NotificationId, StringComparison.Ordinal);

        // And it is not handed over twice: the first is still being sent.
        await harness.Runner.ScanOnceAsync(Ct);
        Assert.Single(harness.Queue.Requests);
    }

    [Fact]
    public async Task A_burst_of_parks_ten_seconds_apart_is_one_notification()
    {
        using var harness = await StartedAsync();
        var handedAt = new List<DateTime>();

        for (var park = 0; park < 30; park++)
        {
            harness.Clock.Advance(TimeSpan.FromSeconds(10));
            await harness.WriteLineAsync(JournalKinds.ApprovalRequested, Ct);
            await ScanAndNoteAsync();
        }

        var last = harness.Clock.Now.UtcDateTime;
        for (var tick = 0; tick < 6; tick++)
        {
            harness.Clock.Advance(TimeSpan.FromSeconds(10));
            await ScanAndNoteAsync();
        }

        var request = Assert.Single(harness.Queue.Requests);
        Assert.Equal(30, request.Burst.Lines);
        Assert.Equal([last.AddSeconds(60)], handedAt);

        async Task ScanAndNoteAsync()
        {
            if ((await harness.Runner.ScanOnceAsync(Ct)).Notified == 1)
            {
                handedAt.Add(harness.Clock.Now.UtcDateTime);
            }
        }
    }

    /// <summary>A new parking for changed input is a new <c>apr_</c>, a new question for the person.</summary>
    [Fact]
    public async Task A_new_parking_for_changed_input_is_a_new_trigger()
    {
        using var harness = await StartedAsync();
        await harness.WriteLineAsync(JournalKinds.ApprovalSuperseded, Ct);
        await harness.WriteLineAsync(JournalKinds.ApprovalRequested, Ct);

        harness.Clock.Advance(TimeSpan.FromSeconds(60));
        await harness.Runner.ScanOnceAsync(Ct);

        Assert.Equal(1, Assert.Single(harness.Queue.Requests).Burst.Lines);
    }

    /// <summary>
    /// Every channel off reads nothing and writes nothing, and the watermark stays where it was: turned on later, the
    /// lines since the last notification are read then, and give one pointer.
    /// </summary>
    [Fact]
    public async Task Nothing_is_read_or_handed_over_while_every_channel_is_off()
    {
        using var harness = new DispatchHarness(Start);
        harness.Status.RuntimeStartedAt = Start;
        var covered = await harness.WriteLineAsync(JournalKinds.WorkItemCreated, Ct);
        await harness.WriteLineAsync(JournalKinds.NotificationSent, Ct, Line(covered.PublicId, lastDeliveredAt: null));
        await harness.WriteLineAsync(JournalKinds.ApprovalRequested, Ct);

        harness.Clock.Advance(TimeSpan.FromSeconds(60));
        await harness.Runner.ScanOnceAsync(Ct);
        harness.Clock.Advance(TimeSpan.FromSeconds(60));
        await harness.Runner.ScanOnceAsync(Ct);

        Assert.Empty(harness.Queue.Requests);
        Assert.False(harness.NotifierState.Started);

        harness.NotificationsOn();
        await harness.Runner.ScanOnceAsync(Ct);

        Assert.Equal(1, Assert.Single(harness.Queue.Requests).Burst.Lines);
    }

    [Fact]
    [Critical(Family.N3, "one in flight")]
    public async Task Only_one_notification_is_in_flight()
    {
        using var harness = await StartedAsync();
        await harness.WriteLineAsync(JournalKinds.ApprovalRequested, Ct);
        harness.Clock.Advance(TimeSpan.FromSeconds(60));
        await harness.Runner.ScanOnceAsync(Ct);
        Assert.Single(harness.Queue.Requests);

        await harness.WriteLineAsync(JournalKinds.DecisionRaised, Ct);
        await harness.WriteLineAsync(JournalKinds.ApprovalRequested, Ct);
        harness.Clock.Advance(TimeSpan.FromSeconds(120));
        await harness.Runner.ScanOnceAsync(Ct);

        Assert.Single(harness.Queue.Requests);
        Assert.True(harness.NotifierState.InFlight);
        Assert.Equal(2, harness.NotifierState.Pending!.Lines);

        // The sender is done with the first: the next scan hands the second over.
        harness.NotifierState.Release(harness.Queue.Requests[0].NotificationId);
        await harness.Runner.ScanOnceAsync(Ct);

        Assert.Equal(2, harness.Queue.Requests.Count);
        Assert.Equal(2, harness.Queue.Requests[1].Burst.Lines);
    }

    /// <summary>
    /// A failure in the notifier while a notification is being sent leaves that notification in flight: it releases
    /// only a hand-over it made itself. Otherwise a second could be handed over during the first send, and the
    /// sender's own release would then free the second's flag in turn.
    /// </summary>
    [Fact]
    public async Task A_read_that_fails_while_a_notification_is_in_flight_leaves_it_in_flight()
    {
        using var harness = await StartedAsync();
        await harness.WriteLineAsync(JournalKinds.ApprovalRequested, Ct);
        harness.Clock.Advance(TimeSpan.FromSeconds(60));
        await harness.Runner.ScanOnceAsync(Ct);
        Assert.True(harness.NotifierState.InFlight);

        await harness.WriteLineAsync(JournalKinds.ApprovalRequested, Ct);
        harness.Clock.Advance(TimeSpan.FromSeconds(60));
        harness.Commands.FailNextJournalRead();
        await harness.Runner.ScanOnceAsync(Ct);

        Assert.True(harness.NotifierState.InFlight);
        Assert.Contains("The notifier failed; the scan's work was already handed out", harness.Logs.Warnings);

        harness.Clock.Advance(TimeSpan.FromSeconds(60));
        await harness.Runner.ScanOnceAsync(Ct);
        Assert.Single(harness.Queue.Requests);

        harness.NotifierState.Release(harness.Queue.Requests[0].NotificationId);
        await harness.Runner.ScanOnceAsync(Ct);
        Assert.Equal(2, harness.Queue.Requests.Count);
    }

    /// <summary>A hand-over the queue refused is not lost: the burst goes back, and the next scan hands it over again.</summary>
    [Fact]
    public async Task A_hand_over_the_queue_refused_is_handed_over_again()
    {
        using var harness = await StartedAsync();
        await harness.WriteLineAsync(JournalKinds.ApprovalRequested, Ct);
        harness.Clock.Advance(TimeSpan.FromSeconds(60));

        harness.Queue.Refuse = true;
        await harness.Runner.ScanOnceAsync(Ct);
        Assert.False(harness.NotifierState.InFlight);
        Assert.Equal(1, harness.NotifierState.Pending!.Lines);

        harness.Queue.Refuse = false;
        await harness.Runner.ScanOnceAsync(Ct);
        Assert.Equal(1, Assert.Single(harness.Queue.Requests).Burst.Lines);
    }

    /// <summary>Until the runtime has a start to count the grace from, nothing is handed over.</summary>
    [Fact]
    public async Task Nothing_is_handed_over_before_the_runtime_has_a_start_time()
    {
        using var harness = new DispatchHarness(Start);
        harness.NotificationsOn();
        harness.Status.RuntimeStartedAt = null;
        await harness.Runner.ScanOnceAsync(Ct);
        await harness.WriteLineAsync(JournalKinds.ApprovalRequested, Ct);

        harness.Clock.Advance(TimeSpan.FromMinutes(5));
        await harness.Runner.ScanOnceAsync(Ct);
        Assert.Empty(harness.Queue.Requests);

        harness.Status.RuntimeStartedAt = Start;
        await harness.Runner.ScanOnceAsync(Ct);
        Assert.Single(harness.Queue.Requests);
    }

    /// <summary>
    /// A start reads from the line the last notification covered through, not from the line itself: what was written
    /// while it was being sent is read again. What it covered is never read again, and the floor counts from the last
    /// delivery it carries forward. Both reads are bounded, and their shapes are the ones named in the code.
    /// </summary>
    [Fact]
    public async Task A_start_continues_from_the_last_notification_line()
    {
        using var harness = new DispatchHarness(Start);
        harness.NotificationsOn();
        await harness.WriteLineAsync(JournalKinds.ApprovalRequested, Ct);
        var covered = await harness.WriteLineAsync(JournalKinds.WorkItemCreated, Ct);
        await harness.WriteLineAsync(JournalKinds.NotificationSent, Ct, Line(covered.PublicId, Start.AddMinutes(-10)));
        await harness.WriteLineAsync(JournalKinds.DecisionRaised, Ct);

        var state = new NotifierState();
        harness.Commands.Clear();
        await harness.NotifyAsync(state, Ct);

        Assert.True(state.Started);
        Assert.Equal(1, state.Pending!.Lines);
        Assert.Equal(Start.AddMinutes(-10), state.LastDeliveredAt);

        var texts = harness.Commands.Texts;
        Assert.Contains(texts, text => Flat(text).Contains("WHERE \"j\".\"kind\" = 'notification_sent' ORDER BY \"j\".\"id\" DESC LIMIT 1", StringComparison.Ordinal));
        Assert.Contains(texts, text => Flat(text).Contains("WHERE \"j\".\"id\" > @", StringComparison.Ordinal)
            && Flat(text).Contains("ORDER BY \"j\".\"id\" LIMIT", StringComparison.Ordinal));
        // The triggers above the watermark are folded in by one aggregate over the kind index, and no read returns
        // rows without a bound.
        Assert.Contains(texts, text => Flat(text).Contains("\"j\".\"kind\" IN ('approval_requested', 'decision_raised')", StringComparison.Ordinal));
        Assert.DoesNotContain(
            texts,
            text => text.Contains("FROM \"journal\"", StringComparison.Ordinal)
                && !text.Contains("LIMIT", StringComparison.Ordinal)
                && !text.Contains("COUNT(", StringComparison.Ordinal)
                && !text.Contains("MIN(", StringComparison.Ordinal)
                && !text.Contains("MAX(", StringComparison.Ordinal));
    }

    /// <summary>
    /// A watermark goes stale when nothing is written for a long time, and a start above it must not read everything
    /// since, a batch per scan, before it sees a new approval. The triggers above it are folded in by one read of the
    /// kind index, and the cursor is the newest line.
    /// </summary>
    [Fact]
    public async Task A_start_above_a_stale_watermark_folds_its_triggers_in_one_read()
    {
        using var harness = new DispatchHarness(Start);
        harness.NotificationsOn();
        var covered = await harness.WriteLineAsync(JournalKinds.WorkItemCreated, Ct);
        await harness.WriteLineAsync(JournalKinds.NotificationSent, Ct, Line(covered.PublicId, lastDeliveredAt: null));
        await using (var db = harness.Open())
        {
            var writer = new JournalWriter(harness.Clock);
            for (var line = 0; line < Notifier.ReadBatch + 500; line++)
            {
                writer.Append(db, Jason.Runtime.Domain.Actors.Runtime, JournalKinds.WorkItemUpdated, campaign: null);
            }

            await db.SaveChangesAsync(Ct);
        }

        var fresh = await harness.WriteLineAsync(JournalKinds.ApprovalRequested, Ct);

        var state = new NotifierState();
        await harness.NotifyAsync(state, Ct);

        Assert.Equal(1, state.Pending?.Lines);
        Assert.Equal(fresh.PublicId, state.CursorPublicId);
    }

    /// <summary>
    /// Whether there is a desktop is a property of the process that tried. A start does not carry a "no desktop" an
    /// earlier runtime recorded into this one's status, where it would contradict this runtime's own presence.
    /// </summary>
    [Fact]
    public async Task A_start_does_not_carry_another_process_s_missing_desktop_into_its_status()
    {
        using var harness = new DispatchHarness(Start);
        harness.NotificationsOn();
        var covered = await harness.WriteLineAsync(JournalKinds.WorkItemCreated, Ct);
        var line = new NotificationLine(
            "ntf_earlier",
            1,
            0,
            covered.PublicId,
            [
                ChannelOutcome.NotAttempted("toast", Jason.Contracts.Notifications.ChannelKinds.Toast, Jason.Contracts.Notifications.NotAttemptedReasons.NoDesktop, "session 0"),
                ChannelOutcome.Delivered("ntfy", Jason.Contracts.Notifications.ChannelKinds.Plugin),
            ],
            Start.AddMinutes(-5));
        await harness.WriteLineAsync(JournalKinds.NotificationSent, Ct, line.ToJson());

        await harness.NotifyAsync(new NotifierState(), Ct);

        Assert.Equal(["ntfy"], harness.NotificationStatus.Snapshot().Channels.Select(channel => channel.Outcome.Channel));
    }

    /// <summary>
    /// The first start on a database that has never notified anybody does not walk its history: the cursor is the
    /// newest line, and what is still pending gives one pointer after the start grace — or nothing, if nothing is.
    /// </summary>
    [Theory]
    [Critical(Family.N3, "no flood on first start")]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public async Task The_first_start_on_a_database_with_no_line_does_not_walk_its_history(bool pending, int notified)
    {
        using var harness = new DispatchHarness(Start);
        harness.NotificationsOn();
        await using (var db = harness.Open())
        {
            var writer = new JournalWriter(harness.Clock);
            for (var line = 0; line < 5_000; line++)
            {
                writer.Append(db, Jason.Runtime.Domain.Actors.Runtime, line % 2 == 0 ? JournalKinds.ApprovalRequested : JournalKinds.WorkItemCreated, campaign: null);
            }

            await db.SaveChangesAsync(Ct);
            if (pending)
            {
                await ParkedWork.WriteAsync(db, Ct);
            }
        }

        await harness.Runner.ScanOnceAsync(Ct);
        Assert.Equal(pending ? 1 : null, harness.NotifierState.Pending?.Lines);

        harness.Clock.Advance(TimeSpan.FromSeconds(60));
        await harness.Runner.ScanOnceAsync(Ct);

        Assert.Equal(notified, harness.Queue.Requests.Count);
    }

    /// <summary>
    /// Notifications turned off and on again in a running runtime: what was written meanwhile is folded in the way a
    /// start folds it, not read a batch per scan. Read a batch per scan, a burst split across two batches goes as two
    /// pointers, the second held behind the floor and then sent saying the same.
    /// </summary>
    [Fact]
    public async Task Turned_off_and_on_again_over_more_than_a_batch_the_burst_goes_as_one_pointer()
    {
        using var harness = await StartedAsync();
        harness.NotificationsOn(options => options.Enabled = false);
        await harness.WriteLineAsync(JournalKinds.ApprovalRequested, Ct);
        await using (var db = harness.Open())
        {
            var writer = new JournalWriter(harness.Clock);
            for (var line = 0; line < Notifier.ReadBatch + 500; line++)
            {
                writer.Append(db, Jason.Runtime.Domain.Actors.Runtime, JournalKinds.WorkItemUpdated, campaign: null);
            }

            await db.SaveChangesAsync(Ct);
        }

        await harness.WriteLineAsync(JournalKinds.ApprovalRequested, Ct);
        await harness.Runner.ScanOnceAsync(Ct);
        Assert.Empty(harness.Queue.Requests);

        harness.NotificationsOn();
        for (var minute = 0; minute < 40; minute++)
        {
            harness.Clock.Advance(TimeSpan.FromMinutes(1));
            await harness.Runner.ScanOnceAsync(Ct);
            if (harness.NotifierState.InFlight)
            {
                // The sender's part: somebody was told, and the notification is done.
                harness.NotifierState.Delivered(harness.Clock.Now.UtcDateTime);
                harness.NotifierState.Release(harness.Queue.Requests[^1].NotificationId);
            }
        }

        Assert.Equal(2, Assert.Single(harness.Queue.Requests).Burst.Lines);
    }

    private static JsonObject Line(string coveredThrough, DateTime? lastDeliveredAt) => new()
    {
        ["covered_through"] = coveredThrough,
        ["last_delivered_at"] = lastDeliveredAt?.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
    };

    private static string Flat(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
}
