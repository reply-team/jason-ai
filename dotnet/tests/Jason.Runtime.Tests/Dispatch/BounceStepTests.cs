using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Plugins;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// The bounce step: a bounce a provider's events said of one of a campaign's people asks for reads of the campaign's own
/// sends that could be the one, at most three, newest first, each where the send ran, and never a second read for a send
/// that already has an open one. It writes no line and no register entry of its own; a bounce of a send Jason did not make
/// asks for nothing and stops nothing. A bounce that names no contact, and a read no route takes, are skips said once.
/// A paused campaign's bounces wait, and so do their reads.
/// </summary>
public class BounceStepTests
{
    private static readonly DateTime Noon = new(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

    private const string Reply = "reply";

    private const string Seat = "sha256:seat";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_bounce_asks_for_reads_of_the_newest_three_sends_where_they_ran_and_writes_nothing_itself()
    {
        using var harness = new DispatchHarness(Noon);
        Routed(harness);
        var (campaign, ada) = await SceneAsync(harness);
        var sends = new List<string>();
        for (var day = 1; day <= 4; day++)
        {
            sends.Add(await SendAsync(harness, campaign, ada, Noon.AddDays(-day)));
        }

        await BounceAsync(harness, campaign, ada);

        Assert.Equal(3, await harness.BounceAsync(Ct));

        await using (var db = harness.Open())
        {
            var reads = await db.WorkItems.Where(item => item.Operation == "message.status").OrderBy(item => item.Id).ToListAsync(Ct);
            Assert.Equal(sends.Take(3), reads.Select(read => (string?)read.Context["input"]!["send"]));
            Assert.All(reads, read =>
            {
                Assert.Equal((WorkItemKind.ProviderOp, ActorType.System, harness.Responder.CurrentValue.Priority - BounceStep.BelowResponders, (int?)null), (read.Kind, read.CreatedByType, read.Priority, read.ContactId));
                Assert.Equal(WorkItemStatus.Created, read.Status);
            });
            Assert.False(await db.Journal.AnyAsync(entry => entry.Kind == JournalKinds.MessageBounceObserved, Ct));
            Assert.False(await db.Suppressions.AnyAsync(Ct));
        }

        // The claim takes a read where its send ran: the plugin and the account of the send's attempt.
        var claimed = Assert.Single(await harness.ClaimAsync(Ct));
        Assert.Equal(("message.status", Reply, Seat), (claimed.Plan!.Contract.Id, claimed.Plan.Plugin.Manifest.Id, claimed.Plan.BindingIdentity));
    }

    /// <summary>
    /// The reads a bounce asks for sit one below the respond step's: a person's reply is read before a mail server's
    /// notice, so a reply's read made after a bounce's reads is claimed first.
    /// </summary>
    [Fact]
    public async Task A_reply_s_read_made_after_a_bounce_s_reads_is_claimed_first()
    {
        using var harness = new DispatchHarness(Noon);
        Routed(harness);
        var (campaign, ada) = await SceneAsync(harness);
        await SendAsync(harness, campaign, ada, Noon.AddDays(-1));
        await SendAsync(harness, campaign, ada, Noon.AddDays(-2));
        await BounceAsync(harness, campaign, ada);
        Assert.Equal(2, await harness.BounceAsync(Ct));
        await RepliedAsync(harness, campaign, "th-ada");
        Assert.Equal(1, await harness.RespondAsync(Ct));

        var claimed = Assert.Single(await harness.ClaimAsync(Ct));

        Assert.Equal(Responder.ReadOperation, claimed.Plan!.Contract.Id);
    }

    /// <summary>
    /// A line is read once, and a second bounce of the same person while the reads it asked for are open asks for none
    /// again: never a second read for a send that already has an open read the runtime made.
    /// </summary>
    [Fact]
    public async Task A_send_with_an_open_read_is_not_read_twice()
    {
        using var harness = new DispatchHarness(Noon);
        Routed(harness);
        var (campaign, ada) = await SceneAsync(harness);
        await SendAsync(harness, campaign, ada, Noon.AddDays(-1));
        await BounceAsync(harness, campaign, ada);
        Assert.Equal(1, await harness.BounceAsync(Ct));

        Assert.Equal(0, await harness.BounceAsync(Ct));
        await BounceAsync(harness, campaign, ada, at: Noon.AddMinutes(5));
        Assert.Equal(0, await harness.BounceAsync(Ct));

        await using var db = harness.Open();
        Assert.Equal(1, await db.WorkItems.CountAsync(item => item.Operation == "message.status", Ct));
        Assert.Equal(await LastLineAsync(db, campaign), (await db.Campaigns.SingleAsync(c => c.Id == campaign.Id, Ct)).BounceWatermark);
    }

    /// <summary>
    /// A bounce of a send the campaign did not make — a provider's own sequence, a mail written by hand, a send older than
    /// the window — asks for nothing and is no skip: the line is passed, nothing is said, and other kinds of provider
    /// event are passed the same way.
    /// </summary>
    [Fact]
    public async Task A_bounce_of_no_send_the_campaign_made_asks_for_nothing_and_is_no_skip()
    {
        using var harness = new DispatchHarness(Noon);
        Routed(harness);
        var (campaign, ada) = await SceneAsync(harness);
        await SendAsync(harness, campaign, ada, Noon.AddDays(-46));
        await BounceAsync(harness, campaign, ada);
        await BounceAsync(harness, campaign, ada, kind: "reply_detected");

        Assert.Equal(0, await harness.BounceAsync(Ct));

        await using var db = harness.Open();
        Assert.False(await db.WorkItems.AnyAsync(item => item.Operation == "message.status", Ct));
        Assert.Equal(await LastLineAsync(db, campaign), (await db.Campaigns.SingleAsync(c => c.Id == campaign.Id, Ct)).BounceWatermark);
        Assert.DoesNotContain(harness.Logs.All, line => line.StartsWith("The bounce step queued no read", StringComparison.Ordinal));
    }

    /// <summary>
    /// A send whose only line says soft stays one a bounce could be of: the person's next bounce asks for a read of it, and
    /// a read that finds it hard then says so once more and stops the address.
    /// </summary>
    [Fact]
    public async Task A_send_said_soft_is_read_again_at_the_person_s_next_bounce()
    {
        using var harness = new DispatchHarness(Noon);
        Routed(harness);
        var (campaign, ada) = await SceneAsync(harness);
        var send = await SendAsync(harness, campaign, ada, Noon.AddDays(-1));
        await SaidAsync(harness, send, ("", "soft"));
        await BounceAsync(harness, campaign, ada);

        Assert.Equal(1, await harness.BounceAsync(Ct));

        await using var db = harness.Open();
        var read = await db.WorkItems.SingleAsync(item => item.Operation == "message.status", Ct);
        Assert.Equal(send, (string?)read.Context["input"]!["send"]);
    }

    /// <summary>
    /// After a line that stops, the first one with a kind that is not soft or the second one, the send is none a bounce
    /// could be of: a later bounce of the person asks for no read of it.
    /// </summary>
    [Theory]
    [InlineData("hard")]
    [InlineData(null)]
    [InlineData("soft,hard")]
    public async Task A_send_whose_bounce_stopped_is_not_read_again(string? said)
    {
        using var harness = new DispatchHarness(Noon);
        Routed(harness);
        var (campaign, ada) = await SceneAsync(harness);
        var send = await SendAsync(harness, campaign, ada, Noon.AddDays(-1));
        await SaidAsync(harness, send, said is null ? [("", null)] : [.. said.Split(',').Select((kind, n) => (n == 0 ? "" : SentFacts.StopKeySuffix, (string?)kind))]);
        await BounceAsync(harness, campaign, ada);

        Assert.Equal(0, await harness.BounceAsync(Ct));

        await using var db = harness.Open();
        Assert.False(await db.WorkItems.AnyAsync(item => item.Operation == "message.status", Ct));
    }

    /// <summary>
    /// A stop a person lifted is never put back by the same send: with the entry gone from the register, a later bounce of
    /// the person asks for no read of that send, and nothing is staged.
    /// </summary>
    [Fact]
    public async Task A_lifted_stop_and_a_later_bounce_ask_no_read_of_that_send_and_stage_nothing()
    {
        using var harness = new DispatchHarness(Noon);
        Routed(harness);
        var (campaign, ada) = await SceneAsync(harness);
        var send = await SendAsync(harness, campaign, ada, Noon.AddDays(-1));
        await SaidAsync(harness, send, ("", "soft"), (SentFacts.StopKeySuffix, "hard"));
        await BounceAsync(harness, campaign, ada, at: Noon.AddMinutes(5));

        Assert.Equal(0, await harness.BounceAsync(Ct));

        await using var db = harness.Open();
        Assert.False(await db.WorkItems.AnyAsync(item => item.Operation == "message.status", Ct));
        Assert.False(await db.Suppressions.AnyAsync(Ct));
        Assert.False(await db.Journal.AnyAsync(entry => entry.Kind == JournalKinds.SuppressionAdded, Ct));
    }

    /// <summary>A bounce that names no contact asks for nothing, and the campaign is said skipped for it once.</summary>
    [Fact]
    public async Task A_bounce_that_names_no_contact_is_said_once()
    {
        using var harness = new DispatchHarness(Noon);
        Routed(harness);
        var (campaign, ada) = await SceneAsync(harness);
        await SendAsync(harness, campaign, ada, Noon.AddDays(-1));
        await BounceAsync(harness, campaign, contact: null);
        await BounceAsync(harness, campaign, contact: null, at: Noon.AddMinutes(5));

        Assert.Equal(0, await harness.BounceAsync(Ct));

        Assert.Equal(1, harness.Logs.All.Count(line => line.EndsWith(": " + BounceSkipCodes.NoContact, StringComparison.Ordinal)));
        await using var db = harness.Open();
        Assert.False(await db.WorkItems.AnyAsync(item => item.Operation == "message.status", Ct));
    }

    /// <summary>
    /// A send no route active now takes a read of where it ran — its plugin gone from the routes — gets no read, which the
    /// claim would refuse, and the campaign is said skipped for it once.
    /// </summary>
    [Fact]
    public async Task A_send_no_route_reads_where_it_ran_gets_no_read_and_is_said_once()
    {
        using var harness = new DispatchHarness(Noon);
        Routed(harness);
        var (campaign, ada) = await SceneAsync(harness);
        await SendAsync(harness, campaign, ada, Noon.AddDays(-1), plugin: "retired");
        await SendAsync(harness, campaign, ada, Noon.AddDays(-2), plugin: "retired");
        await BounceAsync(harness, campaign, ada, plugin: "retired");
        await BounceAsync(harness, campaign, ada, plugin: "retired", at: Noon.AddMinutes(5));

        Assert.Equal(0, await harness.BounceAsync(Ct));

        Assert.Equal(1, harness.Logs.All.Count(line => line.EndsWith(": " + BounceSkipCodes.ReadNotRouted, StringComparison.Ordinal)));
        await using var db = harness.Open();
        Assert.False(await db.WorkItems.AnyAsync(item => item.Operation == "message.status", Ct));
    }

    /// <summary>A paused campaign's bounces wait, and so do their reads: the watermark holds until it is active again.</summary>
    [Fact]
    public async Task A_paused_campaign_s_bounces_wait_until_it_resumes()
    {
        using var harness = new DispatchHarness(Noon);
        Routed(harness);
        var (campaign, ada) = await SceneAsync(harness);
        await SendAsync(harness, campaign, ada, Noon.AddDays(-1));
        await BounceAsync(harness, campaign, ada);
        await SetStatusAsync(harness, campaign, CampaignStatus.Paused);

        Assert.Equal(0, await harness.BounceAsync(Ct));
        await using (var db = harness.Open())
        {
            Assert.Equal(0, (await db.Campaigns.SingleAsync(c => c.Id == campaign.Id, Ct)).BounceWatermark);
        }

        await SetStatusAsync(harness, campaign, CampaignStatus.Active);
        Assert.Equal(1, await harness.BounceAsync(Ct));
    }

    /// <summary>The scan runs the bounce step right after the respond step, and counts the reads it asked for.</summary>
    [Fact]
    public async Task The_scan_asks_for_the_reads_and_counts_them()
    {
        using var harness = new DispatchHarness(Noon);
        Routed(harness);
        var (campaign, ada) = await SceneAsync(harness);
        await SendAsync(harness, campaign, ada, Noon.AddDays(-1));
        await BounceAsync(harness, campaign, ada);

        var report = await harness.Runner.ScanOnceAsync(Ct);

        Assert.Equal(1, report.Bounced);
    }

    /// <summary>The routed plugin performs sends and their reads, at one account, by default.</summary>
    private static void Routed(DispatchHarness harness)
    {
        string[] operations = ["message.send", "message.status", "conversation.list", Responder.ReadOperation];
        var plugins = harness.Resolve<PluginRegistry>();
        plugins.Replace(
            new PluginSnapshot(PublicId.New(PluginProtocol.SnapshotIdPrefix), Noon, SnapshotSource.Startup, [TestPlugins.Loaded(Reply, operations)]),
            new ReloadReport(Noon, SnapshotSource.Startup, Activated: true, [], []));
        var global = new RouteSet(
            new Route(Reply, new JsonObject { ["seat"] = Reply }, Seat),
            new Dictionary<string, Route>(StringComparer.Ordinal),
            new Dictionary<string, IReadOnlyDictionary<string, Route>>(StringComparer.Ordinal));
        harness.Resolve<RouteRegistry>().Replace(new RouteSnapshot(
            PublicId.New(RouteSnapshot.IdPrefix),
            Noon,
            plugins.Snapshot.Id,
            global,
            RouteSnapshot.Empty(Noon, plugins.Snapshot.Id).Campaigns));
    }

    private static async Task<(Campaign Campaign, Contact Ada)> SceneAsync(DispatchHarness harness)
    {
        await using var db = harness.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon.AddDays(-60));
        var ada = new Contact { PublicId = PublicId.New("cnt"), CreatedAt = Noon.AddDays(-60), UpdatedAt = Noon.AddDays(-60) };
        db.Campaigns.Add(campaign);
        db.Contacts.Add(ada);
        await db.SaveChangesAsync(Ct);
        return (campaign, ada);
    }

