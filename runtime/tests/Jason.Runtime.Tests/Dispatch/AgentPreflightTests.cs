using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Execution;
using Jason.Runtime.Execution.Hosts;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// Which execution profile runs a piece of agent work, decided where every other refusal of a claim is decided:
/// before a child process exists, with the attempt kept so the refusal can be read, and never retried — nothing
/// about the work changes between two scans, so an item put back would be refused for the same reason for as
/// long as the queue existed.
/// <para>
/// The order itself is proved as a function in <c>ProfileResolutionTests</c>. What is proved here is that the
/// claim obeys it: one test per level that can choose, and the two that decide whether the invariant is real —
/// work whose ancestry cannot be resolved must stop rather than quietly run under the house default, and must
/// run as soon as somebody says which profile to use.
/// </para>
/// </summary>
public class AgentPreflightTests
{
    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_profile_the_item_names_itself_runs_it_and_is_pinned_as_the_items_own_choice()
    {
        using var harness = new Harness(globalDefault: "house");
        using var database = new TestDatabase();
        await using var db = database.Open();
        await SeedProfileAsync(db, "house", "claude");
        await SeedProfileAsync(db, "chosen", "claude");
        var item = await SeedItemAsync(db, configure: w => w.ExecutionProfile = "chosen");

        await harness.Claimer.ClaimAsync(db, 4, Ct);

        var agent = await AgentOf(database, item);
        Assert.Equal(ProfileResolutionSource.WorkItemOverride, agent.ResolutionSource);
        Assert.Equal("chosen", agent.ProfileName);
        Assert.Equal(1, agent.ProfileRevision);
    }

    [Fact]
    public async Task A_campaign_policy_runs_the_work_its_campaign_owns()
    {
        using var harness = new Harness(globalDefault: "house");
        using var database = new TestDatabase();
        await using var db = database.Open();
        await SeedProfileAsync(db, "house", "claude");
        await SeedProfileAsync(db, "by-campaign", "claude");
        var item = await SeedItemAsync(db, campaign: c => c.ExecutionProfile = "by-campaign");

        await harness.Claimer.ClaimAsync(db, 4, Ct);

        var agent = await AgentOf(database, item);
        Assert.Equal(ProfileResolutionSource.CampaignPolicy, agent.ResolutionSource);
        Assert.Equal("by-campaign", agent.ProfileName);
    }

    [Fact]
    public async Task A_role_policy_runs_the_work_of_the_role_that_carries_it()
    {
        using var harness = new Harness(globalDefault: "house");
        using var database = new TestDatabase();
        await using var db = database.Open();
        await SeedProfileAsync(db, "house", "claude");
        await SeedProfileAsync(db, "by-role", "claude");
        var item = await SeedItemAsync(db, rolePolicy: "by-role");

        await harness.Claimer.ClaimAsync(db, 4, Ct);

        var agent = await AgentOf(database, item);
        Assert.Equal(ProfileResolutionSource.RolePolicy, agent.ResolutionSource);
        Assert.Equal("by-role", agent.ProfileName);
    }

    [Fact]
    public async Task What_the_run_that_created_the_work_used_runs_it_too()
    {
        using var harness = new Harness(globalDefault: "house");
        using var database = new TestDatabase();
        await using var db = database.Open();
        await SeedProfileAsync(db, "house", "claude");
        await SeedProfileAsync(db, "inherited", "claude");
        var item = await SeedItemAsync(db, configure: w =>
        {
            w.LineageState = LineageState.Inherited;
            w.LineageProfileName = "inherited";
            w.LineageProfileRevision = 1;
            w.LineageFromAttemptId = "att_ancestor";
        });

        await harness.Claimer.ClaimAsync(db, 4, Ct);

        var agent = await AgentOf(database, item);
        Assert.Equal(ProfileResolutionSource.Lineage, agent.ResolutionSource);
        Assert.Equal("inherited", agent.ProfileName);
        Assert.Equal(1, agent.LineageRevision);
    }

