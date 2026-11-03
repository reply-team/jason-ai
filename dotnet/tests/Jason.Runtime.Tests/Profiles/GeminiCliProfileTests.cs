using System.Net;
using Jason.Contracts.Api;

namespace Jason.Runtime.Tests.Profiles;

/// <summary>
/// A profile naming <c>gemini_cli</c>, held where it is written to what that host reads: both tools of the vocabulary
/// are the host's own and may be granted; a deny is a tool of the host by its name or a shell command by its prefix, and
/// another word is refused naming the host; arguments are flags with their values joined, since the host reads a bare word
/// as the prompt; and a profile moved onto the host is held to the flags the host composes for itself.
/// </summary>
public class GeminiCliProfileTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_profile_naming_the_host_by_its_word_grants_both_tools_and_denies_by_the_hosts_own_words()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var created = await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate,
            new
            {
                name = "gemini",
                host = "gemini_cli",
                program = "gemini",
                tools = new[] { "web_fetch", "web_search" },
                deny = new[] { "run_shell_command(jason campaign delete)", "read_many_files" },
                args = new[] { "--model=made-up-model" },
            },
            Ct);

        Assert.Equal(AgentHostKind.GeminiCli, created.Revision.Host);
        Assert.Equal(["run_shell_command(jason campaign delete)", "read_many_files"], created.Revision.Deny);
    }

    [Fact]
    public async Task A_deny_in_another_hosts_words_is_refused_naming_gemini_cli_and_the_word()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "gemini-deny", host = "gemini_cli", program = "gemini", deny = new[] { "web_fetch", "Bash(rm *)" } },
            HttpStatusCode.BadRequest,
            Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal(("deny[1]", "invalid"), (detail.Field, detail.Code));
        Assert.Contains("gemini_cli", detail.Message, StringComparison.Ordinal);
        Assert.Contains("Bash(rm *)", detail.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_bare_word_in_the_arguments_is_refused_naming_gemini_cli()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "gemini-args", host = "gemini_cli", program = "gemini", args = new[] { "--model", "made-up" } },
            HttpStatusCode.BadRequest,
            Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal(("args", "invalid"), (detail.Field, detail.Code));
        Assert.Contains("gemini_cli", detail.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_flag_the_host_composes_is_reserved_on_create()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "gemini-yolo", host = "gemini_cli", program = "gemini", args = new[] { "--approval-mode=yolo" } },
            HttpStatusCode.BadRequest,
            Ct);

        Assert.Contains(error.Details!, detail => detail is { Code: "reserved" } && detail.Message.Contains("--approval-mode", StringComparison.Ordinal));
    }

    /// <summary>
    /// A profile stored on Claude Code with an argument that is a flag only Gemini CLI composes is moved onto Gemini CLI by a patch naming nothing but the host. The stored
    /// argument would be launched after the composed approval mode and win, so the move is refused naming the host. The patch
    /// names the program too, since a move that leaves the stored program of another host is refused for that alone.
    /// </summary>
    [Fact]
    public async Task A_patch_moving_a_profile_to_the_host_is_held_to_the_flags_the_host_composes()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate,
            new { name = "moving", host = "claude_code", program = "claude", args = new[] { "--approval-mode=yolo" } },
            Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileUpdate,
            new { name = "moving", host = "gemini_cli", program = "gemini" },
            HttpStatusCode.BadRequest,
            Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal(("args[0]", "reserved"), (detail.Field, detail.Code));
        Assert.Contains("gemini_cli", detail.Message, StringComparison.Ordinal);
        Assert.Contains("--approval-mode", detail.Message, StringComparison.Ordinal);
        var read = await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileGet, new { name = "moving" }, Ct);
        Assert.Equal(1, read.Revision.Number);
    }

    /// <summary>The same move with arguments the host reads as the prompt: refused on the stored arguments, saying they are the profile's.</summary>
    [Fact]
    public async Task A_patch_moving_a_profile_to_the_host_is_held_to_what_the_host_reads_as_a_prompt()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate,
            new { name = "moving-bare", host = "claude_code", program = "claude", args = new[] { "--model", "made-up" } },
            Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileUpdate,
            new { name = "moving-bare", host = "gemini_cli", program = "gemini" },
            HttpStatusCode.BadRequest,
            Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal(("args", "invalid"), (detail.Field, detail.Code));
        Assert.StartsWith("the profile's args are refused on gemini_cli: ", detail.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The parser reads each of these as a reserved option, by stripping a dash or an underscore from its name, so a name of
    /// anything but letters, digits and single dashes is refused before the reserved list is asked, at create and at update.
    /// </summary>
    [Theory]
    [InlineData("---acp")]
    [InlineData("--acp-")]
    [InlineData("--fake-responses_=x")]
    [InlineData("--record-responses-=p")]
    [InlineData("--delete-session-=id")]
    [InlineData("--prompt-=t")]
    [InlineData("--approval-_mode=x")]
    [InlineData("--approval--mode=x")]
    public async Task A_name_the_parser_would_read_as_a_reserved_option_is_refused_at_create_and_at_update(string flag)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var created = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "gemini-spelled", host = "gemini_cli", program = "gemini", args = new[] { flag } },
            HttpStatusCode.BadRequest,
            Ct);
        Assert.Contains(created.Details!, detail => detail.Field.StartsWith("args", StringComparison.Ordinal));

        await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate,
            new { name = "gemini-plain", host = "gemini_cli", program = "gemini", args = new[] { "--model=made-up" } },
            Ct);
        var updated = await api.PostErrorAsync(
            Operations.ProfileUpdate,
            new { name = "gemini-plain", args = new[] { flag } },
            HttpStatusCode.BadRequest,
            Ct);
        Assert.Contains(updated.Details!, detail => detail.Field.StartsWith("args", StringComparison.Ordinal));
        var read = await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileGet, new { name = "gemini-plain" }, Ct);
        Assert.Equal(1, read.Revision.Number);
    }
}
