using Jason.Runtime.Audiences;
using Jason.Runtime.Tests.Approvals;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Persistence;

/// <summary>
/// Each read the claim and the preview make of a campaign's audiences finds its row through one of the campaign's two
/// unique indexes, so it costs the same at a campaign's thousandth audience, and in an installation of many campaigns,
/// as at its first.
/// </summary>
public class AudiencesSchemaTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The three reads — an audience by its number, by its content, and the campaign's highest number — are searches of
    /// one campaign's rows by an index: none of them reads the table whole, whatever the installation holds.
    /// </summary>
    [Fact]
    public async Task Each_read_of_a_campaign_s_audiences_searches_one_of_its_indexes()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        var byNumber = await QueryPlans.PlanAsync(db, CampaignAudiences.ByNumber(db, 1, 2).ToQueryString(), Ct);
        var byDefinition = await QueryPlans.PlanAsync(db, CampaignAudiences.ByDefinition(db, 1, "sha256:software").ToQueryString(), Ct);
        var latest = await QueryPlans.PlanAsync(db, CampaignAudiences.Latest(db, 1).ToQueryString(), Ct);

        Assert.Equal(["SEARCH a USING INDEX ix_audiences_campaign_id_number (campaign_id=? AND number=?)"], byNumber);
        Assert.Equal(["SEARCH a USING INDEX ix_audiences_campaign_id_definition_hash (campaign_id=? AND definition_hash=?)"], byDefinition);
        Assert.Equal(["SEARCH a USING COVERING INDEX ix_audiences_campaign_id_number (campaign_id=?)"], latest);
    }
}
