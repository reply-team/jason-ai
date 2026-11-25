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
    /// A follow-up left to the runtime, held by Reply for five minutes, goes into the first letter's thread, and Jason's own
    /// letter there holds nobody: the events read says it as the account's send, which no line of the campaign carries.
    /// The person's answer in that thread is the other side's move, which holds the line, once; and the next touch of the
    /// person waits, <c>created</c> and not eligible, with nothing tried and nothing parked for a person. The clock moves
    /// less than the dispatcher's tick at every step, so the loop never scans in the middle of a move.
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

        // 2. A follow-up for five minutes later, created with no follows: the claim composes the line's root, and Reply holds it.
        var moment = clock.GetUtcNow().AddMinutes(5).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
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

        // 3. Eleven minutes pass at Reply and here, past the follow-up's moment and the events cadence and short of the
        // dispatcher's tick. The follow-up lands in the first letter's thread, and the events read of the account says it as
        // the account's own send.
        account.SendBecomes("email-1002", "sent");
        await PassAsync(api, clock, TimeSpan.FromMinutes(11));
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
    /// The person answers in the first letter's thread, and Jason's follow-up, held by Reply for that moment, lands in the
    /// same thread before the next events read. Reply moves no thread moment for the follow-up, so the read finds the
    /// account's letter on top of a thread holding a reply, names nobody, and gives the answer's moment. A follow-up never
    /// explains a move, so the line holds the person, once, and the letter Reply still holds for later is taken back. The
    /// follow-up's moment is minutes away, and the clock moves less than the dispatcher's tick, so the loop never scans in
    /// the middle of the move: the read and the hold come after the follow-up's moment, where it reaches the own-move check.
    /// </summary>
    [Fact]
    public async Task An_answer_a_follow_up_overtakes_before_the_read_holds_the_line_and_takes_back_the_letter_held_for_later()
    {
        var clock = new FixedClock(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var started = await StartAsync(clock);
        var api = await DispatchHarness.ScannedOnceAsync(started, Ct);
        var (campaign, contact) = await WorkAsync(api);

        // 1. The first letter goes and opens its thread; two follow-ups left to the runtime are held by Reply, for five
        // minutes and for six days.
        await SendAsync(api, campaign, contact, Message(Body));
        var thread = Assert.Single(account.Threads)!["id"]!.GetValue<int>();
        var soon = Message("Any news?");
        var due = clock.GetUtcNow().AddMinutes(5);
        soon["send_after"] = due.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var second = await SendAsync(api, campaign, contact, soon);
        var later = Message("One last note.");
        later["send_after"] = clock.GetUtcNow().AddDays(6).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var third = await SendAsync(api, campaign, contact, later);
        Assert.Equal(("line_root", "line_root"), (Because(second), Because(third)));
        Assert.Equal(("email-1002", "email-1003"), (Sent(second), Sent(third)));

        // 2. The person answers in the first letter's thread; then the first follow-up comes due at Reply, and the stand-in
        // places it in that thread before it answers anything more, so before any read.
        account.Answers(thread, "Yes, Tuesday works.");
        account.SendBecomes("email-1002", "sent");

        // 3. Eleven minutes pass here, past the follow-up's moment and the events cadence and short of the dispatcher's
        // tick. The thread holds the answer under the follow-up, the read says it moved by nobody, at the answer's moment,
        // and the line holds.
        await PassAsync(api, clock, TimeSpan.FromMinutes(11));
        await SettleAsync(api, async () => await EventsOfAsync(api, "thread_moved", thread) == 1);
        await SettleAsync(api, async () => (await LinesAsync(api, campaign, JournalKinds.InboundReplyObserved)).Count == 1);
        await SettleAsync(api, () => Task.FromResult(true));
        var messages = Assert.Single(account.Threads.OfType<JsonObject>())["messages"]!.AsArray().OfType<JsonObject>().ToList();
        Assert.Equal([true, false, true], messages.Select(message => (bool)message["isOutbound"]!));
        Assert.Equal((string?)messages[1]["date"], (string?)Assert.Single(account.Threads.OfType<JsonObject>())["lastActivityDate"]);
        Assert.Null(await MoverAsync(api, thread));

        Assert.Equal(MembershipState.Paused, await StateAsync(api, campaign));
        var paused = Assert.Single(await LinesAsync(api, campaign, JournalKinds.ContactsPaused));
        Assert.Equal(ActorType.System, paused.Actor.Type);

        // The walk took the path it is about: the answer was read, and the line held, after the follow-up's moment, so the
        // follow-up reached the own-move check rather than being taken back as a letter still held.
        var observed = Assert.Single(await LinesAsync(api, campaign, JournalKinds.InboundReplyObserved));
        Assert.True(observed.Ts >= due && paused.Ts >= due, $"The answer was read at {observed.Ts:O} and held at {paused.Ts:O}, before the follow-up's moment {due:O}.");

        // The letter Reply holds for later is taken back by one cancel; the follow-up that went is not.
        await using var db = new JasonDbContext(JasonDbContext.CreateOptions(api.Paths.DatabaseFile));
        var cancel = Assert.Single(await db.WorkItems.AsNoTracking().Where(w => w.Operation == "message.cancel").ToListAsync(Ct));
        Assert.Equal(third.Id, (string?)cancel.Context["input"]!["send"]);
    }

    /// <summary>The send reference the work item's succeeded attempt recorded.</summary>
    private static string Sent(WorkItemDto item) => Assert.Single(item.Attempts!).Provenance!.ExternalIdsReturned!["send"];

    /// <summary>Who the account's reads say moved the thread, as the one <c>thread_moved</c> event stored about it says.</summary>
    private static async Task<string?> MoverAsync(RuntimeApiFixture api, int thread)
    {
        await using var db = new JasonDbContext(JasonDbContext.CreateOptions(api.Paths.DatabaseFile));
        var stored = await db.Events.AsNoTracking().Where(row => row.Kind == "thread_moved").ToListAsync(Ct);
        return (string?)Assert.Single(stored, row => (string?)row.Data["thread"] == thread.ToString(CultureInfo.InvariantCulture)).Data["moved_by"];
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
