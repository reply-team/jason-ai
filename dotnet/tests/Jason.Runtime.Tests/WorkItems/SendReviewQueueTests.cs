using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Approvals;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.WorkItems;

/// <summary>
/// The critic the runtime creates for a send under an automatic send mode, and the reserved context key that names the send
/// it reviews, <c>send_review</c>: the runtime's alone, refused at create and in every patch, on every item, whoever sends it.
/// </summary>
public class SendReviewQueueTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, bool> Creates() => new()
    {
        { "a send", false },
        { "a send", true },
        { "role work", false },
        { "role work", true },
    };

    [Theory]
    [MemberData(nameof(Creates))]
    public async Task A_create_naming_send_review_is_refused_at_its_field_and_writes_nothing(string work, bool keyed)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        var review = SendReview.Context("wi_01K000000000000000000000AB", "message.send")[SendReview.Key]!.DeepClone();
        var request = work == "a send"
            ? Send(seed) with { Context = new JsonObject { [WorkItemService.InputKey] = Message(), [SendReview.Key] = review } }
            : Role(seed, "critic") with { Context = new JsonObject { [SendReview.Key] = review } };
        request = keyed ? request with { IdempotencyKey = "made-once" } : request;

        var refused = await Assert.ThrowsAsync<ValidationException>(() => Service(db).CreateAsync(request, Ct));

        var detail = Assert.Single(refused.Details!);
        Assert.Equal(("context.send_review", "not_allowed"), (detail.Field, detail.Code));
        db.ChangeTracker.Clear();
        Assert.Empty(await db.WorkItems.AsNoTracking().ToListAsync(Ct));
        Assert.Empty(await db.Journal.AsNoTracking().Where(line => line.Kind == JournalKinds.WorkItemCreated).ToListAsync(Ct));
    }

    public static TheoryData<string, string> Patches() => new()
    {
        { "set", "a send" },
        { "unset", "a send" },
        { "set", "role work" },
        { "unset", "role work" },
    };

    /// <summary>Written by the runtime and never moved: a patch that names the key is refused, whatever it would do.</summary>
    [Theory]
    [MemberData(nameof(Patches))]
    public async Task A_patch_that_names_send_review_is_refused_and_changes_nothing(string patch, string work)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        var made = await Service(db).CreateAsync(work == "a send" ? Send(seed) : Role(seed, "critic"), Ct);

        var refused = await Assert.ThrowsAsync<ValidationException>(() => Service(db).UpdateAsync(Patch(made.Id, patch, new ActorRef(ActorType.Human, "ada")), Ct));

        Assert.Equal(patch == "unset" ? ("unset[0]", "not_allowed") : ("set.send_review", "not_allowed"), (Assert.Single(refused.Details!).Field, refused.Details![0].Code));
        db.ChangeTracker.Clear();
        var stored = await db.WorkItems.AsNoTracking().SingleAsync(w => w.PublicId == made.Id, Ct);
        Assert.False(stored.Context.ContainsKey(SendReview.Key));
        Assert.Empty(await db.Journal.AsNoTracking().Where(line => line.Kind == JournalKinds.WorkItemContextUpdated).ToListAsync(Ct));
    }

    private static WorkItemUpdateRequest Patch(string id, string patch, ActorRef actor) => new(
        id,
        patch == "set" ? SendReview.Context("wi_01K000000000000000000000AB", "message.send") : null,
        patch == "unset" ? [SendReview.Key] : null,
        default, default, default, default, default, default, default, default,
        actor,
        Reason: null);

    private static WorkItemCreateRequest Send(Seed seed) => new(
        seed.Campaign.PublicId, WorkItemKind.ProviderOp, null, "message.send", seed.Contact.PublicId, null, null, null, null, null, null, null,
        new JsonObject { [WorkItemService.InputKey] = Message() }, null, new ActorRef(ActorType.Role, "composer"), null);

    private static WorkItemCreateRequest Role(Seed seed, string role) => new(
        seed.Campaign.PublicId, WorkItemKind.AiRole, role, null, seed.Contact.PublicId, null, null, null, null, null, null, null,
        null, null, new ActorRef(ActorType.Role, "composer"), null);

    private static JsonObject Message() => new()
    {
        ["channel"] = "email",
        ["sender"] = new JsonObject { ["external_id"] = "snd-1", ["address"] = "marta@example.com" },
        ["message"] = new JsonObject { ["format"] = "email", ["subject"] = "Thursday", ["body"] = "Thursday at 10 works." },
    };

    private static WorkItemService Service(JasonDbContext db)
    {
        var clock = new FixedClock(Noon);
        var journal = new JournalWriter(clock);
        return new WorkItemService(db, journal, clock, TestCanceller.New(clock), TestOptions.PluginSettings(), outcomes: new AttemptOutcomes(journal, clock, TestOptions.Settings()));
    }

    private static async Task<Seed> SeedAsync(JasonDbContext db)
    {
        var at = Noon.UtcDateTime;
        var campaign = WorkItemFactory.NewCampaign(now: at);
        var contact = new Contact { PublicId = PublicId.New("cnt"), FirstName = "Ada", CreatedAt = at, UpdatedAt = at };
        contact.Channels.Add(new ContactChannel { Channel = "email", Value = "ada@example.test", IsPrimary = true });
        db.Campaigns.Add(campaign);
        db.Contacts.Add(contact);
        db.CampaignContacts.Add(new CampaignContact { Campaign = campaign, Contact = contact, State = MembershipState.Enrolled, AddedAt = at, UpdatedAt = at });
        await db.SaveChangesAsync(Ct);
        return new Seed(campaign, contact);
    }

    private sealed record Seed(Campaign Campaign, Contact Contact);
}
