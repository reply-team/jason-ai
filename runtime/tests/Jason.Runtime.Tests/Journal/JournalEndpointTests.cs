using System.Net;
using Jason.Contracts.Api;
using Jason.Runtime.Journal;

namespace Jason.Runtime.Tests.Journal;

public class JournalEndpointTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_entry_appended_by_a_role_comes_back_at_the_head_of_the_listing()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "LatAm" }, Ct);

        var appended = await api.PostOkAsync<JournalEntryDto>(
            Operations.JournalAppend,
            new
            {
                CampaignId = campaign.Id,
                Kind = "plan_revision",
                Key = "step_2",
                New = "send on tuesday",
                Actor = new { Type = "role", Id = "planner" },
                Reason = "the buyer asked for a week",
            },
            Ct);

        Assert.StartsWith("jrn_", appended.Id, StringComparison.Ordinal);
        Assert.Equal(new ActorRef(ActorType.Role, "planner"), appended.Actor);
        Assert.Equal(campaign.Id, appended.CampaignId);

        var page = await api.PostOkAsync<Page<JournalEntryDto>>(Operations.JournalList, new { CampaignId = campaign.Id }, Ct);

        Assert.Equal(appended.Id, page.Items[0].Id);
        Assert.Equal("send on tuesday", (string?)page.Items[0].New);
        Assert.Equal(JournalKinds.CampaignCreated, page.Items[^1].Kind);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task A_caller_cannot_append_as_the_runtime_itself()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "LatAm" }, Ct);

        var error = await api.PostErrorAsync(
            Operations.JournalAppend,
            new { CampaignId = campaign.Id, Kind = "observation", Actor = new { Type = "system" } },
            HttpStatusCode.BadRequest,
            Ct);

        Assert.Equal("validation_failed", error.Code);
        Assert.Equal("actor.type", Assert.Single(error.Details!).Field);
    }

    [Fact]
    public async Task A_kind_the_runtime_owns_is_refused_over_http()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "LatAm" }, Ct);

        var error = await api.PostErrorAsync(
            Operations.JournalAppend,
            new { CampaignId = campaign.Id, Kind = JournalKinds.CampaignStarted },
            HttpStatusCode.BadRequest,
            Ct);

        Assert.Equal("reserved_kind", error.Code);
    }

    [Fact]
    public async Task Appending_to_an_unknown_campaign_is_a_not_found_envelope()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            Operations.JournalAppend,
            new { CampaignId = "cmp_01JASONNOTHERE", Kind = "observation" },
            HttpStatusCode.NotFound,
            Ct);

        Assert.Equal("campaign_not_found", error.Code);
    }
}
