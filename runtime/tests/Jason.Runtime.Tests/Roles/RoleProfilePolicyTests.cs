using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Roles;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Roles;

/// <summary>
/// A role's execution profile says which host that kind of worker uses where the item and its campaign say
/// nothing. Roles have no general update verb and this wave is not about roles, so the policy is moved by one
/// narrow verb of its own — which still has to hold the same rule the campaign's policy does: the name must be
/// one something answers.
/// </summary>
public class RoleProfilePolicyTests
{
    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_role_can_be_registered_with_a_profile_and_a_name_nothing_answers_is_refused()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var service = NewService(db);
        db.ExecutionProfiles.Add(Profile("prf_A", "local-claude"));
        await db.SaveChangesAsync(Ct);

        var added = await service.AddAsync(new RoleAddRequest("fake", null, null, null, "local-claude", null, null), Ct);
        Assert.Equal("local-claude", added.ExecutionProfile);

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.AddAsync(new RoleAddRequest("other", null, null, null, "nothing-answers-this", null, null), Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("execution_profile", detail.Field);
        Assert.Equal("unknown", detail.Code);

        // The refusal is the whole request: no role was registered under the name that carried a bad policy.
        Assert.False(await db.Roles.AnyAsync(role => role.Name == "other", Ct));
    }

    [Fact]
    public async Task Set_profile_moves_a_builtin_roles_policy_and_writes_down_what_changed()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var service = NewService(db);
        db.ExecutionProfiles.Add(Profile("prf_A", "local-claude"));
        db.ExecutionProfiles.Add(Profile("prf_B", "local-codex"));
        await db.SaveChangesAsync(Ct);

        await service.SetProfileAsync(new RoleSetProfileRequest("researcher", Optional<string?>.Of("local-claude"), null, null), Ct);
        var moved = await service.SetProfileAsync(
            new RoleSetProfileRequest("researcher", Optional<string?>.Of("local-codex"), null, "the other host reads faster"),
            Ct);

        Assert.Equal("local-codex", moved.ExecutionProfile);
        Assert.True(moved.Builtin);

        var entries = await db.Journal.AsNoTracking().Where(entry => entry.Kind == JournalKinds.RoleUpdated).OrderBy(entry => entry.Id).ToListAsync(Ct);
        Assert.Equal(2, entries.Count);
        Assert.Equal("researcher", entries[1].Key);
        Assert.Equal("local-claude", (string?)entries[1].Old);
        Assert.Equal("local-codex", (string?)entries[1].New);
        Assert.Equal("the other host reads faster", entries[1].Reason);
    }

    [Fact]
    public async Task Set_profile_clears_a_policy_with_an_explicit_null_and_writes_nothing_when_nothing_moved()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var service = NewService(db);
        db.ExecutionProfiles.Add(Profile("prf_A", "local-claude"));
        await db.SaveChangesAsync(Ct);
        await service.SetProfileAsync(new RoleSetProfileRequest("planner", Optional<string?>.Of("local-claude"), null, null), Ct);

        var cleared = await service.SetProfileAsync(new RoleSetProfileRequest("planner", Optional<string?>.Of(null), null, null), Ct);
        Assert.Null(cleared.ExecutionProfile);

        await service.SetProfileAsync(new RoleSetProfileRequest("planner", Optional<string?>.Of(null), null, null), Ct);

        Assert.Equal(2, await db.Journal.CountAsync(entry => entry.Kind == JournalKinds.RoleUpdated, Ct));
    }

    [Fact]
    public async Task Set_profile_refuses_a_profile_nothing_answers_and_a_role_nobody_registered()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var service = NewService(db);

        var refused = await Assert.ThrowsAsync<ValidationException>(
            () => service.SetProfileAsync(new RoleSetProfileRequest("planner", Optional<string?>.Of("nothing-answers-this"), null, null), Ct));
        var detail = Assert.Single(refused.Details!);
        Assert.Equal("execution_profile", detail.Field);
        Assert.Equal("unknown", detail.Code);

        var missing = await Assert.ThrowsAsync<NotFoundException>(
            () => service.SetProfileAsync(new RoleSetProfileRequest("nobody-registered-this", Optional<string?>.Of(null), null, null), Ct));
        Assert.Equal("role_not_found", missing.Code);
    }

    private static RoleService NewService(JasonDbContext db)
    {
        var clock = new FixedClock(Noon);
        return new RoleService(db, new JournalWriter(clock), clock);
    }

    private static ExecutionProfile Profile(string publicId, string name)
    {
        var profile = new ExecutionProfile
        {
            PublicId = publicId,
            Name = name,
            CurrentRevision = 1,
            CreatedAt = Noon,
            UpdatedAt = Noon,
        };

        profile.Revisions.Add(new ExecutionProfileRevision
        {
            Number = 1,
            Host = AgentHostKind.ClaudeCode,
            Program = "claude",
            CreatedByType = ActorType.Human,
            CreatedAt = Noon,
        });

        return profile;
    }
}
