using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Journal;
using Jason.Contracts.Plugins;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Routing;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Execution;

/// <summary>
/// What a read of a send says when it finds the send did not go: one <c>message_unsent_observed</c> line about the work
/// item that made the send, for a send the definition of a held send covers, whoever made the read. A read that finds it
/// failed or cancelled says so; the last read the runtime makes of a deferred send, a week or more after its moment, that
/// still finds it waiting says it is overdue, and no read says so earlier. Nothing is said of a send the campaign's own
/// cancel took back, and the line holds identifiers, the state, the provider's one word and moments, never its sentence.
/// </summary>
public class UnsentFactsTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The moment of a send whose overdue span ends at <see cref="Noon"/> with the shipped settings.</summary>
    private static readonly DateTimeOffset WeekAgo = Noon.AddSeconds(-new DispatcherOptions().DeferredReadOverdueSeconds);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// A read that finds the send failed, or cancelled at the provider, says so once about the send, keyed by it, with the
    /// plugin, the reference, the channel, the state, the provider's one word, the moment, when it was cancelled and the
    /// read: the line its document publishes. A send that failed with no code of the provider's is said with none. A second
    /// read that finds the same says nothing more.
    /// </summary>
    [Theory]
    [InlineData("failed", "mailbox_disconnected", null)]
    [InlineData("failed", null, null)]
    [InlineData("cancelled", null, "2026-11-20T08:30:00Z")]
    public async Task A_read_that_finds_the_send_did_not_go_says_it_once_about_the_send(string state, string? code, string? cancelledAt)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var (send, read) = await SeedAsync(db, Noon.AddHours(-3));
        var result = Answer(state, code, cancelledAt);

        Assert.True(await EndAsync(db, read, send, result, Noon));

        var line = await db.Journal.AsNoTracking().SingleAsync(entry => entry.Kind == JournalKinds.MessageUnsentObserved, Ct);
        Assert.Equal(new ActorRef(ActorType.System, "dispatcher"), new ActorRef(line.ActorType, line.ActorId));
        Assert.Equal((send.CampaignId, send.PublicId, send.PublicId, send.ContactId), (line.CampaignId, line.WorkItemId, line.Key, line.ContactId));
        Assert.True(JsonNode.DeepEquals(
            new JsonObject
            {
                ["plugin"] = "reply",
                ["reference"] = "email-1001",
                ["channel"] = "email",
                ["state"] = state,
                ["failure_code"] = code,
                ["moment"] = "2026-11-20T09:00:00.000Z",
                ["cancelled_at"] = cancelledAt,
                ["read_work_item"] = read.PublicId,
            },
            line.New),
            line.New!.ToJsonString());
        Assert.Empty(JournalKindCatalog.Find(JournalKinds.MessageUnsentObserved)!.CheckBody(line.Key, line.Old, line.New));

        var again = await AnotherReadAsync(db, send, ActorType.Human);
        Assert.False(await EndAsync(db, again, send, result, Noon.AddMinutes(5)));
        Assert.Equal(1, await db.Journal.CountAsync(entry => entry.Kind == JournalKinds.MessageUnsentObserved, Ct));
    }

    /// <summary>
    /// The provider's code is carried only where it is one word of its vocabulary, as a bounce's is; a server's text, a
    /// word too long or one that does not start with a letter or a digit is left out as null. The provider's sentence for
    /// the failure, and any text the read returned, is never in the line.
    /// </summary>
    [Theory]
    [InlineData("550 5.7.1 sending denied")]
    [InlineData("-leading")]
    [InlineData("a.word.longer.than.a.hundred.and.twenty.eight.characters.a.word.longer.than.a.hundred.and.twenty.eight.characters.a.word.longer.than.")]
    public async Task A_code_that_is_not_one_word_is_left_out_and_the_provider_s_sentence_is_never_said(string code)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var (send, read) = await SeedAsync(db, Noon.AddHours(-3));
        var result = Answer("failed", code, cancelledAt: null);
        result["failure_reason"] = "The mailbox of the sender was disconnected.";
        result["rendered_subject"] = "A quiet word about the pilot";

        Assert.True(await EndAsync(db, read, send, result, Noon));

        var line = await db.Journal.AsNoTracking().SingleAsync(entry => entry.Kind == JournalKinds.MessageUnsentObserved, Ct);
        Assert.Null(line.New!["failure_code"]);
        Assert.DoesNotContain("disconnected", line.New.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain("pilot", line.New.ToJsonString(), StringComparison.Ordinal);
        Assert.Empty(JournalKindCatalog.Find(JournalKinds.MessageUnsentObserved)!.CheckBody(line.Key, line.Old, line.New));
    }

    /// <summary>
    /// A send the provider holds for a moment still to come that a read finds failed did not go either, and is said; the
    /// manager's own read says it as the runtime's does.
    /// </summary>
    [Fact]
    public async Task A_held_send_found_failed_before_its_moment_is_said_whoever_read_it()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var (send, read) = await SeedAsync(db, Noon.AddDays(2), by: ActorType.Role);

        Assert.True(await EndAsync(db, read, send, Answer("failed", null, null), Noon));

        var line = await db.Journal.AsNoTracking().SingleAsync(entry => entry.Kind == JournalKinds.MessageUnsentObserved, Ct);
        Assert.Equal(("failed", "2026-11-22T12:00:00.000Z"), ((string?)line.New!["state"], (string?)line.New["moment"]));
    }

    /// <summary>
    /// Nothing is said of a send a cancel of the campaign's took back, whoever made the cancel: the cancel's own line is the
    /// record. Not when a read then finds it cancelled, not when one finds it failed, and not when the last read finds it
    /// still waiting a week after its moment.
    /// </summary>
    [Theory]
    [InlineData("cancelled")]
    [InlineData("failed")]
    [InlineData("queued")]
    [Critical(Family.N1, "a send the campaign took back is never said unsent")]
    public async Task Nothing_is_said_of_a_send_the_campaign_s_own_cancel_took_back(string state)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var (send, read) = await SeedAsync(db, WeekAgo);
        await EarlierAsync(db, send, WeekAgo.AddMinutes(-30), ActorType.Human, operation: HeldSends.Cancel, state: null);
        await EarlierAsync(db, send, Noon.AddDays(-1), ActorType.System);

        Assert.False(await EndAsync(db, read, send, Answer(state, state == "failed" ? "lost" : null, null), Noon));

        Assert.False(await db.Journal.AnyAsync(entry => entry.Kind == JournalKinds.MessageUnsentObserved, Ct));
    }

    /// <summary>
    /// A send the definition does not cover is never said: one the provider cannot cancel, one with no moment known, and
    /// one of another campaign than the read's.
    /// </summary>
    [Theory]
    [InlineData("not cancellable")]
    [InlineData("no moment")]
    [InlineData("another campaign")]
    public async Task A_send_the_definition_does_not_cover_is_never_said(string which)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var (send, read) = await SeedAsync(db, which == "no moment" ? null : Noon.AddHours(-3), cancelable: which != "not cancellable");
        if (which == "another campaign")
        {
            var elsewhere = WorkItemFactory.NewCampaign(now: Noon.UtcDateTime);
            db.Campaigns.Add(elsewhere);
            await db.SaveChangesAsync(Ct);
            read.Campaign = elsewhere;
            await db.SaveChangesAsync(Ct);
        }

        Assert.False(await EndAsync(db, read, send, Answer("failed", null, null), Noon));
        Assert.False(await db.Journal.AnyAsync(entry => entry.Kind == JournalKinds.MessageUnsentObserved, Ct));
    }

    /// <summary>
    /// A read that finds the send went says nothing here, its own line is the sent one; one that finds the provider lost
    /// track of it says nothing either, since it may have gone.
    /// </summary>
    [Theory]
    [InlineData("sent")]
    [InlineData("unknown")]
    public async Task A_read_that_finds_the_send_went_or_may_have_says_nothing_here(string state)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var (send, read) = await SeedAsync(db, WeekAgo);
        await EarlierAsync(db, send, Noon.AddDays(-1), ActorType.System);

        Assert.False(await EndAsync(db, read, send, Answer(state, null, null), Noon));
        Assert.False(await db.Journal.AnyAsync(entry => entry.Kind == JournalKinds.MessageUnsentObserved, Ct));
    }

    /// <summary>
    /// The last read the runtime makes of a deferred send, ending the overdue span after its moment and still finding it
    /// waiting, says it is overdue, with no word of the provider's and no moment it was cancelled; a second after the span
    /// ended earlier, the same read says nothing, even as the ninth: no read says a send is overdue before its time.
    /// </summary>
    [Fact]
    [Critical(Family.N1, "a deferred send is said overdue at its last read, never earlier")]
    public async Task The_last_read_says_a_deferred_send_is_overdue_and_no_read_says_it_earlier()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var (send, read) = await SeedAsync(db, WeekAgo);
        for (var day = 0; day < DeferredReads.MostReads - 1; day++)
        {
            await EarlierAsync(db, send, WeekAgo.AddHours(1).AddMinutes(day), ActorType.System);
        }

        Assert.False(await EndAsync(db, read, send, Answer("queued", null, null), Noon.AddSeconds(-1), save: false));
        Assert.False(await db.Journal.AnyAsync(entry => entry.Kind == JournalKinds.MessageUnsentObserved, Ct));

        Assert.True(await EndAsync(db, read, send, Answer("queued", null, null), Noon));
        var line = await db.Journal.AsNoTracking().SingleAsync(entry => entry.Kind == JournalKinds.MessageUnsentObserved, Ct);
        Assert.True(JsonNode.DeepEquals(
            new JsonObject
            {
                ["plugin"] = "reply",
                ["reference"] = "email-1001",
                ["channel"] = "email",
                ["state"] = SentFacts.Overdue,
                ["failure_code"] = null,
                ["moment"] = "2026-11-13T12:00:00.000Z",
                ["cancelled_at"] = null,
                ["read_work_item"] = read.PublicId,
            },
            line.New),
            line.New!.ToJsonString());
        Assert.Empty(JournalKindCatalog.Find(JournalKinds.MessageUnsentObserved)!.CheckBody(line.Key, line.Old, line.New));
    }

    /// <summary>
    /// Only a read with nothing after it is the last: while a read the runtime made of the send is open, or the reads it
    /// made since the moment have not reached the span or the nine, a read that still finds the send waiting says nothing;
    /// a read of anybody's that comes after the runtime's last read ended past the span, which failed and said nothing,
    /// says it. Nine reads of the runtime's are the most where nine succeeded or nine failed: one that failed among nine
    /// leaves a read still to come, and a person's read after nine that failed is the last. A send a read already found
    /// final, by anybody, is never overdue.
    /// </summary>
    [Theory]
    [InlineData("another read open", false)]
    [InlineData("reads still to come", false)]
    [InlineData("a person's read after the last", true)]
    [InlineData("a person's read after nine, one failed", false)]
    [InlineData("a person's read after nine", true)]
    [InlineData("a person's read after nine that failed", true)]
    [InlineData("found sent before", false)]
    public async Task A_send_is_overdue_only_where_no_read_of_it_is_still_to_come(string scene, bool said)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var by = scene.StartsWith("a person's read", StringComparison.Ordinal) || scene == "reads still to come" ? ActorType.Human : ActorType.System;
        var (send, read) = await SeedAsync(db, WeekAgo, by: by);
        switch (scene)
        {
            case "another read open":
                await EarlierAsync(db, send, Noon.AddDays(-1), ActorType.System);
                await EarlierAsync(db, send, ended: null, ActorType.System);
                break;
            case "reads still to come":
                await EarlierAsync(db, send, Noon.AddDays(-1), ActorType.System);
                break;
            case "a person's read after the last":
                await EarlierAsync(db, send, Noon.AddDays(-1), ActorType.System);
                await EarlierAsync(db, send, Noon, ActorType.System, code: "provider_unavailable");
                break;
            case "a person's read after nine, one failed":
            case "a person's read after nine that failed":
            case "a person's read after nine":
                for (var earlier = 0; earlier < DeferredReads.MostReads; earlier++)
                {
                    var failed = scene.EndsWith("that failed", StringComparison.Ordinal) || (earlier == 0 && scene.EndsWith("one failed", StringComparison.Ordinal));
                    await EarlierAsync(db, send, Noon.AddDays(-1).AddMinutes(earlier), ActorType.System, code: failed ? "provider_unavailable" : null);
                }

                break;
            case "found sent before":
                await EarlierAsync(db, send, Noon.AddDays(-2), ActorType.Role, state: "sent");
                await EarlierAsync(db, send, Noon.AddDays(-1), ActorType.System);
                break;
        }

        var ended = by == ActorType.Human && scene != "reads still to come" ? Noon.AddHours(1) : Noon;
        Assert.Equal(said, await EndAsync(db, read, send, Answer("queued", null, null), ended));
        Assert.Equal(said ? 1 : 0, await db.Journal.CountAsync(entry => entry.Kind == JournalKinds.MessageUnsentObserved, Ct));
    }

    private static SentFacts Facts() => new(new JournalWriter(new FixedClock(Noon)));

    /// <summary>
    /// Ends <paramref name="read"/> at <paramref name="ended"/> the way the recorder does, its row not saved yet when the
    /// line is decided, and saves the change set where <paramref name="save"/> says so; answers whether a line was added.
    /// </summary>
    private static async Task<bool> EndAsync(JasonDbContext db, WorkItem read, WorkItem send, JsonObject result, DateTimeOffset ended, bool save = true)
    {
        read.Status = WorkItemStatus.Succeeded;
        read.FinishedAt = ended.UtcDateTime;
        read.Result = result;
        var said = await Facts().SayUnsentAsync(db, read, "reply", Input(send), result, Ct);
        if (save)
        {
            await db.SaveChangesAsync(Ct);
        }
        else
        {
            db.ChangeTracker.Clear();
        }

        return said;
    }

    /// <summary>
    /// A send of the campaign to a person, taken by the provider with its reference for <paramref name="moment"/> or for
    /// none, saying it can cancel it unless <paramref name="cancelable"/> is false; and a read of it under way, made by
    /// <paramref name="by"/>.
    /// </summary>
    private static async Task<(WorkItem Send, WorkItem Read)> SeedAsync(
        JasonDbContext db, DateTimeOffset? moment, bool cancelable = true, ActorType by = ActorType.System)
    {
        var taken = (moment ?? Noon).AddDays(-3).UtcDateTime;
        var campaign = WorkItemFactory.NewCampaign(now: taken);
        var ada = new Contact { PublicId = "cnt_ADA", FirstName = "Ada", CreatedAt = taken, UpdatedAt = taken };
        var send = WorkItemFactory.NewProviderOp(campaign, "message.send", taken);
        send.Contact = ada;
        send.Status = WorkItemStatus.Succeeded;
        send.FinishedAt = taken;
        send.Context = new JsonObject { ["input"] = new JsonObject { ["channel"] = "email" } };
        send.Result = new JsonObject
        {
            ["items"] = new JsonArray(new JsonObject
            {
                ["status"] = "accepted",
                ["reference"] = "email-1001",
                ["accepted_for"] = moment is { } at ? InboundSync.Instant(at) : null,
                ["cancelable"] = cancelable,
            }),
        };
        var attempt = WorkItemFactory.NewAttempt(send, 1, AttemptStatus.Succeeded, taken);
        attempt.FinishedAt = taken;
        attempt.Provenance = new AttemptProvenanceDto(
            "reply", "1.0.0", "sha256:0", PluginProtocol.CurrentVersion, PluginProtocol.OperationContractVersion, "message.send", 1, null, null,
            RouteScope.GlobalDefault, "sha256:seat",
            ExternalIdsReturned: new Dictionary<string, string>(StringComparer.Ordinal) { ["send"] = "email-1001" });
        var read = Read(campaign, send, by, Noon.UtcDateTime);
        read.Status = WorkItemStatus.Processing;
        db.Campaigns.Add(campaign);
        db.Contacts.Add(ada);
        db.WorkItems.AddRange(send, read);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(Ct);
        return (send, read);
    }

    /// <summary>One more read of the send under way, made by <paramref name="by"/>.</summary>
    private static async Task<WorkItem> AnotherReadAsync(JasonDbContext db, WorkItem send, ActorType by)
    {
        var again = Read(send.Campaign!, send, by, Noon.UtcDateTime);
        again.Status = WorkItemStatus.Processing;
        db.WorkItems.Add(again);
        await db.SaveChangesAsync(Ct);
        return again;
    }

    /// <summary>
    /// An earlier read of the send, or another <paramref name="operation"/> naming it, made by <paramref name="by"/>: open
    /// where <paramref name="ended"/> is null; otherwise ended then, failed with <paramref name="code"/> where one is
    /// given, and succeeded with <paramref name="state"/> otherwise.
    /// </summary>
    private static async Task EarlierAsync(
        JasonDbContext db, WorkItem send, DateTimeOffset? ended, ActorType by, string? state = "queued", string? code = null, string operation = SendReads.Operation)
    {
        var item = Read(send.Campaign!, send, by, (ended ?? Noon).AddMinutes(-1).UtcDateTime, operation);
        if (ended is { } at)
        {
            item.FinishedAt = at.UtcDateTime;
            if (code is not null)
            {
                item.Status = WorkItemStatus.Failed;
                item.LastError = new AttemptErrorDto(code, "The provider answered.", Retriable: true);
            }
            else
            {
                item.Status = WorkItemStatus.Succeeded;
                item.Result = operation == SendReads.Operation ? Answer(state!, null, null) : new JsonObject();
            }
        }

        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);
    }

    private static WorkItem Read(Campaign campaign, WorkItem send, ActorType by, DateTime created, string operation = SendReads.Operation)
    {
        var read = WorkItemFactory.NewProviderOp(campaign, operation, created);
        read.CreatedByType = by;
        read.CreatedById = by switch
        {
            ActorType.System => "dispatcher",
            ActorType.Role => "manager",
            _ => null,
        };
        read.Context = new JsonObject { ["input"] = new JsonObject { ["send"] = send.PublicId } };
        return read;
    }

    /// <summary>The input the claim composes for a read: the send it names, with the reference the send's attempt returned.</summary>
    private static JsonObject Input(WorkItem send) => new()
    {
        ["args"] = new JsonObject { ["send"] = send.PublicId },
        ["send"] = new JsonObject
        {
            ["work_item"] = send.PublicId,
            ["operation"] = send.Operation,
            ["reference"] = "email-1001",
        },
    };

    /// <summary>A read's answer of <paramref name="state"/>, with the provider's code and the moment it was cancelled.</summary>
    private static JsonObject Answer(string state, string? code, string? cancelledAt) => new()
    {
        ["state"] = state,
        ["channel"] = "email",
        ["cancelable"] = state == "queued",
        ["accepted_for"] = null,
        ["sent_at"] = state == "sent" ? "2026-11-20T11:58:00Z" : null,
        ["message_id"] = state == "sent" ? "msg-7001" : null,
        ["rendered_subject"] = null,
        ["rendered_body"] = null,
        ["failure_reason"] = null,
        ["failure_code"] = code,
        ["cancelled_at"] = cancelledAt,
    };
}
