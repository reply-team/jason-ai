using Jason.Contracts.Api;
using Jason.Runtime.Domain;
using Jason.Runtime.Persistence;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
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
