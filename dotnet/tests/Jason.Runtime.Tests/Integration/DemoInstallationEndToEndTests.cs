using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Events;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Tests.Dispatch;
using Jason.Runtime.Tests.Plugins;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Integration;

/// <summary>
/// The demo installation in small, end to end: a real runtime on a held clock with the demo's own settings — the routes
/// naming the demo provider alone, the responder off, the manager left only the trigger it cannot do without at its
/// slowest cadence, the events read at the shortest cadence there is — the real claim, and the real plugin host running
/// the shipped <c>plugins/demo</c> package for every provider call and every read of its events. A send approved by a
/// person goes to the demo provider, succeeds and pins the person; the story is then started, as the seed starts it,
/// after that success; the person's written reply is read as an event, stored, said to that campaign and shown in the
/// person's thread; and a written reply of somebody the campaign holds but never wrote to is stored and said nowhere.
/// Beside it, what the demo's manager settings do at a campaign's start: no review is queued.
/// </summary>
/// <remarks>
/// The runtime's clock is held at a moment in the past and moved only by the row. The demo provider counts its replies by
/// the machine's own clock, so every reply the story writes after a start set from the runtime's clock has already
/// landed when it is read: the row waits on the runtime's own scans and the children they start, never on a sleep.
/// Every person here is the package's own invented data.
/// </remarks>
[Collection(ProcessEnvironmentCollection.Name)]
public class DemoInstallationEndToEndTests
{
    private const string Demo = "demo";

    /// <summary>The seed's slowest manager cadence, a week: the installation's and each campaign's.</summary>
    private const int ReviewSeconds = 604_800;

    /// <summary>The shortest events cadence a setting may name, as the seed sets it.</summary>
    private const int EventsCadenceSeconds = 60;

    /// <summary>An invented operator: the person who creates the work and approves the send.</summary>
    private static readonly ActorRef Operator = new(ActorType.Human, "demo.operator");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Shipped => Path.Combine(AppContext.BaseDirectory, "Fixtures", "plugins", Demo);

    /// <summary>
    /// What the demo's manager does when a campaign starts under the seed's settings: no check-in is queued by the start or
    /// by the scans after it. The start sets the cadence's anchor, so the first review falls due a week later and not
    /// before, and the one trigger left is a person's answer to a question, which nothing in the story writes.
    /// </summary>
    [Fact]
    public async Task Under_the_demo_s_settings_a_campaign_s_start_queues_no_manager_review_and_the_first_falls_due_a_week_later()
    {
        var people = People().Where(person => person["replies"]!.AsArray().Count > 0).Take(1).ToArray();
        await using var host = await StartAsync();

        var (campaign, _) = await SeededCampaignAsync(host, people);
        var started = host.Clock.Now.UtcDateTime;
        await SettleAsync(host, () => Task.FromResult(true));
        await SettleAsync(host, () => Task.FromResult(true));

        Assert.Empty(await ReviewsAsync(host, campaign));
        var anchor = await ReviewAnchorAsync(host, campaign);
        Assert.Equal(started, anchor);
        Assert.False(ReviewSchedule.IsDue(anchor, ReviewSeconds, ReviewSeconds, started.AddSeconds(ReviewSeconds - 1)));
        Assert.True(ReviewSchedule.IsDue(anchor, ReviewSeconds, ReviewSeconds, started.AddSeconds(ReviewSeconds)));
    }

