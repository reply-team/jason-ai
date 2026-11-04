using Jason.Contracts.Api;
using Jason.Contracts.Execution;
using Jason.Runtime.Persistence;
using Jason.Runtime.Profiles;
using Jason.Runtime.Execution.Hosts;
using Jason.Runtime.Tests.Dispatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Tests.Profiles;

/// <summary>
/// A revision stored under an older build's wider rules is worked out against this build's rules for its host on every read:
/// <c>profile get</c> and <c>profile list</c> carry what is refused, an update that names the words clears it, and the
/// start says only how many profiles are refused.
/// </summary>
public class ProfileStoredRulesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_clean_profile_carries_no_problems_and_one_stored_under_wider_rules_names_the_field_and_the_code()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var created = await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate, new { name = "older", host = "claude_code", program = "claude", args = new[] { "--model", "sonnet" } }, Ct);
        Assert.Empty(created.Problems);

        // What a build with a narrower reserved set accepted: the flag that makes a session unconfined, as one token.
        await StoreArgsAsync(api, "older", ["--model", "sonnet", "--permission-mode=bypassPermissions"]);

        var got = await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileGet, new { name = "older" }, Ct);
        var problem = Assert.Single(got.Problems);
        Assert.Equal(("args[2]", "reserved"), (problem.Field, problem.Code));
        var listed = await api.PostOkAsync<Page<ExecutionProfileDto>>(Operations.ProfileList, new { }, Ct);
        Assert.Equal(problem, Assert.Single(Assert.Single(listed.Items).Problems));

        // The value is the person's and is never in the answer's problems.
        var raw = await api.PostAsync(Operations.ProfileGet, new { name = "older" }, Ct);
        Assert.Contains("\"problems\":[{\"field\":\"args[2]\",\"code\":\"reserved\"}]", raw.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_update_that_names_the_words_clears_the_problem()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate, new { name = "older", host = "claude_code", program = "claude" }, Ct);
        await StoreArgsAsync(api, "older", ["--permission-mode=bypassPermissions"]);

        var updated = await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileUpdate, new { name = "older", args = new[] { "--model", "sonnet" } }, Ct);

        Assert.Equal(3, updated.CurrentRevision);
        Assert.Empty(updated.Problems);
        Assert.Empty((await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileGet, new { name = "older" }, Ct)).Problems);
    }

    [Fact]
    public async Task The_start_says_how_many_profiles_are_refused_and_never_which()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate, new { name = "private-name-one", host = "claude_code", program = "claude" }, Ct);
        await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate, new { name = "private-name-two", host = "claude_code", program = "claude" }, Ct);
        await StoreArgsAsync(api, "private-name-one", ["--permission-mode=bypassPermissions"]);
        var logs = new RecordingLogs();
        var services = new ServiceCollection();
        services.AddScoped(_ => new JasonDbContext(JasonDbContext.CreateOptions(api.Paths.DatabaseFile)));
        await using var provider = services.BuildServiceProvider();

        await new ProfileRevisionCheck(provider.GetRequiredService<IServiceScopeFactory>(), [new ClaudeCodeHost()], new Logger<ProfileRevisionCheck>(new LoggerFactory([logs])))
            .StartAsync(Ct);

        var line = Assert.Single(logs.Warnings);
        Assert.StartsWith("Execution profiles whose current revision this build's rules for its host refuse: 1;", line, StringComparison.Ordinal);
        Assert.DoesNotContain(logs.Lines, entry => entry.Message.Contains("private-name", StringComparison.Ordinal) || entry.Message.Contains("bypass", StringComparison.Ordinal));
    }

    /// <summary>Disabling or enabling a profile answers with it as a read does, what this build's rules refuse included.</summary>
    [Theory]
    [InlineData(Operations.ProfileDisable)]
    [InlineData(Operations.ProfileEnable)]
    public async Task Disable_and_enable_carry_the_problems_of_a_revision_stored_under_wider_rules(string operation)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate, new { name = "older", host = "claude_code", program = "claude" }, Ct);
        await StoreArgsAsync(api, "older", ["--permission-mode=bypassPermissions"]);
        if (operation == Operations.ProfileEnable)
        {
            await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileDisable, new { name = "older" }, Ct);
        }

        var toggled = await api.PostOkAsync<ExecutionProfileDto>(operation, new { name = "older" }, Ct);

        var problem = Assert.Single(toggled.Problems);
        Assert.Equal(("args[0]", "reserved"), (problem.Field, problem.Code));
    }

    /// <summary>
    /// A revision stored for a host this build does not register is not something to guess about: no host's rules are
    /// read against it, so a read carries no problems, and the start counts nothing for it. Its launch is refused for the
    /// host instead.
    /// </summary>
    [Fact]
    public async Task A_stored_revision_for_a_host_this_build_does_not_register_carries_no_problems()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, configureServices: services =>
        {
            Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.RemoveAll<IAgentHost>(services);
            services.AddSingleton<IAgentHost, ClaudeCodeHost>();
        });
        await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate, new { name = "elsewhere", host = "claude_code", program = "claude" }, Ct);

        // Codex composes --json for every attempt, so under Codex's rules this argument would be refused.
        await StoreArgsAsync(api, "elsewhere", ["--json"], AgentHostKind.Codex);

        var got = await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileGet, new { name = "elsewhere" }, Ct);
        Assert.Equal(AgentHostKind.Codex, got.Revision.Host);
        Assert.Empty(got.Problems);
        Assert.Empty(Assert.Single((await api.PostOkAsync<Page<ExecutionProfileDto>>(Operations.ProfileList, new { }, Ct)).Items).Problems);

        var logs = new RecordingLogs();
        var services = new ServiceCollection();
        services.AddScoped(_ => new JasonDbContext(JasonDbContext.CreateOptions(api.Paths.DatabaseFile)));
        await using var provider = services.BuildServiceProvider();
        await new ProfileRevisionCheck(provider.GetRequiredService<IServiceScopeFactory>(), [new ClaudeCodeHost()], new Logger<ProfileRevisionCheck>(new LoggerFactory([logs])))
            .StartAsync(Ct);
        Assert.Empty(logs.Warnings);
    }

    private static async Task StoreArgsAsync(RuntimeApiFixture api, string name, string[] args, AgentHostKind? host = null)
    {
        await using var db = new JasonDbContext(JasonDbContext.CreateOptions(api.Paths.DatabaseFile));
        var profile = await db.ExecutionProfiles.Include(p => p.Revisions).SingleAsync(p => p.Name == name, Ct);

        // A revision is immutable, so the old build's word arrives as a revision of its own, as a stored one would have.
        var current = profile.Revisions.Single(r => r.Number == profile.CurrentRevision);
        profile.Revisions.Add(new ExecutionProfileRevision
        {
            Number = current.Number + 1,
            Host = host ?? current.Host,
            Program = current.Program,
            Args = [.. args],
            Deny = [.. current.Deny],
            CreatedByType = current.CreatedByType,
            CreatedAt = current.CreatedAt,
        });
        profile.CurrentRevision += 1;
        await db.SaveChangesAsync(Ct);
    }
}
