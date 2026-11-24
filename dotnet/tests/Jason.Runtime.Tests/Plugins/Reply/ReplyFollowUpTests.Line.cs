using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Events;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Dispatch;
using Jason.Runtime.Tests.Routing;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// A person's line from the first letter to their answer, through the runtime's own steps: the claim, the events read of
/// the account, the respond step's hold and the eligibility of a touch, with the official package at the stand-in.
/// </summary>
public partial class ReplyFollowUpTests
{
    /// <summary>
    /// A follow-up left to the runtime, held by Reply for three days, goes into the first letter's thread, and Jason's own
    /// letter there holds nobody: the events read says it as the account's send, which no line of the campaign carries.
    /// The person's answer in that thread is the other side's move, which holds the line, once; and the next touch of the
    /// person waits, <c>created</c> and not eligible, with nothing tried and nothing parked for a person.
    /// </summary>
    [Fact]
    public async Task A_follow_up_goes_into_the_first_letter_s_thread_holds_nobody_and_the_person_s_answer_there_holds_the_next_touch()
    {
        // The stand-in and the package keep this machine's time; the runtime's clock starts with it and is moved by hand.
        var clock = new FixedClock(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var started = await StartAsync(clock);
        var api = await DispatchHarness.ScannedOnceAsync(started, Ct);
        var (campaign, contact) = await WorkAsync(api);

        // 1. The first letter goes, and its reference is recorded.
        var first = await SendAsync(api, campaign, contact, Message(Body));
        Assert.Equal("email-1001", Assert.Single(first.Attempts!).Provenance!.ExternalIdsReturned!["send"]);

        // 2. A follow-up for three days later, created with no follows: the claim composes the line's root, and Reply holds it.
        var moment = clock.GetUtcNow().AddDays(3).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var later = Message("Any news?");
        later["send_after"] = moment;
        var second = await SendAsync(api, campaign, contact, later);
        // The context records the decision less the reference, which only the plugin is handed.
        var decided = second.Context["follows"]!.AsObject();
        Assert.Equal(
            (first.Id, "message.send", "line_root", false),
            ((string?)decided["work_item"], (string?)decided["operation"], Because(second), decided.ContainsKey("reference")));
        Assert.Equal(("accepted", "email-1002"), ((string?)second.Result!["items"]![0]!["status"], Assert.Single(second.Attempts!).Provenance!.ExternalIdsReturned!["send"]));
        var followUp = LastSend(account);
        Assert.Equal(
            ["--json", "-q", "contact", "send-email", "--contact-id", "1001", "--email-account-id", "12", "--message", "-", "--idempotency-key", second.Id, "--send-after", moment, "--follows", "email-1001"],
            followUp.Args);
        Assert.Equal("""{"body":"Any news?"}""", Stdin(followUp));

        // 3. Three days pass at Reply and here. The follow-up lands in the first letter's thread, and the events read of
        // the account says it as the account's own send.
        account.SendBecomes("email-1002", "sent");
        await PassAsync(api, clock, TimeSpan.FromDays(3));
        var thread = Assert.Single(account.Threads)!["id"]!.GetValue<int>();
        await SettleAsync(api, async () => await EventsOfAsync(api, "message_sent", thread) == 1);
        Assert.Equal(
            [(Subject, Body), ("Re: " + Subject, "Any news?")],
            account.OutboundIn(thread).Select(message => ((string?)message["subject"], (string?)message["body"])));
        Assert.Equal(0, await EventsOfAsync(api, "thread_moved", thread));

        // Jason's own move holds nobody: the person is still enrolled, and nothing says a reply or a hold.
        await SettleAsync(api, () => Task.FromResult(true));
        Assert.Equal(MembershipState.Enrolled, await StateAsync(api, campaign));
        Assert.Empty(await LinesAsync(api, campaign, JournalKinds.InboundReplyObserved));
        Assert.Empty(await LinesAsync(api, campaign, JournalKinds.ContactsPaused));

        // 4. The person answers in that thread. The next read says the other side moved it, and the respond step holds the line.
        account.Answers(thread, "Yes, Tuesday works.");
        await PassAsync(api, clock, TimeSpan.FromMinutes(10));
        await SettleAsync(api, async () => await StateAsync(api, campaign) == MembershipState.Paused);
        Assert.Equal(1, await EventsOfAsync(api, "thread_moved", thread));
        Assert.Single(await LinesAsync(api, campaign, JournalKinds.InboundReplyObserved));
        var paused = Assert.Single(await LinesAsync(api, campaign, JournalKinds.ContactsPaused));
        Assert.Equal(ActorType.System, paused.Actor.Type);

        // 5. The third touch is held at eligibility: it stays created, is never claimed, and nothing is parked or tried.
        var third = (await api.PostOkAsync<WorkItemDto>(
            Operations.WorkItemCreate,
            new { campaign_id = campaign, kind = "provider_op", operation = "message.send", contact_id = contact, context = new JsonObject { ["input"] = Message("One more thing.") } },
            Ct)).Id;
        for (var scan = 0; scan < 3; scan++)
        {
            Assert.Equal(0, (await api.Resolve<ScanRunner>().ScanOnceAsync(Ct)).Claimed);
            await IdleAsync(api);
        }

        // Claimed == 0 alone proves nothing: an eligible send is parked for a person's approval with none claimed too. The
        // proof is the four together: still created, not eligible, no attempt, and no approval parked for anybody.
        var held = await api.PostOkAsync<WorkItemDto>(Operations.WorkItemGet, new { work_item_id = third, include_snapshots = true }, Ct);
        Assert.Equal((WorkItemStatus.Created, false), (held.Status, held.Eligible));
        Assert.Empty(held.Attempts ?? []);
        Assert.Empty((await api.PostOkAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, new { }, Ct)).Items);
        Assert.Equal(2, account.Calls.Count(call => call.Command == "contact send-email"));

        // The scans since said the answer and the hold no second time.
        Assert.Single(await LinesAsync(api, campaign, JournalKinds.InboundReplyObserved));
        Assert.Single(await LinesAsync(api, campaign, JournalKinds.ContactsPaused));
    }

