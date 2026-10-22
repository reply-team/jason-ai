using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Campaigns;

/// <summary>
/// One campaign whose people sit in every count of the Campaigns screen, several in more than one, with the edge each
/// definition names; an unlinked draft and an archived campaign beside it. The line on each person is what the counts
/// must say about them: in Spring, now, and in the week from <see cref="Since"/> to <see cref="Until"/>.
/// </summary>
internal sealed class CampaignWorld
{
    public static readonly DateTime Now = new(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc);

    public static readonly DateTime Since = new(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc);

    public static readonly DateTime Until = Now;

    private static readonly DateTime Before = new(2026, 10, 2, 10, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Added = new(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);

    private readonly JasonDbContext _db;

    private readonly Func<string, string> _id;

    private readonly Dictionary<string, Contact> _people = new(StringComparer.Ordinal);

    private readonly List<Action> _onceSaved = [];

    private CampaignWorld(JasonDbContext db, Func<string, string> id)
    {
        _db = db;
        _id = id;
        Spring = Campaign("Spring", CampaignStatus.Active);
        Autumn = Campaign("Autumn", CampaignStatus.Draft);
        Winter = Campaign("Winter", CampaignStatus.Archived);
        Winter.ArchivedAt = Added;
        db.Campaigns.AddRange(Spring, Autumn, Winter);
        db.ExternalIds.Add(new ExternalId
        {
            Campaign = Spring,
            PluginId = "reply",
            Kind = "campaign",
            Value = "4711",
            RecordedAt = Added,
            RecordedByAttemptId = "att_01K7QN00000000000000000001",
        });
    }

    /// <summary>Active, its provider campaign pinned.</summary>
    public Campaign Spring { get; }

    /// <summary>A draft whose provider campaign is not pinned yet.</summary>
    public Campaign Autumn { get; }

    public Campaign Winter { get; }

    public IReadOnlyDictionary<string, Contact> People => _people;

    /// <summary>The world written into a running runtime's database.</summary>
    public static async Task<CampaignWorld> WriteAsync(RuntimeApiFixture api, CancellationToken ct)
    {
        await using var scope = api.Resolve<IServiceScopeFactory>().CreateAsyncScope();
        return await WriteAsync(scope.ServiceProvider.GetRequiredService<JasonDbContext>(), ct);
    }

    /// <param name="db">The context the world is written through.</param>
    /// <param name="ct">The test's token.</param>
    /// <param name="id">
    /// A public id for a prefix, in the order the world is written; fresh ULIDs when none is given. A recording passes ids
    /// that sort the same on every machine, because a listing orders by them and a cursor carries one.
    /// </param>
    public static async Task<CampaignWorld> WriteAsync(JasonDbContext db, CancellationToken ct, Func<string, string>? id = null)
    {
        var world = new CampaignWorld(db, id ?? PublicId.New);
        world.Seed();
        await db.SaveChangesAsync(ct);
        world._onceSaved.ForEach(write => write());
        await db.SaveChangesAsync(ct);
        return world;
    }

    private void Seed()
    {
        // Waiting (an approval). Added this week.
        var ada = Member("Ada", Spring, new DateTime(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc));
        Approval(Work(ada, "campaign.enroll", WorkItemStatus.AwaitingApproval, null));
        Member(ada, Autumn, Added); // New in Autumn: no work there

        // Waiting (a question about the campaign that names her research) and Idle. Worked on.
        var ben = Member("Ben", Spring, Added);
        var research = Role(ben, "researcher", WorkItemStatus.Succeeded, At(13));
        var checkIn = Role(null, "manager", WorkItemStatus.Succeeded, At(13));
        Question(checkIn, new JsonObject { ["kind"] = "work_item", ["id"] = research.PublicId });
        Question(checkIn, new JsonObject { ["kind"] = "journal_entry", ["id"] = "jrn_01K7QN00000000000000000001" });

        // Failed: her latest work failed. Worked on, not Contacted.
        var cleo = Member("Cleo", Spring, Added);
        Role(cleo, "researcher", WorkItemStatus.Succeeded, Before);
        Work(cleo, "campaign.enroll", WorkItemStatus.Failed, At(14), "provider_error");

        // Working: a failure, then newer work that is open.
        var dan = Member("Dan", Spring, Added);
        Work(dan, "campaign.enroll", WorkItemStatus.Failed, Before, "provider_error");
        Role(dan, "researcher", WorkItemStatus.Scheduled, null);

        // Failed: expired. Not Worked on: expired is neither succeeded nor failed.
        var eve = Member("Eve", Spring, Added);
        Role(eve, "writer", WorkItemStatus.Expired, At(10));

        // Idle: a rejection is a decision, not a failure. Not Worked on.
        var finn = Member("Finn", Spring, Added);
        Work(finn, "campaign.enroll", WorkItemStatus.Failed, At(11), "approval_rejected");

        // Working.
        var gus = Member("Gus", Spring, Added);
        Role(gus, "researcher", WorkItemStatus.Processing, null);

        // New: his only work is in Autumn, where he is Idle.
        var hal = Member("Hal", Spring, Added);
        Member(hal, Autumn, Added);
        Role(hal, "researcher", WorkItemStatus.Succeeded, Before, Autumn);

        // Sequence. Worked on and Contacted by an enroll into a campaign that isn't live yet; an approval decided long ago.
        var ivy = Member("Ivy", Spring, Added);
        var enroll = Work(ivy, "campaign.enroll", WorkItemStatus.Succeeded, At(12));
        enroll.Result = new JsonObject { ["campaign_live"] = false };
        Approval(enroll, ApprovalStatus.Approved);

        // Out: replied. His first reply was before the week; the follow-up line inside it doesn't count him again.
        var jon = Enrolled("Jon");
        Reply(jon, written: new DateTime(2026, 10, 5, 11, 0, 0, DateTimeKind.Utc), moment: "2026-10-05T10:41:00.000Z");
        Reply(jon, written: At(14), moment: "2026-10-14T08:00:00.000Z");

        // Out: on the register now. Opted out this week.
        var kit = Enrolled("Kit");
        Suppress(kit, At(15), lifted: null);

        // Out: the sequence was stopped for him. Worked on, not Contacted.
        var lou = Enrolled("Lou");
        Work(lou, "enrollment.stop", WorkItemStatus.Succeeded, At(14));

        // Out: a hard bounce.
        var max = Enrolled("Max");
        Event(max, "bounce", At(13), "2026-10-13T07:00:00.000Z", bounce: "hard");

        // Sequence: a soft bounce takes nobody out.
        var ned = Enrolled("Ned");
        Event(ned, "bounce", At(13), "2026-10-13T07:00:00.000Z", bounce: "soft");

        // Removed this week: Removed now and in nothing else; Replied this week, so the week still counts him.
        var oli = Member("Oli", Spring, Added, MembershipState.Excluded);
        Removal(oli, At(13));
        Approval(Work(oli, "campaign.enroll", WorkItemStatus.AwaitingApproval, null));
        Work(oli, "campaign.enroll", WorkItemStatus.Failed, Before, "provider_error");
        Event(oli, "reply_detected", At(14), "2026-10-14T08:00:00.000Z");

        // Waiting (a question her own work raised) and Failed. Worked on.
        var pia = Member("Pia", Spring, Added);
        var writer = Role(pia, "writer", WorkItemStatus.Failed, At(13), "role_error");
        Question(writer);

        // New. Re-added this week after a removal before it: Added, not Removed, though her membership is as old as anyone's.
        var quinn = Member("Quinn", Spring, Added);
        Removal(quinn, Before);
        Line(JournalKinds.ContactsAdded, Spring, null, At(14), new JsonObject
        {
            ["added"] = 1,
            ["already_member"] = 0,
            ["rejected"] = 0,
            ["contact_ids"] = new JsonArray(quinn.PublicId),
            ["created_contact_ids"] = new JsonArray(),
        });

        // Out: replied. Replied this week.
        var rae = Enrolled("Rae");
        Reply(rae, written: At(12), moment: "2026-10-12T08:00:00.000Z");

        // Out: replied in March; the line was written by this week's backfill. Not Replied this week.
        var sam = Enrolled("Sam");
        Reply(sam, written: At(15), moment: "2026-03-10T12:00:00.000Z");

        // Idle. Worked on and Contacted, one by each of the other three operations that reach a person.
        var tess = Member("Tess", Spring, Added);
        Work(tess, "message.send", WorkItemStatus.Succeeded, At(12));
        var uma = Member("Uma", Spring, Added);
        Work(uma, "conversation.reply", WorkItemStatus.Succeeded, At(13));
        var vic = Member("Vic", Spring, Added);
        Work(vic, "social_invitation.send", WorkItemStatus.Succeeded, At(14));

        // Opted out this week, then lifted: still Opted out this week, not Out now.
        Suppress(uma, At(11), lifted: At(12));

        // Idle. Worked on by work that reaches nobody: research, an activation, an update. Not Contacted.
        var wes = Member("Wes", Spring, Added);
        Role(wes, "researcher", WorkItemStatus.Succeeded, At(13));
        Work(wes, "campaign.activate", WorkItemStatus.Succeeded, At(13));
        Work(wes, "contact.update", WorkItemStatus.Succeeded, At(13));
    }

    private static DateTime At(int day) => new(2026, 10, day, 10, 0, 0, DateTimeKind.Utc);

    private Campaign Campaign(string name, CampaignStatus status)
    {
        var campaign = WorkItemFactory.NewCampaign(name, status, Added);
        campaign.PublicId = _id("cmp");
        return campaign;
    }

    private Contact Member(string name, Campaign campaign, DateTime addedAt, MembershipState state = MembershipState.Enrolled)
    {
        var contact = new Contact
        {
            PublicId = _id("cnt"),
            FirstName = name,
            LastName = "Example",
            Company = name + " Labs",
            CreatedAt = Added,
            UpdatedAt = Added,
        };
        contact.Channels.Add(new ContactChannel { Channel = "email", Value = $"{name.ToLowerInvariant()}@example.test", IsPrimary = true });
        _db.Contacts.Add(contact);
        _people.Add(name, contact);
        return Member(contact, campaign, addedAt, state);
    }

    private Contact Member(Contact contact, Campaign campaign, DateTime addedAt, MembershipState state = MembershipState.Enrolled)
    {
        _db.CampaignContacts.Add(new CampaignContact
        {
            Campaign = campaign,
            Contact = contact,
            State = state,
            AddedAt = addedAt,
            UpdatedAt = state == MembershipState.Excluded ? At(15) : addedAt,
        });
        return contact;
    }

    /// <summary>A member put into the provider's sequence before the week began.</summary>
    private Contact Enrolled(string name)
    {
        var contact = Member(name, Spring, Added);
        Work(contact, "campaign.enroll", WorkItemStatus.Succeeded, Before);
        return contact;
    }

    private WorkItem Work(Contact? contact, string operation, WorkItemStatus status, DateTime? finishedAt, string? code = null) =>
        Finish(WorkItemFactory.NewProviderOp(Spring, operation, Added), contact, status, finishedAt, code);

    private WorkItem Role(Contact? contact, string role, WorkItemStatus status, DateTime? finishedAt, string? code = null) =>
        Role(contact, role, status, finishedAt, Spring, code);

    private WorkItem Role(Contact? contact, string role, WorkItemStatus status, DateTime? finishedAt, Campaign campaign, string? code = null) =>
        Finish(WorkItemFactory.NewAiRole(campaign, role, Added), contact, status, finishedAt, code);

    private WorkItem Finish(WorkItem item, Contact? contact, WorkItemStatus status, DateTime? finishedAt, string? code)
    {
        item.PublicId = _id("wi");
        item.Contact = contact;
        item.Status = status;
        item.FinishedAt = finishedAt;
        item.UpdatedAt = finishedAt ?? Added;
        item.LastError = code is null ? null : new AttemptErrorDto(code, "It did not work.", Retriable: false);
        _db.WorkItems.Add(item);
        return item;
    }

    /// <summary>An approval names its campaign by key alone, so it is written once the item has one.</summary>
    private void Approval(WorkItem item, ApprovalStatus status = ApprovalStatus.Pending) =>
        _onceSaved.Add(() => _db.Approvals.Add(new Approval
        {
            PublicId = _id("apr"),
            WorkItem = item,
            CampaignId = item.CampaignId,
            Operation = item.Operation!,
            OperationVersion = 1,
            Subject = new JsonObject { ["operation"] = item.Operation, ["work_item"] = item.PublicId },
            SubjectHash = "sha256:" + new string('a', 64),
            Preview = new JsonObject { ["intent"] = "Put one person into the campaign at the provider." },
            PluginId = "reply",
            RouteScope = RouteScope.GlobalDefault,
            PluginSnapshotId = "snp_01K7QN00000000000000000001",
            RoutingSnapshotId = "rts_01K7QN00000000000000000001",
            Status = status,
            Reason = "approval_required",
            RequestedAt = Added,
            DecidedAt = status == ApprovalStatus.Pending ? null : Added,
        }));

    private void Question(WorkItem raisedBy, params JsonObject[] references) =>
        _db.Decisions.Add(new Decision
        {
            PublicId = _id("dec"),
            Campaign = raisedBy.Campaign,
            WorkItem = raisedBy,
            Attempt = raisedBy.Attempts.FirstOrDefault() ?? Ran(raisedBy),
            Question = "Follow up on LinkedIn?",
            References = references.Length == 0 ? null : new JsonArray([.. references]),
            Status = DecisionStatus.Pending,
            RaisedAt = raisedBy.FinishedAt ?? Added,
        });

    private Attempt Ran(WorkItem item)
    {
        var attempt = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Succeeded, item.FinishedAt ?? Added);
        attempt.PublicId = _id("att");
        return attempt;
    }

