using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Plugins;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Journal;
using Jason.Runtime.Json;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// What a completed read becomes: one <c>inbound_reply_observed</c> line for each thread that moved, said once per
/// campaign for each move, carrying identifiers, moments and the provider's own flags — and never a subject, a preview
/// or a name, which are a person's words and stay at the provider.
/// </summary>
public class InboundSyncHarvestTests
{
    private static readonly DateTime Noon = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

    private const string Reader = "reader";

    private const string Sentinel = "SENTINEL_INBOUND_7f3c";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_thread_that_moved_is_one_line_naming_the_thread_its_moment_its_flags_and_its_person()
    {
        using var harness = new DispatchHarness(Noon);
        var read = await StartedAsync(harness);
        var ada = await ContactAsync(harness, "Ada", pins: [(Reader, "rc-ada")]);
        var attempt = await SucceedAsync(harness, read, Page(Thread("th-1", Noon.AddMinutes(-30), contact: "rc-ada", unread: true, meeting: false)));

        var report = await harness.SyncAsync(Ct);

        Assert.Equal(1, report.Observed);
        await using var db = harness.Open();
        var line = await db.Journal.SingleAsync(entry => entry.Kind == JournalKinds.InboundReplyObserved, Ct);
        Assert.Equal(ActorType.System, line.ActorType);
        Assert.Equal("dispatcher", line.ActorId);
        Assert.Equal(read.CampaignId, line.CampaignId);
        Assert.Equal(read.PublicId, line.WorkItemId);
        Assert.Equal(attempt, line.AttemptId);
        Assert.Equal((await db.Contacts.SingleAsync(c => c.PublicId == ada, Ct)).Id, line.ContactId);
        Assert.Null(line.Old);
        Assert.Null(line.Reason);
        Assert.Equal(
            CanonicalJson.Hash(new JsonObject { ["plugin"] = Reader, ["thread"] = "th-1", ["moment"] = "2026-10-06T11:30:00.000Z" }),
            line.Key);
        Assert.Equal(
            $$"""{"plugin":"reader","thread":"th-1","channel":"email","last_activity_at":"2026-10-06T11:30:00.000Z","unread":true,"has_meeting_intent":false,"contact_id":"{{ada}}"}""",
            line.New!.ToJsonString());
    }

    /// <summary>
    /// A person's words travel nowhere: not the subject, the preview, the name the provider shows, the provider's own
    /// campaign identifier or its vendor block. Not in the journal, not on the campaign's row, not in the log.
    /// </summary>
    [Fact]
    [Critical(Family.N1, "inbound keeps identifiers only")]
    public async Task The_subject_the_preview_the_name_and_the_vendor_block_are_never_kept()
    {
        using var harness = new DispatchHarness(Noon);
        var read = await StartedAsync(harness);
        await SucceedAsync(harness, read, Page(Thread("th-1", Noon.AddMinutes(-30), contact: "rc-ada", words: Sentinel)));

        Assert.Equal(1, (await harness.SyncAsync(Ct)).Observed);

        await using var db = harness.Open();
        foreach (var entry in await db.Journal.ToListAsync(Ct))
        {
            Assert.DoesNotContain(Sentinel, $"{entry.Key} {entry.Old?.ToJsonString()} {entry.New?.ToJsonString()} {entry.Reason}", StringComparison.Ordinal);
        }

        Assert.DoesNotContain(Sentinel, CampaignRow(harness, read.CampaignId), StringComparison.Ordinal);
        Assert.False(harness.Logs.Mentions(Sentinel));
    }

    [Fact]
    [Critical(Family.N1, "said once")]
    public async Task A_thread_read_again_at_the_same_moment_is_not_said_again()
    {
        using var harness = new DispatchHarness(Noon);
        var first = await StartedAsync(harness);
        await SucceedAsync(harness, first, Page(Thread("th-1", Noon.AddMinutes(-30))));
        await harness.SyncAsync(Ct);

        harness.Clock.Advance(TimeSpan.FromSeconds(900));
        var second = await NextReadAsync(harness);
        await SucceedAsync(harness, second, Page(Thread("th-1", Noon.AddMinutes(-30))));

        Assert.Equal(0, (await harness.SyncAsync(Ct)).Observed);
        Assert.Equal(1, await LinesAsync(harness));
    }