    /// <summary>An email of the campaign to the contact, finished at <paramref name="finished"/>, with its one attempt.</summary>
    private static async Task<string> SendAsync(DispatchHarness harness, Campaign campaign, Contact contact, DateTime finished, string plugin = Reply)
    {
        await using var db = harness.Open();
        db.Attach(campaign);
        db.Attach(contact);
        var send = WorkItemFactory.NewProviderOp(campaign, "message.send", finished.AddMinutes(-5));
        send.Contact = contact;
        send.Status = WorkItemStatus.Succeeded;
        send.FinishedAt = finished;
        send.Context = new JsonObject { ["input"] = new JsonObject { ["channel"] = "email" } };
        var attempt = WorkItemFactory.NewAttempt(send, 1, AttemptStatus.Succeeded, finished);
        attempt.Provenance = new AttemptProvenanceDto(
            plugin, "1.0.0", "sha256:0", PluginProtocol.CurrentVersion, PluginProtocol.OperationContractVersion, "message.send", 1, null, null,
            RouteScope.GlobalDefault, Seat,
            ExternalIdsReturned: new Dictionary<string, string>(StringComparer.Ordinal) { ["send"] = "email-" + PublicId.New("ref") });
        db.WorkItems.Add(send);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(Ct);
        return send.PublicId;
    }