    private void Reply(Contact contact, DateTime written, string moment) =>
        Line(JournalKinds.InboundReplyObserved, Spring, contact, written, new JsonObject
        {
            ["plugin"] = "reply",
            ["thread"] = "t-" + contact.FirstName,
            ["channel"] = "email",
            ["last_activity_at"] = moment,
            ["contact_id"] = contact.PublicId,
        });

    private void Event(Contact contact, string kind, DateTime written, string moment, string? bounce = null)
    {
        var line = new JsonObject
        {
            ["kind"] = kind,
            ["plugin"] = "reply",
            ["event_id"] = _id("evt"),
            ["occurred_at"] = moment,
            ["channel"] = "email",
            ["contact_id"] = contact.PublicId,
        };
        if (bounce is not null)
        {
            line["bounce"] = bounce;
        }

        Line(JournalKinds.ProviderEventObserved, Spring, contact, written, line);
    }

    private void Suppress(Contact contact, DateTime at, DateTime? lifted)
    {
        var value = contact.Channels[0].Value;
        Line(JournalKinds.SuppressionAdded, null, null, at, new JsonObject { ["channel"] = "email", ["value"] = value });
        if (lifted is { } liftedAt)
        {
            Line(JournalKinds.SuppressionRemoved, null, null, liftedAt, new JsonObject { ["channel"] = "email", ["value"] = value });
            return;
        }

        _db.Suppressions.Add(new Suppression { PublicId = _id("sup"), Channel = "email", Value = value, CreatedAt = at });
    }

    private void Removal(Contact person, DateTime at) => Line(JournalKinds.ContactsRemoved, Spring, null, at, new JsonObject
    {
        ["removed"] = 1,
        ["not_member"] = 0,
        ["already_excluded"] = 0,
        ["rejected"] = 0,
        ["contact_ids"] = new JsonArray(person.PublicId),
    });

    private void Line(string kind, Campaign? campaign, Contact? contact, DateTime ts, JsonObject updated) =>
        _db.Journal.Add(new JournalEntry
        {
            PublicId = _id("jrn"),
            Ts = ts,
            ActorType = ActorType.System,
            Kind = kind,
            Campaign = campaign,
            Contact = contact,
            New = updated,
        });
}
