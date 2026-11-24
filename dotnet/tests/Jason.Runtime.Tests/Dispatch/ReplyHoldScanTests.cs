using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Approvals;
using Jason.Runtime.Contacts;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.Tests.Routing;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Jason.Runtime.Tests.Dispatch.InboundSyncHarvestTests;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// A reply stops the line at once, through a whole scan and the claims after it: a letter waiting for its
/// <c>not_before</c> and a letter parked for the person's approval do not go, though the moment comes and the person
/// approves, and the letter the provider already holds for later is taken back by one cancel; once the line is continued,
/// both letters go.
/// </summary>
public class ReplyHoldScanTests
{
    private const string Mailer = "holding-mailer";

    private static readonly DateTime Noon = new(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_reply_holds_both_waiting_letters_takes_back_the_held_one_and_continuing_lets_them_go()
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, TestPlugins.Loaded(Mailer, operations: ["message.send", "message.cancel", "message.status", "conversation.list", Responder.ReadOperation]));
        RouteAll(harness, Mailer);
        int campaign;
        int person;
        string personId;
        string campaignId;
        string held;
        await using (var db = harness.Open())
        {
            var owner = WorkItemFactory.NewCampaign(now: Noon.AddDays(-7));
            var contact = new Contact { PublicId = PublicId.New("cnt"), FirstName = "Ines", CreatedAt = Noon.AddDays(-7), UpdatedAt = Noon.AddDays(-7) };
            contact.Channels.Add(new ContactChannel { Channel = "email", Value = "ines@example.test", IsPrimary = true });
            db.Campaigns.Add(owner);
            db.Contacts.Add(contact);
            db.CampaignContacts.Add(new CampaignContact { Campaign = owner, Contact = contact, State = MembershipState.Enrolled, AddedAt = Noon.AddDays(-7), UpdatedAt = Noon.AddDays(-7) });
            await db.SaveChangesAsync(Ct);
            (campaign, person, personId, campaignId) = (owner.Id, contact.Id, contact.PublicId, owner.PublicId);
            held = await HeldSendsCancelTests.SentAsync(db, owner, person, HeldSendsCancelTests.Moment(Noon.AddDays(2)));
        }

        // The approval-parked letter: the claim parks it for the person, as it parks every letter of a cold person.
        var parked = await LetterAsync(harness, campaign, person, notBefore: null);
        await ClaimAllAsync(harness);
        Assert.Equal(WorkItemStatus.AwaitingApproval, (await harness.ReadItemAsync(parked, Ct)).Status);

        // The letter waiting for its moment, an hour from now.
        var later = await LetterAsync(harness, campaign, person, notBefore: Noon.AddHours(1));

        // The reply, and one scan of every step, claims aside.
        await HeldSendsCancelTests.ReplyLineAsync(harness, campaign, person, "th-1", Noon.AddMinutes(-5));
        harness.Status.State = DispatcherState.Draining;
        var report = await harness.Runner.ScanOnceAsync(Ct);
        harness.Status.State = DispatcherState.Running;
        Assert.Equal(1, report.Responded);

        await using (var db = harness.Open())
        {
            Assert.Equal(MembershipState.Paused, await db.CampaignContacts.Where(m => m.CampaignId == campaign && m.ContactId == person).Select(m => m.State).SingleAsync(Ct));
            var cancel = Assert.Single(await db.WorkItems.AsNoTracking().Where(w => w.Operation == HeldSends.Cancel).ToListAsync(Ct));
            Assert.Equal(held, (string?)cancel.Context["input"]!["send"]);
        }

        // The person approves the parked letter, and the other letter's moment comes: neither goes while the line is held,
        // whatever else of the campaign is claimed meanwhile.
        await ApproveAsync(harness, parked);
        harness.Clock.Advance(TimeSpan.FromHours(2));
        var taken = await ClaimAllAsync(harness);

        Assert.DoesNotContain(parked, taken);
        Assert.DoesNotContain(later, taken);
        Assert.Equal(WorkItemStatus.Created, (await harness.ReadItemAsync(parked, Ct)).Status);
        Assert.Equal(WorkItemStatus.Created, (await harness.ReadItemAsync(later, Ct)).Status);
        await using (var db = harness.Open())
        {
            Assert.Equal(1, await db.WorkItems.CountAsync(w => w.Operation == HeldSends.Cancel, Ct));
            Assert.False(await db.Attempts.AnyAsync(a => a.WorkItem!.PublicId == later, Ct));
        }

