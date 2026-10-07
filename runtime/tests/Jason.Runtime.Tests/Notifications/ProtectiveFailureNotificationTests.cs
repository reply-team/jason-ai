using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Notifications;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Configuration;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Notifications;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Dispatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Tests.Notifications;

/// <summary>
/// A protective action — one the catalogue runs in a paused campaign, such as recording an opt-out — that failed or
/// expired reaches nobody through the manager, because a paused campaign's manager is never summoned. The pointer is the
/// one way it reaches a person: it counts those failures since the last pointer that reached anybody, names at most
/// three campaigns by public id to say where to look, and carries the count forward when a pointer reached nobody.
/// </summary>
public sealed class ProtectiveFailureNotificationTests
{
    private static readonly DateTime Start = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- what wakes a pointer ---------------------------------------------------------------------------------------

    [Fact]
    public async Task A_failed_protective_action_in_a_paused_campaign_wakes_a_pointer()
    {
        using var harness = await StartedAsync();
        await ProtectiveWork.EndedAsync(harness, "optout.record", JournalKinds.WorkItemFailed, Ct);

        var request = await HandedOverAsync(harness);

        Assert.True(request is not null, "A failed optout.record in a paused campaign woke nothing.");
        Assert.Equal(1, request.Burst.Lines);
    }

    [Fact]
    public async Task An_expired_protective_action_wakes_a_pointer()
    {
        using var harness = await StartedAsync();
        await ProtectiveWork.EndedAsync(harness, "campaign.pause", JournalKinds.WorkItemExpired, Ct);

        Assert.NotNull(await HandedOverAsync(harness));
    }

    /// <summary>Work that is not protective, and a protective action somebody cancelled, wake nothing: a cancellation is a decision.</summary>
    [Theory]
    [InlineData("campaign.get", JournalKinds.WorkItemFailed)]
    [InlineData("optout.record", JournalKinds.WorkItemCancelled)]
    public async Task Other_endings_wake_nothing(string operation, string kind)
    {
        using var harness = await StartedAsync();
        await ProtectiveWork.EndedAsync(harness, operation, kind, Ct);

        Assert.Null(await HandedOverAsync(harness));
    }

    /// <summary>What counts is what the catalogue runs in a paused campaign, every such operation and no other.</summary>
    [Fact]
    public async Task Every_operation_the_catalogue_runs_in_a_paused_campaign_counts_and_no_other()
    {
        using var harness = new DispatchHarness(Start);
        var byItem = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var contract in OperationCatalog.All)
        {
            var (_, item, _) = await ProtectiveWork.EndedAsync(harness, contract.Id, JournalKinds.WorkItemFailed, Ct);
            byItem[item.PublicId] = contract.Id;
        }

        await using var db = harness.Open();
        var counted = await ProtectiveFailureLines.Of(db).Select(entry => entry.WorkItemId!).ToListAsync(Ct);

