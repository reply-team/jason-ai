using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Campaigns;

/// <summary>
/// <c>campaign.create</c> with a key the caller chose. The same request again answers with the campaign the first one
/// made, as it is now, and writes nothing; a different request under the same key is refused and writes nothing; and a
/// request without a key is exactly what it always was.
/// </summary>
public class CampaignIdempotencyTests
{
    private const string Sentinel = "SENTINEL_CKEY_7f3c";

    private static readonly DateTimeOffset Noon = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_campaign_made_without_a_key_has_none_and_is_answered_as_before()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));

        var first = await service.CreateAsync(new CampaignCreateRequest("Q3", null, null, null), Ct);
        var second = await service.CreateAsync(new CampaignCreateRequest("Q3", null, null, null), Ct);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Null(first.IdempotencyKey);
        Assert.False(JsonSerializer.SerializeToNode(first, JasonJson.Options)!.AsObject().ContainsKey("idempotency_key"));
        Assert.All(await db.Campaigns.ToListAsync(Ct), row => Assert.Null(row.IdempotencyDigest));
        Assert.Equal(2, await db.Journal.CountAsync(e => e.Kind == JournalKinds.CampaignCreated, Ct));
    }

    [Fact]
    public async Task A_campaign_made_with_a_key_keeps_it_trimmed_with_the_digest_of_its_request()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));
        var request = new CampaignCreateRequest("Q3", new JsonObject { ["region"] = "latam" }, null, null, "  launch-q3 ");

        var campaign = await service.CreateAsync(request, Ct);

        Assert.Equal("launch-q3", campaign.IdempotencyKey);
        var row = await db.Campaigns.SingleAsync(Ct);
        Assert.Equal("launch-q3", row.IdempotencyKey);
        Assert.Equal(CreationKey.Digest(request), row.IdempotencyDigest);

        // The chronicle says a campaign was made and what it is called, as it always has: the key is the caller's text
        // and is not a fact about the campaign anybody reads there.
        var line = await db.Journal.SingleAsync(Ct);
        Assert.DoesNotContain("launch-q3", JsonSerializer.Serialize(JournalWriter.ToDto(line, campaign.Id), JasonJson.Options), StringComparison.Ordinal);
    }

    [Fact]
    [Critical(Family.W8, "idempotent campaign.create")]
    public async Task The_same_request_again_answers_the_same_campaign_and_writes_nothing()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var service = NewService(db, clock);
        var request = new CampaignCreateRequest("Q3", new JsonObject { ["region"] = "latam" }, new ActorRef(ActorType.Human, "ada"), "first", "launch-q3");

        var first = await service.CreateAsync(request, Ct);
        clock.Advance(TimeSpan.FromMinutes(5));
        var again = await service.CreateAsync(request with { Actor = new ActorRef(ActorType.Role, "planner"), Reason = "the retry" }, Ct);

        Assert.Equal(first.Id, again.Id);
        Assert.Equal(Noon, again.CreatedAt);
        Assert.Equal(1, await db.Campaigns.CountAsync(Ct));
        Assert.Equal(1, await db.Journal.CountAsync(Ct));
    }

    /// <summary>A repeat answers with the campaign as it is now: renamed, started and archived since, it says so.</summary>
    [Fact]
    public async Task A_repeat_answers_the_campaign_as_it_is_now_archived_included()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));
        var request = new CampaignCreateRequest("Q3", null, null, null, "launch-q3");
        var made = await service.CreateAsync(request, Ct);

        await service.UpdateAsync(new CampaignUpdateRequest(made.Id, Optional<string?>.Of("Q3 renamed"), Optional<string?>.Absent, Optional<int?>.Absent, null, null), Ct);
        await service.StartAsync(new CampaignTransitionRequest(made.Id, null, null), Ct);
        await service.ArchiveAsync(new CampaignTransitionRequest(made.Id, null, null), Ct);
        var lines = await db.Journal.CountAsync(Ct);

        var again = await service.CreateAsync(request, Ct);

        Assert.Equal(made.Id, again.Id);
        Assert.Equal("Q3 renamed", again.Name);
        Assert.Equal(CampaignStatus.Archived, again.Status);
        Assert.Equal(lines, await db.Journal.CountAsync(Ct));
    }

    [Fact]
    public async Task A_different_name_under_the_same_key_is_refused_naming_the_campaign_and_not_the_key()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));
        var made = await service.CreateAsync(new CampaignCreateRequest("Q3", null, null, null, Sentinel), Ct);

        var refused = await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(new CampaignCreateRequest("Q4", null, null, null, Sentinel), Ct));

        Assert.Equal("idempotency_key_reused", refused.Code);
        Assert.Equal(409, refused.StatusCode);
        Assert.False(refused.Retryable);
        Assert.Contains(made.Id, refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL", refused.Message, StringComparison.Ordinal);
        Assert.Equal(1, await db.Campaigns.CountAsync(Ct));
        Assert.Equal(1, await db.Journal.CountAsync(Ct));
    }

    [Fact]
    public async Task A_different_context_under_the_same_key_is_refused()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));
        await service.CreateAsync(new CampaignCreateRequest("Q3", new JsonObject { ["region"] = "latam" }, null, null, "launch-q3"), Ct);

        var refused = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateAsync(new CampaignCreateRequest("Q3", new JsonObject { ["region"] = "emea" }, null, null, "launch-q3"), Ct));

        Assert.Equal("idempotency_key_reused", refused.Code);
        Assert.Equal(1, await db.Campaigns.CountAsync(Ct));
    }

    [Fact]
    public async Task A_key_that_cannot_be_one_is_refused_with_the_other_problems_of_the_request_and_never_quoted()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));

        var refused = await Assert.ThrowsAsync<ValidationException>(() =>
            service.CreateAsync(new CampaignCreateRequest(null, null, null, null, Sentinel + new string('k', 200)), Ct));

        Assert.Equal(["name", "idempotency_key"], refused.Details!.Select(detail => detail.Field));
        Assert.DoesNotContain("SENTINEL", refused.Message, StringComparison.Ordinal);
        Assert.Empty(await db.Campaigns.ToListAsync(Ct));
    }

    // -------------------------------------------------------------------------------------------------------
    // Two writers, one key: the database decides, and the loser answers as a repeat would
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_writer_that_loses_the_race_to_the_same_request_answers_with_the_winner()
    {
        using var database = new TestDatabase();
        var request = new CampaignCreateRequest("Q3", null, null, null, "launch-q3");
        var rival = new CompetingInsert(database.File, _ => Rival("launch-q3", CreationKey.Digest(request)));
        using var db = Open(database, rival);
        var service = NewService(db, new FixedClock(Noon));

        var answer = await service.CreateAsync(request, Ct);

        Assert.True(rival.Fired);
        Assert.Equal("cmp_rival", answer.Id);
        Assert.Equal(1, await db.Campaigns.CountAsync(Ct));
        Assert.Equal(0, await db.Journal.CountAsync(Ct));
    }

    [Fact]
    [Critical(Family.W8, "key race")]
    public async Task A_writer_that_loses_the_race_to_a_different_request_is_refused_naming_the_winner()
    {
        using var database = new TestDatabase();
        var rival = new CompetingInsert(database.File, _ => Rival("launch-q3", "sha256:" + new string('0', 64)));
        using var db = Open(database, rival);
        var service = NewService(db, new FixedClock(Noon));

        var refused = await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(new CampaignCreateRequest("Q3", null, null, null, "launch-q3"), Ct));

        Assert.Equal("idempotency_key_reused", refused.Code);
        Assert.Contains("cmp_rival", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>Only a refusal over the key is a race lost: anything else the database refuses is what it always was.</summary>
    [Fact]
    public async Task A_save_refused_for_anything_but_the_key_is_not_taken_for_a_race()
    {
        using var database = new TestDatabase();
        var rival = new CompetingInsert(
            database.File,
            context => Rival("another-key", "sha256:" + new string('0', 64), context.ChangeTracker.Entries<Campaign>().Single(e => e.State == EntityState.Added).Entity.PublicId));
        using var db = Open(database, rival);
        var service = NewService(db, new FixedClock(Noon));

        await Assert.ThrowsAsync<DbUpdateException>(() => service.CreateAsync(new CampaignCreateRequest("Q3", null, null, null, "launch-q3"), Ct));
    }

    private static string Rival(string key, string digest, string publicId = "cmp_rival") =>
        $"""
        INSERT INTO campaigns (public_id, name, status, context_json, created_at, updated_at, idempotency_key, idempotency_digest, project_id)
        VALUES ('{publicId}', 'Rival', 'draft', '{"{}"}', '2026-10-05 11:00:00', '2026-10-05 11:00:00', '{key}', '{digest}', (SELECT id FROM projects WHERE is_default = 1));
        """;

    private static JasonDbContext Open(TestDatabase database, CompetingInsert rival) =>
        new(new DbContextOptionsBuilder<JasonDbContext>(database.Options).AddInterceptors(rival).Options);

    private static CampaignService NewService(JasonDbContext db, TimeProvider clock) => new(db, new JournalWriter(clock), clock, TestCanceller.New(clock));
}
