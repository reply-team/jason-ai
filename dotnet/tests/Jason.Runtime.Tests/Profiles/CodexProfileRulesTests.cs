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