    /// <summary>A thread that moves again is a new moment, and a new line: the other side may have written twice.</summary>
    [Fact]
    public async Task A_thread_that_moved_again_is_said_again()
    {
        using var harness = new DispatchHarness(Noon);
        var first = await StartedAsync(harness);
        await SucceedAsync(harness, first, Page(Thread("th-1", Noon.AddMinutes(-30))));
        await harness.SyncAsync(Ct);

        harness.Clock.Advance(TimeSpan.FromSeconds(900));
        var second = await NextReadAsync(harness);
        await SucceedAsync(harness, second, Page(Thread("th-1", Noon.AddMinutes(10))));

        Assert.Equal(1, (await harness.SyncAsync(Ct)).Observed);
        Assert.Equal(2, await LinesAsync(harness));
    }

    [Fact]
    public async Task A_thread_listed_twice_on_one_page_is_said_once()
    {
        using var harness = new DispatchHarness(Noon);
        var read = await StartedAsync(harness);
        await SucceedAsync(harness, read, Page(Thread("th-1", Noon.AddMinutes(-30)), Thread("th-1", Noon.AddMinutes(-30))));

        Assert.Equal(1, (await harness.SyncAsync(Ct)).Observed);
    }

    /// <summary>
    /// A thread whose last move is before the read's start is history, even if the provider listed it: the first read
    /// of a campaign starts when it went live, and what its conversations held before that is not a reply to it.
    /// </summary>
    [Fact]
    public async Task A_thread_that_last_moved_before_the_read_started_is_not_said()
    {
        using var harness = new DispatchHarness(Noon);
        var read = await StartedAsync(harness, watermark: Noon.AddHours(-1));
        await SucceedAsync(harness, read, Page(Thread("th-old", Noon.AddHours(-1).AddSeconds(-1)), Thread("th-edge", Noon.AddHours(-1))));

        Assert.Equal(1, (await harness.SyncAsync(Ct)).Observed);
        await using var db = harness.Open();
        Assert.Equal("th-edge", (string?)(await db.Journal.SingleAsync(entry => entry.Kind == JournalKinds.InboundReplyObserved, Ct)).New!["thread"]);
    }

    /// <summary>
    /// The person a thread belongs to is the one contact pinned at that plugin with the identifier the provider gave,
    /// and nobody when no contact or more than one is. A value recorded only as a divergence is not a pin.
    /// </summary>
    [Fact]
    public async Task The_person_is_named_only_when_exactly_one_contact_is_pinned_with_that_identifier()
    {
        using var harness = new DispatchHarness(Noon);
        var read = await StartedAsync(harness);
        var ada = await ContactAsync(harness, "Ada", pins: [(Reader, "rc-ada"), ("elsewhere", "rc-nobody")]);
        await ContactAsync(harness, "Twin one", pins: [(Reader, "rc-twin")]);
        await ContactAsync(harness, "Twin two", pins: [(Reader, "rc-twin")]);
        await ContactAsync(harness, "Moved", pins: [(Reader, "rc-first")], diverged: "rc-second");
        await SucceedAsync(harness, read, Page(
            Thread("th-ada", Noon.AddMinutes(-1), contact: "rc-ada"),
            Thread("th-nobody", Noon.AddMinutes(-2), contact: "rc-nobody"),
            Thread("th-twin", Noon.AddMinutes(-3), contact: "rc-twin"),
            Thread("th-moved", Noon.AddMinutes(-4), contact: "rc-second"),
            Thread("th-gone", Noon.AddMinutes(-5), contact: null),
            Thread("th-none", Noon.AddMinutes(-6), noContact: true)));

        Assert.Equal(6, (await harness.SyncAsync(Ct)).Observed);

        await using var db = harness.Open();
        var named = (await db.Journal.Where(entry => entry.Kind == JournalKinds.InboundReplyObserved).ToListAsync(Ct))
            .ToDictionary(entry => (string)entry.New!["thread"]!, entry => (string?)entry.New!["contact_id"]);
        Assert.Equal(ada, named["th-ada"]);
        Assert.Null(named["th-nobody"]);
        Assert.Null(named["th-twin"]);
        Assert.Null(named["th-moved"]);
        Assert.Null(named["th-gone"]);
        Assert.Null(named["th-none"]);
    }

