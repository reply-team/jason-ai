using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.WorkItems;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using static Jason.Runtime.Tests.Dispatch.InboundSyncHarvestTests;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// A reply wakes the responder: each <c>inbound_reply_observed</c> line about a campaign's own thread queues a read of
/// the thread and a responder waiting on it, once a thread while that responder is open; a further move is recorded on
/// it. What the items carry is identifiers and moments, never a word of the conversation.
/// </summary>
public class ResponderTests
{
    private static readonly DateTime Noon = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

    private const string Reader = "reader";

    private const string Sentinel = "SENTINEL_RESPONDER_4b1e";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Critical(Family.M1, "a reply wakes the responder")]
    public async Task Responder_OnSyncLine_CreatesReadThenResponder_InSameScan()
    {
        using var harness = new DispatchHarness(Noon);
        var read = await StartedAsync(harness);
        Readable(harness);
        await SucceedAsync(harness, read, Page(Thread("th-1", Noon.AddMinutes(-5))));

        // Draining: every step runs and nothing is claimed, so what the scan made is still as it made it.
        harness.Status.State = DispatcherState.Draining;
        var report = await harness.Runner.ScanOnceAsync(Ct);

        Assert.Equal(1, report.Observed);
        Assert.Equal(1, report.Responded);
        await using var db = harness.Open();
        var line = await db.Journal.SingleAsync(entry => entry.Kind == JournalKinds.InboundReplyObserved, Ct);
        var get = await db.WorkItems.SingleAsync(item => item.Operation == Responder.ReadOperation, Ct);
        var responder = await db.WorkItems.SingleAsync(item => item.Role == Responder.Role, Ct);

        Assert.True(get.Id < responder.Id);
        Assert.Equal(WorkItemKind.ProviderOp, get.Kind);
        Assert.Equal(WorkItemStatus.Created, get.Status);
        Assert.Equal(50, get.Priority);
        Assert.Equal("""{"input":{"thread":{"external_id":"th-1"},"limit":20,"offset":0}}""", get.Context.ToJsonString());

        Assert.Equal(WorkItemKind.AiRole, responder.Kind);
        Assert.Equal(WorkItemStatus.Waiting, responder.Status);
        Assert.Equal((ActorType.System, "dispatcher"), (responder.CreatedByType, responder.CreatedById));
        Assert.Equal(LineageState.Root, responder.LineageState);
        Assert.Equal((50, 900, 1), (responder.Priority, responder.TimeoutSeconds, responder.MaxAttempts));
        Assert.Null(responder.DueAt);
        Assert.True(JsonNode.DeepEquals(Responder.ResultFormat, responder.ResultFormat));
        Assert.Equal(
            $$$"""{"trigger":"inbound_reply_observed","inbound_reply":{"plugin":"reader","thread":"th-1","channel":"email","contact_id":null,"read":"{{{get.PublicId}}}","last_activity_at":"2026-10-06T11:55:00.000Z","moves":[{"line":"{{{line.PublicId}}}","at":"2026-10-06T11:55:00.000Z"}],"move_count":1}}""",
            responder.Context.ToJsonString());

        // Created already waiting on the read: no attempt behind the wait, released when the read ends.
        var wait = await db.WorkItemWaits.Include(w => w.Items).SingleAsync(w => w.WorkItemId == responder.Id, Ct);
        Assert.Equal((WaitStatus.Open, WaitMode.All, (int?)null), (wait.Status, wait.Mode, wait.AttemptId));
        Assert.Equal([get.Id], wait.Items.Select(i => i.ChildWorkItemId));
        Assert.Equal(Noon.AddSeconds(harness.Options.CurrentValue.AiRole.MaxWaitSeconds), wait.Deadline);
        var reason = await db.Journal.Where(e => e.WorkItemId == responder.PublicId && e.Kind == JournalKinds.WorkItemCreated).Select(e => e.Reason).SingleAsync(Ct);
        Assert.Equal("a reply to answer", reason);
    }

    /// <summary>
    /// The read ends — read, failed or cancelled alike — the resume step releases the responder, and the claim takes it: a
    /// read that failed leaves the responder to find the thread itself, never stranded waiting.
    /// </summary>
    [Theory]
    [InlineData(WorkItemStatus.Succeeded)]
    [InlineData(WorkItemStatus.Failed)]
    [InlineData(WorkItemStatus.Cancelled)]
    public async Task Responder_WaitingOnItsRead_IsReleasedWhenTheReadEnds_AndRuns(WorkItemStatus ended)
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness);
        RoutedTo(harness, Reader);
        await LineAsync(harness, campaign, "th-1", Noon.AddMinutes(-5));
        Assert.Equal(1, await harness.RespondAsync(Ct));

        Assert.Equal(0, await harness.ResumeAsync(Ct));
        await EndAsync(harness, Responder.ReadOperation, ended);
        Assert.Equal(1, await harness.ResumeAsync(Ct));

        string responder;
        await using (var db = harness.Open())
        {
            var item = await db.WorkItems.SingleAsync(row => row.Role == Responder.Role, Ct);
            Assert.Equal(WorkItemStatus.Created, item.Status);
            responder = item.PublicId;
        }

        var claimed = await harness.ClaimAsync(Ct);
        Assert.Equal([responder], claimed.Select(work => work.WorkItemPublicId));
    }

    [Fact]
    public async Task Responder_OnEventsLine_CampaignPin_CreatesPairNextScan()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness);
        RoutedTo(harness, Reader);

        // The events store says a line off the scan, in its reader's own transaction: the next scan's respond step reads it.
        var line = await LineAsync(harness, campaign, "th-9", Noon.AddMinutes(-3), attributedBy: "campaign_pin", events: true);
        Assert.Equal(1, await harness.RespondAsync(Ct));

        await using var db = harness.Open();
        var responder = await db.WorkItems.SingleAsync(item => item.Role == Responder.Role, Ct);
        var get = await db.WorkItems.SingleAsync(item => item.Operation == Responder.ReadOperation, Ct);
        Assert.Equal(
            $$$"""{"trigger":"inbound_reply_observed","inbound_reply":{"plugin":"reader","thread":"th-9","channel":"email","contact_id":null,"read":"{{{get.PublicId}}}","last_activity_at":"2026-10-06T11:57:00.000Z","moves":[{"line":"{{{line}}}","at":"2026-10-06T11:57:00.000Z"}],"move_count":1}}""",
            responder.Context.ToJsonString());
    }

    /// <summary>
    /// Both writers say a move under one key, so the chronicle holds it once and the step acts on it once; a second read of
    /// the same chronicle finds nothing new.
    /// </summary>
    [Fact]
    public async Task Responder_LineSaidByBothWriters_ActsOnce()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness);
        RoutedTo(harness, Reader);
        await LineAsync(harness, campaign, "th-1", Noon.AddMinutes(-5));

        await Assert.ThrowsAsync<DbUpdateException>(() => LineAsync(harness, campaign, "th-1", Noon.AddMinutes(-5), attributedBy: "campaign_pin", events: true));
        Assert.Equal(1, await harness.RespondAsync(Ct));
        Assert.Equal(0, await harness.RespondAsync(Ct));

        await using var db = harness.Open();
        Assert.Equal(1, await db.WorkItems.CountAsync(item => item.Role == Responder.Role, Ct));
        Assert.Equal(1, await db.WorkItems.CountAsync(item => item.Operation == Responder.ReadOperation, Ct));
    }

    [Theory]
    [InlineData(WorkItemStatus.Created)]
    [InlineData(WorkItemStatus.AwaitingApproval)]
    [InlineData(WorkItemStatus.Scheduled)]
    [InlineData(WorkItemStatus.Processing)]
    [InlineData(WorkItemStatus.Waiting)]
    public async Task Responder_SecondMoveWhileOpen_PatchesReplyNoSecondItem(WorkItemStatus status)
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness);
        RoutedTo(harness, Reader);
        var first = await LineAsync(harness, campaign, "th-1", Noon.AddMinutes(-20));
        Assert.Equal(1, await harness.RespondAsync(Ct));
        await StatusAsync(harness, status);

        var second = await LineAsync(harness, campaign, "th-1", Noon.AddMinutes(-2));
        Assert.Equal(0, await harness.RespondAsync(Ct));

        await using var db = harness.Open();
        var responder = await db.WorkItems.SingleAsync(item => item.Role == Responder.Role, Ct);
        Assert.Equal(1, await db.WorkItems.CountAsync(item => item.Operation == Responder.ReadOperation, Ct));
        Assert.Equal(status, responder.Status);
        var reply = responder.Context["inbound_reply"]!;
        Assert.Equal(
            $$"""[{"line":"{{first}}","at":"2026-10-06T11:40:00.000Z"},{"line":"{{second}}","at":"2026-10-06T11:58:00.000Z"}]""",
            reply["moves"]!.ToJsonString());
        Assert.Equal(2, (int)reply["move_count"]!);
        Assert.Equal("2026-10-06T11:58:00.000Z", (string)reply["last_activity_at"]!);

        // Recorded through the path every patch takes, by the dispatcher.
        var patch = await db.Journal.SingleAsync(e => e.WorkItemId == responder.PublicId && e.Kind == JournalKinds.WorkItemContextUpdated, Ct);
        Assert.Equal(("inbound_reply", ActorType.System, "dispatcher"), (patch.Key, patch.ActorType, patch.ActorId));
    }

    [Fact]
    public async Task Responder_ManyMoves_KeepsTheNewestTwenty()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness);
        RoutedTo(harness, Reader);
        for (var move = 0; move < 25; move++)
        {
            await LineAsync(harness, campaign, "th-1", Noon.AddMinutes(-30 + move));
        }

        Assert.Equal(1, await harness.RespondAsync(Ct));

        await using var db = harness.Open();
        var reply = (await db.WorkItems.SingleAsync(item => item.Role == Responder.Role, Ct)).Context["inbound_reply"]!;
        Assert.Equal(25, (int)reply["move_count"]!);
        var moves = reply["moves"]!.AsArray();
        Assert.Equal(20, moves.Count);
        Assert.Equal("2026-10-06T11:35:00.000Z", (string)moves[0]!["at"]!);
        Assert.Equal("2026-10-06T11:54:00.000Z", (string)moves[19]!["at"]!);
    }

    [Theory]
    [InlineData(WorkItemStatus.Succeeded)]
    [InlineData(WorkItemStatus.Failed)]
    public async Task Responder_MoveAfterResponderEnded_CreatesNewPair(WorkItemStatus ended)
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness);
        RoutedTo(harness, Reader);
        await LineAsync(harness, campaign, "th-1", Noon.AddMinutes(-20));
        Assert.Equal(1, await harness.RespondAsync(Ct));
        await StatusAsync(harness, ended);

        await LineAsync(harness, campaign, "th-1", Noon.AddMinutes(-2));
        Assert.Equal(1, await harness.RespondAsync(Ct));

        await using var db = harness.Open();
        Assert.Equal(2, await db.WorkItems.CountAsync(item => item.Role == Responder.Role, Ct));
        Assert.Equal(2, await db.WorkItems.CountAsync(item => item.Operation == Responder.ReadOperation, Ct));
    }

    /// <summary>
    /// The step gives a responder no due date, so one expires only where a person set one.
    /// </summary>
    [Fact]
    public async Task Responder_MoveAfterResponderExpired_CreatesNewPair_ExpiredUntouched()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness);
        RoutedTo(harness, Reader);
        await LineAsync(harness, campaign, "th-1", Noon.AddMinutes(-20));
        Assert.Equal(1, await harness.RespondAsync(Ct));

        string expired;
        string before;
        await using (var db = harness.Open())
        {
            var responder = await db.WorkItems.SingleAsync(item => item.Role == Responder.Role, Ct);
            foreach (var wait in await db.WorkItemWaits.Where(w => w.WorkItemId == responder.Id && w.Status == WaitStatus.Open).ToListAsync(Ct))
            {
                wait.Status = WaitStatus.Expired;
            }

            responder.DueAt = Noon.AddMinutes(-10);
            responder.Status = WorkItemStatus.Expired;
            responder.FinishedAt = Noon.AddMinutes(-10);
            await db.SaveChangesAsync(Ct);
            (expired, before) = (responder.PublicId, responder.Context.ToJsonString());
        }

        await LineAsync(harness, campaign, "th-1", Noon.AddMinutes(-2));
        Assert.Equal(1, await harness.RespondAsync(Ct));

        await using var read = harness.Open();
        Assert.Equal(2, await read.WorkItems.CountAsync(item => item.Role == Responder.Role, Ct));
        Assert.Equal(2, await read.WorkItems.CountAsync(item => item.Operation == Responder.ReadOperation, Ct));
        var old = await read.WorkItems.SingleAsync(item => item.PublicId == expired, Ct);
        Assert.Equal(WorkItemStatus.Expired, old.Status);
        Assert.Equal(before, old.Context.ToJsonString());
        Assert.False(await read.Journal.AnyAsync(e => e.WorkItemId == expired && e.Kind == JournalKinds.WorkItemContextUpdated, Ct));
    }

    /// <summary>A responder somebody else created is theirs: the rule does not count it as the thread's.</summary>
    [Fact]
    public async Task Responder_ARolesOwnResponder_IsNotTheThreads()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness);
        RoutedTo(harness, Reader);
        await using (var db = harness.Open())
        {
            var row = await db.Campaigns.SingleAsync(c => c.Id == campaign, Ct);
            db.WorkItems.Add(WorkItemFactory.NewAiRole(row, role: Responder.Role, now: Noon, configure: item =>
            {
                item.CreatedByType = ActorType.Human;
                item.Context = new JsonObject { ["inbound_reply"] = new JsonObject { ["plugin"] = Reader, ["thread"] = "th-1" } };
            }));
            await db.SaveChangesAsync(Ct);
        }

        await LineAsync(harness, campaign, "th-1", Noon.AddMinutes(-2));
        Assert.Equal(1, await harness.RespondAsync(Ct));
    }

    [Fact]
    public async Task Responder_Off_CreatesNothing_AndMovesWatermark()
    {
        using var harness = new DispatchHarness(Noon);
        harness.Responder.CurrentValue = new ResponderOptions { OnReply = false };
        var campaign = await CampaignAsync(harness);
        RoutedTo(harness, Reader);
        await LineAsync(harness, campaign, "th-1", Noon.AddMinutes(-5));
        var last = await LineAsync(harness, campaign, "th-2", Noon.AddMinutes(-4));

        Assert.Equal(0, await harness.RespondAsync(Ct));
        Assert.Equal(await IdOfAsync(harness, last), await WatermarkAsync(harness, campaign));

        // On again: what was said while it was off wakes nothing.
        harness.Responder.CurrentValue = new ResponderOptions();
        Assert.Equal(0, await harness.RespondAsync(Ct));
        await using var db = harness.Open();
        Assert.Equal(0, await db.WorkItems.CountAsync(Ct));
    }

    [Fact]
    public async Task Responder_NotConfigured_CreatesNothing_LogsOnce()
    {
        // No entry command for the role or by default, and no profile anywhere: the claim would end a responder
        // role_not_launchable, so none is created.
        using var harness = new DispatchHarness(Noon, roles: new RolesOptions());
        var campaign = await CampaignAsync(harness);
        RoutedTo(harness, Reader);
        await LineAsync(harness, campaign, "th-1", Noon.AddMinutes(-5));
        var last = await LineAsync(harness, campaign, "th-2", Noon.AddMinutes(-4));

        Assert.Equal(0, await harness.RespondAsync(Ct));
        await LineAsync(harness, campaign, "th-3", Noon.AddMinutes(-3));
        Assert.Equal(0, await harness.RespondAsync(Ct));

        await using var db = harness.Open();
        Assert.Equal(0, await db.WorkItems.CountAsync(Ct));
        Assert.True(await WatermarkAsync(harness, campaign) > await IdOfAsync(harness, last));
        Assert.Equal(1, harness.Logs.All.Count(line => line.EndsWith(": responder_not_configured", StringComparison.Ordinal)));
        Assert.Equal(0, await db.Journal.CountAsync(e => e.Kind != JournalKinds.InboundReplyObserved && e.Kind != JournalKinds.CampaignCreated, Ct));
    }

    /// <summary>A profile named for the role's work still creates the pair: a disabled or missing one fails at the claim with its own code.</summary>
    [Fact]
    public async Task Responder_ANamedProfile_CountsAsConfigured_EvenWhenItCannotRun()
    {
        using var harness = new DispatchHarness(Noon, roles: new RolesOptions { DefaultExecutionProfile = "night-shift" });
        var campaign = await CampaignAsync(harness);
        RoutedTo(harness, Reader);
        await LineAsync(harness, campaign, "th-1", Noon.AddMinutes(-5));

        Assert.Equal(1, await harness.RespondAsync(Ct));
    }

    [Fact]
    public async Task Responder_ContactPinLine_LeftToManager()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness);
        RoutedTo(harness, Reader);
        var line = await LineAsync(harness, campaign, "th-1", Noon.AddMinutes(-5), attributedBy: "contact_pin", events: true);

        Assert.Equal(0, await harness.RespondAsync(Ct));
        Assert.Equal(1, await harness.SummonAsync(Ct));

        await using var db = harness.Open();
        Assert.Equal(0, await db.WorkItems.CountAsync(item => item.Role == Responder.Role, Ct));
        Assert.Equal(await IdOfAsync(harness, line), await WatermarkAsync(harness, campaign));
        Assert.Equal(1, harness.Logs.All.Count(l => l.EndsWith(": thread_not_campaign_own", StringComparison.Ordinal)));
    }

    public enum Person
    {
        Unpinned,
        NotAMember,
        Excluded,
        Archived,
        Member,
    }

    [Theory]
    [InlineData(Person.Unpinned)]
    [InlineData(Person.NotAMember)]
    [InlineData(Person.Excluded)]
    [InlineData(Person.Archived)]
    [InlineData(Person.Member)]
    public async Task Responder_UnpinnedOrNonMemberContact_ItemHasNoContactColumn_ReplyKeepsId(Person person)
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness);
        RoutedTo(harness, Reader);
        string? contact = null;
        if (person != Person.Unpinned)
        {
            contact = await ContactAsync(harness, "Grace", pins: [(Reader, "rc-grace")]);
            await using var db = harness.Open();
            var row = await db.Contacts.SingleAsync(c => c.PublicId == contact, Ct);
            if (person == Person.Archived)
            {
                row.ArchivedAt = Noon.AddHours(-1);
            }

            if (person != Person.NotAMember)
            {
                db.CampaignContacts.Add(new CampaignContact
                {
                    CampaignId = campaign,
                    ContactId = row.Id,
                    State = person == Person.Excluded ? MembershipState.Excluded : MembershipState.Enrolled,
                    AddedAt = Noon.AddDays(-1),
                    UpdatedAt = Noon.AddDays(-1),
                });
            }

            await db.SaveChangesAsync(Ct);
        }

        await LineAsync(harness, campaign, "th-1", Noon.AddMinutes(-5), contact: contact);
        Assert.Equal(1, await harness.RespondAsync(Ct));

        await using var read = harness.Open();
        var items = await read.WorkItems.Include(item => item.Contact).Where(item => item.CampaignId == campaign).ToListAsync(Ct);
        Assert.Equal(2, items.Count);
        var column = person == Person.Member ? contact : null;
        Assert.All(items, item => Assert.Equal(column, item.Contact?.PublicId));
        Assert.Equal(contact, (string?)items.Single(item => item.Role == Responder.Role).Context["inbound_reply"]!["contact_id"]);
    }

    /// <summary>
    /// A conversation's words — a subject, a preview, a person's name, the provider's own reading of it — reach neither
    /// item, nor any line the step writes: the runtime wakes a responder, it does not tell it what the reply says.
    /// </summary>
    [Fact]
    public async Task Responder_ContextCarriesNoWords()
    {
        using var harness = new DispatchHarness(Noon);
        var read = await StartedAsync(harness);
        Readable(harness);
        await SucceedAsync(harness, read, Page(Thread("th-1", Noon.AddMinutes(-5), words: Sentinel, unread: true, meeting: true)));
        harness.Status.State = DispatcherState.Draining;

        Assert.Equal(1, (await harness.Runner.ScanOnceAsync(Ct)).Responded);

        await using var db = harness.Open();
        var made = await db.WorkItems.Where(item => item.Operation == Responder.ReadOperation || item.Role == Responder.Role).ToListAsync(Ct);
        Assert.Equal(2, made.Count);
        foreach (var item in made)
        {
            var text = item.Context.ToJsonString();
            Assert.DoesNotContain(Sentinel, text, StringComparison.Ordinal);
            foreach (var word in new[] { "subject", "preview", "name", "unread", "has_meeting_intent", "event_id", "attributed_by" })
            {
                Assert.DoesNotContain($"\"{word}\"", text, StringComparison.Ordinal);
            }
        }

        var said = await db.Journal.Where(e => e.Kind != JournalKinds.InboundReplyObserved).ToListAsync(Ct);
        Assert.All(said, e => Assert.DoesNotContain(Sentinel, (e.New?.ToJsonString() ?? "") + (e.Old?.ToJsonString() ?? "") + e.Reason, StringComparison.Ordinal));
        Assert.DoesNotContain(harness.Logs.All, line => line.Contains(Sentinel, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("other-plugin")]
    public async Task Responder_NoRouteForGet_ReadNull(string? routedTo)
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness);
        if (routedTo is not null)
        {
            RoutedTo(harness, routedTo);
        }

        await LineAsync(harness, campaign, "th-1", Noon.AddMinutes(-5));
        Assert.Equal(1, await harness.RespondAsync(Ct));

        await using var db = harness.Open();
        var responder = await db.WorkItems.SingleAsync(Ct);
        Assert.Equal(Responder.Role, responder.Role);
        Assert.Equal(WorkItemStatus.Created, responder.Status);
        Assert.Null((string?)responder.Context["inbound_reply"]!["read"]);
        Assert.Empty(await db.WorkItemWaits.ToListAsync(Ct));
    }

    [Fact]
    public async Task Responder_ThreadIdOverSchema_ReadNull_ResponderStillCreated()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness);
        RoutedTo(harness, Reader);
        var thread = new string('t', 257);
        await LineAsync(harness, campaign, thread, Noon.AddMinutes(-5));

        Assert.Equal(1, await harness.RespondAsync(Ct));

        await using var db = harness.Open();
        var responder = await db.WorkItems.SingleAsync(Ct);
        Assert.Equal((Responder.Role, WorkItemStatus.Created), (responder.Role, responder.Status));
        Assert.Null((string?)responder.Context["inbound_reply"]!["read"]);
        Assert.Equal(thread, (string?)responder.Context["inbound_reply"]!["thread"]);
    }

    /// <summary>
    /// A line the step cannot act on — here a thread whose identifier would make the responder's context larger than any
    /// context may be — is rolled back, said once, and passed over alone: the line after it still wakes its responder.
    /// </summary>
    [Fact]
    public async Task Responder_ALineItCannotActOn_IsPassedOverAlone()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness);
        RoutedTo(harness, Reader);
        var poisoned = await LineAsync(harness, campaign, new string('t', 300_000), Noon.AddMinutes(-6));
        var next = await LineAsync(harness, campaign, "th-2", Noon.AddMinutes(-5));

        Assert.Equal(1, await harness.RespondAsync(Ct));

        await using var db = harness.Open();
        var responder = await db.WorkItems.SingleAsync(item => item.Role == Responder.Role, Ct);
        Assert.Equal("th-2", (string?)responder.Context["inbound_reply"]!["thread"]);
        Assert.Equal(await IdOfAsync(harness, next), await WatermarkAsync(harness, campaign));
        Assert.Equal(1, harness.Logs.All.Count(l => l.StartsWith("The respond step passed over a line of campaign ", StringComparison.Ordinal)));
        Assert.True(await IdOfAsync(harness, poisoned) < await IdOfAsync(harness, next));
    }

    /// <summary>
    /// A write the database refuses by a constraint is the line's, and passed over; one that fails for any other reason —
    /// here a statement the database cannot run, standing in for a disk that failed — is not the line's fault: the
    /// campaign is left for this scan with its watermark where it was, and the next scan acts on the line.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Responder_OnlyAConstraintPassesALineOver_AnyOtherWriteFailureWaitsForTheNextScan(bool refusal)
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness);
        RoutedTo(harness, Reader);
        var line = await LineAsync(harness, campaign, "th-1", Noon.AddMinutes(-5));
        await using (var db = harness.Open())
        {
            var failing = refusal
                ? "CREATE TRIGGER failing_write BEFORE INSERT ON work_items BEGIN SELECT RAISE(ABORT, 'refused'); END"
                : "CREATE TRIGGER failing_write BEFORE INSERT ON work_items BEGIN SELECT no_such_function(); END";
            await db.Database.ExecuteSqlRawAsync(failing, Ct);
        }

        Assert.Equal(0, await harness.RespondAsync(Ct));

        await using (var db = harness.Open())
        {
            Assert.Equal(0, await db.WorkItems.CountAsync(Ct));
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER failing_write", Ct);
        }

        if (refusal)
        {
            Assert.Equal(await IdOfAsync(harness, line), await WatermarkAsync(harness, campaign));
            Assert.Single(harness.Logs.All, l => l.StartsWith("The respond step passed over a line of campaign ", StringComparison.Ordinal));
            Assert.Equal(0, await harness.RespondAsync(Ct));
        }
        else
        {
            Assert.Equal(0, await WatermarkAsync(harness, campaign));
            Assert.DoesNotContain(harness.Logs.All, l => l.StartsWith("The respond step passed over", StringComparison.Ordinal));
            Assert.Single(harness.Logs.All, l => l.StartsWith("The respond step for campaign ", StringComparison.Ordinal));
            Assert.Equal(1, await harness.RespondAsync(Ct));
        }
    }

    [Fact]
    public async Task Responder_ReadsAHundredLinesACampaignAScan()
    {
        using var harness = new DispatchHarness(Noon, roles: new RolesOptions());
        var campaign = await CampaignAsync(harness);
        var lines = new List<string>();
        for (var thread = 0; thread < 150; thread++)
        {
            lines.Add(await LineAsync(harness, campaign, $"th-{thread}", Noon.AddMinutes(-5)));
        }

        await harness.RespondAsync(Ct);
        Assert.Equal(await IdOfAsync(harness, lines[99]), await WatermarkAsync(harness, campaign));
        await harness.RespondAsync(Ct);
        Assert.Equal(await IdOfAsync(harness, lines[149]), await WatermarkAsync(harness, campaign));
    }

    /// <summary>
    /// A campaign whose chronicle says no reply is moved to its own last line, never read again for it; a line said
    /// anywhere else — another campaign's, or the installation's — leaves it where it is, so a quiet campaign costs no write.
    /// </summary>
    [Fact]
    public async Task Responder_NoLine_MovesTheWatermarkToTheCampaignsOwnLastLine()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness);
        var other = await CampaignAsync(harness);
        var own = await CampaignLineAsync(harness, campaign);
        await CampaignLineAsync(harness, other);
        await harness.WriteLineAsync(JournalKinds.CampaignCreated, Ct);

        Assert.Equal(0, await harness.RespondAsync(Ct));
        Assert.Equal(own, await WatermarkAsync(harness, campaign));

        // Every campaign level with its own chronicle: an installation's line writes nothing for any of them.
        await harness.WriteLineAsync(JournalKinds.CampaignCreated, Ct);
        harness.Commands.Clear();
        Assert.Equal(0, await harness.RespondAsync(Ct));
        Assert.Equal(own, await WatermarkAsync(harness, campaign));
        Assert.DoesNotContain(harness.Commands.Texts, text => text.Contains("UPDATE", StringComparison.Ordinal));
    }

    /// <summary>
    /// The thread's open responder is looked for under the writer's lock, so the query is held to a plan: the campaign's
    /// items in the statuses that are not final, by the index on campaign and status, and only the three columns it reads.
    /// </summary>
    [Fact]
    public async Task Responder_OpenResponderQuery_SeeksTheCampaignsLiveItemsByIndex()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness);
        RoutedTo(harness, Reader);
        await LineAsync(harness, campaign, "th-1", Noon.AddMinutes(-20));
        Assert.Equal(1, await harness.RespondAsync(Ct));
        await LineAsync(harness, campaign, "th-1", Noon.AddMinutes(-2));

        harness.Commands.Clear();
        Assert.Equal(0, await harness.RespondAsync(Ct));

        var sent = Assert.Single(harness.Commands.Texts, text => text.StartsWith("SELECT", StringComparison.Ordinal)
            && text.Contains("FROM \"work_items\"", StringComparison.Ordinal)
            && text.Contains("'responder'", StringComparison.Ordinal));
        var selected = sent[..sent.IndexOf("FROM", StringComparison.Ordinal)];
        Assert.Equal(3, selected.Split(',').Length);
        Assert.DoesNotContain("result_json", selected, StringComparison.Ordinal);

        var plan = PlanOf(harness, sent);
        var said = string.Join(" / ", plan);
        Assert.False(plan.Any(step => step.StartsWith("SCAN", StringComparison.Ordinal)), $"The open responder query scans: {said}");
        Assert.True(
            plan.SequenceEqual(
            [
                "SEARCH w USING INDEX ix_work_items_campaign_id_status (campaign_id=? AND status=?)",
                // Sorting what the seek found: the campaign's live items, never its history.
                "USE TEMP B-TREE FOR ORDER BY",
            ]),
            $"The open responder query's plan changed: {said}");
    }

    [Fact]
    public async Task Responder_PausedCampaign_HoldsWatermark_WakesOnResume()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness, CampaignStatus.Paused);
        RoutedTo(harness, Reader);
        await LineAsync(harness, campaign, "th-1", Noon.AddMinutes(-5));

        Assert.Equal(0, await harness.RespondAsync(Ct));
        Assert.Equal(0, await WatermarkAsync(harness, campaign));

        await using (var db = harness.Open())
        {
            (await db.Campaigns.SingleAsync(c => c.Id == campaign, Ct)).Status = CampaignStatus.Active;
            await db.SaveChangesAsync(Ct);
        }

        Assert.Equal(1, await harness.RespondAsync(Ct));
    }

    [Fact]
    public async Task Responder_TooOld_Skipped()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness);
        RoutedTo(harness, Reader);
        await LineAsync(harness, campaign, "th-old", Noon.AddSeconds(-259_201));
        await LineAsync(harness, campaign, "th-new", Noon.AddSeconds(-259_200));

        Assert.Equal(1, await harness.RespondAsync(Ct));

        await using var db = harness.Open();
        Assert.Equal("th-new", (string?)(await db.WorkItems.SingleAsync(item => item.Role == Responder.Role, Ct)).Context["inbound_reply"]!["thread"]);
        Assert.Equal(1, harness.Logs.All.Count(l => l.EndsWith(": reply_too_old", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Responder_NeverSetsIdempotencyKey()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness);
        RoutedTo(harness, Reader);
        await LineAsync(harness, campaign, "th-1", Noon.AddMinutes(-5));

        Assert.Equal(1, await harness.RespondAsync(Ct));

        await using var db = harness.Open();
        var made = await db.WorkItems.ToListAsync(Ct);
        Assert.Equal(2, made.Count);
        Assert.All(made, item => Assert.Null(item.IdempotencyKey));
        Assert.All(made, item => Assert.Null(item.IdempotencyDigest));
    }

    /// <summary>The same line wakes the manager too; the responder is queued first and claimed first, once its read has ended.</summary>
    [Fact]
    [Critical(Family.M1, "a reply wakes the responder")]
    public async Task Responder_ManagerStillSummonedBySameLine()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness);
        RoutedTo(harness, Reader);
        await LineAsync(harness, campaign, "th-1", Noon.AddMinutes(-5));
        harness.Status.State = DispatcherState.Draining;

        var report = await harness.Runner.ScanOnceAsync(Ct);
        Assert.Equal((1, 1), (report.Responded, report.Summoned));

        await EndAsync(harness, Responder.ReadOperation, WorkItemStatus.Succeeded);
        harness.Status.State = DispatcherState.Running;
        Assert.Equal(1, await harness.ResumeAsync(Ct));
        var claimed = await harness.ClaimAsync(Ct);

        await using var db = harness.Open();
        var responder = await db.WorkItems.SingleAsync(item => item.Role == Responder.Role, Ct);
        var checkIn = await db.WorkItems.SingleAsync(item => item.Role == ManagerCheckIn.Role, Ct);
        Assert.True(responder.Id < checkIn.Id);
        Assert.Equal([responder.PublicId], claimed.Select(work => work.WorkItemPublicId));
    }

    /// <summary>
    /// A campaign's read of its conversations is never claimed behind its responders: it sits ten above them, however
    /// the responders' priority is set, so detection goes on while replies are answered and the sends wait.
    /// </summary>
    [Theory]
    [InlineData(50)]
    [InlineData(900)]
    public async Task Inbound_ReadIsClaimedBeforeTheCampaignsResponders(int responderPriority)
    {
        using var harness = new DispatchHarness(Noon);
        harness.Responder.CurrentValue = new ResponderOptions { Priority = responderPriority };
        int campaignId;
        await using (var db = harness.Open())
        {
            var campaign = WorkItemFactory.NewCampaign(now: Noon.AddDays(-1));
            campaign.InboundWatermark = Noon.AddHours(-2);
            db.Campaigns.Add(campaign);
            db.ExternalIds.Add(new ExternalId
            {
                Campaign = campaign,
                PluginId = Reader,
                Kind = "campaign",
                Value = "seq-1",
                RecordedAt = Noon.AddDays(-1),
                RecordedByAttemptId = "att_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
            });

            // An older responder already queued in the same campaign, at the responders' priority.
            db.WorkItems.Add(WorkItemFactory.NewAiRole(campaign, role: Responder.Role, now: Noon.AddMinutes(-30), configure: item =>
            {
                item.Priority = responderPriority;
                item.CreatedByType = ActorType.System;
                item.CreatedById = "dispatcher";
            }));
            await db.SaveChangesAsync(Ct);
            campaignId = campaign.Id;
        }

        Load(harness, TestPlugins.Loaded(Reader, operations: ["conversation.list", Responder.ReadOperation]));
        RouteAll(harness, Reader);
        Assert.Equal(1, (await harness.SyncAsync(Ct)).Synced);

        var claimed = await harness.ClaimAsync(Ct);

        await using var read = harness.Open();
        var list = await read.WorkItems.SingleAsync(item => item.Operation == InboundSync.Operation, Ct);
        Assert.Equal(responderPriority + 10, list.Priority);
        Assert.Equal([list.PublicId], claimed.Select(work => work.WorkItemPublicId));
    }

    private static void Readable(DispatchHarness harness)
    {
        Load(harness, TestPlugins.Loaded(Reader, operations: ["conversation.list", Responder.ReadOperation]));
        RouteAll(harness, Reader);
    }

    private static void RoutedTo(DispatchHarness harness, string plugin)
    {
        Load(harness, TestPlugins.Loaded(plugin, operations: ["conversation.list", Responder.ReadOperation]));
        RouteAll(harness, plugin);
    }

    private static async Task<int> CampaignAsync(DispatchHarness harness, CampaignStatus status = CampaignStatus.Active)
    {
        await using var db = harness.Open();
        var campaign = WorkItemFactory.NewCampaign(status: status, now: Noon.AddDays(-1));
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(Ct);
        return campaign.Id;
    }

    /// <summary>
    /// One line saying a thread moved, as the inbound read says it or, with <paramref name="events"/>, as the events store
    /// does: the same fields under the same key, and the store's two more. Answers the line's public id.
    /// </summary>
    private static async Task<string> LineAsync(
        DispatchHarness harness,
        int campaignId,
        string thread,
        DateTime moved,
        string? contact = null,
        string? attributedBy = null,
        bool events = false)
    {
        await using var db = harness.Open();
        var campaign = await db.Campaigns.SingleAsync(c => c.Id == campaignId, Ct);
        var moment = InboundSync.Instant(new DateTimeOffset(moved));
        var said = new JsonObject
        {
            ["plugin"] = Reader,
            ["thread"] = thread,
            ["channel"] = "email",
            ["last_activity_at"] = moment,
            ["unread"] = true,
            ["has_meeting_intent"] = null,
            ["contact_id"] = contact,
        };
        if (events)
        {
            said["event_id"] = PublicId.New("evt");
            said["attributed_by"] = attributedBy;
        }

        var entry = new JournalWriter(harness.Clock).Append(
            db,
            Actors.Dispatcher,
            JournalKinds.InboundReplyObserved,
            campaign,
            key: InboundSync.ObservationKey(Reader, thread, moment),
            updated: said);
        await db.SaveChangesAsync(Ct);
        return entry.PublicId;
    }

    /// <summary>A line of the campaign's own that says no reply; answers its id.</summary>
    private static async Task<int> CampaignLineAsync(DispatchHarness harness, int campaignId)
    {
        await using var db = harness.Open();
        var campaign = await db.Campaigns.SingleAsync(c => c.Id == campaignId, Ct);
        var entry = new JournalWriter(harness.Clock).Append(db, Actors.Runtime, JournalKinds.CampaignCreated, campaign);
        await db.SaveChangesAsync(Ct);
        return entry.Id;
    }

    /// <summary>What SQLite plans for a command the step sent, its parameters bound to whole numbers; one step a line.</summary>
    private static List<string> PlanOf(DispatchHarness harness, string text)
    {
        using var db = harness.Open();
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        connection.Open();
        using var explain = connection.CreateCommand();
        explain.CommandText = "EXPLAIN QUERY PLAN " + text;
        foreach (System.Text.RegularExpressions.Match name in System.Text.RegularExpressions.Regex.Matches(text, @"@\w+"))
        {
            if (!explain.Parameters.Contains(name.Value))
            {
                explain.Parameters.AddWithValue(name.Value, 1);
            }
        }

        using var reader = explain.ExecuteReader();
        var plan = new List<string>();
        while (reader.Read())
        {
            plan.Add(reader.GetString(3));
        }

        return plan;
    }

    private static async Task<int> IdOfAsync(DispatchHarness harness, string line)
    {
        await using var db = harness.Open();
        return await db.Journal.Where(e => e.PublicId == line).Select(e => e.Id).SingleAsync(Ct);
    }

    private static async Task<int> WatermarkAsync(DispatchHarness harness, int campaign)
    {
        await using var db = harness.Open();
        return await db.Campaigns.Where(c => c.Id == campaign).Select(c => c.ResponderWatermark).SingleAsync(Ct);
    }

    /// <summary>Puts the responder in a status as the claim, a run or its end would, its wait closed where it had one.</summary>
    private static async Task StatusAsync(DispatchHarness harness, WorkItemStatus status)
    {
        await using var db = harness.Open();
        var responder = await db.WorkItems.SingleAsync(item => item.Role == Responder.Role, Ct);
        foreach (var wait in await db.WorkItemWaits.Where(w => w.WorkItemId == responder.Id && w.Status == WaitStatus.Open).ToListAsync(Ct))
        {
            wait.Status = status == WorkItemStatus.Waiting ? WaitStatus.Open : WaitStatus.Released;
        }

        responder.Status = status;
        if (WorkItemTransitions.Final.Contains(status))
        {
            responder.FinishedAt = Noon;
        }

        await db.SaveChangesAsync(Ct);
    }

    private static async Task EndAsync(DispatchHarness harness, string operation, WorkItemStatus status)
    {
        await using var db = harness.Open();
        var item = await db.WorkItems.SingleAsync(row => row.Operation == operation, Ct);
        item.Status = status;
        item.FinishedAt = Noon;
        await db.SaveChangesAsync(Ct);
    }
}