    /// <summary>
    /// A runtime as <see cref="StartAsync()"/> starts one, on a clock the test moves. Nothing is summoned by what is read
    /// or by time passing: the line is the point here, not a manager's review or the responder's answer, and the hold is
    /// made whether the responder is woken or not.
    /// </summary>
    private static async Task<RuntimeApiFixture> StartAsync(FixedClock clock) =>
        await ReplyPlugins.StartAsync(
            Ct,
            paths =>
            {
                var settings = JsonNode.Parse(File.ReadAllText(paths.UserSettingsFile))!.AsObject();
                settings["Dispatcher"] = JsonNode.Parse(Dispatcher);
                settings["Manager"] = new JsonObject
                {
                    ["Triggers"] = new JsonArray(JournalKinds.DecisionAnswered),
                    ["ReviewSeconds"] = ManagerOptions.MaximumReviewSeconds,
                    ["DeepReviewSeconds"] = ManagerOptions.MaximumReviewSeconds,
                };
                settings["Responder"] = new JsonObject { ["OnReply"] = false };
                File.WriteAllText(paths.UserSettingsFile, settings.ToJsonString());
                TestRoutes.WriteGlobal(paths, TestRoutes.GlobalDefault(ReplyPlugins.PluginId));
            },
            clock);

    /// <summary>
    /// Moves the runtime's clock on. The dispatcher's own loop wakes when its tick falls inside the move and scans once,
    /// so the test waits for that scan and what it started before it scans itself.
    /// </summary>
    private static async Task PassAsync(RuntimeApiFixture api, FixedClock clock, TimeSpan span)
    {
        var status = api.Resolve<DispatcherStatus>();
        var scans = status.Scans;
        clock.Advance(span);
        if (span >= TimeSpan.FromHours(1))
        {
            Assert.True(await DispatchHarness.EventuallyAsync(() => status.Scans > scans, Ct, timeoutMilliseconds: 30_000), "The loop's tick never scanned.");
        }

        await IdleAsync(api);
    }

    /// <summary>Scans until the condition holds, each scan followed by a wait for the children and the events read it started.</summary>
    private static async Task SettleAsync(RuntimeApiFixture api, Func<Task<bool>> done)
    {
        for (var scan = 0; scan < 12; scan++)
        {
            await api.Resolve<ScanRunner>().ScanOnceAsync(Ct);
            await IdleAsync(api);
            if (await done())
            {
                return;
            }
        }

        Assert.Fail("Twelve scans did not bring the line to the state it waits for.");
    }

    private static async Task IdleAsync(RuntimeApiFixture api)
    {
        var pool = api.Resolve<HandlerPool>();
        var reader = api.Resolve<EventReader>();
        Assert.True(
            await DispatchHarness.EventuallyAsync(() => pool.InFlightAttemptIds.Count == 0 && reader.Idle, Ct, timeoutMilliseconds: 30_000),
            "A child or a read the scan started never finished.");
    }

    /// <summary>The events of the kind the account's reads stored about the thread.</summary>
    private static async Task<int> EventsOfAsync(RuntimeApiFixture api, string kind, int thread)
    {
        await using var db = new JasonDbContext(JasonDbContext.CreateOptions(api.Paths.DatabaseFile));
        var stored = await db.Events.AsNoTracking().Where(row => row.Kind == kind).ToListAsync(Ct);
        return stored.Count(row => (string?)row.Data["thread"] == thread.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>The one member's state in the campaign.</summary>
    private static async Task<MembershipState> StateAsync(RuntimeApiFixture api, string campaign) =>
        Assert.Single((await api.PostOkAsync<Page<MembershipItemDto>>(Operations.CampaignListContacts, new { campaign_id = campaign, limit = 10 }, Ct)).Items).State;

    private static async Task<IReadOnlyList<JournalEntryDto>> LinesAsync(RuntimeApiFixture api, string campaign, string kind) =>
        (await api.PostOkAsync<Page<JournalEntryDto>>(Operations.JournalList, new { campaign_id = campaign, kinds = new[] { kind }, limit = 50 }, Ct)).Items;
}
