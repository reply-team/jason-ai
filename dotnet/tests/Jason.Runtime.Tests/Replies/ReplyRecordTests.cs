using System.Net;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Replies;
using Jason.Runtime.Tests.Harness;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Replies;

/// <summary>
/// A role's verdict on one reply, recorded as the reserved line <c>reply_classified</c>: identifiers, the moment, the
/// category as a string, the confidence, the reaction chosen and the goal it steers to, and never a word anybody wrote.
/// The runtime checks that the category is a non-empty string and nothing about what it means.
/// </summary>
public class ReplyRecordTests
{
    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static readonly ActorRef Ada = new(ActorType.Human, "ada");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>What the line carries, and all of it: identifiers, a moment, numbers and words from closed lists, and the category.</summary>
    private static readonly string[] LineFields =
        ["plugin", "thread", "message_id", "message_at", "contact_id", "category", "confidence", "reaction", "goal_id", "goal_kind", "decision_id", "answered_by"];

    /// <summary>Each check the request is held to, the field it is refused at and the code it is refused with.</summary>
    public static TheoryData<string, string, string> Refusals() => new()
    {
        { "category blank", "category", "required" },
        { "category too long", "category", "too_long" },
        { "category with a line break", "category", "invalid" },
        { "category with a line separator", "category", "invalid" },
        { "category with a paragraph separator", "category", "invalid" },
        { "confidence missing", "confidence", "required" },
        { "confidence below 0", "confidence", "invalid" },
        { "confidence above 1", "confidence", "invalid" },
        { "reaction missing", "reaction", "required" },
        { "plugin missing", "plugin_id", "required" },
        { "plugin too long", "plugin_id", "too_long" },
        { "thread missing", "thread_id", "required" },
        { "thread too long", "thread_id", "too_long" },
        { "message missing", "message_id", "required" },
        { "message too long", "message_id", "too_long" },
        { "moment missing", "message_at", "required" },
        { "goal blank", "goal_id", "invalid" },
        { "goal longer than any id", "goal_id", "unknown" },
        { "goal the campaign does not have", "goal_id", "unknown" },
        { "reason too long", "reason", "too_long" },
        { "decision blank", "decision_id", "invalid" },
    };

    /// <summary>Each reason a decision cannot carry a person's answer into a verdict, and the code it is refused with.</summary>
    public static TheoryData<string, string> DecisionRefusals() => new()
    {
        { "a decision nobody raised", "decision_not_found" },
        { "a decision of another campaign", "decision_not_found" },
        { "a decision still waiting", "decision_not_answered" },
        { "a decision cancelled", "decision_not_answered" },
        { "a role's answer", "decision_not_human" },
        { "an answer nobody named", "decision_not_human" },
        { "an answer of another option", "decision_option_differs" },
        { "an answer that chose no option", "decision_option_differs" },
    };

