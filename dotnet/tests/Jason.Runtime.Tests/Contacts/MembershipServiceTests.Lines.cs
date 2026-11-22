using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Decisions;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Contacts;

/// <summary>
/// A person's line in a campaign, ended, continued and reopened: <c>campaign.finish_contacts</c> ends it with a reason and
/// cancels the person's open touches there and nothing else, <c>campaign.continue_contacts</c> enrols a held person again,
/// and <c>campaign.reopen_contacts</c> enrols a finished one again on the person's decision — their own word outside a
/// run, or a run naming the question they answered about the line that ended it.
/// </summary>
public partial class MembershipServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Finishing_ends_the_line_and_cancels_the_person_s_open_touches_there_and_nothing_else()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaign = NewCampaign(db);
        var elsewhere = NewCampaign(db);
        var ada = Member(db, campaign, "Ada", MembershipState.Enrolled);
        var grace = Member(db, campaign, "Grace", MembershipState.Enrolled);
        Enrol(db, elsewhere, ada, MembershipState.Enrolled);

        var send = Op(db, campaign, ada, "message.send");
        var invite = Op(db, campaign, ada, "social_invitation.send");
        var enroll = Op(db, campaign, ada, "campaign.enroll");
        var read = Op(db, campaign, ada, "conversation.get");
        var reply = Op(db, campaign, ada, "conversation.reply");
        var cancel = Op(db, campaign, ada, "message.cancel");
        var responder = WorkItemFactory.NewAiRole(campaign, "responder", Noon.UtcDateTime, w => w.Contact = ada);
        var gracesSend = Op(db, campaign, grace, "message.send");
        var adaElsewhere = Op(db, elsewhere, ada, "message.send");
        db.WorkItems.Add(responder);
        await db.SaveChangesAsync(Ct);

        var result = await NewService(db, clock).FinishAsync(new FinishContactsRequest(campaign.PublicId, [ada.PublicId], null, "said no thanks"), Ct);

        Assert.Equal(new FinishContactsSummary(1, 0, 0, 0, 0), result.Summary);
        Assert.Equal(MembershipState.Finished, await StateOf(db, campaign, ada));
        Assert.Equal(MembershipState.Enrolled, await StateOf(db, campaign, grace));
        Assert.Equal(MembershipState.Enrolled, await StateOf(db, elsewhere, ada));
        foreach (var touch in new[] { send, invite, enroll })
        {
            Assert.Equal(WorkItemStatus.Cancelled, await StatusOf(db, touch));
        }

        foreach (var kept in new[] { read, reply, cancel, responder, gracesSend, adaElsewhere })
        {
            Assert.Equal(WorkItemStatus.Created, await StatusOf(db, kept));
        }

        var cancelled = await db.Journal.Where(e => e.Kind == JournalKinds.WorkItemCancelled).ToListAsync(Ct);
        Assert.Equal(new[] { send.PublicId, invite.PublicId, enroll.PublicId }.Order(StringComparer.Ordinal), cancelled.Select(e => e.WorkItemId!).Order(StringComparer.Ordinal));
        Assert.All(cancelled, entry => Assert.Equal("line finished in campaign", entry.Reason));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Finishing_needs_a_reason_and_without_one_changes_nothing(string? reason)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaign = NewCampaign(db);
        var ada = Member(db, campaign, "Ada", MembershipState.Enrolled);
        var send = Op(db, campaign, ada, "message.send");
        await db.SaveChangesAsync(Ct);

        var refused = await Assert.ThrowsAsync<ValidationException>(() =>
            NewService(db, clock).FinishAsync(new FinishContactsRequest(campaign.PublicId, [ada.PublicId], null, reason), Ct));

        var detail = Assert.Single(refused.Details!);
        Assert.Equal(("reason", "required"), (detail.Field, detail.Code));
        Assert.Equal(MembershipState.Enrolled, await StateOf(db, campaign, ada));
        Assert.Equal(WorkItemStatus.Created, await StatusOf(db, send));
        Assert.False(await db.Journal.AnyAsync(e => e.Kind == JournalKinds.ContactsFinished, Ct));
    }

    [Fact]
    public async Task Finishing_names_every_outcome_and_writes_one_line()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaign = NewCampaign(db);
        var enrolled = Member(db, campaign, "Ada", MembershipState.Enrolled);
        var paused = Member(db, campaign, "Bob", MembershipState.Paused);
        var finished = Member(db, campaign, "Cleo", MembershipState.Finished);
        var excluded = Member(db, campaign, "Dora", MembershipState.Excluded);
        var stranger = await NewContacts(db, clock).CreateAsync(new ContactCreateRequest("Eve", null, null, null, null, null, null, null, null), Ct);

        var result = await NewService(db, clock).FinishAsync(
            new FinishContactsRequest(campaign.PublicId, [enrolled.PublicId, paused.PublicId, finished.PublicId, excluded.PublicId, stranger.Id, "cnt_nobody", " "], new ActorRef(ActorType.Role, "manager"), "ran its course"),
            Ct);

        Assert.Equal(new FinishContactsSummary(2, 1, 1, 1, 2), result.Summary);
        Assert.Equal(
            [FinishContactsItemStatus.Finished, FinishContactsItemStatus.Finished, FinishContactsItemStatus.AlreadyFinished, FinishContactsItemStatus.Excluded, FinishContactsItemStatus.NotMember, FinishContactsItemStatus.Rejected, FinishContactsItemStatus.Rejected],
            result.Items.Select(item => item.Status));
        Assert.Equal("contact_not_found", result.Items[5].Error!.Code);
        Assert.Equal("validation_failed", result.Items[6].Error!.Code);
        Assert.Equal(MembershipState.Finished, await StateOf(db, campaign, paused));
        Assert.Equal(MembershipState.Excluded, await StateOf(db, campaign, excluded));

        var line = Assert.Single(await db.Journal.Where(e => e.Kind == JournalKinds.ContactsFinished).ToListAsync(Ct));
        Assert.Equal(campaign.Id, line.CampaignId);
        Assert.Equal((ActorType.Role, "manager"), (line.ActorType, line.ActorId));
        Assert.Equal("ran its course", line.Reason);
        Assert.True(JsonNode.DeepEquals(
            new JsonObject
            {
                ["finished"] = 2,
                ["already_finished"] = 1,
                ["not_member"] = 1,
                ["excluded"] = 1,
                ["rejected"] = 2,
                ["contact_ids"] = new JsonArray(enrolled.PublicId, paused.PublicId),
            },
            line.New));
    }

    [Fact]
    public async Task Continuing_enrols_a_held_person_again_and_nobody_else()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaign = NewCampaign(db);
        var paused = Member(db, campaign, "Ada", MembershipState.Paused);
        var enrolled = Member(db, campaign, "Bob", MembershipState.Enrolled);
        var finished = Member(db, campaign, "Cleo", MembershipState.Finished);
        var excluded = Member(db, campaign, "Dora", MembershipState.Excluded);
        var stranger = await NewContacts(db, clock).CreateAsync(new ContactCreateRequest("Eve", null, null, null, null, null, null, null, null), Ct);

        var result = await NewService(db, clock).ContinueAsync(
            new ContinueContactsRequest(campaign.PublicId, [paused.PublicId, enrolled.PublicId, finished.PublicId, excluded.PublicId, stranger.Id, "cnt_nobody"], new ActorRef(ActorType.Role, "manager"), "the reply was answered"),
            Ct);

        Assert.Equal(new ContinueContactsSummary(1, 2, 1, 1, 1), result.Summary);
        Assert.Equal(
            [ContinueContactsItemStatus.Continued, ContinueContactsItemStatus.NotPaused, ContinueContactsItemStatus.NotPaused, ContinueContactsItemStatus.Excluded, ContinueContactsItemStatus.NotMember, ContinueContactsItemStatus.Rejected],
            result.Items.Select(item => item.Status));
        Assert.Equal(MembershipState.Enrolled, await StateOf(db, campaign, paused));
        Assert.Equal(MembershipState.Finished, await StateOf(db, campaign, finished));

        var line = Assert.Single(await db.Journal.Where(e => e.Kind == JournalKinds.ContactsContinued).ToListAsync(Ct));
        Assert.Equal("the reply was answered", line.Reason);
        Assert.True(JsonNode.DeepEquals(
            new JsonObject
            {
                ["continued"] = 1,
                ["not_paused"] = 2,
                ["not_member"] = 1,
                ["excluded"] = 1,
                ["rejected"] = 1,
                ["contact_ids"] = new JsonArray(paused.PublicId),
            },
            line.New));
    }

    [Fact]
    public async Task A_person_reopens_a_finished_line_by_their_own_word()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaign = NewCampaign(db);
        var finished = Member(db, campaign, "Ada", MembershipState.Finished);
        var enrolled = Member(db, campaign, "Bob", MembershipState.Enrolled);
        var paused = Member(db, campaign, "Cleo", MembershipState.Paused);
        var excluded = Member(db, campaign, "Dora", MembershipState.Excluded);
        var stranger = await NewContacts(db, clock).CreateAsync(new ContactCreateRequest("Eve", null, null, null, null, null, null, null, null), Ct);

        var result = await NewService(db, clock).ReopenAsync(
            new ReopenContactsRequest(campaign.PublicId, [finished.PublicId, enrolled.PublicId, paused.PublicId, excluded.PublicId, stranger.Id, "cnt_nobody"], null, new ActorRef(ActorType.Human, "Marta"), "she wrote again"),
            Ct);

        Assert.Equal(new ReopenContactsSummary(1, 2, 1, 1, 0, 1), result.Summary);
        Assert.Equal(
            [ReopenContactsItemStatus.Reopened, ReopenContactsItemStatus.NotFinished, ReopenContactsItemStatus.NotFinished, ReopenContactsItemStatus.Excluded, ReopenContactsItemStatus.NotMember, ReopenContactsItemStatus.Rejected],
            result.Items.Select(item => item.Status));
        Assert.Equal(MembershipState.Enrolled, await StateOf(db, campaign, finished));
        Assert.Equal(MembershipState.Paused, await StateOf(db, campaign, paused));

        var line = Assert.Single(await db.Journal.Where(e => e.Kind == JournalKinds.ContactsReopened).ToListAsync(Ct));
        Assert.Equal((ActorType.Human, "Marta"), (line.ActorType, line.ActorId));
        Assert.Equal("she wrote again", line.Reason);
        Assert.True(JsonNode.DeepEquals(
            new JsonObject
            {
                ["reopened"] = 1,
                ["not_finished"] = 2,
                ["not_member"] = 1,
                ["excluded"] = 1,
                ["not_in_decision"] = 0,
                ["rejected"] = 1,
                ["contact_ids"] = new JsonArray(finished.PublicId),
                ["decision_id"] = null,
            },
            line.New));
    }

    /// <summary>
    /// With no question named, a reopen is the person's own word: refused without a named actor, from inside a run whatever
    /// it names, and to a role or an attempt; in that order, and with nothing changed.
    /// </summary>
    [Theory]
    [InlineData(null, null, null, "actor_required")]
    [InlineData(ActorType.Human, null, null, "actor_required")]
    [InlineData(null, null, "att_01K7QN0000000000000000000R", "actor_required")]
    [InlineData(ActorType.Human, "Marta", "att_01K7QN0000000000000000000R", "decided_inside_a_run")]
    [InlineData(ActorType.Attempt, "att_01K7QN0000000000000000000R", "att_01K7QN0000000000000000000R", "decided_inside_a_run")]
    [InlineData(ActorType.Role, "manager", null, "reopen_not_human")]
    [InlineData(ActorType.Attempt, "att_01K7QN0000000000000000000R", null, "reopen_not_human")]
    public async Task Without_a_question_a_reopen_is_the_person_s_own_word(ActorType? type, string? id, string? run, string code)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaign = NewCampaign(db);
        var finished = Member(db, campaign, "Ada", MembershipState.Finished);
        var actor = type is { } named ? new ActorRef(named, id) : null;

        using var scope = run is null ? null : RunScope.Open(run);
        var refused = await Assert.ThrowsAnyAsync<DomainException>(() =>
            NewService(db, clock).ReopenAsync(new ReopenContactsRequest(campaign.PublicId, [finished.PublicId], null, actor, "again"), Ct));

        Assert.Equal(code, refused.Code);
        Assert.Equal(MembershipState.Finished, await StateOf(db, campaign, finished));
        Assert.False(await db.Journal.AnyAsync(e => e.Kind == JournalKinds.ContactsReopened, Ct));
    }

    /// <summary>
    /// A run carries the person's decision as the question they answered about the line that ended: the question is the
    /// campaign's, answered, by a named person, and names that campaign's <c>contacts_finished</c> line; each person reopened
    /// is one that line ended, and anybody else is <c>not_in_decision</c>.
    /// </summary>
    [Fact]
    public async Task A_run_reopens_the_lines_a_person_s_answered_question_names()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaign = NewCampaign(db);
        var ada = Member(db, campaign, "Ada", MembershipState.Enrolled);
        var bob = Member(db, campaign, "Bob", MembershipState.Finished);
        var service = NewService(db, clock);
        await service.FinishAsync(new FinishContactsRequest(campaign.PublicId, [ada.PublicId], null, "no answer in a month"), Ct);
        var line = await db.Journal.SingleAsync(e => e.Kind == JournalKinds.ContactsFinished, Ct);
        var (decision, attempt) = Asked(db, campaign, [new DecisionReference(DecisionReferenceKind.JournalEntry, line.PublicId)]);
        await db.SaveChangesAsync(Ct);

        using var run = RunScope.Open(attempt.PublicId);
        var result = await service.ReopenAsync(
            new ReopenContactsRequest(campaign.PublicId, [ada.PublicId, bob.PublicId], decision.PublicId, new ActorRef(ActorType.Attempt, attempt.PublicId), "she said yes"),
            Ct);

        Assert.Equal(new ReopenContactsSummary(1, 0, 0, 0, 1, 0), result.Summary);
        Assert.Equal([ReopenContactsItemStatus.Reopened, ReopenContactsItemStatus.NotInDecision], result.Items.Select(item => item.Status));
        Assert.Equal(MembershipState.Enrolled, await StateOf(db, campaign, ada));
        Assert.Equal(MembershipState.Finished, await StateOf(db, campaign, bob));

        var reopened = Assert.Single(await db.Journal.Where(e => e.Kind == JournalKinds.ContactsReopened).ToListAsync(Ct));
        Assert.Equal(decision.PublicId, (string?)reopened.New!["decision_id"]);
        Assert.Equal(1, (int)reopened.New["not_in_decision"]!);
        Assert.Equal("she said yes", reopened.Reason);
    }

    public static TheoryData<string, string> DecisionRefusals() => new()
    {
        { "unknown", "decision_not_found" },
        { "another_campaign", "decision_not_found" },
        { "pending", "decision_not_answered" },
        { "answered_by_a_role", "decision_not_human" },
        { "answered_by_nobody_named", "decision_not_human" },
        { "no_references", "decision_not_about_finish" },
        { "another_kind_of_line", "decision_not_about_finish" },
        { "another_campaign_s_line", "decision_not_about_finish" },
    };

    [Theory]
    [MemberData(nameof(DecisionRefusals))]
    public async Task A_question_that_carries_no_answer_about_the_ended_line_reopens_nothing(string shape, string code)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaign = NewCampaign(db);
        var other = NewCampaign(db);
        var ada = Member(db, campaign, "Ada", MembershipState.Enrolled);
        var bea = Member(db, other, "Bea", MembershipState.Enrolled);
        var service = NewService(db, clock);
        await service.FinishAsync(new FinishContactsRequest(campaign.PublicId, [ada.PublicId], null, "no answer"), Ct);
        await service.FinishAsync(new FinishContactsRequest(other.PublicId, [bea.PublicId], null, "no answer"), Ct);
        var ours = await db.Journal.SingleAsync(e => e.Kind == JournalKinds.ContactsFinished && e.CampaignId == campaign.Id, Ct);
        var theirs = await db.Journal.SingleAsync(e => e.Kind == JournalKinds.ContactsFinished && e.CampaignId == other.Id, Ct);
        var observed = new JournalWriter(clock).Append(db, new ActorRef(ActorType.Role, "manager"), "observation", campaign);
        await db.SaveChangesAsync(Ct);

        DecisionReference[] references = shape switch
        {
            "no_references" => [],
            "another_kind_of_line" => [new(DecisionReferenceKind.JournalEntry, observed.PublicId)],
            "another_campaign_s_line" => [new(DecisionReferenceKind.JournalEntry, theirs.PublicId)],
            _ => [new(DecisionReferenceKind.JournalEntry, ours.PublicId)],
        };
        var (decision, attempt) = Asked(db, shape == "another_campaign" ? other : campaign, references);
        switch (shape)
        {
            case "pending":
                decision.Status = DecisionStatus.Pending;
                decision.AnsweredByType = null;
                decision.AnsweredById = null;
                break;
            case "answered_by_a_role":
                decision.AnsweredByType = ActorType.Role;
                decision.AnsweredById = "manager";
                break;
            case "answered_by_nobody_named":
                decision.AnsweredById = null;
                break;
        }

        await db.SaveChangesAsync(Ct);
        var id = shape == "unknown" ? "dec_01K7QN0000000000000000000Z" : decision.PublicId;

        using var run = RunScope.Open(attempt.PublicId);
        var refused = await Assert.ThrowsAnyAsync<DomainException>(() =>
            service.ReopenAsync(new ReopenContactsRequest(campaign.PublicId, [ada.PublicId], id, new ActorRef(ActorType.Attempt, attempt.PublicId), "yes"), Ct));

        Assert.Equal(code, refused.Code);
        Assert.Equal(MembershipState.Finished, await StateOf(db, campaign, ada));
        Assert.False(await db.Journal.AnyAsync(e => e.Kind == JournalKinds.ContactsReopened, Ct));
    }

    /// <summary>A contact made and enrolled in <paramref name="campaign"/> in <paramref name="state"/>.</summary>
    private static Contact Member(JasonDbContext db, Campaign campaign, string name, MembershipState state)
    {
        var contact = new Contact { PublicId = PublicId.New("cnt"), FirstName = name, CreatedAt = Noon.UtcDateTime, UpdatedAt = Noon.UtcDateTime };
        db.Contacts.Add(contact);
        db.SaveChanges();
        Enrol(db, campaign, contact, state);
        return contact;
    }

    private static void Enrol(JasonDbContext db, Campaign campaign, Contact contact, MembershipState state)
    {
        db.CampaignContacts.Add(new CampaignContact { CampaignId = campaign.Id, ContactId = contact.Id, State = state, AddedAt = Noon.UtcDateTime, UpdatedAt = Noon.UtcDateTime });
        db.SaveChanges();
    }

    private static WorkItem Op(JasonDbContext db, Campaign campaign, Contact contact, string operation)
    {
        var item = WorkItemFactory.NewProviderOp(campaign, operation, Noon.UtcDateTime);
        item.Contact = contact;
        db.WorkItems.Add(item);
        return item;
    }

    /// <summary>A question the campaign's manager raised and a named person answered, with these references.</summary>
    private static (Decision Decision, Attempt Attempt) Asked(JasonDbContext db, Campaign campaign, IReadOnlyList<DecisionReference> references)
    {
        var item = WorkItemFactory.NewAiRole(campaign, "manager", Noon.UtcDateTime, w =>
        {
            w.Status = WorkItemStatus.Succeeded;
            w.FinishedAt = Noon.UtcDateTime;
        });
        var attempt = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Succeeded, Noon.UtcDateTime);
        attempt.FinishedAt = Noon.UtcDateTime;
        db.WorkItems.Add(item);
        var decision = new Decision
        {
            PublicId = PublicId.New("dec"),
            Campaign = campaign,
            CampaignId = campaign.Id,
            WorkItem = item,
            Attempt = attempt,
            Question = "Write to her again?",
            References = DecisionMapper.ToJson(references),
            Status = DecisionStatus.Answered,
            RaisedAt = Noon.UtcDateTime,
            Answer = "Yes.",
            AnsweredAt = Noon.UtcDateTime,
            AnsweredByType = ActorType.Human,
            AnsweredById = "Marta",
        };
        db.Decisions.Add(decision);
        return (decision, attempt);
    }

    private static async Task<MembershipState> StateOf(JasonDbContext db, Campaign campaign, Contact contact) =>
        (await db.CampaignContacts.AsNoTracking().SingleAsync(m => m.CampaignId == campaign.Id && m.ContactId == contact.Id, Ct)).State;

    private static async Task<WorkItemStatus> StatusOf(JasonDbContext db, WorkItem item) =>
        (await db.WorkItems.AsNoTracking().SingleAsync(w => w.Id == item.Id, Ct)).Status;
}
