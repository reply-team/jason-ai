using System.Net;
using Jason.Contracts.Api;
using Jason.Contracts.Execution;
using Jason.Runtime.Execution.Hosts;
using Jason.Runtime.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Profiles;

/// <summary>
/// What a host says of a profile's words, held where the profile is written, once the host is parsed: a grant of a tool
/// the host does not have is refused naming the host and the tool; a deny of such a tool is kept, since it holds anyway;
/// a deny the host has no way to write, and a grant it has but cannot make true, are refused naming the host; and so are
/// arguments the host's own reading refuses. Each refusal is <c>invalid</c>, and its message names the host and the word.
/// The host here is a stand-in for one with all four answers, registered over the one that ships.
/// </summary>
public class ProfileHostRulesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<RuntimeApiFixture> StartAsync() =>
        RuntimeApiFixture.StartAsync(Ct, configureServices: services => services.AddSingleton<IAgentHost>(new RuleHost()));

    [Fact]
    public async Task A_grant_of_a_tool_the_host_does_not_have_is_refused_naming_the_host_and_the_tool()
    {
        await using var api = await StartAsync();

        var error = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "fetching", host = "claude_code", program = "claude", tools = new[] { "web_fetch" } },
            HttpStatusCode.BadRequest,
            Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal(("tools[0]", "invalid"), (detail.Field, detail.Code));
        Assert.Contains("claude_code", detail.Message, StringComparison.Ordinal);
        Assert.Contains("web_fetch", detail.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_deny_of_a_tool_the_host_does_not_have_is_kept_as_the_persons_word()
    {
        await using var api = await StartAsync();

        var created = await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate,
            new { name = "no-fetch", host = "claude_code", program = "claude", deny = new[] { "WebFetch" } },
            Ct);

        Assert.Equal(["WebFetch"], created.Revision.Deny);
    }

    [Fact]
    public async Task A_deny_the_host_cannot_write_is_refused_naming_the_host_and_the_word()
    {
        await using var api = await StartAsync();

        var error = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "undeniable", host = "claude_code", program = "claude", deny = new[] { "Write", RuleHost.Undeniable } },
            HttpStatusCode.BadRequest,
            Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal(("deny[1]", "invalid"), (detail.Field, detail.Code));
        Assert.Contains("claude_code", detail.Message, StringComparison.Ordinal);
        Assert.Contains(RuleHost.Undeniable, detail.Message, StringComparison.Ordinal);
        Assert.Contains(RuleHost.Why, detail.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_grant_the_host_has_but_jason_cannot_make_true_is_refused_naming_the_host()
    {
        await using var api = await StartAsync();

        var error = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "searching", host = "claude_code", program = "claude", tools = new[] { "web_search" } },
            HttpStatusCode.BadRequest,
            Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal(("tools[0]", "invalid"), (detail.Field, detail.Code));
        Assert.Contains("claude_code", detail.Message, StringComparison.Ordinal);
        Assert.Contains("web_search", detail.Message, StringComparison.Ordinal);
        Assert.Contains(RuleHost.Why, detail.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Arguments_the_host_refuses_are_refused_naming_the_host()
    {
        await using var api = await StartAsync();

        var error = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "bare-word", host = "claude_code", program = "claude", args = new[] { "--model", "made-up", RuleHost.BareWord } },
            HttpStatusCode.BadRequest,
            Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal(("args", "invalid"), (detail.Field, detail.Code));
        Assert.Contains("claude_code", detail.Message, StringComparison.Ordinal);
        Assert.Contains(RuleHost.Why, detail.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An edit is held to the revision as it would be launched, as the grant beside the deny is: a patch naming a deny the
    /// host cannot write is refused, and one that leaves the lists alone meets them as the profile has them.
    /// </summary>
    [Fact]
    public async Task A_patch_naming_a_deny_the_host_cannot_write_is_refused_and_writes_nothing()
    {
        await using var api = await StartAsync();
        await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate, new { name = "editing", host = "claude_code", program = "claude" }, Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileUpdate,
            new { name = "editing", deny = new[] { RuleHost.Undeniable } },
            HttpStatusCode.BadRequest,
            Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal(("deny[0]", "invalid"), (detail.Field, detail.Code));
        var read = await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileGet, new { name = "editing" }, Ct);
        Assert.Equal(1, read.Revision.Number);
    }

    /// <summary>The one that ships, with every answer a host may give: a tool it lacks, one it cannot make true, a deny it cannot write and arguments it refuses.</summary>
    private sealed class RuleHost : IAgentHost
    {
        public const string Undeniable = "Forbidden(*)";

        public const string BareWord = "bare";

        public const string Why = "the stand-in has no way to say this";

        private readonly ClaudeCodeHost _inner = new();

        public AgentHostKind Kind => _inner.Kind;

        public IReadOnlySet<string> ReservedFlags => _inner.ReservedFlags;

        public string? ToolRule(AgentTool tool) => tool == AgentTool.WebFetch ? null : _inner.ToolRule(tool);

        public string? GrantProblem(AgentTool tool) => tool == AgentTool.WebSearch ? Why : null;

        public string? DenyProblem(string word) => string.Equals(word, Undeniable, StringComparison.Ordinal) ? Why : null;

        public string? ArgsProblem(IReadOnlyList<string> args) => args.Contains(BareWord, StringComparer.Ordinal) ? Why : null;

        public HostLaunch Compose(ExecutionProfileRevision revision, IReadOnlyList<string> launch, MemoryFileLocations memoryFiles) =>
            _inner.Compose(revision, launch, memoryFiles);

        public void WriteWorkDirectory(string workDir, IReadOnlyList<string> allow, IReadOnlyList<string> deny) =>
            _inner.WriteWorkDirectory(workDir, allow, deny);

        public string? Unavailable(int exitCode, string? stderrTail) => null;
    }
}
