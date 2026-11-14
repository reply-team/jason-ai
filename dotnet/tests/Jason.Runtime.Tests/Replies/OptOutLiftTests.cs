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
/// request from there — a human actor is what the run says, and lifts nothing: the line records the run it came from. There a
/// verdict lifts the lock only by carrying the answer a named person gave to the campaign's question about that reply,
/// which the runtime checks and does not take on the caller's word.
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
    /// A run that carries the answer a named person gave to the campaign's question about that reply lifts the lock, since the
    /// runtime checked the answer; the same run's verdict without it lifts nothing, whatever it says.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Inside_a_run_only_a_person_s_checked_answer_lifts_the_lock(bool carriesTheAnswer)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (campaign, contact, attempt) = await OptedOutAsync(db);
        var decision = await AnsweredAsync(db, campaign, attempt);
        var request = OptOutRegisterTests.Request(campaign, contact, ReplyNature.Person) with
        {
            Actor = new ActorRef(ActorType.Attempt, attempt.PublicId),
            DecisionId = carriesTheAnswer ? decision.PublicId : null,
        };

        using (RunScope.Open(attempt.PublicId))
        {
            await OptOutRegisterTests.Service(db).RecordAsync(request, Ct);
        }

        await using var fresh = database.Open();
        Assert.Equal(carriesTheAnswer, await OptOuts.StandingAsync(fresh, contact.Id, Ct) is null);
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

    /// <summary>The question the run raised about the reply, answered by a named person who chose <c>interested</c>.</summary>
    internal static async Task<Decision> AnsweredAsync(JasonDbContext db, Campaign campaign, Attempt attempt)
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
            ChosenOption = "interested",
            AnsweredAt = Noon.AddMinutes(-10),
            AnsweredByType = ActorType.Human,
            AnsweredById = "operator",
        };
        db.Decisions.Add(decision);
        await db.SaveChangesAsync(Ct);
        return decision;
    }
}
