using System.Globalization;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Harness;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Campaigns;

/// <summary>
/// How few untouched people a campaign keeps before its manager is told the reserve is low. It is a field beside the
/// review cadence rather than a key in the context, because the dispatcher reads it; null, the default, means nobody
/// counts.
/// </summary>
public class CampaignAudienceThresholdTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTime Noon = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Set, it is on the campaign and in its chronicle; asked for again, nothing is written; absent from a patch, it is left
    /// alone; null takes it away, with its line. Unset, the campaign's answer says null, as its other fields that may hold
    /// nothing do, rather than leaving it out.
    /// </summary>
    [Fact]
    public async Task A_threshold_is_set_kept_and_cleared_with_one_line_per_change()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var service = NewService(db);
        var created = await service.CreateAsync(new CampaignCreateRequest("LatAm", null, null, null), Ct);
        Assert.Null(created.AudienceThreshold);
        Assert.Contains("\"audience_threshold\":null", System.Text.Json.JsonSerializer.Serialize(created, JasonJson.Options), StringComparison.Ordinal);

        Assert.Equal(50, (await service.UpdateAsync(Patch(created.Id, 50), Ct)).AudienceThreshold);
        await service.UpdateAsync(Patch(created.Id, 50), Ct);
        var renamed = await service.UpdateAsync(
            new CampaignUpdateRequest(created.Id, Optional<string?>.Of("EMEA"), Optional<string?>.Absent, Optional<int?>.Absent, null, null),
            Ct);
        Assert.Equal(50, renamed.AudienceThreshold);

        var cleared = await service.UpdateAsync(Patch(created.Id, null), Ct);
        Assert.Null(cleared.AudienceThreshold);
        Assert.Null(db.Campaigns.Single().AudienceThreshold);

        var lines = await db.Journal.Where(e => e.Key == "audience_threshold").OrderBy(e => e.Id).ToListAsync(Ct);
        Assert.Equal([JournalKinds.CampaignUpdated, JournalKinds.CampaignUpdated], lines.Select(line => line.Kind));
        Assert.Equal(((int?)null, (int?)50), ((int?)lines[0].Old, (int?)lines[0].New));
        Assert.Equal(((int?)50, (int?)null), ((int?)lines[1].Old, (int?)lines[1].New));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(100_000, true)]
    [InlineData(100_001, false)]
    public async Task A_threshold_is_held_to_one_to_a_hundred_thousand(int threshold, bool allowed)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var service = NewService(db);
        var created = await service.CreateAsync(new CampaignCreateRequest("LatAm", null, null, null), Ct);

        if (allowed)
        {
            Assert.Equal(threshold, (await service.UpdateAsync(Patch(created.Id, threshold), Ct)).AudienceThreshold);
            return;
        }

        var error = await Assert.ThrowsAsync<ValidationException>(() => service.UpdateAsync(Patch(created.Id, threshold), Ct));
        var detail = Assert.Single(error.Details!);
        Assert.Equal(("audience_threshold", "out_of_range"), (detail.Field, detail.Code));
        Assert.Contains(ReserveCounter.MaximumThreshold.ToString(CultureInfo.InvariantCulture), detail.Message, StringComparison.Ordinal);
        Assert.Null(db.Campaigns.AsNoTracking().Single().AudienceThreshold);
    }

    private static CampaignUpdateRequest Patch(string campaignId, int? threshold) =>
        new(campaignId, Optional<string?>.Absent, Optional<string?>.Absent, Optional<int?>.Absent, null, null, AudienceThreshold: Optional<int?>.Of(threshold));

    private static CampaignService NewService(JasonDbContext db)
    {
        var clock = new FixedClock(Noon);
        return new(db, new JournalWriter(clock), clock, TestCanceller.New(clock), TestHarness.FoldersFor(db));
    }
}
