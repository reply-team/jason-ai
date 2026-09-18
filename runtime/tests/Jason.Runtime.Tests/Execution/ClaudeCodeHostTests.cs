using Jason.Contracts.Api;
using Jason.Runtime.Execution.Hosts;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Tests.Execution;

/// <summary>
/// The shape one headless agent session is started with. It is composed rather than configured: the profile says
/// which program and what to add at the end, and everything in between is the runtime's own, so two profiles of
/// the same host cannot disagree about how a session is confined or about how the agent calls home.
/// </summary>
public class ClaudeCodeHostTests
{
    [Fact]
    public void The_command_carries_neither_restricted_nor_tools()
    {
        var launch = new ClaudeCodeHost().Compose(Revision(), "/opt/claude/claude");

        // --restricted makes the per-attempt work directory inert: under it the host reads no project settings
        // file and loads no project skill, so the directory could neither deny anything nor teach the role its
        // job. What the agent may not do is written there instead, which is why the flag is absent.
        Assert.DoesNotContain("--restricted", launch.Command);

        // --tools narrows the session to the tools it names, and a skill is reached through a tool, so naming
        // tools costs the role its skills. What the agent may do is said by the allow rule instead.
        Assert.DoesNotContain("--tools", launch.Command);
    }

    [Fact]
    public void The_session_is_headless_streams_what_it_does_and_may_call_home()
    {
        var launch = new ClaudeCodeHost().Compose(Revision(), "claude");

        string[] expected =
            [
                "claude",
                "-p",
                "--output-format",
                "stream-json",
                "--include-partial-messages",
                "--allowed-tools",
                $"Bash({ProgramResolver.DefaultCliCommand} *)",
                "--permission-mode",
                "dontAsk",
                "--session-id",
                launch.SessionId,
            ];
        Assert.Equal(expected, launch.Command);
    }

    [Fact]
    public void The_profiles_own_arguments_come_last_and_nothing_follows_them()
    {
        var launch = new ClaudeCodeHost().Compose(Revision("--model", "opus"), "claude");

        string[] last = ["--model", "opus"];
        Assert.Equal(last, launch.Command.TakeLast(2));
        Assert.Equal("claude", launch.Command[0]);
    }

    [Fact]
    public void The_allow_rule_names_the_word_the_runtime_puts_within_the_childs_reach()
    {
        var launch = new ClaudeCodeHost().Compose(Revision(), "claude");

        // A test runs under whatever host started it, so the name itself is never the claim. The rule is: the
        // word is a bare command word — no directory in it — and it is the word the allow rule is built from.
        var word = ProgramResolver.DefaultCliCommand;
        Assert.False(word.Contains(Path.DirectorySeparatorChar), $"'{word}' carries a directory separator.");
        Assert.False(word.Contains(Path.AltDirectorySeparatorChar), $"'{word}' carries a directory separator.");
        Assert.False(Path.IsPathRooted(word), $"'{word}' is a path rather than a command word.");
        Assert.Contains($"Bash({word} *)", launch.Command);
    }

    [Fact]
    public void A_profile_that_names_its_own_command_word_is_the_one_in_the_allow_rule()
    {
        var revision = Revision();
        revision.CliCommand = "jason-dev";

        var launch = new ClaudeCodeHost().Compose(revision, "claude");

        Assert.Contains("Bash(jason-dev *)", launch.Command);
        Assert.DoesNotContain($"Bash({ProgramResolver.DefaultCliCommand} *)", launch.Command);
    }

    [Fact]
    public void Every_attempt_gets_a_session_of_its_own_and_it_is_a_uuid()
    {
        var host = new ClaudeCodeHost();
        var revision = Revision();

        var first = host.Compose(revision, "claude");
        var second = host.Compose(revision, "claude");

        Assert.NotEqual(first.SessionId, second.SessionId);
        Assert.True(Guid.TryParseExact(first.SessionId, "D", out _), $"'{first.SessionId}' is not a UUID.");

        // Never the attempt's own id: it is a prefixed ULID, which no host would accept as a session.
        Assert.DoesNotContain("att_", first.SessionId, StringComparison.Ordinal);
    }

    [Fact]
    public void The_session_it_reports_is_the_session_it_told_the_host_to_use()
    {
        var launch = new ClaudeCodeHost().Compose(Revision(), "claude");

        var flag = launch.Command.ToList().IndexOf("--session-id");
        Assert.True(flag >= 0, "the command names no session");
        Assert.Equal(launch.SessionId, launch.Command[flag + 1]);
    }

    [Fact]
    public void It_answers_for_one_host_and_says_which()
    {
        Assert.Equal(AgentHostKind.ClaudeCode, new ClaudeCodeHost().Kind);
    }

    private static ExecutionProfileRevision Revision(params string[] args) => new()
    {
        Number = 1,
        Host = AgentHostKind.ClaudeCode,
        Program = "claude",
        Args = [.. args],
    };
}
