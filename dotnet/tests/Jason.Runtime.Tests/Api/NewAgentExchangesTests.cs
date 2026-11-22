using Jason.Contracts.Api;

namespace Jason.Runtime.Tests.Api;

/// <summary>
/// The exchanges the console reads for an agent Jason Desktop made at a click on New GTM agent: a draft named
/// "New agent 1" with no brief and no one in it, as the agents' list, its counts and the agent's own read answer it, and
/// the same draft once a brief is written, which keeps Start.
/// </summary>
public sealed class NewAgentExchangesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Nine = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_draft_made_at_the_click_has_no_brief_and_no_one_in_it()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));
        var made = await api.PostOkAsync<CampaignDto>(
            Operations.CampaignCreate,
            new { name = "New agent 1", idempotency_key = "desktop-agent:0b5c1e4f-1d7a-4c3e-9f6b-2a8d4e6f8a10" },
            Ct);

        var listed = await api.RecordAsync<Page<CampaignSummaryDto>>(Operations.CampaignList, "new-agent", new { limit = 100 }, Ct);
        var counts = await api.RecordAsync<CampaignSummaryResponse>(
            Operations.CampaignSummary,
            "new-agent",
            new { campaign_ids = new[] { made.Id }, exclude_archived_contacts = true },
            Ct);
        var read = await api.RecordAsync<CampaignDto>(Operations.CampaignGet, "new-agent", new { campaign_id = made.Id }, Ct);

        Assert.Equal([("New agent 1", CampaignStatus.Draft)], listed.Items.Select(each => (each.Name, each.Status)));
        Assert.Equal(0, Assert.Single(counts.Campaigns).People);
        Assert.Equal((CampaignStatus.Draft, 0), (read.Status, read.Context.Count));
    }

    [Fact]
    public async Task A_draft_made_at_the_click_with_a_brief_and_no_one_in_it()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));
        var made = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "New agent 1" }, Ct);
        await api.PostOkAsync<CampaignDto>(
            Operations.CampaignUpdateContext,
            new { campaign_id = made.Id, set = new { offer = "A two-week pilot of the reconciliation tool." } },
            Ct);

        var read = await api.RecordAsync<CampaignDto>(Operations.CampaignGet, "new-agent-brief", new { campaign_id = made.Id }, Ct);

        Assert.Equal((CampaignStatus.Draft, "A two-week pilot of the reconciliation tool."), (read.Status, (string?)read.Context["offer"]));
    }
}
