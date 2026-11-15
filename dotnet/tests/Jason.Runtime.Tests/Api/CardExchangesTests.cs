using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Plugins;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Execution;
using Microsoft.Extensions.Logging.Abstractions;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
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
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));
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

        // (b) The edit: the item's arguments as they were, with the text replaced, patched with the card's own reason.
        var edited = EmailArgs("Hi Ines, thanks for the talk at the meetup. Shall we talk next week?");
        var patched = await api.RecordAsync<WorkItemDto>(Operations.WorkItemUpdate, "card-message-edited", new { work_item_id = item, set = new { input = edited }, reason = EditReason, actor = Ada }, Ct);
        Assert.Equal((item, WorkItemStatus.Created), (patched.Id, patched.Status));
        Assert.True(JsonNode.DeepEquals(edited, patched.Context["input"]));

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
                await NextMillisecondAsync();
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
            await NextMillisecondAsync();
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
        Assert.Equal(names, [.. first.Items.Concat(next.Items).Select(a => a.ContactName!)]);
        Assert.Equal(51, first.Items.Concat(next.Items).Select(a => a.Id).Distinct().Count());
    }

    /// <summary>Two ids made one after the other never share a millisecond, so a listing in id order is a listing in the order made.</summary>
    private static Task NextMillisecondAsync() => Task.Delay(2, Ct);

    /// <summary>A role that stays running, as a role does while it thinks, until the test lets it end.</summary>
    private sealed class HoldingRole : ICommand
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public WorkItemKind Kind => WorkItemKind.AiRole;

        public Task Entered => _entered.Task;

        public void Release() => _release.TrySetResult();

        public async Task<CommandOutcome> RunAsync(CommandContext context, CancellationToken cancellationToken)
        {
            _entered.TrySetResult();
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

        public static async Task<Scene> OpenAsync(RuntimeApiFixture api, string name)
        {
            var routed = new JsonObject { ["workspace"] = "a" };
            var schema = JsonNode.Parse("""{ "type": "object", "properties": { "workspace": { "type": "string" } } }""")!.AsObject();
            var plugins = api.Resolve<PluginRegistry>();
            plugins.Replace(
                new PluginSnapshot(PublicId.New(PluginProtocol.SnapshotIdPrefix), Nine.UtcDateTime, SnapshotSource.Startup,
                    [TestPlugins.Loaded("reply", ["message.send", "social_invitation.send"], binding: schema)]),
                new ReloadReport(Nine.UtcDateTime, SnapshotSource.Startup, Activated: true, [], []));
            api.Resolve<RouteRegistry>().Replace(new RouteSnapshot(
                PublicId.New(RouteSnapshot.IdPrefix),
                Nine,
                plugins.Snapshot.Id,
                new RouteSet(new Route("reply", routed, BindingIdentity.Of(routed)), RouteSet.Empty.Operations),
                new Dictionary<string, RouteSet>(StringComparer.Ordinal)));

            var scene = new Scene(api);
            scene.Campaign = (await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name, actor = Ada }, Ct)).Id;
            return scene;
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

        /// <summary>A send a person put in the queue, with the arguments the operation takes.</summary>
        public async Task<string> SendAsync(string contact, string operation, JsonObject args)
        {
            if (!_started)
            {
                await api.PostOkAsync<CampaignDto>(Operations.CampaignStart, new { campaign_id = Campaign, actor = Ada }, Ct);
                _started = true;
            }

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
            await api.PostOkAsync<CampaignDto>(Operations.CampaignStart, new { campaign_id = Campaign, actor = Ada }, Ct);
            _started = true;
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
            await role.Entered.WaitAsync(Ct);
            return new RunningRole(claimed.WorkItemPublicId, claimed.AttemptPublicId, ended);
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
