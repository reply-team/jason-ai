using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Execution;

/// <summary>
/// What a read of a send says when it finds the send gone: one <c>message_sent_observed</c> line, for any send a read
/// finds gone, once, about the work item that made it, holding identifiers and moments
/// and never the text the read returned.
/// </summary>
public class SentFactsTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_read_that_finds_the_send_gone_says_it_once_about_the_item_that_accepted_it()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var (send, read) = await SeedAsync(db);

        Assert.True(await Facts().SayAsync(db, read, "reply", Input(send), Sent(), Ct));
        await db.SaveChangesAsync(Ct);

        var line = await db.Journal.Include(entry => entry.Campaign).SingleAsync(entry => entry.Kind == JournalKinds.MessageSentObserved, Ct);
        Assert.Equal(new ActorRef(ActorType.System, "dispatcher"), new ActorRef(line.ActorType, line.ActorId));
        Assert.Equal(send.CampaignId, line.CampaignId);
        Assert.Equal(send.PublicId, line.WorkItemId);
        Assert.Equal(send.PublicId, line.Key);
        Assert.Equal(send.ContactId, line.ContactId);
        Assert.Null(line.AttemptId);
        Assert.True(JsonNode.DeepEquals(
            new JsonObject
            {
                ["plugin"] = "reply",
                ["reference"] = "email-1001",
                ["channel"] = "email",
                ["sent_at"] = "2026-10-29T11:58:00Z",
                ["message_id"] = "msg-7001",
                ["observed_by"] = "status",
                ["read_work_item"] = read.PublicId,
            },
            line.New),
            line.New!.ToJsonString());
    }

    [Fact]
    public async Task A_second_read_that_finds_it_gone_says_nothing_more()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var (send, read) = await SeedAsync(db);
        Assert.True(await Facts().SayAsync(db, read, "reply", Input(send), Sent(), Ct));
        await db.SaveChangesAsync(Ct);

        var again = WorkItemFactory.NewProviderOp(send.Campaign!, "message.status", Noon.UtcDateTime);
        db.WorkItems.Add(again);
        await db.SaveChangesAsync(Ct);

        Assert.False(await Facts().SayAsync(db, again, "reply", Input(send), Sent(), Ct));
        await db.SaveChangesAsync(Ct);
        Assert.Equal(1, await db.Journal.CountAsync(entry => entry.Kind == JournalKinds.MessageSentObserved, Ct));
    }

    /// <summary>
    /// The send is read only to name it: the finished item is never tracked, so nothing the read's own save writes can
    /// reach it.
    /// </summary>
    [Fact]
    public async Task The_send_is_read_untracked()
    {
        using var database = new TestDatabase();
        WorkItem send, read;
        using (var seeded = database.Open())
        {
            (send, read) = await SeedAsync(seeded);
        }

        using var db = database.Open();
        var reading = await db.WorkItems.SingleAsync(item => item.Id == read.Id, Ct);

        Assert.True(await Facts().SayAsync(db, reading, "reply", Input(send), Sent(), Ct));

        Assert.Equal([read.PublicId], db.ChangeTracker.Entries<WorkItem>().Select(entry => entry.Entity.PublicId));
    }

    [Theory]
    [InlineData("queued")]
    [InlineData("cancelled")]
    [InlineData("failed")]
    [InlineData("unknown")]
    public async Task A_read_that_finds_the_send_waiting_cancelled_or_failed_says_nothing(string state)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var (send, read) = await SeedAsync(db);
        var result = Sent();
        result["state"] = state;
        result["sent_at"] = null;
        result["message_id"] = null;

        Assert.False(await Facts().SayAsync(db, read, "reply", Input(send), result, Ct));
        await db.SaveChangesAsync(Ct);
        Assert.Equal(0, await db.Journal.CountAsync(entry => entry.Kind == JournalKinds.MessageSentObserved, Ct));
    }

    [Fact]
    public async Task The_line_never_holds_the_text_the_read_returned()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var (send, read) = await SeedAsync(db);
        var result = Sent();
        result["rendered_subject"] = "Quarterly planning with Northwind";
        result["rendered_body"] = "Hello Ada, a short note about the plan.";

        Assert.True(await Facts().SayAsync(db, read, "reply", Input(send), result, Ct));
        await db.SaveChangesAsync(Ct);

        var text = (await db.Journal.SingleAsync(entry => entry.Kind == JournalKinds.MessageSentObserved, Ct)).New!.ToJsonString();
        Assert.DoesNotContain("Northwind", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Hello Ada", text, StringComparison.Ordinal);
        Assert.DoesNotContain("rendered", text, StringComparison.Ordinal);
    }

    /// <summary>The send a read names is one of the read's own campaign, or nothing is said: a line names one campaign's work.</summary>
    [Fact]
    public async Task A_send_of_another_campaign_is_never_said()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var (send, _) = await SeedAsync(db);
        var elsewhere = WorkItemFactory.NewCampaign("Elsewhere", now: Noon.UtcDateTime);
        var read = WorkItemFactory.NewProviderOp(elsewhere, "message.status", Noon.UtcDateTime);
        db.Campaigns.Add(elsewhere);
        db.WorkItems.Add(read);
        await db.SaveChangesAsync(Ct);

        Assert.False(await Facts().SayAsync(db, read, "reply", Input(send), Sent(), Ct));
        await db.SaveChangesAsync(Ct);
        Assert.Equal(0, await db.Journal.CountAsync(entry => entry.Kind == JournalKinds.MessageSentObserved, Ct));
    }

    private static SentFacts Facts() => new(new JournalWriter(new FixedClock(Noon)));

    private static async Task<(WorkItem Send, WorkItem Read)> SeedAsync(JasonDbContext db)
    {
        var campaign = WorkItemFactory.NewCampaign(now: Noon.UtcDateTime);
        var ada = new Contact { PublicId = "cnt_ADA", FirstName = "Ada", CreatedAt = Noon.UtcDateTime, UpdatedAt = Noon.UtcDateTime };
        var send = WorkItemFactory.NewProviderOp(campaign, "message.send", Noon.UtcDateTime);
        send.Contact = ada;
        var read = WorkItemFactory.NewProviderOp(campaign, "message.status", Noon.UtcDateTime);
        db.Campaigns.Add(campaign);
        db.Contacts.Add(ada);
        db.WorkItems.AddRange(send, read);
        await db.SaveChangesAsync(Ct);
        return (send, read);
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

    private static JsonObject Sent() => new()
    {
        ["state"] = "sent",
        ["channel"] = "email",
        ["cancelable"] = false,
        ["accepted_for"] = "2026-10-29T11:55:00Z",
        ["sent_at"] = "2026-10-29T11:58:00Z",
        ["message_id"] = "msg-7001",
        ["rendered_subject"] = null,
        ["rendered_body"] = null,
        ["failure_reason"] = null,
        ["failure_code"] = null,
        ["cancelled_at"] = null,
    };
}
