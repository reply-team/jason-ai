using System.Net;
using Jason.Contracts.Api;

namespace Jason.Runtime.Tests.Campaigns;

public class CampaignEndpointTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_created_campaign_comes_back_as_a_draft_with_an_inline_empty_context()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var (status, body) = await api.PostAsync(Operations.CampaignCreate, new { Name = "LatAm founders" }, Ct);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("\"status\":\"draft\"", body, StringComparison.Ordinal);
        Assert.Contains("\"context\":{}", body, StringComparison.Ordinal);
        Assert.Contains("\"archived_at\":null", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_campaign_is_a_not_found_envelope()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(Operations.CampaignGet, new { CampaignId = "cmp_01JASONNOTHERE" }, HttpStatusCode.NotFound, Ct);

        Assert.Equal("campaign_not_found", error.Code);
        Assert.False(error.Retryable);
    }

    [Fact]
    public async Task Pausing_a_draft_is_a_conflict()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var created = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "LatAm" }, Ct);

        var error = await api.PostErrorAsync(Operations.CampaignPause, new { CampaignId = created.Id }, HttpStatusCode.Conflict, Ct);

        Assert.Equal("invalid_transition", error.Code);
        Assert.False(error.Retryable);
    }

    [Fact]
    public async Task An_empty_name_is_a_validation_failure_that_names_the_field()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(Operations.CampaignCreate, new { Name = "" }, HttpStatusCode.BadRequest, Ct);

        Assert.Equal("validation_failed", error.Code);
        var detail = Assert.Single(error.Details!);
        Assert.Equal("name", detail.Field);
        Assert.Equal("required", detail.Code);
    }

    [Fact]
    public async Task A_caller_cannot_claim_to_be_the_runtime_itself()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            Operations.CampaignCreate,
            new { Name = "LatAm", Actor = new { Type = "system" } },
            HttpStatusCode.BadRequest,
            Ct);

        Assert.Equal("validation_failed", error.Code);
        Assert.Equal("actor.type", Assert.Single(error.Details!).Field);
    }

    [Fact]
    public async Task A_listing_that_fits_one_page_ends_without_a_cursor()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var created = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "LatAm" }, Ct);

        var page = await api.PostOkAsync<Page<CampaignSummaryDto>>(Operations.CampaignList, null, Ct);

        Assert.Equal(created.Id, Assert.Single(page.Items).Id);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task The_lifecycle_runs_end_to_end_over_http()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var created = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "LatAm" }, Ct);

        var started = await api.PostOkAsync<CampaignDto>(Operations.CampaignStart, new { CampaignId = created.Id }, Ct);
        var paused = await api.PostOkAsync<CampaignDto>(Operations.CampaignPause, new { CampaignId = created.Id, Reason = "budget freeze" }, Ct);
        var renamed = await api.PostOkAsync<CampaignDto>(Operations.CampaignUpdate, new { CampaignId = created.Id, Name = "EMEA" }, Ct);
        var described = await api.PostOkAsync<CampaignDto>(
            Operations.CampaignUpdateContext,
            new { CampaignId = created.Id, Set = new { Icp = "founders" } },
            Ct);
        var archived = await api.PostOkAsync<CampaignDto>(Operations.CampaignArchive, new { CampaignId = created.Id }, Ct);

        Assert.Equal(CampaignStatus.Active, started.Status);
        Assert.Equal(CampaignStatus.Paused, paused.Status);
        Assert.Equal("EMEA", renamed.Name);
        Assert.Equal("founders", (string?)described.Context["icp"]);
        Assert.Equal(CampaignStatus.Archived, archived.Status);
        Assert.NotNull(archived.ArchivedAt);
    }
}
