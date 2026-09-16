using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Ids;
using Jason.Contracts.Plugins;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.WorkItems;
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
        Assert.Contains("campaign.get", attempt.Error.Message, StringComparison.Ordinal);
        Assert.Null(attempt.Launch);
    }

    [Fact]
    public async Task A_routed_provider_operation_is_claimed_with_the_plan_that_will_run_it()
    {
        using var harness = new Harness(
            plugin: TestPlugins.Loaded("stand-in-provider", ["campaign.get"]),
            route: new Route("stand-in-provider", new JsonObject { ["workspace"] = "west" }, "sha256:west"));
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);
        var item = WorkItemFactory.NewProviderOp(campaign, now: Noon);
        item.Context = new JsonObject
        {
            [WorkItemService.InputKey] = new JsonObject { ["campaign"] = new JsonObject { ["external_id"] = "c-7714" } },
        };
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);

        var work = Assert.Single(await harness.Claimer.ClaimAsync(db, 4, Ct));

        Assert.Equal(WorkItemStatus.Scheduled, item.Status);
        var plan = work.Plan;
        Assert.NotNull(plan);
        Assert.Equal("campaign.get", plan.Contract.Id);
        Assert.Equal("stand-in-provider", plan.Plugin.Manifest.Id);
        Assert.Equal(RouteScope.GlobalDefault, plan.Scope);
        Assert.Equal("sha256:west", plan.BindingIdentity);
        Assert.Equal("c-7714", (string?)plan.Input["args"]!["campaign"]!["external_id"]);
        Assert.Equal(campaign.PublicId, (string?)plan.Input["campaign"]!["id"]);
    }

    /// <summary>
    /// The pins are read inside the claim and they decide the claim: this item names the campaign by nothing but
    /// the identifier Jason already holds, so an unread pin would fail it for an input that is in fact complete.
    /// What another plugin calls the same campaign never travels.
    /// </summary>
    [Fact]
    public async Task The_plan_carries_the_routed_plugin_s_identifiers_and_no_other_plugin_s()
    {
        using var harness = new Harness(
            plugin: TestPlugins.Loaded("stand-in-provider", ["campaign.get"]),
            route: new Route("stand-in-provider", null, null));
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        campaign.ExternalIds.Add(Pin("stand-in-provider", "c-7714"));
        campaign.ExternalIds.Add(Pin("other-provider", "OTHER-1"));
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(WorkItemFactory.NewProviderOp(campaign, now: Noon));
        await db.SaveChangesAsync(Ct);

        var work = Assert.Single(await harness.Claimer.ClaimAsync(db, 4, Ct));

        var identifiers = work.Plan!.Input["campaign"]!["external_ids"]!.AsObject();
        Assert.Equal("c-7714", (string?)identifiers["campaign"]);
        Assert.Single(identifiers);
    }

    /// <summary>The composed input is validated as a whole, and an item it refuses never reaches a child.</summary>
    [Fact]
    public async Task A_provider_operation_whose_composed_input_is_refused_fails_before_anything_starts()
    {
        using var harness = new Harness(
            plugin: TestPlugins.Loaded("stand-in-provider", ["campaign.get"]),
            route: new Route("stand-in-provider", null, null));
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        db.Campaigns.Add(campaign);

        // Nothing names the provider's campaign: no argument, and no pin either.
        var item = WorkItemFactory.NewProviderOp(campaign, now: Noon);
        item.Context = new JsonObject { ["note"] = "what the planner wrote" };
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        var attempt = await fresh.Attempts.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(AttemptErrors.InputInvalid, attempt.Error!.Code);
        Assert.Equal(FailureClass.Validation, attempt.Error.Class);
        Assert.False(attempt.Error.Retriable);
        Assert.Equal("/", Assert.Single(attempt.Error.Details!).Field);
        Assert.Null(attempt.Launch);

        // The attempt is kept, and it is kept with what the item said at the moment it was refused.
        Assert.Equal("what the planner wrote", (string?)attempt.ContextSnapshot["note"]);
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

    private static ExternalId Pin(string pluginId, string value) => new()
    {
        PluginId = pluginId,
        Kind = "campaign",
        Value = value,
        RecordedAt = Noon,
        RecordedByAttemptId = PublicId.New("att"),
    };

    private static async Task GiveRoleAsync(JasonDbContext db, string name, params string[] entryCommand)
    {
        var role = await db.Roles.SingleAsync(r => r.Name == name, Ct);
        role.EntryCommand = [.. entryCommand];
    }

    private sealed class Harness : IDisposable
    {
        private readonly TempDataDir _dir = new();

        public Harness(
            Action<DispatcherOptions>? dispatcher = null,
            RolesOptions? roles = null,
            LoadedPlugin? plugin = null,
            Route? route = null)
        {
            var clock = new FixedClock(Noon);
            var options = TestOptions.Dispatcher(dispatcher);
            var journal = new JournalWriter(clock);
            var plugins = new PluginRegistry(clock);
            if (plugin is not null)
            {
                plugins.Replace(
                    new PluginSnapshot(PublicId.New(PluginProtocol.SnapshotIdPrefix), Noon, SnapshotSource.Startup, [plugin]),
                    new ReloadReport(Noon, SnapshotSource.Startup, Activated: true, [], []));
            }

            var routing = new RouteRegistry(clock, plugins);
            if (route is not null)
            {
                routing.Replace(new RouteSnapshot(
                    PublicId.New(RouteSnapshot.IdPrefix),
                    Noon,
                    plugins.Snapshot.Id,
                    new RouteSet(route, RouteSet.Empty.Operations),
                    RouteSnapshot.Empty(Noon, plugins.Snapshot.Id).Campaigns));
            }

            Claimer = new Claimer(
                journal,
                clock,
                new AttemptOutcomes(journal, clock, options),
                new EntryCommandResolver(new TestOptionsMonitor<RolesOptions>(roles ?? new RolesOptions())),
                plugins,
                routing,
                new ExternalIdStore(journal, clock),
                new NothingIsSuppressed(),
                options,
                _dir.Paths,
                NullLogger<Claimer>.Instance);
        }

        public JasonPaths Paths => _dir.Paths;

        public Claimer Claimer { get; }

        public void Dispose() => _dir.Dispose();

        /// <summary>The register these tests are not about; the suppression check itself is tested where it lives.</summary>
        private sealed class NothingIsSuppressed : ISuppressionCheck
        {
            public Task<bool> IsSuppressedAsync(JasonDbContext db, string channel, string value, CancellationToken cancellationToken) =>
                Task.FromResult(false);
        }
    }
}
