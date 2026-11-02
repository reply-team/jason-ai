using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Plugins;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// When the scan starts a read of a campaign's conversations, and when it skips one. A read is one work item through
/// the routine every work item is made by, so it is claimed, routed, retried and recorded like any other; a campaign
/// that cannot be read is skipped by a reason, and a skip moves the campaign on as an empty read would.
/// </summary>
public class InboundSyncStartTests
{
    private static readonly DateTime Noon = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

    private const string Reader = "reader";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>One read: a provider operation the dispatcher made, about no one, starting where the campaign's watermark is.</summary>
    [Fact]
    public async Task A_linked_routed_live_campaign_gets_one_read_starting_at_its_watermark()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await ReadableAsync(harness, watermark: Noon.AddHours(-2).AddMilliseconds(750));

        var report = await harness.SyncAsync(Ct);

        Assert.Equal(1, report.Synced);
        await using var db = harness.Open();
        var item = await db.WorkItems.SingleAsync(Ct);
        Assert.Equal(WorkItemKind.ProviderOp, item.Kind);
        Assert.Equal("conversation.list", item.Operation);
        Assert.Null(item.ContactId);
        Assert.Null(item.Role);
        // Ten above the responders' priority: detecting a reply never waits behind answering one.
        Assert.Equal(new ResponderOptions().Priority + InboundSync.ReadAboveResponders, item.Priority);
        Assert.Equal(ActorType.System, item.CreatedByType);
        Assert.Equal("dispatcher", item.CreatedById);
        Assert.Equal(LineageState.Root, item.LineageState);
        Assert.Null(item.IdempotencyKey);
        Assert.Equal(
            """{"input":{"source":"inbound","since":"2026-10-06T10:00:00Z","limit":100,"offset":0}}""",
            item.Context.ToJsonString());

        var created = await db.Journal.SingleAsync(entry => entry.Kind == JournalKinds.WorkItemCreated, Ct);
        Assert.Equal("inbound sync", created.Reason);

