using System.Net;
using Jason.Contracts.Api;
using Jason.Contracts.Execution;

namespace Jason.Runtime.Tests.Profiles;

/// <summary>
/// What a profile naming <c>copilot_cli</c> is held to where it is written: the one rule for every host's tools, as this
/// host answers it. Its documented tool table has a fetch and no search, so a grant of a search is refused naming the
/// host and the tool, and a deny of one is kept as the person's word.
/// </summary>
public class CopilotCliProfileTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_grant_of_a_search_is_refused_naming_the_host_and_the_tool()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "searching", host = "copilot_cli", program = "copilot", tools = new[] { "web_search" } },
            HttpStatusCode.BadRequest,
            Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal(("tools[0]", "invalid"), (detail.Field, detail.Code));
        Assert.Contains("copilot_cli", detail.Message, StringComparison.Ordinal);
        Assert.Contains("web_search", detail.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_deny_of_a_search_is_kept_and_a_grant_of_a_fetch_is_accepted()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var created = await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate,
            new { name = "fetching", host = "copilot_cli", program = "copilot", tools = new[] { "web_fetch" }, deny = new[] { "web_search" } },
            Ct);

        Assert.Equal(["web_search"], created.Revision.Deny);
        Assert.Equal([AgentTool.WebFetch], created.Revision.Tools);
    }

    [Fact]
    public async Task A_deny_that_is_two_rules_and_an_argument_that_glues_an_option_to_its_value_are_refused_naming_the_host()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var denied = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "two-rules", host = "copilot_cli", program = "copilot", deny = new[] { "shell(git push),write" } },
            HttpStatusCode.BadRequest,
            Ct);
        var glued = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "glued", host = "copilot_cli", program = "copilot", args = new[] { "-pfoo" } },
            HttpStatusCode.BadRequest,
            Ct);

        var deny = Assert.Single(denied.Details!);
        Assert.Equal(("deny[0]", "invalid"), (deny.Field, deny.Code));
        Assert.Contains("copilot_cli", deny.Message, StringComparison.Ordinal);
        var args = Assert.Single(glued.Details!);
        Assert.Equal(("args", "invalid"), (args.Field, args.Code));
        Assert.Contains("copilot_cli", args.Message, StringComparison.Ordinal);
    }

    private const string Reason = "the host's parser is not read, so only the documented options the person owns are taken";

    /// <summary>
    /// The host's parser is closed, so a profile's arguments are an allowlist of the documented options a person owns: an
    /// undocumented long flag, a word shaped like the flags that allow everything, a cluster of short options and a short
    /// option glued to its value are all refused, at create and at update alike, naming the host and the reason.
    /// </summary>
    [Theory]
    [InlineData("--made-up-flag")]
    [InlineData("--made-up-flag=1")]
    [InlineData("--yolo")]
    [InlineData("--allow-all-tools")]
    [InlineData("--auto-approve-everything")]
    [InlineData("-pi")]
    [InlineData("-pfoo")]
    [InlineData("-Cfolder")]
    [InlineData("--log-level=debug")]
    public async Task A_word_outside_the_allowlist_is_refused_at_create_and_at_update_naming_the_host_and_the_reason(string word)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var created = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "refused-at-create", host = "copilot_cli", program = "copilot", args = new[] { "--model=made-up-model", word } },
            HttpStatusCode.BadRequest,
            Ct);
        await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate,
            new { name = "refused-at-update", host = "copilot_cli", program = "copilot", args = new[] { "--model=made-up-model" } },
            Ct);
        var updated = await api.PostErrorAsync(
            Operations.ProfileUpdate,
            new { name = "refused-at-update", args = new[] { "--model=made-up-model", word } },
            HttpStatusCode.BadRequest,
            Ct);

        foreach (var error in new[] { created, updated })
        {
            if (word is "--yolo" or "--allow-all-tools")
            {
                // A flag the runtime composes or reserves is refused as that, before the allowlist is asked.
                var reserved = Assert.Single(error.Details!);
                Assert.Equal(("args[1]", "reserved"), (reserved.Field, reserved.Code));
                Assert.Contains(word, reserved.Message, StringComparison.Ordinal);
                continue;
            }

            var refusal = Assert.Single(error.Details!, detail => detail.Field == "args" && detail.Code == "invalid");
            Assert.Contains("copilot_cli", refusal.Message, StringComparison.Ordinal);
            Assert.Contains(Reason, refusal.Message, StringComparison.Ordinal);
            Assert.Contains(word, refusal.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task The_documented_options_a_person_owns_are_accepted_at_create_and_at_update()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        string[] owned = ["--model=made-up-model", "--effort=high", "--reasoning-effort=low", "--max-ai-credits=5", "--no-color", "--disable-client-telemetry"];

        var created = await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate, new { name = "owned", host = "copilot_cli", program = "copilot", args = new[] { "--model=made-up-model" } }, Ct);
        var updated = await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileUpdate, new { name = "owned", args = owned }, Ct);

        Assert.Equal(["--model=made-up-model"], created.Revision.Args);
        Assert.Equal(owned, updated.Revision.Args);
    }

    [Fact]
    public async Task A_command_word_with_commas_is_refused_so_the_callback_stays_one_rule()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "three-rules", host = "copilot_cli", program = "copilot", cli_command = "jason,shell,x" },
            HttpStatusCode.BadRequest,
            Ct);

        Assert.Equal("cli_command", Assert.Single(error.Details!).Field);
    }

    /// <summary>
    /// Arguments written under another host are held to this one where a patch moves the profile here and leaves them
    /// alone: a value given as a separate argument is no option this host's allowlist takes, so the move is refused,
    /// speaking of the profile's own list, and nothing is written.
    /// </summary>
    [Fact]
    public async Task A_move_to_copilot_whose_stored_arguments_the_host_does_not_take_is_refused_and_writes_nothing()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate,
            new { name = "moving", host = "claude_code", program = "claude", args = new[] { "--model", "sonnet" } },
            Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileUpdate, new { name = "moving", host = "copilot_cli", program = "copilot" }, HttpStatusCode.BadRequest, Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal(("args", "invalid"), (detail.Field, detail.Code));
        Assert.StartsWith("the profile's args are refused on copilot_cli", detail.Message, StringComparison.Ordinal);
        Assert.Contains(Reason, detail.Message, StringComparison.Ordinal);
        var read = await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileGet, new { name = "moving" }, Ct);
        Assert.Equal(1, read.Revision.Number);
        Assert.Equal(AgentHostKind.ClaudeCode, read.Revision.Host);
    }

    /// <summary>
    /// A deny the host reads as an MCP server's name, and so as nothing the role is offered, is refused where a profile is
    /// written: at create, at update, and where a patch moves a stored profile to this host and leaves its deny alone.
    /// A grant of the fetch tool beside a deny of it by its own name is the case that matters: the fetch the person meant
    /// to take away would stay.
    /// </summary>
    [Theory]
    [InlineData("web_fetch")]
    [InlineData("WebFetch")]
    [InlineData("Shell(rm:*)")]
    [InlineData("Write")]
    [InlineData("made-up-server")]
    public async Task A_deny_the_host_does_not_read_as_a_rule_is_refused_at_create_at_update_and_on_a_move(string word)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var created = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "refused-at-create", host = "copilot_cli", program = "copilot", tools = new[] { "web_fetch" }, deny = new[] { word } },
            HttpStatusCode.BadRequest,
            Ct);
        await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate, new { name = "refused-at-update", host = "copilot_cli", program = "copilot" }, Ct);
        var updated = await api.PostErrorAsync(
            Operations.ProfileUpdate, new { name = "refused-at-update", deny = new[] { word } }, HttpStatusCode.BadRequest, Ct);
        await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate, new { name = "refused-on-move", host = "claude_code", program = "claude", deny = new[] { word } }, Ct);
        var moved = await api.PostErrorAsync(
            Operations.ProfileUpdate, new { name = "refused-on-move", host = "copilot_cli", program = "copilot" }, HttpStatusCode.BadRequest, Ct);

        Assert.All(new[] { created, updated }, error =>
        {
            var detail = Assert.Single(error.Details!, detail => detail.Field == "deny[0]");
            Assert.Equal("invalid", detail.Code);
            Assert.Contains("copilot_cli", detail.Message, StringComparison.Ordinal);
            Assert.Contains(word, detail.Message, StringComparison.Ordinal);
        });
        var onMove = Assert.Single(moved.Details!, detail => detail.Field == "deny[0]");
        Assert.StartsWith("the profile's deny[0]", onMove.Message, StringComparison.Ordinal);
        Assert.Contains("copilot_cli", onMove.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_deny_of_the_fetch_tool_by_its_own_name_is_refused_naming_the_word_the_host_reads()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "fetch-and-deny", host = "copilot_cli", program = "copilot", tools = new[] { "web_fetch" }, deny = new[] { "web_fetch" } },
            HttpStatusCode.BadRequest,
            Ct);

        var detail = Assert.Single(error.Details!, detail => detail.Field == "deny[0]");
        Assert.Contains("url", detail.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("web_search")]
    [InlineData("WebSearch")]
    public async Task A_deny_of_a_search_stays_kept_whichever_way_it_is_spelled(string word)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var created = await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate, new { name = "no-search", host = "copilot_cli", program = "copilot", deny = new[] { word } }, Ct);

        Assert.Equal([word], created.Revision.Deny);
    }
}
