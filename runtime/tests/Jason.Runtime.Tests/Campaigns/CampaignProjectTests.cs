using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Json;
using Jason.Runtime.Persistence;
using Jason.Runtime.Projects;
using Jason.Runtime.Tests.Harness;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Campaigns;

/// <summary>
/// Every campaign is in exactly one project: the one it was created in, or the default when it named none, until it is
/// moved. A campaign's answers name its project by id and by slug, read with the campaign rather than asked for again.
/// </summary>
public class CampaignProjectTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_campaign_that_names_no_project_is_in_the_default()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var standing = await db.Projects.AsNoTracking().SingleAsync(p => p.IsDefault, Ct);

        var campaign = await Campaigns(db).CreateAsync(new CampaignCreateRequest("Autumn outreach", null, null, null), Ct);

        Assert.Equal((standing.PublicId, "default"), (campaign.ProjectId, campaign.ProjectSlug));
        Assert.Equal(standing.Id, (await db.Campaigns.AsNoTracking().SingleAsync(c => c.PublicId == campaign.Id, Ct)).ProjectId);
    }

    [Fact]
    public async Task A_campaign_created_in_a_project_is_there_in_every_answer()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var acme = await Projects(db).CreateAsync(new ProjectCreateRequest("Acme", null, null, null), Ct);
        var service = Campaigns(db);

        var campaign = await service.CreateAsync(new CampaignCreateRequest("Q3 leads", null, null, null, ProjectId: acme.Id), Ct);

        Assert.Equal((acme.Id, "acme"), (campaign.ProjectId, campaign.ProjectSlug));
        db.ChangeTracker.Clear();
        var read = await service.GetAsync(new CampaignGetRequest(campaign.Id), Ct);
        Assert.Equal((acme.Id, "acme"), (read.ProjectId, read.ProjectSlug));
        var listed = Assert.Single((await service.ListAsync(new CampaignListRequest(null, null, null), Ct)).Items);
        Assert.Equal((acme.Id, "acme"), (listed.ProjectId, listed.ProjectSlug));
        var started = await service.StartAsync(new CampaignTransitionRequest(campaign.Id, null, null), Ct);
        Assert.Equal((acme.Id, "acme"), (started.ProjectId, started.ProjectSlug));
    }

    [Fact]
    public async Task A_campaign_in_a_project_nobody_made_is_refused_and_nothing_is_written()
    {
        using var database = new TestDatabase();
        using var db = database.Open();

        var refused = await Assert.ThrowsAsync<NotFoundException>(
            () => Campaigns(db).CreateAsync(new CampaignCreateRequest("Q3", null, null, null, ProjectId: "prj_01K7QN0000000000000000NONE"), Ct));

        Assert.Equal("project_not_found", refused.Code);
        Assert.Equal(0, await db.Campaigns.CountAsync(Ct));
        Assert.Equal(0, await db.Journal.CountAsync(Ct));

        var blank = await Assert.ThrowsAsync<ValidationException>(() => Campaigns(db).CreateAsync(new CampaignCreateRequest("Q3", null, null, null, ProjectId: "  "), Ct));
        Assert.Equal(["project_id/invalid"], blank.Details!.Select(detail => $"{detail.Field}/{detail.Code}"));
    }

    /// <summary>
    /// The key's digest names the project only when the request did, so a digest stored before projects existed still
    /// matches the request that made it; and a request under the same key in another project is a different request.
    /// </summary>
    [Fact]
    public async Task A_key_names_the_project_only_when_the_request_did()
    {
        Assert.Equal(
            CanonicalJson.Hash(new JsonObject { ["name"] = "Q3", ["context"] = new JsonObject { ["icp"] = "founders" } }),
            CreationKey.Digest(new CampaignCreateRequest("Q3", new JsonObject { ["icp"] = "founders" }, null, null, "launch-q3")));
        Assert.Equal(
            CanonicalJson.Hash(new JsonObject { ["name"] = "Q3", ["project_id"] = "prj_01K7QN00000000000000000001" }),
            CreationKey.Digest(new CampaignCreateRequest("Q3", null, null, null, "launch-q3", " prj_01K7QN00000000000000000001 ")));

        using var database = new TestDatabase();
        using var db = database.Open();
        var acme = await Projects(db).CreateAsync(new ProjectCreateRequest("Acme", null, null, null), Ct);
        var service = Campaigns(db);
        var made = await service.CreateAsync(new CampaignCreateRequest("Q3", null, null, null, "launch-q3", acme.Id), Ct);

        Assert.Equal(made.Id, (await service.CreateAsync(new CampaignCreateRequest("Q3", null, null, null, "launch-q3", acme.Id), Ct)).Id);
        var elsewhere = await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(new CampaignCreateRequest("Q3", null, null, null, "launch-q3"), Ct));
        Assert.Equal("idempotency_key_reused", elsewhere.Code);
    }

    /// <summary>
    /// Moving a campaign is an update of its project, said once in its chronicle by the projects' ids; moving it to where
    /// it is says nothing.
    /// </summary>
    [Fact]
    public async Task A_campaign_moved_to_another_project_is_there_and_the_move_is_said_once()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var standing = await db.Projects.AsNoTracking().SingleAsync(p => p.IsDefault, Ct);
        var acme = await Projects(db).CreateAsync(new ProjectCreateRequest("Acme", null, null, null), Ct);
        var service = Campaigns(db);
        var campaign = await service.CreateAsync(new CampaignCreateRequest("Q3", null, null, null), Ct);

        var moved = await service.UpdateAsync(Move(campaign.Id, acme.Id, "it belongs to the product"), Ct);

        Assert.Equal((acme.Id, "acme"), (moved.ProjectId, moved.ProjectSlug));
        var line = await db.Journal.AsNoTracking().OrderBy(e => e.Id).LastAsync(Ct);
        Assert.Equal((JournalKinds.CampaignUpdated, "project_id"), (line.Kind, line.Key));
        Assert.Equal((standing.PublicId, acme.Id), ((string?)line.Old, (string?)line.New));
        Assert.Equal("it belongs to the product", line.Reason);
        Assert.Equal(campaign.Id, (await db.Campaigns.AsNoTracking().SingleAsync(c => c.Id == line.CampaignId, Ct)).PublicId);

        var lines = await db.Journal.CountAsync(Ct);
        var again = await service.UpdateAsync(Move(campaign.Id, acme.Id), Ct);
        Assert.Equal((moved.ProjectId, moved.UpdatedAt), (again.ProjectId, again.UpdatedAt));
        Assert.Equal(lines, await db.Journal.CountAsync(Ct));
    }

    [Fact]
    public async Task A_campaign_is_never_moved_out_of_every_project_nor_to_one_nobody_made_nor_once_archived()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var acme = await Projects(db).CreateAsync(new ProjectCreateRequest("Acme", null, null, null), Ct);
        var service = Campaigns(db);
        var campaign = await service.CreateAsync(new CampaignCreateRequest("Q3", null, null, null), Ct);

        var nowhere = await Assert.ThrowsAsync<ValidationException>(() => service.UpdateAsync(Move(campaign.Id, null), Ct));
        Assert.Equal(["project_id/required"], nowhere.Details!.Select(detail => $"{detail.Field}/{detail.Code}"));
        var unknown = await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateAsync(Move(campaign.Id, "prj_01K7QN0000000000000000NONE"), Ct));
        Assert.Equal("project_not_found", unknown.Code);

        await service.ArchiveAsync(new CampaignTransitionRequest(campaign.Id, null, null), Ct);
        var archived = await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(Move(campaign.Id, acme.Id), Ct));
        Assert.Equal("campaign_archived", archived.Code);
        Assert.Equal("default", (await service.GetAsync(new CampaignGetRequest(campaign.Id), Ct)).ProjectSlug);
    }

    [Fact]
    public async Task A_listing_by_project_keeps_that_project_s_campaigns()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var acme = await Projects(db).CreateAsync(new ProjectCreateRequest("Acme", null, null, null), Ct);
        var service = Campaigns(db);
        var first = await service.CreateAsync(new CampaignCreateRequest("Q3 leads", null, null, null, ProjectId: acme.Id), Ct);
        await service.CreateAsync(new CampaignCreateRequest("Elsewhere", null, null, null), Ct);
        var second = await service.CreateAsync(new CampaignCreateRequest("Q4 leads", null, null, null, ProjectId: acme.Id), Ct);
        var gone = await service.CreateAsync(new CampaignCreateRequest("Q2 leads", null, null, null, ProjectId: acme.Id), Ct);
        await service.ArchiveAsync(new CampaignTransitionRequest(gone.Id, null, null), Ct);

        var page = await service.ListAsync(new CampaignListRequest(null, 1, null, ProjectId: acme.Id), Ct);
        Assert.Equal([first.Id], page.Items.Select(c => c.Id));
        var rest = await service.ListAsync(new CampaignListRequest(null, 1, page.NextCursor, ProjectId: acme.Id), Ct);
        Assert.Equal([second.Id], rest.Items.Select(c => c.Id));
        Assert.Null(rest.NextCursor);
        Assert.Equal([second.Id], (await service.ListAsync(new CampaignListRequest(null, null, null, "Q4", ProjectId: acme.Id), Ct)).Items.Select(c => c.Id));
        Assert.Equal([gone.Id], (await service.ListAsync(new CampaignListRequest(CampaignStatus.Archived, null, null, ProjectId: acme.Id), Ct)).Items.Select(c => c.Id));

        var unknown = await Assert.ThrowsAsync<NotFoundException>(() => service.ListAsync(new CampaignListRequest(null, null, null, ProjectId: "prj_01K7QN0000000000000000NONE"), Ct));
        Assert.Equal("project_not_found", unknown.Code);
    }

    private static CampaignUpdateRequest Move(string campaignId, string? projectId, string? reason = null) =>
        new(campaignId, default, default, default, null, reason, Optional<string?>.Of(projectId));

    private static CampaignService Campaigns(JasonDbContext db)
    {
        var clock = new FixedClock(Noon);
        return new CampaignService(db, new JournalWriter(clock), clock, TestCanceller.New(clock), TestHarness.FoldersFor(db));
    }

    private static ProjectService Projects(JasonDbContext db)
    {
        var clock = new FixedClock(Noon);
        return new ProjectService(db, new JournalWriter(clock), clock, TestHarness.FoldersFor(db));
    }
}
