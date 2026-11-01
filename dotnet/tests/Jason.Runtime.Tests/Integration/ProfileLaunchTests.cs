using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Execution;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Integration;

/// <summary>
/// Agent work launched the way it will really be launched: a profile names a program, the dispatcher resolves
/// it, a real child process is started with the command the profile composed, and the attempt records for ever
/// which profile ran it. The host here is the repository's stand-in rather than an installed agent, but nothing
/// between the claim and the child knows that.
/// </summary>
internal static class ProfileFixture
{
    /// <summary>
    /// The stand-in host as a program a profile can name. It is a real executable, so the runtime starts it
    /// exactly as it would start an installed agent — no shell, no interpreter in front of it.
    /// </summary>
    public static string Program => Path.Combine(
        AppContext.BaseDirectory,
        OperatingSystem.IsWindows() ? "Jason.FakeAgentHost.exe" : "Jason.FakeAgentHost");
}

public class ProfileLaunchTests
{
    /// <summary>A runtime with a house default configured, so refusing to use it is a choice and not an absence.</summary>
    private const string DefaultProfileSettings = """
        {"Dispatcher":{"TickSeconds":3600,"RetryDelaySeconds":0,"ExitGraceSeconds":30,"AiRole":{"TimeoutSeconds":60,"HeartbeatSeconds":10,"MaxAttempts":2}},"Roles":{"DefaultExecutionProfile":"house"}}
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The shape of an answer, in the dialect the runtime enforces. Small on purpose: what it proves is that the
    /// runtime holds the answer to it, not that the dialect is expressive.
    /// </summary>
    private static JsonObject Shape() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject { ["summary"] = new JsonObject { ["type"] = "string" } },
        ["required"] = new JsonArray("summary"),
    };

    [Fact]
    public async Task An_item_launched_through_a_profile_succeeds_and_pins_what_ran()
    {
        await using var host = await FakeHostRuntime.StartAsync(Ct);
        await ProfileAsync(host, "local-host", ProfileFixture.Program);
        var campaign = await host.CampaignAsync(Ct);
        await host.RoleAsync("profiled", Ct, "silent");

        var item = await host.CreateAsync(
            new
            {
                campaign_id = campaign,
                kind = "ai_role",
                role = "profiled",
                execution_profile = "local-host",
                context = new { behaviour = "succeed" },
            },
            Ct);

        await host.ScanAsync(Ct);
        var done = await host.WaitForStatusAsync(item.Id, WorkItemStatus.Succeeded, Ct);

        var agent = done.Attempts![0].Provenance!.Agent!;
        Assert.Equal(ProfileResolutionSource.WorkItemOverride, agent.ResolutionSource);
        Assert.Equal("local-host", agent.ProfileName);
        Assert.Equal(1, agent.ProfileRevision);
        Assert.Equal(AgentHostKind.ClaudeCode, agent.Host);
        Assert.Equal(ProfileFixture.Program, agent.Program);
        Assert.True(Guid.TryParse(agent.SessionId, out _));

        // The whole command line as launched, so what ran can be read rather than reconstructed — and the two
        // flags that are deliberately absent are absent here too.
        Assert.Equal(agent.Args, done.Attempts[0].Launch!.EntryCommand);
        Assert.DoesNotContain("--restricted", agent.Args!);
        Assert.DoesNotContain("--tools", agent.Args!);
    }

    /// <summary>
    /// The tools the revision granted are recorded beside it, in the API's words, and the command the attempt
    /// records is the one that carried them in the host's. A profile that granted none records an empty grant, which
    /// says "none" where an attempt from before grants existed says nothing.
    /// </summary>
    [Fact]
    public async Task A_launch_records_the_tools_its_revision_granted_beside_the_revision()
    {
        await using var host = await FakeHostRuntime.StartAsync(Ct);
        await ProfileAsync(host, "researching", ProfileFixture.Program, "web_search", "web_fetch");
        await ProfileAsync(host, "ungranted", ProfileFixture.Program);
        var campaign = await host.CampaignAsync(Ct);
        await host.RoleAsync("profiled", Ct, "silent");

        var granted = await host.CreateAsync(
            new { campaign_id = campaign, kind = "ai_role", role = "profiled", execution_profile = "researching", context = new { behaviour = "succeed" } },
            Ct);
        var other = await host.CampaignAsync(Ct, "Ungranted work");
        var ungranted = await host.CreateAsync(
            new { campaign_id = other, kind = "ai_role", role = "profiled", execution_profile = "ungranted", context = new { behaviour = "succeed" } },
            Ct);

        await host.ScanAsync(Ct);
        var done = await host.WaitForStatusAsync(granted.Id, WorkItemStatus.Succeeded, Ct);
        var plain = await host.WaitForStatusAsync(ungranted.Id, WorkItemStatus.Succeeded, Ct);

        var agent = done.Attempts![0].Provenance!.Agent!;
        Assert.Equal(1, agent.ProfileRevision);
        Assert.Equal([AgentTool.WebSearch, AgentTool.WebFetch], agent.Tools);
        Assert.Contains("WebSearch", agent.Args!);
        Assert.Contains("WebFetch", agent.Args!);

        var (_, body) = await host.Fixture.PostRawAsync(Operations.WorkItemGet, $$"""{"work_item_id":"{{granted.Id}}"}""", Ct);
        var recorded = JsonNode.Parse(body)!["attempts"]![0]!["provenance"]!["agent"]!;
        Assert.Equal(["web_search", "web_fetch"], recorded["tools"]!.AsArray().Select(tool => (string?)tool));

        var none = plain.Attempts![0].Provenance!.Agent!;
        Assert.NotNull(none.Tools);
        Assert.Empty(none.Tools);
        Assert.DoesNotContain("WebSearch", none.Args!);
        Assert.DoesNotContain("WebFetch", none.Args!);
    }

    /// <summary>
    /// A deny that is a pattern travels beside the grant: the command carries the grant in the host's name, and the
    /// work directory the role starts in carries the pattern, where the host decides how much of the grant it leaves.
    /// </summary>
    [Fact]
    public async Task A_launch_carries_the_grant_and_a_pattern_deny_written_beside_it()
    {
        await using var host = await FakeHostRuntime.StartAsync(Ct);
        await host.Fixture.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate,
            new
            {
                name = "beside",
                host = "claude_code",
                program = ProfileFixture.Program,
                deny = new[] { "Write", "WebFetch(domain:example.com)" },
                tools = new[] { "web_fetch" },
                host_version_verified = "0.0.0-stand-in",
            },
            Ct);
        var campaign = await host.CampaignAsync(Ct);
        await host.RoleAsync("profiled", Ct, "silent");
        var item = await host.CreateAsync(
            new { campaign_id = campaign, kind = "ai_role", role = "profiled", execution_profile = "beside", context = new { behaviour = "succeed" } },
            Ct);

        await host.ScanAsync(Ct);
        var done = await host.WaitForStatusAsync(item.Id, WorkItemStatus.Succeeded, Ct);

        var attempt = done.Attempts![0];
        Assert.Contains("WebFetch", attempt.Provenance!.Agent!.Args!);
        var settings = JsonNode.Parse(FakeHostRuntime.ReadShared(
            Path.Combine(host.Paths.AttemptWorkDirectory(item.Id, attempt.Id), ".claude", "settings.json")))!;
        Assert.Equal(
            ["Write", "WebFetch(domain:example.com)", "WebSearch"],
            settings["permissions"]!["deny"]!.AsArray().Select(rule => (string?)rule));
    }

    /// <summary>
    /// A profile that grants no web tool starts its role in a directory that denies both by the host's own names, after
    /// the person's own deny rules, and a tool the person denied already is named once.
    /// </summary>
    [Fact]
    [Critical(Family.S6, "the work directory of a profile with no grant denies both web tools by name")]
    public async Task A_launch_without_a_grant_starts_where_both_web_tools_are_denied()
    {
        await using var host = await FakeHostRuntime.StartAsync(Ct);
        await host.Fixture.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate,
            new
            {
                name = "closed",
                host = "claude_code",
                program = ProfileFixture.Program,
                deny = new[] { "Write", "WebFetch" },
                host_version_verified = "0.0.0-stand-in",
            },
            Ct);
        var campaign = await host.CampaignAsync(Ct);
        await host.RoleAsync("profiled", Ct, "silent");
        var item = await host.CreateAsync(
            new { campaign_id = campaign, kind = "ai_role", role = "profiled", execution_profile = "closed", context = new { behaviour = "succeed" } },
            Ct);

        await host.ScanAsync(Ct);
        var done = await host.WaitForStatusAsync(item.Id, WorkItemStatus.Succeeded, Ct);

        var attempt = done.Attempts![0];
        var settings = JsonNode.Parse(FakeHostRuntime.ReadShared(
            Path.Combine(host.Paths.AttemptWorkDirectory(item.Id, attempt.Id), ".claude", "settings.json")))!;
        Assert.Equal(
            ["Write", "WebFetch", "WebSearch"],
            settings["permissions"]!["deny"]!.AsArray().Select(rule => (string?)rule));
        Assert.DoesNotContain("WebSearch", attempt.Provenance!.Agent!.Args!);
        Assert.DoesNotContain("WebFetch", attempt.Provenance!.Agent!.Args!);
    }

    /// <summary>
    /// An attempt is a record of what happened. A profile edited while the work is still running has to leave it
    /// exactly as it stands, or "this attempt ran revision 1" would mean whatever the profile says today.
    /// </summary>
    [Fact]
    public async Task Editing_a_profile_after_launch_does_not_rewrite_the_attempt()
    {
        await using var host = await FakeHostRuntime.StartAsync(Ct);
        await ProfileAsync(host, "moving-target", ProfileFixture.Program);
        var campaign = await host.CampaignAsync(Ct);
        await host.RoleAsync("profiled", Ct, "silent");

        var item = await host.CreateAsync(
            new
            {
                campaign_id = campaign,
                kind = "ai_role",
                role = "profiled",
                execution_profile = "moving-target",
                context = new { behaviour = "hang" },
            },
            Ct);

        await host.ScanAsync(Ct);
        await host.WaitForStatusAsync(item.Id, WorkItemStatus.Processing, Ct);

        // The edit lands while the child is still running, and appends a second revision.
        var edited = await host.Fixture.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileUpdate,
            new { name = "moving-target", deny = new[] { "Write", "WebFetch" }, tools = new[] { "web_search" } },
            Ct);
        Assert.Equal(2, edited.CurrentRevision);

        var running = await host.GetAsync(item.Id, Ct);
        var agent = running.Attempts![0].Provenance!.Agent!;
        Assert.Equal(1, agent.ProfileRevision);
        Assert.DoesNotContain("WebFetch", string.Join(' ', agent.Args!), StringComparison.Ordinal);
        Assert.DoesNotContain("WebSearch", string.Join(' ', agent.Args!), StringComparison.Ordinal);
        Assert.Empty(agent.Tools!);
    }

    /// <summary>
    /// Installation is not a requirement of the runtime, only of the AI work in it. A machine without the host a
    /// profile names still runs everything else, and says which program it went looking for.
    /// </summary>
    [Fact]
    public async Task A_host_that_is_not_installed_blocks_ai_work_while_other_work_carries_on()
    {
        await using var host = await FakeHostRuntime.StartAsync(Ct);
        await ProfileAsync(host, "absent-host", "a-host-nobody-installed");
        var campaign = await host.CampaignAsync(Ct);
        await host.RoleAsync("profiled", Ct, "silent");
        await host.RoleAsync("deterministic", Ct, "succeed");

        var blocked = await host.CreateAsync(
            new
            {
                campaign_id = campaign,
                kind = "ai_role",
                role = "profiled",
                execution_profile = "absent-host",
                context = new { brief = "needs a host" },
            },
            Ct);

        var other = await host.CampaignAsync(Ct, "Other work");
        var carries = await host.CreateAsync(
            new { campaign_id = other, kind = "ai_role", role = "deterministic", context = new { brief = "runs anyway" } },
            Ct);

        await host.ScanAsync(Ct);

        var failed = await host.WaitForStatusAsync(blocked.Id, WorkItemStatus.Failed, Ct);
        Assert.Equal(AttemptErrors.HostNotAvailable, failed.LastError!.Code);
        Assert.False(failed.LastError.Retriable);
        Assert.Contains("a-host-nobody-installed", failed.LastError.Message, StringComparison.Ordinal);
        Assert.Null(failed.Attempts![0].Launch);

        await host.WaitForStatusAsync(carries.Id, WorkItemStatus.Succeeded, Ct);
    }

    /// <summary>
    /// A launch the machine refused still says what it would have run with: the profile, its revision and the grant,
    /// since "the host was missing" is read beside what the person had allowed it.
    /// </summary>
    [Fact]
    public async Task A_refused_launch_records_the_grant_it_would_have_run_with()
    {
        await using var host = await FakeHostRuntime.StartAsync(Ct);
        await ProfileAsync(host, "absent-researcher", "a-host-nobody-installed", "web_fetch");
        var campaign = await host.CampaignAsync(Ct);
        await host.RoleAsync("profiled", Ct, "silent");

        var item = await host.CreateAsync(
            new { campaign_id = campaign, kind = "ai_role", role = "profiled", execution_profile = "absent-researcher", context = new { brief = "needs a host" } },
            Ct);

        await host.ScanAsync(Ct);

        var failed = await host.WaitForStatusAsync(item.Id, WorkItemStatus.Failed, Ct);
        Assert.Equal(AttemptErrors.HostNotAvailable, failed.LastError!.Code);
        var agent = failed.Attempts![0].Provenance!.Agent!;
        Assert.Equal(1, agent.ProfileRevision);
        Assert.Equal([AgentTool.WebFetch], agent.Tools);
    }

    /// <summary>
    /// Persuasive prose is not a result when a structure was asked for. The host is told so while it still holds
    /// its attempt; this one gives up instead, and the attempt ends the way any executor that stops talking
    /// ends — accountably, with what it said kept.
    /// </summary>
    [Fact]
    public async Task An_answer_without_the_required_shape_ends_the_attempt_accountably()
    {
        await using var host = await FakeHostRuntime.StartAsync(Ct);
        await ProfileAsync(host, "local-host", ProfileFixture.Program);
        var campaign = await host.CampaignAsync(Ct);
        await host.RoleAsync("profiled", Ct, "silent");

        var item = await host.CreateAsync(
            new
            {
                campaign_id = campaign,
                kind = "ai_role",
                role = "profiled",
                execution_profile = "local-host",
                max_attempts = 1,
                result_format = Shape(),
                // One token, because the brief names a behaviour and its options by words: a JSON string rather
                // than the object the shape asks for, which is what prose amounts to here.
                context = new { behaviour = "succeed --result \"a-thorough-look-at-the-question\"" },
            },
            Ct);

        await host.ScanAsync(Ct);
        var failed = await host.WaitForStatusAsync(item.Id, WorkItemStatus.Failed, Ct);

        Assert.Equal(AttemptErrors.ExecutorExited, failed.LastError!.Code);

        // What the host reported is read first, so a checkpoint that was lost is said with the host's own reason.
        var stderr = FakeHostRuntime.StderrOf(host.Paths, failed.Id, failed.Attempts![0]);

        // What it checked in halfway is still there — a checkpoint is not held to the shape of a finished
        // answer — but the prose it tried to finish with never became the result.
        Assert.True(failed.Result is not null, $"The checkpoint the host set halfway was not kept. The host said:\n{stderr}");
        Assert.DoesNotContain("a-thorough-look", failed.Result.ToJsonString(), StringComparison.Ordinal);

        // The refusal reached the child rather than being decided after it: it is in what the host reported.
        Assert.Contains("result_invalid", stderr, StringComparison.Ordinal);
    }

    /// <summary>
    /// The repair the block promises, end to end. Work whose ancestry cannot be read is told to stop; naming a
    /// profile on the item itself is what makes it run, and it runs under the profile that was named.
    /// <para>
    /// The repair is made before the claim, which is the only time it can be made: a refused attempt fails the
    /// item, and a finished item is not changed — the same as every other fail-closed refusal in this runtime,
    /// where the remedy is to repair the configuration and ask for the work again.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Work_whose_ancestry_cannot_be_read_runs_once_a_profile_is_named_on_it()
    {
        await using var host = await FakeHostRuntime.StartAsync(Ct);
        await ProfileAsync(host, "local-host", ProfileFixture.Program);
        var campaign = await host.CampaignAsync(Ct);
        await host.RoleAsync("profiled", Ct, "silent");

        var item = await host.CreateAsync(
            new { campaign_id = campaign, kind = "ai_role", role = "profiled", context = new { behaviour = "succeed" } },
            Ct);

        // The state an upgraded database leaves behind: work a run created, whose run pinned nothing.
        await UnresolveAsync(host, item.Id);

        await host.Fixture.PostOkAsync<WorkItemDto>(
            Operations.WorkItemUpdate,
            new { work_item_id = item.Id, execution_profile = "local-host" },
            Ct);

        await host.ScanAsync(Ct);
        var done = await host.WaitForStatusAsync(item.Id, WorkItemStatus.Succeeded, Ct);

        var agent = done.Attempts![^1].Provenance!.Agent!;
        Assert.Equal(ProfileResolutionSource.WorkItemOverride, agent.ResolutionSource);
        Assert.Equal("local-host", agent.ProfileName);
    }

    /// <summary>
    /// And the same work unrepaired, with a house default configured and willing: it stops rather than running
    /// under an executor nobody chose for it.
    /// </summary>
    [Fact]
    public async Task The_same_work_unrepaired_stops_rather_than_taking_the_house_default()
    {
        await using var host = await FakeHostRuntime.StartAsync(Ct, DefaultProfileSettings);
        await ProfileAsync(host, "house", ProfileFixture.Program);
        var campaign = await host.CampaignAsync(Ct);
        await host.RoleAsync("profiled", Ct, "silent");

        var item = await host.CreateAsync(
            new { campaign_id = campaign, kind = "ai_role", role = "profiled", context = new { behaviour = "succeed" } },
            Ct);
        await UnresolveAsync(host, item.Id);

        await host.ScanAsync(Ct);
        var blocked = await host.WaitForStatusAsync(item.Id, WorkItemStatus.Failed, Ct);

        Assert.Equal(AttemptErrors.LineageResolutionUnsupported, blocked.LastError!.Code);
        Assert.Null(blocked.Attempts![0].Launch);
        Assert.Null(blocked.Attempts[0].Provenance?.Agent);
    }

    private static async Task UnresolveAsync(FakeHostRuntime host, string workItemId)
    {
        await using var scope = host.Fixture.Resolve<IServiceScopeFactory>().CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        var row = await db.WorkItems.SingleAsync(w => w.PublicId == workItemId, Ct);
        row.LineageState = LineageState.Unresolved;
        await db.SaveChangesAsync(Ct);
    }

    private static async Task ProfileAsync(FakeHostRuntime host, string name, string program, params string[] tools) =>
        await host.Fixture.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate,
            new { name, host = "claude_code", program, deny = new[] { "Write" }, tools, host_version_verified = "0.0.0-stand-in" },
            Ct);
}
