using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// The claim under a sender's blocks kept per channel: the block on the work's channel governs that channel, and the
/// sender's block on every channel governs only the channels that have no open block of their own.
/// </summary>
public partial class ClaimerTests
{
    /// <summary>
    /// A sender's email block and its block on every channel both open, and an email send that is the email block's trial:
    /// the claim lets it through to the provider as that trial. It is not parked on the block on every channel, which would
    /// hold the email block's trial behind it.
    /// </summary>
    [Fact]
    public async Task The_trial_of_a_senders_email_block_runs_while_its_block_on_every_channel_is_open()
    {
        using var harness = SendingHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        var item = await ApprovedSendAsync(harness, db, campaign);
        var everywhere = SendersBlock(db, campaign, channel: null, nextTrialAt: Noon);
        var mailbox = SendersBlock(db, campaign, channel: "email", nextTrialAt: Noon.AddHours(1));
        await db.SaveChangesAsync(Ct);
        mailbox.TrialWorkItemId = item.Id;
        await db.SaveChangesAsync(Ct);

        var work = Assert.Single(await harness.Claimer.ClaimAsync(db, 4, Ct));

        Assert.Equal(item.PublicId, work.WorkItemPublicId);
        await using var fresh = database.Open();
        var rows = await fresh.ProviderBlocks.AsNoTracking().ToDictionaryAsync(b => b.Id, Ct);
        Assert.Equal((item.Id, (int?)null), (rows[mailbox.Id].TrialWorkItemId, rows[everywhere.Id].TrialWorkItemId));
        Assert.False(await fresh.WorkItemWaits.AsNoTracking().AnyAsync(w => w.WorkItemId == item.Id && w.ProviderBlockId != null, Ct));
    }

