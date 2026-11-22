using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Configuration;
using Jason.Runtime.Contacts;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Harness;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Campaigns;

/// <summary>
/// The reserve's default: at a campaign's first going live, a campaign whose people come from a search, and that holds no
/// threshold, is given <c>Manager:AudienceThreshold</c> by the runtime, on a line of the runtime's own. A campaign of a list
/// the person brought is given none, a threshold the person set is kept, and no resume sets one, so a threshold cleared
/// after the start stays cleared.
/// </summary>
public class ReserveDefaultTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private static readonly ActorRef Ada = new(ActorType.Human, "ada");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_draft_with_a_search_and_no_threshold_is_started_with_the_default_on_a_line_of_the_runtime_s()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var clock = new FixedClock(Noon);
        var service = NewService(db, clock);
        var id = await DraftAsync(service);
        await SearchAsync(db, id, WorkItemStatus.Created);

        var started = await service.StartAsync(new CampaignTransitionRequest(id, Ada, "go"), Ct);

        // The answer carries the threshold, so the caller sees it, and the row holds it for the reserve step.
        Assert.Equal(new ManagerOptions().AudienceThreshold, started.AudienceThreshold);
        Assert.Equal(20, started.AudienceThreshold);
        Assert.Equal(20, (await db.Campaigns.AsNoTracking().SingleAsync(Ct)).AudienceThreshold);

        // The start is the person's; the default is the runtime's own, on a line of its own, of the kind a change of the
        // threshold writes.
        var lines = await db.Journal.AsNoTracking().OrderBy(line => line.Id).ToListAsync(Ct);
        var start = Assert.Single(lines, line => line.Kind == JournalKinds.CampaignStarted);
        Assert.Equal((ActorType.Human, "ada"), (start.ActorType, start.ActorId));
        var set = Assert.Single(lines, line => line.Kind == JournalKinds.CampaignUpdated && line.Key == "audience_threshold");
        Assert.Equal((Actors.Runtime.Type, Actors.Runtime.Id), (set.ActorType, set.ActorId));
        Assert.Null(set.Old);
        Assert.Equal(20, (int?)set.New);
        Assert.Equal(CampaignService.ReserveDefaultReason, set.Reason);
        Assert.Contains("first start", set.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// A search in every status but <c>cancelled</c> and <c>expired</c> — waiting for the start or for the person's approval,
    /// scheduled, running, waiting, or ended either way — or an audience the campaign already holds is the fact that its
    /// people come from a search.
    /// </summary>
    [Theory]
    [InlineData(WorkItemStatus.Created)]
    [InlineData(WorkItemStatus.AwaitingApproval)]
    [InlineData(WorkItemStatus.Scheduled)]
    [InlineData(WorkItemStatus.Processing)]
    [InlineData(WorkItemStatus.Waiting)]
    [InlineData(WorkItemStatus.Succeeded)]
    [InlineData(WorkItemStatus.Failed)]
    public async Task A_search_in_each_status_that_counts_is_a_campaign_a_search_can_top_up(WorkItemStatus status)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));
        var id = await DraftAsync(service);
        await SearchAsync(db, id, status);

        Assert.Equal(20, (await service.StartAsync(new CampaignTransitionRequest(id, Ada, null), Ct)).AudienceThreshold);
    }

    [Fact]
    public async Task An_audience_the_campaign_holds_is_a_campaign_a_search_can_top_up()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));
        var id = await DraftAsync(service);
        var campaign = await db.Campaigns.SingleAsync(c => c.PublicId == id, Ct);
        db.Audiences.Add(new Audience
        {
            PublicId = "aud_made_up",
            CampaignId = campaign.Id,
            Number = 1,
            Definition = new JsonObject { ["industries"] = new JsonArray("Logistics") },
            DefinitionHash = "sha256:made_up",
            CreatedAt = Noon.UtcDateTime,
        });
        await db.SaveChangesAsync(Ct);

        Assert.Equal(20, (await service.StartAsync(new CampaignTransitionRequest(id, Ada, null), Ct)).AudienceThreshold);
    }

    /// <summary>
    /// A list the person brought is a closed set by design: nothing tops it up, so a threshold would only say it is running
    /// out. A search the person withdrew, or one whose time ran out, is no search either.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData(WorkItemStatus.Cancelled)]
    [InlineData(WorkItemStatus.Expired)]
    public async Task A_campaign_of_a_list_the_person_brought_is_given_no_threshold(WorkItemStatus? withdrawn)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var clock = new FixedClock(Noon);
        var service = NewService(db, clock);
        var id = await DraftAsync(service);
        var memberships = new MembershipService(db, new JournalWriter(clock), clock, TestCanceller.New(clock));
        await memberships.AddAsync(
            new AddContactsRequest(id, null, [.. Enumerable.Range(0, 30).Select(_ => new AddContactsItem(null, "Alex", "Example", null, null, null, null, null))], null, null),
            Ct);
        if (withdrawn is { } status)
        {
            await SearchAsync(db, id, status);
        }

        var started = await service.StartAsync(new CampaignTransitionRequest(id, Ada, null), Ct);

        Assert.Null(started.AudienceThreshold);
        Assert.DoesNotContain(await db.Journal.AsNoTracking().ToListAsync(Ct), line => line.Key == "audience_threshold");
    }

    [Fact]
    public async Task A_threshold_the_person_set_is_kept()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));
        var id = await DraftAsync(service);
        await SearchAsync(db, id, WorkItemStatus.Created);
        await service.UpdateAsync(Threshold(id, 7, null), Ct);

        var started = await service.StartAsync(new CampaignTransitionRequest(id, Ada, null), Ct);

        Assert.Equal(7, started.AudienceThreshold);
        var set = Assert.Single(await db.Journal.AsNoTracking().ToListAsync(Ct), line => line.Key == "audience_threshold");
        Assert.Equal("ada", set.ActorId);
    }

    /// <summary>A resume is no first start: a campaign that went live without a threshold is not given one by waking.</summary>
    [Fact]
    public async Task A_resume_sets_nothing()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));
        var id = await DraftAsync(service);
        var request = new CampaignTransitionRequest(id, Ada, null);
        await service.StartAsync(request, Ct);

        // Its first search came after it went live; a pause and a resume later, the reserve is still the person's to set.
        await SearchAsync(db, id, WorkItemStatus.Succeeded);
        await service.PauseAsync(request, Ct);
        var resumed = await service.StartAsync(request, Ct);

        Assert.Equal(CampaignStatus.Active, resumed.Status);
        Assert.Null(resumed.AudienceThreshold);
        Assert.DoesNotContain(await db.Journal.AsNoTracking().ToListAsync(Ct), line => line.Key == "audience_threshold");
    }

    /// <summary>"None" on purpose: a person who wants none clears it after the start, and it stays cleared.</summary>
    [Fact]
    public async Task A_threshold_cleared_after_the_start_stays_cleared_through_a_pause_and_a_resume()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));
        var id = await DraftAsync(service);
        await SearchAsync(db, id, WorkItemStatus.Created);
        var request = new CampaignTransitionRequest(id, Ada, null);
        Assert.Equal(20, (await service.StartAsync(request, Ct)).AudienceThreshold);

        var cleared = await service.UpdateAsync(Threshold(id, null, "none for this one"), Ct);
        Assert.Null(cleared.AudienceThreshold);
        await service.PauseAsync(request, Ct);
        var resumed = await service.StartAsync(request, Ct);

        Assert.Null(resumed.AudienceThreshold);
        Assert.Null((await db.Campaigns.AsNoTracking().SingleAsync(Ct)).AudienceThreshold);
    }

    /// <summary>The number is the installation's setting, read at each first start, never a constant.</summary>
    [Fact]
    public async Task A_changed_setting_is_used_at_the_next_first_start()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var monitor = new TestOptionsMonitor<ManagerOptions>(new ManagerOptions());
        var service = NewService(db, new FixedClock(Noon), monitor);
        var first = await DraftAsync(service, "First");
        var second = await DraftAsync(service, "Second");
        await SearchAsync(db, first, WorkItemStatus.Created);
        await SearchAsync(db, second, WorkItemStatus.Created);

        Assert.Equal(20, (await service.StartAsync(new CampaignTransitionRequest(first, Ada, null), Ct)).AudienceThreshold);
        monitor.CurrentValue = new ManagerOptions { AudienceThreshold = 35 };
        Assert.Equal(35, (await service.StartAsync(new CampaignTransitionRequest(second, Ada, null), Ct)).AudienceThreshold);

        // The campaign that started first keeps what it was given.
        Assert.Equal(20, (await service.GetAsync(new CampaignGetRequest(first), Ct)).AudienceThreshold);
    }

    /// <summary>Through the runtime as it is composed: the setting from the file, the answer of the start over the API.</summary>
    [Fact]
    public async Task A_running_runtime_starts_a_searched_campaign_with_the_installation_s_setting()
    {
        await using var api = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths => File.WriteAllText(paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false},"Manager":{"AudienceThreshold":30}}"""),
            clock: new FixedClock(Noon));
        var campaign = (await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Autumn" }, Ct)).Id;
        await api.PostOkAsync<WorkItemDto>(Operations.WorkItemCreate, new
        {
            campaign_id = campaign,
            kind = "provider_op",
            operation = "audience.search",
            context = new JsonObject { ["input"] = new JsonObject { ["definition"] = new JsonObject { ["industries"] = new JsonArray("Logistics") }, ["count"] = 25 } },
            actor = new { type = "role", id = "planner" },
        }, Ct);

        var started = await api.PostOkAsync<CampaignDto>(Operations.CampaignStart, new { campaign_id = campaign, actor = new { type = "human", id = "ada" } }, Ct);

        Assert.Equal(30, started.AudienceThreshold);
    }

    private static CampaignUpdateRequest Threshold(string campaign, int? threshold, string? reason) =>
        new(campaign, Optional<string?>.Absent, Optional<string?>.Absent, Optional<int?>.Absent, Ada, reason, AudienceThreshold: Optional<int?>.Of(threshold));

    private static async Task<string> DraftAsync(CampaignService service, string name = "LatAm") =>
        (await service.CreateAsync(new CampaignCreateRequest(name, null, null, null), Ct)).Id;

    /// <summary>A search for the campaign's audience, as the planner leaves one while the campaign is set up.</summary>
    private static async Task SearchAsync(JasonDbContext db, string campaign, WorkItemStatus status)
    {
        var row = await db.Campaigns.AsNoTracking().SingleAsync(c => c.PublicId == campaign, Ct);
        db.WorkItems.Add(new WorkItem
        {
            PublicId = "wi_" + Guid.NewGuid().ToString("N"),
            CampaignId = row.Id,
            Kind = WorkItemKind.ProviderOp,
            Operation = "audience.search",
            Status = status,
            CreatedByType = ActorType.Role,
            CreatedById = "planner",
            CreatedAt = Noon.UtcDateTime,
            UpdatedAt = Noon.UtcDateTime,
        });
        await db.SaveChangesAsync(Ct);
    }

    private static CampaignService NewService(JasonDbContext db, TimeProvider clock, TestOptionsMonitor<ManagerOptions>? monitor = null) =>
        new(db, new JournalWriter(clock), clock, TestCanceller.New(clock), TestHarness.FoldersFor(db), manager: TestOptions.ManagerSettings(monitor));
}