    /// <summary>A provider event line as the events store writes it, of a bounce of the contact read at the plugin.</summary>
    private static async Task BounceAsync(
        DispatchHarness harness, Campaign campaign, Contact? contact, string plugin = Reply, DateTime? at = null, string kind = BounceStep.EventKind)
    {
        var said = new JsonObject
        {
            ["kind"] = kind,
            ["plugin"] = plugin,
            ["event_id"] = PublicId.New("evt"),
            ["occurred_at"] = (at ?? Noon).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture),
            ["channel"] = null,
            ["contact_id"] = contact?.PublicId,
            ["attributed_by"] = "campaign_pin",
        };
        if (kind == BounceStep.EventKind)
        {
            said["message"] = null;
            said["bounce"] = null;
        }
        else
        {
            said["channel"] = "email";
            said["thread"] = "th-1";
        }

        await using var db = harness.Open();
        db.Attach(campaign);
        new JournalWriter(harness.Clock).Append(
            db, Actors.Dispatcher, JournalKinds.ProviderEventObserved, campaign, key: "sha256:" + PublicId.New("key"), updated: said, contactId: contact?.Id);
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>The bounce lines a read said of the send, in order: each its key's suffix after the send's id and its kind.</summary>
    private static async Task SaidAsync(DispatchHarness harness, string send, params (string Suffix, string? Bounce)[] lines)
    {
        await using var db = harness.Open();
        var item = await db.WorkItems.SingleAsync(candidate => candidate.PublicId == send, Ct);
        foreach (var (suffix, bounce) in lines)
        {
            new JournalWriter(harness.Clock).Append(
                db, Actors.Dispatcher, JournalKinds.MessageBounceObserved, campaign: null, key: send + suffix,
                updated: new JsonObject
                {
                    ["plugin"] = Reply, ["reference"] = "email-1001", ["channel"] = "email", ["message_id"] = null, ["bounce"] = bounce,
                    ["bounce_code"] = bounce, ["bounced_at"] = null, ["read_work_item"] = PublicId.New("wi"),
                },
                workItem: item);
            await db.SaveChangesAsync(Ct);
        }
    }

