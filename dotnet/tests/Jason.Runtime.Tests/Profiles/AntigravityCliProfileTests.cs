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
}
