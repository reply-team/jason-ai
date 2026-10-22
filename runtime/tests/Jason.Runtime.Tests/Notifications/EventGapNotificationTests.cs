using System.Text.Json.Nodes;
using Jason.Contracts.Notifications;
using Jason.Contracts.Plugins;
using Jason.Runtime.Configuration;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Notifications;
using Jason.Runtime.Tests.Dispatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Tests.Notifications;

/// <summary>
/// An account whose events were not read for longer than the store reaches back is said once, by a pointer: counted as
/// failed protective work is, in the window since the last pointer that reached anybody, by the accounts it concerns —
/// and carried to the next pointer when one reached nobody.
/// </summary>
public sealed class EventGapNotificationTests
{
    private static readonly DateTime Start = new(2026, 10, 18, 9, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Critical(Family.N3, "unread events are told")]
    public async Task A_gap_wakes_one_pointer_that_says_an_account_was_not_read()
    {
        using var harness = await StartedAsync();
        await GapAsync(harness, "evs_01JB6K8TQ2W9V4MZ0C3Y7H5NRA");

        var request = await HandedOverAsync(harness);
        Assert.True(request is not null, "A gap woke no pointer.");
        var toast = FakeChannel.Delivering();
        var line = await Sender(harness, toast).SendAsync(request, Ct);

        Assert.Equal(1, line!.EventGaps);
        Assert.Equal("1 account has events Jason did not read. Ask your agent what Jason did not read, or run jason events sources.", Assert.Single(toast.Messages).Text);
        Assert.Equal(1, (await LastLineAsync(harness))["counts"]!["event_gaps"]!.GetValue<int>());
    }

    [Fact]
    public async Task A_gap_is_pointed_at_once_and_not_again_after_a_restart()
    {
        using var harness = await StartedAsync();
        await GapAsync(harness, "evs_01JB6K8TQ2W9V4MZ0C3Y7H5NRA");
        var first = await HandedOverAsync(harness);
        await Sender(harness, FakeChannel.Delivering()).SendAsync(first!, Ct);
        harness.NotifierState.Release(first!.NotificationId);

        harness.Clock.Advance(TimeSpan.FromHours(1));
        await Waiting.ApprovalsAsync(harness, 1, Ct);
        var later = await HandedOverAsync(harness);
        var toast = FakeChannel.Delivering();
        var line = await Sender(harness, toast).SendAsync(later!, Ct);
        harness.NotifierState.Release(later!.NotificationId);

        Assert.Equal(0, line!.EventGaps);
        Assert.DoesNotContain("did not read", Assert.Single(toast.Messages).Text, StringComparison.Ordinal);

        var restarted = new NotifierState();
        await harness.NotifyAsync(restarted, Ct);
        harness.Clock.Advance(TimeSpan.FromHours(1));
        await Waiting.ApprovalsAsync(harness, 1, Ct);
        harness.Clock.Advance(TimeSpan.FromSeconds(60));
        await harness.NotifyAsync(restarted, Ct);
        var afterRestart = await Sender(harness, FakeChannel.Delivering()).SendAsync(harness.Queue.Requests[^1], Ct);
        Assert.Equal(0, afterRestart!.EventGaps);
    }