        Assert.Equal(
            OperationCatalog.RunsInPausedCampaign.Order(StringComparer.Ordinal),
            counted.Select(id => byItem[id]).Order(StringComparer.Ordinal));
    }

    // ---- what it says, and the window it counts ---------------------------------------------------------------------

    [Fact]
    public async Task One_failed_protective_action_with_nothing_waiting_is_a_pointer_that_names_its_campaign()
    {
        using var harness = await StartedAsync();
        var (campaign, _, _) = await ProtectiveWork.EndedAsync(harness, "optout.record", JournalKinds.WorkItemFailed, Ct);
        var request = await HandedOverAsync(harness);
        var toast = FakeChannel.Delivering();

        var line = await Sender(harness, toast).SendAsync(request!, Ct);

        Assert.True(line is not null, "Nothing was sent for a failed protective action with nothing else waiting.");
        Assert.Equal(1, line.ProtectiveFailures);
        Assert.Equal(
            $"1 protective action failed in Jason, in {campaign}. {NotificationComposer.LookAtFailures}",
            Assert.Single(toast.Messages).Text);
        Assert.Equal(1, (await LastLineAsync(harness))["counts"]!["protective_failures"]!.GetValue<int>());
    }

    /// <summary>
    /// A pointer that reached nobody still writes its line, and its failures are counted again by the next pointer, with
    /// any new ones; once a pointer reaches somebody, the next counts only what failed after it.
    /// </summary>
    [Fact]
    public async Task A_pointer_that_reached_nobody_leaves_its_failures_for_the_next_one()
    {
        using var harness = await StartedAsync();

        var (first, _, _) = await ProtectiveWork.EndedAsync(harness, "optout.record", JournalKinds.WorkItemFailed, Ct);
        var unheard = await HandedOverAsync(harness);
        var nobody = await Sender(
                harness,
                FakeChannel.Failing("notification_rejected", FailureClass.Permanent),
                FakeChannel.Skipped(NotAttemptedReasons.NoDesktop))
            .SendAsync(unheard!, Ct);
        harness.NotifierState.Release(unheard!.NotificationId);
        Assert.Equal(1, nobody!.ProtectiveFailures);
        Assert.Equal(unheard.DeliveredThrough, nobody.DeliveredThrough);

        var (second, _, _) = await ProtectiveWork.EndedAsync(harness, "optout.record", JournalKinds.WorkItemFailed, Ct);
        var heard = await HandedOverAsync(harness);
        var toast = FakeChannel.Delivering();
        var reached = await Sender(harness, toast).SendAsync(heard!, Ct);
        harness.NotifierState.Release(heard!.NotificationId);

        Assert.Equal(2, reached!.ProtectiveFailures);
        Assert.Equal($"2 protective actions failed in Jason, in {first} and {second}. {NotificationComposer.LookAtFailures}", Assert.Single(toast.Messages).Text);
        Assert.Equal(heard.CoveredThrough, reached.DeliveredThrough);
        Assert.Equal(heard.CoveredThrough, harness.NotifierState.DeliveredThrough);

        // The window moved with the pointer that reached somebody: the next one counts only what failed after it.
        harness.Clock.Advance(TimeSpan.FromHours(1));
        var (third, _, _) = await ProtectiveWork.EndedAsync(harness, "campaign.pause", JournalKinds.WorkItemExpired, Ct);
        var later = await HandedOverAsync(harness);
        var toastLater = FakeChannel.Delivering();
        var after = await Sender(harness, toastLater).SendAsync(later!, Ct);

        Assert.Equal(1, after!.ProtectiveFailures);
        var said = Assert.Single(toastLater.Messages).Text;
        Assert.DoesNotContain(first, said, StringComparison.Ordinal);
        Assert.Contains(third, said, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Past_three_campaigns_the_rest_are_counted_and_not_named()
    {
        using var harness = await StartedAsync();
        var campaigns = new List<string>();
        for (var n = 0; n < 4; n++)
        {
            campaigns.Add((await ProtectiveWork.EndedAsync(harness, "optout.record", JournalKinds.WorkItemFailed, Ct)).Campaign);
        }

        var toast = FakeChannel.Delivering();
        await Sender(harness, toast).SendAsync((await HandedOverAsync(harness))!, Ct);

        Assert.Equal(
            $"4 protective actions failed in Jason, in {campaigns[0]}, {campaigns[1]}, {campaigns[2]} and 1 more. {NotificationComposer.LookAtFailures}",
            Assert.Single(toast.Messages).Text);
    }

    [Fact]
    public async Task Approvals_and_a_failed_protective_action_are_said_together()
    {
        using var harness = await StartedAsync();
        await Waiting.ApprovalsAsync(harness, 2, Ct);
        var (campaign, _, _) = await ProtectiveWork.EndedAsync(harness, "optout.record", JournalKinds.WorkItemFailed, Ct);
        var toast = FakeChannel.Delivering();

        var line = await Sender(harness, toast).SendAsync((await HandedOverAsync(harness))!, Ct);

        Assert.Equal((2, 0, 1), (line!.Approvals, line.Decisions, line.ProtectiveFailures));
        Assert.Equal(
            $"2 approvals are waiting in Jason. 1 protective action failed in Jason, in {campaign}. {NotificationComposer.LookAtBoth}",
            Assert.Single(toast.Messages).Text);
    }

    // ---- across a start -----------------------------------------------------------------------------------------------

    /// <summary>
    /// Where the count starts is the state's, for the process's life: a hand-over, a notification handed back unsent, and
    /// reading further do not move it — before any line was written it stays where this process started reading — and
    /// only a notification that reached somebody does.
    /// </summary>
    [Fact]
    public void Where_the_count_starts_moves_only_when_a_notification_reaches_somebody()
    {
        var state = new NotifierState();
        state.Start(10, "jrn_start", lastDeliveredAt: null, seed: null, deliveredThrough: "jrn_start");

        state.Advance(11, "jrn_11", [Start]);
        Assert.True(state.TryHandOver((_, _) => true, out var first));
        Assert.Equal(("jrn_start", "jrn_11"), (first.DeliveredThrough, first.CoveredThrough));
        state.Return(first);

        state.Advance(12, "jrn_12", [Start]);
        Assert.True(state.TryHandOver((_, _) => true, out var second));
        Assert.Equal("jrn_start", second.DeliveredThrough);
        state.Release(second.NotificationId);

        state.Delivered(Start.AddMinutes(1), second.CoveredThrough);
        Assert.Equal("jrn_12", state.DeliveredThrough);
    }


    /// <summary>A line written before the window had a key of its own starts the window where that line covered through.</summary>
    [Fact]
    public async Task A_line_written_before_the_window_had_its_key_starts_the_window_at_what_it_covered()
    {
        using var harness = new DispatchHarness(Start);
        harness.NotificationsOn();
        var covered = await harness.WriteLineAsync(JournalKinds.WorkItemCreated, Ct);
        await harness.WriteLineAsync(JournalKinds.NotificationSent, Ct, new JsonObject
        {
            ["notification_id"] = "ntf_before",
            ["counts"] = new JsonObject { ["approvals"] = 1, ["decisions"] = 0 },
            ["covered_through"] = covered.PublicId,
            ["channels"] = new JsonArray(),
            ["last_delivered_at"] = null,
        });

        await harness.Runner.ScanOnceAsync(Ct);

        Assert.Equal(covered.PublicId, harness.NotifierState.DeliveredThrough);
    }

    /// <summary>
    /// A start that resumes behind the newest line folds what lies between into one burst rather than reading it a batch
    /// at a time, and a protective failure in what it folds wakes a pointer as one read line by line does.
    /// </summary>
    [Fact]
    public async Task A_start_that_folds_what_it_did_not_read_wakes_on_a_protective_failure_in_it()
    {
        using var harness = new DispatchHarness(Start);
        harness.NotificationsOn();
        var covered = await harness.WriteLineAsync(JournalKinds.WorkItemCreated, Ct);
        await harness.WriteLineAsync(JournalKinds.NotificationSent, Ct, new JsonObject
        {
            ["notification_id"] = "ntf_earlier",
            ["counts"] = new JsonObject { ["approvals"] = 1, ["decisions"] = 0, ["protective_failures"] = 0 },
            ["covered_through"] = covered.PublicId,
            ["channels"] = new JsonArray(),
            ["last_delivered_at"] = null,
            ["delivered_through"] = covered.PublicId,
        });
        await ProtectiveWork.EndedAsync(harness, "optout.record", JournalKinds.WorkItemFailed, Ct);

        await harness.Runner.ScanOnceAsync(Ct);
        var request = await HandedOverAsync(harness);

        Assert.True(request is not null, "A start that folded a failed optout.record handed nothing over.");
        Assert.Equal(1, request.Burst.Lines);
    }

    /// <summary>A failure no pointer reached anybody about is still counted by the first pointer after a restart.</summary>
    [Fact]
    public async Task A_failure_nobody_was_told_of_is_counted_after_a_restart()
    {
        using var harness = await StartedAsync();
        await ProtectiveWork.EndedAsync(harness, "optout.record", JournalKinds.WorkItemFailed, Ct);
        var unheard = await HandedOverAsync(harness);
        await Sender(harness, FakeChannel.Skipped(NotAttemptedReasons.NoDesktop)).SendAsync(unheard!, Ct);

        var restarted = new NotifierState();
        await harness.NotifyAsync(restarted, Ct);
        await Waiting.ApprovalsAsync(harness, 1, Ct);
        harness.Clock.Advance(TimeSpan.FromSeconds(60));
        await harness.NotifyAsync(restarted, Ct);
        var after = harness.Queue.Requests[^1];
        var line = await Sender(harness, FakeChannel.Delivering()).SendAsync(after, Ct);

        Assert.Equal((1, 1), (line!.Approvals, line.ProtectiveFailures));
    }

    /// <summary>
    /// A first start walks no history: a protective failure journaled before any start that wrote a line is not counted
    /// by a pointer. The work-item list is where it shows.
    /// </summary>
    [Fact]
    public async Task A_failure_journaled_before_the_first_start_is_not_counted()
    {
        using var harness = new DispatchHarness(Start);
        harness.NotificationsOn();
        await ProtectiveWork.EndedAsync(harness, "optout.record", JournalKinds.WorkItemFailed, Ct);
        await harness.Runner.ScanOnceAsync(Ct);

        Assert.Null(await HandedOverAsync(harness));

        await Waiting.ApprovalsAsync(harness, 1, Ct);
        var toast = FakeChannel.Delivering();
        var line = await Sender(harness, toast).SendAsync((await HandedOverAsync(harness))!, Ct);

        Assert.Equal((1, 0), (line!.Approvals, line.ProtectiveFailures));
        Assert.DoesNotContain("protective", Assert.Single(toast.Messages).Text, StringComparison.Ordinal);
    }

    // ---- helpers ------------------------------------------------------------------------------------------------------

    /// <summary>A harness with the notifier on and its state started over an empty journal.</summary>
    private static async Task<DispatchHarness> StartedAsync()
    {
        var harness = new DispatchHarness(Start);
        harness.NotificationsOn();
        await harness.Runner.ScanOnceAsync(Ct);
        Assert.True(harness.NotifierState.Started);
        return harness;
    }

    /// <summary>The burst settled and scanned once more: what the notifier handed over, if anything.</summary>
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

/// <summary>A provider operation that ended, written the way the runtime writes it: the item, and the line that says so.</summary>
internal static class ProtectiveWork
{
    public static async Task<(string Campaign, WorkItem Item, JournalEntry Line)> EndedAsync(
        DispatchHarness harness,
        string operation,
        string kind,
        CancellationToken ct,
        CampaignStatus campaignStatus = CampaignStatus.Paused)
    {
        await using var db = harness.Open();
        var now = harness.Clock.Now.UtcDateTime;
        var campaign = WorkItemFactory.NewCampaign(status: campaignStatus, now: now);
        var item = WorkItemFactory.NewProviderOp(campaign, operation, now);
        item.Status = kind switch
        {
            JournalKinds.WorkItemFailed => WorkItemStatus.Failed,
            JournalKinds.WorkItemExpired => WorkItemStatus.Expired,
            JournalKinds.WorkItemCancelled => WorkItemStatus.Cancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not an ending."),
        };
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(ct);

        var line = new JournalWriter(harness.Clock).Append(db, Actors.Dispatcher, kind, campaign: null, key: "status", workItem: item);
        await db.SaveChangesAsync(ct);
        return (campaign.PublicId, item, line);
    }
}
