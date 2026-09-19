using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Roles;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Campaigns;

/// <summary>
/// A campaign's execution profile is a policy rather than a launch: it says which host this campaign's agent
/// work uses where the work item itself names none. The policy is a name, and the name has to be one something
/// answers — a campaign pointed at a profile that does not exist would refuse every claim afterwards, far from
/// the person who typed it and with nothing but a work item to say so.
/// </summary>
public class CampaignProfilePolicyTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Setting_a_campaign_policy_to_an_unknown_profile_is_refused_and_clearing_it_is_not()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var service = NewService(db, clock);
        db.ExecutionProfiles.Add(Profile("prf_A", "local-claude"));
        await db.SaveChangesAsync(Ct);
        var campaign = await service.CreateAsync(new CampaignCreateRequest("LatAm", null, null, null), Ct);

        var pointed = await service.UpdateAsync(Patch(campaign.Id, "local-claude"), Ct);
        Assert.Equal("local-claude", pointed.ExecutionProfile);

        var error = await Assert.ThrowsAsync<ValidationException>(() => service.UpdateAsync(Patch(campaign.Id, "nothing-answers-this"), Ct));
        var detail = Assert.Single(error.Details!);
        Assert.Equal("execution_profile", detail.Field);
        Assert.Equal("unknown", detail.Code);

        // The refusal changed nothing: the campaign still uses the profile it was pointed at.
        db.ChangeTracker.Clear();
        Assert.Equal("local-claude", (await service.GetAsync(new CampaignGetRequest(campaign.Id), Ct)).ExecutionProfile);

        // Null is a value, not an absence: it takes the policy away, and nothing has to exist for that.
        var cleared = await service.UpdateAsync(Patch(campaign.Id, null), Ct);
        Assert.Null(cleared.ExecutionProfile);
    }

    /// <summary>A patch that does not name the field leaves it alone, which is what tells "clear it" from "say nothing".</summary>
    [Fact]
    public async Task A_patch_that_says_nothing_about_the_profile_leaves_the_policy_where_it_was()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var service = NewService(db, clock);
        db.ExecutionProfiles.Add(Profile("prf_A", "local-claude"));
        await db.SaveChangesAsync(Ct);
        var campaign = await service.CreateAsync(new CampaignCreateRequest("LatAm", null, null, null), Ct);
        await service.UpdateAsync(Patch(campaign.Id, "local-claude"), Ct);

        var renamed = await service.UpdateAsync(
            new CampaignUpdateRequest(campaign.Id, Optional<string?>.Of("LatAm founders"), Optional<string?>.Absent, Optional<int?>.Absent, null, null),
            Ct);

        Assert.Equal("LatAm founders", renamed.Name);
        Assert.Equal("local-claude", renamed.ExecutionProfile);
    }

    /// <summary>
    /// The change is somebody's act and is written down as one, under the key that says which field moved —
    /// the campaign chronicle is where a person later asks why work started running somewhere else.
    /// </summary>
    [Fact]
    public async Task Pointing_a_campaign_at_a_profile_is_journalled_and_pointing_it_at_the_same_one_again_is_not()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var service = NewService(db, clock);
        db.ExecutionProfiles.Add(Profile("prf_A", "local-claude"));
        await db.SaveChangesAsync(Ct);
        var campaign = await service.CreateAsync(new CampaignCreateRequest("LatAm", null, null, null), Ct);

        await service.UpdateAsync(Patch(campaign.Id, "local-claude", "the laptop runs this one"), Ct);
        await service.UpdateAsync(Patch(campaign.Id, "local-claude"), Ct);

        var entries = await db.Journal.AsNoTracking()
            .Where(entry => entry.Kind == JournalKinds.CampaignUpdated && entry.Key == "execution_profile")
            .ToListAsync(Ct);

        var entry = Assert.Single(entries);
        Assert.Null(entry.Old);
        Assert.Equal("local-claude", (string?)entry.New);
        Assert.Equal("the laptop runs this one", entry.Reason);
    }

    /// <summary>
    /// Two levels of the resolution order, written in two places, read back apart: a campaign's policy is about
    /// this campaign, a role's is about that kind of worker everywhere, and neither overwrites the other.
    /// </summary>
    [Fact]
    public async Task A_campaign_policy_and_a_role_policy_are_both_kept_and_both_read_back()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaigns = NewService(db, clock);
        var roles = new RoleService(db, new JournalWriter(clock), clock);
        db.ExecutionProfiles.Add(Profile("prf_A", "local-claude"));
        db.ExecutionProfiles.Add(Profile("prf_B", "local-codex"));
        await db.SaveChangesAsync(Ct);
        var campaign = await campaigns.CreateAsync(new CampaignCreateRequest("LatAm", null, null, null), Ct);

        await campaigns.UpdateAsync(Patch(campaign.Id, "local-claude"), Ct);
        await roles.SetProfileAsync(new RoleSetProfileRequest("researcher", Optional<string?>.Of("local-codex"), null, null), Ct);

        Assert.Equal("local-claude", (await campaigns.GetAsync(new CampaignGetRequest(campaign.Id), Ct)).ExecutionProfile);
        var researcher = Assert.Single((await roles.ListAsync(new RoleListRequest(null, null), Ct)).Items, role => role.Name == "researcher");
        Assert.Equal("local-codex", researcher.ExecutionProfile);
    }

    [Fact]
    public async Task An_archived_campaign_refuses_a_policy_the_way_it_refuses_every_other_write()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var service = NewService(db, clock);
        db.ExecutionProfiles.Add(Profile("prf_A", "local-claude"));
        await db.SaveChangesAsync(Ct);
        var campaign = await service.CreateAsync(new CampaignCreateRequest("LatAm", null, null, null), Ct);
        await service.ArchiveAsync(new CampaignTransitionRequest(campaign.Id, null, null), Ct);

        var error = await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(Patch(campaign.Id, "local-claude"), Ct));

        Assert.Equal("campaign_archived", error.Code);
    }

    private static CampaignUpdateRequest Patch(string campaignId, string? profile, string? reason = null) =>
        new(campaignId, Optional<string?>.Absent, Optional<string?>.Of(profile), Optional<int?>.Absent, null, reason);

    private static CampaignService NewService(JasonDbContext db, TimeProvider clock) =>
        new(db, new JournalWriter(clock), clock, TestCanceller.New(clock));

    private static ExecutionProfile Profile(string publicId, string name)
    {
        var now = Noon.UtcDateTime;
        var profile = new ExecutionProfile
        {
            PublicId = publicId,
            Name = name,
            CurrentRevision = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };

        profile.Revisions.Add(new ExecutionProfileRevision
        {
            Number = 1,
            Host = AgentHostKind.ClaudeCode,
            Program = "claude",
            CreatedByType = ActorType.Human,
            CreatedAt = now,
        });

        return profile;
    }
}
