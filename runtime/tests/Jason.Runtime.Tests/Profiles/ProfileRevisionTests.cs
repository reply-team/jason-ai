using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Json;

namespace Jason.Runtime.Tests.Profiles;

/// <summary>
/// The one thing this feature exists to guarantee: an edit appends a revision rather than changing one, and a
/// revision is whole — revision 7 is read as it was written, never reconstructed from the six rows before it.
/// An attempt names the revision it ran under for ever, so an edit that could reach backwards would let today
/// rewrite what last week says it did.
/// </summary>
public class ProfileRevisionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Updating_a_profile_appends_a_revision_and_leaves_the_old_one_alone()
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
                args = new[] { "--permission-mode", "dontAsk" },
                deny = new[] { "Write" },
                cli_command = "jason",
                host_version_verified = "2.1.275",
            },
            Ct);

        Assert.Equal(1, created.CurrentRevision);
        Assert.Equal(1, created.Revision.Number);

        var updated = await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileUpdate,
            new { name = "local-claude", deny = new[] { "Write", "WebFetch" } },
            Ct);

        // The patch named one field; the revision it appended is whole all the same.
        Assert.Equal(2, updated.CurrentRevision);
        Assert.Equal(2, updated.Revision.Number);
        Assert.Equal(["Write", "WebFetch"], updated.Revision.Deny);
        Assert.Equal(created.Revision.Host, updated.Revision.Host);
        Assert.Equal(created.Revision.Program, updated.Revision.Program);
        Assert.Equal(created.Revision.Args, updated.Revision.Args);
        Assert.Equal(created.Revision.CliCommand, updated.Revision.CliCommand);
        Assert.Equal(created.Revision.HostVersionVerified, updated.Revision.HostVersionVerified);

        // And the revision that ran yesterday still says what it said. Compared as the document it is, because
        // that is the claim: not "equal enough", but the same revision.
        var first = await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileGet,
            new { name = "local-claude", revision = 1 },
            Ct);

        Assert.Equal(Document(created.Revision), Document(first.Revision));
        Assert.Equal(2, first.CurrentRevision);
    }

    /// <summary>Reading a profile without naming a revision is reading the one in force.</summary>
    [Fact]
    public async Task A_read_that_names_no_revision_answers_with_the_one_in_force()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileCreate, Minimal("in-force"), Ct);
        await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileUpdate, new { name = "in-force", program = "claude-next" }, Ct);

        var read = await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileGet, new { name = "in-force" }, Ct);

        Assert.Equal(2, read.CurrentRevision);
        Assert.Equal(2, read.Revision.Number);
        Assert.Equal("claude-next", read.Revision.Program);
    }

    /// <summary>
    /// Five edits, five revisions, and every one of them still readable. The numbering is the profile's own
    /// sequence, which is what an attempt records.
    /// </summary>
    [Fact]
    public async Task Every_revision_a_profile_has_ever_had_can_still_be_read_by_number()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileCreate, Minimal("busy"), Ct);

        for (var edit = 2; edit <= 6; edit++)
        {
            var revised = await api.PostOkAsync<ExecutionProfileDto>(
                Operations.ProfileUpdate,
                new { name = "busy", host_version_verified = $"2.1.{edit}" },
                Ct);
            Assert.Equal(edit, revised.CurrentRevision);
        }

        for (var number = 2; number <= 6; number++)
        {
            var read = await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileGet, new { name = "busy", revision = number }, Ct);
            Assert.Equal(number, read.Revision.Number);
            Assert.Equal($"2.1.{number}", read.Revision.HostVersionVerified);

            // Whole, not a delta: the field nobody has touched since revision 1 is still on every one of them.
            Assert.Equal("claude", read.Revision.Program);
        }
    }

    /// <summary>A patch that names nothing is not an edit, and a profile that did not change does not gain a revision.</summary>
    [Fact]
    public async Task A_patch_that_names_nothing_leaves_the_profile_exactly_as_it_was()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var created = await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileCreate, Minimal("untouched"), Ct);

        var answered = await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileUpdate, new { name = "untouched" }, Ct);

        Assert.Equal(1, answered.CurrentRevision);
        Assert.Equal(Document(created), Document(answered));
    }

    /// <summary>A revision the profile has never had is a 404, not an empty answer somebody would read as a fact.</summary>
    [Fact]
    public async Task A_revision_that_was_never_written_is_not_found()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileCreate, Minimal("only-one"), Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileGet, new { name = "only-one", revision = 4 }, System.Net.HttpStatusCode.NotFound, Ct);

        Assert.Equal("profile_revision_not_found", error.Code);
    }

    internal static object Minimal(string name) => new { name, host = "claude_code", program = "claude" };

    private static string Document<T>(T value) => JsonSerializer.Serialize(value, JasonJson.Options);
}
