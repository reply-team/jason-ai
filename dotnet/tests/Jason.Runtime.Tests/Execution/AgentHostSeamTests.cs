using System.Net;
using System.Reflection;
using Jason.Contracts.Api;
using Jason.Contracts.Execution;
using Jason.Contracts.Hosts;
using Jason.Runtime.Execution.Hosts;
using Jason.Runtime.Hosting.Modules;
using Jason.Runtime.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Execution;

/// <summary>
/// What every agent host inherits by being one: a value of the enum, a row of the table and a registration, together;
/// a command whose shape the runtime composed and a profile can only add to; an end that never carries the host's own
/// words; and a work directory laid out where that host reads it. A host added later passes these rows or does not
/// build.
/// </summary>
public class AgentHostSeamTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly MemoryFileLocations NoFiles = new(null, null, null);

    /// <summary>Every registration the runtime's composition makes, one instance each.</summary>
    private static IReadOnlyList<IAgentHost> Registered()
    {
        var services = new ServiceCollection();
        services.AddDispatcherModule();
        using var provider = services.BuildServiceProvider();
        return [.. provider.GetServices<IAgentHost>()];
    }

    public static TheoryData<AgentHostKind> Kinds() => [.. Enum.GetValues<AgentHostKind>()];

    [Fact]
    [Critical(Family.S5, "a host value without its row or its registration is the first thing a new host gets wrong")]
    public void The_table_the_enum_and_the_registrations_name_the_same_hosts_once_each()
    {
        var registered = Registered().Select(host => host.Kind).Order().ToArray();

        Assert.Equal(Enum.GetValues<AgentHostKind>().Order(), registered);
        Assert.Equal(Enum.GetValues<AgentHostKind>().Order(), AgentHosts.All.Select(facts => facts.Kind).Order());
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void A_hosts_facts_are_its_row_of_the_table(AgentHostKind kind)
    {
        var host = Registered().Single(candidate => candidate.Kind == kind);

        Assert.Same(AgentHosts.Of(kind), host.Facts);
    }

    [Fact]
    public async Task A_profile_naming_no_host_this_build_knows_is_refused_with_every_word_it_does_know()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            Operations.ProfileCreate, new { name = "unknown-host", host = "not_a_host", program = "agent" }, HttpStatusCode.BadRequest, Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal("host", detail.Field);
        Assert.All(Enum.GetValues<AgentHostKind>(), kind => Assert.Contains(AgentHosts.Word(kind), detail.Message, StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Every_tool_of_the_vocabulary_has_a_rule_in_the_hosts_own_name(AgentHostKind kind)
    {
        var host = Registered().Single(candidate => candidate.Kind == kind);

        Assert.All(Enum.GetValues<AgentTool>(), tool => Assert.False(string.IsNullOrWhiteSpace(host.ToolRule(tool))));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void The_command_starts_with_the_launch_and_ends_with_the_profiles_arguments(AgentHostKind kind)
    {
        var host = Registered().Single(candidate => candidate.Kind == kind);
        string[] launch = ["/opt/hosts/node", "/opt/hosts/entry.js"];
        string[] args = ["--model", "made-up-model"];

        var composed = host.Compose(Revision(kind, args), launch, NoFiles).Command;

        Assert.Equal(launch, composed.Take(launch.Length));
        Assert.Equal(args, composed.TakeLast(args.Length));
    }

    /// <summary>
    /// A flag the runtime composes is one a profile may not carry: a host's parser takes the last value it is given, so
    /// a flag of the shape left unreserved is one a profile's arguments could quietly replace.
    /// </summary>
    [Theory]
    [MemberData(nameof(Kinds))]
    public void Every_flag_the_runtime_composes_is_reserved(AgentHostKind kind)
    {
        var host = Registered().Single(candidate => candidate.Kind == kind);
        string[] launch = [host.Facts.CommandWord];

        var composed = host.Compose(Revision(kind), launch, NoFiles).Command;

        var flags = composed.Skip(launch.Length).Where(word => word.StartsWith('-')).Select(word => word.Split('=')[0]);
        Assert.All(flags, flag => Assert.Contains(flag, host.ReservedFlags));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Every_attempt_is_its_own_session(AgentHostKind kind)
    {
        var host = Registered().Single(candidate => candidate.Kind == kind);

        var first = host.Compose(Revision(kind), [host.Facts.CommandWord], NoFiles);
        var second = host.Compose(Revision(kind), [host.Facts.CommandWord], NoFiles);

        Assert.False(string.IsNullOrWhiteSpace(first.SessionId));
        Assert.NotEqual(first.SessionId, second.SessionId);
    }

    /// <summary>
    /// The envelope cannot reach a command line because composing one is never handed it: what a host composes from is
    /// the frozen revision, how the program starts, and where the memory files are. A parameter added here is a door
    /// for the brief, the context or the capability token, and is decided here rather than slipped in.
    /// </summary>
    [Fact]
    public void Composing_a_command_is_given_the_revision_the_launch_and_the_memory_files_and_nothing_else()
    {
        var compose = typeof(IAgentHost).GetMethod(nameof(IAgentHost.Compose))!;

        Assert.Equal(
            [typeof(ExecutionProfileRevision), typeof(IReadOnlyList<string>), typeof(MemoryFileLocations)],
            compose.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal(typeof(HostLaunch), compose.ReturnType);
    }

    /// <summary>
    /// A host's stderr can carry the person's login or address, and an attempt's error reaches the journal and a
    /// diagnostic bundle that includes it. So whatever a host makes of its end is a sentence of its own, and none of
    /// the host's words travels in it.
    /// </summary>
    [Theory]
    [MemberData(nameof(Kinds))]
    public void What_a_host_says_of_its_end_never_carries_its_own_words(AgentHostKind kind)
    {
        var host = Registered().Single(candidate => candidate.Kind == kind);
        const string Said = "Error: not signed in as person@example.test (plan 7f3a9c-made-up); run login to continue";

        foreach (var exit in new[] { 0, 1, 2, 41, 127, -1 })
        {
            if (host.Unavailable(exit, Said) is { } sentence)
            {
                Assert.DoesNotContain("example.test", sentence, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("7f3a9c", sentence, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("person@", sentence, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void Claude_code_says_nothing_of_its_end()
    {
        var host = Registered().Single(candidate => candidate.Kind == AgentHostKind.ClaudeCode);

        Assert.Null(host.Unavailable(1, "Invalid API key · Please run /login"));
    }

    private static ExecutionProfileRevision Revision(AgentHostKind kind, params string[] args) => new()
    {
        Number = 1,
        Host = kind,
        Program = AgentHosts.Of(kind).CommandWord,
        Args = [.. args],
    };
}
