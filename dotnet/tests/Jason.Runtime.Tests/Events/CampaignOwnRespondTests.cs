using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Events;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Replies;
using Jason.Runtime.Tests.Dispatch;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.Tests.Replies;
using Microsoft.EntityFrameworkCore;
using static Jason.Runtime.Tests.Events.EventStoreFixture;

namespace Jason.Runtime.Tests.Events;

/// <summary>
/// The seam between the events store and the readers of its mark: a line the store said and marked <c>campaign_own</c>,
/// read by the dispatcher's own respond step and by warmth over the same database, raises the read and the responder,
/// records a later move of the thread on that responder, and holds a warm person while a new reply waits.
/// </summary>
public class CampaignOwnRespondTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The account, the two campaigns that hold the person, the person, and the store over the harness's database.</summary>
    private static async Task<(EventStoreFixture Store, EventSource Source, Campaign A, Campaign B, Contact Marta)> WorldAsync(DispatchHarness harness)
    {
        InboundSyncHarvestTests.Load(harness, TestPlugins.Loaded(Reader, operations: ["conversation.list", Responder.ReadOperation]));
        InboundSyncHarvestTests.RouteAll(harness, Reader);
        var store = new EventStoreFixture(harness.Open, harness.Clock);
        var source = await store.SourceAsync();
        var a = await store.CampaignAsync();
        var b = await store.CampaignAsync();
        var marta = await store.ContactAsync("c_1001");
        await store.MemberAsync(marta, a);
        await store.MemberAsync(marta, b);
        return (store, source, a, b, marta);
    }

    private static async Task<List<WorkItem>> RespondersAsync(DispatchHarness harness, Campaign campaign)
    {
        await using var db = harness.Open();
        return await db.WorkItems.AsNoTracking().Where(item => item.CampaignId == campaign.Id && item.Role == Responder.Role).ToListAsync(Ct);
    }

    /// <summary>
    /// A reply the store found by the person's pin and marked in the one campaign that had written to them: the respond step
    /// reads the stored line and queues the read of the thread and the responder waiting on it there, and nothing in the
    /// campaign that wrote nothing.
    /// </summary>
    [Fact]
    public async Task A_line_the_store_marked_raises_the_read_and_the_responder()
    {
        using var harness = new DispatchHarness(Noon);
        var (store, source, a, b, marta) = await WorldAsync(harness);
        await store.SentAsync(marta, a);
        await store.StoreAsync(source, null, Page("c-1", Moved(campaign: null, at: Noon.AddHours(-3))));

        Assert.Equal(1, await harness.RespondAsync(Ct));

        var responder = Assert.Single(await RespondersAsync(harness, a));
        Assert.Equal(WorkItemStatus.Waiting, responder.Status);
        Assert.Equal("t_501", (string?)responder.Context["inbound_reply"]!["thread"]);
        Assert.Equal(1, (int?)responder.Context["inbound_reply"]!["move_count"]);
        Assert.DoesNotContain("\"campaign_own\"", responder.Context.ToJsonString(), StringComparison.Ordinal);
        await using (var db = harness.Open())
        {
            var read = await db.WorkItems.SingleAsync(item => item.Operation == Responder.ReadOperation, Ct);
            Assert.Equal(a.Id, read.CampaignId);
            Assert.Equal(read.PublicId, (string?)responder.Context["inbound_reply"]!["read"]);
        }

        Assert.Empty(await RespondersAsync(harness, b));
        Assert.Equal(1, harness.Logs.All.Count(l => l.EndsWith(": thread_not_campaign_own", StringComparison.Ordinal)));
    }

    /// <summary>
    /// The person is warm in the campaign whose thread it is; another campaign then writes to them; they reply again on the
    /// same thread. The line is marked in the thread's campaign, so the reply waits there — the person is no longer warm
    /// but awaiting a judgement — and the move is recorded on the thread's open responder; nothing is marked in the
    /// campaign that wrote later.
    /// </summary>
    [Fact]
    public async Task A_warm_persons_later_reply_on_the_campaigns_own_thread_waits_after_another_campaign_wrote()
    {
        using var harness = new DispatchHarness(Noon);
        var (store, source, a, b, marta) = await WorldAsync(harness);
        await store.SentAsync(marta, a);
        await store.StoreAsync(source, null, Page("c-1", Moved(campaign: null, at: Noon.AddHours(-3))));
        Assert.Equal(1, await harness.RespondAsync(Ct));
        await using (var db = harness.Open())
        {
            var campaign = await db.Campaigns.SingleAsync(c => c.Id == a.Id, Ct);
            var contact = await db.Contacts.SingleAsync(c => c.Id == marta.Id, Ct);
            var verdict = ReplyLines.Request(campaign, contact, "t_501", "msg-1", Stamp(Noon.AddHours(-3)), ReplyNature.Person) with { PluginId = Reader };
            await new ReplyService(db, new JournalWriter(harness.Clock)).RecordAsync(verdict, Ct);
            Assert.Equal(WarmthState.Warm, await Warmth.OfAsync(db, a.Id, marta.Id, Ct));
        }

        await store.SentAsync(marta, b);
        await store.StoreAsync(source, "c-1", Page("c-2", Moved(campaign: null, at: Noon.AddHours(-1))));

        var lines = await store.LinesAsync(a, JournalKinds.InboundReplyObserved);
        Assert.Equal(2, lines.Count);
        Assert.True(lines[1].New!["campaign_own"] is JsonValue value && value.GetValue<bool>(), $"The later line is {lines[1].New!.ToJsonString()}.");
        Assert.All(await store.LinesAsync(b, JournalKinds.InboundReplyObserved), line => Assert.False(line.New!.AsObject().ContainsKey("campaign_own")));
        await using (var db = harness.Open())
        {
            Assert.Equal(WarmthState.AwaitingJudgement, await Warmth.OfAsync(db, a.Id, marta.Id, Ct));
        }

        await harness.RespondAsync(Ct);

        var responder = Assert.Single(await RespondersAsync(harness, a));
        var recorded = responder.Context["inbound_reply"]!;
        Assert.Equal(2, (int?)recorded["move_count"]);
        Assert.Equal([lines[0].PublicId, lines[1].PublicId], recorded["moves"]!.AsArray().Select(move => (string?)move!["line"]));
        Assert.Equal(Stamp(Noon.AddHours(-1)), (string?)recorded["last_activity_at"]);
        Assert.Empty(await RespondersAsync(harness, b));
    }

    /// <summary>
    /// What stays open, said on the page: once two campaigns have written to the person, a reply on a thread neither
    /// holds as its own holds nobody — a person warm in one of them stays warm there, sent to while the reply is not
    /// judged — and raises no responder in either; the managers are woken by the line, as before.
    /// </summary>
    [Fact]
    public async Task A_reply_on_a_thread_neither_writer_holds_holds_nobody()
    {
        using var harness = new DispatchHarness(Noon);
        var (store, source, a, b, marta) = await WorldAsync(harness);
        await store.SentAsync(marta, a);
        await store.StoreAsync(source, null, Page("c-1", Moved(campaign: null, at: Noon.AddHours(-3))));
        await using (var db = harness.Open())
        {
            var campaign = await db.Campaigns.SingleAsync(c => c.Id == a.Id, Ct);
            var contact = await db.Contacts.SingleAsync(c => c.Id == marta.Id, Ct);
            var verdict = ReplyLines.Request(campaign, contact, "t_501", "msg-1", Stamp(Noon.AddHours(-3)), ReplyNature.Person) with { PluginId = Reader };
            await new ReplyService(db, new JournalWriter(harness.Clock)).RecordAsync(verdict, Ct);
        }

        await store.SentAsync(marta, b);
        await store.StoreAsync(source, "c-1", Page("c-2", Moved(thread: "t_777", campaign: null, at: Noon.AddHours(-1))));
        await harness.RespondAsync(Ct);

        await using (var db = harness.Open())
        {
            Assert.Equal(WarmthState.Warm, await Warmth.OfAsync(db, a.Id, marta.Id, Ct));
            Assert.Equal(WarmthState.Cold, await Warmth.OfAsync(db, b.Id, marta.Id, Ct));
        }

        Assert.DoesNotContain(await RespondersAsync(harness, a), item => (string?)item.Context["inbound_reply"]!["thread"] == "t_777");
        Assert.Empty(await RespondersAsync(harness, b));
    }
}
