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
            $$$"""{"trigger":"inbound_reply_observed","reply":{"plugin":"reader","thread":"th-1","channel":"email","contact_id":null,"read":"{{{get.PublicId}}}","last_activity_at":"2026-10-06T11:55:00.000Z","moves":[{"line":"{{{line.PublicId}}}","at":"2026-10-06T11:55:00.000Z"}],"move_count":1}}""",
            responder.Context.ToJsonString());

        // Created already waiting on the read: no attempt behind the wait, released when the read ends.
        var wait = await db.WorkItemWaits.Include(w => w.Items).SingleAsync(w => w.WorkItemId == responder.Id, Ct);
        Assert.Equal((WaitStatus.Open, WaitMode.All, (int?)null), (wait.Status, wait.Mode, wait.AttemptId));
        Assert.Equal([get.Id], wait.Items.Select(i => i.ChildWorkItemId));
        Assert.Equal(Noon.AddSeconds(harness.Options.CurrentValue.AiRole.MaxWaitSeconds), wait.Deadline);
        var reason = await db.Journal.Where(e => e.WorkItemId == responder.PublicId && e.Kind == JournalKinds.WorkItemCreated).Select(e => e.Reason).SingleAsync(Ct);
        Assert.Equal("a reply to answer", reason);
    }

    /// <summary>The read ends, the resume step releases the responder, and the claim takes it ahead of ordinary work.</summary>
    [Fact]
    public async Task Responder_WaitingOnItsRead_IsReleasedWhenTheReadEnds()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness);
        RoutedTo(harness, Reader);
        await LineAsync(harness, campaign, "th-1", Noon.AddMinutes(-5));
        Assert.Equal(1, await harness.RespondAsync(Ct));

        Assert.Equal(0, await harness.ResumeAsync(Ct));
        await EndAsync(harness, Responder.ReadOperation, WorkItemStatus.Succeeded);
        Assert.Equal(1, await harness.ResumeAsync(Ct));

        await using var db = harness.Open();
        Assert.Equal(WorkItemStatus.Created, (await db.WorkItems.SingleAsync(item => item.Role == Responder.Role, Ct)).Status);
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
            $$$"""{"trigger":"inbound_reply_observed","reply":{"plugin":"reader","thread":"th-9","channel":"email","contact_id":null,"read":"{{{get.PublicId}}}","last_activity_at":"2026-10-06T11:57:00.000Z","moves":[{"line":"{{{line}}}","at":"2026-10-06T11:57:00.000Z"}],"move_count":1}}""",
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
        var reply = responder.Context["reply"]!;
        Assert.Equal(
            $$"""[{"line":"{{first}}","at":"2026-10-06T11:40:00.000Z"},{"line":"{{second}}","at":"2026-10-06T11:58:00.000Z"}]""",
            reply["moves"]!.ToJsonString());
        Assert.Equal(2, (int)reply["move_count"]!);
        Assert.Equal("2026-10-06T11:58:00.000Z", (string)reply["last_activity_at"]!);

        // Recorded through the path every patch takes, by the dispatcher.
        var patch = await db.Journal.SingleAsync(e => e.WorkItemId == responder.PublicId && e.Kind == JournalKinds.WorkItemContextUpdated, Ct);
        Assert.Equal(("reply", ActorType.System, "dispatcher"), (patch.Key, patch.ActorType, patch.ActorId));
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
        var reply = (await db.WorkItems.SingleAsync(item => item.Role == Responder.Role, Ct)).Context["reply"]!;
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
                item.Context = new JsonObject { ["reply"] = new JsonObject { ["plugin"] = Reader, ["thread"] = "th-1" } };
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
        Assert.Equal(contact, (string?)items.Single(item => item.Role == Responder.Role).Context["reply"]!["contact_id"]);
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
        Assert.Null((string?)responder.Context["reply"]!["read"]);
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
        Assert.Null((string?)responder.Context["reply"]!["read"]);
        Assert.Equal(thread, (string?)responder.Context["reply"]!["thread"]);
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
        Assert.Equal("th-2", (string?)responder.Context["reply"]!["thread"]);
        Assert.Equal(await IdOfAsync(harness, next), await WatermarkAsync(harness, campaign));
        Assert.Equal(1, harness.Logs.All.Count(l => l.StartsWith("The respond step passed over a line of campaign ", StringComparison.Ordinal)));
        Assert.True(await IdOfAsync(harness, poisoned) < await IdOfAsync(harness, next));
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

    /// <summary>A campaign whose chronicle says no reply is moved to the chronicle's last line, and never read again for it.</summary>
    [Fact]
    public async Task Responder_NoLine_MovesTheWatermarkToTheChroniclesLastLine()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness);
        var last = await harness.WriteLineAsync(JournalKinds.CampaignCreated, Ct);

        Assert.Equal(0, await harness.RespondAsync(Ct));
        Assert.Equal(last.Id, await WatermarkAsync(harness, campaign));
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
        Assert.Equal("th-new", (string?)(await db.WorkItems.SingleAsync(item => item.Role == Responder.Role, Ct)).Context["reply"]!["thread"]);
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
