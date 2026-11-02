using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Hosts;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Integration;

/// <summary>
/// Every host, launched as a person's profile launches it: the profile names the host and its bare command word, the
/// runtime finds the program on its search path, starts it with the command that host composes, and lays the work
/// directory out where that host reads it. The program is the stand-in under the host's word, so nothing here reaches a
/// model or an account; what is proved is everything up to the host's own work.
/// </summary>
public class HostOnPathTests
{
    private const string Role = "host-researcher";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<AgentHostKind> Kinds() => [.. Enum.GetValues<AgentHostKind>()];

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task A_profile_naming_a_host_by_its_word_runs_it_and_the_role_finds_its_skill_where_that_host_reads(AgentHostKind kind)
    {
        using var stub = StubHost.OnPath(kind);
        await using var host = await FakeHostRuntime.StartAsync(
            Ct,
            prepare: paths =>
            {
                var skill = Directory.CreateDirectory(Path.Combine(paths.RoleSkillsDirectory, Role)).FullName;
                File.WriteAllText(Path.Combine(skill, "SKILL.md"), $"---\nname: {Role}\ndescription: finds the decision maker\n---\n\nAsk the runtime for the brief.\n");
            },
            configureServices: services => services.AddSingleton(stub.SearchPath));
        var facts = AgentHosts.Of(kind);
        await host.Fixture.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate,
            new { name = "on-path", host = AgentHosts.Word(kind), program = facts.CommandWord, host_version_verified = "0.0.0-stand-in" },
            Ct);
        var campaign = await host.CampaignAsync(Ct);
        await host.RoleAsync(Role, Ct, "silent");

        var item = await host.CreateAsync(
            new { campaign_id = campaign, kind = "ai_role", role = Role, execution_profile = "on-path", context = new { behaviour = "researcher" } },
            Ct);
        await host.ScanAsync(Ct);
        var done = await host.WaitForStatusAsync(item.Id, WorkItemStatus.Succeeded, Ct);

        var attempt = done.Attempts![0];
        var agent = attempt.Provenance!.Agent!;
        Assert.Equal(kind, agent.Host);
        Assert.Equal(stub.Program, agent.Program, ignoreCase: OperatingSystem.IsWindows());
        Assert.Equal(Role, (string?)done.Result!["taught_by"]);
        Assert.True(File.Exists(Path.Combine(AgentHosts.Under(attempt.Launch!.WorkDir, facts.WorkSkills), Role, "SKILL.md")));
    }
}