    /// <summary>
    /// Inherited work runs the profile as it stands now, not as it stood when the ancestor ran — otherwise a
    /// profile repaired today could never reach the work that inherited it yesterday. Both numbers are kept, so
    /// "this attempt ran revision 2 and inherited revision 1" is a sentence somebody can read rather than a
    /// difference they have to infer.
    /// </summary>
    [Fact]
    public async Task Inherited_work_runs_the_profile_as_it_stands_now_and_says_what_it_inherited()
    {
        using var harness = new Harness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        await SeedProfileAsync(db, "moved-on", "claude");
        await ReviseAsync(db, "moved-on");

        var item = await SeedItemAsync(db, configure: w =>
        {
            w.LineageState = LineageState.Inherited;
            w.LineageProfileName = "moved-on";
            w.LineageProfileRevision = 1;
            w.LineageFromAttemptId = "att_ancestor";
        });

        await harness.Claimer.ClaimAsync(db, 4, Ct);

        var agent = await AgentOf(database, item);
        Assert.Equal(ProfileResolutionSource.Lineage, agent.ResolutionSource);
        Assert.Equal(2, agent.ProfileRevision);
        Assert.Equal(1, agent.LineageRevision);
    }

    /// <summary>
    /// A refusal that names nothing is only half an answer. Where a level did choose a profile, the attempt says
    /// which level and which profile, because "disabled" is actionable only beside the name of what is disabled.
    /// Work whose ancestry could not be read is the exception, and it is the honest one: nothing was chosen.
    /// </summary>
    [Fact]
    public async Task A_refusal_records_how_far_the_choice_got()
    {
        using var harness = new Harness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        await SeedProfileAsync(db, "retired", "claude", disabled: true);
        var item = await SeedItemAsync(db, configure: w => w.ExecutionProfile = "retired");

        await harness.Claimer.ClaimAsync(db, 4, Ct);

        var agent = await AgentOf(database, item);
        Assert.Equal(ProfileResolutionSource.WorkItemOverride, agent.ResolutionSource);
        Assert.Equal("retired", agent.ProfileName);
        Assert.Null(agent.Args);
        Assert.Null(agent.SessionId);
    }

    [Fact]
    public async Task Root_work_with_no_policy_anywhere_runs_under_the_house_default()
    {
        using var harness = new Harness(globalDefault: "house");
        using var database = new TestDatabase();
        await using var db = database.Open();
        await SeedProfileAsync(db, "house", "claude");
        var item = await SeedItemAsync(db);

        await harness.Claimer.ClaimAsync(db, 4, Ct);

        var agent = await AgentOf(database, item);
        Assert.Equal(ProfileResolutionSource.GlobalDefault, agent.ResolutionSource);
        Assert.Equal("house", agent.ProfileName);
    }

    /// <summary>
    /// No profile at any level: the role's own entry command runs the work exactly as it did before profiles
    /// existed, and the attempt says so rather than leaving a reader to infer it from the absence of a name.
    /// </summary>
    [Fact]
    public async Task With_no_profile_anywhere_the_roles_own_command_still_runs_the_work_and_is_recorded_as_such()
    {
        using var harness = new Harness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var item = await SeedItemAsync(db);

        var claimed = await harness.Claimer.ClaimAsync(db, 4, Ct);

        Assert.Single(claimed);
        var agent = await AgentOf(database, item);
        Assert.Equal(ProfileResolutionSource.RoleEntryCommand, agent.ResolutionSource);
        Assert.Null(agent.ProfileName);

        await using var fresh = database.Open();
        var attempt = await fresh.Attempts.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(["agent-host", "--serve"], attempt.Launch!.EntryCommand);
    }

    /// <summary>
    /// The invariant this whole level exists for. A run created this work; nothing about that run's profile can
    /// be read; a house default is configured and would happily run it. Using it would change which AI executor
    /// somebody's work runs under without anybody saying so, so the work stops instead — with an attempt, a code
    /// and a sentence naming the repair.
    /// </summary>
    [Fact]
    public async Task An_item_whose_ancestry_cannot_be_resolved_fails_instead_of_using_the_global_default()
    {
        using var harness = new Harness(globalDefault: "house");
        using var database = new TestDatabase();
        await using var db = database.Open();
        await SeedProfileAsync(db, "house", "claude");

        // Written straight at the table: this is the state an upgrade leaves behind, and the point is that the
        // claim reads it as it stands rather than as something it can reconstruct.
        var item = await SeedItemAsync(db, configure: w => w.LineageState = LineageState.Unresolved);

        var claimed = await harness.Claimer.ClaimAsync(db, 4, Ct);

        Assert.Empty(claimed);
        await using var fresh = database.Open();
        var stored = await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.PublicId == item.PublicId, Ct);
        Assert.Equal(WorkItemStatus.Failed, stored.Status);
        Assert.Equal(AttemptErrors.LineageResolutionUnsupported, stored.LastError!.Code);
        Assert.False(stored.LastError.Retriable);

