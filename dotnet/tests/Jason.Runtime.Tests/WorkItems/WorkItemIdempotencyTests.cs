using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.WorkItems;

/// <summary>
/// <c>workitem.create</c> with a key the caller chose. A key is looked up in the item's campaign after the request's
/// own shape is checked and before anything that reads today's state, so a repeat answers with what the first request
/// made whatever has changed since — the campaign archived, the person taken out, the settings edited. A request
/// without a key is exactly what it always was.
/// </summary>
public class WorkItemIdempotencyTests
{
    private const string Sentinel = "SENTINEL_WKEY_7f3c";

    private static readonly DateTimeOffset Noon = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Without a key, one request with two problems of two sorts is told about both, as it always was.</summary>
    [Fact]
    public async Task A_request_without_a_key_is_told_every_problem_at_once_as_before()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db, new FixedClock(Noon));

        var refused = await Assert.ThrowsAsync<ValidationException>(() =>
            service.CreateAsync(Request(campaign.PublicId) with { Role = "nobody-registered", MaxAttempts = 0 }, Ct));

        Assert.Equal(["max_attempts", "role"], refused.Details!.Select(detail => detail.Field).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// With a key, the request's own shape is checked first and today's state after the key has been looked for, so a
    /// shape problem is told on its own. Only a keyed request, which is new, sees the order.
    /// </summary>
    [Fact]
    public async Task A_keyed_request_is_told_its_shape_problems_before_anything_that_reads_todays_state()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db, new FixedClock(Noon));

        var refused = await Assert.ThrowsAsync<ValidationException>(() =>
            service.CreateAsync(Request(campaign.PublicId) with { Role = "nobody-registered", MaxAttempts = 0, IdempotencyKey = "k" }, Ct));

        Assert.Equal("max_attempts", Assert.Single(refused.Details!).Field);
        Assert.Empty(await db.WorkItems.ToListAsync(Ct));
    }

    [Fact]
    [Critical(Family.W8, "idempotent workitem.create")]
    public async Task The_same_request_again_answers_the_same_item_and_writes_nothing()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var clock = new FixedClock(Noon);
        var service = NewService(db, clock);
        var request = Request(campaign.PublicId) with { Context = new JsonObject { ["brief"] = "who signs" }, IdempotencyKey = " research-1 " };

        var first = await service.CreateAsync(request, Ct);
        clock.Advance(TimeSpan.FromMinutes(5));
        var again = await service.CreateAsync(request with { Reason = "the retry", Actor = new ActorRef(ActorType.Role, "planner") }, Ct);

        Assert.Equal(first.Id, again.Id);
        Assert.Equal("research-1", again.IdempotencyKey);
        Assert.Equal(Noon, again.CreatedAt);
        Assert.Equal(1, await db.WorkItems.CountAsync(Ct));
        Assert.Equal(1, await db.Journal.CountAsync(e => e.Kind == JournalKinds.WorkItemCreated, Ct));

        // The line about the item is what it always was: the key is the caller's text and stays on the row.
        var line = await db.Journal.SingleAsync(e => e.Kind == JournalKinds.WorkItemCreated, Ct);
        Assert.DoesNotContain("research-1", line.New!.ToJsonString(), StringComparison.Ordinal);
        Assert.Equal(CreationKey.Digest(request), (await db.WorkItems.SingleAsync(Ct)).IdempotencyDigest);
    }

    [Fact]
    public async Task A_repeat_answers_the_item_as_it_is_now()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db, new FixedClock(Noon));
        var request = Request(campaign.PublicId) with { IdempotencyKey = "research-1" };
        var made = await service.CreateAsync(request, Ct);

        // The item has run since: one attempt, finished, and the item with it.
        var row = await db.WorkItems.SingleAsync(w => w.PublicId == made.Id, Ct);
        var attempt = WorkItemFactory.NewAttempt(row, 1, AttemptStatus.Succeeded, Noon.UtcDateTime);
        row.Status = WorkItemStatus.Succeeded;
        row.AttemptCount = 1;
        row.FinishedAt = Noon.UtcDateTime;
        await db.SaveChangesAsync(Ct);
        db.ChangeTracker.Clear();

        var again = await service.CreateAsync(request, Ct);

        Assert.Equal(made.Id, again.Id);
        Assert.Equal(WorkItemStatus.Succeeded, again.Status);
        Assert.Equal(1, again.AttemptCount);
        Assert.Equal(attempt.PublicId, Assert.Single(again.Attempts!).Id);
    }

    [Fact]
    public async Task A_repeat_answers_after_the_campaign_was_archived()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db, new FixedClock(Noon));
        var request = Request(campaign.PublicId) with { IdempotencyKey = "research-1" };
        var made = await service.CreateAsync(request, Ct);

        await db.Campaigns.Where(c => c.Id == campaign.Id).ExecuteUpdateAsync(set => set.SetProperty(c => c.Status, CampaignStatus.Archived), Ct);
        db.ChangeTracker.Clear();

        var again = await service.CreateAsync(request, Ct);
        Assert.Equal(made.Id, again.Id);

        // And a request that is not a repeat is refused exactly as it would be without a key.
        var fresh = await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(request with { IdempotencyKey = "research-2" }, Ct));
        Assert.Equal("campaign_archived", fresh.Code);
    }

    [Fact]
    public async Task A_repeat_answers_after_the_person_was_taken_out_of_the_campaign()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var contact = new Contact { PublicId = "cnt_ada", CreatedAt = Noon.UtcDateTime, UpdatedAt = Noon.UtcDateTime };
        db.Contacts.Add(contact);
        db.CampaignContacts.Add(new CampaignContact { Campaign = campaign, Contact = contact, AddedAt = Noon.UtcDateTime, UpdatedAt = Noon.UtcDateTime });
        db.SaveChanges();
        var service = NewService(db, new FixedClock(Noon));
        var request = Request(campaign.PublicId) with { ContactId = "cnt_ada", IdempotencyKey = "research-ada" };
        var made = await service.CreateAsync(request, Ct);

        await db.CampaignContacts.ExecuteUpdateAsync(set => set.SetProperty(m => m.State, MembershipState.Excluded), Ct);
        db.ChangeTracker.Clear();

        Assert.Equal(made.Id, (await service.CreateAsync(request, Ct)).Id);
        var fresh = await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(request with { IdempotencyKey = "research-ada-2" }, Ct));
        Assert.Equal("contact_not_member", fresh.Code);
    }

    /// <summary>
    /// A settings edit can raise the shortest lease a provider item may ask for above the one an item already has. The
    /// item was made; its repeat is answered with it, while the same body sent fresh is refused under the new floor.
    /// </summary>
    [Fact]
    public async Task A_repeat_answers_after_a_settings_edit_that_would_now_refuse_the_body()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var plugins = new TestOptionsMonitor<PluginsOptions>(new PluginsOptions());
        var clock = new FixedClock(Noon);
        var service = new WorkItemService(db, new JournalWriter(clock), clock, TestCanceller.New(clock), TestOptions.PluginSettings(plugins));
        var request = ProviderOp(campaign.PublicId) with { TimeoutSeconds = 65, IdempotencyKey = "read-1" };
        var made = await service.CreateAsync(request, Ct);

        var raised = new PluginsOptions();
        raised.Invoker.KillGraceMs = 30_000;
        plugins.CurrentValue = raised;

        Assert.Equal(made.Id, (await service.CreateAsync(request, Ct)).Id);
        var fresh = await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(request with { IdempotencyKey = "read-2" }, Ct));
        Assert.Equal("timeout_seconds", Assert.Single(fresh.Details!).Field);
    }

    [Fact]
    public async Task One_key_in_two_campaigns_makes_two_items()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var first = Seed(db, WorkItemFactory.NewCampaign("First"));
        var second = Seed(db, WorkItemFactory.NewCampaign("Second"));
        var service = NewService(db, new FixedClock(Noon));

        var a = await service.CreateAsync(Request(first.PublicId) with { IdempotencyKey = "research-1" }, Ct);
        var b = await service.CreateAsync(Request(second.PublicId) with { IdempotencyKey = "research-1" }, Ct);

        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal(2, await db.WorkItems.CountAsync(Ct));
    }

    [Fact]
    [Critical(Family.W8, "a key reuse is refused")]
    public async Task A_different_request_under_the_same_key_is_refused_naming_the_item_and_not_the_key()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db, new FixedClock(Noon));
        var made = await service.CreateAsync(Request(campaign.PublicId) with { IdempotencyKey = Sentinel }, Ct);

        var refused = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateAsync(Request(campaign.PublicId) with { Priority = 5, IdempotencyKey = Sentinel }, Ct));

        Assert.Equal("idempotency_key_reused", refused.Code);
        Assert.False(refused.Retryable);
        Assert.Contains(made.Id, refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL", refused.Message, StringComparison.Ordinal);
        Assert.Equal(1, await db.WorkItems.CountAsync(Ct));
        Assert.Equal(1, await db.Journal.CountAsync(Ct));
    }

    [Fact]
    [Critical(Family.W8, "key race")]
    public async Task A_writer_that_loses_the_race_to_the_same_request_answers_with_the_winner()
    {
        using var database = new TestDatabase();
        Campaign campaign;
        using (var seeding = database.Open())
        {
            campaign = Seed(seeding, WorkItemFactory.NewCampaign());
        }

        var request = Request(campaign.PublicId) with { IdempotencyKey = "research-1" };
        var rival = new CompetingInsert(database.File, _ => Rival(campaign.Id, "research-1", CreationKey.Digest(request)));
        using var db = Open(database, rival);
        var service = NewService(db, new FixedClock(Noon));

        var answer = await service.CreateAsync(request, Ct);

        Assert.True(rival.Fired);
        Assert.Equal("wi_rival", answer.Id);
        Assert.Equal(1, await db.WorkItems.CountAsync(Ct));
        Assert.Equal(0, await db.Journal.CountAsync(Ct));
    }

    [Fact]
    public async Task A_writer_that_loses_the_race_to_a_different_request_is_refused_naming_the_winner()
    {
        using var database = new TestDatabase();
        Campaign campaign;
        using (var seeding = database.Open())
        {
            campaign = Seed(seeding, WorkItemFactory.NewCampaign());
        }

        var rival = new CompetingInsert(database.File, _ => Rival(campaign.Id, "research-1", "sha256:" + new string('0', 64)));
        using var db = Open(database, rival);
        var service = NewService(db, new FixedClock(Noon));

        var refused = await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(Request(campaign.PublicId) with { IdempotencyKey = "research-1" }, Ct));

        Assert.Equal("idempotency_key_reused", refused.Code);
        Assert.Contains("wi_rival", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A context past the cap is part of the request's shape, refused before the request is named — never answered as a
    /// conflict with the item already under the key, and never read whole to be hashed.
    /// </summary>
    [Fact]
    public async Task A_context_past_the_cap_under_a_used_key_is_refused_for_its_size()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db, new FixedClock(Noon));
        await service.CreateAsync(Request(campaign.PublicId) with { IdempotencyKey = "research-1" }, Ct);

        var huge = new JsonObject { ["brief"] = new string('x', Jason.Runtime.Campaigns.ContextRules.MaxBytes) };
        var refused = await Assert.ThrowsAsync<InvalidRequestException>(() =>
            service.CreateAsync(Request(campaign.PublicId) with { Context = huge, IdempotencyKey = "research-1" }, Ct));

        Assert.Equal("context_too_large", refused.Code);
    }

    /// <summary>
    /// Who asks is not part of the request, and whether the runtime knows the actor is today's state: a repeat sent by
    /// an attempt this runtime no longer knows is still answered with the item, while a request that is not a repeat is
    /// refused for the actor as it would be without a key.
    /// </summary>
    [Fact]
    public async Task A_repeat_answers_whoever_sends_it_and_a_fresh_request_still_has_its_actor_checked()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db, new FixedClock(Noon));
        var request = Request(campaign.PublicId) with { IdempotencyKey = "research-1" };
        var made = await service.CreateAsync(request, Ct);
        var stranger = new ActorRef(ActorType.Attempt, "att_01JB6K8TQ2W9V4MZ0C3Y7H5NRD");

        Assert.Equal(made.Id, (await service.CreateAsync(request with { Actor = stranger }, Ct)).Id);
        var fresh = await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(request with { Actor = stranger, IdempotencyKey = "research-2" }, Ct));
        Assert.Equal("actor.id", Assert.Single(fresh.Details!).Field);
    }

    /// <summary>Only a refusal over the key is a race lost: anything else the database refuses is what it always was.</summary>
    [Fact]
    public async Task A_save_refused_for_anything_but_the_key_is_not_taken_for_a_race()
    {
        using var database = new TestDatabase();
        Campaign campaign;
        using (var seeding = database.Open())
        {
            campaign = Seed(seeding, WorkItemFactory.NewCampaign());
        }

        var rival = new CompetingInsert(
            database.File,
            context => Rival(campaign.Id, "another-key", "sha256:" + new string('0', 64), context.ChangeTracker.Entries<WorkItem>().Single(e => e.State == EntityState.Added).Entity.PublicId));
        using var db = Open(database, rival);
        var service = NewService(db, new FixedClock(Noon));

        await Assert.ThrowsAsync<DbUpdateException>(() => service.CreateAsync(Request(campaign.PublicId) with { IdempotencyKey = "research-1" }, Ct));
    }

    /// <summary>The runtime's own work is created by it and for it: nothing it makes carries a key.</summary>
    [Fact]
    public async Task A_check_in_the_runtime_creates_carries_no_key()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());

        var item = await ManagerCheckIn.CreateAsync(NewService(db, new FixedClock(Noon)), db, campaign, null, new ManagerOptions(), Ct);
        await db.SaveChangesAsync(Ct);

        Assert.Null(item.IdempotencyKey);
        Assert.Null(item.IdempotencyDigest);
    }

    private static string Rival(int campaignId, string key, string digest, string publicId = "wi_rival") =>
        $"""
        INSERT INTO work_items (public_id, campaign_id, kind, role, status, priority, created_by_type, context_json, attempt_count, created_at, updated_at, lineage_state, idempotency_key, idempotency_digest)
        VALUES ('{publicId}', {campaignId}, 'ai_role', 'researcher', 'created', 0, 'human', '{"{}"}', 0, '2026-10-05 11:00:00', '2026-10-05 11:00:00', 'root', '{key}', '{digest}');
        """;

    private static JasonDbContext Open(TestDatabase database, CompetingInsert rival) =>
        new(new DbContextOptionsBuilder<JasonDbContext>(database.Options).AddInterceptors(rival).Options);

    private static WorkItemService NewService(JasonDbContext db, TimeProvider clock) =>
        new(db, new JournalWriter(clock), clock, TestCanceller.New(clock), TestOptions.PluginSettings());

    private static WorkItemCreateRequest Request(string campaignId) =>
        new(campaignId, WorkItemKind.AiRole, "researcher", null, null, null, null, null, null, null, null, null, null, null, null, null);

    private static WorkItemCreateRequest ProviderOp(string campaignId) =>
        new(campaignId, WorkItemKind.ProviderOp, null, "campaign.get", null, null, null, null, null, null, null, null,
            new JsonObject { ["input"] = new JsonObject { ["campaign"] = new JsonObject { ["external_id"] = "c-7714" } } }, null, null, null);

    private static Campaign Seed(JasonDbContext db, Campaign campaign)
    {
        db.Campaigns.Add(campaign);
        db.SaveChanges();
        return campaign;
    }
}