    [Fact]
    public async Task A_pointer_that_reached_nobody_leaves_its_gaps_for_the_next_one()
    {
        using var harness = await StartedAsync();
        await GapAsync(harness, "evs_01JB6K8TQ2W9V4MZ0C3Y7H5NRA");
        var unheard = await HandedOverAsync(harness);
        var nobody = await Sender(harness, FakeChannel.Failing("notification_rejected", FailureClass.Permanent)).SendAsync(unheard!, Ct);
        harness.NotifierState.Release(unheard!.NotificationId);
        Assert.Equal(1, nobody!.EventGaps);

        await GapAsync(harness, "evs_01JB6K8TQ2W9V4MZ0C3Y7H5NRB");
        var heard = await HandedOverAsync(harness);
        var toast = FakeChannel.Delivering();
        var reached = await Sender(harness, toast).SendAsync(heard!, Ct);

        Assert.Equal(2, reached!.EventGaps);
        Assert.StartsWith("2 accounts have events Jason did not read.", Assert.Single(toast.Messages).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_account_is_counted_once_however_many_gaps_it_has()
    {
        using var harness = await StartedAsync();
        await GapAsync(harness, "evs_01JB6K8TQ2W9V4MZ0C3Y7H5NRA", "2026-08-01T00:00:00.000Z");
        await GapAsync(harness, "evs_01JB6K8TQ2W9V4MZ0C3Y7H5NRA", "2026-09-01T00:00:00.000Z");

        var line = await Sender(harness, FakeChannel.Delivering()).SendAsync((await HandedOverAsync(harness))!, Ct);

        Assert.Equal(1, line!.EventGaps);
    }

    [Fact]
    public async Task A_gap_said_with_approvals_and_a_failed_protective_action_points_at_each()
    {
        using var harness = await StartedAsync();
        await Waiting.ApprovalsAsync(harness, 2, Ct);
        var (campaign, _, _) = await ProtectiveWork.EndedAsync(harness, "optout.record", JournalKinds.WorkItemFailed, Ct);
        await GapAsync(harness, "evs_01JB6K8TQ2W9V4MZ0C3Y7H5NRA");
        var toast = FakeChannel.Delivering();

        var line = await Sender(harness, toast).SendAsync((await HandedOverAsync(harness))!, Ct);

        Assert.Equal((2, 1, 1), (line!.Approvals, line.ProtectiveFailures, line.EventGaps));
        Assert.Equal(
            $"2 approvals are waiting in Jason. 1 protective action failed in Jason, in {campaign}. 1 account has events Jason did not read. "
                + "Ask your agent, or run jason approval list, jason decision list, jason workitem list --status failed --status expired and jason events sources.",
            Assert.Single(toast.Messages).Text);
    }

    /// <summary>
    /// A start that resumes behind the newest line folds what lies between into one burst, and a gap in what it folds is
    /// pointed at as one read line by line is.
    /// </summary>
    [Fact]
    public async Task A_gap_written_before_a_start_is_pointed_at_after_it()
    {
        using var harness = new DispatchHarness(Start);
        harness.NotificationsOn();
        var covered = await harness.WriteLineAsync(JournalKinds.WorkItemCreated, Ct);
        await harness.WriteLineAsync(JournalKinds.NotificationSent, Ct, new JsonObject
        {
            ["notification_id"] = "ntf_earlier",
            ["counts"] = new JsonObject { ["approvals"] = 1, ["decisions"] = 0, ["protective_failures"] = 0, ["event_gaps"] = 0 },
            ["covered_through"] = covered.PublicId,
            ["channels"] = new JsonArray(),
            ["last_delivered_at"] = null,
            ["delivered_through"] = covered.PublicId,
        });
        await GapAsync(harness, "evs_01JB6K8TQ2W9V4MZ0C3Y7H5NRA");

        await harness.Runner.ScanOnceAsync(Ct);
        var request = await HandedOverAsync(harness);

        Assert.True(request is not null, "A start that folded a gap handed nothing over.");
        var line = await Sender(harness, FakeChannel.Delivering()).SendAsync(request, Ct);
        Assert.Equal(1, line!.EventGaps);
    }

    private static async Task GapAsync(DispatchHarness harness, string source, string from = "2026-09-01T00:00:00.000Z")
    {
        await using var db = harness.Open();
        new JournalWriter(harness.Clock).Append(db, Actors.Dispatcher, JournalKinds.EventsGapObserved, campaign: null, $"gap/{source}/{from}", updated: new JsonObject
        {
            ["source"] = source,
            ["plugin"] = "reader",
            ["from"] = from,
            ["to"] = "2026-09-18T09:00:00.000Z",
        });
        await db.SaveChangesAsync(Ct);
    }

    private static async Task<DispatchHarness> StartedAsync()
    {
        var harness = new DispatchHarness(Start);
        harness.NotificationsOn();
        await harness.Runner.ScanOnceAsync(Ct);
        Assert.True(harness.NotifierState.Started);
        return harness;
    }

    private static async Task<NotificationRequest?> HandedOverAsync(DispatchHarness harness)
    {
        var before = harness.Queue.Requests.Count;
        harness.Clock.Advance(TimeSpan.FromSeconds(60));
        await harness.Runner.ScanOnceAsync(Ct);
        return harness.Queue.Requests.Count > before ? harness.Queue.Requests[^1] : null;
    }

    private static NotificationSender Sender(DispatchHarness harness, params INotificationChannel[] channels) => new(
        harness.Scopes,
        new FakeChannels(channels),
        harness.NotifierState,
        harness.NotificationStatus,
        harness.Resolve<LiveSettings<NotificationsOptions>>(),
        harness.Clock,
        harness.Resolve<ILogger<NotificationSender>>());

    private static async Task<JsonNode> LastLineAsync(DispatchHarness harness)
    {
        await using var db = harness.Open();
        var line = await db.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.NotificationSent).OrderByDescending(e => e.Id).FirstAsync(Ct);
        return line.New!;
    }
}