    /// <summary>
    /// The plugin a line names, and the plugin whose pins name the person, is the one the read actually ran at — as its
    /// attempt recorded — not whatever the campaign is routed to by the time the read is accounted for.
    /// </summary>
    [Fact]
    public async Task A_route_changed_after_the_read_ran_does_not_change_what_the_read_found()
    {
        using var harness = new DispatchHarness(Noon);
        var read = await StartedAsync(harness);
        var ada = await ContactAsync(harness, "Ada", pins: [(Reader, "rc-ada"), ("other", "rc-ada-other")]);
        await SucceedAsync(harness, read, Page(Thread("th-1", Noon.AddMinutes(-30), contact: "rc-ada")));

        Load(harness, TestPlugins.Loaded("other", operations: ["conversation.list"]));
        RouteAll(harness, "other");
        await harness.SyncAsync(Ct);

        await using var db = harness.Open();
        var line = await db.Journal.SingleAsync(entry => entry.Kind == JournalKinds.InboundReplyObserved, Ct);
        Assert.Equal(Reader, (string?)line.New!["plugin"]);
        Assert.Equal(ada, (string?)line.New!["contact_id"]);
        Assert.Equal(
            CanonicalJson.Hash(new JsonObject { ["plugin"] = Reader, ["thread"] = "th-1", ["moment"] = "2026-10-06T11:30:00.000Z" }),
            line.Key);
    }

    /// <summary>A read that found nothing writes nothing, and the campaign is accounted for all the same.</summary>
    [Fact]
    public async Task An_empty_read_says_nothing_and_ends_the_read()
    {
        using var harness = new DispatchHarness(Noon);
        var read = await StartedAsync(harness);
        await SucceedAsync(harness, read, Page());

        Assert.Equal(new InboundReport(0, 0), await harness.SyncAsync(Ct));

        await using var db = harness.Open();
        Assert.Null((await db.Campaigns.SingleAsync(c => c.Id == read.CampaignId, Ct)).InboundSyncItemId);
        Assert.Equal(0, await LinesAsync(harness));
    }

    internal static JsonObject Page(params JsonObject[] threads) => Page(hasMore: false, threads);

    internal static JsonObject Page(bool hasMore, params JsonObject[] threads) => new()
    {
        ["items"] = new JsonArray([.. threads]),
        ["has_more"] = hasMore,
        ["vendor"] = new JsonObject { ["ordering"] = "last_activity_desc" },
    };