    [Fact]
    public async Task A_send_approved_in_the_demo_reaches_the_demo_provider_and_the_person_s_written_reply_reaches_that_campaign_alone()
    {
        var story = Story();

        // Two people of one campaign of the story, each with a written reply to a send the story says was approved: the
        // first is written to here, the second is a member who never is.
        var withReplies = People().Where(person => person["replies"]!.AsArray().Count > 0).ToList();
        var written = withReplies[0];
        var unwritten = withReplies.First(person => Text(person["campaign"]) == Text(written["campaign"]) && person != written);
        var send = written["sends"]!.AsArray().Select(entry => entry!.AsObject()).Single(entry => Text(entry["key"]) == Text(written["replies"]![0]!["send"]));
        var sender = story["senders"]!.AsArray().Select(entry => entry!.AsObject()).Single(entry => Text(entry["channel"]) == "email");
        var writtenThread = "demo-thread-" + Text(send["key"]);
        var unwrittenThread = "demo-thread-" + Text(unwritten["replies"]![0]!["send"]);

        await using var host = await StartAsync();
        var (campaign, members) = await SeededCampaignAsync(host, written, unwritten);
        var (contact, bystander) = (members[0], members[1]);

        // 1. A send of the story, created and approved by the operator, is claimed, goes to the demo provider and succeeds.
        var item = (await host.CreateAsync(
            new
            {
                campaign_id = campaign,
                kind = "provider_op",
                operation = "message.send",
                contact_id = contact,
                context = new JsonObject
                {
                    ["input"] = new JsonObject
                    {
                        ["channel"] = "email",
                        ["sender"] = new JsonObject { ["external_id"] = Text(sender["external_id"]), ["address"] = Text(sender["address"]) },
                        ["message"] = new JsonObject
                        {
                            ["compose"] = "text",
                            ["format"] = "email",
                            ["subject"] = Text(send["subject"]),
                            ["body"] = Text(send["body"]),
                        },
                    },
                },
                actor = Operator,
                reason = "the sample story",
            },
            Ct)).Id;
        await SettleAsync(host, async () => (await host.GetAsync(item, Ct)).Status == WorkItemStatus.AwaitingApproval);
        var approval = Assert.Single(await ApprovalsAsync(host), entry => entry.WorkItemId == item);
        var approved = await host.Fixture.PostOkAsync<ApprovalDto>(
            Operations.ApprovalApprove,
            new { approval_id = approval.Id, actor = Operator, reason = "reviewed and fine to send" },
            Ct);
        Assert.Equal((Demo, Operator), (approved.PluginId, approved.DecidedBy));
        await SettleAsync(host, async () => (await host.GetAsync(item, Ct)).Status == WorkItemStatus.Succeeded);

        var sent = await host.GetAsync(item, Ct);
        var attempt = Assert.Single(sent.Attempts!);
        Assert.Equal((AttemptStatus.Succeeded, Demo), (attempt.Status, attempt.Provenance!.PluginId));
        var outcome = sent.Result!["items"]![0]!;
        Assert.Equal("sent", Text(outcome["status"]));
        Assert.Equal("demo-send-", Text(outcome["reference"])[.."demo-send-".Length]);

        //    The provider's identifier for the person is pinned, at the demo provider, by that attempt.
        var pinned = await host.Fixture.PostOkAsync<ContactDto>(Operations.ContactGet, new { contact_id = contact }, Ct);
        var pin = Assert.Single(pinned.ExternalIds, entry => entry.PluginId == Demo && entry.Kind == "contact");
        Assert.Equal((Text(written["contact_id"]), attempt.Id), (pin.Value, pin.RecordedByAttemptId));
        var never = await host.Fixture.PostOkAsync<ContactDto>(Operations.ContactGet, new { contact_id = bystander }, Ct);
        Assert.Empty(never.ExternalIds);

        // Nothing was said about a reply yet: the story has not started, so the provider has written none.
        Assert.Empty(await SeenAsync(host, campaign));
        Assert.Equal(0, await StoredAsync(host));

        // 2. Then the story starts, at a moment at or after that success by the runtime's clock, and the plugins are
        //    reloaded so the provider is handed it.
        var storyStart = host.Clock.Now;
        Assert.True(storyStart >= attempt.FinishedAt, "The story starts at or after the send succeeded.");
        StartTheStory(host.Paths, storyStart);
        var reload = await host.Fixture.PostAsync(Operations.PluginReload, new { actor = Operator, reason = "the story starts" }, Ct);
        Assert.True(reload.Status == HttpStatusCode.OK, $"plugin.reload answered {(int)reload.Status}: {reload.Body}");

        // 3. The account's next read, a cadence later, returns the person's written reply. It is stored, said to that
        //    campaign once as a thread the other side moved, found by the person the send pinned, and the person's thread
        //    shows it.
        host.Clock.Advance(TimeSpan.FromSeconds(EventsCadenceSeconds + 1));
        await SettleAsync(host, async () => (await SeenAsync(host, campaign)).Count > 0);

        var seen = Assert.Single(await SeenAsync(host, campaign));
        Assert.Equal(
            (Demo, writtenThread, "email", "contact_pin", contact),
            ((string)seen["plugin"]!, (string)seen["thread"]!, (string)seen["channel"]!, (string)seen["attributed_by"]!, (string?)seen["contact_id"]));
        var reply = await StoredEventAsync(host, writtenThread);
        Assert.Equal((string)seen["event_id"]!, reply.PublicId);
        Assert.Equal(storyStart.AddSeconds(Offset(written)).UtcDateTime, reply.OccurredAt);
        Assert.Equal([campaign], await AttributedToAsync(host, reply));

        var threads = await host.Fixture.PostOkAsync<ContactThreadsResponse>(Operations.ContactThreads, new { contact_id = contact, plugin_id = Demo }, Ct);
        var listed = Assert.Single(threads.Items)!;
        Assert.Equal((writtenThread, true), (Text(listed["external_id"]), listed["unread"]!.GetValue<bool>()));
        var thread = await host.Fixture.PostOkAsync<ContactThreadResponse>(Operations.ContactThread, new { contact_id = contact, plugin_id = Demo, thread_id = writtenThread }, Ct);
        Assert.Equal(["outbound", "inbound"], thread.Messages.Select(message => Text(message!["direction"])));
        Assert.Equal(Text(written["replies"]![0]!["body"]), Text(thread.Messages[1]!["body"]));

        // 4. The other member's written reply came in the same read. Nobody here ever wrote to them, so no identifier of
        //    theirs is pinned: it is stored, names no contact, and reaches no campaign.
        var stray = await StoredEventAsync(host, unwrittenThread);
        Assert.Null(stray.ContactId);
        Assert.Empty(await AttributedToAsync(host, stray));
        Assert.DoesNotContain(await SeenAsync(host, campaign), line => (string)line["thread"]! == unwrittenThread);
        Assert.Equal(1, await LineCountAsync(host, JournalKinds.InboundReplyObserved));

        // 5, again, at the row's end: the reply woke no manager, since the seed's settings leave it only a person's answer
        //    to a question, and the week's cadence is not due. The listing the check is made on does hold the campaign's work.
        Assert.Empty(await ReviewsAsync(host, campaign));
        var listing = await host.Fixture.PostOkAsync<Page<WorkItemDto>>(Operations.WorkItemList, new { campaign_id = campaign, limit = 200 }, Ct);
        Assert.Contains(listing.Items, entry => entry.Id == item);
    }

