using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Json;
using Jason.Runtime.Api;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.WorkItems;

/// <summary>
/// Inside a run a send a person decides each one of is created under the run's own name: the request's
/// <c>Jason-Attempt</c> header names the attempt it comes from, and a create of such a send that names anybody else — a
/// person, another attempt — is refused <c>created_as_another_inside_a_run</c>, so a run cannot make its send under
/// another name and then review it as itself. Work that is no such send is not this rule's, and outside a run nothing
/// changes.
/// </summary>
public class SendCreatedInsideARunTests
{
    private static readonly DateTime Noon = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("a person", false)]
    [InlineData("another attempt", false)]
    [InlineData("nobody", false)]
    [InlineData("the run itself", true)]
    public async Task Inside_a_run_a_send_is_created_under_the_run_s_own_name(string named, bool accepted)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        ActorRef? actor = named switch
        {
            "a person" => new ActorRef(ActorType.Human, "x"),
            "another attempt" => Run(seed.Other),
            "nobody" => null,
            _ => Run(seed.Run),
        };

        var request = Send(seed, actor);

        if (accepted)
        {
            var made = await Service(db, seed.Run).CreateAsync(request, Ct);
            Assert.Equal(Run(seed.Run), made.CreatedBy);
            return;
        }

        var refused = await Assert.ThrowsAsync<ForbiddenException>(() => Service(db, seed.Run).CreateAsync(request, Ct));
        Assert.Equal("created_as_another_inside_a_run", refused.Code);
        Assert.Equal(
            $"This request comes from inside attempt `{seed.Run.PublicId}`. A send to a person is created there under that attempt's own name, so that a review can tell who wrote it: name attempt:{seed.Run.PublicId} as the actor, which the CLI does when --actor is left out.",
            refused.Message);
        Assert.Empty(await db.WorkItems.AsNoTracking().Where(w => w.Kind == WorkItemKind.ProviderOp).ToListAsync(Ct));
    }

    /// <summary>A read is no send a review covers: inside a run it may still name whoever the caller names.</summary>
    [Fact]
    public async Task Inside_a_run_work_that_is_no_such_send_is_not_this_rule_s()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        var read = new WorkItemCreateRequest(
            seed.Campaign.PublicId, WorkItemKind.ProviderOp, null, "conversation.get", null, null, null, null, null, null, null, null,
            new JsonObject { [WorkItemService.InputKey] = new JsonObject { ["thread"] = new JsonObject { ["external_id"] = "501" }, ["limit"] = 20, ["offset"] = 0 } },
            null, new ActorRef(ActorType.Human, "x"), null);

        var made = await Service(db, seed.Run).CreateAsync(read, Ct);

        Assert.Equal(new ActorRef(ActorType.Human, "x"), made.CreatedBy);
    }

    /// <summary>Outside a run a send names whoever creates it, as before.</summary>
    [Fact]
    public async Task Outside_a_run_a_send_names_whoever_creates_it()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);

        var made = await Service(db, run: null).CreateAsync(Send(seed, new ActorRef(ActorType.Human, "x")), Ct);

        Assert.Equal(new ActorRef(ActorType.Human, "x"), made.CreatedBy);
    }

    /// <summary>
    /// The same door on an edit: inside a run, a patch that writes a send's <c>input</c> is the run's own, since whoever
    /// writes the text is one of its authors and a review is told apart from them. Named as anybody else — a person,
    /// another attempt, nobody — it is refused <c>updated_as_another_inside_a_run</c> and nothing is written; a patch that
    /// leaves the input alone is not this rule's.
    /// </summary>
    [Theory]
    [InlineData("a person", true, false)]
    [InlineData("another attempt", true, false)]
    [InlineData("nobody", true, false)]
    [InlineData("the run itself", true, true)]
    [InlineData("a person", false, true)]
    public async Task Inside_a_run_a_send_s_input_is_written_under_the_run_s_own_name(string named, bool input, bool accepted)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        var send = await Service(db, run: null).CreateAsync(Send(seed, Run(seed.Run)), Ct);
        ActorRef? actor = named switch
        {
            "a person" => new ActorRef(ActorType.Human, "x"),
            "another attempt" => Run(seed.Other),
            "nobody" => null,
            _ => Run(seed.Run),
        };
        var patch = new WorkItemUpdateRequest(
            send.Id,
            input ? new JsonObject { [WorkItemService.InputKey] = Input("Friday at 10 works.") } : null,
            null,
            default,
            default,
            input ? default : Optional<int>.Of(10),
            default,
            default,
            default,
            default,
            default,
            actor,
            Reason: null);

        if (accepted)
        {
            await Service(db, seed.Run).UpdateAsync(patch, Ct);
            return;
        }

        var refused = await Assert.ThrowsAsync<ForbiddenException>(() => Service(db, seed.Run).UpdateAsync(patch, Ct));
        Assert.Equal("updated_as_another_inside_a_run", refused.Code);
        Assert.Equal(
            $"This request comes from inside attempt `{seed.Run.PublicId}`. A send to a person has its input written there under that attempt's own name, so that a review can tell who wrote it: name attempt:{seed.Run.PublicId} as the actor, which the CLI does when --actor is left out.",
            refused.Message);
        await using var fresh = database.Open();
        Assert.Empty(await fresh.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.WorkItemContextUpdated).ToListAsync(Ct));
    }

    /// <summary>Taking a send's input away is writing it too: inside a run, an unset of it under another name is refused.</summary>
    [Fact]
    public async Task Inside_a_run_an_unset_of_a_send_s_input_under_another_name_is_refused()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        var send = await Service(db, run: null).CreateAsync(Send(seed, Run(seed.Run)), Ct);

        var refused = await Assert.ThrowsAsync<ForbiddenException>(() => Service(db, seed.Run).UpdateAsync(
            new WorkItemUpdateRequest(
                send.Id, null, [WorkItemService.InputKey],
                default, default, default, default, default, default, default, default,
                new ActorRef(ActorType.Human, "x"), Reason: null),
            Ct));

        Assert.Equal("updated_as_another_inside_a_run", refused.Code);
    }

    /// <summary>Work that is no send to a person is not this rule's: inside a run a role's work is patched under any name.</summary>
    [Fact]
    public async Task Inside_a_run_a_patch_of_role_work_names_whoever_the_caller_names()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        var work = WorkItemFactory.NewAiRole(seed.Campaign, "researcher", Noon);
        db.WorkItems.Add(work);
        await db.SaveChangesAsync(Ct);

        await Service(db, seed.Run).UpdateAsync(
            new WorkItemUpdateRequest(
                work.PublicId, new JsonObject { [WorkItemService.InputKey] = new JsonObject { ["topic"] = "pricing" } }, null,
                default, default, default, default, default, default, default, default,
                new ActorRef(ActorType.Human, "x"), Reason: null),
            Ct);

        var line = await db.Journal.AsNoTracking().SingleAsync(e => e.Kind == JournalKinds.WorkItemContextUpdated, Ct);
        Assert.Equal((ActorType.Human, "x"), (line.ActorType, line.ActorId));
    }

    /// <summary>Outside a run a send's input is written by whoever the patch names, as before.</summary>
    [Fact]
    public async Task Outside_a_run_a_send_s_input_is_written_by_whoever_the_patch_names()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        var send = await Service(db, run: null).CreateAsync(Send(seed, Run(seed.Run)), Ct);

        await Service(db, run: null).UpdateAsync(
            new WorkItemUpdateRequest(
                send.Id, new JsonObject { [WorkItemService.InputKey] = Input("Friday at 10 works.") }, null,
                default, default, default, default, default, default, default, default,
                new ActorRef(ActorType.Human, "x"), Reason: null),
            Ct);

        var line = await db.Journal.AsNoTracking().SingleAsync(e => e.Kind == JournalKinds.WorkItemContextUpdated, Ct);
        Assert.Equal((ActorType.Human, "x"), (line.ActorType, line.ActorId));
    }

    private static ActorRef Run(Attempt attempt) => new(ActorType.Attempt, attempt.PublicId);

    private static WorkItemCreateRequest Send(Seed seed, ActorRef? actor) =>
        new(
            seed.Campaign.PublicId, WorkItemKind.ProviderOp, null, "message.send", seed.Contact.PublicId, null, null, null, null, null, null, null,
            new JsonObject { [WorkItemService.InputKey] = Input("Thursday at 10 works.") },
            null, actor, null);

    private static JsonObject Input(string body) => new()
    {
        ["channel"] = "email",
        ["sender"] = new JsonObject { ["external_id"] = "snd-1", ["address"] = "marta@example.com" },
        ["message"] = new JsonObject { ["format"] = "email", ["subject"] = "Thursday", ["body"] = body },
    };

    private static WorkItemService Service(JasonDbContext db, Attempt? run)
    {
        var clock = new FixedClock(new DateTimeOffset(Noon));
        return new WorkItemService(
            db,
            new JournalWriter(clock),
            clock,
            TestCanceller.New(clock),
            TestOptions.PluginSettings(),
            run: run is null ? null : new RequestRun { AttemptId = run.PublicId });
    }

    /// <summary>A campaign and its person, and two running attempts of the campaign's role work: the run, and another.</summary>
    private static async Task<Seed> SeedAsync(JasonDbContext db)
    {
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var contact = new Contact { PublicId = PublicId.New("cnt"), FirstName = "Ada", CreatedAt = Noon, UpdatedAt = Noon };
        contact.Channels.Add(new ContactChannel { Channel = "email", Value = "ada@example.test", IsPrimary = true });
        db.Campaigns.Add(campaign);
        db.Contacts.Add(contact);
        db.CampaignContacts.Add(new CampaignContact { Campaign = campaign, Contact = contact, State = MembershipState.Enrolled, AddedAt = Noon, UpdatedAt = Noon });
        var run = Running(db, WorkItemFactory.NewAiRole(campaign, "composer", Noon));
        var other = Running(db, WorkItemFactory.NewAiRole(campaign, "reviewer", Noon));
        await db.SaveChangesAsync(Ct);
        return new Seed(campaign, contact, run, other);
    }

    private static Attempt Running(JasonDbContext db, WorkItem work)
    {
        work.Status = WorkItemStatus.Processing;
        var attempt = WorkItemFactory.NewAttempt(work, 1, AttemptStatus.Running, Noon);
        db.WorkItems.Add(work);
        return attempt;
    }

    private sealed record Seed(Campaign Campaign, Contact Contact, Attempt Run, Attempt Other);
}
