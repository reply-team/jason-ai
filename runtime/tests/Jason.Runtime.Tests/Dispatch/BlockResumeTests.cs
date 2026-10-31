using System.Diagnostics;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Operations;
using Jason.Runtime.Approvals;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Documentation;
using Jason.Runtime.WorkItems;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// The resume step's two branches for work parked on a provider block: every item a block holds goes back to the queue
/// once the block clears, and while it is open a block whose trial is due lets one item through — work that reaches a
/// person first, a held control only where none can run, then the highest priority, then the oldest — and no other until
/// that trial has ended.
/// </summary>
public class BlockResumeTests
{
    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// A block's parked items stay waiting while it is open and its trial is not due, and all of them go back to the
    /// queue, released as <c>block_cleared</c>, at the first scan after it clears; one line each says why.
    /// </summary>
    [Fact]
    public async Task Parked_items_are_released_as_block_cleared_once_their_block_clears_and_not_before()
    {
        using var harness = new DispatchHarness(Noon);
        var (block, parked) = await SeedAsync(harness, nextTrialAt: Noon.AddHours(1), ("message.send", 0), ("conversation.reply", 0));

        Assert.Equal(0, await harness.ResumeAsync(Ct));
        await AssertWaitingAsync(harness, parked);

        await ClearAsync(harness, block);
        Assert.Equal(2, await harness.ResumeAsync(Ct));

        await using var db = harness.Open();
        var waits = await db.WorkItemWaits.AsNoTracking().Where(w => w.ProviderBlockId == block).ToListAsync(Ct);
        Assert.True(
            waits.Count == 2 && waits.All(w => w is { Status: WaitStatus.Released, ReleaseReason: WaitReleaseReason.BlockCleared }),
            $"The waits closed as {string.Join(", ", waits.Select(w => $"{w.Status}/{w.ReleaseReason}"))}.");
        var items = await db.WorkItems.AsNoTracking().Where(w => parked.Contains(w.PublicId)).Select(w => w.Status).ToListAsync(Ct);
        Assert.True(items.All(s => s == WorkItemStatus.Created), $"The parked items are {string.Join(", ", items)}.");
        var because = await db.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.WorkItemResumed).Select(e => e.New).ToListAsync(Ct);
        Assert.True(because.Count == 2 && because.All(u => u!.ToJsonString() == """{"because":"block_cleared"}"""), $"The lines say {string.Join(" / ", because.Select(u => u?.ToJsonString()))}.");
    }

    /// <summary>
    /// A due trial lets exactly one item through, and the block records it: work that reaches a person before any other,
    /// then the highest priority, then the oldest; a held control only once the block holds no such work that could run,
    /// whatever its priority. While that item is in the queue no other is let through; once it has ended and the next trial
    /// is due, the next in that order is.
    /// </summary>
    [Fact]
    public async Task A_due_trial_lets_one_item_through_an_act_first_then_priority_then_oldest()
    {
        using var harness = new DispatchHarness(Noon);
        var (block, parked) = await SeedAsync(
            harness,
            nextTrialAt: Noon,
            ("message.send", 0),
            ("campaign.pause", 9),
            ("message.send", 5),
            ("message.send", 5));
        string[] order = [parked[2], parked[3], parked[0], parked[1]];

        foreach (var expected in order)
        {
            Assert.Equal(1, await harness.ResumeAsync(Ct));
            await using (var db = harness.Open())
            {
                var row = await db.ProviderBlocks.AsNoTracking().Include(b => b.TrialWorkItem).SingleAsync(b => b.Id == block, Ct);
                Assert.True(row.TrialWorkItem?.PublicId == expected, $"The trial is {row.TrialWorkItem?.PublicId}, not {expected}.");
                Assert.Equal(WorkItemStatus.Created, row.TrialWorkItem!.Status);
                var wait = await db.WorkItemWaits.AsNoTracking().SingleAsync(w => w.WorkItemId == row.TrialWorkItemId, Ct);
                Assert.Equal(WaitReleaseReason.BlockTrial, wait.ReleaseReason);
            }

            // While the trial is in the queue, nothing else of the block is let through.
            Assert.Equal(0, await harness.ResumeAsync(Ct));

            await EndTrialAsync(harness, block, WorkItemStatus.Failed);
        }
    }

    /// <summary>
    /// A trial under way holds every later one, in each status an item is live in — in the queue of an active campaign,
    /// claimed, awaiting a decision, running; an item that has ended, in each status an item ends in, holds none.
    /// </summary>
    [Theory]
    [InlineData(WorkItemStatus.Created, false)]
    [InlineData(WorkItemStatus.Scheduled, false)]
    [InlineData(WorkItemStatus.Processing, false)]
    [InlineData(WorkItemStatus.AwaitingApproval, false)]
    [InlineData(WorkItemStatus.Waiting, true)]
    [InlineData(WorkItemStatus.Succeeded, true)]
    [InlineData(WorkItemStatus.Failed, true)]
    [InlineData(WorkItemStatus.Cancelled, true)]
    [InlineData(WorkItemStatus.Expired, true)]
    public async Task Only_a_trial_that_has_ended_lets_the_next_through(WorkItemStatus trial, bool next)
    {
        using var harness = new DispatchHarness(Noon);
        var (block, _) = await SeedAsync(harness, nextTrialAt: Noon, ("message.send", 0), ("message.send", 0));
        Assert.Equal(1, await harness.ResumeAsync(Ct));

        await using (var db = harness.Open())
        {
            var row = await db.ProviderBlocks.Include(b => b.TrialWorkItem).SingleAsync(b => b.Id == block, Ct);
            row.TrialWorkItem!.Status = trial;
            await db.SaveChangesAsync(Ct);
        }

        Assert.Equal(next ? 1 : 0, await harness.ResumeAsync(Ct));
    }

    /// <summary>
    /// A trial left in the queue of a campaign paused since it was let through holds nothing: the claim would never take
    /// it, so the next item the block holds in another campaign is let through as the block's trial. A trial the claim
    /// could still take — protective work, which runs in a paused campaign — or one already claimed holds the block.
    /// </summary>
    [Theory]
    [InlineData(WorkItemStatus.Created, "message.send", true)]
    [InlineData(WorkItemStatus.Created, "optout.record", false)]
    [InlineData(WorkItemStatus.Scheduled, "message.send", false)]
    public async Task A_trial_in_a_campaign_paused_since_holds_the_block_only_where_the_claim_could_still_take_it(
        WorkItemStatus trial,
        string operation,
        bool next)
    {
        using var harness = new DispatchHarness(Noon);
        int block;
        string first;
        string other;
        await using (var db = harness.Open())
        {
            var pausing = WorkItemFactory.NewCampaign("Pausing", now: Noon);
            var active = WorkItemFactory.NewCampaign("Active", now: Noon);
            db.Campaigns.AddRange(pausing, active);
            var row = NewBlock(db, pausing, Noon);
            first = BlockRows.Parked(db, pausing, row, Noon, operation, 5).PublicId;
            await db.SaveChangesAsync(Ct);
            block = row.Id;
        }

        Assert.Equal(1, await harness.ResumeAsync(Ct));
        await using (var db = harness.Open())
        {
            var row = await db.ProviderBlocks.Include(b => b.TrialWorkItem).ThenInclude(w => w!.Campaign).SingleAsync(b => b.Id == block, Ct);
            Assert.True(row.TrialWorkItem?.PublicId == first, $"The first trial is {row.TrialWorkItem?.PublicId}, not {first}.");
            row.TrialWorkItem!.Status = trial;
            row.TrialWorkItem.Campaign!.Status = CampaignStatus.Paused;

            // The other campaign's message is parked only now, so the first trial was drawn from the paused one's item
            // alone, whichever operation it names.
            other = BlockRows.Parked(db, await db.Campaigns.SingleAsync(c => c.Id != row.TrialWorkItem.CampaignId, Ct), row, Noon.AddMinutes(1)).PublicId;
            await db.SaveChangesAsync(Ct);
        }

        Assert.Equal(next ? 1 : 0, await harness.ResumeAsync(Ct));
        await using var read = harness.Open();
        var after = await read.ProviderBlocks.AsNoTracking().Include(b => b.TrialWorkItem).SingleAsync(b => b.Id == block, Ct);
        var expected = next ? other : first;
        Assert.True(after.TrialWorkItem?.PublicId == expected, $"The block's trial is {after.TrialWorkItem?.PublicId}, not {expected}.");
    }

    /// <summary>
    /// A trial that waits for a person's decision — the claim asked again about it, its input having changed — holds the
    /// block's one trial until they decide. Refused, it ends and leaves the live set, and the next trial due is another
    /// item the block holds; the block itself is neither tried nor cleared by the refusal.
    /// </summary>
    [Fact]
    public async Task A_trial_whose_new_decision_is_refused_frees_the_block_for_another_held_item()
    {
        using var harness = new DispatchHarness(Noon);
        var (block, parked) = await SeedAsync(harness, nextTrialAt: Noon, ("campaign.enroll", 5), ("campaign.enroll", 0));
        Assert.Equal(1, await harness.ResumeAsync(Ct));

        string question;
        await using (var db = harness.Open())
        {
            var trial = await db.WorkItems.SingleAsync(w => w.PublicId == parked[0], Ct);
            WorkItemTransitions.Apply(trial, WorkItemStatus.AwaitingApproval, Noon);
            var approval = AskedAgain(trial);
            db.Approvals.Add(approval);
            await db.SaveChangesAsync(Ct);
            question = approval.PublicId;
        }

        // While the person has not decided, the trial is still under way.
        Assert.Equal(0, await harness.ResumeAsync(Ct));

        await using (var db = harness.Open())
        {
            await new ApprovalService(db, new JournalWriter(harness.Clock), harness.Clock).RejectAsync(
                new ApprovalDecisionRequest(question, new ActorRef(ActorType.Human, "operator"), "not this person"),
                Ct);
        }

        Assert.Equal(1, await harness.ResumeAsync(Ct));
        await using (var db = harness.Open())
        {
            var row = await db.ProviderBlocks.AsNoTracking().Include(b => b.TrialWorkItem).SingleAsync(b => b.Id == block, Ct);
            Assert.True(row.TrialWorkItem?.PublicId == parked[1], $"The trial is {row.TrialWorkItem?.PublicId}, not {parked[1]}.");
            Assert.Equal((ProviderBlockStatus.Open, Noon), (row.Status, row.NextTrialAt));
            var refused = await db.WorkItems.AsNoTracking().SingleAsync(w => w.PublicId == parked[0], Ct);
            Assert.Equal(WorkItemStatus.Failed, refused.Status);
            var wait = await db.WorkItemWaits.AsNoTracking().SingleAsync(w => w.WorkItemId == row.TrialWorkItemId, Ct);
            Assert.Equal(WaitReleaseReason.BlockTrial, wait.ReleaseReason);
        }
    }

    /// <summary>
    /// Nothing is let through before the trial is due, and when it is due only an item the claim could run now is: not one
    /// of a paused campaign unless it is protective work, and not one already past its due date. Work that reaches a person
    /// is the trial where one could run; the protective item of a paused campaign is the next, once none can.
    /// </summary>
    [Fact]
    public async Task A_trial_waits_for_its_moment_and_lets_through_only_an_item_the_claim_could_run()
    {
        using var harness = new DispatchHarness(Noon);
        int block;
        string protective;
        string sent;
        await using (var db = harness.Open())
        {
            var paused = WorkItemFactory.NewCampaign("Paused", CampaignStatus.Paused, now: Noon);
            var active = WorkItemFactory.NewCampaign("Active", now: Noon);
            db.Campaigns.AddRange(paused, active);
            var row = NewBlock(db, active, Noon.AddMinutes(15));
            BlockRows.Parked(db, paused, row, Noon);
            BlockRows.Parked(db, active, row, Noon).DueAt = Noon.AddMinutes(10);
            await db.SaveChangesAsync(Ct);
            block = row.Id;
            protective = BlockRows.Parked(db, paused, row, Noon.AddMinutes(1), "optout.record").PublicId;
            var message = BlockRows.Parked(db, active, row, Noon.AddMinutes(2));
            message.DueAt = Noon.AddMinutes(20);
            await db.SaveChangesAsync(Ct);
            sent = message.PublicId;
        }

        harness.Clock.Now = Noon.AddMinutes(15).AddSeconds(-1);
        Assert.Equal(0, await harness.ResumeAsync(Ct));

        // Due now: of the four, the paused campaign's message and the message past its due date cannot run; of the two
        // that can, the active campaign's message reaches a person and comes first, and the protective item of the paused
        // campaign once it has ended.
        harness.Clock.Now = Noon.AddMinutes(15);
        foreach (var expected in new[] { sent, protective })
        {
            Assert.Equal(1, await harness.ResumeAsync(Ct));
            await using (var db = harness.Open())
            {
                var row = await db.ProviderBlocks.AsNoTracking().Include(b => b.TrialWorkItem).SingleAsync(b => b.Id == block, Ct);
                Assert.True(row.TrialWorkItem?.PublicId == expected, $"The trial is {row.TrialWorkItem?.PublicId}, not {expected}.");
            }

            await EndTrialAsync(harness, block, WorkItemStatus.Failed);
        }
    }

    /// <summary>
    /// A block whose trial is due but which holds nothing the claim could run lets nothing through and records no trial.
    /// </summary>
    [Fact]
    public async Task A_due_trial_with_nothing_that_could_run_lets_nothing_through()
    {
        using var harness = new DispatchHarness(Noon);
        int block;
        await using (var db = harness.Open())
        {
            var paused = WorkItemFactory.NewCampaign("Paused", CampaignStatus.Paused, now: Noon);
            db.Campaigns.Add(paused);
            var row = NewBlock(db, paused, Noon);
            BlockRows.Parked(db, paused, row, Noon);
            await db.SaveChangesAsync(Ct);
            block = row.Id;
        }

        Assert.Equal(0, await harness.ResumeAsync(Ct));
        await using var read = harness.Open();
        Assert.Null((await read.ProviderBlocks.AsNoTracking().SingleAsync(b => b.Id == block, Ct)).TrialWorkItemId);
    }

    /// <summary>
    /// A sender's block holds no trial of its own while the block of the account it belongs to is open: the account's trial
    /// answers first. Once the account's block clears, the sender's trial is let through at its own moment.
    /// </summary>
    [Fact]
    public async Task A_senders_block_holds_no_trial_while_its_accounts_block_is_open()
    {
        using var harness = new DispatchHarness(Noon);
        int account;
        int sender;
        string held;
        await using (var db = harness.Open())
        {
            var campaign = WorkItemFactory.NewCampaign(now: Noon);
            db.Campaigns.Add(campaign);
            var accountBlock = NewBlock(db, campaign, Noon.AddHours(1));
            var senderBlock = NewBlock(db, campaign, Noon, ProviderBlockScope.Sender, "snd_marta");
            held = BlockRows.Parked(db, campaign, senderBlock, Noon).PublicId;
            await db.SaveChangesAsync(Ct);
            (account, sender) = (accountBlock.Id, senderBlock.Id);
        }

        Assert.Equal(0, await harness.ResumeAsync(Ct));

        await ClearAsync(harness, account);
        Assert.Equal(1, await harness.ResumeAsync(Ct));
        await using var read = harness.Open();
        var row = await read.ProviderBlocks.AsNoTracking().Include(b => b.TrialWorkItem).SingleAsync(b => b.Id == sender, Ct);
        Assert.True(row.TrialWorkItem?.PublicId == held, $"The sender's trial is {row.TrialWorkItem?.PublicId}, not {held}.");
    }

    /// <summary>
    /// An account's block covers the work its senders' blocks hold, so its trial is drawn from that work as well as its
    /// own: with nothing parked on the account and one item on the block of a sender under it, that item is let through as
    /// the account's trial, and the sender's block, which holds no trial while the account's is open, records none.
    /// </summary>
    [Fact]
    public async Task An_accounts_trial_is_drawn_from_the_work_its_senders_blocks_hold()
    {
        using var harness = new DispatchHarness(Noon);
        int account;
        int sender;
        string held;
        await using (var db = harness.Open())
        {
            var campaign = WorkItemFactory.NewCampaign(now: Noon);
            db.Campaigns.Add(campaign);
            var accountBlock = NewBlock(db, campaign, Noon);
            var senderBlock = NewBlock(db, campaign, Noon.AddHours(1), ProviderBlockScope.Sender, "snd_marta");
            held = BlockRows.Parked(db, campaign, senderBlock, Noon).PublicId;
            await db.SaveChangesAsync(Ct);
            (account, sender) = (accountBlock.Id, senderBlock.Id);
        }

        Assert.Equal(1, await harness.ResumeAsync(Ct));
        await using var read = harness.Open();
        var rows = await read.ProviderBlocks.AsNoTracking().Include(b => b.TrialWorkItem)
            .Where(b => b.Id == account || b.Id == sender).ToDictionaryAsync(b => b.Id, Ct);
        Assert.True(rows[account].TrialWorkItem?.PublicId == held, $"The account's trial is {rows[account].TrialWorkItem?.PublicId}, not {held}.");
        Assert.True(rows[sender].TrialWorkItemId is null, $"The sender's block records trial {rows[sender].TrialWorkItemId}.");
        var wait = await read.WorkItemWaits.AsNoTracking().SingleAsync(w => w.ProviderBlockId == sender, Ct);
        Assert.Equal((WaitStatus.Released, WaitReleaseReason.BlockTrial), (wait.Status, wait.ReleaseReason));
    }

    /// <summary>
    /// A block of another binding is a block of its own: an open account block of one binding holds no trial of a sender's
    /// block under another.
    /// </summary>
    [Fact]
    public async Task An_account_block_of_another_binding_holds_no_senders_trial()
    {
        using var harness = new DispatchHarness(Noon);
        await using (var db = harness.Open())
        {
            var campaign = WorkItemFactory.NewCampaign(now: Noon);
            db.Campaigns.Add(campaign);
            NewBlock(db, campaign, Noon.AddHours(1), binding: "sha256:" + new string('b', 64));
            var senderBlock = NewBlock(db, campaign, Noon, ProviderBlockScope.Sender, "snd_marta");
            BlockRows.Parked(db, campaign, senderBlock, Noon);
            await db.SaveChangesAsync(Ct);
        }

        Assert.Equal(1, await harness.ResumeAsync(Ct));
    }

    /// <summary>
    /// The trial query reads the due blocks by the index of the blocks by status and trial moment, an account's open sender
    /// blocks by the index of one open block per sender, each block's open waits by the index of the waits by block and
    /// status, and every other row by its key: nothing scanned. Pinned by the plan,
    /// not by a timing.
    /// </summary>
    [Fact]
    public async Task The_trial_query_reads_the_due_blocks_and_their_waits_by_indexes_and_every_other_row_by_its_key()
    {
        using var harness = new DispatchHarness(Noon);
        await SeedAsync(harness, nextTrialAt: Noon.AddHours(1), ("message.send", 0));
        harness.Commands.Clear();

        await harness.ResumeAsync(Ct);

        AssertTrialPlan(PlanOf(harness, TrialQuery(harness)));
    }

    /// <summary>
    /// Ten thousand parked items across a hundred blocks, one of which has cleared and every other of which has its trial
    /// due: the step releases the cleared block's hundred items as <c>block_cleared</c> and one item of each of the other
    /// ninety-nine as its trial, and both queries keep their pinned plans at that size. What is asserted is what was
    /// released; how long it took is written to the test's output and not asserted.
    /// </summary>
    [Fact]
    public async Task Ten_thousand_parked_items_across_a_hundred_blocks_one_cleared_release_exactly_what_is_over()
    {
        using var harness = new DispatchHarness(Noon);
        await using (var db = harness.Open())
        {
            var campaign = WorkItemFactory.NewCampaign(now: Noon);
            db.Campaigns.Add(campaign);
            var template = WorkItemFactory.NewProviderOp(campaign, "message.send", Noon);
            db.WorkItems.Add(template);
            for (var n = 0; n < 100; n++)
            {
                var row = NewBlock(db, campaign, Noon, ProviderBlockScope.Sender, $"snd_{n:D3}");
                if (n == 0)
                {
                    row.Status = ProviderBlockStatus.Cleared;
                    row.ClearedAt = Noon;
                }
            }

            await db.SaveChangesAsync(Ct);
            await SeedParkedAsync(db, template.PublicId, count: 10_000, perBlock: 100);
        }

        harness.Commands.Clear();
        var timer = Stopwatch.StartNew();
        var resumed = await harness.ResumeAsync(Ct);
        timer.Stop();
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"The step over 10,000 parked items across 100 blocks took {timer.ElapsedMilliseconds} ms and released {resumed}.");

        Assert.Equal(199, resumed);
        await using (var db = harness.Open())
        {
            var closed = await db.WorkItemWaits.AsNoTracking()
                .Where(w => w.ProviderBlockId != null)
                .GroupBy(w => new { w.Status, w.ReleaseReason })
                .Select(g => new { g.Key.Status, g.Key.ReleaseReason, Count = g.Count() })
                .ToListAsync(Ct);
            var said = string.Join(", ", closed.Select(g => $"{g.Status}/{g.ReleaseReason}={g.Count}"));
            Assert.True(closed.Count == 3, $"The waits closed as {said}.");
            Assert.Contains(closed, g => g is { Status: WaitStatus.Released, ReleaseReason: WaitReleaseReason.BlockCleared, Count: 100 });
            Assert.Contains(closed, g => g is { Status: WaitStatus.Released, ReleaseReason: WaitReleaseReason.BlockTrial, Count: 99 });
            Assert.Contains(closed, g => g is { Status: WaitStatus.Open, ReleaseReason: null, Count: 9801 });
            var trials = await db.ProviderBlocks.AsNoTracking().CountAsync(b => b.TrialWorkItemId != null, Ct);
            Assert.Equal(99, trials);
        }

        ResumerTests.AssertReadByKeys(PlanOf(harness, Assert.Single(harness.Commands.Texts, text => text.Contains("work_item_wait_items", StringComparison.Ordinal))));
        AssertTrialPlan(PlanOf(harness, TrialQuery(harness)));
    }

    /// <summary>
    /// A cleared block holding more waits than the step releases in one scan is released over as many scans as the bound
    /// needs, the oldest waits first: each scan releases exactly the bound until fewer are left, and every wait is released
    /// once, with one line each.
    /// </summary>
    [Fact]
    public async Task A_cleared_block_holding_more_than_the_bound_is_released_over_as_many_scans_as_it_needs_oldest_first()
    {
        var bound = Resumer.ReleasesPerScan;
        var count = (bound * 2) + 7;
        using var harness = new DispatchHarness(Noon);
        await SeedOneClearedBlockAsync(harness, count);

        var released = new List<int>();
        int[] expected = [bound, bound, 7, 0];
        foreach (var _ in expected)
        {
            released.Add(await harness.ResumeAsync(Ct));
            await using var db = harness.Open();
            var waits = await db.WorkItemWaits.AsNoTracking().Where(w => w.ProviderBlockId != null).OrderBy(w => w.Id)
                .Select(w => w.Status).ToListAsync(Ct);
            var open = waits.FindIndex(s => s == WaitStatus.Open);
            var closed = waits.Count(s => s == WaitStatus.Released);
            Assert.True(
                open == -1 ? closed == count : open == closed && waits.Skip(open).All(s => s == WaitStatus.Open),
                $"After {released.Count} scans {closed} of {count} waits are released, and not the oldest ones: the first open is wait {open}.");
        }

        Assert.True(released.SequenceEqual(expected), $"The scans released {string.Join(", ", released)}, not {string.Join(", ", expected)}.");
        await using (var db = harness.Open())
        {
            var lines = await db.Journal.AsNoTracking().CountAsync(e => e.Kind == JournalKinds.WorkItemResumed, Ct);
            Assert.Equal(count, lines);
        }
    }

    /// <summary>
    /// The sized run of the bound: ten thousand parked items in one cleared block. The first scan releases exactly the
    /// bound, oldest first, and keeps both plans; the scans after it release the rest, and every wait is released once.
    /// How long the first scan and the whole release took is written to the test's output, not asserted.
    /// </summary>
    [Fact]
    public async Task Ten_thousand_parked_items_in_one_cleared_block_are_released_a_bound_a_scan()
    {
        var bound = Resumer.ReleasesPerScan;
        using var harness = new DispatchHarness(Noon);
        await SeedOneClearedBlockAsync(harness, 10_000);

        harness.Commands.Clear();
        var timer = Stopwatch.StartNew();
        var first = await harness.ResumeAsync(Ct);
        var firstMs = timer.ElapsedMilliseconds;
        Assert.Equal(bound, first);
        ResumerTests.AssertReadByKeys(PlanOf(harness, Assert.Single(harness.Commands.Texts, text => text.Contains("work_item_wait_items", StringComparison.Ordinal))));
        AssertTrialPlan(PlanOf(harness, TrialQuery(harness)));

        var scans = 1;
        var total = first;
        while (await harness.ResumeAsync(Ct) is var more and > 0)
        {
            Assert.True(more <= bound, $"Scan {scans + 1} released {more}, more than the bound of {bound}.");
            total += more;
            scans++;
        }

        timer.Stop();
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"Bound {bound}: the first scan over 10,000 parked items in one cleared block took {firstMs} ms; all {total} were released in {scans} scans and {timer.ElapsedMilliseconds} ms.");
        Assert.Equal(10_000, total);
        await using var db = harness.Open();
        var lines = await db.Journal.AsNoTracking().CountAsync(e => e.Kind == JournalKinds.WorkItemResumed, Ct);
        Assert.Equal(10_000, lines);
    }

    /// <summary>
    /// The operations the trial query names are the catalogue's own sets, written into the text from the catalogue: the
    /// held work that reaches a person, which it puts first, and the protective work, which it lets run in a paused
    /// campaign. No other operation the catalogue knows is named, and its order names the first set alone.
    /// </summary>
    [Fact]
    public async Task The_trial_query_names_exactly_the_catalogues_sets()
    {
        using var harness = new DispatchHarness(Noon);
        await SeedAsync(harness, nextTrialAt: Noon.AddHours(1), ("message.send", 0));
        harness.Commands.Clear();

        await harness.ResumeAsync(Ct);

        var text = TrialQuery(harness);
        var named = OperationCatalog.All.Select(contract => contract.Id).Where(id => text.Contains($"'{id}'", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).ToList();
        var sets = OperationCatalog.RunsInPausedCampaign.Union(OperationCatalog.TriedByProviderBlock).Order(StringComparer.Ordinal).ToList();
        Assert.True(named.SequenceEqual(sets), $"The trial query names {string.Join(", ", named)}; the catalogue's sets are {string.Join(", ", sets)}.");
        var first = System.Text.RegularExpressions.Regex.Match(text, "ORDER BY CASE WHEN w[.]operation IN [(](?<ids>[^)]*)[)] THEN 0").Groups["ids"].Value;
        Assert.Equal(
            OperationCatalog.TriedByProviderBlock.Order(StringComparer.Ordinal).Select(id => $"'{id}'"),
            first.Split(", ").Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The resume step and the claim's checks take the work a block holds from the catalogue's own properties and write
    /// no operation's name themselves: no identifier the catalogue knows appears in either source as a literal.
    /// </summary>
    [Theory]
    [InlineData("Dispatch", "Resumer.cs")]
    [InlineData("Routing", "ProviderOpPreflight.cs")]
    public void The_source_names_no_operation(string folder, string file)
    {
        var source = File.ReadAllText(Path.Combine(Publication.RepositoryRoot(), "runtime", "src", "Jason.Runtime", folder, file));
        var named = OperationCatalog.All.Select(contract => contract.Id)
            .Where(id => source.Contains($"\"{id}\"", StringComparison.Ordinal) || source.Contains($"'{id}'", StringComparison.Ordinal))
            .ToList();
        Assert.True(named.Count == 0, $"{file} names {string.Join(", ", named)}.");
    }

    /// <summary>
    /// The plan the trial query must keep: the due open blocks by the index of the blocks by status and trial moment, each
    /// one's account block by the index of one open block per account, its trial item and that item's campaign by their
    /// keys, the blocks its trial is drawn from by key and, for an account, its senders' open blocks by the index of one
    /// open block per sender, their open waits by the index of the waits by block and status, and each wait's item and
    /// campaign by their keys. Nothing scanned but the temporary order of one block's candidates.
    /// </summary>
    internal static void AssertTrialPlan(IReadOnlyList<string> plan)
    {
        var said = string.Join(" / ", plan);
        Assert.False(plan.Any(step => step.StartsWith("SCAN", StringComparison.Ordinal)), $"The trial query scans a table: {said}");
        Assert.True(
            plan.Where(step => step.StartsWith("SEARCH", StringComparison.Ordinal)).Distinct().Order(StringComparer.Ordinal).SequenceEqual(
            [
                "SEARCH a USING COVERING INDEX ix_provider_blocks_one_open_per_account (plugin_id=? AND binding_identity=?)",
                "SEARCH b USING INDEX ix_provider_blocks_status_next_trial_at (status=? AND next_trial_at<?)",
                "SEARCH c USING INTEGER PRIMARY KEY (rowid=?)",
                "SEARCH h USING COVERING INDEX ix_provider_blocks_one_open_per_sender (plugin_id=? AND binding_identity=?)",
                "SEARCH h USING INTEGER PRIMARY KEY (rowid=?)",
                "SEARCH t USING INTEGER PRIMARY KEY (rowid=?)",
                "SEARCH tc USING INTEGER PRIMARY KEY (rowid=?)",
                "SEARCH w USING INTEGER PRIMARY KEY (rowid=?)",
                "SEARCH x USING INDEX ix_work_item_waits_provider_block_id_status (provider_block_id=? AND status=?)",
            ]),
            $"The trial query's plan changed: {said}");
    }

    private static string TrialQuery(DispatchHarness harness) =>
        Assert.Single(harness.Commands.Texts, text => text.Contains("AS \"BlockId\"", StringComparison.Ordinal));

    /// <summary>What SQLite plans for the command the step sent, its parameters bound to the harness's clock; one step a line.</summary>
    internal static List<string> PlanOf(DispatchHarness harness, string text)
    {
        using var db = harness.Open();
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        connection.Open();
        using var explain = connection.CreateCommand();
        explain.CommandText = "EXPLAIN QUERY PLAN " + text;
        foreach (System.Text.RegularExpressions.Match name in System.Text.RegularExpressions.Regex.Matches(text, @"@p\d+"))
        {
            if (!explain.Parameters.Contains(name.Value))
            {
                explain.Parameters.AddWithValue(name.Value, "2026-09-14 12:00:00");
            }
        }

        using var reader = explain.ExecuteReader();
        var plan = new List<string>();
        while (reader.Read())
        {
            plan.Add(reader.GetString(3));
        }

        return plan;
    }

    /// <summary>One active campaign, one account block whose trial is due at <paramref name="nextTrialAt"/>, and the items it holds, a minute apart.</summary>
    private static async Task<(int Block, string[] Parked)> SeedAsync(DispatchHarness harness, DateTime nextTrialAt, params (string Operation, int Priority)[] items)
    {
        await using var db = harness.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        var block = NewBlock(db, campaign, nextTrialAt);
        var parked = new List<WorkItem>();
        for (var n = 0; n < items.Length; n++)
        {
            parked.Add(BlockRows.Parked(db, campaign, block, Noon.AddMinutes(n), items[n].Operation, items[n].Priority));
            await db.SaveChangesAsync(Ct);
        }

        return (block.Id, [.. parked.Select(p => p.PublicId)]);
    }

    /// <summary>A block of <paramref name="campaign"/>'s binding, opened by an item of it that ended.</summary>
    private static ProviderBlock NewBlock(
        JasonDbContext db,
        Campaign campaign,
        DateTime nextTrialAt,
        ProviderBlockScope scope = ProviderBlockScope.Account,
        string? sender = null,
        string binding = BlockRows.Binding)
    {
        var opener = WorkItemFactory.NewProviderOp(campaign, "message.send", Noon);
        opener.Status = WorkItemStatus.Failed;
        opener.FinishedAt = Noon;
        db.WorkItems.Add(opener);
        var block = BlockRows.Block(opener, Noon, nextTrialAt, scope, sender, binding);
        db.ProviderBlocks.Add(block);
        return block;
    }

    private static async Task ClearAsync(DispatchHarness harness, int block)
    {
        await using var db = harness.Open();
        var row = await db.ProviderBlocks.SingleAsync(b => b.Id == block, Ct);
        row.Status = ProviderBlockStatus.Cleared;
        row.ClearedAt = harness.Clock.Now.UtcDateTime;
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>The block's trial item ends in <paramref name="how"/>; the block still holds and its next trial is due now.</summary>
    private static async Task EndTrialAsync(DispatchHarness harness, int block, WorkItemStatus how)
    {
        await using var db = harness.Open();
        var row = await db.ProviderBlocks.Include(b => b.TrialWorkItem).SingleAsync(b => b.Id == block, Ct);
        row.TrialWorkItem!.Status = how;
        row.TrialWorkItem.FinishedAt = harness.Clock.Now.UtcDateTime;
        row.TrialWorkItemId = null;
        row.NextTrialAt = harness.Clock.Now.UtcDateTime;
        await db.SaveChangesAsync(Ct);
    }

    private static async Task AssertWaitingAsync(DispatchHarness harness, IReadOnlyCollection<string> items)
    {
        await using var db = harness.Open();
        var statuses = await db.WorkItems.AsNoTracking().Where(w => items.Contains(w.PublicId)).Select(w => w.Status).ToListAsync(Ct);
        Assert.True(statuses.Count == items.Count && statuses.All(s => s == WorkItemStatus.Waiting), $"The parked items are {string.Join(", ", statuses)}.");
    }

    /// <summary>The question the claim asks again about <paramref name="item"/>, as for an input changed since a person decided.</summary>
    private static Approval AskedAgain(WorkItem item) => new()
    {
        PublicId = PublicId.New(ApprovalGate.IdPrefix),
        WorkItem = item,
        CampaignId = item.CampaignId,
        Operation = item.Operation!,
        OperationVersion = 1,
        Subject = new JsonObject { ["operation"] = item.Operation, ["work_item"] = item.PublicId },
        SubjectHash = "sha256:asked-again",
        Preview = new JsonObject { ["intent"] = "Put one person into a campaign at the provider." },
        PluginId = BlockRows.Plugin,
        BindingIdentity = BlockRows.Binding,
        RouteScope = RouteScope.GlobalDefault,
        PluginSnapshotId = "snp_one",
        RoutingSnapshotId = "rts_one",
        Reason = ApprovalGate.InputChanged,
        RequestedAt = Noon,
    };

    /// <summary>One active campaign whose one account block has cleared, holding <paramref name="count"/> parked items.</summary>
    private static async Task SeedOneClearedBlockAsync(DispatchHarness harness, int count)
    {
        await using var db = harness.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        var template = WorkItemFactory.NewProviderOp(campaign, "message.send", Noon);
        db.WorkItems.Add(template);
        var block = NewBlock(db, campaign, Noon.AddHours(1));
        block.Status = ProviderBlockStatus.Cleared;
        block.ClearedAt = Noon;
        await db.SaveChangesAsync(Ct);
        await SeedParkedAsync(db, template.PublicId, count, perBlock: count);
    }

    /// <summary>
    /// The rows of a sized run, in two statements from the one item the test made through the runtime:
    /// <paramref name="count"/> waiting items, <paramref name="perBlock"/> to each block in the order the blocks were made,
    /// and the wait of each.
    /// </summary>
    private static async Task SeedParkedAsync(JasonDbContext db, string template, int count, int perBlock)
    {
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        await connection.OpenAsync(Ct);
        var columns = new List<string>();
        using (var info = connection.CreateCommand())
        {
            info.CommandText = "PRAGMA table_info(work_items)";
            using var reader = await info.ExecuteReaderAsync(Ct);
            while (await reader.ReadAsync(Ct))
            {
                columns.Add(reader.GetString(1));
            }
        }

        var into = columns.Where(c => c != "id").ToList();
        var values = into.Select(c => c switch
        {
            "public_id" => "'wi_B' || printf('%05d', n.v)",
            "status" => "'waiting'",
            _ => "t." + c,
        });

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(Ct);
        await RunAsync(
            $"WITH RECURSIVE n(v) AS (SELECT 1 UNION ALL SELECT v + 1 FROM n WHERE v < {count}) "
            + $"INSERT INTO work_items ({string.Join(", ", into)}) SELECT {string.Join(", ", values)} FROM work_items t, n WHERE t.public_id = '{template}'");
        await RunAsync(
            $"""
            WITH RECURSIVE n(v) AS (SELECT 1 UNION ALL SELECT v + 1 FROM n WHERE v < {count}),
                 blocks(id, k) AS (SELECT id, ROW_NUMBER() OVER (ORDER BY id) FROM provider_blocks)
            INSERT INTO work_item_waits (work_item_id, attempt_id, provider_block_id, mode, until, deadline, status, opened_at)
            SELECT p.id, NULL, b.id, NULL, NULL, NULL, 'open', @p0
            FROM n
            JOIN work_items p ON p.public_id = 'wi_B' || printf('%05d', n.v)
            JOIN blocks b ON b.k = (n.v - 1) / {perBlock} + 1
            ORDER BY n.v
            """,
            Noon);
        await transaction.CommitAsync(Ct);

        async Task RunAsync(string sql, params object[] parameters)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            for (var i = 0; i < parameters.Length; i++)
            {
                command.Parameters.AddWithValue($"@p{i}", parameters[i]);
            }

            await command.ExecuteNonQueryAsync(Ct);
        }
    }
}
