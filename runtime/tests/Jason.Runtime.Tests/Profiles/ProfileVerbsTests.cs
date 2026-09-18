using System.Net;
using System.Reflection;
using Jason.Contracts.Api;
using Jason.Runtime.Journal;
using Jason.Runtime.Profiles;

namespace Jason.Runtime.Tests.Profiles;

/// <summary>
/// The six verbs at the door: what they write, what they answer, and the seventh that does not exist. A profile
/// is never deleted, because an attempt names the revision it ran under for ever and a row somebody's history
/// points at is not the runtime's to remove.
/// </summary>
public class ProfileVerbsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task There_is_no_verb_that_deletes_a_profile()
    {
        var published = typeof(Operations)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Where(operation => operation.StartsWith("profile.", StringComparison.Ordinal))
            .OrderBy(operation => operation, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            ["profile.create", "profile.disable", "profile.enable", "profile.get", "profile.list", "profile.update"],
            published);

        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        foreach (var absent in (string[])["profile.delete", "profile.remove", "profile.rename"])
        {
            var (status, _) = await api.PostRawAsync(absent, "{}", Ct);
            Assert.Equal(HttpStatusCode.NotFound, status);
        }
    }

    /// <summary>What was written is what comes back, and the profile starts in service at revision 1.</summary>
    [Fact]
    public async Task A_created_profile_carries_its_first_revision()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var created = await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate,
            new
            {
                name = "local-claude",
                description = "The host installed on this machine.",
                host = "claude_code",
                program = "claude",
                args = new[] { "--model", "sonnet" },
                deny = new[] { "Write", "WebFetch" },
                cli_command = "jason",
                host_version_verified = "2.1.275",
                actor = new { type = "human", id = "person-1" },
                reason = "Registering the host that is installed.",
            },
            Ct);

        Assert.StartsWith("prf_", created.Id, StringComparison.Ordinal);
        Assert.Equal("local-claude", created.Name);
        Assert.Equal("The host installed on this machine.", created.Description);
        Assert.False(created.Disabled);
        Assert.Equal(1, created.CurrentRevision);
        Assert.Equal(AgentHostKind.ClaudeCode, created.Revision.Host);
        Assert.Equal("claude", created.Revision.Program);
        Assert.Equal(["--model", "sonnet"], created.Revision.Args);
        Assert.Equal(["Write", "WebFetch"], created.Revision.Deny);
        Assert.Equal("jason", created.Revision.CliCommand);
        Assert.Equal("2.1.275", created.Revision.HostVersionVerified);
    }

    /// <summary>
    /// A profile that named no command word still answers with one. The runtime puts its own executable within
    /// the child's reach, so null on the row would leave a reader to work out what the agent will type.
    /// </summary>
    [Fact]
    public async Task A_profile_that_names_no_cli_command_answers_with_the_runtimes_own()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var created = await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileCreate, ProfileRevisionTests.Minimal("bare"), Ct);

        Assert.False(string.IsNullOrWhiteSpace(created.Revision.CliCommand));
        Assert.Equal(ProfileService.DefaultCliCommand, created.Revision.CliCommand);
        Assert.Empty(created.Revision.Args);
        Assert.Empty(created.Revision.Deny);
        Assert.Null(created.Revision.HostVersionVerified);
    }

    [Fact]
    public async Task A_name_that_is_already_taken_is_a_conflict()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileCreate, ProfileRevisionTests.Minimal("taken"), Ct);

        var error = await api.PostErrorAsync(Operations.ProfileCreate, ProfileRevisionTests.Minimal("taken"), HttpStatusCode.Conflict, Ct);

        Assert.Equal("profile_exists", error.Code);
    }

    [Theory]
    [InlineData(Operations.ProfileGet)]
    [InlineData(Operations.ProfileUpdate)]
    [InlineData(Operations.ProfileDisable)]
    [InlineData(Operations.ProfileEnable)]
    public async Task A_verb_that_names_no_such_profile_is_a_404(string operation)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(operation, new { name = "never-registered" }, HttpStatusCode.NotFound, Ct);

        Assert.Equal("profile_not_found", error.Code);
    }

    /// <summary>
    /// A listing answers what can run work now. A disabled profile still exists and is still named by attempts,
    /// so it is there to be asked for rather than gone.
    /// </summary>
    [Fact]
    public async Task A_listing_leaves_out_disabled_profiles_unless_they_are_asked_for()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileCreate, ProfileRevisionTests.Minimal("in-service"), Ct);
        await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileCreate, ProfileRevisionTests.Minimal("retired"), Ct);
        await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileDisable, new { name = "retired", reason = "The host was uninstalled." }, Ct);

        var listed = await api.PostOkAsync<Page<ExecutionProfileDto>>(Operations.ProfileList, null, Ct);
        Assert.Equal(["in-service"], listed.Items.Select(p => p.Name));

        var all = await api.PostOkAsync<Page<ExecutionProfileDto>>(Operations.ProfileList, new { include_disabled = true }, Ct);
        Assert.Equal(["in-service", "retired"], all.Items.Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.True(all.Items.Single(p => p.Name == "retired").Disabled);

        // Every item carries the revision in force, which is what the listing is read for.
        Assert.All(all.Items, profile => Assert.Equal(profile.CurrentRevision, profile.Revision.Number));
    }

    [Fact]
    public async Task A_listing_pages_by_cursor_like_every_other_listing()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        foreach (var name in (string[])["first", "second", "third"])
        {
            await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileCreate, ProfileRevisionTests.Minimal(name), Ct);
        }

        var page = await api.PostOkAsync<Page<ExecutionProfileDto>>(Operations.ProfileList, new { limit = 2 }, Ct);
        Assert.Equal(2, page.Items.Count);
        Assert.NotNull(page.NextCursor);

        var rest = await api.PostOkAsync<Page<ExecutionProfileDto>>(Operations.ProfileList, new { limit = 2, cursor = page.NextCursor }, Ct);
        Assert.Single(rest.Items);
        Assert.Null(rest.NextCursor);
        Assert.Equal(["first", "second", "third"], page.Items.Concat(rest.Items).Select(p => p.Name));
    }

    /// <summary>Out of service and back again, and the revisions untouched by either.</summary>
    [Fact]
    public async Task Disabling_and_enabling_move_the_profile_and_nothing_else()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var created = await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileCreate, ProfileRevisionTests.Minimal("toggled"), Ct);

        var disabled = await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileDisable, new { name = "toggled" }, Ct);
        Assert.True(disabled.Disabled);
        Assert.Equal(created.CurrentRevision, disabled.CurrentRevision);

        var enabled = await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileEnable, new { name = "toggled" }, Ct);
        Assert.False(enabled.Disabled);
        Assert.Equal(created.CurrentRevision, enabled.CurrentRevision);
        Assert.Equal(created.Revision.Number, enabled.Revision.Number);
    }

    /// <summary>
    /// The chronicle records what happened. Setting a profile to the state it is already in is not something
    /// that happened, so the second call writes no line — and answers the same thing as the first.
    /// </summary>
    [Fact]
    public async Task Disabling_a_disabled_profile_changes_nothing_and_says_nothing()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileCreate, ProfileRevisionTests.Minimal("already"), Ct);
        await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileDisable, new { name = "already" }, Ct);

        var again = await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileDisable, new { name = "already" }, Ct);

        Assert.True(again.Disabled);
        Assert.Single(await KindsAsync(api, JournalKinds.ProfileDisabled));
    }

    /// <summary>
    /// What the chronicle says about a profile: that it exists, that it was revised — naming both numbers — and
    /// that it went out of service. Enough to read a history without reading the profile.
    /// </summary>
    [Fact]
    public async Task The_chronicle_names_the_profile_and_both_revision_numbers()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate,
            new { name = "chronicled", host = "claude_code", program = "claude", host_version_verified = "2.1.275" },
            Ct);
        await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileUpdate,
            new { name = "chronicled", deny = new[] { "Write" }, reason = "Writing was never wanted." },
            Ct);
        await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileDisable, new { name = "chronicled" }, Ct);

        var created = Assert.Single(await KindsAsync(api, JournalKinds.ProfileCreated));
        Assert.Equal("chronicled", created.Key);
        Assert.Equal("claude_code", (string?)created.New?["host"]);
        Assert.Equal(1, (int?)created.New?["revision"]);
        Assert.Equal("2.1.275", (string?)created.New?["host_version_verified"]);

        var revised = Assert.Single(await KindsAsync(api, JournalKinds.ProfileRevised));
        Assert.Equal(1, (int?)revised.Old?["revision"]);
        Assert.Equal(2, (int?)revised.New?["revision"]);
        Assert.Equal(["deny"], revised.New?["fields"]?.AsArray().Select(field => (string?)field));
        Assert.Equal("Writing was never wanted.", revised.Reason);

        var disabled = Assert.Single(await KindsAsync(api, JournalKinds.ProfileDisabled));
        Assert.Equal("chronicled", disabled.Key);

        // A registry entry belongs to no campaign, so none of these lines claims one.
        Assert.All([created, revised, disabled], entry => Assert.Null(entry.CampaignId));
    }

    /// <summary>An actor is recorded as claimed, the way every other mutating verb records one.</summary>
    [Fact]
    public async Task The_actor_that_registered_a_profile_is_recorded()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate,
            new { name = "attributed", host = "claude_code", program = "claude", actor = new { type = "human", id = "person-1" } },
            Ct);

        var created = Assert.Single(await KindsAsync(api, JournalKinds.ProfileCreated));
        Assert.Equal(ActorType.Human, created.Actor.Type);
        Assert.Equal("person-1", created.Actor.Id);
    }

    /// <summary>The runtime's own actor is refused to callers here as everywhere else.</summary>
    [Fact]
    public async Task A_caller_cannot_claim_to_be_the_runtime()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "impostor", host = "claude_code", program = "claude", actor = new { type = "system", id = "runtime" } },
            HttpStatusCode.BadRequest,
            Ct);

        Assert.Equal("validation_failed", error.Code);
    }

    private static async Task<IReadOnlyList<JournalEntryDto>> KindsAsync(RuntimeApiFixture api, string kind)
    {
        var page = await api.PostOkAsync<Page<JournalEntryDto>>(Operations.JournalList, new { kind }, Ct);
        return page.Items;
    }
}
