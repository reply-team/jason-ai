using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// The chronicle of one work item that failed once and then succeeded. What the dispatcher writes is the only
/// record of why work moved, so the order, the actors and the ids are the contract — not an implementation detail.
/// </summary>
public class DispatcherJournalTests
{
    private const string Settings = """
        {"Dispatcher":{"TickSeconds":3600,"RetryDelaySeconds":0},"Roles":{"DefaultEntryCommand":["agent-host"]}}
        """;

    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_whole_life_is_written_down_in_order()
    {
        var clock = new FixedClock(Noon);
        var command = new LifeCommand();
        await using var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths => File.WriteAllText(paths.UserSettingsFile, Settings),
            clock: clock,
            configureServices: services => services.AddSingleton<ICommand>(command));
        command.Services = fixture.Runtime.Services;

        Assert.True(await DispatchHarness.FirstScanDoneAsync(fixture.Resolve<DispatcherStatus>(), Ct));
        var campaign = await fixture.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Chronicle" }, Ct);
        await fixture.PostOkAsync<CampaignDto>(Operations.CampaignStart, new { campaign_id = campaign.Id }, Ct);
        var itemId = await SeedAsync(fixture, campaign.Id, Ct);

        var runner = fixture.Resolve<ScanRunner>();
        Assert.Equal(1, (await runner.ScanOnceAsync(Ct)).Claimed);
        Assert.True(await DispatchHarness.EventuallyAsync(() => StatusOf(fixture.Paths, itemId) == WorkItemStatus.Created, Ct));

        Assert.Equal(1, (await runner.ScanOnceAsync(Ct)).Claimed);
        Assert.True(await DispatchHarness.EventuallyAsync(() => StatusOf(fixture.Paths, itemId) == WorkItemStatus.Succeeded, Ct));

        await using var db = Open(fixture.Paths);
        var entries = await db.Journal.AsNoTracking().Where(e => e.WorkItemId == itemId).OrderBy(e => e.Id).ToListAsync(Ct);
        Assert.Equal(
            new[]
            {
                JournalKinds.WorkItemCreated,
                JournalKinds.WorkItemScheduled,
                JournalKinds.WorkItemProcessing,
                JournalKinds.WorkItemReleased,
                JournalKinds.WorkItemScheduled,
                JournalKinds.WorkItemProcessing,
                JournalKinds.WorkItemSucceeded,
            },
            entries.Select(e => e.Kind).ToArray());

        var campaignRow = await db.Campaigns.AsNoTracking().SingleAsync(c => c.PublicId == campaign.Id, Ct);
        Assert.All(entries, entry =>
        {
            Assert.Equal(campaignRow.Id, entry.CampaignId);
            Assert.Equal(itemId, entry.WorkItemId);
        });

        // Everything the dispatcher did is signed by the dispatcher; the completion is signed by the attempt.
        Assert.All(entries.Skip(1).Take(5), entry =>
        {
            Assert.Equal(ActorType.System, entry.ActorType);
            Assert.Equal("dispatcher", entry.ActorId);
            Assert.NotNull(entry.AttemptId);
        });
        Assert.Null(entries[0].AttemptId);