        // The manager continues the line: the approved letter goes, and the one whose moment came is taken to the person.
        await using (var db = harness.Open())
        {
            var memberships = new MembershipService(db, new JournalWriter(harness.Clock), harness.Clock, TestCanceller.New(harness.Clock));
            await memberships.ContinueAsync(new ContinueContactsRequest(campaignId, [personId], new ActorRef(ActorType.Role, "manager"), "An answer asking to hear more."), Ct);
        }

        var after = await ClaimAllAsync(harness);

        Assert.Contains(parked, after);
        Assert.Equal(WorkItemStatus.AwaitingApproval, (await harness.ReadItemAsync(later, Ct)).Status);
    }

    /// <summary>
    /// Claims until nothing of any campaign is a candidate, ending each claimed item as succeeded at once, as a run that
    /// finished would, so the next item of its campaign may be claimed; answers the items it claimed.
    /// </summary>
    private static async Task<List<string>> ClaimAllAsync(DispatchHarness harness)
    {
        var taken = new List<string>();
        for (var turn = 0; turn < 20; turn++)
        {
            await using (var db = harness.Open())
            {
                if ((await Claimer.CandidatesAsync(db, harness.Clock.GetUtcNow().UtcDateTime, freeSlots: 100, Ct)).Count == 0)
                {
                    return taken;
                }
            }

            var claimed = await harness.ClaimAsync(Ct);
            await using var write = harness.Open();
            foreach (var work in claimed)
            {
                taken.Add(work.WorkItemPublicId);
                var item = await write.WorkItems.SingleAsync(w => w.PublicId == work.WorkItemPublicId, Ct);
                item.Status = WorkItemStatus.Succeeded;
                item.FinishedAt = harness.Clock.GetUtcNow().UtcDateTime;
            }

            await write.SaveChangesAsync(Ct);
        }

        throw new InvalidOperationException("The claims did not come to an end.");
    }

    /// <summary>A letter to the person, through the routine every caller creates work through.</summary>
    private static async Task<string> LetterAsync(DispatchHarness harness, int campaign, int person, DateTime? notBefore)
    {
        using var scope = harness.Scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        var owner = await db.Campaigns.AsNoTracking().SingleAsync(c => c.Id == campaign, Ct);
        var contact = await db.Contacts.AsNoTracking().Where(c => c.Id == person).Select(c => c.PublicId).SingleAsync(Ct);
        var item = await scope.ServiceProvider.GetRequiredService<WorkItemService>().CreateCoreAsync(
            new WorkItemCreateRequest(
                owner.PublicId,
                WorkItemKind.ProviderOp,
                Role: null,
                "message.send",
                contact,
                ExecutionProfile: null,
                Priority: 10,
                notBefore is { } at ? new DateTimeOffset(at) : null,
                DueAt: null,
                TimeoutSeconds: null,
                HeartbeatSeconds: null,
                MaxAttempts: null,
                new JsonObject
                {
                    [WorkItemService.InputKey] = new JsonObject
                    {
                        ["channel"] = "email",
                        ["sender"] = new JsonObject { ["external_id"] = "snd-1", ["address"] = "owner@example.test" },
                        ["message"] = new JsonObject { ["format"] = "email", ["subject"] = "Following up", ["body"] = "Ines, a short follow-up." },
                    },
                },
                ResultFormat: null,
                Actor: null,
                Reason: null),
            new ActorRef(ActorType.Role, "manager"),
            LineageRecord.Root,
            Ct);
        return item.PublicId;
    }

    private static async Task ApproveAsync(DispatchHarness harness, string letter)
    {
        await using var db = harness.Open();
        var approval = await db.Approvals.Where(a => a.WorkItem!.PublicId == letter).Select(a => a.PublicId).SingleAsync(Ct);
        await new ApprovalService(db, new JournalWriter(harness.Clock), harness.Clock).ApproveAsync(
            new ApprovalDecisionRequest(approval, new ActorRef(ActorType.Human, "operator"), "reads well"),
            Ct);
    }
}