        var attempt = await fresh.Attempts.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(AttemptStatus.Failed, attempt.Status);

        // Nothing was started, and the default it refused to use appears nowhere.
        Assert.Null(attempt.Launch);
        Assert.Null(attempt.Provenance?.Agent);
    }

    [Fact]
    public async Task The_same_item_runs_once_it_is_told_which_profile_to_use()
    {
        using var harness = new Harness(globalDefault: "house");
        using var database = new TestDatabase();
        await using var db = database.Open();
        await SeedProfileAsync(db, "house", "claude");
        await SeedProfileAsync(db, "named-by-hand", "claude");
        var item = await SeedItemAsync(db, configure: w =>
        {
            w.LineageState = LineageState.Unresolved;
            w.ExecutionProfile = "named-by-hand";
        });

        var claimed = await harness.Claimer.ClaimAsync(db, 4, Ct);

        Assert.Single(claimed);
        var agent = await AgentOf(database, item);
        Assert.Equal(ProfileResolutionSource.WorkItemOverride, agent.ResolutionSource);
        Assert.Equal("named-by-hand", agent.ProfileName);
    }

    [Fact]
    public async Task A_profile_that_no_longer_exists_fails_the_attempt_before_a_child_exists()
    {
        using var harness = new Harness(globalDefault: "gone");
        using var database = new TestDatabase();
        await using var db = database.Open();
        var item = await SeedItemAsync(db);

        await harness.Claimer.ClaimAsync(db, 4, Ct);

        await using var fresh = database.Open();
        var stored = await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.PublicId == item.PublicId, Ct);
        Assert.Equal(AttemptErrors.ProfileNotFound, stored.LastError!.Code);
        Assert.Contains("Roles:DefaultExecutionProfile", stored.LastError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_disabled_profile_fails_the_attempt_before_a_child_exists()
    {
        using var harness = new Harness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        await SeedProfileAsync(db, "retired", "claude", disabled: true);
        var item = await SeedItemAsync(db, configure: w => w.ExecutionProfile = "retired");

        var claimed = await harness.Claimer.ClaimAsync(db, 4, Ct);

        Assert.Empty(claimed);
        await using var fresh = database.Open();
        var stored = await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.PublicId == item.PublicId, Ct);
        Assert.Equal(AttemptErrors.ProfileDisabled, stored.LastError!.Code);
        Assert.False(stored.LastError.Retriable);

        var attempt = await fresh.Attempts.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Null(attempt.Launch);
    }

    /// <summary>
    /// A profile is a name plus a program, and a program is a fact about the machine. Deciding it here is what
    /// keeps "the host is not installed" a configuration problem somebody can read, rather than a child that
    /// failed to start for reasons buried in an exit code.
    /// </summary>
    [Fact]
    public async Task A_host_this_machine_does_not_have_fails_the_attempt_with_the_program_it_looked_for()
    {
        using var harness = new Harness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        await SeedProfileAsync(db, "elsewhere", "a-host-nobody-installed");
        var item = await SeedItemAsync(db, configure: w => w.ExecutionProfile = "elsewhere");

        await harness.Claimer.ClaimAsync(db, 4, Ct);

        await using var fresh = database.Open();
        var stored = await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.PublicId == item.PublicId, Ct);
        Assert.Equal(AttemptErrors.HostNotAvailable, stored.LastError!.Code);
        Assert.Contains("a-host-nobody-installed", stored.LastError.Message, StringComparison.Ordinal);
    }

    private static async Task<AgentProvenanceDto> AgentOf(TestDatabase database, WorkItem item)
    {
        await using var db = database.Open();
        var attempt = await db.Attempts.AsNoTracking()
            .Include(a => a.WorkItem)
            .SingleAsync(a => a.WorkItem!.PublicId == item.PublicId, Ct);
        return attempt.Provenance!.Agent!;
    }

    /// <summary>A second revision, so that "the one in force" and "the one inherited" are different numbers.</summary>
    private static async Task ReviseAsync(JasonDbContext db, string name)
    {
        var profile = await db.ExecutionProfiles.Include(p => p.Revisions).SingleAsync(p => p.Name == name, Ct);
        profile.Revisions.Add(new ExecutionProfileRevision
        {
            Number = profile.CurrentRevision + 1,
            Host = AgentHostKind.ClaudeCode,
            Program = "claude",
            Deny = ["Write", "WebFetch"],
            CreatedByType = ActorType.Human,
            CreatedAt = Noon,
        });

        profile.CurrentRevision += 1;
        await db.SaveChangesAsync(Ct);
    }

    private static async Task SeedProfileAsync(JasonDbContext db, string name, string program, bool disabled = false)
    {
        var profile = new ExecutionProfile
        {
            PublicId = PublicId.New("prf"),
            Name = name,
            CurrentRevision = 1,
            DisabledAt = disabled ? Noon : null,
            CreatedAt = Noon,
            UpdatedAt = Noon,
        };

        profile.Revisions.Add(new ExecutionProfileRevision
        {
            Number = 1,
            Host = AgentHostKind.ClaudeCode,
            Program = program,
            Deny = ["Write"],
            CreatedByType = ActorType.Human,
            CreatedAt = Noon,
        });

        db.ExecutionProfiles.Add(profile);
        await db.SaveChangesAsync(Ct);
    }

    private static async Task<WorkItem> SeedItemAsync(
        JasonDbContext db,
        Action<WorkItem>? configure = null,
        Action<Campaign>? campaign = null,
        string? rolePolicy = null)
    {
        var owner = WorkItemFactory.NewCampaign(now: Noon);
        campaign?.Invoke(owner);
        var item = WorkItemFactory.NewAiRole(owner, now: Noon, configure: w =>
        {
            w.Context = new JsonObject { ["brief"] = "find the decision maker" };
            configure?.Invoke(w);
        });

        db.Campaigns.Add(owner);
        db.WorkItems.Add(item);

        // The roster ships with the migration, so the role is edited rather than added.
        var role = await db.Roles.SingleAsync(r => r.Name == "researcher", Ct);
        role.EntryCommand = ["agent-host", "--serve"];
        role.ExecutionProfile = rolePolicy;

        await db.SaveChangesAsync(Ct);
        return item;
    }

    /// <summary>
    /// The claim, with a machine that has exactly one program on it. The search path is stated rather than
    /// inherited so these tests say the same thing on every machine they run on.
    /// </summary>
    private sealed class Harness : IDisposable
    {
        private readonly TempDataDir _dir = new();

        public Harness(string? globalDefault = null)
        {
            var clock = new FixedClock(Noon);
            var settings = TestOptions.Settings();
            var journal = new JournalWriter(clock);
            var plugins = new PluginRegistry(clock);
            var roles = TestOptions.RoleSettings(new RolesOptions { DefaultExecutionProfile = globalDefault });

            Claimer = new Claimer(
                journal,
                clock,
                new AttemptOutcomes(journal, clock, settings),
                new EntryCommandResolver(roles),
                new AgentLaunchPlanner(
                    new AgentPreflight(),
                    new ProgramResolver(new OneProgramOnly(_dir.Paths.Root)),
                    [new ClaudeCodeHost()]),
                roles,
                plugins,
                new RouteRegistry(clock, plugins),
                new ExternalIdStore(journal, clock),
                new NothingIsSuppressed(),
                settings,
                _dir.Paths,
                NullLogger<Claimer>.Instance);
        }

        public Claimer Claimer { get; }

        public void Dispose() => _dir.Dispose();

        /// <summary>A search path holding one directory, in which only <c>claude</c> can be found.</summary>
        private sealed class OneProgramOnly : ISearchPath
        {
            public OneProgramOnly(string root)
            {
                Directory.CreateDirectory(root);
                var name = OperatingSystem.IsWindows() ? "claude.exe" : "claude";
                var program = System.IO.Path.Combine(root, name);
                File.WriteAllText(program, "#!/bin/sh\nexit 0\n");
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(program, UnixFileMode.UserRead | UnixFileMode.UserExecute);
                }

                Path = root;
            }

            public string? Path { get; }

            public string? PathExt => OperatingSystem.IsWindows() ? ".EXE;.COM" : null;
        }

        private sealed class NothingIsSuppressed : ISuppressionCheck
        {
            public Task<bool> IsSuppressedAsync(JasonDbContext db, string channel, string value, CancellationToken cancellationToken) =>
                Task.FromResult(false);
        }
    }
}