    // ------------------------------------------------------------------------------------------------------------
    // The story and the demo installation
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>The story the package holds, read as the desktop reads it.</summary>
    private static JsonObject Story()
    {
        var text = File.ReadAllText(Path.Combine(Shipped, "modules", "story.js"));
        const string Prefix = "export const story = ";
        Assert.StartsWith(Prefix, text, StringComparison.Ordinal);
        return JsonNode.Parse(text[Prefix.Length..text.TrimEnd().TrimEnd(';').Length])!.AsObject();
    }

    private static List<JsonObject> People() => [.. Story()["people"]!.AsArray().Select(person => person!.AsObject())];

    private static string Text(JsonNode? node) => node!.GetValue<string>();

    private static int Offset(JsonObject person) => person["replies"]![0]!["offset_seconds"]!.GetValue<int>();

    /// <summary>
    /// The demo installation's settings, as the seed writes them, over the held loop: the routes name the demo provider
    /// alone, the responder is off, the manager has one trigger and a week's cadence, and events are read every minute.
    /// </summary>
    private static string Settings()
    {
        var settings = JsonNode.Parse(FakeHostRuntime.Settings)!.AsObject();
        settings["Dispatcher"]!["EventsCadenceSeconds"] = EventsCadenceSeconds;
        settings["Responder"] = new JsonObject { ["OnReply"] = false };
        settings["Manager"] = new JsonObject
        {
            ["Triggers"] = new JsonArray(JournalKinds.DecisionAnswered),
            ["ReviewSeconds"] = ReviewSeconds,
        };
        settings["Routes"] = new JsonObject { ["Default"] = new JsonObject { ["Plugin"] = Demo } };
        return settings.ToJsonString();
    }

