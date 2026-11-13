using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Plugins;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// The responder's read of a thread goes where the line says the thread was seen: the plugin it names and the account its
/// read ran under, taken from the line itself, when a route active now names them and that plugin performs the read. So a
/// reply seen at a campaign's second plugin is not answered blind, and one seen at an account no route names is read
/// nowhere, as a reply at a plugin no route named was before.
/// </summary>
public class ResponderThreadRouteTests
{
    private static readonly DateTime Noon = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

    private const string Mailer = "mailer";

    private const string Social = "social";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Email through the mailer, LinkedIn messages through the social plugin, and the thread read routed to the mailer: a
    /// LinkedIn reply the social plugin's events said is read at the social plugin, by the channel route that names its
    /// account, and the claim takes the read there too.
    /// </summary>
    [Fact]
    public async Task A_reply_seen_at_the_second_plugin_is_read_there()
    {
        using var harness = new DispatchHarness(Noon);
        Routed(harness);
        var campaign = await CampaignAsync(harness);
        await SeenByEventsAsync(harness, campaign, "th-social", Social, "sha256:" + Social);

        Assert.Equal(1, await harness.RespondAsync(Ct));

        await using (var db = harness.Open())
        {
            var responder = await db.WorkItems.SingleAsync(item => item.Role == Responder.Role, Ct);
            var read = await db.WorkItems.SingleAsync(item => item.Operation == Responder.ReadOperation, Ct);
            Assert.Equal(read.PublicId, (string?)responder.Context["inbound_reply"]!["read"]);
        }

        var claimed = Assert.Single(await harness.ClaimAsync(Ct));
        Assert.Equal((Social, "sha256:" + Social), (claimed.Plan!.Plugin.Manifest.Id, claimed.Plan.BindingIdentity));
    }

    /// <summary>
    /// A reply seen at an account no route names any more — of the second plugin, or of the first beside the one the read is
    /// routed to — is read nowhere: the responder is created ready, with no read, and finds the thread itself.
    /// </summary>
    [Theory]
    [InlineData(Social, "sha256:retired-seat", true)]
    [InlineData(Mailer, "sha256:another-mailbox", false)]
    public async Task A_reply_seen_at_an_account_no_route_names_is_read_nowhere(string plugin, string identity, bool byEvents)
    {
        using var harness = new DispatchHarness(Noon);
        Routed(harness);
        var campaign = await CampaignAsync(harness);
        if (byEvents)
        {
            await SeenByEventsAsync(harness, campaign, "th-gone", plugin, identity);
        }
        else
        {
            await SeenByInboundReadAsync(harness, campaign, "th-gone", plugin, identity);
        }

        Assert.Equal(1, await harness.RespondAsync(Ct));

        await using var db = harness.Open();
        var responder = await db.WorkItems.SingleAsync(item => item.Role == Responder.Role, Ct);
        Assert.Null((string?)responder.Context["inbound_reply"]!["read"]);
        Assert.Equal(WorkItemStatus.Created, responder.Status);
        Assert.False(await db.WorkItems.AnyAsync(item => item.Operation == Responder.ReadOperation, Ct));
    }

    /// <summary>A reply the inbound read saw at the account the read is routed to is read there, as it always was.</summary>
    [Fact]
    public async Task A_reply_seen_at_the_read_s_own_account_is_read_there()
    {
        using var harness = new DispatchHarness(Noon);
        Routed(harness);
        var campaign = await CampaignAsync(harness);
        await SeenByInboundReadAsync(harness, campaign, "th-mail", Mailer, "sha256:" + Mailer);

        Assert.Equal(1, await harness.RespondAsync(Ct));

        var claimed = Assert.Single(await harness.ClaimAsync(Ct));
        Assert.Equal((Responder.ReadOperation, Mailer, "sha256:" + Mailer), (claimed.Plan!.Contract.Id, claimed.Plan.Plugin.Manifest.Id, claimed.Plan.BindingIdentity));
    }

