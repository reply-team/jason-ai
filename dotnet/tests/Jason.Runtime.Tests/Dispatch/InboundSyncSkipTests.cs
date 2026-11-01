using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Plugins;
using Jason.Runtime.Configuration;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Events;
using Jason.Runtime.Tests.Plugins;
using Microsoft.EntityFrameworkCore;
using static Jason.Runtime.Tests.Events.EventsBed;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// The inbound read leaves a campaign to the events only while their source is healthy: its last read succeeded and
/// started within two cadences. Before that source's first read that succeeded, and once it fails or stops being read,
/// the inbound read reads the campaign as before, from its watermark less the look-back — so a reply reaches the
/// manager by one path or the other, and is said once whichever it takes.
/// </summary>
public class InboundSyncSkipTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_campaign_routed_to_a_healthy_events_source_is_left_to_it_and_moved_on()
    {
        using var harness = Create();
        var campaign = await LinkedAsync(harness);
        await SourceAsync(harness, started: Noon.AddSeconds(-60), succeeded: Noon.AddSeconds(-60));

        Assert.Equal(0, (await harness.SyncAsync(Ct)).Synced);

        await using var db = harness.Open();
        Assert.Empty(await db.WorkItems.ToListAsync(Ct));
        var row = await db.Campaigns.SingleAsync(c => c.Id == campaign, Ct);
        Assert.Equal((Noon, Noon.AddHours(-1), 0), (row.InboundSyncedAt, row.InboundWatermark, row.InboundFailures));
        Assert.Equal(1, Skips(harness, "plugin_reads_events"));
    }

    [Fact]
    public async Task Two_cadences_without_a_read_that_succeeded_and_the_campaign_is_read_again()
    {
        using var harness = Create();
        await LinkedAsync(harness);
        await SourceAsync(harness, started: Noon.AddSeconds(-2 * Cadence - 1), succeeded: Noon.AddSeconds(-2 * Cadence - 1));

        Assert.Equal(1, (await harness.SyncAsync(Ct)).Synced);
        Assert.Equal(0, Skips(harness, "plugin_reads_events"));
    }

    [Fact]
    [Critical(Family.N2, "inbound skips only for a healthy events source")]
    public async Task A_campaign_is_never_left_to_a_source_before_its_first_read_that_succeeded()
    {
        using var unread = Create();
        await LinkedAsync(unread);
        Assert.Equal(1, (await unread.SyncAsync(Ct)).Synced);

        using var reading = Create();
        await LinkedAsync(reading);
        await SourceAsync(reading, started: Noon.AddSeconds(-10));
        Assert.Equal(1, (await reading.SyncAsync(Ct)).Synced);
    }

    [Fact]
    public async Task A_source_whose_last_read_failed_hands_the_campaign_back()
    {
        using var harness = Create();
        await LinkedAsync(harness);
        var source = await SourceAsync(harness, started: Noon.AddSeconds(-30), succeeded: Noon.AddSeconds(-660));
        await using (var db = harness.Open())
        {
            await db.EventSources.Where(row => row.Id == source.Id).ExecuteUpdateAsync(set => set.SetProperty(row => row.Failures, 1), Ct);
        }

        Assert.Equal(1, (await harness.SyncAsync(Ct)).Synced);
    }

    [Fact]
    public async Task The_source_is_the_one_the_conversation_route_binds()
    {
        using var harness = Create("b");
        await LinkedAsync(harness);
        await SourceAsync(harness, "a", started: Noon.AddSeconds(-60), succeeded: Noon.AddSeconds(-60));

        Assert.Equal(1, (await harness.SyncAsync(Ct)).Synced);
    }

    /// <summary>
    /// Two campaigns linked to one provider campaign: an event naming it concerns neither, so neither is left to the events —
    /// the inbound read reads both, however healthy the source.
    /// </summary>
    [Fact]
    public async Task Two_campaigns_linked_to_one_provider_campaign_are_both_read_though_the_source_is_healthy()
    {
        using var harness = Create();
        await LinkedAsync(harness);
        await LinkedAsync(harness);
        await SourceAsync(harness, started: Noon.AddSeconds(-60), succeeded: Noon.AddSeconds(-60));

        Assert.Equal(2, (await harness.SyncAsync(Ct)).Synced);
        Assert.Equal(0, Skips(harness, "plugin_reads_events"));
    }

    [Fact]
    public async Task Two_campaigns_linked_to_one_provider_campaign_are_both_read_when_the_source_is_not_healthy()
    {
        using var harness = Create();
        await LinkedAsync(harness);
        await LinkedAsync(harness);

        Assert.Equal(2, (await harness.SyncAsync(Ct)).Synced);
    }

    /// <summary>An archived campaign holding the same provider campaign is told nothing by the events, so it does not count.</summary>
    [Fact]
    public async Task An_archived_campaign_holding_the_same_provider_campaign_does_not_keep_a_live_one_from_the_events()
    {
        using var harness = Create();
        await LinkedAsync(harness);
        await CampaignAsync(harness, linked: true, CampaignStatus.Archived);
        await SourceAsync(harness, started: Noon.AddSeconds(-60), succeeded: Noon.AddSeconds(-60));

        Assert.Equal(0, (await harness.SyncAsync(Ct)).Synced);
        Assert.Equal(1, Skips(harness, "plugin_reads_events"));
    }

    /// <summary>
    /// At the widest cadence and the shipped look-back, a campaign the events had and that is handed back is read from no
    /// later than the source's last read that succeeded: a source is healthy only within the look-back of that read.
    /// </summary>
    [Theory]
    [InlineData(4)]
    [InlineData(20)]
    public async Task The_hand_back_starts_no_later_than_the_last_read_that_succeeded(int succeededHoursAgo)
    {
        using var harness = Create();
        harness.Options.CurrentValue.EventsCadenceSeconds = 86_400;
        harness.Inbound.CurrentValue = new InboundOptions { LookbackSeconds = 14_400 };
        await using (var setup = harness.Open())
        {
            var made = WorkItemFactory.NewCampaign(now: Noon.AddDays(-2));
            made.InboundWatermark = Noon.AddHours(-21);
            setup.Campaigns.Add(made);
            await setup.SaveChangesAsync(Ct);
            setup.ExternalIds.Add(new ExternalId
            {
                CampaignId = made.Id,
                PluginId = Reader,
                Kind = "campaign",
                Value = "p_1",
                RecordedAt = Noon.AddDays(-2),
                RecordedByAttemptId = "att_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
            });
            await setup.SaveChangesAsync(Ct);
        }

        var succeeded = Noon.AddHours(-succeededHoursAgo);
        var source = await SourceAsync(harness, started: succeeded, succeeded: succeeded);
        var first = await harness.SyncAsync(Ct);
        await using (var db = harness.Open())
        {
            await db.EventSources.Where(row => row.Id == source.Id).ExecuteUpdateAsync(set => set.SetProperty(row => row.Failures, 1), Ct);
        }

        harness.Clock.Advance(TimeSpan.FromHours(1));
        var second = await harness.SyncAsync(Ct);

        Assert.Equal(1, first.Synced + second.Synced);
        await using var check = harness.Open();
        var since = DateTime.Parse((string)(await check.WorkItems.AsNoTracking().SingleAsync(Ct)).Context["input"]!["since"]!, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal);
        Assert.True(since <= succeeded, $"The hand-back reads from {since:O}, after the last read that succeeded at {succeeded:O}.");
    }

    [Fact]
    public async Task A_look_back_of_zero_never_leaves_a_campaign_to_the_events()
    {
        using var harness = Create();
        harness.Inbound.CurrentValue = new InboundOptions { LookbackSeconds = 0 };
        await LinkedAsync(harness);
        await SourceAsync(harness, started: Noon, succeeded: Noon);

        Assert.Equal(1, (await harness.SyncAsync(Ct)).Synced);
        Assert.Equal(0, Skips(harness, "plugin_reads_events"));
    }

    /// <summary>The fourth reason comes after the three: a campaign that could not be read anyway is skipped for that.</summary>
    [Fact]
    public async Task A_campaign_never_linked_is_skipped_as_not_linked_though_its_source_is_healthy()
    {
        using var harness = Create();
        await CampaignAsync(harness);
        await SourceAsync(harness, started: Noon.AddSeconds(-60), succeeded: Noon.AddSeconds(-60));

        await harness.SyncAsync(Ct);

        Assert.Equal((1, 0), (Skips(harness, "campaign_not_linked"), Skips(harness, "plugin_reads_events")));
    }

    /// <summary>A plugin no longer reading events here — unusable, or no longer declaring the contract — leaves nothing to the events.</summary>
    [Theory]
    [InlineData(false, PluginStatus.Valid)]
    [InlineData(true, PluginStatus.Unavailable)]
    public async Task A_plugin_that_does_not_read_events_here_leaves_nothing_to_them(bool declares, PluginStatus status)
    {
        using var harness = Create();
        Load(harness, TestPlugins.Loaded(Reader, operations: ["campaign.get", "conversation.list"], status: status, events: declares ? [1] : null));
        Route(harness, Bound(Reader, "a"));
        await LinkedAsync(harness);
        await SourceAsync(harness, started: Noon.AddSeconds(-60), succeeded: Noon.AddSeconds(-60));

        Assert.Equal(1, (await harness.SyncAsync(Ct)).Synced);
    }

    internal static int Skips(DispatchHarness harness, string code) =>
        harness.Logs.All.Count(line => line.StartsWith("Inbound sync skipped campaign ", StringComparison.Ordinal)
            && line.EndsWith(": " + code, StringComparison.Ordinal));

    /// <summary>A live campaign whose watermark is five hours back, linked at the events plugin.</summary>
    internal static async Task<int> LinkedAsync(DispatchHarness harness) => await CampaignAsync(harness, linked: true);

    private static async Task<int> CampaignAsync(DispatchHarness harness, bool linked = false, CampaignStatus status = CampaignStatus.Active)
    {
        await using var db = harness.Open();
        var campaign = WorkItemFactory.NewCampaign(status: status, now: Noon.AddDays(-1));
        campaign.InboundWatermark = Noon.AddHours(-5);
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(Ct);
        if (linked)
        {
            db.ExternalIds.Add(new ExternalId
            {
                CampaignId = campaign.Id,
                PluginId = Reader,
                Kind = "campaign",
                Value = "p_1",
                RecordedAt = Noon.AddDays(-1),
                RecordedByAttemptId = "att_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
            });
            await db.SaveChangesAsync(Ct);
        }

        return campaign.Id;
    }
}
