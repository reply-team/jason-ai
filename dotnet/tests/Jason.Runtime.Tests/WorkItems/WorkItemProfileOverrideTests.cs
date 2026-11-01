using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Json;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

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

    /// <summary>
    /// The repair an item that is already blocked needs: naming a profile on the item itself, rather than
    /// creating different work or moving every other item its campaign or its role owns.
    /// </summary>
    [Fact]
    public async Task A_patch_stores_the_profile_and_records_the_change()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var item = Seed(db, WorkItemFactory.NewAiRole(WorkItemFactory.NewCampaign()));
        SeedProfile(db, "fast-claude");
        var service = NewService(db);

        var patched = await service.UpdateAsync(
            Patch(item.PublicId) with { ExecutionProfile = Optional<string?>.Of("  fast-claude  "), Reason = "unblocking it" },
            Ct);

        Assert.Equal("fast-claude", patched.ExecutionProfile);
        var entry = Assert.Single(await db.Journal.ToListAsync(Ct));
        Assert.Equal(JournalKinds.WorkItemUpdated, entry.Kind);
        Assert.Equal("execution_profile", entry.Key);
        Assert.Null((string?)entry.Old);
        Assert.Equal("fast-claude", (string?)entry.New);
        Assert.Equal("unblocking it", entry.Reason);
    }

    [Fact]
    public async Task A_patch_that_names_no_profile_is_refused_by_the_same_rule_as_a_create()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var item = Seed(db, WorkItemFactory.NewAiRole(WorkItemFactory.NewCampaign()));
        var service = NewService(db);

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.UpdateAsync(Patch(item.PublicId) with { ExecutionProfile = Optional<string?>.Of("nobody-installed-this") }, Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("execution_profile", detail.Field);
        Assert.Equal("unknown", detail.Code);
    }

    [Fact]
    public async Task An_explicit_null_gives_the_item_back_to_the_levels_below_it()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var item = Seed(db, WorkItemFactory.NewAiRole(WorkItemFactory.NewCampaign(), configure: w => w.ExecutionProfile = "fast-claude"));
        var service = NewService(db);

        var patched = await service.UpdateAsync(Patch(item.PublicId) with { ExecutionProfile = Optional<string?>.Of(null) }, Ct);

        Assert.Null(patched.ExecutionProfile);
        var entry = Assert.Single(await db.Journal.ToListAsync(Ct));
        Assert.Equal("execution_profile", entry.Key);
        Assert.Equal("fast-claude", (string?)entry.Old);
        Assert.Null((string?)entry.New);
    }

    [Fact]
    public async Task A_patch_that_leaves_the_field_alone_leaves_the_profile_alone()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var item = Seed(db, WorkItemFactory.NewAiRole(WorkItemFactory.NewCampaign(), configure: w => w.ExecutionProfile = "fast-claude"));
        var service = NewService(db);

        var patched = await service.UpdateAsync(Patch(item.PublicId) with { Priority = Optional<int>.Of(3) }, Ct);

        Assert.Equal("fast-claude", patched.ExecutionProfile);
        Assert.DoesNotContain(await db.Journal.ToListAsync(Ct), entry => entry.Key == "execution_profile");
    }

    /// <summary>Work that has already ended is not repairable, and nothing about this field changes that.</summary>
    [Fact]
    public async Task Work_that_has_already_finished_takes_no_profile_either()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var item = Seed(db, WorkItemFactory.NewAiRole(WorkItemFactory.NewCampaign(), configure: w => w.Status = WorkItemStatus.Succeeded));
        SeedProfile(db, "fast-claude");
        var service = NewService(db);

        var error = await Assert.ThrowsAsync<ConflictException>(
            () => service.UpdateAsync(Patch(item.PublicId) with { ExecutionProfile = Optional<string?>.Of("fast-claude") }, Ct));

        Assert.Equal("workitem_terminal", error.Code);
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

    private static WorkItemUpdateRequest Patch(string workItemId) =>
        new(workItemId, null, null, default, default, default, default, default, default, default, default, null, null);

    private static Campaign Seed(JasonDbContext db, Campaign campaign)
    {
        db.Campaigns.Add(campaign);
        db.SaveChanges();
        return campaign;
    }

    private static WorkItem Seed(JasonDbContext db, WorkItem item)
    {
        db.WorkItems.Add(item);
        db.SaveChanges();
        return item;
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