    /// <summary>One thread as the operation's result carries it, with every field the contract requires.</summary>
    internal static JsonObject Thread(
        string id,
        DateTime moved,
        string? contact = "rc-somebody",
        bool unread = false,
        bool? meeting = null,
        string words = "Re: hello",
        bool noContact = false) => new()
    {
        ["external_id"] = id,
        ["channel"] = "email",
        ["subject"] = words + " subject",
        ["preview"] = words + " preview",
        ["last_activity_at"] = moved.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture),
        ["unread"] = unread,
        ["contact"] = noContact ? null : new JsonObject { ["external_id"] = contact, ["name"] = words + " name" },
        ["campaign_external_id"] = words + " campaign",
        ["has_meeting_intent"] = meeting,
        ["vendor"] = new JsonObject
        {
            ["is_last_message_planned"] = false,
            ["state"] = "ok",
            ["category"] = new JsonObject { ["id"] = "c1", ["name"] = words + " category" },
        },
    };

    /// <summary>The read outstanding on a readable campaign: its item, made by the step as the scan would make it.</summary>
    internal static async Task<WorkItem> StartedAsync(DispatchHarness harness, DateTime? watermark = null)
    {
        Load(harness, TestPlugins.Loaded(Reader, operations: ["conversation.list"]));
        RouteAll(harness, Reader);
        await using (var db = harness.Open())
        {
            var campaign = WorkItemFactory.NewCampaign(now: Noon.AddDays(-1));
            campaign.InboundWatermark = watermark ?? Noon.AddHours(-2);
            db.Campaigns.Add(campaign);
            db.ExternalIds.Add(new ExternalId
            {
                Campaign = campaign,
                PluginId = Reader,
                Kind = "campaign",
                Value = "seq-1",
                RecordedAt = Noon.AddDays(-1),
                RecordedByAttemptId = "att_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
            });
            await db.SaveChangesAsync(Ct);
        }

        Assert.Equal(1, (await harness.SyncAsync(Ct)).Synced);
        return await NewestReadAsync(harness);
    }

    /// <summary>The next read, once the cadence has come round on a campaign whose last read was accounted for.</summary>
    internal static async Task<WorkItem> NextReadAsync(DispatchHarness harness)
    {
        Assert.Equal(1, (await harness.SyncAsync(Ct)).Synced);
        return await NewestReadAsync(harness);
    }

    internal static async Task<WorkItem> NewestReadAsync(DispatchHarness harness)
    {
        await using var db = harness.Open();
        return await db.WorkItems.AsNoTracking().OrderByDescending(item => item.Id).FirstAsync(Ct);
    }

    /// <summary>The read ran at a plugin and succeeded with this page: an attempt whose provenance names the plugin.</summary>
    internal static async Task<string> SucceedAsync(DispatchHarness harness, WorkItem read, JsonObject page, string plugin = Reader)
    {
        await using var db = harness.Open();
        var item = await db.WorkItems.SingleAsync(row => row.Id == read.Id, Ct);
        var attempt = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Succeeded, harness.Clock.GetUtcNow().UtcDateTime);
        attempt.Provenance = new AttemptProvenanceDto(plugin, "1.0.0", "sha256:0", 1, 1, InboundSync.Operation, 1, null, null, null, null);
        item.Status = WorkItemStatus.Succeeded;
        item.Result = page;
        await db.SaveChangesAsync(Ct);
        return attempt.PublicId;
    }

    internal static async Task<string> ContactAsync(
        DispatchHarness harness,
        string name,
        (string Plugin, string Value)[] pins,
        string? diverged = null)
    {
        await using var db = harness.Open();
        var contact = new Contact
        {
            PublicId = PublicId.New("cnt"),
            FirstName = name,
            CreatedAt = Noon.AddDays(-1),
            UpdatedAt = Noon.AddDays(-1),
        };
        db.Contacts.Add(contact);
        foreach (var (plugin, value) in pins)
        {
            db.ExternalIds.Add(new ExternalId
            {
                Contact = contact,
                PluginId = plugin,
                Kind = "contact",
                Value = value,
                DivergedValue = diverged,
                DivergedAt = diverged is null ? null : Noon.AddDays(-1),
                DivergedByAttemptId = diverged is null ? null : "att_01JB6K8TQ2W9V4MZ0C3Y7H5NRE",
                RecordedAt = Noon.AddDays(-1),
                RecordedByAttemptId = "att_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
            });
        }

        await db.SaveChangesAsync(Ct);
        return contact.PublicId;
    }

    internal static async Task<int> LinesAsync(DispatchHarness harness)
    {
        await using var db = harness.Open();
        return await db.Journal.CountAsync(entry => entry.Kind == JournalKinds.InboundReplyObserved, Ct);
    }

    /// <summary>Every column of the campaign's row, as text.</summary>
    internal static string CampaignRow(DispatchHarness harness, int campaign)
    {
        using var db = harness.Open();
        using var connection = new SqliteConnection(db.Database.GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM campaigns WHERE id = {campaign}";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        return string.Join(" ", Enumerable.Range(0, reader.FieldCount).Select(index => reader.IsDBNull(index) ? "" : Convert.ToString(reader.GetValue(index), System.Globalization.CultureInfo.InvariantCulture)));
    }

    internal static void Load(DispatchHarness harness, LoadedPlugin plugin) =>
        harness.Resolve<PluginRegistry>().Replace(
            new PluginSnapshot(PublicId.New(PluginProtocol.SnapshotIdPrefix), Noon, SnapshotSource.Startup, [plugin]),
            new ReloadReport(Noon, SnapshotSource.Startup, Activated: true, [], []));

    internal static void RouteAll(DispatchHarness harness, string plugin)
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
