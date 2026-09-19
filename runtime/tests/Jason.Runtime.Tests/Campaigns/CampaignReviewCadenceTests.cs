using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Campaigns;

/// <summary>
/// A campaign that wants to be looked at more often than the rest of the installation, and the bounds it is
/// held to anyway. It is a field beside the execution profile rather than a key in the context, because the
/// dispatcher reads it — and the dispatcher must never read a context for meaning.
/// </summary>
public class CampaignReviewCadenceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task A_campaign_may_carry_its_own_review_cadence()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var clock = new FixedClock(Noon);
        var service = NewService(db, clock);
        var created = await service.CreateAsync(new CampaignCreateRequest("LatAm", null, null, null), Ct);

        var updated = await service.UpdateAsync(Patch(created.Id, 600), Ct);

        Assert.Equal(600, updated.ReviewSeconds);
        Assert.Equal(600, db.Campaigns.Single().ManagerReviewSeconds);

        var entry = await db.Journal.SingleAsync(e => e.Key == "review_seconds", Ct);
        Assert.Equal(JournalKinds.CampaignUpdated, entry.Kind);
        Assert.Equal(600, (int?)entry.New);
    }

    /// <summary>
    /// Every review is a launch on somebody's plan, so a campaign cannot buy itself a tighter loop than the
    /// installation allows: the bounds are the ones <c>Manager:ReviewSeconds</c> is held to.
    /// </summary>
    [Theory]
    [InlineData(299)]
    [InlineData(604_801)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task A_cadence_outside_the_bounds_is_refused(int seconds)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));
        var created = await service.CreateAsync(new CampaignCreateRequest("LatAm", null, null, null), Ct);

        var error = await Assert.ThrowsAsync<ValidationException>(() => service.UpdateAsync(Patch(created.Id, seconds), Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("review_seconds", detail.Field);
        Assert.Equal("out_of_range", detail.Code);
        Assert.Contains("300", detail.Message, StringComparison.Ordinal);
        Assert.Contains("604800", detail.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(300)]
    [InlineData(604_800)]
    public async Task The_bound_itself_is_allowed(int seconds)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));
        var created = await service.CreateAsync(new CampaignCreateRequest("LatAm", null, null, null), Ct);

        Assert.Equal(seconds, (await service.UpdateAsync(Patch(created.Id, seconds), Ct)).ReviewSeconds);
    }

    /// <summary>
    /// Absent leaves it alone and null takes it away, which is the difference every patch in this API makes and
    /// the reason the field is an <c>Optional</c> rather than a nullable.
    /// </summary>
    [Fact]
    public async Task Clearing_it_puts_the_campaign_back_on_the_installations_cadence()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));
        var created = await service.CreateAsync(new CampaignCreateRequest("LatAm", null, null, null), Ct);
        await service.UpdateAsync(Patch(created.Id, 600), Ct);

        var renamed = await service.UpdateAsync(
            new CampaignUpdateRequest(created.Id, Optional<string?>.Of("EMEA"), Optional<string?>.Absent, Optional<int?>.Absent, null, null),
            Ct);
        Assert.Equal(600, renamed.ReviewSeconds);

        var cleared = await service.UpdateAsync(Patch(created.Id, null), Ct);
        Assert.Null(cleared.ReviewSeconds);
        Assert.Null(db.Campaigns.Single().ManagerReviewSeconds);
    }

    /// <summary>Asking for what it already says writes nothing: a retry is never a second line in the chronicle.</summary>
    [Fact]
    public async Task Setting_the_same_cadence_twice_writes_one_entry()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));
        var created = await service.CreateAsync(new CampaignCreateRequest("LatAm", null, null, null), Ct);

        await service.UpdateAsync(Patch(created.Id, 600), Ct);
        await service.UpdateAsync(Patch(created.Id, 600), Ct);

        Assert.Equal(1, await db.Journal.CountAsync(e => e.Key == "review_seconds", Ct));
    }

    private static CampaignUpdateRequest Patch(string campaignId, int? seconds) =>
        new(campaignId, Optional<string?>.Absent, Optional<string?>.Absent, Optional<int?>.Of(seconds), null, null);

    private static CampaignService NewService(JasonDbContext db, TimeProvider clock) =>
        new(db, new JournalWriter(clock), clock, TestCanceller.New(clock));
}
