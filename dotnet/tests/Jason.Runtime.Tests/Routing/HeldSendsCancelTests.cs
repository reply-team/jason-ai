using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Plugins;
using Jason.Runtime.Contacts;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Dispatch;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Jason.Runtime.Tests.Dispatch.InboundSyncHarvestTests;

namespace Jason.Runtime.Tests.Routing;

/// <summary>
/// What the provider still holds for people whose line stopped is taken back: for each send of theirs in the campaign that
/// the provider holds for a moment still to come, by <see cref="HeldSends"/>' own definition, one <c>message.cancel</c>
/// naming it, as the dispatcher, unless an open or succeeded cancel already names it; nothing for a send the provider has
/// let go, one whose moment passed, or one whose cancel no route takes where it ran. Three acts call it: the hold a reply
/// sets, the end of a line and a removal.
/// </summary>
public partial class HeldSendsCancelTests
{
    private const string Mailer = "holding-mailer";

    private static readonly DateTime Noon = new(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Later => Moment(Noon.AddDays(2));

    /// <summary>
    /// One cancel per send the provider holds for the person in the campaign, each naming its send and nothing else, made by
    /// the dispatcher as root work about no person, one above <c>Responder:Priority</c>; the person's send in another
    /// campaign and another person's send here are left alone.
    /// </summary>
    [Fact]
    public async Task One_cancel_is_queued_for_each_send_the_provider_holds_for_the_person_there()
    {
        using var harness = Routed();
        var (campaign, person) = await SceneAsync(harness);
        var (other, _) = await SceneAsync(harness, contact: person);
        var (_, someoneElse) = await SceneAsync(harness, campaign: campaign);
        var first = await SentAsync(harness, campaign, person, Later);
        var second = await SentAsync(harness, campaign, person, Moment(Noon.AddHours(3)));
        await SentAsync(harness, other, person, Later);
        await SentAsync(harness, campaign, someoneElse, Later);

        Assert.Equal(2, await CancelForAsync(harness, campaign, person));

        await using var db = harness.Open();
        var cancels = await db.WorkItems.AsNoTracking().Where(w => w.Operation == HeldSends.Cancel).OrderBy(w => w.Id).ToListAsync(Ct);
        Assert.Equal([first, second], cancels.Select(w => (string)w.Context["input"]!["send"]!));
        Assert.All(cancels, cancel =>
        {
            Assert.Equal((campaign, (int?)null, WorkItemKind.ProviderOp, WorkItemStatus.Created), (cancel.CampaignId, cancel.ContactId, cancel.Kind, cancel.Status));
            Assert.Equal((ActorType.System, "dispatcher"), (cancel.CreatedByType, cancel.CreatedById));
            Assert.Equal((LineageState.Root, harness.Responder.CurrentValue.Priority + HeldSendCancels.AboveResponders), (cancel.LineageState, cancel.Priority));
            Assert.Equal($$$"""{"input":{"send":"{{{cancel.Context["input"]!["send"]}}}"}}""", cancel.Context.ToJsonString());
        });
    }

    /// <summary>
    /// Never twice: asked again, it queues nothing for a send whose cancel is still open, whoever made it, or succeeded; one
    /// whose cancel failed for any other reason is still held, so it is taken back again.
    /// </summary>
    [Theory]
    [InlineData("asked twice", 0)]
    [InlineData("a person's cancel is still to run", 0)]
    [InlineData("a cancel is under way", 0)]
    [InlineData("a cancel failed for now", 1)]
    public async Task No_send_is_taken_back_twice(string before, int queued)
    {
        using var harness = Routed();
        var (campaign, person) = await SceneAsync(harness);
        var send = await SentAsync(harness, campaign, person, Later);
        switch (before)
        {
            case "asked twice":
                Assert.Equal(1, await CancelForAsync(harness, campaign, person));
                break;
            case "a person's cancel is still to run":
                await CancelAsync(harness, campaign, send, WorkItemStatus.Created);
                break;
            case "a cancel is under way":
                await CancelAsync(harness, campaign, send, WorkItemStatus.Processing);
                break;
            case "a cancel failed for now":
                await CancelAsync(harness, campaign, send, WorkItemStatus.Failed, "provider_unavailable");
                break;
        }

        Assert.Equal(queued, await CancelForAsync(harness, campaign, person));
    }

    /// <summary>
    /// Nothing for a send the provider has let go, in each of the ways <see cref="HeldSends"/> reads it: a cancel succeeded;
    /// a cancel answered that nothing was pending or that there was no such send; a status read found no such send; a status
    /// read says the send is final; the latest status read says it can no longer be cancelled. Nor for a send whose moment
    /// has passed, the provider's own or the arguments', one with no moment, or one whose result does not say the provider
    /// can cancel it.
    /// </summary>
    [Theory]
    [InlineData("its cancel succeeded")]
    [InlineData("its cancel found nothing pending")]
    [InlineData("its cancel found no such send")]
    [InlineData("a status read found no such send")]
    [InlineData("a status read says it went")]
    [InlineData("the latest status read says it can no longer be cancelled")]
    [InlineData("its moment passed")]
    [InlineData("the provider's own moment passed")]
    [InlineData("no moment")]
    [InlineData("the provider cannot cancel it")]
    public async Task Nothing_is_queued_for_a_send_the_provider_no_longer_holds(string after)
    {
        using var harness = Routed();
        var (campaign, person) = await SceneAsync(harness);
        var send = await SentAsync(
            harness,
            campaign,
            person,
            after switch
            {
                "its moment passed" => Moment(Noon.AddMinutes(-5)),
                "no moment" => null,
                _ => Later,
            },
            cancelable: after != "the provider cannot cancel it",
            acceptedFor: after == "the provider's own moment passed" ? Moment(Noon.AddMinutes(-5)) : null);
        switch (after)
        {
            case "its cancel succeeded":
                await CancelAsync(harness, campaign, send, WorkItemStatus.Succeeded);
                break;
            case "its cancel found nothing pending":
                await CancelAsync(harness, campaign, send, WorkItemStatus.Failed, "send_not_pending");
                break;
            case "its cancel found no such send":
                await CancelAsync(harness, campaign, send, WorkItemStatus.Failed, "send_not_found");
                break;
            case "a status read found no such send":
                await CancelAsync(harness, campaign, send, WorkItemStatus.Failed, "send_not_found", HeldSends.Status);
                break;
            case "a status read says it went":
                await StatusReadAsync(harness, campaign, send, "sent", cancelable: false);
                break;
            case "the latest status read says it can no longer be cancelled":
                await StatusReadAsync(harness, campaign, send, "queued", cancelable: false);
                break;
        }

        Assert.Equal(0, await CancelForAsync(harness, campaign, person));
        await using var db = harness.Open();
        Assert.False(await db.WorkItems.AnyAsync(w => w.Operation == HeldSends.Cancel && w.Status == WorkItemStatus.Created, Ct));
    }

    /// <summary>
    /// A cancel no route takes where the send ran is not queued, since the claim would refuse it: the take-back answers
    /// nothing queued, does not fail, and the log says so once for the campaign, by its id and the code, and by nothing the
    /// send says.
    /// </summary>
    [Fact]
    public async Task A_send_whose_cancel_no_route_takes_is_left_and_said_once_in_the_log()
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, TestPlugins.Loaded(Mailer, operations: ["message.send", "message.status"]));
        RouteAll(harness, Mailer);
        var (campaign, person) = await SceneAsync(harness);
        await SentAsync(harness, campaign, person, Later);
        await SentAsync(harness, campaign, person, Moment(Noon.AddHours(5)));

