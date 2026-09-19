using System.Net;
using Jason.Contracts.Api;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Decisions;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Decisions;

/// <summary>
/// The edges of a question, met by somebody typing rather than by the stand-in that usually asks. Every input
/// here is built by the test itself: nothing is borrowed from a fixture, because a fixture is a thing somebody
/// already made well-formed.
/// </summary>
public class DecisionHostileInputTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static readonly ActorRef Ada = new(ActorType.Human, "ada");

    [Fact]
    public async Task A_question_that_is_only_whitespace_is_no_question()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);

        var refused = await Assert.ThrowsAsync<ValidationException>(() => Service(db).RaiseAsync(
            new DecisionRaiseRequest(seed.Item.PublicId, seed.Attempt.PublicId, "   \t \n ", null, null, null), Ct));

        Assert.Equal("question", refused.Details![0].Field);
        Assert.Equal("required", refused.Details[0].Code);
        Assert.Empty(await db.Decisions.ToListAsync(Ct));
    }

    [Fact]
    public async Task Eleven_options_are_more_than_anybody_can_answer_at_a_glance()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        var options = Enumerable.Range(0, DecisionLimits.MaxOptions + 1)
            .Select(index => new DecisionOption($"option {index}", null))
            .ToList();

        var refused = await Assert.ThrowsAsync<ValidationException>(() => Service(db).RaiseAsync(
            new DecisionRaiseRequest(seed.Item.PublicId, seed.Attempt.PublicId, "which?", options, null, null), Ct));

        Assert.Equal("options", refused.Details![0].Field);
        Assert.Equal("too_many", refused.Details[0].Code);
        Assert.Empty(await db.Decisions.ToListAsync(Ct));
    }

    [Fact]
    public async Task An_option_label_past_its_bound_is_refused()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        var options = new[] { new DecisionOption(new string('x', DecisionLimits.MaxOptionLabelLength + 1), null) };

        var refused = await Assert.ThrowsAsync<ValidationException>(() => Service(db).RaiseAsync(
            new DecisionRaiseRequest(seed.Item.PublicId, seed.Attempt.PublicId, "which?", options, null, null), Ct));

        Assert.Equal("options[0]", refused.Details![0].Field);
        Assert.Equal("too_long", refused.Details[0].Code);
    }

    [Fact]
    public async Task Fifty_one_references_are_a_database_dump_rather_than_a_reading_list()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        var references = Enumerable.Range(0, DecisionLimits.MaxReferences + 1)
            .Select(_ => new DecisionReference(DecisionReferenceKind.WorkItem, seed.Item.PublicId))
            .ToList();

        var refused = await Assert.ThrowsAsync<ValidationException>(() => Service(db).RaiseAsync(
            new DecisionRaiseRequest(seed.Item.PublicId, seed.Attempt.PublicId, "which?", null, references, null), Ct));

        Assert.Equal("references", refused.Details![0].Field);
        Assert.Equal("too_many", refused.Details[0].Code);
    }

    /// <summary>A public id is bounded everywhere in this runtime, so something longer is not one.</summary>
    [Fact]
    public async Task A_reference_id_longer_than_any_public_id_is_refused_without_a_lookup()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        var references = new[]
        {
            new DecisionReference(DecisionReferenceKind.WorkItem, new string('w', DecisionLimits.MaxReferenceIdLength + 1)),
        };

        var refused = await Assert.ThrowsAsync<ValidationException>(() => Service(db).RaiseAsync(
            new DecisionRaiseRequest(seed.Item.PublicId, seed.Attempt.PublicId, "which?", null, references, null), Ct));

        Assert.Equal("references[0]", refused.Details![0].Field);
        Assert.Equal("too_long", refused.Details[0].Code);
    }

    /// <summary>
    /// A well-formed id of a known kind that names nothing at all. A different mistake from naming somebody
    /// else's row, and the same answer: a reference is kept as an identifier so it can be read later, and one
    /// that resolves to nothing never could be.
    /// </summary>
    [Fact]
    public async Task A_reference_that_names_nothing_at_all_is_refused()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        var references = new[] { new DecisionReference(DecisionReferenceKind.Report, "rpt_nobody") };

        var refused = await Assert.ThrowsAsync<InvalidRequestException>(() => Service(db).RaiseAsync(
            new DecisionRaiseRequest(seed.Item.PublicId, seed.Attempt.PublicId, "which?", null, references, null), Ct));

        Assert.Equal("decision_reference_unresolved", refused.Code);
        Assert.Contains("report", refused.Message, StringComparison.Ordinal);
        Assert.Empty(await db.Decisions.ToListAsync(Ct));
    }

    [Fact]
    public async Task A_reason_past_its_bound_is_refused()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);

        var refused = await Assert.ThrowsAsync<ValidationException>(() => Service(db).RaiseAsync(
            new DecisionRaiseRequest(
                seed.Item.PublicId,
                seed.Attempt.PublicId,
                "which?",
                null,
                null,
                new string('r', DecisionService.MaxReasonLength + 1)),
            Ct));

        Assert.Equal("reason", refused.Details![0].Field);
    }

    /// <summary>
    /// A question about a campaign nobody can run again cannot be answered afterwards. The row says which of
    /// the two things happened to it, so an answer arriving late is told why rather than silently ignored.
    /// </summary>
    [Fact]
    public async Task A_cancelled_decision_cannot_be_answered()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var seed = await SeedAsync(db);
        var decision = await Service(db).RaiseAsync(
            new DecisionRaiseRequest(seed.Item.PublicId, seed.Attempt.PublicId, "which?", null, null, null), Ct);

        await new CampaignService(db, new JournalWriter(new FixedClock(Noon)), new FixedClock(Noon), TestCanceller.New(new FixedClock(Noon)))
            .ArchiveAsync(new CampaignTransitionRequest(seed.Campaign.PublicId, Ada, null), Ct);

        var refused = await Assert.ThrowsAsync<ConflictException>(() => Service(db).AnswerAsync(
            new DecisionAnswerRequest(decision.Id, "stop", null, Ada, null), Ct));

        Assert.Equal("decision_not_pending", refused.Code);
        Assert.Contains("cancelled", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRD")]
    [InlineData("'; DROP TABLE decisions; --")]
    public async Task An_identifier_that_is_not_a_decisions_is_answered_as_one_that_does_not_exist(string id)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        await SeedAsync(db);

        var refused = await Assert.ThrowsAnyAsync<DomainException>(() => Service(db).GetAsync(new DecisionGetRequest(id), Ct));

        // An empty id is a field nobody filled in; anything else is a question that does not exist. Neither is
        // ever a query that runs: a public id is matched whole, and an id shaped like SQL is just an id.
        Assert.Contains(refused.Code, new[] { "decision_not_found", "validation_failed" }, StringComparer.Ordinal);
        Assert.Empty(await db.Decisions.ToListAsync(Ct));
    }

    /// <summary>
    /// A reference kind outside the vocabulary, over the wire, where a caller can type anything. A role note is
    /// the near miss worth naming: it is real memory and it is deliberately not referenceable, because a note
    /// is addressed by campaign and role rather than by an id.
    /// </summary>
    [Fact]
    public async Task A_reference_kind_outside_the_vocabulary_is_refused_at_the_edge()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var refused = await api.PostErrorAsync(
            Operations.DecisionRaise,
            new
            {
                work_item_id = "wi_nobody",
                attempt_id = "att_nobody",
                question = "which?",
                references = new[] { new { kind = "role_note", id = "manager" } },
            },
            HttpStatusCode.BadRequest,
            Ct);

        Assert.Equal("invalid_request", refused.Code);
    }

    private static DecisionService Service(JasonDbContext db)
    {
        var clock = new FixedClock(Noon);
        return new DecisionService(db, new JournalWriter(clock), clock);
    }

    private static async Task<Seed> SeedAsync(JasonDbContext db)
    {
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        campaign.Status = CampaignStatus.Active;
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
        return new Seed(campaign, item, attempt);
    }

    private sealed record Seed(Campaign Campaign, WorkItem Item, Attempt Attempt);
}
