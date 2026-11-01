using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Plugins;
using Jason.Runtime.Configuration;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Notifications;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Dispatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Tests.Notifications;

/// <summary>
/// A provider block that holds work is said once, by a pointer: counted as an account's gap is, in the window since the
/// last pointer that reached anybody, by block however many campaigns it told — only while it is still open when the
/// pointer is sent, beside the work items it holds then — and carried to the next pointer when one reached nobody.
/// </summary>
public sealed class ProviderBlockNotificationTests
{
    private static readonly DateTime Start = new(2026, 10, 18, 9, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_block_wakes_one_pointer_that_counts_it_and_the_work_it_holds()
    {
        using var harness = await StartedAsync();
        await ParkAsync(harness, items: 4);

        var request = await HandedOverAsync(harness);
        Assert.True(request is not null, "A block woke no pointer.");
        var toast = FakeChannel.Delivering();
        var line = await Sender(harness, toast).SendAsync(request, Ct);

        Assert.Equal(1, line!.Blocks);
        Assert.Equal("1 provider block holds 4 work items in Jason. Ask your agent what is blocked, or run jason block list.", Assert.Single(toast.Messages).Text);
        Assert.Equal(1, (await LastLineAsync(harness))["counts"]!["blocks"]!.GetValue<int>());
    }

    [Fact]
    public async Task A_block_cleared_before_the_pointer_is_sent_is_not_said()
    {
        using var harness = await StartedAsync();
        var block = await ParkAsync(harness, items: 2);
        var request = await HandedOverAsync(harness);
        Assert.True(request is not null, "A block woke no pointer.");

        await ClearAsync(harness, block);
        var toast = FakeChannel.Delivering();
        var line = await Sender(harness, toast).SendAsync(request, Ct);

        Assert.True(line is null, $"A pointer was written for a block cleared before it was sent: {line}");
        Assert.Empty(toast.Messages);
        Assert.NotNull(harness.NotificationStatus.Snapshot().NothingWaitedAt);
    }

    [Fact]
    public async Task Two_blocks_are_two_and_a_block_told_to_two_campaigns_is_one()
    {
        using var harness = await StartedAsync();
        var first = await ParkAsync(harness, items: 2);
        await ParkAsync(harness, items: 3, block: first);
        await ParkAsync(harness, items: 1);
        var toast = FakeChannel.Delivering();

        var line = await Sender(harness, toast).SendAsync((await HandedOverAsync(harness))!, Ct);

        Assert.Equal(2, line!.Blocks);
        Assert.StartsWith("2 provider blocks hold 6 work items in Jason.", Assert.Single(toast.Messages).Text, StringComparison.Ordinal);
    }

    /// <summary>The work a block holds is counted as the pointer is sent: an item let go meanwhile is not held any more.</summary>
    [Fact]
    public async Task The_work_a_block_holds_is_counted_when_the_pointer_is_sent()
    {
        using var harness = await StartedAsync();
        var block = await ParkAsync(harness, items: 3);
        var request = await HandedOverAsync(harness);

        await using (var db = harness.Open())
        {
            var wait = await db.WorkItemWaits.Where(w => w.ProviderBlock!.PublicId == block).OrderBy(w => w.Id).FirstAsync(Ct);
            wait.Status = WaitStatus.Cancelled;
            wait.ReleasedAt = harness.Clock.Now.UtcDateTime;
            await db.SaveChangesAsync(Ct);
        }

        var toast = FakeChannel.Delivering();
        await Sender(harness, toast).SendAsync(request!, Ct);

        Assert.StartsWith("1 provider block holds 2 work items in Jason.", Assert.Single(toast.Messages).Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The work items a block holds are the ones parked on it, as <c>block.get</c>'s <c>held_count</c> counts them: an
    /// account block whose sending account has an open block of its own counts only its own waits, not the sender's.
    /// </summary>
    [Fact]
    public async Task An_account_block_counts_its_own_waits_and_not_those_of_its_sender_s_block()
    {
        using var harness = await StartedAsync();
        var shared = "sha256:" + new string('a', 64);
        await ParkAsync(harness, items: 2, shape: row =>
        {
            row.BindingIdentity = shared;
            row.Scope = ProviderBlockScope.Sender;
            row.SenderExternalId = "snd_marta";
            row.Reason = "limit";
        });
        var first = await HandedOverAsync(harness);
        await Sender(harness, FakeChannel.Delivering()).SendAsync(first!, Ct);
        harness.NotifierState.Release(first!.NotificationId);

        harness.Clock.Advance(TimeSpan.FromHours(1));
        await ParkAsync(harness, items: 1, shape: row => row.BindingIdentity = shared);
        var toast = FakeChannel.Delivering();
        var line = await Sender(harness, toast).SendAsync((await HandedOverAsync(harness))!, Ct);

        Assert.Equal(1, line!.Blocks);
        Assert.StartsWith("1 provider block holds 1 work item in Jason.", Assert.Single(toast.Messages).Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Said once: a later campaign the same block reaches writes its own line for its manager, and no pointer says the
    /// block again — nor does one after a restart.
    /// </summary>
    [Fact]
    public async Task A_block_is_pointed_at_once_and_not_again_when_it_reaches_another_campaign_or_after_a_restart()
    {
        using var harness = await StartedAsync();
        var block = await ParkAsync(harness, items: 1);
        var first = await HandedOverAsync(harness);
        await Sender(harness, FakeChannel.Delivering()).SendAsync(first!, Ct);
        harness.NotifierState.Release(first!.NotificationId);

        harness.Clock.Advance(TimeSpan.FromHours(1));
        await ParkAsync(harness, items: 2, block: block);
        await Waiting.ApprovalsAsync(harness, 1, Ct);
        var later = await HandedOverAsync(harness);
        var toast = FakeChannel.Delivering();
        var line = await Sender(harness, toast).SendAsync(later!, Ct);
        harness.NotifierState.Release(later!.NotificationId);

        Assert.Equal(0, line!.Blocks);
        Assert.DoesNotContain("provider block", Assert.Single(toast.Messages).Text, StringComparison.Ordinal);

        var restarted = new NotifierState();
        await harness.NotifyAsync(restarted, Ct);
        harness.Clock.Advance(TimeSpan.FromHours(1));
        await Waiting.ApprovalsAsync(harness, 1, Ct);
        harness.Clock.Advance(TimeSpan.FromSeconds(60));
        await harness.NotifyAsync(restarted, Ct);
        var afterRestart = await Sender(harness, FakeChannel.Delivering()).SendAsync(harness.Queue.Requests[^1], Ct);
        Assert.Equal(0, afterRestart!.Blocks);
    }

    /// <summary>
    /// A block reaching another campaign is none of the things a pointer says: its line tells that campaign's manager and
    /// wakes no pointer — read line by line or folded at a start — even while an older approval still waits, which a
    /// pointer woken by it would say again.
    /// </summary>
    [Fact]
    public async Task A_later_line_of_a_block_already_said_wakes_no_pointer_beside_an_older_approval()
    {
        using var harness = await StartedAsync();
        await Waiting.ApprovalsAsync(harness, 1, Ct);
        var block = await ParkAsync(harness, items: 1);
        var first = await HandedOverAsync(harness);
        var said = await Sender(harness, FakeChannel.Delivering()).SendAsync(first!, Ct);
        harness.NotifierState.Release(first!.NotificationId);
        Assert.Equal((1, 1), (said!.Approvals, said.Blocks));

        harness.Clock.Advance(TimeSpan.FromHours(1));
        await ParkAsync(harness, items: 2, block: block);
        var woken = await HandedOverAsync(harness);
        Assert.True(woken is null, "Another campaign's line for a block already said woke a pointer.");

        var restarted = new NotifierState();
        var before = harness.Queue.Requests.Count;
        await harness.NotifyAsync(restarted, Ct);
        harness.Clock.Advance(TimeSpan.FromHours(1));
        await harness.NotifyAsync(restarted, Ct);
        Assert.True(harness.Queue.Requests.Count == before, "A start that folded another campaign's line for a block already said woke a pointer.");
    }

    [Fact]
    public async Task A_pointer_that_reached_nobody_leaves_its_block_for_the_next_one()
    {
        using var harness = await StartedAsync();
        await ParkAsync(harness, items: 1);
        var unheard = await HandedOverAsync(harness);
        var nobody = await Sender(harness, FakeChannel.Failing("notification_rejected", FailureClass.Permanent)).SendAsync(unheard!, Ct);
        harness.NotifierState.Release(unheard!.NotificationId);
        Assert.Equal(1, nobody!.Blocks);

        await ParkAsync(harness, items: 1);
        var heard = await HandedOverAsync(harness);
        var toast = FakeChannel.Delivering();
        var reached = await Sender(harness, toast).SendAsync(heard!, Ct);

        Assert.Equal(2, reached!.Blocks);
        Assert.StartsWith("2 provider blocks hold 2 work items in Jason.", Assert.Single(toast.Messages).Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A protective action a block holds is parked, not failed: it is the block's pointer that says it, and the pointer
    /// about failed protective work stays for work that failed for another reason.
    /// </summary>
    [Fact]
    public async Task A_parked_protective_action_is_said_as_the_block_and_not_as_a_failed_one()
    {
        using var harness = await StartedAsync();
        await ParkAsync(harness, items: 1, operation: "optout.record", campaignStatus: CampaignStatus.Paused);
        var toast = FakeChannel.Delivering();

        var line = await Sender(harness, toast).SendAsync((await HandedOverAsync(harness))!, Ct);

        Assert.Equal((1, 0), (line!.Blocks, line.ProtectiveFailures));
        Assert.Equal("1 provider block holds 1 work item in Jason. Ask your agent what is blocked, or run jason block list.", Assert.Single(toast.Messages).Text);
    }

    /// <summary>
    /// Nothing of the block travels but the counts: not its link, its reason, the sending account, the plugin, the
    /// campaign's name or id, nor the block's own id.
    /// </summary>
    [Fact]
    public async Task The_pointer_carries_the_counts_and_nothing_of_the_block()
    {
        using var harness = await StartedAsync();
        string campaignId;
        await using (var db = harness.Open())
        {
            var campaign = WorkItemFactory.NewCampaign("Northwind renewal", now: harness.Clock.Now.UtcDateTime);
            db.Campaigns.Add(campaign);
            await db.SaveChangesAsync(Ct);
            campaignId = campaign.PublicId;
        }

        var block = await ParkAsync(harness, items: 2, campaign: campaignId, shape: row =>
        {
            row.Scope = ProviderBlockScope.Sender;
            row.SenderExternalId = "snd_marta";
            row.Reason = "limit";
            row.Link = "https://example.com/senders/marta";
        });
        var toast = FakeChannel.Delivering();

        await Sender(harness, toast).SendAsync((await HandedOverAsync(harness))!, Ct);

        var text = Assert.Single(toast.Messages).Text;
        Assert.Equal("1 provider block holds 2 work items in Jason. Ask your agent what is blocked, or run jason block list.", text);
        foreach (var word in new[] { "example.com", "limit", "snd_marta", BlockRows.Plugin, "Northwind", campaignId, block, "https" })
        {
            Assert.False(text.Contains(word, StringComparison.Ordinal), $"The pointer says '{word}': {text}");
        }
    }

    /// <summary>A start that folds the lines it did not read points at a block among them, as a read line by line does.</summary>
    [Fact]
    public async Task A_block_said_before_a_start_is_pointed_at_after_it()
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
        await ParkAsync(harness, items: 1);

        await harness.Runner.ScanOnceAsync(Ct);
        var request = await HandedOverAsync(harness);

        Assert.True(request is not null, "A start that folded a block handed nothing over.");
        var line = await Sender(harness, FakeChannel.Delivering()).SendAsync(request, Ct);
        Assert.Equal(1, line!.Blocks);
    }

    /// <summary>
    /// <paramref name="items"/> provider items of a campaign — a new one, or the one named — parked on a block: a new one,
    /// opened by the first of them, or the one named; and the line that tells the campaign, written by the runtime's own
    /// routine.
    /// </summary>
    /// <returns>The block's public id.</returns>
    private static async Task<string> ParkAsync(
        DispatchHarness harness,
        int items,
        string? block = null,
        string operation = "message.send",
        CampaignStatus campaignStatus = CampaignStatus.Active,
        string? campaign = null,
        Action<ProviderBlock>? shape = null)
    {
        await using var db = harness.Open();
        var now = harness.Clock.Now.UtcDateTime;
        Campaign owner;
        if (campaign is null)
        {
            owner = WorkItemFactory.NewCampaign(status: campaignStatus, now: now);
            db.Campaigns.Add(owner);
        }
        else
        {
            owner = await db.Campaigns.SingleAsync(c => c.PublicId == campaign, Ct);
        }

        var first = WorkItemFactory.NewProviderOp(owner, operation, now);
        first.Status = WorkItemStatus.Waiting;
        db.WorkItems.Add(first);
        ProviderBlock row;
        if (block is null)
        {
            // Each new block on a binding of its own: two open account blocks on one binding are one block.
            var binding = $"sha256:{await db.ProviderBlocks.CountAsync(Ct) + 1:x64}";
            row = BlockRows.Block(first, now, now.AddHours(1), binding: binding);
            shape?.Invoke(row);
            db.ProviderBlocks.Add(row);
        }
        else
        {
            row = await db.ProviderBlocks.SingleAsync(b => b.PublicId == block, Ct);
        }

        db.WorkItemWaits.Add(new WorkItemWait { WorkItem = first, ProviderBlock = row, OpenedAt = now });
        for (var index = 1; index < items; index++)
        {
            BlockRows.Parked(db, owner, row, now, operation);
        }

        await db.SaveChangesAsync(Ct);
        await ProviderBlocks.SayBlockedAsync(db, new JournalWriter(harness.Clock), first, row, attempt: null, Ct);
        await db.SaveChangesAsync(Ct);
        return row.PublicId;
    }

    private static async Task ClearAsync(DispatchHarness harness, string block)
    {
        await using var db = harness.Open();
        var row = await db.ProviderBlocks.SingleAsync(b => b.PublicId == block, Ct);
        row.Status = ProviderBlockStatus.Cleared;
        row.ClearedAt = harness.Clock.Now.UtcDateTime;
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