    /// <summary>A reply the inbound read saw on a thread of the campaign's own, at the routed plugin and account.</summary>
    private static async Task RepliedAsync(DispatchHarness harness, Campaign campaign, string thread)
    {
        await using var db = harness.Open();
        db.Attach(campaign);
        var read = WorkItemFactory.NewProviderOp(campaign, InboundSync.Operation, Noon.AddMinutes(-10));
        read.Status = WorkItemStatus.Succeeded;
        read.FinishedAt = Noon.AddMinutes(-10);
        db.WorkItems.Add(read);
        var attempt = WorkItemFactory.NewAttempt(read, 1, AttemptStatus.Succeeded, Noon.AddMinutes(-10));
        attempt.Provenance = new AttemptProvenanceDto(
            Reply, "1.0.0", "sha256:0", PluginProtocol.CurrentVersion, PluginProtocol.OperationContractVersion,
            InboundSync.Operation, 1, null, null, RouteScope.GlobalDefault, Seat);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(Ct);

        new JournalWriter(harness.Clock).Append(
            db, Actors.Dispatcher, JournalKinds.InboundReplyObserved, campaign,
            key: InboundSync.ObservationKey(Reply, thread, "2026-11-14T11:55:00.000Z"),
            updated: new JsonObject
            {
                ["plugin"] = Reply,
                ["thread"] = thread,
                ["channel"] = "email",
                ["last_activity_at"] = "2026-11-14T11:55:00.000Z",
                ["unread"] = true,
                ["has_meeting_intent"] = null,
                ["contact_id"] = null,
            },
            workItem: read,
            attempt: attempt);
        await db.SaveChangesAsync(Ct);
    }

    private static async Task SetStatusAsync(DispatchHarness harness, Campaign campaign, CampaignStatus status)
    {
        await using var db = harness.Open();
        await db.Campaigns.Where(c => c.Id == campaign.Id).ExecuteUpdateAsync(set => set.SetProperty(c => c.Status, status), Ct);
    }

    private static async Task<int> LastLineAsync(JasonDbContext db, Campaign campaign) =>
        await db.Journal.Where(entry => entry.CampaignId == campaign.Id).MaxAsync(entry => entry.Id, Ct);
}