    private static Task<FakeHostRuntime> StartAsync() =>
        FakeHostRuntime.StartAsync(
            Ct,
            Settings(),
            prepare: paths => TestPlugins.Copy(Shipped, paths.PluginPackageDirectory(Demo)),
            configureServices: services => services.AddSingleton<IPluginHostLocator>(new JasonDllLocator()));

    /// <summary>Tells the provider when the story starts, as the seed does: <c>Plugins:Config:demo:story_start</c>.</summary>
    private static void StartTheStory(JasonPaths paths, DateTimeOffset start)
    {
        var settings = JsonNode.Parse(File.ReadAllText(paths.UserSettingsFile))!.AsObject();
        settings["Plugins"] ??= new JsonObject();
        settings["Plugins"]!["Config"] = new JsonObject
        {
            [Demo] = new JsonObject { ["story_start"] = start.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture) },
        };
        File.WriteAllText(paths.UserSettingsFile, settings.ToJsonString());
    }

    /// <summary>
    /// A campaign as the seed makes one, in the operator's name: created, given the week's cadence, its people added, and
    /// started. Returns the campaign and its people's contacts, in the order given.
    /// </summary>
    private static async Task<(string Campaign, string[] Contacts)> SeededCampaignAsync(FakeHostRuntime host, params JsonObject[] people)
    {
        var campaign = (await host.Fixture.PostOkAsync<CampaignDto>(
            Operations.CampaignCreate,
            new { name = "Regional fleets", actor = Operator, reason = "the sample story" },
            Ct)).Id;
        await host.Fixture.PostOkAsync<CampaignDto>(
            Operations.CampaignUpdate,
            new { campaign_id = campaign, review_seconds = ReviewSeconds, actor = Operator, reason = "the sample story" },
            Ct);
        var contacts = new List<string>();
        foreach (var person in people)
        {
            contacts.Add(await MemberAsync(host, campaign, person));
        }

        await host.Fixture.PostOkAsync<CampaignDto>(
            Operations.CampaignStart,
            new { campaign_id = campaign, actor = Operator, reason = "the sample story" },
            Ct);
        return (campaign, [.. contacts]);
    }

    /// <summary>A person of the story, made a contact by their email and added to the campaign.</summary>
    private static async Task<string> MemberAsync(FakeHostRuntime host, string campaign, JsonObject person)
    {
        var email = Text(person["channels"]!.AsArray().First(channel => Text(channel!["channel"]) == "email")!["value"]);
        var contact = await host.Fixture.PostOkAsync<ContactDto>(
            Operations.ContactCreate,
            new
            {
                first_name = Text(person["first_name"]),
                last_name = Text(person["last_name"]),
                channels = new[] { new { channel = "email", value = email } },
            },
            Ct);
        await host.Fixture.PostOkAsync<AddContactsResult>(
            Operations.CampaignAddContacts,
            new { campaign_id = campaign, contacts = new[] { new { contact_id = contact.Id } }, actor = Operator, reason = "the sample story" },
            Ct);
        return contact.Id;
    }

    // ------------------------------------------------------------------------------------------------------------
    // The runtime's scans and what they wrote
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Scans until the row's condition holds, each scan followed by a wait for what it started: the handler pool's children
    /// and the events reader's read.
    /// </summary>
    private static async Task SettleAsync(FakeHostRuntime host, Func<Task<bool>> done)
    {
        var pool = host.Fixture.Resolve<HandlerPool>();
        var reader = host.Fixture.Resolve<EventReader>();
        for (var scan = 0; scan < 12; scan++)
        {
            await host.ScanAsync(Ct);
            Assert.True(
                await DispatchHarness.EventuallyAsync(() => pool.InFlightAttemptIds.Count == 0 && reader.Idle, Ct, timeoutMilliseconds: 30_000),
                "A child the scan started never finished.");
            if (await done())
            {
                return;
            }
        }

        Assert.Fail("Twelve scans did not bring the row to the state it waits for.");
    }

    private static async Task<IReadOnlyList<ApprovalSummaryDto>> ApprovalsAsync(FakeHostRuntime host) =>
        (await host.Fixture.PostOkAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, new { }, Ct)).Items;

    /// <summary>
    /// The campaign's AI-role work items, in any status — a manager's check-in among them, were one queued: every item of
    /// the campaign is listed and the kind is read from each, so no filter of the request can hide one.
    /// </summary>
    private static async Task<IReadOnlyList<WorkItemDto>> ReviewsAsync(FakeHostRuntime host, string campaign)
    {
        var items = (await host.Fixture.PostOkAsync<Page<WorkItemDto>>(Operations.WorkItemList, new { campaign_id = campaign, limit = 200 }, Ct)).Items;
        return [.. items.Where(item => item.Kind == WorkItemKind.AiRole)];
    }

    /// <summary>The moment the campaign's review cadence is measured from.</summary>
    private static async Task<DateTime?> ReviewAnchorAsync(FakeHostRuntime host, string campaign)
    {
        await using var db = Open(host);
        return await db.Campaigns.AsNoTracking().Where(entry => entry.PublicId == campaign).Select(entry => entry.ManagerReviewAnchor).SingleAsync(Ct);
    }

    /// <summary>The campaign's <c>inbound_reply_observed</c> lines.</summary>
    private static async Task<List<JsonObject>> SeenAsync(FakeHostRuntime host, string campaign)
    {
        await using var db = Open(host);
        var lines = await db.Journal.AsNoTracking()
            .Where(entry => entry.Kind == JournalKinds.InboundReplyObserved && entry.Campaign!.PublicId == campaign)
            .OrderBy(entry => entry.Id)
            .ToListAsync(Ct);
        return [.. lines.Select(line => line.New!.AsObject())];
    }

    private static async Task<int> StoredAsync(FakeHostRuntime host)
    {
        await using var db = Open(host);
        return await db.Events.CountAsync(Ct);
    }

    /// <summary>The one stored event about the thread.</summary>
    private static async Task<ProviderEvent> StoredEventAsync(FakeHostRuntime host, string thread)
    {
        await using var db = Open(host);
        var events = await db.Events.AsNoTracking().Where(stored => stored.PluginId == Demo).ToListAsync(Ct);
        return Assert.Single(events, stored => (string?)stored.Data["thread"] == thread);
    }

    /// <summary>The campaigns an event was found to concern.</summary>
    private static async Task<List<string>> AttributedToAsync(FakeHostRuntime host, ProviderEvent stored)
    {
        await using var db = Open(host);
        return await db.EventAttributions.AsNoTracking()
            .Where(attribution => attribution.EventId == stored.Id)
            .Select(attribution => attribution.Campaign!.PublicId)
            .ToListAsync(Ct);
    }

    private static async Task<int> LineCountAsync(FakeHostRuntime host, string kind)
    {
        await using var db = Open(host);
        return await db.Journal.CountAsync(entry => entry.Kind == kind, Ct);
    }

    private static JasonDbContext Open(FakeHostRuntime host) => new(JasonDbContext.CreateOptions(host.Paths.DatabaseFile));
}
