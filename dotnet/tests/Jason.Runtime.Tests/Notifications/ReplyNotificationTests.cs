using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Plugins;
using Jason.Runtime.Configuration;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Notifications;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Dispatch;
using Jason.Runtime.Tests.Journal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Tests.Notifications;

/// <summary>
/// A reply a person wrote themselves is said by a pointer: the verdicts a role recorded inside a run with the nature
/// <c>person</c>, counted by reply in the window since the last pointer that reached anybody, and woken by the end of the run
/// that recorded the verdict — so the approval its answer needs, parked a tick later, is said in the same pointer. A verdict
/// of another nature, a person's own verdict outside a run and the chat's are not counted, and a reply already counted is
/// not counted again.
/// </summary>
public sealed class ReplyNotificationTests
{
    private static readonly DateTime Noon = new(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_person_s_reply_judged_in_a_run_wakes_one_pointer_at_the_run_s_end()
    {
        using var harness = await StartedAsync();
        var run = await RunAsync(harness);
        await VerdictAsync(harness, run, "sha256:reply-1");

        // The run has not ended: the verdict alone wakes nothing yet.
        Assert.Null(await HandedOverAsync(harness));

        await EndAsync(harness, run);
        var request = await HandedOverAsync(harness);
        Assert.True(request is not null, "The end of the run that judged a reply woke no pointer.");

        var toast = FakeChannel.Delivering();
        var line = await Sender(harness, toast).SendAsync(request, Ct);

        Assert.Equal(1, line!.Replies);
        Assert.Equal("1 reply came in. Ask your agent who replied.", Assert.Single(toast.Messages).Text);
        Assert.Equal(1, (await LastLineAsync(harness))["counts"]!["replies"]!.GetValue<int>());
    }

    /// <summary>A run that ends failed or expired is the end of the run all the same.</summary>
    [Theory]
    [InlineData(JournalKinds.WorkItemFailed)]
    [InlineData(JournalKinds.WorkItemExpired)]
    public async Task A_run_that_ended_otherwise_wakes_the_pointer_too(string ended)
    {
        using var harness = await StartedAsync();
        var run = await RunAsync(harness);
        await VerdictAsync(harness, run, "sha256:reply-1");
        await EndAsync(harness, run, ended);

        var line = await Sender(harness, FakeChannel.Delivering()).SendAsync((await HandedOverAsync(harness))!, Ct);

        Assert.Equal(1, line!.Replies);
    }

    /// <summary>
    /// An automatic answer, a bounce, an opt-out and a verdict that does not say are no person's reply; a verdict a person
    /// recorded outside a run is one they already know of; and the chat's verdict is never a person's reply. None wakes a
    /// pointer, and beside something that does, none is counted.
    /// </summary>
    [Theory]
    [InlineData("automatic", true, false)]
    [InlineData("bounce", true, false)]
    [InlineData("opt_out", true, false)]
    [InlineData(null, true, false)]
    [InlineData("person", false, false)]
    [InlineData("person", true, true)]
    public async Task A_verdict_that_is_no_person_s_reply_from_a_run_is_not_said(string? nature, bool inARun, bool confined)
    {
        using var harness = await StartedAsync();
        var run = await RunAsync(harness);
        await VerdictAsync(harness, inARun ? run : null, "sha256:reply-1", nature, confined, campaign: run.Campaign);
        await EndAsync(harness, run);

        Assert.Null(await HandedOverAsync(harness));

        await Waiting.ApprovalsAsync(harness, 1, Ct);
        var toast = FakeChannel.Delivering();
        var line = await Sender(harness, toast).SendAsync((await HandedOverAsync(harness))!, Ct);

        Assert.Equal((1, 0), (line!.Approvals, line.Replies));
        Assert.DoesNotContain("repl", Assert.Single(toast.Messages).Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The verdict recorded by the real writer, <see cref="Jason.Runtime.Replies.ReplyService"/>, as <c>jason reply record</c>
    /// sends it, never a line built by hand: the writer decides the nature's spelling, the confined mark, the key and the
    /// attempt, and the count reads exactly those. A run's attempt counts and its end wakes the pointer; a person outside
    /// any run does not; the same request from the confined chat does not; and a person named from inside a run is that
    /// run's, so it counts.
    /// </summary>
    [Theory]
    [InlineData("attempt", 1)]
    [InlineData("person_outside_a_run", 0)]
    [InlineData("confined", 0)]
    [InlineData("person_inside_a_run", 1)]
    public async Task A_verdict_the_reply_service_records_is_counted_as_the_writer_marks_it(string recorded, int counted)
    {
        using var harness = await StartedAsync();
        var run = await RunAsync(harness);

        await using (var db = harness.Open())
        {
            var actor = recorded is "attempt" or "confined" ? new ActorRef(ActorType.Attempt, run.Attempt) : new ActorRef(ActorType.Human, "dana");
            var verdict = new ReplyRecordRequest(
                run.Campaign,
                "mailbox",
                "thread-1",
                "message-1",
                new DateTimeOffset(Noon),
                "interested",
                0.9,
                ReplyReaction.Draft,
                Actor: actor,
                Nature: ReplyNature.Person);
            var service = new Jason.Runtime.Replies.ReplyService(db, new JournalWriter(harness.Clock));
            using var confined = recorded == "confined" ? ConfinedScope.Open() : null;
            using var inside = recorded == "person_inside_a_run" ? RunScope.Open(run.Attempt) : null;
            await service.RecordAsync(verdict, Ct);
        }

        // The run has not ended: the verdict alone wakes nothing yet. Its end wakes the pointer only where the verdict counts.
        Assert.Null(await HandedOverAsync(harness));
        await EndAsync(harness, run);
        var request = await HandedOverAsync(harness);
        if (counted == 0)
        {
            Assert.Null(request);
            await Waiting.ApprovalsAsync(harness, 1, Ct);
            request = await HandedOverAsync(harness);
        }

        Assert.True(request is not null, $"A verdict recorded {recorded} left no pointer to send.");
        var line = await Sender(harness, FakeChannel.Delivering()).SendAsync(request, Ct);
        Assert.Equal(counted, line!.Replies);
    }

    [Fact]
    public async Task One_reply_judged_in_two_campaigns_is_counted_once()
    {
        using var harness = await StartedAsync();
        var first = await RunAsync(harness);
        var second = await RunAsync(harness);
        await VerdictAsync(harness, first, "sha256:reply-1");
        await VerdictAsync(harness, second, "sha256:reply-1");
        await EndAsync(harness, first);
        await EndAsync(harness, second);

        var line = await Sender(harness, FakeChannel.Delivering()).SendAsync((await HandedOverAsync(harness))!, Ct);

        Assert.Equal(1, line!.Replies);
    }

    [Fact]
    public async Task Two_replies_are_counted_by_reply()
    {
        using var harness = await StartedAsync();
        var run = await RunAsync(harness);
        await VerdictAsync(harness, run, "sha256:reply-1");
        await VerdictAsync(harness, run, "sha256:reply-2");
        await EndAsync(harness, run);

        var toast = FakeChannel.Delivering();
        var line = await Sender(harness, toast).SendAsync((await HandedOverAsync(harness))!, Ct);

        Assert.Equal(2, line!.Replies);
        Assert.StartsWith("2 replies came in.", Assert.Single(toast.Messages).Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A later verdict on a reply already said — the manager recording a person's answer on it, say — wakes nothing and is
    /// not counted again.
    /// </summary>
    [Fact]
    public async Task A_reply_judged_again_in_a_later_window_is_not_said_again()
    {
        using var harness = await StartedAsync();
        var run = await RunAsync(harness);
        await VerdictAsync(harness, run, "sha256:reply-1");
        await EndAsync(harness, run);
        var first = await HandedOverAsync(harness);
        Assert.Equal(1, (await Sender(harness, FakeChannel.Delivering()).SendAsync(first!, Ct))!.Replies);
        harness.NotifierState.Release(first!.NotificationId);

        harness.Clock.Advance(TimeSpan.FromHours(1));
        var again = await RunAsync(harness, run.Campaign);
        await VerdictAsync(harness, again, "sha256:reply-1");
        await EndAsync(harness, again);
        Assert.Null(await HandedOverAsync(harness));

        await Waiting.ApprovalsAsync(harness, 1, Ct);
        var toast = FakeChannel.Delivering();
        var line = await Sender(harness, toast).SendAsync((await HandedOverAsync(harness))!, Ct);

        Assert.Equal(0, line!.Replies);
        Assert.DoesNotContain("repl", Assert.Single(toast.Messages).Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Under the default send mode the responder records its verdict, drafts, and ends; its answer parks at the next claim, a
    /// tick later. Woken by the run's end, the quiet window takes the park in, and the reply and the approval its answer needs
    /// go out as one pointer — never a reply pointer first that would hold the approval behind the floor.
    /// </summary>
    [Fact]
    public async Task Under_every_message_the_reply_and_its_answer_s_approval_are_one_pointer()
    {
        using var harness = await StartedAsync();
        var tick = TimeSpan.FromSeconds(harness.Options.CurrentValue.TickSeconds);
        var before = harness.Queue.Requests.Count;

        var run = await RunAsync(harness);
        await VerdictAsync(harness, run, "sha256:reply-1");
        await harness.Runner.ScanOnceAsync(Ct);
        harness.Clock.Advance(tick);
        await EndAsync(harness, run);
        await harness.Runner.ScanOnceAsync(Ct);

        // The next claim, a tick after the run ended, parks the answer.
        harness.Clock.Advance(tick);
        await Waiting.ApprovalsAsync(harness, 1, Ct);
        await harness.Runner.ScanOnceAsync(Ct);

        // Scan by scan, nothing is said until the burst is quiet; then once.
        for (var waited = TimeSpan.Zero; waited < TimeSpan.FromSeconds(120); waited += tick)
        {
            harness.Clock.Advance(tick);
            await harness.Runner.ScanOnceAsync(Ct);
        }

        var request = Assert.Single(harness.Queue.Requests.Skip(before));
        var toast = FakeChannel.Delivering();
        var line = await Sender(harness, toast).SendAsync(request, Ct);

        Assert.Equal((1, 1), (line!.Approvals, line.Replies));
        Assert.Equal(
            "1 approval is waiting in Jason. 1 reply came in. Ask your agent, or run jason approval list and jason decision list.",
            Assert.Single(toast.Messages).Text);
    }

    /// <summary>A verdict a run recorded after its item already ended — late, and still true — wakes the pointer by its own line.</summary>
    [Fact]
    public async Task A_verdict_recorded_after_its_run_ended_wakes_by_its_own_line()
    {
        using var harness = await StartedAsync();
        var run = await RunAsync(harness);
        await EndAsync(harness, run);
        Assert.Null(await HandedOverAsync(harness));

        await VerdictAsync(harness, run, "sha256:reply-1");
        var request = await HandedOverAsync(harness);

        Assert.True(request is not null, "A verdict recorded after its run ended woke no pointer.");
        Assert.Equal(1, (await Sender(harness, FakeChannel.Delivering()).SendAsync(request, Ct))!.Replies);
    }

    [Fact]
    public async Task A_pointer_that_reached_nobody_leaves_its_replies_for_the_next_one()
    {
        using var harness = await StartedAsync();
        var run = await RunAsync(harness);
        await VerdictAsync(harness, run, "sha256:reply-1");
        await EndAsync(harness, run);
        var unheard = await HandedOverAsync(harness);
        var nobody = await Sender(harness, FakeChannel.Failing("notification_rejected", FailureClass.Permanent)).SendAsync(unheard!, Ct);
        harness.NotifierState.Release(unheard!.NotificationId);
        Assert.Equal(1, nobody!.Replies);

        var next = await RunAsync(harness);
        await VerdictAsync(harness, next, "sha256:reply-2");
        await EndAsync(harness, next);
        var heard = await HandedOverAsync(harness);
        var toast = FakeChannel.Delivering();
        var reached = await Sender(harness, toast).SendAsync(heard!, Ct);

        Assert.Equal(2, reached!.Replies);
        Assert.StartsWith("2 replies came in.", Assert.Single(toast.Messages).Text, StringComparison.Ordinal);
    }

    /// <summary>With <c>Notifications:Replies</c> false, no reply wakes a pointer and none is counted.</summary>
    [Fact]
    public async Task Replies_turned_off_say_nothing()
    {
        using var harness = await StartedAsync(options => options.Replies = false);
        var run = await RunAsync(harness);
        await VerdictAsync(harness, run, "sha256:reply-1");
        await EndAsync(harness, run);

        Assert.Null(await HandedOverAsync(harness));

        await Waiting.ApprovalsAsync(harness, 1, Ct);
        var toast = FakeChannel.Delivering();
        var line = await Sender(harness, toast).SendAsync((await HandedOverAsync(harness))!, Ct);

        Assert.Equal((1, 0), (line!.Approvals, line.Replies));
        Assert.Equal(0, (await LastLineAsync(harness))["counts"]!["replies"]!.GetValue<int>());
        Assert.DoesNotContain("repl", Assert.Single(toast.Messages).Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A start that resumes behind the newest line folds what lies between into one burst, and a run that judged a reply in
    /// what it folds is pointed at as one read line by line is; the switch off, the same fold wakes nothing.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_reply_judged_before_a_start_is_pointed_at_after_it(bool replies)
    {
        using var harness = new DispatchHarness(Noon);
        harness.NotificationsOn(options => options.Replies = replies);
        var covered = await harness.WriteLineAsync(JournalKinds.WorkItemCreated, Ct);
        await harness.WriteLineAsync(JournalKinds.NotificationSent, Ct, new JsonObject
        {
            ["notification_id"] = "ntf_earlier",
            ["counts"] = new JsonObject { ["approvals"] = 1, ["decisions"] = 0 },
            ["covered_through"] = covered.PublicId,
            ["channels"] = new JsonArray(),
            ["last_delivered_at"] = null,
            ["delivered_through"] = covered.PublicId,
        });
        var run = await RunAsync(harness);
        await VerdictAsync(harness, run, "sha256:reply-1");
        await EndAsync(harness, run);

        await harness.Runner.ScanOnceAsync(Ct);
        var request = await HandedOverAsync(harness);

        if (!replies)
        {
            Assert.Null(request);
            return;
        }

        Assert.True(request is not null, "A start that folded a judged reply handed nothing over.");
        Assert.Equal(1, (await Sender(harness, FakeChannel.Delivering()).SendAsync(request, Ct))!.Replies);
    }

    /// <summary>One run of a role in a campaign of its own, or in the one given: its item and its running attempt.</summary>
    internal sealed record Run(string Campaign, string Item, string Attempt);

    internal static async Task<Run> RunAsync(DispatchHarness harness, string? campaign = null)
    {
        var now = harness.Clock.Now.UtcDateTime;
        await using var db = harness.Open();
        var owner = campaign is null ? WorkItemFactory.NewCampaign(now: now) : await db.Campaigns.SingleAsync(c => c.PublicId == campaign, Ct);
        if (campaign is null)
        {
            db.Campaigns.Add(owner);
        }

        var item = WorkItemFactory.NewAiRole(owner, "responder", now);
        item.Status = WorkItemStatus.Processing;
        var attempt = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Running, now);
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);
        return new Run(owner.PublicId, item.PublicId, attempt.PublicId);
    }

    /// <summary>
    /// A verdict on one reply, as <c>jason reply record</c> writes it: from inside the run when one is given, under the
    /// attempt's own actor, and otherwise a person's, outside any run.
    /// </summary>
    internal static async Task VerdictAsync(DispatchHarness harness, Run? run, string key, string? nature = "person", bool confined = false, string? campaign = null)
    {
        await using var db = harness.Open();
        var owner = await db.Campaigns.SingleAsync(c => c.PublicId == (run == null ? campaign : run.Campaign), Ct);
        var item = run is null ? null : await db.WorkItems.SingleAsync(w => w.PublicId == run.Item, Ct);
        var attempt = run is null ? null : await db.Attempts.SingleAsync(a => a.PublicId == run.Attempt, Ct);
        var with = new JsonObject
        {
            ["contact_id"] = null,
            ["decision_id"] = null,
            ["answered_by"] = null,
            ["nature"] = nature,
        };
        if (confined)
        {
            with["confined"] = true;
        }

        new JournalWriter(harness.Clock).AppendDocumented(
            db,
            run is null ? new ActorRef(ActorType.Human, "dana") : new ActorRef(ActorType.Attempt, run.Attempt),
            JournalKinds.ReplyClassified,
            owner,
            key: key,
            with: with,
            workItem: item,
            attempt: attempt);
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>The run's end, as the one routine that ends work writes it.</summary>
    internal static async Task EndAsync(DispatchHarness harness, Run run, string kind = JournalKinds.WorkItemSucceeded)
    {
        await using var db = harness.Open();
        var owner = await db.Campaigns.SingleAsync(c => c.PublicId == run.Campaign, Ct);
        var item = await db.WorkItems.SingleAsync(w => w.PublicId == run.Item, Ct);
        var attempt = await db.Attempts.SingleAsync(a => a.PublicId == run.Attempt, Ct);
        new JournalWriter(harness.Clock).AppendDocumented(
            db,
            Actors.Dispatcher,
            kind,
            owner,
            workItem: item,
            attempt: attempt);
        await db.SaveChangesAsync(Ct);
    }

    private static async Task<DispatchHarness> StartedAsync(Action<NotificationsOptions>? configure = null)
    {
        var harness = new DispatchHarness(Noon);
        harness.NotificationsOn(configure);
        await harness.Runner.ScanOnceAsync(Ct);
        Assert.True(harness.NotifierState.Started);
        return harness;
    }

    private static async Task<NotificationRequest?> HandedOverAsync(DispatchHarness harness)
    {
        var before = harness.Queue.Requests.Count;
        harness.Clock.Advance(TimeSpan.FromSeconds(60));
        await harness.Runner.ScanOnceAsync(Ct);
        return harness.Queue.Requests.Count > before ? harness.Queue.Requests[^1] : null;
    }

    private static NotificationSender Sender(DispatchHarness harness, params INotificationChannel[] channels) => new(
        harness.Scopes,
        new FakeChannels(channels),
        harness.NotifierState,
        harness.NotificationStatus,
        harness.Resolve<LiveSettings<NotificationsOptions>>(),
        harness.Clock,
        harness.Resolve<ILogger<NotificationSender>>());

    private static async Task<JsonNode> LastLineAsync(DispatchHarness harness)
    {
        await using var db = harness.Open();
        var line = await db.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.NotificationSent).OrderByDescending(e => e.Id).FirstAsync(Ct);
        return line.New!;
    }
}
