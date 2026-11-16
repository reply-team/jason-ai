using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Plugins;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Contacts;
using Jason.Runtime.Execution;
using Microsoft.Extensions.Logging.Abstractions;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Contacts;
using Jason.Runtime.Tests.Dispatch;
using Jason.Runtime.Tests.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Api;

/// <summary>
/// The exchanges the desktop chat's cards read, each recorded as one coherent story: a campaign, its people, the work a
/// person is asked about and what the person does with it. Every state is reached through the API's own operations and
/// the runtime's own claim (the scan that parks a send for a person), so the ids in a list row are the ids its
/// <c>get</c> answers and the item a patch answers is the item the list held. Nothing here writes a row by hand.
/// </summary>
public class CardExchangesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Nine = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);
    private static readonly object Ada = new { type = "human", id = "ada" };

    // The claim plans a role's launch before it hands the work over, so a role needs the entry command a person's settings
    // give it; nothing is started here, the role is a command of the test's own, and the dispatcher stays off.
    private const string RoleSettings = """{"Dispatcher":{"Enabled":false},"Roles":{"DefaultEntryCommand":["agent-host"]}}""";
    private const string EditReason = "Message text edited on the approval card.";

    /// <summary>
    /// A text message waiting on a person, as the chat's card reads it: the campaign's pending approvals, the approval
    /// whole and the item behind it; then the text edited on the card, the approval the edit withdrew, and the one the
    /// runtime asks again once its claim takes the work.
    /// </summary>
    [Fact]
    public async Task A_message_waiting_is_read_then_edited_and_asked_again()
    {
        var reads = new FakeProviderReads();
        await using var api = await RuntimeApiFixture.StartAsync(
            Ct,
            clock: new FixedClock(Nine),
            configureServices: services => services.AddSingleton<IProviderReadInvoker>(reads));
        var scene = await Scene.OpenAsync(api, "Spring launch");
        var ines = await scene.PersonAsync("Ines", "Duarte", "email", "ines@example.test");
        var item = await scene.SendAsync(ines, "message.send", EmailArgs("Hi Ines, thanks for the talk at the meetup."));
        await scene.ClaimAsync();

        // (a) What the card lists, reads whole, and checks against the item before Approve is offered.
        var pending = await api.RecordAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, "card-message-pending", new { status = "pending", campaign_id = scene.Campaign, limit = 50 }, Ct);
        var row = Assert.Single(pending.Items);
        Assert.Equal((item, "message.send", "Ines Duarte"), (row.WorkItemId, row.Operation, row.ContactName));
        var asked = await api.RecordAsync<ApprovalDto>(Operations.ApprovalGet, "card-message", new { approval_id = row.Id }, Ct);
        Assert.Equal((row.Id, item, ApprovalStatus.Pending), (asked.Id, asked.WorkItemId, asked.Status));
        var parked = await api.RecordAsync<WorkItemDto>(Operations.WorkItemGet, "card-message-parked", new { work_item_id = row.WorkItemId }, Ct);
        Assert.Equal((item, WorkItemStatus.AwaitingApproval), (parked.Id, parked.Status));
        Assert.True(JsonNode.DeepEquals(parked.Context["input"], asked.Subject["input"]!["args"]), "the item holds the arguments its approval asked about");

        // The line under the message: the accounts the campaign's route lists for a send, read as the card reads them (the
        // whole team's, the person's own among them), and the same read once the account the send names is gone. The
        // account is the one the parked send names, so the card finds it by its identifier.
        reads.Answer(
            FakeProviderReads.Result(Senders(listed: true)),
            FakeProviderReads.Result(Senders(listed: false)));
        var senderAsked = new { campaign_id = scene.Campaign, owners = "team", sender_channel = "email", limit = 100 };
        var sender = (string?)asked.Subject["input"]!["args"]!["sender"]!["external_id"];
        var accounts = await api.RecordAsync<SenderListResponse>(Operations.SenderList, "card-message-sender", senderAsked, Ct);
        var named = Assert.Single(accounts.Items, a => (string?)a!["external_id"] == sender);
        Assert.Equal(("Owner Example", "own"), ((string?)named!["name"], (string?)named["owner"]));
        Assert.Contains(accounts.Items, a => (string?)a!["owner"] == "teammate");
        var gone = await api.RecordAsync<SenderListResponse>(Operations.SenderList, "card-sender-other", senderAsked, Ct);
        Assert.DoesNotContain(gone.Items, a => (string?)a!["external_id"] == sender);

        // (b) The edit: the item's arguments as they were, with the text replaced, patched with the card's own reason.
        var edited = EmailArgs("Hi Ines, thanks for the talk at the meetup. Shall we talk next week?");
        var patched = await api.RecordAsync<WorkItemDto>(Operations.WorkItemUpdate, "card-message-edited", new { work_item_id = item, set = new { input = edited }, reason = EditReason, actor = Ada }, Ct);
        Assert.Equal((item, WorkItemStatus.Created), (patched.Id, patched.Status));
        Assert.True(JsonNode.DeepEquals(edited, patched.Context["input"]));

        // The approval the edit withdrew, read whole by a card that still holds its id.
        var withdrawn = await api.RecordAsync<ApprovalDto>(Operations.ApprovalGet, "card-message-withdrawn", new { approval_id = row.Id }, Ct);
        Assert.Equal((row.Id, item), (withdrawn.Id, withdrawn.WorkItemId));
        Assert.NotEqual(ApprovalStatus.Pending, withdrawn.Status);

        // The old approval is gone from what waits, and the item is back in the queue for the claim to decide about again.
        Assert.Empty((await api.RecordAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, "card-message-after-edit", new { status = "pending", campaign_id = scene.Campaign, limit = 50 }, Ct)).Items);
        await scene.ClaimAsync();
        var reasked = Assert.Single((await api.RecordAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, "card-message-reasked", new { status = "pending", campaign_id = scene.Campaign, limit = 50 }, Ct)).Items);
        Assert.Equal(item, reasked.WorkItemId);
        Assert.NotEqual(row.Id, reasked.Id);
        var again = await api.RecordAsync<ApprovalDto>(Operations.ApprovalGet, "card-message-reasked", new { approval_id = reasked.Id }, Ct);
        Assert.True(JsonNode.DeepEquals(edited, again.Subject["input"]!["args"]), "the re-asked approval holds the edited text");
    }

    /// <summary>A LinkedIn invitation waiting on a person: the list row, the approval whole and the item behind it.</summary>
    [Fact]
    public async Task An_invitation_waiting_is_read_whole()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));
        var scene = await Scene.OpenAsync(api, "Spring launch");
        var noor = await scene.PersonAsync("Noor", "Haddad", "linkedin", "https://www.linkedin.com/in/noor-example");
        var item = await scene.SendAsync(noor, "social_invitation.send", new JsonObject
        {
            ["channel"] = "linkedin",
            ["sender"] = new JsonObject { ["external_id"] = "s_34", ["address"] = "https://www.linkedin.com/in/owner-example" },
            ["note"] = "Noor, I enjoyed your talk on hiring; would be good to be connected.",
        });
        await scene.ClaimAsync();

        var row = Assert.Single((await api.RecordAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, "card-invitation-pending", new { status = "pending", campaign_id = scene.Campaign, limit = 50 }, Ct)).Items);
        Assert.Equal((item, "social_invitation.send", "Noor Haddad"), (row.WorkItemId, row.Operation, row.ContactName));
        var asked = await api.RecordAsync<ApprovalDto>(Operations.ApprovalGet, "card-invitation", new { approval_id = row.Id }, Ct);
        Assert.Equal((row.Id, item, ApprovalStatus.Pending), (asked.Id, asked.WorkItemId, asked.Status));
        var parked = await api.RecordAsync<WorkItemDto>(Operations.WorkItemGet, "card-invitation-parked", new { work_item_id = row.WorkItemId }, Ct);
        Assert.Equal((item, WorkItemStatus.AwaitingApproval), (parked.Id, parked.Status));
        Assert.True(JsonNode.DeepEquals(parked.Context["input"], asked.Subject["input"]!["args"]), "the item holds the arguments its approval asked about");
    }

    /// <summary>
    /// Three questions a role left for a person, as the chat's cards read them: the campaign's pending questions, each
    /// question whole, and one answered in the person's own words. The role is a real claimed attempt, running, and the
    /// questions are raised through the verb it uses.
    /// </summary>
    [Fact]
    public async Task Questions_waiting_are_read_whole_and_one_is_answered_in_free_text()
    {
        var role = new HoldingRole();
        await using var api = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths => File.WriteAllText(paths.UserSettingsFile, RoleSettings),
            clock: new FixedClock(Nine),
            configureServices: services => services.AddSingleton<ICommand>(role));
        var scene = await Scene.OpenAsync(api, "Spring launch");
        var ines = await scene.PersonAsync("Ines", "Duarte", "email", "ines@example.test");
        var work = await scene.RunRoleAsync(role, ines);
        try
        {
            async Task<DecisionDto> RaiseAsync(string question, object[]? options = null, object[]? references = null)
            {
                return await api.PostOkAsync<DecisionDto>(
                    Operations.DecisionRaise,
                    new { work_item_id = work.WorkItemPublicId, attempt_id = work.AttemptPublicId, question, options, references },
                    Ct);
            }

            var free = await RaiseAsync("What should the follow-up to Ines say about the pricing change?");
            var pause = await RaiseAsync(
                "Pause outreach to Kavali until their funding news?",
                [new { label = "Pause until the news", detail = "Their CFO is leaving; the round is in the press next week." }, new { label = "Keep going", detail = (string?)null }]);
            var move = await RaiseAsync(
                "Move Ines to the autumn campaign?",
                [new { label = "Move her", detail = "Her role changed in September." }, new { label = "Leave her here", detail = (string?)null }],
                [new { kind = "work_item", id = work.WorkItemPublicId }]);

            // What the card lists, in the order the runtime answers, then each question of it read whole.
            var pending = await api.RecordAsync<Page<DecisionSummaryDto>>(Operations.DecisionList, "card-questions-pending", new { status = "pending", campaign_id = scene.Campaign, limit = 50 }, Ct);
            Assert.Equal([free.Id, pause.Id, move.Id], pending.Items.Select(d => d.Id));
            Assert.All(pending.Items, d => Assert.Equal((work.WorkItemPublicId, "Ines Duarte"), (d.WorkItemId, d.ContactName)));
            var second = await api.RecordAsync<DecisionDto>(Operations.DecisionGet, "card-question-2", new { decision_id = pending.Items[1].Id }, Ct);
            var third = await api.RecordAsync<DecisionDto>(Operations.DecisionGet, "card-question-3", new { decision_id = pending.Items[2].Id }, Ct);
            Assert.Equal(pause.Question, second.Question);
            Assert.Equal(["Pause until the news", "Keep going"], second.Options!.Select(o => o.Label));
            Assert.Equal((move.Question, work.WorkItemPublicId), (third.Question, Assert.Single(third.References!).Id));

            // (c) The first question offers no options: the answer is the person's text, with no option chosen and no reason.
            var answered = await api.RecordAsync<DecisionDto>(
                Operations.DecisionAnswer,
                "card-free-text",
                new { decision_id = pending.Items[0].Id, answer = "Say the new price starts in January and the current one is held for them until then.", actor = Ada },
                Ct);
            Assert.Equal((free.Id, DecisionStatus.Answered, null), (answered.Id, answered.Status, answered.ChosenOption));
            Assert.Equal([pause.Id, move.Id], (await api.RecordAsync<Page<DecisionSummaryDto>>(Operations.DecisionList, "card-questions-after-answer", new { status = "pending", campaign_id = scene.Campaign, limit = 50 }, Ct)).Items.Select(d => d.Id));
        }
        finally
        {
            role.Release();
            await work.Ended;
        }
    }

    /// <summary>
    /// More messages waiting than a page holds: a person has fifty-one waiting, the first page ends in a cursor, and the
    /// cursor opens the page with the one left.
    /// </summary>
    [Fact]
    public async Task Messages_waiting_beyond_a_page_are_read_by_their_cursor()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));
        api.ScrubCursors = true;
        var scene = await Scene.OpenAsync(api, "Spring launch");
        var names = new List<string>();
        for (var n = 1; n <= 51; n++)
        {
            var last = "Person" + n.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
            names.Add("Pat " + last);
            var person = await scene.PersonAsync("Pat", last, "email", $"pat{n}@example.test");
            await scene.SendAsync(person, "message.send", EmailArgs($"Hi Pat, a note for {last}."));
            await scene.ClaimAsync();
        }

        var first = await api.RecordAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, "card-messages-first-page", new { status = "pending", campaign_id = scene.Campaign, limit = 50 }, Ct);
        Assert.Equal(50, first.Items.Count);
        Assert.NotNull(first.NextCursor);
        var next = await api.RecordAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, "card-messages-next-page", new { status = "pending", campaign_id = scene.Campaign, limit = 50, cursor = first.NextCursor }, Ct);
        Assert.Null(next.NextCursor);
        // The ids are monotonic by construction (Jason.Contracts/Ids/Ulid.cs), so a listing in id order is the order made.
        Assert.Equal(names, [.. first.Items.Concat(next.Items).Select(a => a.ContactName!)]);
        Assert.Equal(51, first.Items.Concat(next.Items).Select(a => a.Id).Distinct().Count());
    }

    /// <summary>
    /// What the chat's data cards read, in one installation: a campaign's status and people with one message and one
    /// question waiting on a person, the same waiting things read for every campaign, the accounts and their routes, the
    /// plan of a draft, and the campaigns a chip is chosen from. Everything is made through the API and the runtime's own
    /// claim; a send that goes through is answered by a stand-in where the provider's process would run.
    /// </summary>
    [Fact]
    public async Task Campaigns_are_read_by_their_status_people_and_what_waits_on_them()
    {
        var role = new HoldingRole();
        var clock = new FixedClock(Nine);
        await using var api = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths => File.WriteAllText(paths.UserSettingsFile, RoleSettings),
            clock: clock,
            configureServices: services =>
            {
                services.AddSingleton<ICommand>(role);
                services.AddSingleton<ICommand>(Provider());
            });
        api.ScrubCursors = true;
        var spring = await Scene.OpenAsync(api, "Spring launch");
        var autumn = await spring.AnotherAsync("Autumn outreach");
        var summer = await spring.AnotherAsync("Summer reach");
        var winter = await spring.AnotherAsync("Winter plan");
        var push = await spring.AnotherAsync("Autumn push");

        string[] names = ["Ines Duarte", "Omar Reyes", "Rafa Costa", "Tomas Berg", "Alma Novak", "Bruno Silva", "Chen Wu", "Dara Okafor", "Elio Marin", "Fara Nasser", "Gita Rao", "Hugo Lang"];
        var people = new List<string>();
        foreach (var name in names)
        {
            var (first, last) = (name.Split(' ')[0], name.Split(' ')[1]);
            people.Add(await spring.PersonAsync(first, last, "email", $"{first.ToLowerInvariant()}@example.test"));
        }

        var (ines, omar, rafa, tomas) = (people[0], people[1], people[2], people[3]);
        var kai = await autumn.PersonAsync("Kai", "Lindt", "email", "kai@example.test");
        var lena = await autumn.PersonAsync("Lena", "Pohl", "email", "lena@example.test");
        await winter.PersonAsync("Mira", "Sato", "email", "mira@example.test");
        await winter.PersonAsync("Nils", "Aho", "email", "nils@example.test");
        var piet = await push.PersonAsync("Piet", "Vos", "email", "piet@example.test");

        // The campaign is made at the provider, so it is linked there; Omar's message is approved and goes; the provider then
        // lists the thread Omar replied on, and the runtime's own read of it says so. Ines's waits on a person; a role asks
        // about Tomas; Autumn has one of each.
        await spring.StartAsync();
        var made = await api.PostOkAsync<WorkItemDto>(Operations.WorkItemCreate, new { campaign_id = spring.Campaign, kind = "provider_op", operation = "campaign.create", context = new { input = new { name = "Spring launch" } }, actor = Ada }, Ct);
        Assert.Equal(made.Id, Assert.Single(await spring.ClaimAndRunAsync()).WorkItemPublicId);
        var sentItem = await spring.SendAsync(omar, "message.send", EmailArgs("Hi Omar, a note after the meetup."));
        await spring.ClaimAsync();
        var toApprove = Assert.Single((await api.PostOkAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, new { status = "pending", campaign_id = spring.Campaign, limit = 50 }, Ct)).Items);
        await api.PostOkAsync<ApprovalDto>(Operations.ApprovalApprove, new { approval_id = toApprove.Id, actor = Ada }, Ct);
        Assert.Equal(sentItem, Assert.Single(await spring.ClaimAndRunAsync()).WorkItemPublicId);
        Assert.Equal(WorkItemStatus.Succeeded, (await api.PostOkAsync<WorkItemDto>(Operations.WorkItemGet, new { work_item_id = sentItem }, Ct)).Status);
        Assert.Equal(1, (await InboundScanAsync(api)).Synced);
        Assert.Single(await spring.ClaimAndRunAsync());
        Assert.Equal(1, (await InboundScanAsync(api)).Observed);

        await spring.SendAsync(ines, "message.send", EmailArgs("Hi Ines, thanks for the talk at the meetup."));
        await spring.ClaimAsync();
        var springWork = await spring.RunRoleAsync(role, tomas);
        var autumnItem = await autumn.SendAsync(kai, "message.send", EmailArgs("Hi Kai, a note after the meetup."));
        await autumn.ClaimAsync();
        var autumnWork = await autumn.RunRoleAsync(role, lena);
        try
        {
            var springQuestion = await api.PostOkAsync<DecisionDto>(
                Operations.DecisionRaise,
                new { work_item_id = springWork.WorkItemPublicId, attempt_id = springWork.AttemptPublicId, question = "Should Tomas hear about the autumn pricing before the spring one?" },
                Ct);
            var autumnQuestion = await api.PostOkAsync<DecisionDto>(
                Operations.DecisionRaise,
                new
                {
                    work_item_id = autumnWork.WorkItemPublicId,
                    attempt_id = autumnWork.AttemptPublicId,
                    question = "Keep Lena in the autumn campaign after her role change?",
                    options = new[] { new { label = "Keep her", detail = (string?)null }, new { label = "Move her", detail = (string?)null } },
                },
                Ct);

            // Two campaigns move last: work made and not yet taken by the claim, so someone is being worked on.
            await spring.SendAsync(rafa, "message.send", EmailArgs("Hi Rafa, a note after the meetup."));
            await push.SendAsync(piet, "message.send", EmailArgs("Hi Piet, a note after the meetup."));
            clock.Advance(TimeSpan.FromMinutes(5));
            var now = clock.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);

            // A campaign's status: its row, the counts of its people, the last thirty days, and what waits on it.
            var campaign = await api.RecordAsync<CampaignDto>(Operations.CampaignGet, "card-status", new { campaign_id = spring.Campaign }, Ct);
            Assert.Equal((spring.Campaign, CampaignStatus.Active), (campaign.Id, campaign.Status));
            var status = await api.RecordAsync<CampaignSummaryResponse>(Operations.CampaignSummary, "card-status", new { campaign_ids = new[] { spring.Campaign }, exclude_archived_contacts = true }, Ct);
            var row = Assert.Single(status.Campaigns);
            // Working: the role the runtime started for Tomas, and the send made and not yet taken for Rafa.
            Assert.Equal((12, 2, 2, 1), (row.People, row.Waiting, row.Working, row.Out));
            var month = await api.RecordAsync<CampaignActivityResponse>(Operations.CampaignActivity, "card-status-30-days", new { campaign_ids = new[] { spring.Campaign }, since = "2026-09-17T00:00:00.000Z", until = now, exclude_archived_contacts = true }, Ct);
            Assert.Equal((1, 1), (Assert.Single(month.Campaigns).Contacted, month.Campaigns[0].Replied));
            var waiting = await api.RecordAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, "card-status-waiting", new { status = "pending", campaign_id = spring.Campaign, limit = 50 }, Ct);
            var asked = await api.RecordAsync<Page<DecisionSummaryDto>>(Operations.DecisionList, "card-status-waiting", new { status = "pending", campaign_id = spring.Campaign, limit = 50 }, Ct);
            Assert.Equal(("Ines Duarte", springQuestion.Id), (Assert.Single(waiting.Items).ContactName, Assert.Single(asked.Items).Id));

            // The people the card lists: a first page with more behind it, a search, and the ones the card says are waiting.
            var first = await api.RecordAsync<Page<MembershipItemDto>>(Operations.CampaignListContacts, "card-people-first", new { campaign_id = spring.Campaign, limit = 10, exclude_archived_contacts = true }, Ct);
            Assert.Equal(10, first.Items.Count);
            Assert.NotNull(first.NextCursor);
            var query = await api.RecordAsync<Page<MembershipItemDto>>(Operations.CampaignListContacts, "card-people-query", new { campaign_id = spring.Campaign, limit = 10, exclude_archived_contacts = true, query = "ines" }, Ct);
            Assert.Equal([ines], query.Items.Select(i => i.Contact.Id));
            var peopleWaiting = await api.RecordAsync<Page<MembershipItemDto>>(Operations.CampaignListContacts, "card-people-waiting", new { campaign_id = spring.Campaign, limit = 10, exclude_archived_contacts = true, summary = "waiting" }, Ct);
            Assert.Equal([ines, tomas], peopleWaiting.Items.Select(i => i.Contact.Id).Order(StringComparer.Ordinal));
            var everyone = await api.RecordAsync<Page<ContactDto>>(Operations.ContactList, "card-people-everyone", new { limit = 10 }, Ct);
            Assert.Equal(10, everyone.Items.Count);
            Assert.NotNull(everyone.NextCursor);
            Assert.Equal([ines], (await api.RecordAsync<Page<ContactDto>>(Operations.ContactList, "card-people-query", new { query = "ines", limit = 10 }, Ct)).Items.Select(c => c.Id));
            Assert.Empty((await api.RecordAsync<Page<ContactDto>>(Operations.ContactList, "card-people-nobody", new { query = "nobody-by-this-name", limit = 10 }, Ct)).Items);

            // What needs a person across every campaign, and the campaigns' names the card puts beside it.
            var everything = await api.RecordAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, "card-needs-all", new { }, Ct);
            Assert.Equal([spring.Campaign, autumn.Campaign], everything.Items.Select(a => a.CampaignId).Distinct().Order(StringComparer.Ordinal));
            var everyQuestion = await api.RecordAsync<Page<DecisionSummaryDto>>(Operations.DecisionList, "card-needs-all", new { }, Ct);
            Assert.Equal([springQuestion.Id, autumnQuestion.Id], everyQuestion.Items.Select(d => d.Id));
            Assert.Equal(autumnItem, Assert.Single(everything.Items, a => a.CampaignId == autumn.Campaign).WorkItemId);

            // A draft with nobody in it, and what the runtime answers for a campaign it does not hold.
            Assert.Equal(CampaignStatus.Draft, (await api.RecordAsync<CampaignDto>(Operations.CampaignGet, "card-status-draft", new { campaign_id = summer.Campaign }, Ct)).Status);
            Assert.Equal(0, Assert.Single((await api.RecordAsync<CampaignSummaryResponse>(Operations.CampaignSummary, "card-status-draft", new { campaign_ids = new[] { summer.Campaign }, exclude_archived_contacts = true }, Ct)).Campaigns).People);
            Assert.Empty((await api.RecordAsync<Page<MembershipItemDto>>(Operations.CampaignListContacts, "card-people-empty", new { campaign_id = summer.Campaign, limit = 10, exclude_archived_contacts = true }, Ct)).Items);
            var unknown = PublicId.New("cmp");
            Assert.Equal("campaign_not_found", (await api.RecordErrorAsync(Operations.CampaignSummary, "card-unknown-campaign", new { campaign_ids = new[] { unknown }, exclude_archived_contacts = true }, System.Net.HttpStatusCode.NotFound, Ct)).Code);
            Assert.Equal("campaign_not_found", (await api.RecordErrorAsync(Operations.CampaignGet, "card-unknown-campaign", new { campaign_id = unknown }, System.Net.HttpStatusCode.NotFound, Ct)).Code);

            // The accounts: the plugin that sends and the route that names it, as the Accounts screen reads them.
            var plugins = await api.RecordAsync<JsonObject>(Operations.PluginList, "card-accounts", new { }, Ct);
            Assert.Equal("reply", (string?)plugins["plugins"]![0]!["id"]);
            var routes = await api.RecordAsync<JsonObject>(Operations.RouteList, "card-accounts", new { }, Ct);
            Assert.Equal("reply", (string?)routes["global"]!["default"]!["plugin_id"]);

            // The plan of a draft with two people in it: its row and counts, and the route its first send would take.
            var plan = await api.RecordAsync<CampaignDto>(Operations.CampaignGet, "card-plan-draft", new { campaign_id = winter.Campaign }, Ct);
            Assert.Equal(CampaignStatus.Draft, plan.Status);
            Assert.Equal(2, Assert.Single((await api.RecordAsync<CampaignSummaryResponse>(Operations.CampaignSummary, "card-plan-draft", new { campaign_ids = new[] { winter.Campaign }, exclude_archived_contacts = true }, Ct)).Campaigns).People);
            var route = await api.RecordAsync<JsonObject>(Operations.RouteResolve, "card-plan-message-send", new { campaign_id = winter.Campaign, operation = "message.send" }, Ct);
            Assert.Equal((true, "reply"), ((bool?)route["usable"], (string?)route["plugin_id"]));

            // The campaigns a chip is chosen from: the page the panel lists and the counts it reads for them.
            var listed = await api.RecordAsync<Page<CampaignSummaryDto>>(Operations.CampaignList, "card-chips", new { limit = 100 }, Ct);
            Assert.Equal(5, listed.Items.Count);
            var counts = await api.RecordAsync<CampaignSummaryResponse>(Operations.CampaignSummary, "card-chips", new { campaign_ids = listed.Items.Select(c => c.Id).ToArray(), exclude_archived_contacts = true }, Ct);
            var byCampaign = counts.Campaigns.ToDictionary(c => c.CampaignId);
            Assert.Equal(0, byCampaign[summer.Campaign].People);
            Assert.True(byCampaign[autumn.Campaign].Waiting > 0);
            Assert.True(byCampaign[push.Campaign] is { Working: > 0, Waiting: 0 });

            // Later: one more person put in the first campaign through the API, and the same counts asked again, so a card's
            // Refresh has a newer answer to draw. Recorded last, so no earlier file of the story sees the new member.
            await spring.PersonAsync("Vera", "Lund", "email", "vera@example.test");
            var later = Assert.Single((await api.RecordAsync<CampaignSummaryResponse>(Operations.CampaignSummary, "card-status-later", new { campaign_ids = new[] { spring.Campaign }, exclude_archived_contacts = true }, Ct)).Campaigns);
            Assert.Equal((13, row.Waiting, row.Working, row.Out), (later.People, later.Waiting, later.Working, later.Out));

            // What a person did to the campaign since a moment: asked first, when nobody has done anything yet, then again
            // after a person pauses the campaign through the API (the act the card's choice follows).
            string[] acts = ["campaign_started", "campaign_paused", "approval_approved", "approval_rejected", "decision_answered"];
            var since = clock.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);
            clock.Advance(TimeSpan.FromMinutes(1));
            Assert.Empty((await api.RecordAsync<Page<JournalEntryDto>>(Operations.JournalList, "card-choice-acts-none", new { campaign_id = spring.Campaign, kinds = acts, since, limit = 50 }, Ct)).Items);
            clock.Advance(TimeSpan.FromMinutes(1));
            await api.PostOkAsync<CampaignDto>(Operations.CampaignPause, new { campaign_id = spring.Campaign, actor = Ada }, Ct);
            var choice = Assert.Single((await api.RecordAsync<Page<JournalEntryDto>>(Operations.JournalList, "card-choice-acts", new { campaign_id = spring.Campaign, kinds = acts, since, limit = 50 }, Ct)).Items);
            Assert.Equal("campaign_paused", choice.Kind);
            Assert.Equal("human", choice.Actor.Type.ToString().ToLowerInvariant());

            // Last, a campaign archived by a person, read as a card reads one: its row, its counts, its people, what waits
            // on it and its last thirty days. Recorded after every other file, so none of them sees the archive.
            await api.PostOkAsync<CampaignDto>(Operations.CampaignArchive, new { campaign_id = winter.Campaign, actor = Ada }, Ct);
            var archivedAt = clock.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(CampaignStatus.Archived, (await api.RecordAsync<CampaignDto>(Operations.CampaignGet, "card-archived", new { campaign_id = winter.Campaign }, Ct)).Status);
            Assert.Equal(2, Assert.Single((await api.RecordAsync<CampaignSummaryResponse>(Operations.CampaignSummary, "card-archived", new { campaign_ids = new[] { winter.Campaign }, exclude_archived_contacts = true }, Ct)).Campaigns).People);
            Assert.Equal(2, (await api.RecordAsync<Page<MembershipItemDto>>(Operations.CampaignListContacts, "card-archived", new { campaign_id = winter.Campaign, limit = 10, exclude_archived_contacts = true }, Ct)).Items.Count);
            Assert.Empty((await api.RecordAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, "card-archived", new { status = "pending", campaign_id = winter.Campaign, limit = 50 }, Ct)).Items);
            Assert.Empty((await api.RecordAsync<Page<DecisionSummaryDto>>(Operations.DecisionList, "card-archived", new { status = "pending", campaign_id = winter.Campaign, limit = 50 }, Ct)).Items);
            Assert.Equal(0, Assert.Single((await api.RecordAsync<CampaignActivityResponse>(Operations.CampaignActivity, "card-archived-30-days", new { campaign_ids = new[] { winter.Campaign }, since = "2026-09-17T00:00:00.000Z", until = archivedAt, exclude_archived_contacts = true }, Ct)).Campaigns).Contacted);

            // Last of all, two of what waits read whole as Needs you opens them, and the approval again once a person
            // has approved it since: what an Open on a card read before then lands on.
            var inesApproval = Assert.Single(waiting.Items).Id;
            Assert.Equal("pending", (await api.RecordAsync<ApprovalDto>(Operations.ApprovalGet, "card-needs-pending", new { approval_id = inesApproval }, Ct)).Status.ToString().ToLowerInvariant());
            Assert.Equal(springQuestion.Id, (await api.RecordAsync<DecisionDto>(Operations.DecisionGet, "card-needs-question", new { decision_id = springQuestion.Id }, Ct)).Id);
            await api.PostOkAsync<ApprovalDto>(Operations.ApprovalApprove, new { approval_id = inesApproval, actor = Ada }, Ct);
            Assert.Equal("approved", (await api.RecordAsync<ApprovalDto>(Operations.ApprovalGet, "card-needs-decided", new { approval_id = inesApproval }, Ct)).Status.ToString().ToLowerInvariant());
        }
        finally
        {
            role.Release();
            await springWork.Ended;
            await autumnWork.Ended;
        }
    }

    /// <summary>A campaign no route sends for: what the plan says of it, and the refusal the sender line meets.</summary>
    [Fact]
    public async Task A_campaign_with_no_route_is_refused_where_a_card_asks_for_one()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));
        var scene = await Scene.OpenAsync(api, "Winter plan", routed: false);
        Assert.Equal("no_route", (await api.RecordErrorAsync(Operations.RouteResolve, "card-plan-no-route", new { campaign_id = scene.Campaign, operation = "message.send" }, System.Net.HttpStatusCode.NotFound, Ct)).Code);
        Assert.Equal("no_route", (await api.RecordErrorAsync(Operations.SenderList, "card-sender-unread", new { campaign_id = scene.Campaign, owners = "team", sender_channel = "email", limit = 100 }, System.Net.HttpStatusCode.NotFound, Ct)).Code);
    }

    /// <summary>
    /// A cursor the scrubber cannot read as an id fails the row that records it, naming the file, rather than the scrubber
    /// with a format error of its own.
    /// </summary>
    [Fact]
    public async Task A_cursor_that_is_not_an_id_fails_the_recording_naming_its_file()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));
        api.ScrubCursors = true;
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => api.RecordErrorAsync(Operations.ApprovalList, "card-a-cursor-that-is-not-an-id", new { cursor = "not a cursor!" }, System.Net.HttpStatusCode.BadRequest, Ct));
        Assert.Contains("approval.list", failure.Message, StringComparison.Ordinal);
        Assert.Contains("card-a-cursor-that-is-not-an-id.json", failure.Message, StringComparison.Ordinal);
        Assert.Contains("not a cursor!", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// What the provider lists for a team: the person's own mailbox, named, and a colleague's; without the first once the
    /// account is gone from the provider's list.
    /// </summary>
    private static JsonObject Senders(bool listed)
    {
        var items = new JsonArray();
        if (listed)
        {
            items.Add(Account("s_12", "Owner Example", "owner@example.test", "own"));
        }

        items.Add(Account("s_13", null, "colleague@example.test", "teammate"));
        return new JsonObject
        {
            ["items"] = items,
            ["has_more"] = false,
            ["channels"] = new JsonObject { ["email"] = "listed", ["linkedin"] = "not_asked" },
            ["vendor"] = new JsonObject { ["ordering"] = "email_then_linkedin" },
        };
    }

    private static JsonObject Account(string id, string? name, string address, string owner) => new()
    {
        ["external_id"] = id,
        ["channel"] = "email",
        ["name"] = name,
        ["address"] = address,
        ["status"] = "active",
        ["owner"] = owner,
        ["vendor"] = new JsonObject { ["status"] = "active" },
    };

    /// <summary>
    /// The plugin's side where the provider's process would run: a campaign made at the provider, a send that goes through
    /// and the one thread that moved. The claim, the approval, the attempt, the pins and everything the runtime writes from
    /// an answer are the runtime's own.
    /// </summary>
    private static FakeCommand Provider() => new(WorkItemKind.ProviderOp, context =>
    {
        var input = context.Plan!.Input;
        var (result, pins) = context.Operation switch
        {
            "campaign.create" => (
                new JsonObject
                {
                    ["items"] = new JsonArray(new JsonObject
                    {
                        ["campaign_external_id"] = "c_7714",
                        ["name"] = (string?)input["args"]!["name"],
                        ["status"] = "created",
                        ["state"] = "draft",
                        ["enrolled"] = 0,
                        ["steps_added"] = 0,
                    }),
                },
                new JsonObject { ["campaign"] = "c_7714" }),
            "message.send" => (
                new JsonObject
                {
                    ["items"] = new JsonArray(new JsonObject
                    {
                        ["contact_id"] = (string?)input["contacts"]![0]!["id"],
                        ["status"] = "sent",
                        ["channel"] = "email",
                        ["format"] = "email",
                        ["sender_external_id"] = (string?)input["args"]!["sender"]!["external_id"],
                        ["message"] = new JsonObject { ["external_id"] = "m_501", ["sent_at"] = Moment(Nine) },
                        ["external_ids"] = new JsonObject { ["contact"] = "rc_77" },
                    }),
                },
                new JsonObject { ["contact"] = "rc_77" }),
            "conversation.list" => (
                new JsonObject
                {
                    ["items"] = new JsonArray(new JsonObject
                    {
                        ["external_id"] = "t_501",
                        ["channel"] = "email",
                        ["subject"] = "Re: Notes from the meetup",
                        ["preview"] = "Thanks, could we talk on Thursday?",
                        ["last_activity_at"] = Moment(Nine.AddMinutes(1)),
                        ["unread"] = true,
                        ["contact"] = new JsonObject { ["external_id"] = "rc_77", ["name"] = "Omar Reyes" },
                        ["campaign_external_id"] = "c_7714",
                        ["has_meeting_intent"] = null,
                        ["vendor"] = new JsonObject
                        {
                            ["is_last_message_planned"] = false,
                            ["state"] = "ok",
                            ["category"] = new JsonObject { ["id"] = "c1", ["name"] = "Interested" },
                        },
                    }),
                    ["has_more"] = false,
                    ["vendor"] = new JsonObject { ["ordering"] = "last_activity_desc" },
                },
                null),
            _ => throw new InvalidOperationException($"The stand-in provider has no answer for {context.Operation}."),
        };
        var provenance = new InvocationProvenance(
            "reply", "1.0.0", "sha256:0", PluginProtocol.CurrentVersion, PluginProtocol.OperationContractVersion,
            PluginProtocol.InvocationIdPrefix + "_01K5B7Q2WE5X3M9T0YH4C6RDNC", context.AttemptId, context.Plan.PluginSnapshotId);
        return Task.FromResult<CommandOutcome>(new CommandOutcome.Provider(
            new PluginInvocationResult(new InvocationOutcome.Succeeded(result, pins, new OutcomeDiagnostics(12, 1, 0, 1)), provenance, null),
            null));
    });

    /// <summary>One inbound read step, in a scope of its own, as the scan runs it.</summary>
    private static async Task<InboundReport> InboundScanAsync(RuntimeApiFixture api)
    {
        using var scope = api.Resolve<IServiceScopeFactory>().CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        return await scope.ServiceProvider.GetRequiredService<InboundSync>().SyncAsync(db, Ct);
    }

    private static string Moment(DateTimeOffset moment) =>
        moment.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A role that stays running, as a role does while it thinks, until the test lets it end.</summary>
    private sealed class HoldingRole : ICommand
    {
        private readonly SemaphoreSlim _entered = new(0);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public WorkItemKind Kind => WorkItemKind.AiRole;

        /// <summary>Completes once for each attempt the role has been started for.</summary>
        public Task EnteredAsync(CancellationToken cancellationToken) => _entered.WaitAsync(cancellationToken);

        public void Release() => _release.TrySetResult();

        public async Task<CommandOutcome> RunAsync(CommandContext context, CancellationToken cancellationToken)
        {
            _entered.Release();
            await _release.Task.WaitAsync(cancellationToken);
            return new CommandOutcome.Completed(null);
        }
    }

    /// <summary>A role's work claimed and started as the dispatcher does it; its attempt is live until the role is released.</summary>
    private sealed record RunningRole(string WorkItemPublicId, string AttemptPublicId, Task Ended);

    private static JsonObject EmailArgs(string body) => new()
    {
        ["channel"] = "email",
        ["sender"] = new JsonObject { ["external_id"] = "s_12", ["address"] = "owner@example.test" },
        ["message"] = new JsonObject { ["format"] = "email", ["subject"] = "Notes from the meetup", ["body"] = body },
    };

    /// <summary>
    /// A running runtime with a provider routed for sends, one campaign and the people put in it, all made through the
    /// API; the claim is the runtime's own, run once per call, as the dispatcher's scan runs it.
    /// </summary>
    private sealed class Scene(RuntimeApiFixture api)
    {
        private bool _started;

        public string Campaign { get; private set; } = "";

        public static async Task<Scene> OpenAsync(RuntimeApiFixture api, string name, bool routed = true)
        {
            if (routed)
            {
                Route(api);
            }

            var scene = new Scene(api);
            scene.Campaign = (await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name, actor = Ada }, Ct)).Id;
            return scene;
        }

        /// <summary>One more campaign in the same runtime, under the same route.</summary>
        public async Task<Scene> AnotherAsync(string name)
        {
            var scene = new Scene(api);
            scene.Campaign = (await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name, actor = Ada }, Ct)).Id;
            return scene;
        }

        private static void Route(RuntimeApiFixture api)
        {
            var routed = new JsonObject { ["workspace"] = "a" };
            var schema = JsonNode.Parse("""{ "type": "object", "properties": { "workspace": { "type": "string" } } }""")!.AsObject();
            var plugins = api.Resolve<PluginRegistry>();
            plugins.Replace(
                new PluginSnapshot(PublicId.New(PluginProtocol.SnapshotIdPrefix), Nine.UtcDateTime, SnapshotSource.Startup,
                    [TestPlugins.Loaded("reply", ["campaign.create", "conversation.list", "message.send", "social_invitation.send", "sender.list"], binding: schema) with { Root = Path.Combine(api.Paths.PluginsDirectory, "reply") }]),
                new ReloadReport(Nine.UtcDateTime, SnapshotSource.Startup, Activated: true, [], []));
            api.Resolve<RouteRegistry>().Replace(new RouteSnapshot(
                PublicId.New(RouteSnapshot.IdPrefix),
                Nine,
                plugins.Snapshot.Id,
                new RouteSet(new Route("reply", routed, BindingIdentity.Of(routed)), RouteSet.Empty.Operations),
                new Dictionary<string, RouteSet>(StringComparer.Ordinal)));
        }

        /// <summary>A person made through the API and put in the campaign; the campaign is started once there is somebody in it.</summary>
        public async Task<string> PersonAsync(string first, string last, string channel, string value)
        {
            var person = await api.PostOkAsync<ContactDto>(Operations.ContactCreate, new
            {
                first_name = first,
                last_name = last,
                company = "Kavali",
                channels = new[] { new { channel, value, primary = true } },
                actor = Ada,
            }, Ct);
            await api.PostOkAsync<AddContactsResult>(Operations.CampaignAddContacts, new { campaign_id = Campaign, contacts = new[] { new { contact_id = person.Id } }, actor = Ada }, Ct);
            return person.Id;
        }

        /// <summary>The campaign goes live, once.</summary>
        public async Task StartAsync()
        {
            if (!_started)
            {
                await api.PostOkAsync<CampaignDto>(Operations.CampaignStart, new { campaign_id = Campaign, actor = Ada }, Ct);
                _started = true;
            }
        }

        /// <summary>A send a person put in the queue, with the arguments the operation takes.</summary>
        public async Task<string> SendAsync(string contact, string operation, JsonObject args)
        {
            await StartAsync();

            return (await api.PostOkAsync<WorkItemDto>(Operations.WorkItemCreate, new
            {
                campaign_id = Campaign,
                kind = "provider_op",
                operation,
                contact_id = contact,
                context = new { input = args },
                actor = Ada,
            }, Ct)).Id;
        }

        /// <summary>A researcher's work for one person, claimed by the runtime's own claim and started the way the dispatcher starts it.</summary>
        public async Task<RunningRole> RunRoleAsync(HoldingRole role, string contact)
        {
            await StartAsync();
            var item = await api.PostOkAsync<WorkItemDto>(Operations.WorkItemCreate, new
            {
                campaign_id = Campaign,
                kind = "ai_role",
                role = "researcher",
                contact_id = contact,
                context = new { brief = "Find what changed at Kavali this quarter." },
                actor = Ada,
            }, Ct);
            var claimed = Assert.Single(await ClaimAsync());
            Assert.Equal(item.Id, claimed.WorkItemPublicId);
            var ended = AttemptHandler.RunAsync(api.Resolve<IServiceScopeFactory>(), claimed, api.Resolve<RunningAttemptRegistry>(), NullLogger.Instance);
            await role.EnteredAsync(Ct);
            return new RunningRole(claimed.WorkItemPublicId, claimed.AttemptPublicId, ended);
        }

        /// <summary>One scan of the claim, and each provider send it took run the way the dispatcher runs it, to the end.</summary>
        public async Task<IReadOnlyList<ClaimedWork>> ClaimAndRunAsync()
        {
            var claimed = await ClaimAsync();
            foreach (var work in claimed)
            {
                await AttemptHandler.RunAsync(api.Resolve<IServiceScopeFactory>(), work, api.Resolve<RunningAttemptRegistry>(), NullLogger.Instance);
            }

            return claimed;
        }

        /// <summary>One scan of the claim, as the dispatcher runs it.</summary>
        public async Task<IReadOnlyList<ClaimedWork>> ClaimAsync()
        {
            using var scope = api.Resolve<IServiceScopeFactory>().CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
            return await scope.ServiceProvider.GetRequiredService<Claimer>().ClaimAsync(db, 4, Ct);
        }
    }
}
