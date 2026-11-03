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

    /// <summary>
    /// A tool's rule is the host's own name for it, or null where the host does not have the tool: then a grant of it is
    /// refused where a profile is written, and a deny of it holds anyway. Never a blank name, which no host reads.
    /// </summary>
    [Theory]
    [MemberData(nameof(Kinds))]
    public void A_tools_rule_is_the_hosts_own_name_or_null_where_the_host_does_not_have_it(AgentHostKind kind)
    {
        var host = Registered().Single(candidate => candidate.Kind == kind);

        Assert.All(Enum.GetValues<AgentTool>(), tool => Assert.True(host.ToolRule(tool) is null || !string.IsNullOrWhiteSpace(host.ToolRule(tool))));
    }

    /// <summary>
    /// Claude Code answers every word as it always did: a rule for every tool of the vocabulary, no deny word, grant or
    /// argument refused on the host's account, a session the runtime mints, and nothing allowed through the work
    /// directory, since its allow rules travel on the command line.
    /// </summary>
    [Fact]
    public void Claude_code_has_every_tool_and_refuses_no_word_on_its_own_account()
    {
        var host = Registered().Single(candidate => candidate.Kind == AgentHostKind.ClaudeCode);

        Assert.All(Enum.GetValues<AgentTool>(), tool => Assert.NotNull(host.ToolRule(tool)));
        Assert.All(Enum.GetValues<AgentTool>(), tool => Assert.Null(host.GrantProblem(tool)));
        Assert.All(["WebFetch", "WebSearch", "Bash(rm *)", "Read(//c/notes/*)", "any words at all"], word => Assert.Null(host.DenyProblem(word)));
        Assert.Null(host.ArgsProblem(["--model", "made-up-model", "bare"]));
        Assert.True(host.TakesSessionId);
        Assert.Empty(host.Compose(Revision(AgentHostKind.ClaudeCode), [host.Facts.CommandWord], NoFiles).Allow);
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
    /// A command-line parser expands a word of one dash and several letters into one flag per letter, and reads the text
    /// after a short flag that takes a value as its value, so a reserved short flag written inside such a word is the
    /// flag itself and a check of whole words does not see it. A host that reserves a short flag therefore refuses, in
    /// its own argument rule, the words that carry it: the flag followed by a letter, a letter followed by the flag, and
    /// the flag followed by a value. A host with no reserved short flag has nothing to refuse.
    /// </summary>
    [Theory]
    [MemberData(nameof(Kinds))]
    public void A_reserved_short_flag_is_refused_inside_a_cluster_and_with_a_value_joined_to_it(AgentHostKind kind)
    {
        var host = Registered().Single(candidate => candidate.Kind == kind);
        var shortFlags = host.ReservedFlags.Where(flag => flag.Length == 2 && flag[0] == '-' && flag[1] != '-').ToArray();

        Assert.All(
            shortFlags,
            flag => Assert.All(
                [flag + "y", "-y" + flag[1], flag + "http://127.0.0.1:9"],
                word => Assert.True(host.ArgsProblem([word]) is not null, $"{kind} reserves {flag} and takes {word}")));
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

    /// <summary>
    /// Every attempt is its own session: a fresh, non-empty id where the host takes one from the runtime, and none where
    /// the host mints its own, so the provenance never names a session the runtime did not give.
    /// </summary>
    [Theory]
    [MemberData(nameof(Kinds))]
    public void Every_attempt_is_its_own_session_where_the_host_takes_one(AgentHostKind kind)
    {
        var host = Registered().Single(candidate => candidate.Kind == kind);

        var first = host.Compose(Revision(kind), [host.Facts.CommandWord], NoFiles);
        var second = host.Compose(Revision(kind), [host.Facts.CommandWord], NoFiles);

        if (host.TakesSessionId)
        {
            Assert.False(string.IsNullOrWhiteSpace(first.SessionId));
            Assert.NotEqual(first.SessionId, second.SessionId);
        }
        else
        {
            Assert.Null(first.SessionId);
            Assert.Null(second.SessionId);
        }
    }

    /// <summary>
    /// What a host writes into the work directory is given the directory and the two lists, allow first, and nothing
    /// else: a parameter added here is a door for the envelope or the capability token into a file.
    /// </summary>
    [Fact]
    public void Writing_the_work_directory_is_given_the_directory_the_allow_and_the_deny_and_nothing_else()
    {
        var write = typeof(IAgentHost).GetMethod(nameof(IAgentHost.WriteWorkDirectory))!;

        Assert.Equal(["workDir", "allow", "deny"], write.GetParameters().Select(parameter => parameter.Name));
        Assert.Equal(
            [typeof(string), typeof(IReadOnlyList<string>), typeof(IReadOnlyList<string>)],
            write.GetParameters().Select(parameter => parameter.ParameterType));
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
    /// diagnostic bundle that includes it. So whatever a host makes of its end is a fixed sentence of its own, and no
    /// part of the host's words travels in it: two tails that carry the same signal and differ only in whose they are
    /// get one sentence, and it holds no token of either — no piece where the two differ, and no piece a sentence of
    /// plain words would not hold, such as <c>Error:</c>, an address or an id. Every exit is tried, since which exit a
    /// host reads as its signal is the host's.
    /// </summary>
    [Theory]
    [MemberData(nameof(Kinds))]
    public void What_a_host_says_of_its_end_is_one_fixed_sentence_and_never_a_part_of_its_words(AgentHostKind kind)
    {
        var host = Registered().Single(candidate => candidate.Kind == kind);
        const string One = "Error: not signed in as person@example.test (plan 7f3a9c-made-up); run login to continue";
        const string Other = "Error: not signed in as someone.else@example.org (plan 0b11e2-made-up); run login to continue";
        var tokens = Tokens(One, Other);

        foreach (var exit in new[] { 0, 1, 2, 41, 127, -1 })
        {
            var first = host.Unavailable(exit, One);
            Assert.Equal(first, host.Unavailable(exit, Other));
            if (first is { } sentence)
            {
                Assert.All(tokens, token => Assert.DoesNotContain(token, sentence, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    /// <summary>
    /// The pieces of two tails a fixed sentence may not hold: each piece where the two differ, and each piece of either
    /// that is not a plain lowercase word — a capital, a digit, an address, a bracket or a colon in it.
    /// </summary>
    private static IReadOnlyList<string> Tokens(string one, string other)
    {
        var first = one.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var second = other.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return
        [
            .. first.Except(second, StringComparer.Ordinal),
            .. second.Except(first, StringComparer.Ordinal),
            .. first.Concat(second).Where(piece => !piece.All(char.IsAsciiLetterLower)).Distinct(StringComparer.Ordinal),
        ];
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