        var row = await db.Campaigns.SingleAsync(c => c.Id == campaign, Ct);
        Assert.Equal(item.Id, row.InboundSyncItemId);
        Assert.Equal(Noon, row.InboundSyncedAt);
        Assert.Equal(Noon.AddHours(-2).AddMilliseconds(750), row.InboundWatermark);
        Assert.Equal(0, row.InboundOffset);
        Assert.Equal(0, row.InboundFailures);
    }

    [Fact]
    [Critical(Family.N1, "one read at a time")]
    public async Task No_second_read_starts_while_one_is_outstanding()
    {
        using var harness = new DispatchHarness(Noon);
        await ReadableAsync(harness, watermark: Noon);
        await harness.SyncAsync(Ct);

        harness.Clock.Advance(TimeSpan.FromDays(2));
        var report = await harness.SyncAsync(Ct);

        Assert.Equal(0, report.Synced);
        await using var db = harness.Open();
        Assert.Equal(1, await db.WorkItems.CountAsync(Ct));
    }

    /// <summary>The cadence runs from the moment the last read started, not from when it ended.</summary>
    [Fact]
    public async Task The_next_read_is_due_a_cadence_after_the_last_one_started()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await ReadableAsync(harness, watermark: Noon);
        await SetAsync(harness, campaign, syncedAt: Noon.AddSeconds(-899));

        Assert.Equal(0, (await harness.SyncAsync(Ct)).Synced);

        harness.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, (await harness.SyncAsync(Ct)).Synced);
    }

    /// <summary>A read that failed is tried again later each time: the cadence doubled for each failure in a row, at most a day.</summary>
    [Theory]
    [InlineData(1, 1_800)]
    [InlineData(2, 3_600)]
    [InlineData(6, 57_600)]
    [InlineData(7, 86_400)]
    // Critical: the backoff cap.
    [InlineData(40, 86_400, Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.N1)])]
    public async Task Each_failure_in_a_row_doubles_the_wait_up_to_a_day(int failures, int wait)
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await ReadableAsync(harness, watermark: Noon);
        await SetAsync(harness, campaign, syncedAt: Noon.AddSeconds(-wait + 1), failures: failures);

        Assert.Equal(0, (await harness.SyncAsync(Ct)).Synced);

        harness.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, (await harness.SyncAsync(Ct)).Synced);
    }

    [Theory]
    [InlineData(CampaignStatus.Draft)]
    [InlineData(CampaignStatus.Paused)]
    [InlineData(CampaignStatus.Archived)]
    public async Task A_campaign_that_is_not_live_is_never_read_nor_moved(CampaignStatus status)
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await ReadableAsync(harness, watermark: Noon.AddHours(-1), status: status);

        Assert.Equal(0, (await harness.SyncAsync(Ct)).Synced);

        await using var db = harness.Open();
        Assert.Empty(await db.WorkItems.ToListAsync(Ct));
        var row = await db.Campaigns.SingleAsync(c => c.Id == campaign, Ct);
        Assert.Null(row.InboundSyncedAt);
        Assert.Equal(Noon.AddHours(-1), row.InboundWatermark);
    }

    [Fact]
    public async Task Switched_off_it_reads_nothing_and_moves_nothing()
    {
        using var harness = new DispatchHarness(Noon);
        harness.Inbound.CurrentValue.Enabled = false;
        var campaign = await ReadableAsync(harness, watermark: Noon);
        var unrouted = await CampaignAsync(harness, watermark: Noon, linked: false);

        Assert.Equal(new InboundReport(0, 0), await harness.SyncAsync(Ct));

        await using var db = harness.Open();
        Assert.Empty(await db.WorkItems.ToListAsync(Ct));
        Assert.All(await db.Campaigns.ToListAsync(Ct), row => Assert.Null(row.InboundSyncedAt));
        Assert.DoesNotContain(harness.Logs.All, line => line.Contains("Inbound", StringComparison.Ordinal));
        _ = (campaign, unrouted);
    }

    /// <summary>
    /// Nothing routes the read: the campaign is skipped, and the skip moves it on as an empty read would — to the
    /// moment of the skip less the look-back, never back. Said once for the campaign and the reason, not once a tick.
    /// </summary>
    [Fact]
    public async Task A_campaign_nothing_routes_is_skipped_moved_on_and_said_once()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness, watermark: Noon.AddHours(-5), linked: false);

        Assert.Equal(0, (await harness.SyncAsync(Ct)).Synced);

        await AssertSkippedAsync(harness, campaign, syncedAt: Noon, watermark: Noon.AddHours(-1));
        Assert.Equal(1, Skips(harness, "no_route"));

        harness.Clock.Advance(TimeSpan.FromHours(1));
        await harness.SyncAsync(Ct);

        await AssertSkippedAsync(harness, campaign, syncedAt: Noon.AddHours(1), watermark: Noon);
        Assert.Equal(1, Skips(harness, "no_route"));
    }

    /// <summary>A skip is still bound by the cadence: a campaign nothing routes is not looked at again every tick.</summary>
    [Fact]
    public async Task A_skipped_campaign_is_looked_at_again_a_cadence_later()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness, watermark: Noon.AddHours(-5), linked: false);
        await harness.SyncAsync(Ct);

        harness.Clock.Advance(TimeSpan.FromSeconds(899));
        await harness.SyncAsync(Ct);
        await AssertSkippedAsync(harness, campaign, syncedAt: Noon, watermark: Noon.AddHours(-1));
    }

    /// <summary>The watermark never moves back, and never before the campaign went live.</summary>
    [Fact]
    public async Task A_skip_soon_after_going_live_leaves_the_watermark_at_the_go_live()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness, watermark: Noon.AddMinutes(-10), linked: false);

        await harness.SyncAsync(Ct);

        await AssertSkippedAsync(harness, campaign, syncedAt: Noon, watermark: Noon.AddMinutes(-10));
    }

    /// <summary>A plugin that performs no conversation reading: one that never claimed the operation, and one that only notifies.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_campaign_routed_to_a_plugin_that_does_not_read_conversations_is_skipped(bool notifier)
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, notifier
            ? TestPlugins.Loaded(Reader, kind: PluginKind.Notification)
            : TestPlugins.Loaded(Reader, operations: ["campaign.get"]));
        RouteAll(harness, Reader);
        var campaign = await CampaignAsync(harness, watermark: Noon.AddHours(-5), linked: true);

        Assert.Equal(0, (await harness.SyncAsync(Ct)).Synced);

        await AssertSkippedAsync(harness, campaign, syncedAt: Noon, watermark: Noon.AddHours(-1));
        Assert.Equal(1, Skips(harness, "plugin_operation_unsupported"));
    }

    /// <summary>
    /// Routed to a plugin that reads, but the campaign was never linked there: a read would have nothing to name the
    /// campaign by. A pin at another plugin is not a link at this one.
    /// </summary>
    [Fact]
    public async Task A_campaign_not_linked_at_the_routed_plugin_is_skipped()
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, TestPlugins.Loaded(Reader, operations: ["conversation.list"]));
        RouteAll(harness, Reader);
        var campaign = await CampaignAsync(harness, watermark: Noon.AddHours(-5), linked: false);
        await PinAsync(harness, campaign, "elsewhere");

        Assert.Equal(0, (await harness.SyncAsync(Ct)).Synced);

        await AssertSkippedAsync(harness, campaign, syncedAt: Noon, watermark: Noon.AddHours(-1));
        Assert.Equal(1, Skips(harness, "campaign_not_linked"));
    }

    /// <summary>The order is fixed: a route is asked about first, then what its plugin performs, then the link.</summary>
    [Fact]
    public async Task An_unrouted_unlinked_campaign_is_skipped_for_its_route()
    {
        using var harness = new DispatchHarness(Noon);
        await CampaignAsync(harness, watermark: Noon, linked: false);

        await harness.SyncAsync(Ct);

        Assert.Equal(1, Skips(harness, "no_route"));
        Assert.Equal(0, Skips(harness, "campaign_not_linked"));
    }

    /// <summary>
    /// A plugin that is routed but not in the loaded set is not skipped: the read is made and fails at its claim,
    /// where the reason is recorded on the item, and a failed read wakes the manager like any other failure.
    /// </summary>
    [Fact]
    public async Task A_campaign_routed_to_a_plugin_that_is_not_loaded_gets_its_read()
    {
        using var harness = new DispatchHarness(Noon);
        RouteAll(harness, Reader);
        await CampaignAsync(harness, watermark: Noon, linked: true);

        Assert.Equal(1, (await harness.SyncAsync(Ct)).Synced);
    }

    /// <summary>
    /// A campaign that gains its route later starts from then, less the look-back, and never before its go-live: what
    /// its conversations held while nothing could read them is not read as new.
    /// </summary>
    [Fact]
    public async Task A_campaign_routed_later_starts_from_its_last_skip_less_the_look_back()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness, watermark: Noon, linked: true);
        harness.Clock.Advance(TimeSpan.FromHours(5));
        await harness.SyncAsync(Ct);
        await AssertSkippedAsync(harness, campaign, syncedAt: Noon.AddHours(5), watermark: Noon.AddHours(4));

        Load(harness, TestPlugins.Loaded(Reader, operations: ["conversation.list"]));
        RouteAll(harness, Reader);
        harness.Clock.Advance(TimeSpan.FromSeconds(900));
        Assert.Equal(1, (await harness.SyncAsync(Ct)).Synced);

        await using var db = harness.Open();
        var item = await db.WorkItems.SingleAsync(Ct);
        Assert.Equal("2026-10-06T16:00:00Z", (string?)item.Context["input"]!["since"]);
    }

    /// <summary>A live campaign with no watermark — none should exist — is levelled like a skip and read from then on.</summary>
    [Fact]
    public async Task A_live_campaign_without_a_watermark_is_levelled_and_not_read_this_time()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await ReadableAsync(harness, watermark: null);

        Assert.Equal(0, (await harness.SyncAsync(Ct)).Synced);

        await AssertSkippedAsync(harness, campaign, syncedAt: Noon, watermark: Noon.AddHours(-1));
    }

    private static int Skips(DispatchHarness harness, string code) =>
        harness.Logs.All.Count(line => line.StartsWith("Inbound sync skipped campaign ", StringComparison.Ordinal)
            && line.EndsWith(": " + code, StringComparison.Ordinal));

    private static async Task AssertSkippedAsync(DispatchHarness harness, int campaign, DateTime syncedAt, DateTime watermark)
    {
        await using var db = harness.Open();
        Assert.Empty(await db.WorkItems.ToListAsync(Ct));
        var row = await db.Campaigns.SingleAsync(c => c.Id == campaign, Ct);
        Assert.Null(row.InboundSyncItemId);
        Assert.Equal(syncedAt, row.InboundSyncedAt);
        Assert.Equal(watermark, row.InboundWatermark);
        Assert.Equal(0, row.InboundFailures);
    }

    /// <summary>A live campaign routed to a loaded plugin that reads conversations, and linked there.</summary>
    private static async Task<int> ReadableAsync(
        DispatchHarness harness,
        DateTime? watermark,
        CampaignStatus status = CampaignStatus.Active)
    {
        Load(harness, TestPlugins.Loaded(Reader, operations: ["conversation.list", "campaign.get"]));
        RouteAll(harness, Reader);
        return await CampaignAsync(harness, watermark, linked: true, status);
    }

    private static async Task<int> CampaignAsync(
        DispatchHarness harness,
        DateTime? watermark,
        bool linked,
        CampaignStatus status = CampaignStatus.Active)
    {
        await using var db = harness.Open();
        var campaign = WorkItemFactory.NewCampaign(status: status, now: Noon.AddDays(-1));
        campaign.InboundWatermark = watermark;
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(Ct);
        if (linked)
        {
            await PinAsync(harness, campaign.Id, Reader);
        }

        return campaign.Id;
    }

    private static async Task PinAsync(DispatchHarness harness, int campaign, string plugin)
    {
        await using var db = harness.Open();
        db.ExternalIds.Add(new ExternalId
        {
            CampaignId = campaign,
            PluginId = plugin,
            Kind = "campaign",
            Value = "seq-" + plugin,
            RecordedAt = Noon.AddDays(-1),
            RecordedByAttemptId = "att_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
        });
        await db.SaveChangesAsync(Ct);
    }

    private static async Task SetAsync(DispatchHarness harness, int campaign, DateTime syncedAt, int failures = 0)
    {
        await using var db = harness.Open();
        await db.Campaigns.Where(c => c.Id == campaign).ExecuteUpdateAsync(
            set => set.SetProperty(c => c.InboundSyncedAt, syncedAt).SetProperty(c => c.InboundFailures, failures),
            Ct);
    }

    private static void Load(DispatchHarness harness, LoadedPlugin plugin) =>
        harness.Resolve<PluginRegistry>().Replace(
            new PluginSnapshot(PublicId.New(PluginProtocol.SnapshotIdPrefix), Noon, SnapshotSource.Startup, [plugin]),
            new ReloadReport(Noon, SnapshotSource.Startup, Activated: true, [], []));

    private static void RouteAll(DispatchHarness harness, string plugin)
    {
        var plugins = harness.Resolve<PluginRegistry>().Snapshot;
        harness.Resolve<RouteRegistry>().Replace(new RouteSnapshot(
            PublicId.New(RouteSnapshot.IdPrefix),
            Noon,
            plugins.Id,
            new RouteSet(new Route(plugin, new JsonObject(), "sha256:binding"), RouteSet.Empty.Operations),
            RouteSnapshot.Empty(Noon, plugins.Id).Campaigns));
    }
}
