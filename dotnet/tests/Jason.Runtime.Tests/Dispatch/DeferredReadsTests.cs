using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Plugins;
using Jason.Runtime.Configuration;
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
/// The deferred read step: a send the provider took for a moment that has passed by the grace, and no more than thirty days
/// ago, is read where it ran, as the dispatcher, until something says what became of it: once the grace has passed, an
/// hour after the first read ended, then a day after each, until a read ends a week after the moment, nine reads at most.
/// At most five reads the runtime made are open in a campaign, never two of one send; a read no route takes is not made,
/// and said once. It writes no line itself.
/// </summary>
public class DeferredReadsTests
{
    private static readonly DateTime Noon = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    private const string Mailer = "deferred-mailer";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// A first letter and a follow-up, each taken for a moment that passed by the grace, are each read: one
    /// <c>message.status</c> naming the send and nothing else, made by the dispatcher as root work about no person, at
    /// priority 0, with the step's reason; the claim takes it where the send ran. No line is written by the step.
    /// </summary>
    [Fact]
    public async Task A_deferred_send_whose_moment_passed_by_the_grace_is_read_where_it_ran_a_follow_up_as_a_first_letter()
    {
        using var harness = Routed();
        var (campaign, ines) = await SceneAsync(harness);
        var first = await SendAsync(harness, campaign, ines, Noon.AddMinutes(-20));
        var followUp = await SendAsync(harness, campaign, ines, Noon.AddMinutes(-15), follows: first, named: true);

        Assert.Equal(2, await harness.DeferredReadAsync(Ct));

        await using (var db = harness.Open())
        {
            var reads = await db.WorkItems.AsNoTracking().Where(item => item.Operation == SendReads.Operation).OrderBy(item => item.Id).ToListAsync(Ct);
            Assert.Equal([first, followUp], reads.Select(read => (string?)read.Context["input"]!["send"]));
            Assert.All(reads, read =>
            {
                Assert.Equal((WorkItemKind.ProviderOp, ActorType.System, "dispatcher", 0, (int?)null, WorkItemStatus.Created), (read.Kind, read.CreatedByType, read.CreatedById, read.Priority, read.ContactId, read.Status));
                Assert.Equal(["input"], read.Context.Select(pair => pair.Key));
            });
            var created = await db.Journal.AsNoTracking()
                .Where(entry => entry.Kind == JournalKinds.WorkItemCreated && reads.Select(read => read.PublicId).Contains(entry.WorkItemId!))
                .ToListAsync(Ct);
            Assert.Equal([DeferredReads.Reason, DeferredReads.Reason], created.Select(entry => entry.Reason));
            Assert.False(await db.Journal.AnyAsync(entry => entry.Kind == JournalKinds.MessageUnsentObserved, Ct));
        }

        var claimed = Assert.Single(await harness.ClaimAsync(Ct));
        Assert.Equal((SendReads.Operation, Mailer, InboundSyncHarvestTests.RoutedIdentity), (claimed.Plan!.Contract.Id, claimed.Plan.Plugin.Manifest.Id, claimed.Plan.BindingIdentity));
    }