    /// <summary>
    /// An email send let through as the trial of its sender's block on every channel, whose email block has opened since:
    /// the claim moves it onto the email block with no call, and the block on every channel has spent no trial on it — it
    /// names none, and its next trial is as due as it was, so the resume step draws another of its items.
    /// </summary>
    [Fact]
    public async Task Work_let_through_as_the_trial_of_the_block_on_every_channel_moves_to_its_channels_block_with_no_call()
    {
        using var harness = SendingHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        var item = await ApprovedSendAsync(harness, db, campaign);
        var everywhere = SendersBlock(db, campaign, channel: null, nextTrialAt: Noon);
        var mailbox = SendersBlock(db, campaign, channel: "email", nextTrialAt: Noon);
        await db.SaveChangesAsync(Ct);
        everywhere.TrialWorkItemId = item.Id;
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        var stored = await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == item.Id, Ct);
        Assert.Equal((WorkItemStatus.Waiting, 0), (stored.Status, stored.AttemptCount));
        Assert.Empty(await fresh.Attempts.AsNoTracking().Where(a => a.WorkItemId == item.Id).ToListAsync(Ct));
        var wait = await fresh.WorkItemWaits.AsNoTracking().SingleAsync(w => w.WorkItemId == item.Id && w.Status == WaitStatus.Open, Ct);
        Assert.Equal(mailbox.Id, wait.ProviderBlockId);
        var rows = await fresh.ProviderBlocks.AsNoTracking().ToDictionaryAsync(b => b.Id, Ct);
        Assert.True(
            rows[everywhere.Id] is { Status: ProviderBlockStatus.Open, TrialWorkItemId: null } && rows[everywhere.Id].NextTrialAt == Noon,
            $"The block on every channel is {rows[everywhere.Id].Status}, trial {rows[everywhere.Id].TrialWorkItemId}, next {rows[everywhere.Id].NextTrialAt:O}.");
        Assert.Null(rows[mailbox.Id].TrialWorkItemId);
    }

    /// <summary>
    /// A sender's block on every channel and its email block both open, an email send parked on the block on every channel
    /// from before the email block opened, and nothing of the email block's own that could run: its one item is in a
    /// paused campaign. The email block draws its trial from the email work the block on every channel holds, as an
    /// account's is drawn from its senders' blocks, so the backlog is not left with nothing scheduled to move it. The claim
    /// runs that item as the email block's trial, and its send getting past clears both blocks.
    /// </summary>
    [Fact]
    public async Task A_senders_email_block_draws_its_trial_from_the_email_work_its_block_on_every_channel_holds_and_getting_past_clears_both()
    {
        using var harness = SendingHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (backlog, everywhere, mailbox) = await BacklogOnEveryChannelAsync(harness, db);

        var due = await global::Jason.Runtime.Dispatch.Resumer.DueTrialsAsync(db, Noon, Ct);
        Assert.Null(due.Single(row => row.BlockId == everywhere.Id).WaitId);
        Assert.NotNull(due.Single(row => row.BlockId == mailbox.Id).WaitId);
        Assert.Equal(1, await Resumer().ResumeAsync(db, Ct));
        var rows = await Blocks(database);
        Assert.Equal(((int?)null, (int?)backlog.Id), (rows[everywhere.Id].TrialWorkItemId, rows[mailbox.Id].TrialWorkItemId));

        var work = Assert.Single(await harness.Claimer.ClaimAsync(db, 4, Ct));
        Assert.Equal(backlog.PublicId, work.WorkItemPublicId);
        await GotPastAsync(db, backlog.Id, "email");

        rows = await Blocks(database);
        Assert.Equal((ProviderBlockStatus.Cleared, ProviderBlockStatus.Cleared), (rows[everywhere.Id].Status, rows[mailbox.Id].Status));
    }

    /// <summary>
    /// The same backlog item drawn as the email block's trial and refused again with the email block: it parks on the email
    /// block, which is seen again and put off a cadence, and the block on every channel, which let nothing through, is as
    /// it was.
    /// </summary>
    [Fact]
    public async Task Backlog_work_drawn_as_the_email_blocks_trial_and_refused_again_parks_on_the_email_block()
    {
        using var harness = SendingHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (backlog, everywhere, mailbox) = await BacklogOnEveryChannelAsync(harness, db);
        Assert.Equal(1, await Resumer().ResumeAsync(db, Ct));
        Assert.Equal(backlog.PublicId, Assert.Single(await harness.Claimer.ClaimAsync(db, 4, Ct)).WorkItemPublicId);

        var seen = mailbox.SeenCount;
        await BlockedAsync(db, backlog.Id, "email", said: "email");

        var rows = await Blocks(database);
        Assert.True(
            rows[mailbox.Id] is { Status: ProviderBlockStatus.Open, TrialWorkItemId: null } && rows[mailbox.Id].SeenCount == seen + 1 && rows[mailbox.Id].NextTrialAt == Noon.AddSeconds(900),
            $"The email block is {rows[mailbox.Id].Status}, trial {rows[mailbox.Id].TrialWorkItemId}, seen {rows[mailbox.Id].SeenCount}, next {rows[mailbox.Id].NextTrialAt:O}.");
        Assert.True(
            rows[everywhere.Id] is { Status: ProviderBlockStatus.Open, TrialWorkItemId: null } && rows[everywhere.Id].NextTrialAt == Noon,
            $"The block on every channel is {rows[everywhere.Id].Status}, trial {rows[everywhere.Id].TrialWorkItemId}, next {rows[everywhere.Id].NextTrialAt:O}.");
        await using var fresh = database.Open();
        var wait = await fresh.WorkItemWaits.AsNoTracking().SingleAsync(w => w.WorkItemId == backlog.Id && w.Status == WaitStatus.Open, Ct);
        Assert.Equal(mailbox.Id, wait.ProviderBlockId);
    }

    /// <summary>
    /// An email send parked on its sender's email block, whose input a patch moves onto LinkedIn while it waits: the email
    /// block's trial still draws it, as one of its own waits, and the claim finds nothing holding a LinkedIn send of that
    /// sender, so it runs. Its getting past on LinkedIn says nothing of the mailbox: the email block is not cleared, and it
    /// has its next trial a cadence off, as a trial that decided nothing.
    /// </summary>
    [Fact]
    public async Task A_trial_whose_input_moved_to_another_channel_does_not_clear_the_channel_block_it_was_drawn_by()
    {
        using var harness = SendingHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var item = Sending(db, NewCampaign(db), "email");
        item.Contact!.Channels.Add(new ContactChannel { Channel = "linkedin", Value = "https://www.linkedin.com/in/ada-example" });
        await ApproveAllAsync(harness, db);
        var mailbox = SendersBlock(db, item.Campaign!, channel: "email", nextTrialAt: Noon);
        await db.SaveChangesAsync(Ct);
        ParkOn(db, item, mailbox);
        item.Context = new JsonObject
        {
            [WorkItemService.InputKey] = new JsonObject
            {
                ["channel"] = "linkedin",
                ["sender"] = new JsonObject { ["external_id"] = "snd-1", ["address"] = "https://www.linkedin.com/in/marta-owner" },
                ["message"] = new JsonObject { ["format"] = "linkedin_message", ["body"] = "Hello, Ada." },
            },
        };
        await db.SaveChangesAsync(Ct);

        Assert.Equal(1, await Resumer().ResumeAsync(db, Ct));
        Assert.Equal(item.Id, (await Blocks(database))[mailbox.Id].TrialWorkItemId);
        // The patched input is another send than the one the person approved, so the claim asks them again, and the
        // trial holds the block's one trial until they decide.
        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));
        Assert.Equal(WorkItemStatus.AwaitingApproval, (await db.WorkItems.AsNoTracking().SingleAsync(w => w.Id == item.Id, Ct)).Status);
        await Decide(db, ApprovalStatus.Approved);
        Assert.Equal(item.PublicId, Assert.Single(await harness.Claimer.ClaimAsync(db, 4, Ct)).WorkItemPublicId);
        await GotPastAsync(db, item.Id, "linkedin");

        var row = (await Blocks(database))[mailbox.Id];
        Assert.True(
            row is { Status: ProviderBlockStatus.Open, ClearedByAttemptId: null, TrialWorkItemId: null } && row.NextTrialAt == Noon.AddSeconds(900),
            $"The email block is {row.Status}, cleared by {row.ClearedByAttemptId}, trial {row.TrialWorkItemId}, next {row.NextTrialAt:O}.");
    }

    /// <summary>
    /// Sender <c>snd-1</c>'s block on every channel and its email block, both due at noon; an approved email send parked on
    /// the block on every channel, and one parked on the email block in a campaign paused since, which no trial may draw.
    /// </summary>
    private static async Task<(WorkItem Backlog, ProviderBlock Everywhere, ProviderBlock Mailbox)> BacklogOnEveryChannelAsync(Harness harness, JasonDbContext db)
    {
        var backlog = Sending(db, NewCampaign(db), "email");
        var paused = Sending(db, NewCampaign(db), "email");
        await ApproveAllAsync(harness, db);
        var everywhere = SendersBlock(db, backlog.Campaign!, channel: null, nextTrialAt: Noon);
        var mailbox = SendersBlock(db, paused.Campaign!, channel: "email", nextTrialAt: Noon);
        await db.SaveChangesAsync(Ct);
        ParkOn(db, backlog, everywhere);
        ParkOn(db, paused, mailbox);
        paused.Campaign!.Status = CampaignStatus.Paused;
        await db.SaveChangesAsync(Ct);
        return (backlog, everywhere, mailbox);
    }

    private static Campaign NewCampaign(JasonDbContext db)
    {
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        return campaign;
    }

    /// <summary>Every send waiting for a person's approval asks for it at a claim and is approved, back in the queue.</summary>
    private static async Task ApproveAllAsync(Harness harness, JasonDbContext db)
    {
        await db.SaveChangesAsync(Ct);
        Assert.Empty(await harness.Claimer.ClaimAsync(db, 8, Ct));
        foreach (var approval in await db.Approvals.Include(a => a.WorkItem).Where(a => a.Status == ApprovalStatus.Pending).ToListAsync(Ct))
        {
            approval.Status = ApprovalStatus.Approved;
            approval.DecidedAt = Noon;
            approval.DecidedByType = ActorType.Human;
            approval.DecidedById = "operator";
            approval.DecisionReason = "go ahead";
            WorkItemTransitions.Apply(approval.WorkItem!, WorkItemStatus.Created, Noon);
        }

        await db.SaveChangesAsync(Ct);
    }

    /// <summary>The item waits on <paramref name="block"/>, as a park leaves it.</summary>
    private static void ParkOn(JasonDbContext db, WorkItem item, ProviderBlock block)
    {
        item.Status = WorkItemStatus.Waiting;
        db.WorkItemWaits.Add(new WorkItemWait { WorkItem = item, ProviderBlock = block, OpenedAt = Noon });
    }

    private static async Task<Dictionary<int, ProviderBlock>> Blocks(TestDatabase database)
    {
        await using var read = database.Open();
        return await read.ProviderBlocks.AsNoTracking().ToDictionaryAsync(b => b.Id, Ct);
    }

    private static AttemptOutcomes BlockOutcomes()
    {
        var clock = new FixedClock(Noon);
        return new AttemptOutcomes(new JournalWriter(clock), clock, TestOptions.Settings(TestOptions.Dispatcher(o => o.Blocks.TrialSeconds = 900)));
    }

    /// <summary>The item's latest attempt, a send from <c>snd-1</c> on <paramref name="channel"/>, got past the provider's blocks.</summary>
    private static async Task GotPastAsync(JasonDbContext db, int itemId, string channel)
    {
        var item = await db.WorkItems.SingleAsync(w => w.Id == itemId, Ct);
        var attempt = await db.Attempts.Where(a => a.WorkItemId == itemId).OrderByDescending(a => a.Number).FirstAsync(Ct);
        await BlockOutcomes().AnsweredAsync(db, item, attempt, BlockRows.Plugin, SendArgs(channel), BlockAnswer.GotPast, null, null, Ct);
        await db.SaveChangesAsync(Ct);
        db.ChangeTracker.Clear();
    }

    /// <summary>The item's latest attempt was refused with a block of <c>snd-1</c> on <paramref name="said"/>, or on every channel.</summary>
    private static async Task BlockedAsync(JasonDbContext db, int itemId, string channel, string? said)
    {
        var item = await db.WorkItems.SingleAsync(w => w.Id == itemId, Ct);
        var attempt = await db.Attempts.Where(a => a.WorkItemId == itemId).OrderByDescending(a => a.Number).FirstAsync(Ct);
        WorkItemTransitions.Apply(item, WorkItemStatus.Processing, Noon);
        var block = new JsonObject { ["scope"] = "sender", ["reason"] = "limit", ["sender_external_id"] = "snd-1" };
        if (said is not null)
        {
            block["channel"] = said;
        }

        await BlockOutcomes().AnsweredAsync(db, item, attempt, BlockRows.Plugin, SendArgs(channel), BlockAnswer.Blocked, block, null, Ct);
        await db.SaveChangesAsync(Ct);
        db.ChangeTracker.Clear();
    }

    private static JsonObject SendArgs(string channel) => new()
    {
        ["args"] = new JsonObject { ["channel"] = channel, ["sender"] = new JsonObject { ["external_id"] = "snd-1" } },
    };

    private static Harness SendingHarness() => new(
        plugin: TestPlugins.Loaded(BlockRows.Plugin, ["message.send"]),
        route: new Route(BlockRows.Plugin, null, null));

    /// <summary>An email send from sender <c>snd-1</c>, approved by a person and back in the queue.</summary>
    private static async Task<WorkItem> ApprovedSendAsync(Harness harness, JasonDbContext db, Campaign campaign)
    {
        var item = Sending(db, campaign, "email");
        await db.SaveChangesAsync(Ct);
        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));
        await Decide(db, ApprovalStatus.Approved);
        return item;
    }

    /// <summary>An open block of sender <c>snd-1</c> under the stand-in route, which binds nothing, on <paramref name="channel"/>.</summary>
    private static ProviderBlock SendersBlock(JasonDbContext db, Campaign campaign, string? channel, DateTime nextTrialAt)
    {
        var opener = WorkItemFactory.NewProviderOp(campaign, "message.send", Noon);
        opener.Status = WorkItemStatus.Failed;
        opener.FinishedAt = Noon;
        db.WorkItems.Add(opener);
        var block = BlockRows.Block(opener, Noon, nextTrialAt, ProviderBlockScope.Sender, "snd-1", EventSource.NoBinding, "limit", channel);
        db.ProviderBlocks.Add(block);
        return block;
    }
}
