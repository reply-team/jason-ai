using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Approvals;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;

namespace Jason.Runtime.Tests.WorkItems;

/// <summary>
/// <c>context.reply_verdict</c> names the verdict a drafted reply answers. It is held where work is created and where a
/// patch names it or the arguments: it belongs to a reply or a message, and names a verdict of the same campaign about the
/// same thread — or, for a message, the same person. Whether that verdict can release the draft is the claim's question,
/// asked later; this one is only whether the reference is true.
/// </summary>
public class ReplyVerdictReferenceTests
{
    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A reference that is true is accepted, whoever wrote the verdict and whoever drafts the reply.</summary>
    [Theory]
    [InlineData("conversation.reply")]
    [InlineData("message.send")]
    public async Task A_true_reference_is_accepted(string operation)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SceneAsync(db);

        var item = await Service(db).CreateAsync(Create(scene, operation, scene.Verdict), Ct);

        Assert.Equal(scene.Verdict, (string?)item.Context[StandingApprovals.VerdictKey]);
    }

    /// <summary>
    /// A forged reference is refused at create, at <c>context.reply_verdict</c>: a verdict of another campaign, a line of
    /// another kind, an id no line has, a verdict about another thread, another person for a message, or a reference on
    /// work that is no reply.
    /// </summary>
    [Theory]
    [InlineData("another campaign's verdict", "invalid")]
    [InlineData("a line of another kind", "invalid")]
    [InlineData("an id no line has", "unknown")]
    [InlineData("not a string", "invalid")]
    [InlineData("a verdict about another thread", "invalid")]
    [InlineData("a message to another person", "invalid")]
    [InlineData("a message under a verdict that names nobody", "invalid")]
    [InlineData("another operation", "invalid")]
    [InlineData("a role's work", "invalid")]
    public async Task ReplyVerdict_Forged_RefusedAtCreate(string forgery, string code)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SceneAsync(db);
        var request = forgery switch
        {
            "another campaign's verdict" => Create(scene, "conversation.reply", scene.Elsewhere),
            "a line of another kind" => Create(scene, "conversation.reply", scene.Other),
            "an id no line has" => Create(scene, "conversation.reply", PublicId.New("jrn")),
            "not a string" => Create(scene, "conversation.reply", null) with
            {
                Context = new JsonObject { [WorkItemService.InputKey] = Replying("501"), [StandingApprovals.VerdictKey] = 7 },
            },
            "a verdict about another thread" => Create(scene, "conversation.reply", scene.Verdict) with
            {
                Context = new JsonObject { [WorkItemService.InputKey] = Replying("502"), [StandingApprovals.VerdictKey] = scene.Verdict },
            },
            "a message to another person" => Create(scene, "message.send", scene.Verdict) with { ContactId = scene.Stranger.PublicId },
            "a message under a verdict that names nobody" => Create(scene, "message.send", scene.Nameless),
            "another operation" => Create(scene, "campaign.enroll", scene.Verdict),
            "a role's work" => Create(scene, "conversation.reply", scene.Verdict) with { Kind = WorkItemKind.AiRole, Operation = null, Role = "responder" },
            _ => throw new ArgumentOutOfRangeException(nameof(forgery), forgery, "no such forgery"),
        };

        var refused = await Assert.ThrowsAsync<ValidationException>(() => Service(db).CreateAsync(request, Ct));

        Assert.Equal(["context.reply_verdict/" + code], refused.Details!.Select(detail => $"{detail.Field}/{detail.Code}"));
    }

    /// <summary>
    /// A patch that names the reference, or the arguments it is measured against, is held to it as the create is: what the
    /// work would carry after the patch is what is checked. A patch that names neither is not asked about it.
    /// </summary>
    [Theory]
    [InlineData("the reference, to another campaign's verdict", true)]
    [InlineData("the reference, to an id no line has", true)]
    [InlineData("the arguments, to another thread", true)]
    [InlineData("the reference, to a true one", false)]
    [InlineData("neither", false)]
    public async Task ReplyVerdict_Forged_RefusedAtUpdate(string patched, bool refused)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var scene = await SceneAsync(db);
        var item = await Service(db).CreateAsync(Create(scene, "conversation.reply", scene.Verdict), Ct);
        var set = patched switch
        {
            "the reference, to another campaign's verdict" => new JsonObject { [StandingApprovals.VerdictKey] = scene.Elsewhere },
            "the reference, to an id no line has" => new JsonObject { [StandingApprovals.VerdictKey] = PublicId.New("jrn") },
            "the arguments, to another thread" => new JsonObject { [WorkItemService.InputKey] = Replying("502") },
            "the reference, to a true one" => new JsonObject { [StandingApprovals.VerdictKey] = scene.Later },
            "neither" => new JsonObject { ["note"] = "drafted after the second message" },
            _ => throw new ArgumentOutOfRangeException(nameof(patched), patched, "no such patch"),
        };
        var request = new WorkItemUpdateRequest(item.Id, set, null, default, default, default, default, default, default, default, default, null, null);

        if (refused)
        {
            var error = await Assert.ThrowsAsync<ValidationException>(() => Service(db).UpdateAsync(request, Ct));
            Assert.Equal("context.reply_verdict", Assert.Single(error.Details!).Field);
        }
        else
        {
            await Service(db).UpdateAsync(request, Ct);
        }
    }

    private static WorkItemCreateRequest Create(Scene scene, string operation, string? verdict) => new(
        scene.Campaign.PublicId,
        WorkItemKind.ProviderOp,
        null,
        operation,
        scene.Contact.PublicId,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        new JsonObject
        {
            [WorkItemService.InputKey] = operation switch
            {
                "message.send" => new JsonObject
                {
                    ["channel"] = "email",
                    ["sender"] = new JsonObject { ["external_id"] = "snd-1", ["address"] = "marta@example.com" },
                    ["message"] = new JsonObject { ["format"] = "email", ["subject"] = "Thursday", ["body"] = "Thursday at 10 works." },
                },
                "campaign.enroll" => new JsonObject
                {
                    ["campaign"] = new JsonObject { ["external_id"] = "sq-1129" },
                    ["channel"] = "email",
                    ["collision"] = "skip",
                    ["start"] = new JsonObject { ["position"] = "first_step" },
                    ["first_touch"] = "authored_delay",
                },
                _ => Replying("501"),
            },
            [StandingApprovals.VerdictKey] = verdict,
        },
        null,
        new ActorRef(ActorType.Human, "operator"),
        null);

    private static JsonObject Replying(string thread) => new()
    {
        ["channel"] = "email",
        ["thread"] = new JsonObject { ["external_id"] = thread },
        ["body"] = "Thursday at 10 works.",
    };

    private static WorkItemService Service(JasonDbContext db)
    {
        var clock = new FixedClock(new DateTimeOffset(Noon));
        return new WorkItemService(db, new JournalWriter(clock), clock, TestCanceller.New(clock), TestOptions.PluginSettings());
    }

    /// <summary>
    /// Two campaigns; in the first, a person who replied and another who did not, the verdicts on the reply in thread 501,
    /// one that names nobody, and a line of another kind; in the second, a verdict of its own.
    /// </summary>
    private static async Task<Scene> SceneAsync(JasonDbContext db)
    {
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var elsewhere = WorkItemFactory.NewCampaign(name: "Elsewhere", now: Noon);
        var contact = Person("Ada", "ada@example.test");
        var stranger = Person("Grace", "grace@example.test");
        db.Campaigns.AddRange(campaign, elsewhere);
        db.Contacts.AddRange(contact, stranger);
        foreach (var member in new[] { contact, stranger })
        {
            db.CampaignContacts.Add(new CampaignContact { Campaign = campaign, Contact = member, State = MembershipState.Enrolled, AddedAt = Noon, UpdatedAt = Noon });
        }

        await db.SaveChangesAsync(Ct);

        var journal = new JournalWriter(new FixedClock(new DateTimeOffset(Noon)));
        var verdict = Verdict(journal, db, campaign, contact, "msg-1");
        var later = Verdict(journal, db, campaign, contact, "msg-2");
        var nameless = Verdict(journal, db, campaign, null, "msg-3");
        var foreign = Verdict(journal, db, elsewhere, contact, "msg-1");
        var other = journal.Append(db, new ActorRef(ActorType.Human, "operator"), "plan_revised", campaign, updated: new JsonObject { ["thread"] = "501" });
        await db.SaveChangesAsync(Ct);
        return new Scene(campaign, contact, stranger, verdict.PublicId, later.PublicId, nameless.PublicId, foreign.PublicId, other.PublicId);
    }

    private static JournalEntry Verdict(JournalWriter journal, JasonDbContext db, Campaign campaign, Contact? contact, string message) =>
        journal.Append(
            db,
            new ActorRef(ActorType.Human, "operator"),
            JournalKinds.ReplyClassified,
            campaign,
            key: message,
            updated: new JsonObject
            {
                ["plugin"] = "stand-in-provider",
                ["thread"] = "501",
                ["message_id"] = message,
                ["contact_id"] = contact?.PublicId,
                ["category"] = "interested",
                ["reaction"] = "draft",
            },
            contact: contact);

    private static Contact Person(string name, string address)
    {
        var contact = new Contact { PublicId = PublicId.New("cnt"), FirstName = name, CreatedAt = Noon, UpdatedAt = Noon };
        contact.Channels.Add(new ContactChannel { Channel = "email", Value = address, IsPrimary = true });
        return contact;
    }

    private sealed record Scene(
        Campaign Campaign,
        Contact Contact,
        Contact Stranger,
        string Verdict,
        string Later,
        string Nameless,
        string Elsewhere,
        string Other);
}