    [Fact]
    public async Task A_verdict_is_recorded_as_one_reserved_line_and_answered_with_it()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);

        var entry = await Service(db).RecordAsync(Request(seed) with { Reason = "interested by the table; draft toward the demo" }, Ct);

        Assert.Equal(JournalKinds.ReplyClassified, entry.Kind);
        Assert.Equal(seed.Campaign.PublicId, entry.CampaignId);
        Assert.Equal(seed.Contact.PublicId, entry.ContactId);
        Assert.Equal(Ada, entry.Actor);
        Assert.Equal(InboundSync.VerdictKey("mailbox", "thr-1042", "msg-77"), entry.Key);
        Assert.Equal("interested by the table; draft toward the demo", entry.Reason);
        Assert.StartsWith("jrn_", entry.Id, StringComparison.Ordinal);

        var line = (JsonObject)entry.New!;
        Assert.Equal("mailbox", (string?)line["plugin"]);
        Assert.Equal("thr-1042", (string?)line["thread"]);
        Assert.Equal("msg-77", (string?)line["message_id"]);
        Assert.Equal("2026-09-14T11:30:00.250Z", (string?)line["message_at"]);
        Assert.Equal(seed.Contact.PublicId, (string?)line["contact_id"]);
        Assert.Equal("interested", (string?)line["category"]);
        Assert.Equal(0.9, (double)line["confidence"]!);
        Assert.Equal("draft", (string?)line["reaction"]);
        Assert.Equal("demo", (string?)line["goal_id"]);
        Assert.Equal("demo", (string?)line["goal_kind"]);

        var stored = Assert.Single(await db.Journal.Where(row => row.Kind == JournalKinds.ReplyClassified).ToListAsync(Ct));
        Assert.Equal(entry.Id, stored.PublicId);
        Assert.Equal(seed.Campaign.Id, stored.CampaignId);
        Assert.Equal(seed.Contact.Id, stored.ContactId);
    }

    /// <summary>
    /// A second verdict on the same reply is a second line, never a change to the first: both are read back, the later
    /// one last, and a reader takes the latest for a reply as the verdict that stands.
    /// </summary>
    [Fact]
    public async Task A_second_verdict_on_the_same_reply_is_a_second_line_and_the_later_one_comes_last()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);

        var first = await Service(db).RecordAsync(Request(seed), Ct);
        var second = await Service(db).RecordAsync(Request(seed) with { Category = "not_now", Reaction = ReplyReaction.None }, Ct);

        var lines = await db.Journal.Where(row => row.Kind == JournalKinds.ReplyClassified).OrderBy(row => row.Id).ToListAsync(Ct);
        Assert.Equal([first.Id, second.Id], lines.Select(row => row.PublicId));
        Assert.Equal(first.Key, second.Key);
        Assert.Equal(
            ["interested", "not_now"],
            lines.Select(row => (string?)JournalWriter.ToDto(row, seed.Campaign.PublicId).New!["category"]));
    }

    /// <summary>
    /// The line is said under the reply's identity within the campaign — plugin, thread and message, hashed — never the
    /// bare message id, which is the provider's: two plugins of one campaign may each have a message 77.
    /// </summary>
    [Fact]
    public async Task The_key_is_the_reply_s_identity_and_never_a_bare_provider_id()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);

        var mailbox = await Service(db).RecordAsync(Request(seed), Ct);
        var other = await Service(db).RecordAsync(Request(seed) with { PluginId = "another_mailbox" }, Ct);
        var thread = await Service(db).RecordAsync(Request(seed) with { ThreadId = "thr-2001" }, Ct);

        Assert.NotEqual("msg-77", mailbox.Key);
        Assert.Equal(InboundSync.VerdictKey("mailbox", "thr-1042", "msg-77"), mailbox.Key);
        Assert.Equal(3, new[] { mailbox.Key, other.Key, thread.Key }.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// The line is a fact other roles and a person read later, so it carries what the role decided and the
    /// identifiers to find the reply by — never a subject, a body, a preview or a name, which stay with the provider.
    /// </summary>
    [Fact]
    public async Task The_line_carries_identifiers_and_the_verdict_and_never_a_word_anybody_wrote()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);

        var entry = await Service(db).RecordAsync(Request(seed), Ct);

        Assert.Equal(LineFields.Order(StringComparer.Ordinal), ((JsonObject)entry.New!).Select(pair => pair.Key).Order(StringComparer.Ordinal));
        Assert.Null(entry.Old);
        Assert.DoesNotContain(seed.Contact.FirstName!, entry.New!.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_goal_or_a_contact_the_line_says_so_with_nulls()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);

        var entry = await Service(db).RecordAsync(Request(seed) with { GoalId = null, ContactId = null, Reaction = ReplyReaction.None }, Ct);

        var line = (JsonObject)entry.New!;
        Assert.Null(line["goal_id"]);
        Assert.Null(line["goal_kind"]);
        Assert.Null(line["contact_id"]);
        Assert.Null(entry.ContactId);
        Assert.Equal("none", (string?)line["reaction"]);
        Assert.True(line.ContainsKey("decision_id") && line["decision_id"] is null);
        Assert.True(line.ContainsKey("answered_by") && line["answered_by"] is null);
    }

    /// <summary>
    /// A verdict may carry a person's answer to the question that asked which category a reply is. The runtime checks the
    /// decision at the call — answered, by a named person, with the option the verdict's category is, raised by the run
    /// that recorded a verdict on this reply — and the line then names the decision and the person who answered it, which
    /// is what a standing approval reads as that person's word.
    /// </summary>
    [Fact]
    public async Task A_verdict_carries_a_persons_answer_through_the_decision_it_names()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await GuessAsync(db, seed, "msg-77");
        var decision = await DecisionAsync(db, seed, "answered");

        var entry = await Service(db).RecordAsync(Request(seed) with { DecisionId = decision.PublicId, Actor = Manager }, Ct);

        var line = (JsonObject)entry.New!;
        Assert.Equal(decision.PublicId, (string?)line["decision_id"]);
        Assert.Equal("""{"type":"human","id":"operator"}""", line["answered_by"]!.ToJsonString());
        Assert.Equal(Manager, entry.Actor);
    }

    /// <summary>
    /// A decision is about the reply its asker recorded a verdict on, and only that one. Cited for another reply, or raised
    /// by a run that recorded no verdict before it asked, the person's answer is not about the reply this verdict is on, and
    /// the record is refused with a code of its own; nothing is written. The asker's line is found by the journal's own
    /// columns — the campaign, the kind, the reply's key and the attempt — and nothing reads what a line says.
    /// </summary>
    [Theory]
    [InlineData("a decision about another reply")]
    [InlineData("a decision raised before any verdict")]
    public async Task A_decision_not_raised_by_the_run_that_recorded_a_verdict_on_this_reply_is_refused(string asked)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        if (asked == "a decision about another reply")
        {
            await GuessAsync(db, seed, "msg-76");
        }

        var decision = await DecisionAsync(db, seed, "answered");
        var written = await db.Journal.CountAsync(Ct);

        var refused = await Assert.ThrowsAsync<InvalidRequestException>(
            () => Service(db).RecordAsync(Request(seed) with { DecisionId = decision.PublicId, Actor = Manager }, Ct));

        Assert.Equal("decision_not_about_reply", refused.Code);
        Assert.Equal(400, refused.StatusCode);
        Assert.Contains(decision.PublicId, refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("interested", refused.Message, StringComparison.Ordinal);
        Assert.Equal(written, await db.Journal.CountAsync(Ct));
    }

    /// <summary>
    /// A decision is not spent by the verdict that carries it. The manager may record the person's answer on the same reply
    /// again, and each record passes: any verdict the asker recorded on the reply counts, not only the latest line, which
    /// is by then the manager's own. The lines supersede each other with the same word.
    /// </summary>
    [Fact]
    public async Task The_managers_second_record_of_the_same_answer_on_the_same_reply_passes()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        await GuessAsync(db, seed, "msg-77");
        var decision = await DecisionAsync(db, seed, "answered");

        var first = await Service(db).RecordAsync(Request(seed) with { DecisionId = decision.PublicId, Actor = Manager }, Ct);
        var second = await Service(db).RecordAsync(Request(seed) with { DecisionId = decision.PublicId, Actor = Manager }, Ct);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(
            [(decision.PublicId, "interested"), (decision.PublicId, "interested")],
            new[] { first, second }.Select(entry => ((string?)entry.New!["decision_id"], (string?)entry.New!["category"])));
    }

    /// <summary>
    /// What a verdict carries as a person's word has to be one: a decision of this campaign, answered, by somebody who gave
    /// their name and was no role, choosing the very category recorded. Each failure has a code of its own, and nothing is
    /// written. A role's answer is refused here as it is when the role tries to give it.
    /// </summary>
    [Theory]
    [MemberData(nameof(DecisionRefusals))]
    public async Task Each_decision_that_cannot_carry_a_persons_answer_is_refused_with_its_code(string decision, string code)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        var named = decision switch
        {
            "a decision nobody raised" => "dec_nobody",
            "a decision of another campaign" => (await DecisionAsync(db, await SeedAsync(db, attemptId: "att_elsewhere"), "answered")).PublicId,
            _ => (await DecisionAsync(db, seed, decision)).PublicId,
        };
        var written = await db.Journal.CountAsync(Ct);

        var refused = await Assert.ThrowsAsync<InvalidRequestException>(
            () => Service(db).RecordAsync(Request(seed) with { DecisionId = named }, Ct));

        Assert.Equal(code, refused.Code);
        Assert.Equal(400, refused.StatusCode);
        Assert.DoesNotContain("interested", refused.Message, StringComparison.Ordinal);
        Assert.Equal(written, await db.Journal.CountAsync(Ct));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    public async Task A_confidence_at_either_end_is_accepted(double confidence)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);

        var entry = await Service(db).RecordAsync(Request(seed) with { Confidence = confidence }, Ct);

        Assert.Equal(confidence, (double)entry.New!["confidence"]!);
    }

    /// <summary>The category is data the skills own: any non-empty word is recorded as given, trimmed, with its meaning unread.</summary>
    [Fact]
    public async Task A_category_no_list_names_is_recorded_as_given()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);

        var entry = await Service(db).RecordAsync(Request(seed) with { Category = "  referral_to_finance  " }, Ct);

        Assert.Equal("referral_to_finance", (string?)entry.New!["category"]);
    }

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task Each_check_is_refused_with_its_code_and_nothing_is_written(string change, string field, string code)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        var written = await db.Journal.CountAsync(Ct);

        var refused = await Assert.ThrowsAsync<ValidationException>(() => Service(db).RecordAsync(Changed(Request(seed), change), Ct));

        var detail = Assert.Single(refused.Details!);
        Assert.Equal(field, detail.Field);
        Assert.Equal(code, detail.Code);
        Assert.Equal(written, await db.Journal.CountAsync(Ct));
    }

    /// <summary>The refusal names the rule and never repeats what was sent, so a category a role wrote stays out of an error a log might keep.</summary>
    [Fact]
    public async Task A_refusal_never_repeats_the_category()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);

        var refused = await Assert.ThrowsAsync<ValidationException>(
            () => Service(db).RecordAsync(Request(seed) with { Category = "unsubscribe\nplease" }, Ct));

        Assert.DoesNotContain("unsubscribe", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(refused.Details!, detail => detail.Message.Contains("unsubscribe", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_unknown_campaign_or_contact_is_not_found()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);

        var campaign = await Assert.ThrowsAsync<NotFoundException>(
            () => Service(db).RecordAsync(Request(seed) with { CampaignId = "cmp_nobody" }, Ct));
        var contact = await Assert.ThrowsAsync<NotFoundException>(
            () => Service(db).RecordAsync(Request(seed) with { ContactId = "cnt_nobody" }, Ct));

        Assert.Equal("campaign_not_found", campaign.Code);
        Assert.Equal("contact_not_found", contact.Code);
        Assert.Empty(await db.Journal.Where(row => row.Kind == JournalKinds.ReplyClassified).ToListAsync(Ct));
    }

    /// <summary>A verdict names a goal by the id the campaign's goals give it, so an id they do not carry is refused, and so is any when there are none.</summary>
    [Theory]
    [InlineData("""[{"id":"trial","kind":"trial","title":"A trial","reached_when":"signed up","order":1}]""")]
    [InlineData("[]")]
    [InlineData(null)]
    public async Task A_goal_the_campaign_s_goals_do_not_carry_is_refused(string? goals)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db, goals);

        var refused = await Assert.ThrowsAsync<ValidationException>(() => Service(db).RecordAsync(Request(seed), Ct));

        Assert.Equal(("goal_id", "unknown"), (Assert.Single(refused.Details!).Field, refused.Details![0].Code));
    }

    /// <summary>
    /// The goals may be rewritten without the id a verdict named: the list is accepted as written, the line already
    /// written keeps the goal's id and kind, so it reads without the list, and only a later verdict is held to the goals
    /// as they now stand.
    /// </summary>
    [Fact]
    public async Task A_list_that_drops_a_goal_is_accepted_and_the_lines_that_named_it_keep_it()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        var first = await Service(db).RecordAsync(Request(seed), Ct);
        var clock = new FixedClock(new DateTimeOffset(Noon));
        var campaigns = new CampaignService(db, new JournalWriter(clock), clock, TestCanceller.New(clock), TestHarness.FoldersFor(db));

        var updated = await campaigns.UpdateContextAsync(
            new CampaignUpdateContextRequest(
                seed.Campaign.PublicId,
                new JsonObject { ["goals"] = JsonNode.Parse("""[{"id":"trial","kind":"trial","title":"A trial","reached_when":"signed up","order":1}]""") },
                null,
                null,
                "the demo is off the table"),
            Ct);

        Assert.Equal("trial", (string?)updated.Context["goals"]![0]!["id"]);
        var kept = await db.Journal.SingleAsync(row => row.PublicId == first.Id, Ct);
        var line = (JsonObject)JournalWriter.ToDto(kept, seed.Campaign.PublicId).New!;
        Assert.Equal(("demo", "demo"), ((string?)line["goal_id"], (string?)line["goal_kind"]));
        var refused = await Assert.ThrowsAsync<ValidationException>(() => Service(db).RecordAsync(Request(seed), Ct));
        Assert.Equal(("goal_id", "unknown"), (Assert.Single(refused.Details!).Field, refused.Details![0].Code));
        var later = await Service(db).RecordAsync(Request(seed) with { GoalId = "trial" }, Ct);
        Assert.Equal("trial", (string?)later.New!["goal_kind"]);
    }

    /// <summary>
    /// A verdict is history, like any line of the chronicle, so an archived campaign still takes one: the reply came,
    /// and somebody read it.
    /// </summary>
    [Fact]
    public async Task An_archived_campaign_still_takes_a_verdict()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        seed.Campaign.Status = CampaignStatus.Archived;
        await db.SaveChangesAsync(Ct);

        var entry = await Service(db).RecordAsync(Request(seed), Ct);

        Assert.Equal(JournalKinds.ReplyClassified, entry.Kind);
    }

    /// <summary>
    /// A role records its verdict under its attempt, and the line carries the work and the attempt so a reader can follow
    /// it back to the run that decided it. The attempt is checked to exist and to be work of the same campaign.
    /// </summary>
    [Fact]
    public async Task An_attempt_records_under_its_work_and_its_attempt()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);

        var entry = await Service(db).RecordAsync(Request(seed) with { Actor = new ActorRef(ActorType.Attempt, seed.Attempt.PublicId) }, Ct);

        Assert.Equal(new ActorRef(ActorType.Attempt, seed.Attempt.PublicId), entry.Actor);
        Assert.Equal(seed.Item.PublicId, entry.WorkItemId);
        Assert.Equal(seed.Attempt.PublicId, entry.AttemptId);
    }

    [Fact]
    public async Task An_attempt_this_runtime_does_not_know_or_of_another_campaign_is_refused()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        var other = await SeedAsync(db, attemptId: "att_elsewhere");

        var unknown = await Assert.ThrowsAsync<ValidationException>(
            () => Service(db).RecordAsync(Request(seed) with { Actor = new ActorRef(ActorType.Attempt, "att_nobody") }, Ct));
        var elsewhere = await Assert.ThrowsAsync<ValidationException>(
            () => Service(db).RecordAsync(Request(seed) with { Actor = new ActorRef(ActorType.Attempt, other.Attempt.PublicId) }, Ct));

        Assert.Equal(("actor.id", "unknown"), (Assert.Single(unknown.Details!).Field, unknown.Details![0].Code));
        Assert.Equal(("actor.id", "invalid"), (Assert.Single(elsewhere.Details!).Field, elsewhere.Details![0].Code));
        Assert.Empty(await db.Journal.Where(row => row.Kind == JournalKinds.ReplyClassified).ToListAsync(Ct));
    }

    [Fact]
    public async Task The_runtime_s_own_actor_is_never_claimed()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);

        await Assert.ThrowsAsync<ValidationException>(
            () => Service(db).RecordAsync(Request(seed) with { Actor = new ActorRef(ActorType.System, null) }, Ct));
    }

    /// <summary>The line is the runtime's: only <c>reply.record</c> writes it, and the chronicle's open door refuses it.</summary>
    [Fact]
    public async Task Another_writer_is_refused_the_reserved_line()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        var clock = new FixedClock(new DateTimeOffset(Noon));
        var journal = new JournalService(db, new JournalWriter(clock));

        var refused = await Assert.ThrowsAsync<InvalidRequestException>(() => journal.AppendAsync(
            new JournalAppendRequest(seed.Campaign.PublicId, JournalKinds.ReplyClassified, "msg-77", new JsonObject { ["category"] = "interested" }, Ada, null),
            Ct));

        Assert.Equal("reserved_kind", refused.Code);
        Assert.Contains(JournalKinds.ReplyClassified, JournalKinds.Reserved);
        Assert.Empty(await db.Journal.Where(row => row.Kind == JournalKinds.ReplyClassified).ToListAsync(Ct));
    }

    /// <summary>Over the wire: the request and the line in snake_case, and a reaction outside the four refused at the door.</summary>
    [Fact]
    public async Task The_operation_answers_over_the_api_and_refuses_a_reaction_outside_the_four()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Spring" }, Ct);

        var entry = await api.PostOkAsync<JournalEntryDto>(
            Operations.ReplyRecord,
            new
            {
                campaign_id = campaign.Id,
                plugin_id = "mailbox",
                thread_id = "thr-1042",
                message_id = "msg-77",
                message_at = "2026-09-14T11:30:00Z",
                category = "not_now",
                confidence = 0.6,
                reaction = "escalate",
            },
            Ct);
        var refused = await api.PostErrorAsync(
            Operations.ReplyRecord,
            new { campaign_id = campaign.Id, plugin_id = "mailbox", thread_id = "t", message_id = "m", message_at = "2026-09-14T11:30:00Z", category = "x", confidence = 0.5, reaction = "maybe" },
            HttpStatusCode.BadRequest,
            Ct);

        Assert.Equal(JournalKinds.ReplyClassified, entry.Kind);
        Assert.Equal("escalate", (string?)entry.New!["reaction"]);
        Assert.Equal("invalid_request", refused.Code);
    }

    /// <summary>
    /// The exchanges a consumer renders: a verdict recorded, one that carries a person's answer to the question about it, a
    /// goal the campaign does not carry, and a confidence out of range.
    /// </summary>
    [Fact]
    public async Task The_exchanges_are_recorded()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(new DateTimeOffset(Noon)));
        var goals = JsonNode.Parse(DemoGoal);
        var campaign = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Spring outreach", context = new { goals } }, Ct);
        var contact = await api.PostOkAsync<ContactDto>(Operations.ContactCreate, new { first_name = "Marta" }, Ct);
        var verdict = new
        {
            campaign_id = campaign.Id,
            plugin_id = "mailbox",
            thread_id = "thr-1042",
            message_id = "msg-77",
            message_at = "2026-09-14T11:30:00.250Z",
            contact_id = contact.Id,
            category = "interested",
            confidence = 0.9,
            reaction = "draft",
            goal_id = "demo",
            actor = new { type = "role", id = "responder" },
            reason = "interested by the table; draft toward the demo",
        };

        var recorded = await api.RecordAsync<JournalEntryDto>(Operations.ReplyRecord, "recorded", verdict, Ct);
        var unknown = await api.RecordErrorAsync(Operations.ReplyRecord, "unknown-goal", verdict with { goal_id = "purchase" }, HttpStatusCode.BadRequest, Ct);
        var range = await api.RecordErrorAsync(Operations.ReplyRecord, "confidence-out-of-range", verdict with { confidence = 1.5 }, HttpStatusCode.BadRequest, Ct);
        var question = await AnsweredQuestionAsync(api, campaign.Id);
        await api.PostOkAsync<JournalEntryDto>(
            Operations.ReplyRecord,
            verdict with { category = "not_now", reaction = "escalate", actor = new { type = "attempt", id = question.Asker }, reason = "not sure; asking" },
            Ct);
        var answered = await api.RecordAsync<JournalEntryDto>(
            Operations.ReplyRecord,
            "recorded-with-decision",
            new
            {
                verdict.campaign_id,
                verdict.plugin_id,
                verdict.thread_id,
                verdict.message_id,
                verdict.message_at,
                verdict.contact_id,
                verdict.category,
                confidence = 1.0,
                verdict.reaction,
                verdict.goal_id,
                decision_id = question.Id,
                actor = new { type = "role", id = "manager" },
                reason = "the person chose interested",
            },
            Ct);

        Assert.Equal(("reply_classified", "demo"), (recorded.Kind, (string?)recorded.New!["goal_kind"]));
        Assert.Equal(["goal_id/unknown"], unknown.Details!.Select(detail => $"{detail.Field}/{detail.Code}"));
        Assert.Equal(["confidence/invalid"], range.Details!.Select(detail => $"{detail.Field}/{detail.Code}"));
        Assert.Equal((question.Id, "human", "ada"), ((string?)answered.New!["decision_id"], (string?)answered.New!["answered_by"]!["type"], (string?)answered.New!["answered_by"]!["id"]));
    }

    /// <summary>
    /// The responder's question about the reply, whose options are the category ids, answered by a person: its id and the
    /// attempt that asked, which records its own verdict on the reply before the answer is carried.
    /// </summary>
    private static async Task<(string Id, string Asker)> AnsweredQuestionAsync(RuntimeApiFixture api, string campaignId)
    {
        using var scope = api.Resolve<IServiceScopeFactory>().CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        var campaign = await db.Campaigns.SingleAsync(row => row.PublicId == campaignId, Ct);
        var item = WorkItemFactory.NewAiRole(campaign, role: "responder", now: Noon);
        item.Status = WorkItemStatus.Succeeded;
        var attempt = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Succeeded, Noon);
        var decision = new Decision
        {
            PublicId = PublicId.New("dec"),
            Campaign = campaign,
            WorkItem = item,
            Attempt = attempt,
            Question = "Which kind of reply is this?",
            Options = new JsonArray(
                new JsonObject { ["label"] = "interested", ["detail"] = null },
                new JsonObject { ["label"] = "not_now", ["detail"] = null }),
            Status = DecisionStatus.Answered,
            RaisedAt = Noon,
            Answer = "They asked for times.",
            ChosenOption = "interested",
            AnsweredAt = Noon,
            AnsweredByType = ActorType.Human,
            AnsweredById = "ada",
        };
        db.WorkItems.Add(item);
        db.Attempts.Add(attempt);
        db.Decisions.Add(decision);
        await db.SaveChangesAsync(Ct);
        return (decision.PublicId, attempt.PublicId);
    }

    private static ReplyRecordRequest Changed(ReplyRecordRequest request, string change) => change switch
    {
        "category blank" => request with { Category = "   " },
        "category too long" => request with { Category = new string('c', ReplyService.MaxCategoryLength + 1) },
        "category with a line break" => request with { Category = "interested\nnow" },
        "category with a line separator" => request with { Category = "interested\u2028now" },
        "category with a paragraph separator" => request with { Category = "interested\u2029now" },
        "confidence missing" => request with { Confidence = null },
        "confidence below 0" => request with { Confidence = -0.01 },
        "confidence above 1" => request with { Confidence = 1.01 },
        "reaction missing" => request with { Reaction = null },
        "plugin missing" => request with { PluginId = " " },
        "plugin too long" => request with { PluginId = new string('p', ReplyService.MaxIdentifierLength + 1) },
        "thread missing" => request with { ThreadId = null },
        "thread too long" => request with { ThreadId = new string('t', ReplyService.MaxIdentifierLength + 1) },
        "message missing" => request with { MessageId = "" },
        "message too long" => request with { MessageId = new string('m', ReplyService.MaxIdentifierLength + 1) },
        "moment missing" => request with { MessageAt = null },
        "goal blank" => request with { GoalId = " " },
        "goal longer than any id" => request with { GoalId = new string('g', 65) },
        "goal the campaign does not have" => request with { GoalId = "purchase" },
        "reason too long" => request with { Reason = new string('r', 2001) },
        "decision blank" => request with { DecisionId = "  " },
        _ => throw new ArgumentOutOfRangeException(nameof(change), change, "no such change"),
    };

    private static ReplyRecordRequest Request(Seed seed) => new(
        seed.Campaign.PublicId,
        "mailbox",
        "thr-1042",
        "msg-77",
        new DateTimeOffset(2026, 9, 14, 11, 30, 0, 250, TimeSpan.Zero),
        "interested",
        0.9,
        ReplyReaction.Draft,
        ContactId: seed.Contact.PublicId,
        GoalId: "demo",
        Actor: Ada);

    private static ReplyService Service(JasonDbContext db) => new(db, new JournalWriter(new FixedClock(new DateTimeOffset(Noon))));

    private const string DemoGoal = """[{"id":"demo","kind":"demo","title":"A demo","reached_when":"a demo is booked","order":1}]""";

    private static async Task<Seed> SeedAsync(JasonDbContext db, string? goals = DemoGoal, string attemptId = "att_live")
    {
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        campaign.Context = goals is null ? new JsonObject() : new JsonObject { ["goals"] = JsonNode.Parse(goals) };

        var contact = new Contact { PublicId = PublicIdFor(attemptId), FirstName = "Marta", LastName = "Example", CreatedAt = Noon, UpdatedAt = Noon };
        var item = WorkItemFactory.NewAiRole(campaign, role: "responder", now: Noon);
        item.Status = WorkItemStatus.Processing;
        var attempt = new Attempt
        {
            PublicId = attemptId,
            WorkItem = item,
            Number = 1,
            Status = AttemptStatus.Running,
            StartedAt = Noon,
            LockUntil = Noon.AddMinutes(30),
        };

        db.Campaigns.Add(campaign);
        db.Contacts.Add(contact);
        db.WorkItems.Add(item);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(Ct);
        return new Seed(campaign, contact, item, attempt);
    }

    private static string PublicIdFor(string attemptId) => "cnt_" + attemptId;

    /// <summary>The manager's run, which records the person's answer as the verdict that stands.</summary>
    private static readonly ActorRef Manager = new(ActorType.Role, "manager");

    /// <summary>The responder's own verdict on a reply of the seed's thread, recorded by its attempt before it asks.</summary>
    private static async Task GuessAsync(JasonDbContext db, Seed seed, string message) =>
        await Service(db).RecordAsync(
            Request(seed) with
            {
                MessageId = message,
                Category = "not_now",
                Reaction = ReplyReaction.Escalate,
                Actor = new ActorRef(ActorType.Attempt, seed.Attempt.PublicId),
            },
            Ct);

    /// <summary>
    /// The question the responder raised about the reply, whose options are the category ids, as it stands: answered by
    /// a named person choosing <c>interested</c> unless the case says otherwise.
    /// </summary>
    private static async Task<Decision> DecisionAsync(JasonDbContext db, Seed seed, string state)
    {
        var decision = new Decision
        {
            PublicId = PublicId.New("dec"),
            CampaignId = seed.Campaign.Id,
            WorkItemId = seed.Item.Id,
            AttemptId = seed.Attempt.Id,
            Question = "Which kind of reply is this?",
            Options = new JsonArray(
                new JsonObject { ["label"] = "interested", ["detail"] = null },
                new JsonObject { ["label"] = "not_now", ["detail"] = null }),
            Status = DecisionStatus.Answered,
            RaisedAt = Noon.AddMinutes(-20),
            Answer = "It is a yes.",
            ChosenOption = "interested",
            AnsweredAt = Noon.AddMinutes(-10),
            AnsweredByType = ActorType.Human,
            AnsweredById = "operator",
        };

        switch (state)
        {
            case "a decision still waiting":
                (decision.Status, decision.Answer, decision.ChosenOption, decision.AnsweredAt, decision.AnsweredByType, decision.AnsweredById) =
                    (DecisionStatus.Pending, null, null, null, null, null);
                break;
            case "a decision cancelled":
                (decision.Status, decision.Answer, decision.ChosenOption, decision.AnsweredAt, decision.AnsweredByType, decision.AnsweredById) =
                    (DecisionStatus.Cancelled, null, null, null, null, null);
                break;
            case "a role's answer":
                // Never written by the verb, which refuses a role; a row that says so anyway is still not a person's word.
                (decision.AnsweredByType, decision.AnsweredById) = (ActorType.Role, "manager");
                break;
            case "an answer nobody named":
                decision.AnsweredById = null;
                break;
            case "an answer of another option":
                decision.ChosenOption = "not_now";
                break;
            case "an answer that chose no option":
                decision.ChosenOption = null;
                break;
        }

        db.Decisions.Add(decision);
        await db.SaveChangesAsync(Ct);
        return decision;
    }

    private sealed record Seed(Campaign Campaign, Contact Contact, WorkItem Item, Attempt Attempt);
}
