using System.Net;
using Jason.Contracts.Api;
using Jason.Contracts.Execution;

namespace Jason.Runtime.Tests.Profiles;

/// <summary>
/// What a profile naming <c>antigravity_cli</c> is held to where it is written. The host takes its permission rules from
/// the person's own settings files and from nowhere a run could carry them, so a grant of a tool and a deny of any word are
/// refused naming the host, and an argument that is a bare word is refused; a profile that asks of the host nothing it
/// cannot do is accepted as it is.
/// </summary>
public class AntigravityCliProfileTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("web_fetch")]
    [InlineData("web_search")]
    public async Task A_grant_of_either_tool_is_refused_naming_the_host_and_the_tool(string tool)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "granting", host = "antigravity_cli", program = "agy", tools = new[] { tool } },
            HttpStatusCode.BadRequest,
            Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal(("tools[0]", "invalid"), (detail.Field, detail.Code));
        Assert.Contains("antigravity_cli", detail.Message, StringComparison.Ordinal);
        Assert.Contains(tool, detail.Message, StringComparison.Ordinal);

        // A search has no rule in the host's table, so the refusal says what Jason cannot do on the host and claims nothing
        // about whether the host has the tool.
        Assert.Contains($"grants {tool}, which Jason cannot make true on antigravity_cli: ", detail.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_deny_of_any_word_is_refused_naming_the_host_and_the_word()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "denying", host = "antigravity_cli", program = "agy", deny = new[] { "web_fetch" } },
            HttpStatusCode.BadRequest,
            Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal(("deny[0]", "invalid"), (detail.Field, detail.Code));
        Assert.Contains("antigravity_cli", detail.Message, StringComparison.Ordinal);
        Assert.Contains("web_fetch", detail.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_argument_that_is_a_bare_word_is_refused_naming_the_host_and_a_flag_with_its_value_joined_is_kept()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "bare", host = "antigravity_cli", program = "agy", args = new[] { "--model", "made-up" } },
            HttpStatusCode.BadRequest,
            Ct);
        var created = await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate,
            new { name = "joined", host = "antigravity_cli", program = "agy", args = new[] { "--model=made-up" } },
            Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal(("args", "invalid"), (detail.Field, detail.Code));
        Assert.Contains("antigravity_cli", detail.Message, StringComparison.Ordinal);
        Assert.Equal(["--model=made-up"], created.Revision.Args);
        Assert.Equal(AgentHostKind.AntigravityCli, created.Revision.Host);
    }

    [Fact]
    public async Task A_flag_the_runtime_composes_is_reserved_on_this_host()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "reserved", host = "antigravity_cli", program = "agy", args = new[] { "--print-timeout=5m" } },
            HttpStatusCode.BadRequest,
            Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal(("args[0]", "reserved"), (detail.Field, detail.Code));
        Assert.Contains("--print-timeout", detail.Message, StringComparison.Ordinal);
    }

    private const string ParserNotRead = "the host's parser is not read, so only the documented options the person owns are taken";

    /// <summary>
    /// The host's parser is not read, so a profile's argument is one of the documented options the person owns or it is
    /// refused where it is written, on create and on a patch alike: an option the host documents but does not name for the
    /// person, one it does not document, a word shaped like a switch that skips permissions, a cluster, and a glued value.
    /// </summary>
    [Theory]
    [InlineData("--disable-slash-commands")]
    [InlineData("--yolo")]
    [InlineData("--dangerously-bypass-approvals-and-sandbox")]
    [InlineData("-pf")]
    [InlineData("-phello")]
    public async Task A_word_outside_the_allowlist_is_refused_on_create_and_on_a_patch_for_the_reason_the_parser_is_not_read(string word)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var created = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "outside-create", host = "antigravity_cli", program = "agy", args = new[] { "--model=made-up", word } },
            HttpStatusCode.BadRequest,
            Ct);
        await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate, new { name = "outside-patch", host = "antigravity_cli", program = "agy" }, Ct);
        var patched = await api.PostErrorAsync(
            Operations.ProfileUpdate,
            new { name = "outside-patch", args = new[] { word } },
            HttpStatusCode.BadRequest,
            Ct);

        foreach (var error in new[] { created, patched })
        {
            var detail = Assert.Single(error.Details!);
            Assert.Equal(("args", "invalid"), (detail.Field, detail.Code));
            Assert.Contains("antigravity_cli", detail.Message, StringComparison.Ordinal);
            Assert.Contains(word, detail.Message, StringComparison.Ordinal);
            Assert.Contains(ParserNotRead, detail.Message, StringComparison.Ordinal);
        }

        var read = await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileGet, new { name = "outside-patch" }, Ct);
        Assert.Equal(1, read.Revision.Number);
    }

    /// <summary>A value with a control character is refused where the profile is written, by the rule every host shares.</summary>
    [Fact]
    public async Task An_option_value_with_a_control_character_is_refused_on_create()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "control", host = "antigravity_cli", program = "agy", args = new[] { "--model=a\u0000b" } },
            HttpStatusCode.BadRequest,
            Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal(("args[0]", "invalid"), (detail.Field, detail.Code));
        Assert.Contains("control character", detail.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A profile moved onto this host by a patch that names the host and the program, and none of its lists, is held to
    /// the host as it is stored: a grant, a deny and a bare-word argument written under another host are each refused,
    /// speaking of the profile's own list, and nothing is written.
    /// </summary>
    [Fact]
    public async Task A_move_onto_the_host_is_refused_for_each_stored_word_it_cannot_keep_and_writes_nothing()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate,
            new
            {
                name = "moving",
                host = "claude_code",
                program = "claude",
                tools = new[] { "web_fetch" },
                deny = new[] { "Bash(rm:*)" },
                args = new[] { "--model", "sonnet" },
            },
            Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileUpdate,
            new { name = "moving", host = "antigravity_cli", program = "agy" },
            HttpStatusCode.BadRequest,
            Ct);

        Assert.Equal(3, error.Details!.Count);
        Assert.Equal(["args", "deny[0]", "tools[0]"], error.Details.Select(detail => detail.Field).Order(StringComparer.Ordinal));
        Assert.All(error.Details, detail =>
        {
            Assert.Equal("invalid", detail.Code);
            Assert.Contains("antigravity_cli", detail.Message, StringComparison.Ordinal);
            Assert.Contains("the profile's", detail.Message, StringComparison.Ordinal);
        });
        var read = await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileGet, new { name = "moving" }, Ct);
        Assert.Equal((1, AgentHostKind.ClaudeCode), (read.CurrentRevision, read.Revision.Host));
    }

    /// <summary>A profile that asks of this host nothing it cannot do moves onto it, keeping what it says.</summary>
    [Fact]
    public async Task A_plain_profile_moves_onto_the_host_and_keeps_its_owned_option()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate,
            new { name = "plain-move", host = "claude_code", program = "claude", args = new[] { "--model=made-up" } },
            Ct);

        var moved = await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileUpdate,
            new { name = "plain-move", host = "antigravity_cli", program = "agy" },
            Ct);

        Assert.Equal(AgentHostKind.AntigravityCli, moved.Revision.Host);
        Assert.Equal("agy", moved.Revision.Program);
        Assert.Equal(["--model=made-up"], moved.Revision.Args);
    }

    /// <summary>The two options the person owns are kept on create and on a patch, with no value of theirs checked.</summary>
    [Fact]
    public async Task The_model_and_the_effort_are_kept_on_create_and_on_a_patch()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var created = await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate,
            new { name = "owned", host = "antigravity_cli", program = "agy", args = new[] { "--model=made-up" } },
            Ct);
        var patched = await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileUpdate,
            new { name = "owned", args = new[] { "--model=made-up", "--effort=whatever-the-host-calls-it" } },
            Ct);

        Assert.Equal(["--model=made-up"], created.Revision.Args);
        Assert.Equal(["--model=made-up", "--effort=whatever-the-host-calls-it"], patched.Revision.Args);
    }

    /// <summary>
    /// A flag that is reserved keeps its own specific refusal, the reserved one, and the allowlist does not add a second
    /// detail for the same word; every other word gets the allowlist's.
    /// </summary>
    [Theory]
    [InlineData("--dangerously-skip-permissions")]
    [InlineData("--print-timeout=5m")]
    public async Task A_reserved_flag_gets_the_reserved_refusal_alone_and_the_allowlist_speaks_of_no_other(string word)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "reserved-alone", host = "antigravity_cli", program = "agy", args = new[] { word } },
            HttpStatusCode.BadRequest,
            Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal(("args[0]", "reserved"), (detail.Field, detail.Code));
        Assert.DoesNotContain(ParserNotRead, detail.Message, StringComparison.Ordinal);
    }
}