    /// <summary>
    /// A send whose moment is inside the grace waits for it; one whose moment passed more than thirty days ago, one whose
    /// moment is still to come, one with no moment known and one the provider cannot cancel are not read at all. Once the
    /// grace has passed, the first is read.
    /// </summary>
    [Fact]
    public async Task A_send_inside_its_grace_waits_and_one_beyond_thirty_days_or_not_held_is_never_read()
    {
        using var harness = Routed();
        var (campaign, ines) = await SceneAsync(harness);
        var inside = await SendAsync(harness, campaign, ines, Noon.AddMinutes(-10));
        await SendAsync(harness, campaign, ines, Noon.AddDays(-30).AddMinutes(-1));
        await SendAsync(harness, campaign, ines, Noon.AddHours(1));
        await SendAsync(harness, campaign, ines, moment: null);
        await SendAsync(harness, campaign, ines, Noon.AddHours(-2), cancelable: false);

        Assert.Equal(0, await harness.DeferredReadAsync(Ct));

        harness.Clock.Advance(TimeSpan.FromMinutes(4));
        Assert.Equal(0, await harness.DeferredReadAsync(Ct));

        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, await harness.DeferredReadAsync(Ct));
        Assert.Equal([inside], await ReadSendsAsync(harness));
    }

    /// <summary>
    /// A paused campaign's reads would wait, so none is made there; a send of a campaign that resumes is read at the next
    /// scan after it.
    /// </summary>
    [Fact]
    public async Task A_paused_campaign_s_deferred_sends_are_read_once_it_resumes()
    {
        using var harness = Routed();
        var (campaign, ines) = await SceneAsync(harness, CampaignStatus.Paused);
        var send = await SendAsync(harness, campaign, ines, Noon.AddHours(-1));

        Assert.Equal(0, await harness.DeferredReadAsync(Ct));

        await using (var db = harness.Open())
        {
            await db.Campaigns.Where(c => c.Id == campaign.Id).ExecuteUpdateAsync(set => set.SetProperty(c => c.Status, CampaignStatus.Active), Ct);
        }

        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, await harness.DeferredReadAsync(Ct));
        Assert.Equal([send], await ReadSendsAsync(harness));
    }

    /// <summary>
    /// A read no route takes where the send ran is not made, since the claim would refuse it; the step says so once per
    /// campaign and asks again at its next run, and once a route names the account the read is made.
    /// </summary>
    [Fact]
    public async Task A_send_no_route_takes_is_not_read_said_once_and_read_once_routed()
    {
        using var harness = new DispatchHarness(Noon);
        InboundSyncHarvestTests.Load(harness, Plugin());
        RouteElsewhere(harness);
        var (campaign, ines) = await SceneAsync(harness);
        var send = await SendAsync(harness, campaign, ines, Noon.AddHours(-1));

        Assert.Equal(0, await harness.DeferredReadAsync(Ct));
        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(0, await harness.DeferredReadAsync(Ct));
        Assert.Single(harness.Logs.All, line => line.Contains(DeferredReadSkipCodes.NotRouted, StringComparison.Ordinal));

        InboundSyncHarvestTests.RouteAll(harness, Mailer);
        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, await harness.DeferredReadAsync(Ct));
        Assert.Equal([send], await ReadSendsAsync(harness));
    }

    /// <summary>
    /// At most five reads the runtime made are open in a campaign, whichever step made them, the oldest sends first; the
    /// rest wait until reads end. Another campaign has room of its own.
    /// </summary>
    [Fact]
    public async Task At_most_five_reads_the_runtime_made_are_open_in_a_campaign()
    {
        using var harness = Routed();
        var (campaign, ines) = await SceneAsync(harness);
        var (other, omar) = await SceneAsync(harness);
        var bounced = await SendAsync(harness, campaign, ines, Noon.AddDays(-2));
        await ReadAsync(harness, campaign, bounced, ended: null);
        var sends = new List<string>();
        for (var hour = 9; hour >= 3; hour--)
        {
            sends.Add(await SendAsync(harness, campaign, ines, Noon.AddHours(-hour)));
        }

        var elsewhere = await SendAsync(harness, other, omar, Noon.AddHours(-1));

        Assert.Equal(5, await harness.DeferredReadAsync(Ct));
        Assert.Equal([bounced, .. sends.Take(4), elsewhere], await ReadSendsAsync(harness));

        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(0, await harness.DeferredReadAsync(Ct));

        await EndOpenReadsAsync(harness, campaign, "queued", sends[0]);
        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, await harness.DeferredReadAsync(Ct));
        Assert.Equal(sends[4], (await ReadSendsAsync(harness))[^1]);
    }

    /// <summary>
    /// A send something already says what became of is not read: a cancel that succeeded, an answer that the provider holds
    /// no such send, by a cancel or by a read, or a final read, whoever made it. A cancel answered that nothing was pending,
    /// and a read that says the send can no longer be cancelled, say only that it can no longer be taken back, so it is
    /// still read.
    /// </summary>
    [Fact]
    public async Task A_send_something_says_what_became_of_is_not_read_and_one_no_longer_cancellable_is()
    {
        using var harness = Routed();
        var (campaign, ines) = await SceneAsync(harness);
        var cancelled = await SendAsync(harness, campaign, ines, Noon.AddHours(-6));
        await ReadAsync(harness, campaign, cancelled, Noon.AddHours(-7), operation: HeldSends.Cancel, state: null, by: ActorType.Human);
        var cancelGone = await SendAsync(harness, campaign, ines, Noon.AddHours(-6));
        await ReadAsync(harness, campaign, cancelGone, Noon.AddHours(-7), operation: HeldSends.Cancel, code: "send_not_found", by: ActorType.Human);
        var readGone = await SendAsync(harness, campaign, ines, Noon.AddHours(-6));
        await ReadAsync(harness, campaign, readGone, Noon.AddHours(-1), code: "send_not_found", by: ActorType.Role);
        var went = await SendAsync(harness, campaign, ines, Noon.AddHours(-6));
        await ReadAsync(harness, campaign, went, Noon.AddHours(-1), state: "sent", by: ActorType.Role);
        var failed = await SendAsync(harness, campaign, ines, Noon.AddHours(-6));
        await ReadAsync(harness, campaign, failed, Noon.AddHours(-1), state: "failed", by: ActorType.Human);
        var notPending = await SendAsync(harness, campaign, ines, Noon.AddHours(-6));
        await ReadAsync(harness, campaign, notPending, Noon.AddHours(-7), operation: HeldSends.Cancel, code: "send_not_pending", by: ActorType.Human);
        var sending = await SendAsync(harness, campaign, ines, Noon.AddHours(-6));
        await ReadAsync(harness, campaign, sending, Noon.AddHours(-7), state: "queued", cancelable: false, by: ActorType.Human);

        Assert.Equal(2, await harness.DeferredReadAsync(Ct));
        Assert.Equal([notPending, sending], await ReadSendsAsync(harness));
    }

    /// <summary>
    /// An open read the runtime made of a send — the bounce step's, say — holds a second: never two at once. A read a
    /// person or a role made is theirs, and the runtime makes its own beside it.
    /// </summary>
    [Fact]
    public async Task An_open_read_the_runtime_made_holds_a_second_and_a_person_s_does_not()
    {
        using var harness = Routed();
        var (campaign, ines) = await SceneAsync(harness);
        var held = await SendAsync(harness, campaign, ines, Noon.AddHours(-2));
        await ReadAsync(harness, campaign, held, ended: null);
        var persons = await SendAsync(harness, campaign, ines, Noon.AddHours(-1));
        await ReadAsync(harness, campaign, persons, ended: null, by: ActorType.Human);

        Assert.Equal(1, await harness.DeferredReadAsync(Ct));

        await using var db = harness.Open();
        var system = await db.WorkItems.AsNoTracking()
            .Where(item => item.Operation == SendReads.Operation && item.CreatedByType == ActorType.System)
            .Select(item => item.Context)
            .ToListAsync(Ct);
        Assert.Equal(1, system.Count(context => (string?)context["input"]!["send"] == held));
        Assert.Equal(1, system.Count(context => (string?)context["input"]!["send"] == persons));
    }

    /// <summary>
    /// The reads follow the waits: the first once the grace has passed, the second an hour after the first ended, then a
    /// day after each, never a minute before; the read that ends a week after the moment is the last, and with the shipped
    /// settings that is the ninth.
    /// </summary>
    [Fact]
    public async Task Reads_come_after_the_grace_then_an_hour_then_a_day_until_one_ends_a_week_after_the_moment()
    {
        using var harness = Routed();
        var (campaign, ines) = await SceneAsync(harness);
        var moment = Noon.AddMinutes(-15);
        var send = await SendAsync(harness, campaign, ines, moment);

        var made = await DriveAsync(harness, campaign, send);

        Assert.Equal(DeferredReads.MostReads, made.Count);
        Assert.Equal(moment.AddMinutes(15), made[0]);
        Assert.Equal(made[0].AddMinutes(1).AddHours(1), made[1]);
        for (var read = 2; read < made.Count; read++)
        {
            Assert.Equal(made[read - 1].AddMinutes(1).AddDays(1), made[read]);
        }

        Assert.True(made[^1].AddMinutes(1) >= moment.AddDays(7));
        Assert.True(made[^2].AddMinutes(1) < moment.AddDays(7));
    }

    /// <summary>
    /// The reads stop at the read that ends the overdue span after the moment, whatever is left of the nine: with three days,
    /// the fifth read is the last, and none is made after it however long the clock runs.
    /// </summary>
    [Fact]
    public async Task The_reads_stop_at_the_read_that_ends_the_overdue_span_after_the_moment()
    {
        using var harness = Routed(options => options.DeferredReadOverdueSeconds = 3 * 86_400);
        var (campaign, ines) = await SceneAsync(harness);
        var moment = Noon.AddMinutes(-15);
        var send = await SendAsync(harness, campaign, ines, moment);

        var made = await DriveAsync(harness, campaign, send);

        Assert.Equal(5, made.Count);
        Assert.True(made[^1].AddMinutes(1) >= moment.AddDays(3));
    }

    /// <summary>
    /// Nine reads ended since the moment are the most, whatever the waits; a read that ended the overdue span or more after
    /// the moment is the last; before either, the next is due a wait after the last ended, the first wait after the first.
    /// </summary>
    [Fact]
    public void Nine_reads_are_the_most_and_one_ended_after_the_overdue_span_is_the_last()
    {
        var options = new DispatcherOptions();
        var moment = new DateTimeOffset(Noon);
        var send = new HeldSend("cmp_x", "wi_x", "message.send", moment, Mailer, null);
        var far = moment.AddDays(29);

        Assert.False(DeferredReads.Due(send, null, options, moment.AddSeconds(options.DeferredReadGraceSeconds - 1)));
        Assert.True(DeferredReads.Due(send, null, options, moment.AddSeconds(options.DeferredReadGraceSeconds)));
        Assert.False(DeferredReads.Due(send, new MadeReads(1, moment.AddHours(1)), options, moment.AddHours(2).AddSeconds(-1)));
        Assert.True(DeferredReads.Due(send, new MadeReads(1, moment.AddHours(1)), options, moment.AddHours(2)));
        Assert.False(DeferredReads.Due(send, new MadeReads(2, moment.AddHours(2)), options, moment.AddHours(26).AddSeconds(-1)));
        Assert.True(DeferredReads.Due(send, new MadeReads(2, moment.AddHours(2)), options, moment.AddHours(26)));
        Assert.True(DeferredReads.Due(send, new MadeReads(DeferredReads.MostReads - 1, moment.AddDays(1)), options, far));
        Assert.False(DeferredReads.Due(send, new MadeReads(DeferredReads.MostReads, moment.AddDays(1)), options, far));
        Assert.False(DeferredReads.Due(send, new MadeReads(3, moment.AddSeconds(options.DeferredReadOverdueSeconds)), options, far));
    }

    /// <summary>A whole dispatcher's composition with a plugin that sends, cancels and reads sends, routed by default under one account.</summary>
    private static DispatchHarness Routed(Action<DispatcherOptions>? dispatcher = null)
    {
        var harness = new DispatchHarness(Noon, dispatcher);
        InboundSyncHarvestTests.Load(harness, Plugin());
        InboundSyncHarvestTests.RouteAll(harness, Mailer);
        return harness;
    }

    private static LoadedPlugin Plugin() => TestPlugins.Loaded(Mailer, operations: ["message.send", "message.cancel", SendReads.Operation]);

    /// <summary>Every operation routed to the plugin under another account than the one the sends ran under.</summary>
    private static void RouteElsewhere(DispatchHarness harness)
    {
        var plugins = harness.Resolve<PluginRegistry>().Snapshot;
        harness.Resolve<RouteRegistry>().Replace(new RouteSnapshot(
            PublicId.New(RouteSnapshot.IdPrefix),
            Noon,
            plugins.Id,
            new RouteSet(new Route(Mailer, new JsonObject(), "sha256:another"), RouteSet.Empty.Operations),
            RouteSnapshot.Empty(Noon, plugins.Id).Campaigns));
    }

    private static async Task<(Campaign Campaign, Contact Person)> SceneAsync(DispatchHarness harness, CampaignStatus status = CampaignStatus.Active)
    {
        await using var db = harness.Open();
        var campaign = WorkItemFactory.NewCampaign(status: status, now: Noon.AddDays(-60));
        var person = new Contact { PublicId = PublicId.New("cnt"), FirstName = "Ines", CreatedAt = Noon.AddDays(-60), UpdatedAt = Noon.AddDays(-60) };
        db.Campaigns.Add(campaign);
        db.Contacts.Add(person);
        await db.SaveChangesAsync(Ct);
        return (campaign, person);
    }

    /// <summary>
    /// A send of the campaign to the person that the provider took with its reference, three days before its moment, for
    /// <paramref name="moment"/> — named by its result, or by its arguments' <c>send_after</c> where
    /// <paramref name="named"/> — or for none, saying it can cancel it unless <paramref name="cancelable"/> is false.
    /// </summary>
    internal static async Task<string> SendAsync(
        DispatchHarness harness, Campaign campaign, Contact contact, DateTime? moment, string? follows = null, bool named = false, bool cancelable = true)
    {
        await using var db = harness.Open();
        db.Attach(campaign);
        db.Attach(contact);
        var taken = (moment ?? Noon).AddDays(-3);
        var arguments = new JsonObject { ["channel"] = "email" };
        if (named && moment is { } after)
        {
            arguments["send_after"] = Instant(after);
        }

        if (follows is not null)
        {
            arguments["follows"] = follows;
        }

        var reference = "send-" + PublicId.New("ref")[^8..];
        var send = WorkItemFactory.NewProviderOp(campaign, "message.send", taken);
        send.Contact = contact;
        send.Status = WorkItemStatus.Succeeded;
        send.FinishedAt = taken;
        send.Context = new JsonObject { [WorkItemService.InputKey] = arguments };
        send.Result = new JsonObject
        {
            ["items"] = new JsonArray(new JsonObject
            {
                ["status"] = "accepted",
                ["reference"] = reference,
                ["accepted_for"] = named || moment is null ? null : Instant(moment.Value),
                ["cancelable"] = cancelable,
            }),
        };
        var attempt = WorkItemFactory.NewAttempt(send, 1, AttemptStatus.Succeeded, taken);
        attempt.FinishedAt = taken;
        attempt.Provenance = new AttemptProvenanceDto(
            Mailer, "1.0.0", "sha256:0", PluginProtocol.CurrentVersion, PluginProtocol.OperationContractVersion, "message.send", 1, null, null,
            RouteScope.GlobalDefault, InboundSyncHarvestTests.RoutedIdentity,
            ExternalIdsReturned: new Dictionary<string, string>(StringComparer.Ordinal) { ["send"] = reference });
        db.WorkItems.Add(send);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(Ct);
        return send.PublicId;
    }

    /// <summary>
    /// A read of <paramref name="send"/>, or another <paramref name="operation"/> naming it, made by <paramref name="by"/>:
    /// open where <paramref name="ended"/> is null; otherwise ended then, succeeded with <paramref name="state"/>, or failed
    /// with <paramref name="code"/> where one is given.
    /// </summary>
    private static async Task ReadAsync(
        DispatchHarness harness,
        Campaign campaign,
        string send,
        DateTime? ended,
        string? state = "queued",
        string? code = null,
        ActorType by = ActorType.System,
        bool cancelable = true,
        string operation = SendReads.Operation)
    {
        await using var db = harness.Open();
        db.Attach(campaign);
        var item = WorkItemFactory.NewProviderOp(campaign, operation, (ended ?? Noon).AddMinutes(-1));
        item.CreatedByType = by;
        item.CreatedById = by == ActorType.System ? "dispatcher" : by == ActorType.Role ? "manager" : null;
        item.Context = new JsonObject { [WorkItemService.InputKey] = new JsonObject { ["send"] = send } };
        if (ended is { } at)
        {
            item.FinishedAt = at;
            if (code is not null)
            {
                item.Status = WorkItemStatus.Failed;
                item.LastError = new AttemptErrorDto(code, "The provider answered.", Retriable: false);
            }
            else
            {
                item.Status = WorkItemStatus.Succeeded;
                item.Result = operation == SendReads.Operation ? Answer(state!, cancelable) : new JsonObject();
            }
        }

        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>Ends the campaign's open reads the runtime made, of the sends named or of all, now, with <paramref name="state"/>.</summary>
    private static async Task EndOpenReadsAsync(DispatchHarness harness, Campaign campaign, string state, params string[] sends)
    {
        await using var db = harness.Open();
        var open = await db.WorkItems
            .Where(item => item.CampaignId == campaign.Id && item.Operation == SendReads.Operation && item.CreatedByType == ActorType.System && item.Status == WorkItemStatus.Created)
            .ToListAsync(Ct);
        foreach (var read in open.Where(read => sends.Length == 0 || sends.Contains((string?)read.Context["input"]!["send"])))
        {
            read.Status = WorkItemStatus.Succeeded;
            read.FinishedAt = harness.Clock.GetUtcNow().UtcDateTime;
            read.Result = Answer(state, cancelable: true);
        }

        await db.SaveChangesAsync(Ct);
    }

    /// <summary>
    /// Runs the step a minute before each read is due and at the moment it is, ending each read a minute after it was made,
    /// still queued, until two days pass with no read: answers when each read was made.
    /// </summary>
    private static async Task<List<DateTime>> DriveAsync(DispatchHarness harness, Campaign campaign, string send)
    {
        var made = new List<DateTime>();
        Assert.Equal(1, await harness.DeferredReadAsync(Ct));
        made.Add(harness.Clock.GetUtcNow().UtcDateTime);
        while (true)
        {
            harness.Clock.Advance(TimeSpan.FromMinutes(1));
            await EndOpenReadsAsync(harness, campaign, "queued", send);
            var wait = made.Count == 1 ? TimeSpan.FromHours(1) : TimeSpan.FromDays(1);
            harness.Clock.Advance(wait - TimeSpan.FromMinutes(1));
            Assert.Equal(0, await harness.DeferredReadAsync(Ct));
            harness.Clock.Advance(TimeSpan.FromMinutes(1));
            var queued = await harness.DeferredReadAsync(Ct);
            if (queued == 0)
            {
                harness.Clock.Advance(TimeSpan.FromDays(2));
                Assert.Equal(0, await harness.DeferredReadAsync(Ct));
                return made;
            }

            Assert.Equal(1, queued);
            made.Add(harness.Clock.GetUtcNow().UtcDateTime);
        }
    }

    /// <summary>The sends the system's reads name, in the order the reads were made.</summary>
    private static async Task<List<string>> ReadSendsAsync(DispatchHarness harness)
    {
        await using var db = harness.Open();
        return [.. (await db.WorkItems.AsNoTracking()
            .Where(item => item.Operation == SendReads.Operation && item.CreatedByType == ActorType.System)
            .OrderBy(item => item.Id)
            .Select(item => item.Context)
            .ToListAsync(Ct)).Select(context => (string)context["input"]!["send"]!)];
    }

    /// <summary>A read's answer of <paramref name="state"/>, every member the operation publishes present.</summary>
    internal static JsonObject Answer(string state, bool cancelable) => new()
    {
        ["state"] = state,
        ["channel"] = "email",
        ["cancelable"] = cancelable,
        ["accepted_for"] = null,
        ["sent_at"] = null,
        ["message_id"] = null,
        ["rendered_subject"] = null,
        ["rendered_body"] = null,
        ["failure_reason"] = null,
        ["failure_code"] = null,
        ["cancelled_at"] = null,
    };

    private static string Instant(DateTime moment) => InboundSync.Instant(new DateTimeOffset(moment));
}
