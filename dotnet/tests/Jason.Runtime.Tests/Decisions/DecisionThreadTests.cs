using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Decisions;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Decisions;

/// <summary>
/// What a question is about. A question raised by a responder the runtime itself woke for a reply names the thread that
/// reply moved, taken once at the raise from the identifiers the runtime recorded on the item; every other question is
/// about no thread and says null. Every answer that carries the row carries it: the raise, the answer, the get and the list.
/// </summary>
public class DecisionThreadTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task A_question_the_runtimes_own_responder_raises_names_its_thread_in_every_answer()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db, RuntimesOwn, Recorded("mailbox", "501"));
        var service = Service(db);

        var raised = await service.RaiseAsync(Ask(seed), Ct);
        var got = await service.GetAsync(new DecisionGetRequest(raised.Id), Ct);
        var listed = Assert.Single((await service.ListAsync(new DecisionListRequest(null, null, null, null, null), Ct)).Items);
        var answered = await service.AnswerAsync(
            new DecisionAnswerRequest(raised.Id, "close it", null, new ActorRef(ActorType.Human, "dana"), null), Ct);
        var settled = Assert.Single((await service.ListAsync(
            new DecisionListRequest(DecisionStatus.Answered, null, null, null, null), Ct)).Items);

        var thread = new DecisionThread("mailbox", "501");
        Assert.Equal(thread, raised.Thread);
        Assert.Equal(thread, got.Thread);
        Assert.Equal(thread, listed.Thread);
        Assert.Equal(thread, answered.Thread);
        Assert.Equal(thread, settled.Thread);
    }

    /// <summary>
    /// Only the runtime's own responder, and only identifiers it can read as text: a responder a person or a role created
    /// is theirs, whatever its context carries; another role's item is no responder; and a context without the key, or
    /// with a value that is not text, says no thread. None of them refuses the question.
    /// </summary>
    [Theory]
    [InlineData("a person's responder carrying the key")]
    [InlineData("a role's responder carrying the key")]
    [InlineData("another role of the runtime's carrying the key")]
    [InlineData("the runtime's responder without the key")]
    [InlineData("the runtime's responder whose thread is a number")]
    [InlineData("the runtime's responder whose key is not an object")]
    [InlineData("the runtime's responder whose plugin is missing")]
    public async Task A_question_about_no_thread_says_null(string shape)
    {
        var (made, context) = shape switch
        {
            "a person's responder carrying the key" => (By(ActorType.Human), Recorded("mailbox", "501")),
            "a role's responder carrying the key" => (By(ActorType.Role), Recorded("mailbox", "501")),
            "another role of the runtime's carrying the key" => ((Action<WorkItem>)(item =>
            {
                RuntimesOwn(item);
                item.Role = "manager";
            }), Recorded("mailbox", "501")),
            "the runtime's responder without the key" => (RuntimesOwn, new JsonObject { ["trigger"] = JournalKinds.InboundReplyObserved }),
            "the runtime's responder whose thread is a number" => (RuntimesOwn, new JsonObject
            {
                [Responder.InboundReplyKey] = new JsonObject { ["plugin"] = "mailbox", ["thread"] = 501 },
            }),
            "the runtime's responder whose key is not an object" => (RuntimesOwn, new JsonObject { [Responder.InboundReplyKey] = "mailbox 501" }),
            "the runtime's responder whose plugin is missing" => (RuntimesOwn, new JsonObject
            {
                [Responder.InboundReplyKey] = new JsonObject { ["thread"] = "501" },
            }),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db, made, context);
        var service = Service(db);

        var raised = await service.RaiseAsync(Ask(seed), Ct);

        Assert.Null(raised.Thread);
        Assert.Null(Assert.Single((await service.ListAsync(new DecisionListRequest(null, null, null, null, null), Ct)).Items).Thread);
        var row = await db.Decisions.AsNoTracking().SingleAsync(Ct);
        Assert.Equal((null, null), (row.ThreadPluginId, row.ThreadId));
    }

    /// <summary>
    /// Written once: the row keeps the thread it was raised about, and a later patch of the item's context — the respond
    /// step's own, or anybody's — changes nothing a reader of the question sees.
    /// </summary>
    [Fact]
    public async Task The_thread_is_written_at_the_raise_and_a_later_patch_of_the_context_changes_nothing()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db, RuntimesOwn, Recorded("mailbox", "501"));
        var raised = await Service(db).RaiseAsync(Ask(seed), Ct);

        var item = await db.WorkItems.SingleAsync(w => w.Id == seed.Item.Id, Ct);
        item.Context = Recorded("other", "999");
        await db.SaveChangesAsync(Ct);

        await using var fresh = database.Open();
        var got = await Service(fresh).GetAsync(new DecisionGetRequest(raised.Id), Ct);
        Assert.Equal(new DecisionThread("mailbox", "501"), got.Thread);
    }

    private static JsonObject Recorded(string plugin, string thread) => new()
    {
        ["trigger"] = JournalKinds.InboundReplyObserved,
        [Responder.InboundReplyKey] = new JsonObject { ["plugin"] = plugin, ["thread"] = thread, ["channel"] = "email" },
    };

    private static void RuntimesOwn(WorkItem item)
    {
        item.CreatedByType = ActorType.System;
        item.CreatedById = "dispatcher";
    }

    private static Action<WorkItem> By(ActorType type) => item =>
    {
        item.CreatedByType = type;
        item.CreatedById = type == ActorType.Role ? "manager" : "dana";
    };

    private static DecisionRaiseRequest Ask(Seed seed) =>
        new(seed.Item.PublicId, seed.Attempt.PublicId, "Close the thread or reach the person they named?", null, null, null);

    private static DecisionService Service(JasonDbContext db)
    {
        var clock = new FixedClock(Noon);
        return new DecisionService(db, new JournalWriter(clock), clock);
    }

    private static async Task<Seed> SeedAsync(JasonDbContext db, Action<WorkItem> made, JsonObject context)
    {
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var item = WorkItemFactory.NewAiRole(campaign, role: Responder.Role, now: Noon, configure: made);
        item.Context = context;
        item.Status = WorkItemStatus.Processing;
        var attempt = new Attempt
        {
            PublicId = "att_live",
            WorkItem = item,
            Number = 1,
            Status = AttemptStatus.Running,
            StartedAt = Noon,
            LockUntil = Noon.AddMinutes(30),
        };

        db.Campaigns.Add(campaign);
        db.WorkItems.Add(item);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(Ct);
        return new Seed(item, attempt);
    }

    private sealed record Seed(WorkItem Item, Attempt Attempt);
}
