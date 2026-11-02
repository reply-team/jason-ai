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

    private static Task<RuntimeApiFixture> StartAsync(RuleHost? host = null) =>
        RuntimeApiFixture.StartAsync(Ct, configureServices: services => services.AddSingleton<IAgentHost>(host ?? new RuleHost()));

    /// <summary>
    /// A profile stored while the host had no objection, with the host strict from then on: the revision a patch meets is
    /// one the host now refuses, as a profile written before a host learned a rule would be.
    /// </summary>
    private static async Task<(RuntimeApiFixture Api, RuleHost Host)> StoredAsync(object create)
    {
        var host = new RuleHost { Strict = false };
        var api = await StartAsync(host);
        await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileCreate, create, Ct);
        host.Strict = true;
        return (api, host);
    }

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

    /// <summary>
    /// A patch naming only the tools meets the host the profile already has: a grant of a tool that host does not have is
    /// refused, naming the host and the tool, and the message speaks of the patch's own list rather than the profile's.
    /// </summary>
    [Fact]
    public async Task A_patch_naming_only_the_tools_is_held_to_the_stored_host_and_speaks_of_its_own_list()
    {
        await using var api = await StartAsync();
        await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate, new { name = "tooling", host = "claude_code", program = "claude" }, Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileUpdate,
            new { name = "tooling", tools = new[] { "web_fetch" } },
            HttpStatusCode.BadRequest,
            Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal(("tools[0]", "invalid"), (detail.Field, detail.Code));
        Assert.StartsWith("tools[0] grants web_fetch, which claude_code does not have", detail.Message, StringComparison.Ordinal);
        var read = await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileGet, new { name = "tooling" }, Ct);
        Assert.Equal(1, read.Revision.Number);
    }

    /// <summary>
    /// A patch that names neither list meets the stored tools as the profile has them: a grant the host has but cannot make
    /// true is refused, naming the host, and the message says the list is the profile's.
    /// </summary>
    [Fact]
    public async Task A_patch_that_leaves_the_tools_alone_meets_the_stored_grant_and_says_it_is_the_profiles()
    {
        var (api, _) = await StoredAsync(new { name = "searching", host = "claude_code", program = "claude", tools = new[] { "web_search" } });
        await using var _api = api;

        var error = await api.PostErrorAsync(
            Operations.ProfileUpdate,
            new { name = "searching", cli_command = "jason-next" },
            HttpStatusCode.BadRequest,
            Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal(("tools[0]", "invalid"), (detail.Field, detail.Code));
        Assert.StartsWith("the profile's tools[0] grants web_search, which claude_code has and Jason cannot make true on it", detail.Message, StringComparison.Ordinal);
        Assert.Contains(RuleHost.Why, detail.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A patch naming only the program meets the stored arguments: arguments the host refuses are refused, naming the host,
    /// and the message says they are the profile's; nothing is written.
    /// </summary>
    [Fact]
    public async Task A_patch_naming_only_the_program_meets_the_stored_arguments_and_says_they_are_the_profiles()
    {
        var (api, _) = await StoredAsync(new { name = "bare-word", host = "claude_code", program = "claude", args = new[] { RuleHost.BareWord } });
        await using var _api = api;

        var error = await api.PostErrorAsync(
            Operations.ProfileUpdate,
            new { name = "bare-word", program = "claude-next" },
            HttpStatusCode.BadRequest,
            Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal(("args", "invalid"), (detail.Field, detail.Code));
        Assert.StartsWith("the profile's args are refused on claude_code: ", detail.Message, StringComparison.Ordinal);
        var read = await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileGet, new { name = "bare-word" }, Ct);
        Assert.Equal(1, read.Revision.Number);
    }

    /// <summary>Arguments a patch names are its own, and the refusal says so: no owner before them.</summary>
    [Fact]
    public async Task A_patch_naming_arguments_the_host_refuses_speaks_of_its_own_arguments()
    {
        await using var api = await StartAsync();
        await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate, new { name = "arguing", host = "claude_code", program = "claude" }, Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileUpdate,
            new { name = "arguing", args = new[] { RuleHost.BareWord } },
            HttpStatusCode.BadRequest,
            Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal(("args", "invalid"), (detail.Field, detail.Code));
        Assert.StartsWith("args are refused on claude_code: ", detail.Message, StringComparison.Ordinal);
    }

    /// <summary>The one that ships, with every answer a host may give: a tool it lacks, one it cannot make true, a deny it cannot write and arguments it refuses.</summary>
    private sealed class RuleHost : IAgentHost
    {
        public const string Undeniable = "Forbidden(*)";

        public const string BareWord = "bare";

        public const string Why = "the stand-in has no way to say this";

        private readonly ClaudeCodeHost _inner = new();

        /// <summary>Whether the stand-in gives its answers; off, it answers as the host that ships.</summary>
        public bool Strict { get; set; } = true;

        public AgentHostKind Kind => _inner.Kind;

        public IReadOnlySet<string> ReservedFlags => _inner.ReservedFlags;

        public string? ToolRule(AgentTool tool) => Strict && tool == AgentTool.WebFetch ? null : _inner.ToolRule(tool);

        public string? GrantProblem(AgentTool tool) => Strict && tool == AgentTool.WebSearch ? Why : null;

        public string? DenyProblem(string word) => Strict && string.Equals(word, Undeniable, StringComparison.Ordinal) ? Why : null;

        public string? ArgsProblem(IReadOnlyList<string> args) => Strict && args.Contains(BareWord, StringComparer.Ordinal) ? Why : null;

        public HostLaunch Compose(ExecutionProfileRevision revision, IReadOnlyList<string> launch, MemoryFileLocations memoryFiles) =>
            _inner.Compose(revision, launch, memoryFiles);

        public void WriteWorkDirectory(string workDir, IReadOnlyList<string> allow, IReadOnlyList<string> deny) =>
            _inner.WriteWorkDirectory(workDir, allow, deny);

        public string? Unavailable(int exitCode, string? stderrTail) => null;
    }
}
