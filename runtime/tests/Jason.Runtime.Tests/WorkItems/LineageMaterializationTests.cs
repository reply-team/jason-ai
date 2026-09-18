using Jason.Contracts.Api;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.WorkItems;

/// <summary>
/// What a new work item records about the run that caused it. Read in one hop at creation and written onto the
/// row, so the answer is a fact about this item rather than a walk back through a history that may since have
/// changed.
/// </summary>
public class LineageMaterializationTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Work_created_by_an_attempt_that_pinned_a_profile_inherits_it_in_one_hop()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign();
        var parent = WorkItemFactory.NewAiRole(campaign);
        var attempt = Ran(parent, Pinned("fast-claude", 3));
        Save(db, parent);
        var service = NewService(db);

        var item = await service.CreateAsync(By(campaign.PublicId, attempt), Ct);

        Assert.Equal(LineageState.Inherited, item.Lineage!.State);
        Assert.Equal("fast-claude", item.Lineage.ProfileName);
        Assert.Equal(3, item.Lineage.ProfileRevision);
        Assert.Equal(attempt.PublicId, item.Lineage.FromAttemptId);
    }

    [Fact]
    public async Task Work_a_person_asked_for_is_root()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db);

        var item = await service.CreateAsync(Request(campaign.PublicId), Ct);

        Assert.Equal(LineageState.Root, item.Lineage!.State);
        Assert.Null(item.Lineage.ProfileName);
        Assert.Null(item.Lineage.ProfileRevision);
        Assert.Null(item.Lineage.FromAttemptId);
    }

    /// <summary>
    /// A role is an interactive agent session somebody is sitting in front of. The runtime launched nothing and
    /// can resolve no profile for it, so treating it as ancestry would block the ordinary interactive path.
    /// </summary>
    [Fact]
    public async Task Work_an_interactive_role_asked_for_is_root_too()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db);

        var item = await service.CreateAsync(Request(campaign.PublicId) with { Actor = new ActorRef(ActorType.Role, "planner") }, Ct);

        Assert.Equal(LineageState.Root, item.Lineage!.State);
        Assert.Null(item.Lineage.FromAttemptId);
    }

    /// <summary>
    /// The sentence the one-hop copy exists for: a provider operation created by an agent attempt carries the
    /// record it never uses and hands it on to whatever its own attempt creates.
    /// </summary>
    [Fact]
    public async Task A_provider_operation_carries_the_record_it_never_uses_and_hands_it_on()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign();
        var agentWork = WorkItemFactory.NewAiRole(campaign);
        var agentAttempt = Ran(agentWork, Pinned("fast-claude", 3));
        Save(db, agentWork);
        var service = NewService(db);

        var operation = await service.CreateAsync(ProviderOp(campaign.PublicId) with { Actor = Attempt(agentAttempt) }, Ct);

        Assert.Equal(LineageState.Inherited, operation.Lineage!.State);
        Assert.Equal("fast-claude", operation.Lineage.ProfileName);

        // The provider attempt pins no profile of its own: nothing about it is an agent.
        var stored = await db.WorkItems.FirstAsync(w => w.PublicId == operation.Id, Ct);
        var providerAttempt = Ran(stored, provenance: null);
        await db.SaveChangesAsync(Ct);

        var next = await service.CreateAsync(Request(campaign.PublicId) with { Actor = Attempt(providerAttempt) }, Ct);

        Assert.Equal(LineageState.Inherited, next.Lineage!.State);
        Assert.Equal("fast-claude", next.Lineage.ProfileName);
        Assert.Equal(3, next.Lineage.ProfileRevision);
        Assert.Equal(agentAttempt.PublicId, next.Lineage.FromAttemptId);
    }

    /// <summary>
    /// Ancestry nothing can be read from stays unresolved rather than becoming root: root is work nobody's run
    /// caused, and a claim is allowed to resolve it to the global default.
    /// </summary>
    [Fact]
    public async Task Work_created_by_an_attempt_with_nothing_to_inherit_is_unresolved()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign();

        // What the migration left behind: work a run caused before profiles existed, so nothing pinned one.
        var before = WorkItemFactory.NewAiRole(campaign, configure: item => item.LineageState = LineageState.Unresolved);
        var attempt = Ran(before, provenance: null);
        Save(db, before);
        var service = NewService(db);

        var item = await service.CreateAsync(By(campaign.PublicId, attempt), Ct);

        Assert.Equal(LineageState.Unresolved, item.Lineage!.State);
        Assert.Null(item.Lineage.ProfileName);
        Assert.Null(item.Lineage.FromAttemptId);
    }

    /// <summary>
    /// An attempt this runtime cannot read is not work nobody's run caused: it is ancestry nothing can be said
    /// about, and the two are never collapsed. Asserted on the function itself, because a caller cannot reach it
    /// — <c>workitem.create</c> verifies the actor first and refuses an attempt it does not know, which
    /// <c>WorkItemServiceTests</c> covers — so this is the answer the rule gives when it is asked anyway.
    /// </summary>
    [Fact]
    public async Task An_ancestor_attempt_that_cannot_be_read_is_unresolved_rather_than_root()
    {
        using var database = new TestDatabase();
        using var db = database.Open();

        var record = await Lineage.ForCreationAsync(db, new ActorRef(ActorType.Attempt, "att_01JASONNOTHERE"), Ct);

        Assert.Equal(LineageState.Unresolved, record.State);
        Assert.Null(record.ProfileName);
        Assert.Null(record.FromAttemptId);
    }

    /// <summary>
    /// An attempt that ran under the role's own entry command resolved no profile at all, so there is none to
    /// hand down and nothing being changed behind anybody's back: the ancestor's own record carries on.
    /// </summary>
    [Fact]
    public async Task An_attempt_that_ran_under_no_profile_hands_down_what_its_own_item_held()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign();
        var parent = WorkItemFactory.NewAiRole(campaign);
        var attempt = Ran(parent, Agent(new AgentProvenanceDto(ProfileResolutionSource.RoleEntryCommand)));
        Save(db, parent);
        var service = NewService(db);

        var item = await service.CreateAsync(By(campaign.PublicId, attempt), Ct);

        Assert.Equal(LineageState.Root, item.Lineage!.State);
        Assert.Null(item.Lineage.ProfileName);
    }

    /// <summary>Materialized once: what the ancestry says afterwards is somebody else's history, not this item's.</summary>
    [Fact]
    public async Task A_later_change_to_the_ancestry_leaves_the_record_exactly_as_it_was_written()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign();
        var parent = WorkItemFactory.NewAiRole(campaign);
        var attempt = Ran(parent, Pinned("fast-claude", 3));
        Save(db, parent);
        var service = NewService(db);

        var item = await service.CreateAsync(By(campaign.PublicId, attempt), Ct);

        parent.LineageState = LineageState.Unresolved;
        attempt.Provenance = Pinned("something-else", 9);
        await db.SaveChangesAsync(Ct);

        var read = await service.GetAsync(new WorkItemGetRequest(item.Id, null), Ct);

        Assert.Equal(LineageState.Inherited, read.Lineage!.State);
        Assert.Equal("fast-claude", read.Lineage.ProfileName);
        Assert.Equal(3, read.Lineage.ProfileRevision);
    }

    [Fact]
    public async Task The_row_carries_what_the_answer_carries()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign();
        var parent = WorkItemFactory.NewAiRole(campaign);
        var attempt = Ran(parent, Pinned("fast-claude", 3));
        Save(db, parent);
        var service = NewService(db);

        var item = await service.CreateAsync(By(campaign.PublicId, attempt), Ct);

        var stored = await db.WorkItems.AsNoTracking().FirstAsync(w => w.PublicId == item.Id, Ct);
        Assert.Equal(LineageState.Inherited, stored.LineageState);
        Assert.Equal("fast-claude", stored.LineageProfileName);
        Assert.Equal(3, stored.LineageProfileRevision);
        Assert.Equal(attempt.PublicId, stored.LineageFromAttemptId);
    }

    private static AttemptProvenanceDto Pinned(string profile, int revision) =>
        Agent(new AgentProvenanceDto(ProfileResolutionSource.RolePolicy, ProfileName: profile, ProfileRevision: revision));

    private static AttemptProvenanceDto Agent(AgentProvenanceDto agent) =>
        new(null, null, null, null, null, null, null, null, null, null, null, Agent: agent);

    private static Attempt Ran(WorkItem item, AttemptProvenanceDto? provenance)
    {
        var attempt = WorkItemFactory.NewAttempt(item, item.Attempts.Count + 1, AttemptStatus.Running, Noon.UtcDateTime);
        attempt.Provenance = provenance;
        return attempt;
    }

    private static ActorRef Attempt(Attempt attempt) => new(ActorType.Attempt, attempt.PublicId);

    private static WorkItemCreateRequest By(string campaignId, Attempt attempt) =>
        Request(campaignId) with { Actor = Attempt(attempt) };

    private static WorkItemService NewService(JasonDbContext db)
    {
        var clock = new FixedClock(Noon);
        return new WorkItemService(db, new JournalWriter(clock), clock, TestCanceller.New(clock), TestOptions.PluginSettings());
    }

    private static WorkItemCreateRequest Request(string? campaignId) =>
        new(campaignId, WorkItemKind.AiRole, "researcher", null, null, null, null, null, null, null, null, null, null, null, null, null);

    private static WorkItemCreateRequest ProviderOp(string? campaignId) =>
        new(campaignId, WorkItemKind.ProviderOp, null, "campaign.get", null, null, null, null, null, null, null, null, null, null, null, null);

    private static Campaign Seed(JasonDbContext db, Campaign campaign)
    {
        db.Campaigns.Add(campaign);
        db.SaveChanges();
        return campaign;
    }

    private static void Save(JasonDbContext db, WorkItem item)
    {
        db.WorkItems.Add(item);
        db.SaveChanges();
    }
}
