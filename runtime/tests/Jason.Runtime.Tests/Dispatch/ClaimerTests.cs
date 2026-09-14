using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jason.Runtime.Tests.Dispatch;

public class ClaimerTests
{
    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_claim_schedules_the_item_and_records_what_the_attempt_will_run()
    {
        using var harness = new Harness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var item = WorkItemFactory.NewAiRole(campaign, now: Noon, configure: w => w.Context = new JsonObject { ["brief"] = "the original brief" });
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(item);
        await GiveRoleAsync(db, "researcher", "agent-host", "--serve");
        await db.SaveChangesAsync(Ct);

        var claimed = await harness.Claimer.ClaimAsync(db, 4, Ct);

        var work = Assert.Single(claimed);
        Assert.Equal(item.PublicId, work.WorkItemPublicId);
        Assert.Equal(item.Id, work.WorkItemId);
        Assert.Equal(WorkItemStatus.Scheduled, item.Status);

        // The snapshot is the context as it stood at claim: what the executor is told cannot change under it.
        item.Context["brief"] = "a later brief";
        await db.SaveChangesAsync(Ct);

        await using var fresh = database.Open();
        var attempt = await fresh.Attempts.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(work.AttemptPublicId, attempt.PublicId);
        Assert.Equal(1, attempt.Number);
        Assert.Equal(AttemptStatus.Scheduled, attempt.Status);
        Assert.Equal(WorkItemKind.AiRole, attempt.Command);
        Assert.Equal(Noon, attempt.ClaimedAt);
        Assert.Equal(Noon.AddSeconds(3600), attempt.LockUntil);
        Assert.Null(attempt.StartedAt);
        Assert.Equal("the original brief", (string?)attempt.ContextSnapshot["brief"]);
        Assert.Equal(new[] { "agent-host", "--serve" }, attempt.Launch!.EntryCommand);
        Assert.Equal(Path.Combine(harness.Paths.WorkDirectory, item.PublicId, attempt.PublicId), attempt.Launch.WorkDir);
        Assert.Null(attempt.Launch.Pid);

        var entry = Assert.Single(await fresh.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.WorkItemScheduled).ToListAsync(Ct));
        Assert.Equal(ActorType.System, entry.ActorType);
        Assert.Equal("dispatcher", entry.ActorId);
        Assert.Equal("attempt", entry.Key);
        Assert.Equal(1, (int)entry.New!);
        Assert.Equal(item.PublicId, entry.WorkItemId);
        Assert.Equal(attempt.PublicId, entry.AttemptId);
        Assert.Equal(item.CampaignId, entry.CampaignId);
    }

    [Fact]
    public async Task One_campaign_gives_up_one_item_per_scan_and_gives_up_its_most_important()
    {
        using var harness = new Harness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        var ordinary = WorkItemFactory.NewAiRole(campaign, now: Noon);
        var urgent = WorkItemFactory.NewAiRole(campaign, now: Noon, configure: w => w.Priority = 5);
        db.WorkItems.AddRange(ordinary, urgent);
        await GiveRoleAsync(db, "researcher", "agent-host");
        await db.SaveChangesAsync(Ct);

        var claimed = await harness.Claimer.ClaimAsync(db, 4, Ct);

        Assert.Equal(urgent.PublicId, Assert.Single(claimed).WorkItemPublicId);
    }

    [Fact]
    public async Task Equal_priority_is_settled_by_the_order_the_work_was_created_in()
    {
        using var harness = new Harness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        var first = WorkItemFactory.NewAiRole(campaign, now: Noon);
        var second = WorkItemFactory.NewAiRole(campaign, now: Noon);
        db.WorkItems.Add(first);
        await db.SaveChangesAsync(Ct);
        db.WorkItems.Add(second);
        await GiveRoleAsync(db, "researcher", "agent-host");
        await db.SaveChangesAsync(Ct);

        var claimed = await harness.Claimer.ClaimAsync(db, 4, Ct);

        Assert.Equal(first.PublicId, Assert.Single(claimed).WorkItemPublicId);
    }

    [Fact]
    public async Task Every_campaign_gets_a_turn_in_the_same_scan()
    {
        using var harness = new Harness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var left = WorkItemFactory.NewCampaign("Left", now: Noon);
        var right = WorkItemFactory.NewCampaign("Right", now: Noon);
        db.Campaigns.AddRange(left, right);
        db.WorkItems.AddRange(
            WorkItemFactory.NewAiRole(left, now: Noon),
            WorkItemFactory.NewAiRole(left, now: Noon),
            WorkItemFactory.NewAiRole(right, now: Noon));
        await GiveRoleAsync(db, "researcher", "agent-host");
        await db.SaveChangesAsync(Ct);

        var claimed = await harness.Claimer.ClaimAsync(db, 4, Ct);

        Assert.Equal(2, claimed.Count);
        var campaigns = await db.WorkItems.AsNoTracking()
            .Where(w => w.Status == WorkItemStatus.Scheduled)
            .Select(w => w.CampaignId)
            .ToListAsync(Ct);
        Assert.Equal(new[] { left.Id, right.Id }, campaigns.Order().ToArray());
    }

    [Fact]
    public async Task A_single_free_slot_goes_to_the_most_important_work_anywhere()
    {
        using var harness = new Harness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var left = WorkItemFactory.NewCampaign("Left", now: Noon);
        var right = WorkItemFactory.NewCampaign("Right", now: Noon);
        db.Campaigns.AddRange(left, right);
        db.WorkItems.Add(WorkItemFactory.NewAiRole(left, now: Noon));
        var urgent = WorkItemFactory.NewAiRole(right, now: Noon, configure: w => w.Priority = 9);
        db.WorkItems.Add(urgent);
        await GiveRoleAsync(db, "researcher", "agent-host");
        await db.SaveChangesAsync(Ct);

        var claimed = await harness.Claimer.ClaimAsync(db, 1, Ct);

        Assert.Equal(urgent.PublicId, Assert.Single(claimed).WorkItemPublicId);
    }

    [Fact]
    public async Task A_campaign_with_work_in_flight_waits_its_turn()
    {
        using var harness = new Harness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(WorkItemFactory.NewAiRole(campaign, now: Noon, configure: w => w.Status = WorkItemStatus.Processing));
        db.WorkItems.Add(WorkItemFactory.NewAiRole(campaign, now: Noon));
        await GiveRoleAsync(db, "researcher", "agent-host");
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));
    }

    [Fact]
    public async Task A_campaign_that_is_not_active_yields_nothing()
    {
        using var harness = new Harness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(status: CampaignStatus.Paused, now: Noon);
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(WorkItemFactory.NewAiRole(campaign, now: Noon));
        await GiveRoleAsync(db, "researcher", "agent-host");
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(1, false)]
    public async Task A_start_date_one_second_away_decides_the_claim(int offsetSeconds, bool claimable)
    {
        using var harness = new Harness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(WorkItemFactory.NewAiRole(campaign, now: Noon, configure: w => w.NotBefore = Noon.AddSeconds(offsetSeconds)));
        await GiveRoleAsync(db, "researcher", "agent-host");
        await db.SaveChangesAsync(Ct);

        Assert.Equal(claimable ? 1 : 0, (await harness.Claimer.ClaimAsync(db, 4, Ct)).Count);
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(1, false)]
    public async Task A_retry_delay_one_second_away_decides_the_claim(int offsetSeconds, bool claimable)
    {
        using var harness = new Harness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(WorkItemFactory.NewAiRole(campaign, now: Noon, configure: w => w.RetryAfter = Noon.AddSeconds(offsetSeconds)));
        await GiveRoleAsync(db, "researcher", "agent-host");
        await db.SaveChangesAsync(Ct);

        Assert.Equal(claimable ? 1 : 0, (await harness.Claimer.ClaimAsync(db, 4, Ct)).Count);
    }

    [Fact]
    public async Task Work_that_is_already_overdue_is_left_to_the_expirer()
    {
        using var harness = new Harness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(WorkItemFactory.NewAiRole(campaign, now: Noon, configure: w => w.DueAt = Noon.AddSeconds(-1)));
        await GiveRoleAsync(db, "researcher", "agent-host");
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));
    }

    [Fact]
    public async Task A_provider_operation_fails_closed_because_no_route_exists_yet()
    {
        using var harness = new Harness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        var item = WorkItemFactory.NewProviderOp(campaign, now: Noon);
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        var stored = await fresh.WorkItems.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(WorkItemStatus.Failed, stored.Status);
        Assert.Equal(AttemptErrors.NoRoute, stored.LastError!.Code);
        Assert.Equal(1, stored.AttemptCount);
        var attempt = await fresh.Attempts.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(1, attempt.Number);
        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.False(attempt.Error!.Retriable);
        Assert.Contains("contacts.enroll", attempt.Error.Message, StringComparison.Ordinal);
        Assert.Null(attempt.Launch);
    }

    [Fact]
    public async Task A_role_nothing_can_launch_fails_closed_and_says_where_to_configure_it()
    {
        using var harness = new Harness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(WorkItemFactory.NewAiRole(campaign, now: Noon));
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        var stored = await fresh.WorkItems.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(WorkItemStatus.Failed, stored.Status);
        Assert.Equal(AttemptErrors.RoleNotLaunchable, stored.LastError!.Code);
        var attempt = await fresh.Attempts.AsNoTracking().SingleAsync(Ct);
        Assert.False(attempt.Error!.Retriable);
        Assert.Contains("researcher", attempt.Error.Message, StringComparison.Ordinal);
        Assert.Contains("Roles:DefaultEntryCommand", attempt.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task One_configured_command_is_enough_to_run_a_builtin_role()
    {
        using var harness = new Harness(roles: new RolesOptions { DefaultEntryCommand = ["dotnet", "host.dll"] });
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(WorkItemFactory.NewAiRole(campaign, now: Noon));
        await db.SaveChangesAsync(Ct);

        Assert.Single(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        var attempt = await fresh.Attempts.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(new[] { "dotnet", "host.dll" }, attempt.Launch!.EntryCommand);
    }

    [Fact]
    public async Task Two_scans_over_one_item_produce_one_attempt()
    {
        using var harness = new Harness();
        using var other = new Harness();
        using var database = new TestDatabase();
        await using (var seed = database.Open())
        {
            var campaign = WorkItemFactory.NewCampaign(now: Noon);
            seed.Campaigns.Add(campaign);
            seed.WorkItems.Add(WorkItemFactory.NewAiRole(campaign, now: Noon));
            await GiveRoleAsync(seed, "researcher", "agent-host");
            await seed.SaveChangesAsync(Ct);
        }

        await using var left = database.Open();
        await using var right = database.Open();
        var results = await Task.WhenAll(
            Task.Run(() => harness.Claimer.ClaimAsync(left, 4, Ct), Ct),
            Task.Run(() => other.Claimer.ClaimAsync(right, 4, Ct), Ct));

        Assert.Equal(1, results.Sum(r => r.Count));
        await using var fresh = database.Open();
        Assert.Single(await fresh.Attempts.AsNoTracking().ToListAsync(Ct));
    }

    [Fact]
    public async Task The_claim_transaction_locks_the_database_for_writing_from_the_start()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        await using var transaction = await db.Database.BeginTransactionAsync(Ct);

        using var contender = new SqliteConnection($"Data Source={database.File};Default Timeout=1");
        contender.Open();
        using var insert = contender.CreateCommand();
        insert.CommandText =
            "INSERT INTO roles (public_id, name, builtin, entry_command_json, profile_defaults_json, created_at, updated_at) " +
            "VALUES ('rol_probe', 'probe', 0, '[]', '{}', '2026-09-14 12:00:00', '2026-09-14 12:00:00')";

        var started = DateTimeOffset.UtcNow;
        var refused = Assert.Throws<SqliteException>(() => insert.ExecuteNonQuery());

        // 5 = SQLITE_BUSY: the write lock the claim transaction already holds.
        Assert.Equal(5, refused.SqliteErrorCode);
        Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(5));
        await transaction.RollbackAsync(Ct);
    }

    private static async Task GiveRoleAsync(JasonDbContext db, string name, params string[] entryCommand)
    {
        var role = await db.Roles.SingleAsync(r => r.Name == name, Ct);
        role.EntryCommand = [.. entryCommand];
    }

    private sealed class Harness : IDisposable
    {
        private readonly TempDataDir _dir = new();

        public Harness(Action<DispatcherOptions>? dispatcher = null, RolesOptions? roles = null)
        {
            var clock = new FixedClock(Noon);
            var options = TestOptions.Dispatcher(dispatcher);
            var journal = new JournalWriter(clock);
            Claimer = new Claimer(
                journal,
                clock,
                new AttemptOutcomes(journal, clock, options),
                new EntryCommandResolver(new TestOptionsMonitor<RolesOptions>(roles ?? new RolesOptions())),
                options,
                _dir.Paths,
                NullLogger<Claimer>.Instance);
        }

        public JasonPaths Paths => _dir.Paths;

        public Claimer Claimer { get; }

        public void Dispose() => _dir.Dispose();
    }
}
