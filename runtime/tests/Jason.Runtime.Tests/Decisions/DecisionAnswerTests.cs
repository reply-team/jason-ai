using Jason.Contracts.Api;
using Jason.Runtime.Decisions;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Decisions;

/// <summary>
/// Answering, which is a person's and nobody else's. The runtime reads that a question was answered and
/// creates the review that continues the work; what it can never do is answer one, because the whole point
/// of escalating is that the answer comes from outside the run that asked.
/// </summary>
public class DecisionAnswerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static readonly ActorRef Ada = new(ActorType.Human, "ada");

    /// <summary>
    /// A role or an attempt answering its own question is the run deciding after all, which is the one thing
    /// escalating is for.
    /// </summary>
    [Theory]
    [InlineData(ActorType.Role, "researcher")]
    [InlineData(ActorType.Attempt, "att_live")]
    public async Task Only_a_person_can_answer(ActorType type, string id)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var decision = await RaiseAsync(db);

        var refused = await Assert.ThrowsAsync<InvalidRequestException>(() => Service(db).AnswerAsync(
            new DecisionAnswerRequest(decision.Id, "stop", null, new ActorRef(type, id), null), Ct));

        Assert.Equal("decision_not_human", refused.Code);
        Assert.Equal(DecisionStatus.Pending, (await db.Decisions.AsNoTracking().SingleAsync(Ct)).Status);
    }

    /// <summary>
    /// And the runtime's own actor never reaches that rule at all: every caller is refused the system actor
    /// before any verb reads it, which is why <c>decision_not_human</c> is about roles and attempts.
    /// </summary>
    [Fact]
    public async Task The_runtimes_own_actor_is_refused_before_the_verb_sees_it()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var decision = await RaiseAsync(db);

        var refused = await Assert.ThrowsAsync<ValidationException>(() => Service(db).AnswerAsync(
            new DecisionAnswerRequest(decision.Id, "stop", null, new ActorRef(ActorType.System, "dispatcher"), null), Ct));

        Assert.Equal("actor.type", refused.Details![0].Field);
        Assert.Equal("not_allowed", refused.Details[0].Code);
    }

    /// <summary>
    /// An absent actor is an anonymous human, which is right for creating work and wrong for deciding it: what
    /// is recorded has to name the person who decided.
    /// </summary>
    [Fact]
    public async Task An_answer_that_names_nobody_is_refused()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var decision = await RaiseAsync(db);

        var refused = await Assert.ThrowsAsync<InvalidRequestException>(() => Service(db).AnswerAsync(
            new DecisionAnswerRequest(decision.Id, "stop", null, null, null), Ct));

        Assert.Equal("actor_required", refused.Code);
    }

    /// <summary>
    /// The row after an answer, and the line the answer wrote — which the row remembers, because that is how
    /// the summon names this decision later without reading what any line says.
    /// </summary>
    [Fact]
    public async Task An_answer_records_who_and_when_and_the_line_it_wrote()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var decision = await RaiseAsync(db);

        var answered = await Service(db).AnswerAsync(
            new DecisionAnswerRequest(decision.Id, "  stop after this one  ", "stop", Ada, "the account asked us to"), Ct);

        Assert.Equal(DecisionStatus.Answered, answered.Status);
        Assert.Equal("stop after this one", answered.Answer);
        Assert.Equal("stop", answered.ChosenOption);
        Assert.Equal(Ada, answered.AnsweredBy);
        Assert.Equal(Noon, answered.AnsweredAt!.Value.UtcDateTime);

        await using var fresh = database.Open();
        var line = await fresh.Journal.AsNoTracking().SingleAsync(e => e.Kind == JournalKinds.DecisionAnswered, Ct);
        var stored = await fresh.Decisions.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(line.PublicId, stored.AnswerJournalEntryId);
    }

    /// <summary>
    /// What the answered line says, which is what the whole continuation rests on: the campaign, the item and
    /// the escalating attempt — so the review it summons inherits that attempt's chain — and a <em>person</em>
    /// as its actor, which is what tells the summon this is not the loop feeding itself.
    /// </summary>
    [Fact]
    public async Task The_answered_line_names_the_work_the_attempt_and_the_person()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var decision = await RaiseAsync(db);
        var seed = await db.Decisions.AsNoTracking().Include(d => d.WorkItem).Include(d => d.Attempt).SingleAsync(Ct);

        await Service(db).AnswerAsync(new DecisionAnswerRequest(decision.Id, "stop", null, Ada, null), Ct);

        await using var fresh = database.Open();
        var line = await fresh.Journal.AsNoTracking().SingleAsync(e => e.Kind == JournalKinds.DecisionAnswered, Ct);

        Assert.Equal(ActorType.Human, line.ActorType);
        Assert.Equal("ada", line.ActorId);
        Assert.NotNull(line.CampaignId);
        Assert.Equal(seed.WorkItem!.PublicId, line.WorkItemId);
        Assert.Equal(seed.Attempt!.PublicId, line.AttemptId);

        // The id is on the line where a person reads it. What cannot read it is the summon, whose projection
        // has nowhere to put a line's `new` — so it resolves the decision by the identifier instead.
        Assert.Equal(decision.Id, (string?)line.New!["decision_id"]);
    }

    [Fact]
    public async Task A_decision_answered_twice_answers_decision_not_pending()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var decision = await RaiseAsync(db);
        await Service(db).AnswerAsync(new DecisionAnswerRequest(decision.Id, "stop", null, Ada, null), Ct);

        await using var second = database.Open();
        var refused = await Assert.ThrowsAsync<ConflictException>(() => Service(second).AnswerAsync(
            new DecisionAnswerRequest(decision.Id, "keep going", null, new ActorRef(ActorType.Human, "bo"), null), Ct));

        Assert.Equal("decision_not_pending", refused.Code);
        Assert.Contains("answered", refused.Message, StringComparison.Ordinal);
        Assert.Equal("stop", (await second.Decisions.AsNoTracking().SingleAsync(Ct)).Answer);
    }

    [Fact]
    public async Task An_option_outside_the_named_ones_is_refused()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var decision = await RaiseAsync(db);

        var refused = await Assert.ThrowsAsync<InvalidRequestException>(() => Service(db).AnswerAsync(
            new DecisionAnswerRequest(decision.Id, "something else", "escalate further", Ada, null), Ct));

        Assert.Equal("decision_option_unknown", refused.Code);
    }

    /// <summary>And where the asker named none, naming one is the same mistake: there was nothing to choose.</summary>
    [Fact]
    public async Task An_option_on_a_question_that_named_none_is_refused()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var decision = await RaiseAsync(db, withOptions: false);

        var refused = await Assert.ThrowsAsync<InvalidRequestException>(() => Service(db).AnswerAsync(
            new DecisionAnswerRequest(decision.Id, "stop", "stop", Ada, null), Ct));

        Assert.Equal("decision_option_unknown", refused.Code);
    }

    [Fact]
    public async Task An_answer_past_the_bound_is_refused()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var decision = await RaiseAsync(db);

        var refused = await Assert.ThrowsAsync<ValidationException>(() => Service(db).AnswerAsync(
            new DecisionAnswerRequest(decision.Id, new string('x', DecisionLimits.MaxAnswerLength + 1), null, Ada, null), Ct));

        Assert.Equal("answer", refused.Details![0].Field);
        Assert.Equal(DecisionStatus.Pending, (await db.Decisions.AsNoTracking().SingleAsync(Ct)).Status);
    }

    private static DecisionService Service(JasonDbContext db)
    {
        var clock = new FixedClock(Noon);
        return new DecisionService(db, new JournalWriter(clock), clock);
    }

    private static async Task<DecisionDto> RaiseAsync(JasonDbContext db, bool withOptions = true)
    {
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var item = WorkItemFactory.NewAiRole(campaign, now: Noon);
        item.Status = WorkItemStatus.Processing;
        var attempt = new Attempt
        {
            PublicId = "att_live",
            WorkItem = item,
            Number = 1,
            Status = AttemptStatus.Running,
            StartedAt = Noon,
            LockUntil = Noon.AddMinutes(30),
        };

        db.Campaigns.Add(campaign);
        db.WorkItems.Add(item);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(Ct);

        return await Service(db).RaiseAsync(
            new DecisionRaiseRequest(
                item.PublicId,
                attempt.PublicId,
                "do we keep calling this account?",
                withOptions ? [new DecisionOption("keep going", null), new DecisionOption("stop", null)] : null,
                null,
                null),
            Ct);
    }
}
