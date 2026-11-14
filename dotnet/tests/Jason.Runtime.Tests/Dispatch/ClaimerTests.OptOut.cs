using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Plugins;
using Jason.Runtime.Contacts;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Replies;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.Tests.Replies;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// The opt-out's hard stop at the claim, through the real register and the real lock: what an opt-out verdict registered
/// is refused as <c>suppressed</c>, and a value the register never held — gained after the verdict, or by a contact who
/// had none — is refused as <c>opted_out</c>, in any campaign. Only a person's later verdict on that reply lifts the lock.
/// </summary>
public partial class ClaimerTests
{
    private const string OptOutProvider = "stand-in-provider";

    /// <summary>
    /// A contact warm in the campaign who then asks to be left alone: the send queued for them is refused at the register,
    /// never parked and never released, while warmth still says warm, since once warm is always warm.
    /// </summary>
    [Fact]
    [Critical(Family.S1, "the opt-out's stop refuses a warm contact's send")]
    public async Task A_warm_contact_who_opts_out_is_refused_at_the_register_while_warmth_still_says_warm()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        using var harness = OptOutHarness(db, ["message.send"]);
        var seed = await ReplyLines.SeedAsync(db);
        seed.Contact.Channels.Add(new ContactChannel { Channel = "email", Value = "marta@example.test", IsPrimary = true });
        await db.SaveChangesAsync(Ct);
        await ReplyLines.ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T11:30:00.000Z");
        await ReplyLines.VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-1", "2026-09-14T11:30:00.000Z", ReplyNature.Person);
        await ReplyLines.ObservedAsync(db, seed.Campaign, seed.Contact, "thr-1", "2026-09-14T11:45:00.000Z");
        await ReplyLines.VerdictAsync(db, seed.Campaign, seed.Contact, "thr-1", "msg-2", "2026-09-14T11:45:00.000Z", ReplyNature.OptOut);
        var item = Sending(db, seed.Campaign, seed.Contact);
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        Assert.Equal(WarmthState.Warm, await Warmth.OfAsync(fresh, seed.Campaign.Id, seed.Contact.Id, Ct));
        Assert.Equal(AttemptErrors.Suppressed, await RefusalAsync(fresh, item));
        Assert.Empty(await fresh.Approvals.AsNoTracking().ToListAsync(Ct));
        Assert.False(await fresh.Journal.AnyAsync(e => e.Kind == JournalKinds.ApprovalRequested || e.Kind == JournalKinds.ApprovalStandingApplied, Ct));
        Assert.Null((await fresh.Attempts.AsNoTracking().SingleAsync(Ct)).Launch);
    }

    /// <summary>
    /// A contact who had no value when they asked, and one added later, or one who gains a value after: the register holds
    /// nothing for it, and the lock refuses the work by the person — in the campaign that heard them and in another.
    /// </summary>
    [Theory]
    [InlineData("no value at the verdict, one added later", "the same campaign")]
    [InlineData("a value gained after the verdict", "the same campaign")]
    [InlineData("a value gained after the verdict", "another campaign")]
    public async Task A_value_the_register_never_held_is_refused_by_the_lock(string values, string where)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        using var harness = OptOutHarness(db, ["message.send"]);
        var campaign = await OptOutRegisterTests.CampaignAsync(db, "Spring");
        var contact = values.StartsWith("no value", StringComparison.Ordinal)
            ? await OptOutRegisterTests.ContactAsync(db, "cnt_01K52JR0000000000000000001")
            : await OptOutRegisterTests.ContactAsync(db, "cnt_01K52JR0000000000000000001", ("phone", "+15555550100"));
        var verdict = await OptOutRegisterTests.Service(db).RecordAsync(OptOutRegisterTests.Request(campaign, contact, ReplyNature.OptOut), Ct);
        contact.Channels.Add(new ContactChannel { Channel = "email", Value = "marta@example.test", IsPrimary = true });
        var sendingIn = where == "another campaign" ? await OptOutRegisterTests.CampaignAsync(db, "Autumn") : campaign;
        var item = Sending(db, sendingIn, contact);
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        var attempt = await fresh.Attempts.AsNoTracking().SingleAsync(a => a.WorkItemId == item.Id, Ct);
        Assert.Equal((AttemptErrors.OptedOut, FailureClass.Permanent), (attempt.Error!.Code, attempt.Error.Class));
        Assert.Contains(verdict.Id, attempt.Error.Message, StringComparison.Ordinal);
        Assert.False(await fresh.Suppressions.AnyAsync(s => s.Channel == "email", Ct));
        Assert.Empty(await fresh.Approvals.AsNoTracking().ToListAsync(Ct));
    }

    /// <summary>
    /// A send parked for a person's approval before the opt-out stays parked, and when the person approves it after the
    /// verdict, its claim refuses it: nothing cancels it at the verdict, and nothing more is needed.
    /// </summary>
    [Fact]
    public async Task A_send_parked_before_the_opt_out_and_approved_after_it_is_refused_at_its_claim()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        using var harness = OptOutHarness(db, ["message.send"]);
        var campaign = await OptOutRegisterTests.CampaignAsync(db, "Spring");
        var contact = await OptOutRegisterTests.ContactAsync(db, "cnt_01K52JR0000000000000000001", ("email", "marta@example.test"));
        var item = Sending(db, campaign, contact);
        await db.SaveChangesAsync(Ct);
        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));
        Assert.Equal(WorkItemStatus.AwaitingApproval, item.Status);

        await OptOutRegisterTests.Service(db).RecordAsync(OptOutRegisterTests.Request(campaign, contact, ReplyNature.OptOut), Ct);
        var parked = await db.WorkItems.AsNoTracking().SingleAsync(w => w.Id == item.Id, Ct);
        await Decide(db, ApprovalStatus.Approved);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        Assert.Equal(WorkItemStatus.AwaitingApproval, parked.Status);
        Assert.Equal(AttemptErrors.Suppressed, await RefusalAsync(fresh, item));
    }

    /// <summary>
    /// The verdict recorded by a responder's run about one person that names another stops the one it names: their value
    /// gained later is refused by the lock, and the run's own person is still reached.
    /// </summary>
    [Fact]
    public async Task An_opt_out_naming_another_person_than_the_run_s_locks_the_one_named_and_leaves_the_run_s_person_open()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        using var harness = OptOutHarness(db, ["list_membership.add"]);
        var campaign = await OptOutRegisterTests.CampaignAsync(db, "Spring");
        var aboutA = await OptOutRegisterTests.ContactAsync(db, "cnt_01K52JR000000000000000000A", ("email", "anna@example.test"));
        var namedB = await OptOutRegisterTests.ContactAsync(db, "cnt_01K52JR000000000000000000B", ("phone", "+15555550111"));
        var (run, attempt) = await OptOutRegisterTests.RunAsync(db, campaign, aboutA);
        await OptOutRegisterTests.Service(db).RecordAsync(
            OptOutRegisterTests.Request(campaign, namedB, ReplyNature.OptOut) with { Actor = new ActorRef(ActorType.Attempt, attempt.PublicId) },
            Ct);
        await EndedAsync(db, run, attempt);
        namedB.Channels.Add(new ContactChannel { Channel = "email", Value = "boris@example.test", IsPrimary = true });
        var toB = Adding(db, campaign, namedB);
        var toA = Adding(db, campaign, aboutA);
        await db.SaveChangesAsync(Ct);

        var claimed = await harness.Claimer.ClaimAsync(db, 4, Ct);
        claimed = [.. claimed, .. await harness.Claimer.ClaimAsync(db, 4, Ct)];

        await using var fresh = database.Open();
        Assert.Equal(AttemptErrors.OptedOut, await RefusalAsync(fresh, toB));
        Assert.Equal(toA.PublicId, Assert.Single(claimed).WorkItemPublicId);
    }

    /// <summary>
    /// Who lifts the lock: only a person's later verdict on the same reply. A role's corrected verdict leaves it; a person's,
    /// recorded outside any run, lifts it, and leaves the register's entries where they are; taking an entry off the
    /// register lifts that entry and not the lock; a verdict on another reply lifts nothing.
    /// </summary>
    [Theory]
    [InlineData("a role corrects it", AttemptErrors.OptedOut)]
    [InlineData("a person corrects it outside a run", null)]
    [InlineData("a person corrects another reply", AttemptErrors.OptedOut)]
    [InlineData("the entry is taken off the register", AttemptErrors.OptedOut)]
    public async Task Only_a_person_s_later_verdict_on_that_reply_lifts_the_lock(string after, string? refused)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        using var harness = OptOutHarness(db, ["list_membership.add"]);
        var campaign = await OptOutRegisterTests.CampaignAsync(db, "Spring");
        var contact = await OptOutRegisterTests.ContactAsync(db, "cnt_01K52JR0000000000000000001", ("phone", "+15555550100"));
        var (run, attempt) = await OptOutRegisterTests.RunAsync(db, campaign, contact);
        var byTheRun = new ActorRef(ActorType.Attempt, attempt.PublicId);
        await OptOutRegisterTests.Service(db).RecordAsync(OptOutRegisterTests.Request(campaign, contact, ReplyNature.OptOut) with { Actor = byTheRun }, Ct);
        var corrected = OptOutRegisterTests.Request(campaign, contact, ReplyNature.Person);
        switch (after)
        {
            case "a role corrects it":
                await OptOutRegisterTests.Service(db).RecordAsync(corrected with { Actor = new ActorRef(ActorType.Role, "manager") }, Ct);
                await OptOutRegisterTests.Service(db).RecordAsync(corrected with { Actor = byTheRun }, Ct);
                break;
            case "a person corrects it outside a run":
                await OptOutRegisterTests.Service(db).RecordAsync(corrected, Ct);
                break;
            case "a person corrects another reply":
                await OptOutRegisterTests.Service(db).RecordAsync(corrected with { MessageId = "msg-78" }, Ct);
                break;
            case "the entry is taken off the register":
                var clock = new FixedClock(new DateTimeOffset(Noon));
                await new SuppressionService(db, new JournalWriter(clock), clock).RemoveAsync(
                    new SuppressionRemoveRequest("phone", "+15555550100", new ActorRef(ActorType.Human, "ada"), "entered by mistake"),
                    Ct);
                break;
        }

        await EndedAsync(db, run, attempt);
        contact.Channels.Add(new ContactChannel { Channel = "email", Value = "marta@example.test", IsPrimary = true });
        var toTheNewValue = Adding(db, campaign, contact);
        await db.SaveChangesAsync(Ct);

        var claimed = await harness.Claimer.ClaimAsync(db, 4, Ct);

        await using var fresh = database.Open();
        if (refused is null)
        {
            Assert.Equal(toTheNewValue.PublicId, Assert.Single(claimed).WorkItemPublicId);
            Assert.True(await fresh.Suppressions.AnyAsync(s => s.Channel == "phone" && s.Value == "+15555550100", Ct));
        }
        else
        {
            Assert.Empty(claimed);
            Assert.Equal(refused, await RefusalAsync(fresh, toTheNewValue));
        }
    }

    /// <summary>A claim with the real register and the real lock, asked of the test's own database.</summary>
    private static Harness OptOutHarness(JasonDbContext db, string[] operations)
    {
        var clock = new FixedClock(new DateTimeOffset(Noon));
        return new Harness(
            plugin: TestPlugins.Loaded(OptOutProvider, operations),
            route: new Route(OptOutProvider, null, null),
            register: new SuppressionService(db, new JournalWriter(clock), clock));
    }

    /// <summary>A send to the contact by email, as a person or a role queues one.</summary>
    private static WorkItem Sending(JasonDbContext db, Campaign campaign, Contact contact)
    {
        var item = WorkItemFactory.NewProviderOp(campaign, "message.send", Noon);
        item.Contact = contact;
        item.Context = new JsonObject
        {
            [WorkItemService.InputKey] = new JsonObject
            {
                ["channel"] = "email",
                ["sender"] = new JsonObject { ["external_id"] = "snd-1", ["address"] = "sales@example.com" },
                ["message"] = new JsonObject { ["format"] = "email", ["subject"] = "Hello", ["body"] = "Hello again." },
            },
        };
        db.WorkItems.Add(item);
        return item;
    }

    /// <summary>An add of the contact to a provider's list by email: what the register covers, and needs nobody's approval.</summary>
    private static WorkItem Adding(JasonDbContext db, Campaign campaign, Contact contact)
    {
        var item = WorkItemFactory.NewProviderOp(campaign, "list_membership.add", Noon);
        item.Contact = contact;
        item.Context = new JsonObject
        {
            [WorkItemService.InputKey] = new JsonObject { ["list"] = new JsonObject { ["external_id"] = "9" }, ["channel"] = "email" },
        };
        db.WorkItems.Add(item);
        return item;
    }

    /// <summary>The run that recorded the verdicts ends, so the campaign's queue is the claim's again.</summary>
    private static async Task EndedAsync(JasonDbContext db, WorkItem run, Attempt attempt)
    {
        run.Status = WorkItemStatus.Succeeded;
        run.FinishedAt = Noon;
        attempt.Status = AttemptStatus.Succeeded;
        attempt.FinishedAt = Noon;
        await db.SaveChangesAsync(Ct);
    }

    private static async Task<string?> RefusalAsync(JasonDbContext db, WorkItem item)
    {
        var row = await db.WorkItems.AsNoTracking().SingleAsync(w => w.Id == item.Id, Ct);
        Assert.Equal(WorkItemStatus.Failed, row.Status);
        return (await db.Attempts.AsNoTracking().SingleAsync(a => a.WorkItemId == item.Id, Ct)).Error!.Code;
    }
}
