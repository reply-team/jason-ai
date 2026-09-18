using System.Globalization;
using System.Net;
using Jason.Contracts.Api;
using Jason.Runtime.Profiles;

namespace Jason.Runtime.Tests.Profiles;

/// <summary>
/// What a profile is asked to survive. A profile is read when work is already claimed and a child is about to
/// start, so anything wrong with it has to be refused here, at the door, by the person who typed it — and
/// refused as a field-level answer they can act on, never as a 500 that says only that something broke.
/// <para>
/// Every input is built rather than pasted: a blob in a test file is a thing nobody reads, and the one that
/// matters here is the one that is ten thousand entries long.
/// </para>
/// </summary>
public class ProfileHostileInputTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A name is an identifier work spells from memory; a path is not one.</summary>
    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("..\\..\\windows")]
    [InlineData("local/claude")]
    [InlineData("local\\claude")]
    [InlineData("C:claude")]
    public async Task A_name_that_is_a_path_is_refused(string name)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileCreate, new { name, host = "claude_code", program = "claude" }, HttpStatusCode.BadRequest, Ct);

        Assert.Equal("validation_failed", error.Code);
        Assert.Equal("name", Assert.Single(error.Details!).Field);
    }

    [Fact]
    public async Task A_name_carrying_control_characters_is_refused()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        foreach (var control in (char[])['\u0000', '\u0007', '\n', '\r', '\t', '\u001b'])
        {
            var name = "local" + control + "claude";
            var error = await api.PostErrorAsync(
                Operations.ProfileCreate, new { name, host = "claude_code", program = "claude" }, HttpStatusCode.BadRequest, Ct);

            Assert.Equal("validation_failed", error.Code);
            Assert.Equal("name", Assert.Single(error.Details!).Field);
        }
    }

    [Fact]
    public async Task A_name_longer_than_the_cap_is_refused()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = new string('a', ProfileService.MaxNameLength + 1), host = "claude_code", program = "claude" },
            HttpStatusCode.BadRequest,
            Ct);

        Assert.Equal("validation_failed", error.Code);
    }

    /// <summary>
    /// The list that would cost something to store, and more to hand to a process. It is refused by its count
    /// alone, so the answer is one problem rather than ten thousand.
    /// </summary>
    [Fact]
    public async Task An_args_list_of_ten_thousand_entries_is_refused_as_one_problem()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var args = Enumerable.Range(0, 10_000).Select(index => "--flag-" + index.ToString(CultureInfo.InvariantCulture)).ToArray();

        var error = await api.PostErrorAsync(
            Operations.ProfileCreate, new { name = "flooded", host = "claude_code", program = "claude", args }, HttpStatusCode.BadRequest, Ct);

        Assert.Equal("validation_failed", error.Code);
        var detail = Assert.Single(error.Details!);
        Assert.Equal("args", detail.Field);
        Assert.Equal("too_long", detail.Code);
    }

    /// <summary>An entry of a list of strings that is not a string. Named as the field it is, not as a type.</summary>
    [Fact]
    public async Task A_deny_entry_that_is_not_a_string_is_refused_by_field()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "objected", host = "claude_code", program = "claude", deny = new object[] { "Write", new { tool = "WebFetch" } } },
            HttpStatusCode.BadRequest,
            Ct);

        Assert.Equal("validation_failed", error.Code);
        Assert.Equal("deny[1]", Assert.Single(error.Details!).Field);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task An_args_entry_that_is_blank_or_missing_is_refused(string? entry)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "blanked", host = "claude_code", program = "claude", args = new[] { "--ok", entry } },
            HttpStatusCode.BadRequest,
            Ct);

        Assert.Equal("validation_failed", error.Code);
        Assert.Equal("args[1]", Assert.Single(error.Details!).Field);
    }

    /// <summary>
    /// The command word the launched agent types. The runtime is what puts something of that name within its
    /// reach, so a path would name a program the runtime never put there.
    /// </summary>
    [Theory]
    [InlineData("/usr/local/bin/jason")]
    [InlineData("C:\\tools\\jason.exe")]
    [InlineData("..\\jason")]
    [InlineData("./jason")]
    [InlineData(".")]
    [InlineData("..")]
    public async Task A_cli_command_that_is_a_path_is_refused(string cliCommand)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "pathed", host = "claude_code", program = "claude", cli_command = cliCommand },
            HttpStatusCode.BadRequest,
            Ct);

        Assert.Equal("validation_failed", error.Code);
        Assert.Equal("cli_command", Assert.Single(error.Details!).Field);
    }

    /// <summary>
    /// A host this build has no launcher for is refused when it is written, which is the whole reason the
    /// vocabulary is closed: the alternative is discovering it when work is claimed and a child is due to start.
    /// </summary>
    [Theory]
    [InlineData("codex")]
    [InlineData("ClaudeCode")]
    [InlineData("claude-code")]
    [InlineData("")]
    public async Task A_host_outside_the_vocabulary_is_refused_with_the_vocabulary(string host)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileCreate, new { name = "unknown-host", host, program = "claude" }, HttpStatusCode.BadRequest, Ct);

        Assert.Equal("validation_failed", error.Code);
        Assert.Equal("host", Assert.Single(error.Details!).Field);
    }

    [Fact]
    public async Task A_host_the_vocabulary_does_know_is_the_one_that_is_accepted()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var created = await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate, new { name = "known-host", host = "claude_code", program = "claude" }, Ct);

        Assert.Equal(AgentHostKind.ClaudeCode, created.Revision.Host);
    }

    /// <summary>An edit is held to the same rules as a creation; a patch is not a way around them.</summary>
    [Fact]
    public async Task A_patch_is_held_to_the_same_rules_as_a_creation()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileCreate, ProfileRevisionTests.Minimal("patched"), Ct);

        var host = await api.PostErrorAsync(
            Operations.ProfileUpdate, new { name = "patched", host = "codex" }, HttpStatusCode.BadRequest, Ct);
        Assert.Equal("host", Assert.Single(host.Details!).Field);

        var cleared = await api.PostErrorAsync(
            Operations.ProfileUpdate, new { name = "patched", program = (string?)null }, HttpStatusCode.BadRequest, Ct);
        Assert.Equal("program", Assert.Single(cleared.Details!).Field);

        var separated = await api.PostErrorAsync(
            Operations.ProfileUpdate, new { name = "patched", cli_command = "bin/jason" }, HttpStatusCode.BadRequest, Ct);
        Assert.Equal("cli_command", Assert.Single(separated.Details!).Field);

        // Nothing of any of that was written: the profile is still the one revision it was created with.
        var read = await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileGet, new { name = "patched" }, Ct);
        Assert.Equal(1, read.CurrentRevision);
    }

    /// <summary>A revision number below the first one is a caller's mistake, not an empty answer.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task A_revision_number_below_one_is_refused(int revision)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileCreate, ProfileRevisionTests.Minimal("numbered"), Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileGet, new { name = "numbered", revision }, HttpStatusCode.BadRequest, Ct);

        Assert.Equal("validation_failed", error.Code);
        Assert.Equal("revision", Assert.Single(error.Details!).Field);
    }

    /// <summary>
    /// A body that is not an object is the caller's mistake and is answered as one. None of these may be a 500:
    /// a request that never reached a service did not break the runtime.
    /// </summary>
    [Theory]
    [InlineData(Operations.ProfileCreate)]
    [InlineData(Operations.ProfileUpdate)]
    [InlineData(Operations.ProfileGet)]
    [InlineData(Operations.ProfileList)]
    [InlineData(Operations.ProfileDisable)]
    [InlineData(Operations.ProfileEnable)]
    public async Task A_body_that_is_not_an_object_is_refused_by_every_verb(string operation)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        foreach (var body in (string[])["[]", "[{\"name\":\"listed\"}]", "null", "\"a profile\"", "7", "true"])
        {
            var (status, text) = await api.PostRawAsync(operation, body, Ct);

            Assert.Equal(HttpStatusCode.BadRequest, status);
            Assert.Contains("invalid_request", text, StringComparison.Ordinal);
        }
    }

    /// <summary>Every cap, at the value above it, in one pass: what is written is bounded or it is refused.</summary>
    [Fact]
    public async Task Every_bounded_field_is_refused_one_character_past_its_cap()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        await RefusedAsync(api, new { name = "long-description", host = "claude_code", program = "claude", description = Long(ProfileService.MaxDescriptionLength) }, "description");
        await RefusedAsync(api, new { name = "long-program", host = "claude_code", program = Long(ProfileService.MaxProgramLength) }, "program");
        await RefusedAsync(api, new { name = "long-arg", host = "claude_code", program = "claude", args = new[] { Long(ProfileService.MaxArgLength) } }, "args[0]");
        await RefusedAsync(api, new { name = "long-deny", host = "claude_code", program = "claude", deny = new[] { Long(ProfileService.MaxDenyEntryLength) } }, "deny[0]");
        await RefusedAsync(api, new { name = "long-command", host = "claude_code", program = "claude", cli_command = Long(ProfileService.MaxCliCommandLength) }, "cli_command");
        await RefusedAsync(api, new { name = "long-version", host = "claude_code", program = "claude", host_version_verified = Long(ProfileService.MaxHostVersionLength) }, "host_version_verified");
        await RefusedAsync(api, new { name = "long-reason", host = "claude_code", program = "claude", reason = Long(ProfileService.MaxReasonLength) }, "reason");

        static string Long(int cap) => new('x', cap + 1);
    }

    private static async Task RefusedAsync(RuntimeApiFixture api, object body, string field)
    {
        var error = await api.PostErrorAsync(Operations.ProfileCreate, body, HttpStatusCode.BadRequest, Ct);

        Assert.Equal("validation_failed", error.Code);
        Assert.Equal(field, Assert.Single(error.Details!).Field);
    }
}
