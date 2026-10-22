using System.Net;
using Jason.Contracts.Api;
using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Projects;

/// <summary>
/// The four project operations over the API, each exchange recorded under <c>docs/api/fixtures/</c> for the consumers that
/// render them, and the campaign listing by project beside them.
/// </summary>
public class ProjectEndpointTests
{
    private static readonly DateTimeOffset Nine = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Projects_are_made_read_listed_and_renamed_over_the_api()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));
        await HoldTheDefaultsMomentsAsync(api);

        var acme = await api.RecordAsync<ProjectDto>(Operations.ProjectCreate, "created", new { name = "Acme Corp" }, Ct);
        Assert.Equal(("acme-corp", false), (acme.Slug, acme.Default));
        var europe = await api.RecordAsync<ProjectDto>(Operations.ProjectCreate, "with-slug", new { name = "Acme Europe", slug = "acme-eu" }, Ct);
        Assert.Equal("acme-eu", europe.Slug);

        var exists = await api.RecordErrorAsync(Operations.ProjectCreate, "exists", new { name = "Acme Corp" }, HttpStatusCode.Conflict, Ct);
        Assert.Equal("project_exists", exists.Code);
        Assert.Equal([new ErrorDetail("project_id", "exists", acme.Id)], exists.Details);
        var invalid = await api.RecordErrorAsync(Operations.ProjectCreate, "invalid-slug", new { name = "Acme", slug = "Acme Corp" }, HttpStatusCode.BadRequest, Ct);
        Assert.Equal(["slug/invalid"], invalid.Details!.Select(detail => $"{detail.Field}/{detail.Code}"));

        Assert.Equal(acme, await api.RecordAsync<ProjectDto>(Operations.ProjectGet, "by-id", new { project_id = acme.Id }, Ct));
        var standing = await api.RecordAsync<ProjectDto>(Operations.ProjectGet, "by-slug", new { slug = "default" }, Ct);
        Assert.True(standing is { Default: true, Name: "Default" }, $"The default project read back as {standing}.");
        Assert.Equal(
            "project_not_found",
            (await api.RecordErrorAsync(Operations.ProjectGet, "unknown", new { project_id = "prj_01K7QN00000000000000000099" }, HttpStatusCode.NotFound, Ct)).Code);

        var all = await api.RecordAsync<Page<ProjectDto>>(Operations.ProjectList, "all", new { limit = 10 }, Ct);
        Assert.Equal(["default", "acme-corp", "acme-eu"], all.Items.Select(p => p.Slug));
        var found = await api.RecordAsync<Page<ProjectDto>>(Operations.ProjectList, "query", new { query = "europe", limit = 10 }, Ct);
        Assert.Equal([europe], found.Items);

        var renamed = await api.RecordAsync<ProjectDto>(Operations.ProjectUpdate, "renamed", new { project_id = acme.Id, name = "Acme Inc", reason = "the client's new name" }, Ct);
        Assert.Equal(acme with { Name = "Acme Inc" }, renamed);
    }

    [Fact]
    public async Task A_project_s_campaigns_are_listed_with_the_project_they_are_in()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));
        var acme = await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Acme Corp" }, Ct);
        var leads = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Q3 leads", project_id = acme.Id }, Ct);
        await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Elsewhere" }, Ct);

        var page = await api.RecordAsync<Page<CampaignSummaryDto>>(Operations.CampaignList, "by-project", new { project_id = acme.Id, limit = 10 }, Ct);

        var listed = Assert.Single(page.Items);
        Assert.Equal((leads.Id, acme.Id, "acme-corp"), (listed.Id, listed.ProjectId, listed.ProjectSlug));
        var unknown = await api.PostErrorAsync(Operations.CampaignList, new { project_id = "prj_01K7QN00000000000000000099" }, HttpStatusCode.NotFound, Ct);
        Assert.Equal("project_not_found", unknown.Code);

        // A blank id names no project, and is refused by the field, as campaign.create and campaign.update refuse it.
        var blank = await api.PostErrorAsync(Operations.CampaignList, new { project_id = "  " }, HttpStatusCode.BadRequest, Ct);
        Assert.Equal(["project_id/invalid"], blank.Details!.Select(detail => $"{detail.Field}/{detail.Code}"));
    }

    /// <summary>
    /// An installation whose default project is gone — it can go only by hand — refuses a campaign that names no project
    /// with a code that says so and is not to be retried, and says what to do instead, rather than answering an internal
    /// error that a caller honouring <c>retryable</c> would send again for ever.
    /// </summary>
    [Fact]
    public async Task A_campaign_naming_no_project_where_the_default_is_gone_is_refused_for_good()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));
        using (var connection = new SqliteConnection(JasonDbContext.ConnectionString(api.Paths.DatabaseFile)))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM projects WHERE is_default = 1";
            Assert.Equal(1, command.ExecuteNonQuery());
        }

        var refused = await api.PostErrorAsync(Operations.CampaignCreate, new { name = "Q3 leads" }, HttpStatusCode.NotFound, Ct);

        Assert.True(refused is { Code: "project_not_found", Retryable: false }, $"A campaign with no default to land in was answered {refused}.");
        Assert.Equal("This installation has no default project; name the project with project_id.", refused.Message);
    }

    /// <summary>
    /// A campaign moved over the API is answered in its new project, and a move to nowhere is refused by the field, so a
    /// caller can tell "say nothing about the project" from "take the project away", which is not allowed.
    /// </summary>
    [Fact]
    public async Task A_campaign_is_moved_over_the_api_and_never_out_of_every_project()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));
        var acme = await api.PostOkAsync<ProjectDto>(Operations.ProjectCreate, new { name = "Acme Corp" }, Ct);
        var campaign = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Q3 leads" }, Ct);
        Assert.Equal("default", campaign.ProjectSlug);

        var moved = await api.PostOkAsync<CampaignDto>(Operations.CampaignUpdate, new { campaign_id = campaign.Id, project_id = acme.Id }, Ct);
        Assert.Equal((acme.Id, "acme-corp"), (moved.ProjectId, moved.ProjectSlug));

        var (status, body) = await api.PostRawAsync(Operations.CampaignUpdate, $$"""{"campaign_id":"{{campaign.Id}}","project_id":null}""", Ct);
        Assert.True(status == HttpStatusCode.BadRequest && body.Contains("\"field\":\"project_id\"", StringComparison.Ordinal), $"A move to nowhere answered {(int)status}: {body}");
        Assert.Equal("acme-corp", (await api.PostOkAsync<CampaignDto>(Operations.CampaignGet, new { campaign_id = campaign.Id }, Ct)).ProjectSlug);
    }

    /// <summary>
    /// The default project's moments are those of the migration that made it, which no clock of a test holds; a recording
    /// that answers with it holds them to the recording's own moment first, so its file is the same on every run.
    /// </summary>
    private static async Task HoldTheDefaultsMomentsAsync(RuntimeApiFixture api)
    {
        await using var db = new JasonDbContext(JasonDbContext.CreateOptions(api.Paths.DatabaseFile));
        var standing = await db.Projects.SingleAsync(p => p.IsDefault, Ct);
        standing.CreatedAt = standing.UpdatedAt = Nine.UtcDateTime;
        await db.SaveChangesAsync(Ct);
    }
}