    /// <summary>
    /// The two plugins, each reading threads, and the routes: by default everything through the mailer; LinkedIn messages
    /// through the social plugin, by a channel route.
    /// </summary>
    private static void Routed(DispatchHarness harness)
    {
        string[] operations = ["conversation.list", Responder.ReadOperation, "message.send"];
        var plugins = harness.Resolve<PluginRegistry>();
        plugins.Replace(
            new PluginSnapshot(PublicId.New(PluginProtocol.SnapshotIdPrefix), Noon, SnapshotSource.Startup, [TestPlugins.Loaded(Mailer, operations), TestPlugins.Loaded(Social, operations)]),
            new ReloadReport(Noon, SnapshotSource.Startup, Activated: true, [], []));
        var global = new RouteSet(
            new Route(Mailer, new JsonObject { ["seat"] = Mailer }, "sha256:" + Mailer),
            new Dictionary<string, Route>(StringComparer.Ordinal),
            new Dictionary<string, IReadOnlyDictionary<string, Route>>(StringComparer.Ordinal)
            {
                ["message.send"] = new Dictionary<string, Route>(StringComparer.Ordinal)
                {
                    ["linkedin"] = new(Social, new JsonObject { ["seat"] = Social }, "sha256:" + Social),
                },
            });
        harness.Resolve<RouteRegistry>().Replace(new RouteSnapshot(
            PublicId.New(RouteSnapshot.IdPrefix),
            Noon,
            plugins.Snapshot.Id,
            global,
            RouteSnapshot.Empty(Noon, plugins.Snapshot.Id).Campaigns));
    }

    private static async Task<Campaign> CampaignAsync(DispatchHarness harness)
    {
        await using var db = harness.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon.AddDays(-1));
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(Ct);
        return campaign;
    }

    /// <summary>A line as the inbound read writes it: under its read's work item and the attempt that recorded the account.</summary>
    private static async Task SeenByInboundReadAsync(DispatchHarness harness, Campaign campaign, string thread, string plugin, string identity)
    {
        await using var db = harness.Open();
        db.Attach(campaign);
        var read = WorkItemFactory.NewProviderOp(campaign, InboundSync.Operation, Noon.AddMinutes(-10));
        read.Status = WorkItemStatus.Succeeded;
        read.FinishedAt = Noon.AddMinutes(-10);
        db.WorkItems.Add(read);
        var attempt = WorkItemFactory.NewAttempt(read, 1, AttemptStatus.Succeeded, Noon.AddMinutes(-10));
        attempt.Provenance = new AttemptProvenanceDto(
            plugin, "1.0.0", "sha256:0", PluginProtocol.CurrentVersion, PluginProtocol.OperationContractVersion,
            InboundSync.Operation, 1, null, null, RouteScope.GlobalDefault, identity);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(Ct);

        new JournalWriter(harness.Clock).Append(
            db, Actors.Dispatcher, JournalKinds.InboundReplyObserved, campaign,
            key: InboundSync.ObservationKey(plugin, thread, "2026-10-06T11:55:00.000Z"),
            updated: Said(plugin, thread, "email"),
            workItem: read,
            attempt: attempt);
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>
    /// A line as the events store writes it, found by the campaign's own pin: naming the stored event, read from the source of
    /// the plugin and the account.
    /// </summary>
    private static async Task SeenByEventsAsync(DispatchHarness harness, Campaign campaign, string thread, string plugin, string identity)
    {
        await using var db = harness.Open();
        db.Attach(campaign);
        var source = new EventSource { PublicId = PublicId.New("evs"), PluginId = plugin, BindingIdentity = identity, CreatedAt = Noon };
        db.EventSources.Add(source);
        await db.SaveChangesAsync(Ct);
        var stored = new ProviderEvent
        {
            PublicId = PublicId.New("evt"),
            SourceId = source.Id,
            PluginId = plugin,
            Kind = "thread_moved",
            OccurredAt = Noon.AddMinutes(-5),
            StoredAt = Noon,
            Key = "sha256:" + PublicId.New("key"),
            Data = new JsonObject { ["thread"] = thread },
        };
        db.Events.Add(stored);
        await db.SaveChangesAsync(Ct);

        var said = Said(plugin, thread, "linkedin");
        said["event_id"] = stored.PublicId;
        said["attributed_by"] = "campaign_pin";
        new JournalWriter(harness.Clock).Append(
            db, Actors.Dispatcher, JournalKinds.InboundReplyObserved, campaign,
            key: InboundSync.ObservationKey(plugin, thread, "2026-10-06T11:55:00.000Z"),
            updated: said);
        await db.SaveChangesAsync(Ct);
    }

    private static JsonObject Said(string plugin, string thread, string channel) => new()
    {
        ["plugin"] = plugin,
        ["thread"] = thread,
        ["channel"] = channel,
        ["last_activity_at"] = "2026-10-06T11:55:00.000Z",
        ["unread"] = true,
        ["has_meeting_intent"] = null,
        ["contact_id"] = null,
    };
}
