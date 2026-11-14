using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Contacts;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Execution;
using Jason.Runtime.Tests.Plugins;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// Two reads, in two campaigns, that find bounces of one address at the same moment. Each read the register before the
/// other had saved, so each staged the address; the register keeps one entry per pair, and the save that lands second is
/// refused. That refusal says the address is already on the register: the read is decided once more, stages nothing, and
/// ends as it answered — never a failed read and never a repeated one.
/// </summary>
public class BounceStopRaceTests
{
    private static readonly DateTime Noon = new(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

    private const string Address = "ada@northwind.example";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static OperationContract Status => OperationCatalog.Find("message.status")!;

    [Fact]
    public async Task A_stop_the_register_refuses_for_its_pair_is_already_on_the_register_and_the_read_ends_as_it_answered()
    {
        var command = new FakeCommand(
            WorkItemKind.ProviderOp,
            _ => Task.FromResult<CommandOutcome>(new CommandOutcome.Provider(Answered(), null)));
        using var harness = new DispatchHarness(Noon, commands: command);
        var work = await SeedClaimedReadAsync(harness);

        // The read's verdict is about to be saved with the entry it staged; another writer puts the same address on the
        // register first.
        harness.InterfereOnceBeforeSaving(
            context => context.ChangeTracker.Entries<Suppression>().Any(entry => entry.State == EntityState.Added),
            () =>
            {
                using var other = harness.Open();
                other.Suppressions.Add(new Suppression
                {
                    PublicId = "sup_01JB6K8TQ2W9V4MZ0C3Y7H5NRA", Channel = "email", Value = Address, Reason = "Another read's stop.", CreatedAt = Noon,
                });
                other.SaveChanges();
            });
        await ProviderBlockRaceTests.RunAsync(harness, work);

        await using var db = harness.Open();
        var item = await db.WorkItems.AsNoTracking().Include(w => w.Attempts).SingleAsync(w => w.Id == work.WorkItemId, Ct);
        Assert.Equal(WorkItemStatus.Succeeded, item.Status);
        Assert.Equal(AttemptStatus.Succeeded, Assert.Single(item.Attempts).Status);

        var entry = await db.Suppressions.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(("email", Address, "Another read's stop."), (entry.Channel, entry.Value, entry.Reason));
        Assert.False(await db.Journal.AnyAsync(e => e.Kind == JournalKinds.SuppressionAdded, Ct));
        Assert.Equal(1, await db.Journal.CountAsync(e => e.Kind == JournalKinds.MessageBounceObserved, Ct));
    }

    /// <summary>Only the register's index of a pair says the address is already there; another refusal of the table is not it.</summary>
    [Fact]
    public async Task Only_the_register_s_index_of_a_pair_says_the_address_is_already_there()
    {
        using var database = new TestDatabase();
        await using (var db = database.Open())
        {
            db.Suppressions.Add(new Suppression { PublicId = "sup_A", Channel = "email", Value = Address, CreatedAt = Noon });
            await db.SaveChangesAsync(Ct);
        }

        await using var twice = database.Open();
        twice.Suppressions.Add(new Suppression { PublicId = "sup_B", Channel = "email", Value = Address, CreatedAt = Noon });
        var pair = await Assert.ThrowsAsync<DbUpdateException>(() => twice.SaveChangesAsync(Ct));
        Assert.True(SuppressionService.HeldFirstElsewhere(pair), $"A second entry of the pair was not taken for one already there: {pair.InnerException?.Message}");

        await using var reuse = database.Open();
        reuse.Suppressions.Add(new Suppression { PublicId = "sup_A", Channel = "email", Value = "grace@northwind.example", CreatedAt = Noon });
        var refused = await Assert.ThrowsAsync<DbUpdateException>(() => reuse.SaveChangesAsync(Ct));
        Assert.False(SuppressionService.HeldFirstElsewhere(refused), $"A refusal of another constraint was taken for the pair: {refused.InnerException?.Message}");
    }

    /// <summary>A send of Ada's in one campaign, and a claimed read of it.</summary>
    private static async Task<ClaimedWork> SeedClaimedReadAsync(DispatchHarness harness)
    {
        await using var db = harness.Open();
        var (send, read) = await BounceFactsTests.SeedAsync(db);
        read.Status = WorkItemStatus.Scheduled;
        var attempt = WorkItemFactory.NewAttempt(read, 1, AttemptStatus.Scheduled, Noon);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(Ct);
        var campaign = await db.Campaigns.AsNoTracking().SingleAsync(c => c.Id == read.CampaignId, Ct);
        return new ClaimedWork(read.Id, attempt.Id, read.PublicId, attempt.PublicId, campaign.PublicId, Plan(BounceFactsTests.Input(send)));
    }

    /// <summary>A plan as a claim leaves it for a read of the send, through a route that binds nothing.</summary>
    private static ProviderOpPlan Plan(System.Text.Json.Nodes.JsonObject input) => new(
        TestPlugins.Loaded(TestPlugins.FakeProviderId, [Status.Id]),
        PluginProtocol.SnapshotIdPrefix + "_01K5B7Q2WE5X3M9T0YH4C6RDNA",
        RouteSnapshot.IdPrefix + "_01K5B7Q2WE5X3M9T0YH4C6RDNB",
        RouteScope.GlobalDefault,
        null,
        null,
        Status,
        input);

    /// <summary>The read found the send went and came back from an address that takes no mail.</summary>
    private static PluginInvocationResult Answered() => new(
        new InvocationOutcome.Succeeded(BounceFactsTests.Bounced("hard", "hard"), null, new OutcomeDiagnostics(12, 1, 0, 1)),
        new InvocationProvenance(
            TestPlugins.FakeProviderId,
            "1.0.0",
            "sha256:0",
            PluginProtocol.CurrentVersion,
            PluginProtocol.OperationContractVersion,
            PluginProtocol.InvocationIdPrefix + "_01K5B7Q2WE5X3M9T0YH4C6RDNC",
            "att_01K5B7Q2WE5X3M9T0YH4C6RDND",
            PluginProtocol.SnapshotIdPrefix + "_01K5B7Q2WE5X3M9T0YH4C6RDNE"),
        Launch: null);
}
