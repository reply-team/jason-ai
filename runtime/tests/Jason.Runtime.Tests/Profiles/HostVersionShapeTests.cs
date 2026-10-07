using System.Net;
using Jason.Contracts.Api;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Profiles;

/// <summary>
/// A profile's verified host version is a version: it names which release of a host read the profile, and it travels
/// as metadata on the journal on that understanding. Three numbers, and a pre-release after `-` if any; no build
/// metadata, which takes no part in a version's order and could carry anything. Only a write that names the field is
/// held to it: a profile written before the rule keeps what it has.
/// </summary>
public class HostVersionShapeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("2.1.289 (Claude Code)")]
    [InlineData("Claude Code 2.1.289")]
    [InlineData("2.1")]
    [InlineData("2.1.289+build.7")]
    [InlineData("v2.1.289")]
    [InlineData("02.1.289")]
    [InlineData("stand-in")]
    public async Task A_host_version_that_is_not_a_version_is_refused_when_created_and_when_patched(string written)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var created = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "shaped", host = "claude_code", program = "claude", host_version_verified = written },
            HttpStatusCode.BadRequest,
            Ct);
        var detail = Assert.Single(created.Details!);
        Assert.Equal(("host_version_verified", "version_invalid"), (detail.Field, detail.Code));

        await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileCreate, new { name = "patched", host = "claude_code", program = "claude" }, Ct);
        var patched = await api.PostErrorAsync(
            Operations.ProfileUpdate, new { name = "patched", host_version_verified = written }, HttpStatusCode.BadRequest, Ct);
        detail = Assert.Single(patched.Details!);
        Assert.Equal(("host_version_verified", "version_invalid"), (detail.Field, detail.Code));
    }

    [Theory]
    [InlineData("2.1.289")]
    [InlineData("0.0.0-stand-in")]
    [InlineData("1.0.0-rc.1")]
    [InlineData(" 2.1.289 ")]
    public async Task A_version_is_accepted(string written)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var profile = await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate, new { name = "versioned", host = "claude_code", program = "claude", host_version_verified = written }, Ct);

        Assert.Equal(written.Trim(), profile.Revision.HostVersionVerified);
    }

    /// <summary>
    /// A profile written before the rule, with a value that is no version, is still read as it is, and an edit that does
    /// not name the field goes through: the rule holds what is written from now on, and rewrites nothing.
    /// </summary>
    [Fact]
    public async Task A_stored_value_from_before_the_rule_is_kept_and_an_edit_that_does_not_name_it_goes_through()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        // Written as the runtime before the rule wrote it: a revision is immutable once stored, so it is stored so.
        await using (var scope = api.Resolve<IServiceScopeFactory>().CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
            var now = DateTime.UtcNow;
            var profile = new ExecutionProfile { PublicId = "prf_01K0000000000000000000ABCD", Name = "older", CurrentRevision = 1, CreatedAt = now, UpdatedAt = now };
            profile.Revisions.Add(new ExecutionProfileRevision
            {
                Number = 1,
                Host = AgentHostKind.ClaudeCode,
                Program = "claude",
                HostVersionVerified = "stand-in",
                CreatedByType = ActorType.Human,
                CreatedAt = now,
            });
            db.ExecutionProfiles.Add(profile);
            await db.SaveChangesAsync(Ct);
        }

        var read = await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileGet, new { name = "older" }, Ct);
        Assert.Equal("stand-in", read.Revision.HostVersionVerified);

        var edited = await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileUpdate, new { name = "older", description = "kept as it was" }, Ct);
        Assert.Equal("stand-in", edited.Revision.HostVersionVerified);
    }
}
