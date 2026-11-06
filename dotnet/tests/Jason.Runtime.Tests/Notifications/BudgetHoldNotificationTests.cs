using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Configuration;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Notifications;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Dispatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Tests.Notifications;

/// <summary>
/// A hold of the claim at a role's daily limit wakes a pointer by its own line, and the pointer counts, as it is sent, the
/// items that wait on such a limit in every campaign, by the wait and the item's last error rather than by the line.
/// </summary>
public sealed class BudgetHoldNotificationTests
{
    private const string Role = "planner";

    private static readonly DateTime Start = new(2026, 10, 18, 9, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_hold_wakes_one_pointer_that_counts_the_held_item()
    {
        using var harness = await StartedAsync();
        await HeldAsync(harness);

        var request = await HandedOverAsync(harness);
        Assert.True(request is not null, "A hold woke no pointer.");
        var toast = FakeChannel.Delivering();
        var line = await Sender(harness, toast).SendAsync(request, Ct);

        Assert.Equal(1, line!.Held);
        Assert.Equal("1 item waits on a budget. Ask your agent what is held, or run jason workitem list --status waiting.", Assert.Single(toast.Messages).Text);
        Assert.Equal(1, (await LastLineAsync(harness))["counts"]!["held"]!.GetValue<int>());
    }

    /// <summary>Across campaigns, and under the code a 0.30.0 runtime wrote as well: an update leaves that hold standing.</summary>
    [Fact]
    public async Task Holds_in_two_campaigns_are_two_and_an_item_held_under_the_old_code_is_counted()
    {
        using var harness = await StartedAsync();
        await HeldAsync(harness);
        var old = await HeldAsync(harness);
        await using (var db = harness.Open())
        {
            var item = await db.WorkItems.SingleAsync(w => w.Id == old, Ct);
            item.LastError = item.LastError! with { Code = AttemptErrors.ResponderLaunchesSpent };
            await db.SaveChangesAsync(Ct);
        }

        var line = await Sender(harness, FakeChannel.Delivering()).SendAsync((await HandedOverAsync(harness))!, Ct);

        Assert.Equal(2, line!.Held);
    }

    /// <summary>The count is taken as the pointer is sent: a hold a person let go meanwhile waits no more, and nothing is said.</summary>
    [Fact]
    public async Task A_hold_let_go_before_the_pointer_is_sent_is_not_said()
    {
        using var harness = await StartedAsync();
        var held = await HeldAsync(harness);
        var request = await HandedOverAsync(harness);
        Assert.True(request is not null, "A hold woke no pointer.");
        await using (var db = harness.Open())
        {
            var wait = await db.WorkItemWaits.SingleAsync(w => w.WorkItemId == held, Ct);
            wait.Status = WaitStatus.Released;
            await db.SaveChangesAsync(Ct);
        }

        var toast = FakeChannel.Delivering();
        var line = await Sender(harness, toast).SendAsync(request, Ct);

        Assert.True(line is null, $"A pointer was written for a hold let go before it was sent: {line}");
        Assert.Empty(toast.Messages);
    }

    /// <summary>
    /// The word on an item's last error says what stopped it once, not what it waits on now: an item that ran and yielded, or
    /// was parked on a block, since it was held is not held.
    /// </summary>
    [Fact]
    public async Task An_open_wait_of_an_attempt_or_a_block_is_not_a_hold_whatever_the_item_s_last_error_says()
    {
        using var harness = await StartedAsync();
        var yielded = await HeldAsync(harness);
        var parked = await HeldAsync(harness);
        await using (var db = harness.Open())
        {
            var yielding = await db.WorkItemWaits.Include(w => w.WorkItem).SingleAsync(w => w.WorkItemId == yielded, Ct);
            var now = harness.Clock.Now.UtcDateTime;
            yielding.Attempt = WorkItemFactory.NewAttempt(yielding.WorkItem!, 1, AttemptStatus.Yielded, now);
            var block = BlockRows.Block(yielding.WorkItem!, now, now.AddHours(1));
            db.ProviderBlocks.Add(block);
            var wait = await db.WorkItemWaits.SingleAsync(w => w.WorkItemId == parked, Ct);
            wait.ProviderBlock = block;
            await db.SaveChangesAsync(Ct);
        }

        var line = await Sender(harness, FakeChannel.Delivering()).SendAsync((await HandedOverAsync(harness))!, Ct);

        Assert.True(line is null, $"A pointer was written for waits that are not holds: {line}");
    }

    /// <summary>A wait's line that names no limit is an ordinary wait, and says nothing to a person.</summary>
    [Fact]
    public async Task A_wait_s_line_that_names_no_limit_wakes_no_pointer()
    {
        using var harness = await StartedAsync();
        await using (var db = harness.Open())
        {
            var now = harness.Clock.Now.UtcDateTime;
            var campaign = WorkItemFactory.NewCampaign(now: now);
            db.Campaigns.Add(campaign);
            var item = WorkItemFactory.NewAiRole(campaign, role: Role, now: now);
            db.WorkItems.Add(item);
            await db.SaveChangesAsync(Ct);
            new JournalWriter(harness.Clock).Append(
                db,
                Actors.Dispatcher,
                JournalKinds.WorkItemWaiting,
                campaign: null,
                updated: Ordinary(now),
                workItem: item);
            await db.SaveChangesAsync(Ct);
        }

        Assert.True(await HandedOverAsync(harness) is null, "An ordinary wait woke a pointer.");
    }

    /// <summary>A start that folds the lines it did not read points at a hold among them, as a read line by line does.</summary>
    [Fact]
    public async Task A_hold_said_before_a_start_is_pointed_at_after_it()
    {
        using var harness = new DispatchHarness(Start);
        harness.NotificationsOn();
        var covered = await harness.WriteLineAsync(JournalKinds.WorkItemCreated, Ct);
        await harness.WriteLineAsync(JournalKinds.NotificationSent, Ct, new JsonObject
        {
            ["notification_id"] = "ntf_earlier",
            ["counts"] = new JsonObject { ["approvals"] = 1, ["decisions"] = 0, ["protective_failures"] = 0, ["event_gaps"] = 0, ["blocks"] = 0 },
            ["covered_through"] = covered.PublicId,
            ["channels"] = new JsonArray(),
            ["last_delivered_at"] = null,
            ["delivered_through"] = covered.PublicId,
        });
        await HeldAsync(harness);

        await harness.Runner.ScanOnceAsync(Ct);
        var request = await HandedOverAsync(harness);

        Assert.True(request is not null, "A start that folded a hold handed nothing over.");
        var line = await Sender(harness, FakeChannel.Delivering()).SendAsync(request, Ct);
        Assert.Equal(1, line!.Held);
    }

    /// <summary>
    /// A start reads the lines of the kind a batch at a time, because only a line's payload says it is a hold, and a hold past
    /// the first batch is as much a hold as one inside it.
    /// </summary>
    [Fact]
    public async Task A_hold_past_a_batch_of_ordinary_waits_is_folded_at_a_start()
    {
        using var harness = new DispatchHarness(Start);
        harness.NotificationsOn();
        var covered = await harness.WriteLineAsync(JournalKinds.WorkItemCreated, Ct);
        await harness.WriteLineAsync(JournalKinds.NotificationSent, Ct, new JsonObject
        {
            ["notification_id"] = "ntf_earlier",
            ["counts"] = new JsonObject { ["approvals"] = 1, ["decisions"] = 0, ["protective_failures"] = 0, ["event_gaps"] = 0, ["blocks"] = 0 },
            ["covered_through"] = covered.PublicId,
            ["channels"] = new JsonArray(),
            ["last_delivered_at"] = null,
            ["delivered_through"] = covered.PublicId,
        });
        await using (var db = harness.Open())
        {
            var now = harness.Clock.Now.UtcDateTime;
            var campaign = WorkItemFactory.NewCampaign(now: now);
            db.Campaigns.Add(campaign);
            var item = WorkItemFactory.NewAiRole(campaign, role: Role, now: now);
            db.WorkItems.Add(item);
            await db.SaveChangesAsync(Ct);
            var writer = new JournalWriter(harness.Clock);
            for (var line = 0; line < Notifier.ReadBatch; line++)
            {
                writer.Append(db, Actors.Dispatcher, JournalKinds.WorkItemWaiting, campaign: null, updated: Ordinary(now), workItem: item);
            }

            await db.SaveChangesAsync(Ct);
        }

        await HeldAsync(harness);

        var state = new NotifierState();
        await harness.NotifyAsync(state, Ct);

        Assert.Equal(1, state.Pending?.Lines);
    }

    /// <summary>A person's hold reaches a pointer as a count alone: the role, the limit and the campaign stay in the journal.</summary>
    [Fact]
    public async Task The_pointer_carries_the_count_and_nothing_of_the_hold()
    {
        using var harness = await StartedAsync();
        await HeldAsync(harness, role: "sentinel-role");
        var toast = FakeChannel.Delivering();

        await Sender(harness, toast).SendAsync((await HandedOverAsync(harness))!, Ct);

        var text = Assert.Single(toast.Messages).Text;
        Assert.DoesNotContain("sentinel-role", text, StringComparison.Ordinal);
        Assert.DoesNotContain("cmp_", text, StringComparison.Ordinal);
    }

    /// <summary>An ordinary wait's payload: it names no limit.</summary>
    private static JsonObject Ordinary(DateTime now) => new()
    {
        ["on"] = new JsonArray("wi_01JC0000000000000000000003"),
        ["mode"] = "all",
        ["until"] = null,
        ["deadline"] = NotificationLine.Format(now.AddHours(1)),
    };

    /// <summary>One item of a campaign of its own, held by the real claim at a limit of one, which the campaign has spent.</summary>
    /// <returns>The held item's id.</returns>
    private static async Task<int> HeldAsync(DispatchHarness harness, string role = Role)
    {
        harness.Launches.CurrentValue = new LaunchesOptions { MaxPerDay = new Dictionary<string, int>(StringComparer.Ordinal) { [role] = 1 } };
        await using var db = harness.Open();
        var now = harness.Clock.Now.UtcDateTime;
        var campaign = WorkItemFactory.NewCampaign(now: now);
        db.Campaigns.Add(campaign);
        var spent = WorkItemFactory.NewAiRole(campaign, role: role, now: now);
        spent.Status = WorkItemStatus.Succeeded;
        WorkItemFactory.NewAttempt(spent, 1, AttemptStatus.Succeeded, now).StartedAt = now;
        var held = WorkItemFactory.NewAiRole(campaign, role: role, now: now);
        db.WorkItems.AddRange(spent, held);
        await db.SaveChangesAsync(Ct);
        await harness.ClaimAsync(Ct);
        return held.Id;
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
