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
}