        var attempts = await db.Attempts.AsNoTracking().OrderBy(a => a.Number).ToListAsync(Ct);
        Assert.Equal(2, attempts.Count);
        Assert.Equal(attempts[0].PublicId, entries[3].AttemptId);
        Assert.Equal(attempts[1].PublicId, entries[6].AttemptId);
        Assert.Equal(ActorType.Attempt, entries[6].ActorType);
        Assert.Equal(attempts[1].PublicId, entries[6].ActorId);
    }

    [Fact]
    public async Task The_campaign_chronicle_carries_the_work_it_did()
    {
        var clock = new FixedClock(Noon);
        var command = new LifeCommand();
        await using var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths => File.WriteAllText(paths.UserSettingsFile, Settings),
            clock: clock,
            configureServices: services => services.AddSingleton<ICommand>(command));
        command.Services = fixture.Runtime.Services;

        Assert.True(await DispatchHarness.FirstScanDoneAsync(fixture.Resolve<DispatcherStatus>(), Ct));
        var campaign = await fixture.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Chronicle" }, Ct);
        await fixture.PostOkAsync<CampaignDto>(Operations.CampaignStart, new { campaign_id = campaign.Id }, Ct);
        var itemId = await SeedAsync(fixture, campaign.Id, Ct);

        var runner = fixture.Resolve<ScanRunner>();
        await runner.ScanOnceAsync(Ct);
        Assert.True(await DispatchHarness.EventuallyAsync(() => StatusOf(fixture.Paths, itemId) == WorkItemStatus.Created, Ct));
        await runner.ScanOnceAsync(Ct);
        Assert.True(await DispatchHarness.EventuallyAsync(() => StatusOf(fixture.Paths, itemId) == WorkItemStatus.Succeeded, Ct));

        var page = await fixture.PostOkAsync<Page<JournalEntryDto>>(Operations.JournalList, new { campaign_id = campaign.Id, limit = 50 }, Ct);

        // Newest first, the campaign's own lines and its work interleaved in one chronicle.
        Assert.Equal(JournalKinds.WorkItemSucceeded, page.Items[0].Kind);
        Assert.Equal(JournalKinds.CampaignCreated, page.Items[^1].Kind);
        Assert.Equal(9, page.Items.Count);
        Assert.All(page.Items, entry => Assert.Equal(campaign.Id, entry.CampaignId));

        var item = await fixture.PostOkAsync<Page<JournalEntryDto>>(Operations.JournalList, new { work_item_id = itemId, limit = 50 }, Ct);
        Assert.Equal(7, item.Items.Count);
    }

    /// <summary>The item is created the way a caller creates one, so the first line of the chronicle is real too.</summary>
    private static async Task<string> SeedAsync(RuntimeApiFixture fixture, string campaignId, CancellationToken ct)
    {
        var item = await fixture.PostOkAsync<WorkItemDto>(
            Operations.WorkItemCreate,
            new { campaign_id = campaignId, kind = "ai_role", role = "researcher" },
            ct);
        return item.Id;
    }

    private static WorkItemStatus StatusOf(JasonPaths paths, string publicId)
    {
        using var db = Open(paths);
        return db.WorkItems.AsNoTracking().Where(w => w.PublicId == publicId).Select(w => w.Status).Single();
    }

    private static JasonDbContext Open(JasonPaths paths) => new(JasonDbContext.CreateOptions(paths.DatabaseFile));

    /// <summary>Crashes once, then reports success the way an executor does — through the runtime, not the handler.</summary>
    private sealed class LifeCommand : ICommand
    {
        private int _runs;

        public WorkItemKind Kind => WorkItemKind.AiRole;

        public IServiceProvider? Services { get; set; }

        public async Task<CommandOutcome> RunAsync(CommandContext context, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);
            var launch = new AttemptLaunchDto(context.EntryCommand, context.WorkDir, 1234, null);
            if (Interlocked.Increment(ref _runs) == 1)
            {
                return new CommandOutcome.Exited(3, "boom", launch with { ExitCode = 3 });
            }

            await using var scope = Services!.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
            var attempt = await db.Attempts.Include(a => a.WorkItem).SingleAsync(a => a.PublicId == context.AttemptId, cancellationToken);
            scope.ServiceProvider.GetRequiredService<AttemptOutcomes>().Succeed(
                db,
                attempt.WorkItem!,
                attempt,
                JsonNode.Parse("""{"summary":"done"}"""),
                Actors.ForAttempt(attempt));
            await db.SaveChangesAsync(cancellationToken);
            return new CommandOutcome.Completed(launch with { ExitCode = 0 });
        }
    }
}
