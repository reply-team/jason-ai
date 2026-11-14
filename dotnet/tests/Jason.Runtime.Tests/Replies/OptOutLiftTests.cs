using System.Net;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Domain;
using Jason.Runtime.Persistence;
using Jason.Runtime.Replies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Replies;

/// <summary>
/// Who lifts an opt-out's lock: only a person's later verdict on the same reply. Outside a run a <c>human</c> actor is the
/// person's own level of trust and counts. Inside a run — the request names its attempt, as <c>jason</c> does on every
/// request from there — a human actor is what the run says, and lifts nothing: the line records the run it came from. A
/// verdict that carries a person's answer to the campaign's question lifts nothing by that alone. The one shape that lifts
/// is a later verdict on the same reply by a named human outside any run that states a nature other than opt_out.
/// </summary>
public class OptOutLiftTests
{
    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_person_named_from_inside_a_run_is_recorded_as_that_run_s_and_lifts_nothing()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (campaign, contact, attempt) = await OptedOutAsync(db);

        JournalEntryDto corrected;
        using (RunScope.Open(attempt.PublicId))
        {
            corrected = await OptOutRegisterTests.Service(db).RecordAsync(OptOutRegisterTests.Request(campaign, contact, ReplyNature.Person), Ct);
        }

        await using var fresh = database.Open();
        var line = await fresh.Journal.AsNoTracking().SingleAsync(e => e.PublicId == corrected.Id, Ct);
        Assert.Equal((ActorType.Human, "ada"), (line.ActorType, line.ActorId));
        Assert.Equal(attempt.PublicId, line.AttemptId);
        Assert.NotNull(line.WorkItemId);
        Assert.NotNull(await OptOuts.StandingAsync(fresh, contact.Id, Ct));
    }

    [Fact]
    public async Task A_person_outside_any_run_lifts_the_lock()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (campaign, contact, _) = await OptedOutAsync(db);

        await OptOutRegisterTests.Service(db).RecordAsync(OptOutRegisterTests.Request(campaign, contact, ReplyNature.Person), Ct);

        await using var fresh = database.Open();
        Assert.Null(await OptOuts.StandingAsync(fresh, contact.Id, Ct));
    }

    /// <summary>
    /// The one shape that lifts: a named person, outside any run, later, on the same reply, saying what the reply is — any
    /// nature but an opt-out.
    /// </summary>
    [Theory]
    [InlineData(ReplyNature.Person)]
    [InlineData(ReplyNature.Automatic)]
    [InlineData(ReplyNature.Bounce)]
    public async Task A_named_person_outside_a_run_who_says_what_the_reply_is_lifts_the_lock(ReplyNature nature)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (campaign, contact, _) = await OptedOutAsync(db);

        await OptOutRegisterTests.Service(db).RecordAsync(OptOutRegisterTests.Request(campaign, contact, nature), Ct);

        await using var fresh = database.Open();
        Assert.Null(await OptOuts.StandingAsync(fresh, contact.Id, Ct));
        Assert.True(await fresh.Suppressions.AnyAsync(s => s.Channel == "phone", Ct));
    }

    /// <summary>
    /// A person's verdict lifts only when it is theirs and says something: one by a human who gave no name, or by a named
    /// person who left the nature out, or one that says opt_out again, leaves the lock where it is, and the verdict that
    /// stands is still the first one.
    /// </summary>
    [Theory]
    [InlineData("a nameless human, nature person")]
    [InlineData("a named human, no nature")]
    [InlineData("a named human, opt_out again")]
    public async Task A_person_s_verdict_that_names_nobody_or_says_no_nature_lifts_nothing(string shape)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (campaign, contact, _) = await OptedOutAsync(db);
        var first = await OptOuts.StandingAsync(db, contact.Id, Ct);
        var request = shape switch
        {
            "a nameless human, nature person" => OptOutRegisterTests.Request(campaign, contact, ReplyNature.Person) with { Actor = new ActorRef(ActorType.Human) },
            "a named human, no nature" => OptOutRegisterTests.Request(campaign, contact, null),
            _ => OptOutRegisterTests.Request(campaign, contact, ReplyNature.OptOut),
        };

        await OptOutRegisterTests.Service(db).RecordAsync(request, Ct);

        await using var fresh = database.Open();
        Assert.Equal(first, await OptOuts.StandingAsync(fresh, contact.Id, Ct));
    }

    /// <summary>
    /// The verdict that lifts is read by its key, the reply, whoever its body names. A named person outside a run who
    /// records an opt-out on the same reply for another contact lifts nothing for the first, who stays locked, and the other
    /// is locked too; the same person recording that the reply was another contact's answer lifts the first.
    /// </summary>
    [Theory]
    [InlineData(ReplyNature.OptOut, false)]
    [InlineData(ReplyNature.Person, true)]
    public async Task A_person_s_verdict_on_the_same_reply_naming_another_contact_is_read_by_the_reply(ReplyNature nature, bool lifts)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (campaign, contactA, _) = await OptedOutAsync(db);
        var contactB = await OptOutRegisterTests.ContactAsync(db, "cnt_01K52JR000000000000000000B", ("email", "boris@example.test"));

        await OptOutRegisterTests.Service(db).RecordAsync(OptOutRegisterTests.Request(campaign, contactB, nature), Ct);

        await using var fresh = database.Open();
        Assert.Equal(lifts, await OptOuts.StandingAsync(fresh, contactA.Id, Ct) is null);
        Assert.Equal(nature == ReplyNature.OptOut, await OptOuts.StandingAsync(fresh, contactB.Id, Ct) is not null);
    }

    /// <summary>
    /// A verdict that carries a person's answer lifts nothing by that alone. The run that recorded the opt-out asked the
    /// person, who answered <c>unsubscribe</c>: the run records that answer with the nature person, and with none. The lock
    /// holds either way; it is a run's verdict, not the person's own.
    /// </summary>
    [Theory]
    [InlineData(ReplyNature.Person)]
    [InlineData(null)]
    public async Task The_opposite_answer_carried_by_the_run_lifts_nothing(ReplyNature? nature)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (campaign, contact, attempt) = await OptedOutAsync(db);
        var decision = await AnsweredAsync(db, campaign, attempt, chosen: "unsubscribe");

        using (RunScope.Open(attempt.PublicId))
        {
            await OptOutRegisterTests.Service(db).RecordAsync(
                OptOutRegisterTests.Request(campaign, contact, nature) with
                {
                    Category = "unsubscribe",
                    Actor = new ActorRef(ActorType.Attempt, attempt.PublicId),
                    DecisionId = decision.PublicId,
                },
                Ct);
        }

        await using var fresh = database.Open();
        Assert.NotNull(await OptOuts.StandingAsync(fresh, contact.Id, Ct));
    }

    /// <summary>
    /// An answer given before the opt-out landed: the run guessed on the reply, asked, the person answered
    /// <c>interested</c>, and then the opt-out was recorded. Re-recording that old answer, by the run, by a role or by a named
    /// person outside a run who leaves the nature out, lifts nothing.
    /// </summary>
    [Theory]
    [InlineData("the run")]
    [InlineData("a role")]
    [InlineData("a named person outside a run")]
    public async Task An_answer_given_before_the_opt_out_lifts_nothing_when_recorded_again(string caller)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = await OptOutRegisterTests.CampaignAsync(db);
        var contact = await OptOutRegisterTests.ContactAsync(db, "cnt_01K52JR0000000000000000001", ("phone", "+15555550100"));
        var (_, attempt) = await OptOutRegisterTests.RunAsync(db, campaign, contact);
        var byTheRun = new ActorRef(ActorType.Attempt, attempt.PublicId);
        await OptOutRegisterTests.Service(db).RecordAsync(OptOutRegisterTests.Request(campaign, contact, null) with { Actor = byTheRun }, Ct);
        var decision = await AnsweredAsync(db, campaign, attempt);
        await OptOutRegisterTests.Service(db).RecordAsync(OptOutRegisterTests.Request(campaign, contact, ReplyNature.OptOut) with { Actor = byTheRun }, Ct);
        var again = OptOutRegisterTests.Request(campaign, contact, null) with { DecisionId = decision.PublicId };

        if (caller == "the run")
        {
            using (RunScope.Open(attempt.PublicId))
            {
                await OptOutRegisterTests.Service(db).RecordAsync(again with { Actor = byTheRun }, Ct);
            }
        }
        else
        {
            await OptOutRegisterTests.Service(db).RecordAsync(caller == "a role" ? again with { Actor = new ActorRef(ActorType.Role, "manager") } : again, Ct);
        }

        await using var fresh = database.Open();
        Assert.NotNull(await OptOuts.StandingAsync(fresh, contact.Id, Ct));
    }

    /// <summary>
    /// A transplant: a decision a person answered about another reply, asked by another run of the campaign, carried onto
    /// this reply by that run after it recorded a verdict here. The runtime's checks on the decision pass, and the lock
    /// still holds: the answer is not the person's own verdict on this reply.
    /// </summary>
    [Fact]
    public async Task An_answer_transplanted_from_another_reply_lifts_nothing()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (campaign, contact, _) = await OptedOutAsync(db);
        var (_, other) = await OptOutRegisterTests.RunAsync(db, campaign, contact);
        var byOther = new ActorRef(ActorType.Attempt, other.PublicId);
        await OptOutRegisterTests.Service(db).RecordAsync(OptOutRegisterTests.Request(campaign, contact, null) with { Actor = byOther, MessageId = "msg-90" }, Ct);
        var decision = await AnsweredAsync(db, campaign, other);

        using (RunScope.Open(other.PublicId))
        {
            await OptOutRegisterTests.Service(db).RecordAsync(OptOutRegisterTests.Request(campaign, contact, null) with { Actor = byOther }, Ct);
            await OptOutRegisterTests.Service(db).RecordAsync(
                OptOutRegisterTests.Request(campaign, contact, ReplyNature.Person) with { Actor = byOther, DecisionId = decision.PublicId },
                Ct);
        }

        await using var fresh = database.Open();
        Assert.NotNull(await OptOuts.StandingAsync(fresh, contact.Id, Ct));
    }

    /// <summary>
    /// A request that says it comes from a run the runtime cannot place in the verdict's campaign is refused, rather than
    /// recorded as a person's own word: the run is checked, not taken on the caller's word.
    /// </summary>
    [Theory]
    [InlineData("an attempt nobody made")]
    [InlineData("an attempt of another campaign")]
    public async Task A_person_named_from_a_run_the_runtime_cannot_place_in_the_campaign_is_refused(string run)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (campaign, contact, _) = await OptedOutAsync(db);
        var named = run == "an attempt nobody made"
            ? "att_01K52JR0000000000000000099"
            : (await OptOutRegisterTests.RunAsync(db, await OptOutRegisterTests.CampaignAsync(db, "Autumn"), contact)).Attempt.PublicId;

        ValidationException refused;
        using (RunScope.Open(named))
        {
            refused = await Assert.ThrowsAsync<ValidationException>(
                () => OptOutRegisterTests.Service(db).RecordAsync(OptOutRegisterTests.Request(campaign, contact, ReplyNature.Person), Ct));
        }

        Assert.Equal("actor", Assert.Single(refused.Details!).Field);
        await using var fresh = database.Open();
        Assert.NotNull(await OptOuts.StandingAsync(fresh, contact.Id, Ct));
    }

    /// <summary>
    /// Through the door: a request that carries the run's header, as <c>jason</c> sends from inside a run, records a person's
    /// verdict as the run's, and the same request without it as the person's own.
    /// </summary>
    [Fact]
    public async Task The_door_names_the_run_a_request_carries_to_the_verdict_it_records()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(new DateTimeOffset(Noon)));
        string campaignId, contactId, attemptId;
        await using (var scope = api.Resolve<IServiceScopeFactory>().CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
            var (campaign, contact, attempt) = await OptedOutAsync(db);
            (campaignId, contactId, attemptId) = (campaign.PublicId, contact.PublicId, attempt.PublicId);
        }

        object Body(string message) => new
        {
            campaign_id = campaignId,
            plugin_id = "mailbox",
            thread_id = "thr-1",
            message_id = message,
            message_at = "2026-09-14T11:30:00Z",
            category = "interested",
            confidence = 0.9,
            reaction = "draft",
            contact_id = contactId,
            nature = "person",
            actor = new { type = "human", id = "ada" },
        };

        var (status, text) = await api.PostAsync(Operations.ReplyRecord, Body("msg-77"), new Dictionary<string, string> { ["Jason-Attempt"] = attemptId }, Ct);
        var outside = await api.PostOkAsync<JournalEntryDto>(Operations.ReplyRecord, Body("msg-80"), Ct);

        Assert.True(status == HttpStatusCode.OK, text);
        Assert.Equal(attemptId, (string?)JsonNode.Parse(text)!["attempt_id"]);
        Assert.Null(outside.AttemptId);
    }

    /// <summary>A contact with a value, opted out by a responder's run about them, which then ended.</summary>
    private static async Task<(Campaign Campaign, Contact Contact, Attempt Attempt)> OptedOutAsync(JasonDbContext db)
    {
        var campaign = await OptOutRegisterTests.CampaignAsync(db);
        var contact = await OptOutRegisterTests.ContactAsync(db, "cnt_01K52JR0000000000000000001", ("phone", "+15555550100"));
        var (_, attempt) = await OptOutRegisterTests.RunAsync(db, campaign, contact);
        await OptOutRegisterTests.Service(db).RecordAsync(
            OptOutRegisterTests.Request(campaign, contact, ReplyNature.OptOut) with { Actor = new ActorRef(ActorType.Attempt, attempt.PublicId) },
            Ct);
        Assert.NotNull(await OptOuts.StandingAsync(db, contact.Id, Ct));
        return (campaign, contact, attempt);
    }

    /// <summary>The question the run raised about the reply, answered by a named person who chose <paramref name="chosen"/>.</summary>
    internal static async Task<Decision> AnsweredAsync(JasonDbContext db, Campaign campaign, Attempt attempt, string chosen = "interested")
    {
        var decision = new Decision
        {
            PublicId = PublicId.New("dec"),
            CampaignId = campaign.Id,
            WorkItemId = attempt.WorkItemId,
            AttemptId = attempt.Id,
            Question = "Did they ask to be left alone?",
            Options = new JsonArray(
                new JsonObject { ["label"] = "interested", ["detail"] = null },
                new JsonObject { ["label"] = "unsubscribe", ["detail"] = null }),
            Status = DecisionStatus.Answered,
            RaisedAt = Noon.AddMinutes(-20),
            Answer = "They asked for the pricing, not to be left alone.",
            ChosenOption = chosen,
            AnsweredAt = Noon.AddMinutes(-10),
            AnsweredByType = ActorType.Human,
            AnsweredById = "operator",
        };
        db.Decisions.Add(decision);
        await db.SaveChangesAsync(Ct);
        return decision;
    }
}