        Assert.Equal(0, await CancelForAsync(harness, campaign, person));
        Assert.Equal(0, await CancelForAsync(harness, campaign, person));

        var said = harness.Logs.All.Where(text => text.Contains(HeldSendCancels.NotRouted, StringComparison.Ordinal)).ToList();
        var line = Assert.Single(said);
        Assert.Contains(await PublicIdOfAsync(harness, campaign), line, StringComparison.Ordinal);
        Assert.DoesNotContain("pilot", line, StringComparison.Ordinal);
    }

    /// <summary>
    /// The first caller: a reply line that holds the person takes back what the provider holds for them in the same
    /// transaction, before the responder it wakes; a line of a person who was held already takes back nothing more.
    /// </summary>
    [Fact]
    public async Task A_reply_that_holds_the_person_takes_back_their_held_send()
    {
        using var harness = Routed();
        var (campaign, person) = await SceneAsync(harness);
        var send = await SentAsync(harness, campaign, person, Later);
        await ReplyLineAsync(harness, campaign, person, "th-1", Noon.AddMinutes(-5));

        Assert.Equal(1, await harness.RespondAsync(Ct));

        await using (var db = harness.Open())
        {
            var cancel = await db.WorkItems.AsNoTracking().SingleAsync(w => w.Operation == HeldSends.Cancel, Ct);
            Assert.Equal(send, (string?)cancel.Context["input"]!["send"]);
            Assert.True(cancel.Id < (await db.WorkItems.AsNoTracking().SingleAsync(w => w.Role == Responder.Role, Ct)).Id);
            Assert.Equal(MembershipState.Paused, await db.CampaignContacts.Where(m => m.CampaignId == campaign && m.ContactId == person).Select(m => m.State).SingleAsync(Ct));
        }

        await SentAsync(harness, campaign, person, Moment(Noon.AddHours(7)));
        await ReplyLineAsync(harness, campaign, person, "th-1", Noon.AddMinutes(-1));
        await harness.RespondAsync(Ct);

        await using var after = harness.Open();
        Assert.Equal(1, await after.WorkItems.CountAsync(w => w.Operation == HeldSends.Cancel, Ct));
    }

    /// <summary>
    /// The other two callers, in their own save: ending a person's line and removing a person each take back what the
    /// provider holds for them in the campaign, and an act that stops nobody takes back nothing.
    /// </summary>
    [Theory]
    [InlineData("finish")]
    [InlineData("remove")]
    public async Task Ending_a_line_and_removing_a_person_take_back_their_held_sends(string act)
    {
        using var harness = Routed();
        var (campaign, person) = await SceneAsync(harness);
        var (_, ended) = await SceneAsync(harness, campaign: campaign, state: MembershipState.Finished);
        var send = await SentAsync(harness, campaign, person, Later);
        await SentAsync(harness, campaign, ended, Later);
        var campaignId = await PublicIdOfAsync(harness, campaign);
        var ids = await PublicIdsAsync(harness, person, ended);

        using (var scope = harness.Scopes.CreateScope())
        {
            var memberships = ActivatorUtilities.CreateInstance<MembershipService>(scope.ServiceProvider);
            var actor = new ActorRef(ActorType.Role, "manager");
            if (act == "finish")
            {
                await memberships.FinishAsync(new FinishContactsRequest(campaignId, [ids[0], ids[1]], actor, "Asked not to be written to."), Ct);
            }
            else
            {
                await memberships.RemoveAsync(new RemoveContactsRequest(campaignId, [ids[0]], actor, null), Ct);
            }
        }

        await using var db = harness.Open();
        var cancel = await db.WorkItems.AsNoTracking().SingleAsync(w => w.Operation == HeldSends.Cancel, Ct);
        Assert.Equal(send, (string?)cancel.Context["input"]!["send"]);
        Assert.Equal((ActorType.System, "dispatcher", (int?)null), (cancel.CreatedByType, cancel.CreatedById, cancel.ContactId));
    }

    /// <summary>
    /// The runtime as it is composed: <c>campaign.finish_contacts</c> and <c>campaign.remove_contacts</c> over the API take
    /// back what the provider holds for the people they stop.
    /// </summary>
    [Fact]
    public async Task The_composed_runtime_takes_back_at_an_end_and_at_a_removal()
    {
        await using var api = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths =>
            {
                File.WriteAllText(paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
                TestPlugins.Write(
                    paths,
                    Mailer,
                    $"manifest_version: 1\nid: {Mailer}\nversion: 1.0.0\nkind: provider\ncontracts:\n  protocol: [1]\n  operations: [1]\noperations: [message.send, message.cancel, message.status]\n",
                    "export function invoke() { return { result: {} }; }");
            },
            clock: new FixedClock(new DateTimeOffset(Noon)),
            configureServices: services => services.AddSingleton(TestPlugins.SearchPath));
        var campaign = (await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "Held letters" }, Ct)).Id;
        var (status, body) = await api.PostAsync(Operations.RouteSet, new { campaign_id = campaign, plugin = Mailer }, Ct);
        Assert.True(status == HttpStatusCode.OK, body);
        var added = await api.PostOkAsync<AddContactsResult>(
            Operations.CampaignAddContacts,
            new { campaign_id = campaign, contacts = new[] { new { first_name = "Ines", channels = new[] { new { channel = "email", value = "ines@example.test" } } }, new { first_name = "Omar", channels = new[] { new { channel = "email", value = "omar@example.test" } } } } },
            Ct);
        var people = added.Items.Select(item => item.ContactId!).ToArray();
        var sends = new List<string>();
        await using (var db = new JasonDbContext(JasonDbContext.CreateOptions(api.Paths.DatabaseFile)))
        {
            var owner = await db.Campaigns.SingleAsync(c => c.PublicId == campaign, Ct);
            foreach (var person in people)
            {
                var row = await db.Contacts.Where(c => c.PublicId == person).Select(c => c.Id).SingleAsync(Ct);
                sends.Add(await SentAsync(db, owner, row, Later, bindingIdentity: BindingIdentity.Of(null)));
            }
        }

        await api.PostOkAsync<FinishContactsResult>(Operations.CampaignFinishContacts, new { campaign_id = campaign, contact_ids = new[] { people[0] }, actor = new { type = "role", id = "manager" }, reason = "Asked not to be written to." }, Ct);
        await api.PostOkAsync<RemoveContactsResult>(Operations.CampaignRemoveContacts, new { campaign_id = campaign, contact_ids = new[] { people[1] } }, Ct);

        await using var read = new JasonDbContext(JasonDbContext.CreateOptions(api.Paths.DatabaseFile));
        Assert.Equal(
            sends,
            (await read.WorkItems.AsNoTracking().Where(w => w.Operation == HeldSends.Cancel).OrderBy(w => w.Id).ToListAsync(Ct)).Select(w => (string)w.Context["input"]!["send"]!));
    }

    /// <summary>A whole dispatcher's composition with a plugin that sends, cancels, reads sends and reads threads, routed by default.</summary>
    private static DispatchHarness Routed()
    {
        var harness = new DispatchHarness(Noon);
        Load(harness, TestPlugins.Loaded(Mailer, operations: ["message.send", "message.cancel", "message.status", "conversation.list", Responder.ReadOperation]));
        RouteAll(harness, Mailer);
        return harness;
    }

    /// <summary>Asks the take-back, in a scope of its own as a caller's would be, for one person of the campaign.</summary>
    private static async Task<int> CancelForAsync(DispatchHarness harness, int campaign, int person)
    {
        using var scope = harness.Scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        var publicId = await db.Campaigns.Where(c => c.Id == campaign).Select(c => c.PublicId).SingleAsync(Ct);
        return await scope.ServiceProvider.GetRequiredService<HeldSendCancels>().CancelForAsync(db, campaign, publicId, [person], harness.Clock.GetUtcNow(), Ct);
    }

    /// <summary>
    /// An active campaign, or the one named, and a person who is its member in <paramref name="state"/>, or the one named:
    /// the campaign's row and the person's.
    /// </summary>
    private static async Task<(int Campaign, int Contact)> SceneAsync(
        DispatchHarness harness, int? campaign = null, int? contact = null, MembershipState state = MembershipState.Enrolled)
    {
        await using var db = harness.Open();
        var owner = campaign is { } id ? await db.Campaigns.SingleAsync(c => c.Id == id, Ct) : WorkItemFactory.NewCampaign(now: Noon.AddDays(-7));
        if (campaign is null)
        {
            db.Campaigns.Add(owner);
        }

        Contact person;
        if (contact is { } row)
        {
            person = await db.Contacts.SingleAsync(c => c.Id == row, Ct);
        }
        else
        {
            person = new Contact { PublicId = PublicId.New("cnt"), FirstName = "Ines", CreatedAt = Noon.AddDays(-7), UpdatedAt = Noon.AddDays(-7) };
            person.Channels.Add(new ContactChannel { Channel = "email", Value = $"{person.PublicId[^6..].ToLowerInvariant()}@example.test", IsPrimary = true });
            db.Contacts.Add(person);
        }

        db.CampaignContacts.Add(new CampaignContact { Campaign = owner, Contact = person, State = state, AddedAt = Noon.AddDays(-7), UpdatedAt = Noon.AddDays(-7) });
        await db.SaveChangesAsync(Ct);
        return (owner.Id, person.Id);
    }

    private static async Task<string> SentAsync(
        DispatchHarness harness, int campaign, int person, string? sendAfter, bool cancelable = true, string? acceptedFor = null, DateTime? wentAt = null)
    {
        await using var db = harness.Open();
        var owner = await db.Campaigns.SingleAsync(c => c.Id == campaign, Ct);
        return await SentAsync(db, owner, person, sendAfter, cancelable, acceptedFor, RoutedIdentity, wentAt);
    }

    /// <summary>
    /// A send of the campaign to the person that the provider took with its reference at <see cref="Mailer"/>, for the
    /// moment <paramref name="sendAfter"/> names or none, saying it can cancel it unless <paramref name="cancelable"/> is
    /// false, and naming its own moment where <paramref name="acceptedFor"/> is given; its attempt ended at
    /// <paramref name="wentAt"/>, where one is given. Answers its work item.
    /// </summary>
    internal static async Task<string> SentAsync(
        JasonDbContext db,
        Campaign owner,
        int person,
        string? sendAfter,
        bool cancelable = true,
        string? acceptedFor = null,
        string? bindingIdentity = RoutedIdentity,
        DateTime? wentAt = null)
    {
        var arguments = new JsonObject
        {
            ["channel"] = "email",
            ["sender"] = new JsonObject { ["external_id"] = "snd-1", ["address"] = "owner@example.test" },
            ["message"] = new JsonObject { ["format"] = "email", ["subject"] = "A quiet word about the pilot", ["body"] = "Ines, the pilot numbers are in." },
        };
        if (sendAfter is not null)
        {
            arguments["send_after"] = sendAfter;
        }

        var reference = "send-" + PublicId.New("ref")[^8..];
        var item = WorkItemFactory.NewProviderOp(owner, "message.send", Noon.AddHours(-1));
        item.Context = new JsonObject { [WorkItemService.InputKey] = arguments };
        item.ContactId = person;
        item.Status = WorkItemStatus.Succeeded;
        item.FinishedAt = Noon.AddHours(-1);
        item.Result = new JsonObject
        {
            ["items"] = new JsonArray(new JsonObject
            {
                ["status"] = "accepted",
                ["reference"] = reference,
                ["accepted_for"] = acceptedFor,
                ["cancelable"] = cancelable,
            }),
        };
        var attempt = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Succeeded, Noon.AddHours(-1));
        attempt.FinishedAt = wentAt;
        attempt.Provenance = new AttemptProvenanceDto(
            Mailer, "1.0.0", "sha256:0", PluginProtocol.CurrentVersion, PluginProtocol.OperationContractVersion,
            "message.send", 1, null, null, RouteScope.CampaignDefault, bindingIdentity,
            ExternalIdsReturned: new Dictionary<string, string>(StringComparer.Ordinal) { ["send"] = reference });
        db.WorkItems.Add(item);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(Ct);
        return item.PublicId;
    }

    /// <summary>
    /// A cancel of <paramref name="send"/>, or another <paramref name="operation"/> naming it, at <paramref name="status"/>,
    /// failed with <paramref name="code"/> where one is given, made by a person.
    /// </summary>
    private static async Task CancelAsync(
        DispatchHarness harness, int campaign, string send, WorkItemStatus status, string? code = null, string operation = HeldSends.Cancel)
    {
        await using var db = harness.Open();
        var owner = await db.Campaigns.SingleAsync(c => c.Id == campaign, Ct);
        var item = WorkItemFactory.NewProviderOp(owner, operation, Noon);
        item.Context = new JsonObject { [WorkItemService.InputKey] = new JsonObject { ["send"] = send } };
        item.Status = status;
        if (status is WorkItemStatus.Succeeded or WorkItemStatus.Failed)
        {
            item.FinishedAt = Noon;
        }

        if (code is not null)
        {
            item.LastError = new AttemptErrorDto(code, "The provider answered.", Retriable: false);
        }

        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>A status read of <paramref name="send"/> that succeeded, saying <paramref name="state"/> and <paramref name="cancelable"/>.</summary>
    private static async Task StatusReadAsync(DispatchHarness harness, int campaign, string send, string state, bool cancelable)
    {
        await using var db = harness.Open();
        var owner = await db.Campaigns.SingleAsync(c => c.Id == campaign, Ct);
        var item = WorkItemFactory.NewProviderOp(owner, HeldSends.Status, Noon);
        item.Context = new JsonObject { [WorkItemService.InputKey] = new JsonObject { ["send"] = send } };
        item.Status = WorkItemStatus.Succeeded;
        item.FinishedAt = Noon;
        item.Result = new JsonObject { ["state"] = state, ["channel"] = "email", ["cancelable"] = cancelable };
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>A reply line in the campaign's own thread, naming the person by its contact column, as the inbound read says one.</summary>
    internal static async Task<string> ReplyLineAsync(DispatchHarness harness, int campaign, int person, string thread, DateTime moved)
    {
        await using var db = harness.Open();
        var owner = await db.Campaigns.SingleAsync(c => c.Id == campaign, Ct);
        var publicId = await db.Contacts.Where(c => c.Id == person).Select(c => c.PublicId).SingleAsync(Ct);
        var moment = InboundSync.Instant(new DateTimeOffset(moved));
        var entry = new JournalWriter(harness.Clock).Append(
            db,
            Actors.Dispatcher,
            JournalKinds.InboundReplyObserved,
            owner,
            key: InboundSync.ObservationKey(Mailer, thread, moment),
            updated: new JsonObject
            {
                ["plugin"] = Mailer,
                ["thread"] = thread,
                ["channel"] = "email",
                ["last_activity_at"] = moment,
                ["unread"] = true,
                ["has_meeting_intent"] = null,
                ["contact_id"] = publicId,
            },
            contactId: person);
        await db.SaveChangesAsync(Ct);
        return entry.PublicId;
    }

    private static async Task<string> PublicIdOfAsync(DispatchHarness harness, int campaign)
    {
        await using var db = harness.Open();
        return await db.Campaigns.Where(c => c.Id == campaign).Select(c => c.PublicId).SingleAsync(Ct);
    }

    private static async Task<string[]> PublicIdsAsync(DispatchHarness harness, params int[] people)
    {
        await using var db = harness.Open();
        var ids = await db.Contacts.Where(c => people.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.PublicId, Ct);
        return [.. people.Select(row => ids[row])];
    }

    internal static string Moment(DateTimeOffset moment) => moment.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    internal static string Moment(DateTime moment) => Moment(new DateTimeOffset(moment));
}
