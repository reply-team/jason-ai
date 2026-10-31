using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// Two attempts on one account that meet its block at the same moment. Each read the account's open blocks before the
/// other had saved, so each found none and opened one; the database keeps one open block per account, and the save that
/// lands second is refused. What it decided is still true of the account — the provider refused the work for a reason
/// outside it — so the handler decides once more against the block that now exists: the attempt parks on it, uncounted.
/// </summary>
public class ProviderBlockRaceTests
{
    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static OperationContract Activate => OperationCatalog.Find("campaign.activate")!;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Two_attempts_that_meet_one_block_together_both_wait_on_the_block_the_first_save_opened(bool oneCampaign)
    {
        var command = new FakeCommand(WorkItemKind.ProviderOp, _ => Task.FromResult<CommandOutcome>(new CommandOutcome.Provider(Blocked(), null)));
        using var harness = new DispatchHarness(Noon, commands: command);
        var (loser, winner) = await SeedTwoClaimedAsync(harness, oneCampaign);

        // The first verdict is about to be saved with the block it opened; the other attempt runs whole before it and
        // saves the same block first.
        harness.InterfereOnceBeforeSaving(
            context => context.ChangeTracker.Entries<ProviderBlock>().Any(entry => entry.State == EntityState.Added),
            () => RunAsync(harness, winner).GetAwaiter().GetResult());
        await RunAsync(harness, loser);

        await using var db = harness.Open();
        var block = await db.ProviderBlocks.AsNoTracking().SingleAsync(Ct);
        Assert.True(block.SeenCount == 2, $"The block was seen {block.SeenCount} times; the second attempt met the one the first opened.");
        Assert.Equal(winner.AttemptId, block.OpenedByAttemptId);
        foreach (var work in new[] { loser, winner })
        {
            var item = await db.WorkItems.AsNoTracking().Include(w => w.Attempts).SingleAsync(w => w.Id == work.WorkItemId, Ct);
            Assert.Equal((WorkItemStatus.Waiting, 0), (item.Status, item.AttemptCount));
            Assert.Equal(AttemptStatus.Blocked, Assert.Single(item.Attempts).Status);
            Assert.Equal(block.Id, (await db.WorkItemWaits.AsNoTracking().SingleAsync(w => w.WorkItemId == work.WorkItemId, Ct)).ProviderBlockId);
        }

        // Each campaign is told once, of the one block.
        var told = await db.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.ProviderBlocked).ToListAsync(Ct);
        Assert.Equal(oneCampaign ? 1 : 2, told.Count);
        Assert.All(told, line => Assert.Equal(block.PublicId, line.Key));
        Assert.Equal(told.Count, told.Select(line => line.CampaignId).Distinct().Count());
    }

    /// <summary>
    /// Two held calls that reach people, on one account, that get past its block at the same moment. Each read the block open; the one that
    /// saves first clears it, and the other's clear finds it no longer open. One block, cleared by the first; each campaign
    /// that was told of it is told once that it cleared; both calls succeeded.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Two_calls_that_get_past_one_block_together_clear_it_once_and_say_so_once(bool oneCampaign)
    {
        var command = new FakeCommand(WorkItemKind.ProviderOp, _ => Task.FromResult<CommandOutcome>(new CommandOutcome.Provider(Live(), null)));
        using var harness = new DispatchHarness(Noon, commands: command);
        var (loser, winner) = await SeedTwoClaimedAsync(harness, oneCampaign);
        int block;
        await using (var db = harness.Open())
        {
            var campaign = await db.Campaigns.FirstAsync(Ct);
            var opener = WorkItemFactory.NewProviderOp(campaign, Activate.Id, Noon);
            opener.Status = WorkItemStatus.Failed;
            var row = BlockRows.Block(opener, Noon, Noon.AddHours(1));
            row.PluginId = TestPlugins.FakeProviderId;
            row.BindingIdentity = ProviderBlocks.Identity(null);
            db.AddRange(opener, row);
            var journal = new JournalWriter(harness.Clock);
            foreach (var work in new[] { loser, winner })
            {
                var item = await db.WorkItems.Include(w => w.Campaign).SingleAsync(w => w.Id == work.WorkItemId, Ct);
                await ProviderBlocks.SayBlockedAsync(db, journal, item, row, attempt: null, Ct);
            }

            await db.SaveChangesAsync(Ct);
            block = row.Id;
        }

        // The first success is about to be saved with the block it cleared; the other call runs whole before it and
        // clears the block first.
        harness.InterfereOnceBeforeSaving(
            context => context.ChangeTracker.Entries<ProviderBlock>().Any(entry => entry.State == EntityState.Modified),
            () => RunAsync(harness, winner).GetAwaiter().GetResult());
        await RunAsync(harness, loser);

        await using var read = harness.Open();
        var cleared = await read.ProviderBlocks.AsNoTracking().SingleAsync(b => b.Id == block, Ct);
        Assert.True(
            cleared.Status == ProviderBlockStatus.Cleared && cleared.ClearedByAttemptId == winner.AttemptId,
            $"The block is {cleared.Status}, cleared by attempt {cleared.ClearedByAttemptId}, not the first one's {winner.AttemptId}.");
        var items = await read.WorkItems.AsNoTracking().Where(w => w.Id == loser.WorkItemId || w.Id == winner.WorkItemId).ToListAsync(Ct);
        Assert.True(items.All(w => w.Status == WorkItemStatus.Succeeded), $"The two calls ended {string.Join(", ", items.Select(w => w.Status))}.");

        var said = await read.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.ProviderBlockCleared).ToListAsync(Ct);
        Assert.True(
            said.Count == (oneCampaign ? 1 : 2) && said.Select(e => e.CampaignId).Distinct().Count() == said.Count && said.All(e => e.AttemptId == winner.AttemptPublicId),
            $"provider_block_cleared was said {said.Count} time(s): {string.Join(", ", said.Select(e => $"{e.CampaignId}/{e.AttemptId}"))}.");
    }

    /// <summary>
    /// Only the two indexes that keep one block open per account and per sender say another attempt opened a block
    /// first; a save another constraint of the table refuses — a public id used twice — is not decided again.
    /// </summary>
    [Fact]
    public async Task Only_the_one_open_indexes_say_a_block_was_opened_elsewhere()
    {
        using var database = new TestDatabase();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var opener = WorkItemFactory.NewProviderOp(campaign, Activate.Id, Noon);
        var first = BlockRows.Block(opener, Noon, Noon);
        await using (var db = database.Open())
        {
            db.AddRange(opener, first);
            await db.SaveChangesAsync(Ct);
        }

        await using var again = database.Open();
        var item = await again.WorkItems.Include(w => w.Attempts).SingleAsync(w => w.Id == opener.Id, Ct);
        again.Add(BlockRows.Block(item, Noon, Noon));
        var twice = await Assert.ThrowsAsync<DbUpdateException>(() => again.SaveChangesAsync(Ct));
        Assert.True(ProviderBlocks.OpenedFirstElsewhere(twice), $"A second open block of the account was not taken for one opened elsewhere: {twice.InnerException?.Message}");

        await using var reuse = database.Open();
        item = await reuse.WorkItems.Include(w => w.Attempts).SingleAsync(w => w.Id == opener.Id, Ct);
        var reused = BlockRows.Block(item, Noon, Noon, ProviderBlockScope.Sender, "snd_marta");
        reused.PublicId = first.PublicId;
        reuse.Add(reused);
        var refused = await Assert.ThrowsAsync<DbUpdateException>(() => reuse.SaveChangesAsync(Ct));
        Assert.False(ProviderBlocks.OpenedFirstElsewhere(refused), $"A refusal of another constraint was taken for a block opened elsewhere: {refused.InnerException?.Message}");
    }

    internal static Task RunAsync(DispatchHarness harness, ClaimedWork work) =>
        AttemptHandler.RunAsync(harness.Scopes, work, harness.Registry, NullLogger.Instance);

    /// <summary>Two activations on one account, each claimed, in one campaign or in two.</summary>
    internal static async Task<(ClaimedWork First, ClaimedWork Second)> SeedTwoClaimedAsync(DispatchHarness harness, bool oneCampaign)
    {
        await using var db = harness.Open();
        var first = WorkItemFactory.NewCampaign(now: Noon);
        var second = oneCampaign ? first : WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.AddRange(new[] { first, second }.Distinct());
        var claimed = new List<(WorkItem Item, Attempt Attempt)>();
        foreach (var campaign in new[] { first, second })
        {
            var item = WorkItemFactory.NewProviderOp(campaign, Activate.Id, Noon);
            item.Status = WorkItemStatus.Scheduled;
            var attempt = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Scheduled, Noon);
            db.WorkItems.Add(item);
            db.Attempts.Add(attempt);
            claimed.Add((item, attempt));
        }

        await db.SaveChangesAsync(Ct);
        var works = claimed
            .Select(c => new ClaimedWork(c.Item.Id, c.Attempt.Id, c.Item.PublicId, c.Attempt.PublicId, c.Item.Campaign!.PublicId, Plan()))
            .ToList();
        return (works[0], works[1]);
    }

    /// <summary>A plan as a claim leaves it for an activation, through a route that binds nothing.</summary>
    private static ProviderOpPlan Plan() => new(
        TestPlugins.Loaded(TestPlugins.FakeProviderId, [Activate.Id]),
        PluginProtocol.SnapshotIdPrefix + "_01K5B7Q2WE5X3M9T0YH4C6RDNA",
        RouteSnapshot.IdPrefix + "_01K5B7Q2WE5X3M9T0YH4C6RDNB",
        RouteScope.GlobalDefault,
        null,
        null,
        Activate,
        new JsonObject { ["campaign"] = new JsonObject { ["external_id"] = "seq_4410" } });

    /// <summary>The campaign was live already: the call got past whatever held the account.</summary>
    private static PluginInvocationResult Live() => new(
        new InvocationOutcome.Succeeded(
            new JsonObject { ["items"] = new JsonArray(new JsonObject { ["campaign_external_id"] = "seq_4410", ["status"] = "already_live", ["was"] = null, ["enrolled"] = 3 }) },
            null,
            new OutcomeDiagnostics(12, 1, 0, 1)),
        Provenance(),
        Launch: null);

    /// <summary>The account refused the call for its billing.</summary>
    internal static PluginInvocationResult Blocked() => new(
        new InvocationOutcome.Failed(
            new OutcomeError(FailureClass.Blocked, "provider_blocked", "the plan has lapsed", null, null, new JsonObject { ["scope"] = "account", ["reason"] = "billing" }),
            new OutcomeDiagnostics(12, 1, 0, 1)),
        Provenance(),
        Launch: null);

    private static InvocationProvenance Provenance() =>
        new(
            TestPlugins.FakeProviderId,
            "1.0.0",
            "sha256:0",
            PluginProtocol.CurrentVersion,
            PluginProtocol.OperationContractVersion,
            PluginProtocol.InvocationIdPrefix + "_01K5B7Q2WE5X3M9T0YH4C6RDNC",
            "att_01K5B7Q2WE5X3M9T0YH4C6RDND",
            PluginProtocol.SnapshotIdPrefix + "_01K5B7Q2WE5X3M9T0YH4C6RDNE");
}
