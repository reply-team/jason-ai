using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;

namespace Jason.Runtime.Tests.WorkItems;

/// <summary>
/// The work item's own execution profile: the most specific level of the resolution order, and therefore a name
/// the runtime has to answer for where it is typed rather than when the work is claimed.
/// </summary>
public class WorkItemProfileOverrideTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_execution_profile_that_names_no_profile_is_refused_at_create()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db);

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.CreateAsync(Request(campaign.PublicId) with { ExecutionProfile = "nobody-installed-this" }, Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("execution_profile", detail.Field);
        Assert.Equal("unknown", detail.Code);
        Assert.DoesNotContain("nobody-installed-this", detail.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_profile_this_runtime_knows_is_accepted_and_stored_trimmed()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        SeedProfile(db, "fast-claude");
        var service = NewService(db);

        var item = await service.CreateAsync(Request(campaign.PublicId) with { ExecutionProfile = "  fast-claude  " }, Ct);

        Assert.Equal("fast-claude", item.ExecutionProfile);
    }

    /// <summary>
    /// A profile out of service is still a profile somebody may put back before this work is claimed, so naming
    /// one is not the mistake; running under one is, and the claim is where that is decided.
    /// </summary>
    [Fact]
    public async Task A_disabled_profile_may_still_be_named_because_the_claim_is_what_refuses_it()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        SeedProfile(db, "retired", disabled: true);
        var service = NewService(db);

        var item = await service.CreateAsync(Request(campaign.PublicId) with { ExecutionProfile = "retired" }, Ct);

        Assert.Equal("retired", item.ExecutionProfile);
    }

    /// <summary>One mistake is reported once: a name too long to be a profile is not also reported as unknown.</summary>
    [Fact]
    public async Task A_name_longer_than_the_field_allows_is_reported_only_as_too_long()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db);

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.CreateAsync(
                Request(campaign.PublicId) with { ExecutionProfile = new string('p', WorkItemService.MaxExecutionProfileLength + 1) },
                Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("execution_profile", detail.Field);
        Assert.Equal("too_long", detail.Code);
    }

    [Fact]
    public async Task Work_that_names_no_profile_is_created_as_it_always_was()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db);

        var item = await service.CreateAsync(Request(campaign.PublicId), Ct);

        Assert.Null(item.ExecutionProfile);
    }

    [Fact]
    public async Task A_provider_operation_names_a_profile_by_the_same_rule()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var service = NewService(db);

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.CreateAsync(ProviderOp(campaign.PublicId) with { ExecutionProfile = "nobody-installed-this" }, Ct));

        Assert.Contains(error.Details!, detail => detail.Field == "execution_profile" && detail.Code == "unknown");
    }

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

    private static ExecutionProfile SeedProfile(JasonDbContext db, string name, bool disabled = false)
    {
        var profile = new ExecutionProfile
        {
            PublicId = PublicId.New("prf"),
            Name = name,
            CurrentRevision = 1,
            DisabledAt = disabled ? Noon.UtcDateTime : null,
            CreatedAt = Noon.UtcDateTime,
            UpdatedAt = Noon.UtcDateTime,
        };
        db.ExecutionProfiles.Add(profile);
        db.SaveChanges();
        return profile;
    }
}
