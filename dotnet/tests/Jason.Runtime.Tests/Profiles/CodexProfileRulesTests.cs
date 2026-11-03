using System.Net;
using Jason.Contracts.Api;

namespace Jason.Runtime.Tests.Profiles;

/// <summary>
/// A profile naming Codex CLI meets the host's own answers where it is written, through the host this build registers:
/// a fetch it has no tool for is not granted, a deny the sandbox cannot hold is refused rather than dropped at a launch,
/// a bare word that would become the prompt is refused, and a search granted and denied at once is refused as on every
/// host. Each refusal names the host.
/// </summary>
public class CodexProfileRulesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_grant_of_a_fetch_is_refused_naming_the_host_and_the_tool()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "fetching", host = "codex", program = "codex", tools = new[] { "web_fetch" } },
            HttpStatusCode.BadRequest,
            Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal(("tools[0]", "invalid"), (detail.Field, detail.Code));
        Assert.Contains("codex", detail.Message, StringComparison.Ordinal);
        Assert.Contains("web_fetch", detail.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_profile_with_a_search_and_the_denies_the_sandbox_holds_is_written_as_given()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var created = await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate,
            new
            {
                name = "local-codex",
                host = "codex",
                program = "codex",
                args = new[] { "--model=made-up-model" },
                tools = new[] { "web_search" },
                deny = new[] { "Write", "WebFetch", "Read(~/private/**)" },
            },
            Ct);

        Assert.Equal(AgentHostKind.Codex, created.Revision.Host);
        Assert.Equal(["Write", "WebFetch", "Read(~/private/**)"], created.Revision.Deny);
        Assert.Equal([AgentTool.WebSearch], created.Revision.Tools);
    }

    [Fact]
    public async Task A_deny_of_a_command_is_refused_naming_the_host_and_the_word()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "no-curl", host = "codex", program = "codex", deny = new[] { "Write", "Bash(curl *)" } },
            HttpStatusCode.BadRequest,
            Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal(("deny[1]", "invalid"), (detail.Field, detail.Code));
        Assert.Contains("codex", detail.Message, StringComparison.Ordinal);
        Assert.Contains("Bash(curl *)", detail.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_bare_word_among_the_arguments_is_refused_naming_the_host()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "bare-word", host = "codex", program = "codex", args = new[] { "--model", "made-up-model" } },
            HttpStatusCode.BadRequest,
            Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal(("args", "invalid"), (detail.Field, detail.Code));
        Assert.Contains("codex", detail.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The host's parser takes a flag by a second name its help never prints. Each of them sets what a flag the runtime
    /// composes sets, and <c>--yolo</c> runs every command with no sandbox at all, so the second name is held as the first.
    /// </summary>
    [Theory]
    [InlineData("--yolo")]
    [InlineData("--not-so-yolo")]
    [InlineData("--experimental-json")]
    public async Task A_second_name_of_a_flag_codex_composes_is_refused_naming_the_flag(string flag)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "aliased", host = "codex", program = "codex", args = new[] { flag } },
            HttpStatusCode.BadRequest,
            Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal(("args[0]", "reserved"), (detail.Field, detail.Code));
        Assert.Contains($"'{flag}'", detail.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--yolo")]
    [InlineData("--not-so-yolo")]
    [InlineData("--experimental-json")]
    public async Task A_second_name_of_a_flag_codex_composes_is_refused_at_an_update_and_writes_nothing(string flag)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate, new { name = "aliased", host = "codex", program = "codex" }, Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileUpdate, new { name = "aliased", args = new[] { flag } }, HttpStatusCode.BadRequest, Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal(("args[0]", "reserved"), (detail.Field, detail.Code));
        Assert.Contains($"'{flag}'", detail.Message, StringComparison.Ordinal);
        var read = await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileGet, new { name = "aliased" }, Ct);
        Assert.Equal(1, read.Revision.Number);
        Assert.Empty(read.Revision.Args);
    }

    [Theory]
    [InlineData("--yolo")]
    [InlineData("--not-so-yolo")]
    [InlineData("--experimental-json")]
    public async Task A_move_to_codex_whose_stored_arguments_carry_a_second_name_is_refused_and_writes_nothing(string flag)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate, new { name = "moving", host = "claude_code", program = "claude", args = new[] { flag } }, Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileUpdate, new { name = "moving", host = "codex" }, HttpStatusCode.BadRequest, Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal(("args[0]", "reserved"), (detail.Field, detail.Code));
        Assert.Contains(flag, detail.Message, StringComparison.Ordinal);
        Assert.Contains("codex", detail.Message, StringComparison.Ordinal);
        var read = await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileGet, new { name = "moving" }, Ct);
        Assert.Equal(1, read.Revision.Number);
        Assert.Equal(AgentHostKind.ClaudeCode, read.Revision.Host);
    }

    /// <summary>
    /// The host's parser reads one dash and a letter followed by more as that letter's flag with its value glued on, or
    /// as several letters' flags run together, so <c>-sdanger-full-access</c> and <c>-sp</c> both set <c>-s</c>. A word
    /// of one dash is a flag of one letter or nothing, and the letter with its value after <c>=</c> is that letter's flag.
    /// </summary>
    [Theory]
    [InlineData("-sdanger-full-access", "args", "invalid")]
    [InlineData("-sp", "args", "invalid")]
    [InlineData("-ms", "args", "invalid")]
    [InlineData("-Cfar/away", "args", "invalid")]
    [InlineData("-", "args", "invalid")]
    [InlineData("--", "args", "invalid")]
    [InlineData("-s=danger-full-access", "args[0]", "reserved")]
    public async Task A_short_flag_codex_composes_cannot_be_reached_by_running_letters_or_a_value_together(
        string word, string field, string code)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "clustered", host = "codex", program = "codex", args = new[] { word } },
            HttpStatusCode.BadRequest,
            Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal((field, code), (detail.Field, detail.Code));
        Assert.Contains(code == "reserved" ? "'-s'" : "codex", detail.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A flag one host leaves to the person may be one another host composes. A patch that moves a profile to Codex and
    /// leaves its arguments alone would launch the stored ones after the flags Codex composes, where the last value wins,
    /// so the stored arguments are held to Codex's flags and the profile keeps the revision it had.
    /// </summary>
    [Fact]
    public async Task A_move_to_codex_whose_stored_arguments_carry_a_flag_codex_composes_is_refused_and_writes_nothing()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate,
            new { name = "moving", host = "claude_code", program = "claude", args = new[] { "--sandbox=danger-full-access" } },
            Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileUpdate, new { name = "moving", host = "codex" }, HttpStatusCode.BadRequest, Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal(("args[0]", "reserved"), (detail.Field, detail.Code));
        Assert.Contains("--sandbox", detail.Message, StringComparison.Ordinal);
        Assert.Contains("codex", detail.Message, StringComparison.Ordinal);
        var read = await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileGet, new { name = "moving" }, Ct);
        Assert.Equal(1, read.Revision.Number);
        Assert.Equal(AgentHostKind.ClaudeCode, read.Revision.Host);
    }

    /// <summary>
    /// Codex reads no rule by a word outside the few its sandbox holds, so a deny of a tool it does not have, of an MCP
    /// tool or of a fetch of one domain is refused where it is written, when a patch names it, and when a profile that
    /// carries it moves to Codex, naming the host and the word; a profile that is refused keeps the revision it had.
    /// </summary>
    [Theory]
    [InlineData("Glob")]
    [InlineData("Grep")]
    [InlineData("Task")]
    [InlineData("mcp__made_up__send")]
    [InlineData("WebFetch(domain:x)")]
    public async Task A_deny_codex_reads_no_rule_by_is_refused_at_a_create_an_update_and_a_move(string word)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var created = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "denying", host = "codex", program = "codex", deny = new[] { "Write", word } },
            HttpStatusCode.BadRequest,
            Ct);
        AssertRefused(created, "deny[1]", word);

        await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate, new { name = "updating", host = "codex", program = "codex" }, Ct);
        var updated = await api.PostErrorAsync(
            Operations.ProfileUpdate, new { name = "updating", deny = new[] { word } }, HttpStatusCode.BadRequest, Ct);
        AssertRefused(updated, "deny[0]", word);

        await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate, new { name = "moving", host = "claude_code", program = "claude", deny = new[] { word } }, Ct);
        var moved = await api.PostErrorAsync(
            Operations.ProfileUpdate, new { name = "moving", host = "codex", program = "codex" }, HttpStatusCode.BadRequest, Ct);
        AssertRefused(moved, "deny[0]", word);

        foreach (var (name, host) in new[] { ("updating", AgentHostKind.Codex), ("moving", AgentHostKind.ClaudeCode) })
        {
            var read = await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileGet, new { name }, Ct);
            Assert.Equal(1, read.Revision.Number);
            Assert.Equal(host, read.Revision.Host);
        }

        static void AssertRefused(ErrorBody error, string field, string word)
        {
            var detail = Assert.Single(error.Details!);
            Assert.Equal((field, "invalid"), (detail.Field, detail.Code));
            Assert.Contains("codex", detail.Message, StringComparison.Ordinal);
            Assert.Contains($"'{word}'", detail.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("whatever", detail.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>A deny of a fetch, which Codex does not have, holds already and is kept, here as on the move.</summary>
    [Fact]
    public async Task A_profile_denying_a_fetch_moves_to_codex_with_the_deny_kept()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate,
            new { name = "moving", host = "claude_code", program = "claude", deny = new[] { "WebFetch" } },
            Ct);

        var moved = await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileUpdate, new { name = "moving", host = "codex", program = "codex" }, Ct);

        Assert.Equal(2, moved.Revision.Number);
        Assert.Equal(AgentHostKind.Codex, moved.Revision.Host);
        Assert.Equal(["WebFetch"], moved.Revision.Deny);
    }

    /// <summary>
    /// What Codex refuses where a profile is written, it refuses where a profile moves to it: a stored bare word that
    /// would become the prompt, a stored grant of a fetch it has no tool for, and a stored deny of a command by its name,
    /// which its sandbox cannot hold. The profile keeps the revision it had.
    /// </summary>
    [Theory]
    [InlineData("args", "args", "invalid")]
    [InlineData("tools", "tools[0]", "invalid")]
    [InlineData("deny", "deny[0]", "invalid")]
    public async Task A_move_to_codex_of_a_stored_word_codex_refuses_is_refused_and_writes_nothing(
        string stored, string field, string code)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        object profile = stored switch
        {
            "args" => new { name = "moving", host = "claude_code", program = "claude", args = new[] { "--model", "made-up-model" } },
            "tools" => new { name = "moving", host = "claude_code", program = "claude", tools = new[] { "web_fetch" } },
            _ => new { name = "moving", host = "claude_code", program = "claude", deny = new[] { "Bash(curl *)" } },
        };
        await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileCreate, profile, Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileUpdate, new { name = "moving", host = "codex", program = "codex" }, HttpStatusCode.BadRequest, Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal((field, code), (detail.Field, detail.Code));
        Assert.Contains("codex", detail.Message, StringComparison.Ordinal);
        var read = await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileGet, new { name = "moving" }, Ct);
        Assert.Equal(1, read.Revision.Number);
        Assert.Equal(AgentHostKind.ClaudeCode, read.Revision.Host);
    }

    [Fact]
    public async Task A_search_granted_and_denied_at_once_is_refused()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "both", host = "codex", program = "codex", tools = new[] { "web_search" }, deny = new[] { "WebSearch" } },
            HttpStatusCode.BadRequest,
            Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal(("tools[0]", "denied"), (detail.Field, detail.Code));
    }
}
