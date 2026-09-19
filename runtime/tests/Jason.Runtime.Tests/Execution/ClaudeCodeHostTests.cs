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
        var launch = new ClaudeCodeHost().Compose(Revision(), ["/opt/claude/claude"]);

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
        var launch = new ClaudeCodeHost().Compose(Revision(), ["claude"]);

        string[] expected =
            [
                "claude",
                "-p",
                "--output-format",
                "stream-json",
                "--include-partial-messages",
                "--verbose",
                "--allowed-tools",
                $"Bash({ProgramResolver.DefaultCliCommand} *)",
                "--permission-mode",
                "dontAsk",
                "--session-id",
                launch.SessionId,
                "--setting-sources",
                "project",
                "--strict-mcp-config",
            ];
        Assert.Equal(expected, launch.Command);
    }

    /// <summary>
    /// Why each of the three flags that are not about the session itself is there. Verified against Claude Code
    /// 2.1.275; a version that behaves differently is a version this was not checked against.
    /// </summary>
    [Fact]
    public void The_command_says_what_this_version_of_the_host_needs_and_what_it_must_not_inherit()
    {
        var launch = new ClaudeCodeHost().Compose(Revision(), ["claude"]);

        // A print-mode session that reports as it goes is refused outright without this: the host answers
        // "--output-format=stream-json requires --verbose" and exits before a session exists.
        Assert.Contains("--verbose", launch.Command);

        // A launched role is not the person who installed the host. Without these two, the session inherits that
        // person's settings whole — their plugins, their hooks and their MCP servers — none of which the work
        // directory's deny rules reach. With them, the session keeps the built-in tools and the work directory's
        // own skill, which is what the role was given, and nothing else arrives from outside.
        var sources = launch.Command.ToList().IndexOf("--setting-sources");
        Assert.True(sources >= 0, "the command does not say where settings come from");
        Assert.Equal("project", launch.Command[sources + 1]);
        Assert.Contains("--strict-mcp-config", launch.Command);
    }

    /// <summary>
    /// The allow list is one rule, and it is the callback. A rule for the file-editing tools was tried against
    /// 2.1.275 and granted neither writing nor editing, so it is not composed here: an unproven rule on the
    /// command line reads afterwards as a permission the role had.
    /// </summary>
    [Fact]
    public void The_allow_list_is_the_callback_and_nothing_else()
    {
        var launch = new ClaudeCodeHost().Compose(Revision(), ["claude"]);

        var allow = launch.Command.ToList().IndexOf("--allowed-tools");
        Assert.True(allow >= 0, "the command grants nothing");
        Assert.Equal($"Bash({ProgramResolver.DefaultCliCommand} *)", launch.Command[allow + 1]);
        Assert.StartsWith("--", launch.Command[allow + 2], StringComparison.Ordinal);
    }

    [Fact]
    public void The_profiles_own_arguments_come_last_and_nothing_follows_them()
    {
        var launch = new ClaudeCodeHost().Compose(Revision("--model", "opus"), ["claude"]);

        string[] last = ["--model", "opus"];
        Assert.Equal(last, launch.Command.TakeLast(2));
        Assert.Equal("claude", launch.Command[0]);
    }

    [Fact]
    public void The_allow_rule_names_the_word_the_runtime_puts_within_the_childs_reach()
    {
        var launch = new ClaudeCodeHost().Compose(Revision(), ["claude"]);

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

        var launch = new ClaudeCodeHost().Compose(revision, ["claude"]);

        Assert.Contains("Bash(jason-dev *)", launch.Command);
        Assert.DoesNotContain($"Bash({ProgramResolver.DefaultCliCommand} *)", launch.Command);
    }

    [Fact]
    public void Every_attempt_gets_a_session_of_its_own_and_it_is_a_uuid()
    {
        var host = new ClaudeCodeHost();
        var revision = Revision();

        var first = host.Compose(revision, ["claude"]);
        var second = host.Compose(revision, ["claude"]);

        Assert.NotEqual(first.SessionId, second.SessionId);
        Assert.True(Guid.TryParseExact(first.SessionId, "D", out _), $"'{first.SessionId}' is not a UUID.");

        // Never the attempt's own id: it is a prefixed ULID, which no host would accept as a session.
        Assert.DoesNotContain("att_", first.SessionId, StringComparison.Ordinal);
    }

    [Fact]
    public void The_session_it_reports_is_the_session_it_told_the_host_to_use()
    {
        var launch = new ClaudeCodeHost().Compose(Revision(), ["claude"]);

        var flag = launch.Command.ToList().IndexOf("--session-id");
        Assert.True(flag >= 0, "the command names no session");
        Assert.Equal(launch.SessionId, launch.Command[flag + 1]);
    }

    [Fact]
    public void It_answers_for_one_host_and_says_which()
    {
        Assert.Equal(AgentHostKind.ClaudeCode, new ClaudeCodeHost().Kind);
    }

    /// <summary>
    /// The session recorded and the session started are one session. They are written to the attempt from two
    /// places — the provenance takes the id, the launch record takes the whole command — and nothing but this
    /// says they agree, so a future change that minted one and passed another would go unnoticed.
    /// </summary>
    [Fact]
    public void The_session_the_attempt_records_is_the_session_on_the_command_line()
    {
        var launch = new ClaudeCodeHost().Compose(Revision(), ["claude"]);

        var index = launch.Command.ToList().IndexOf("--session-id");
        Assert.True(index >= 0 && index + 1 < launch.Command.Count, "the command names no session");
        Assert.Equal(launch.SessionId, launch.Command[index + 1]);
    }

    /// <summary>
    /// The list a profile is refused for naming, and the shape this host composes, cannot drift apart. Every
    /// flag that ends up on the command line is one a profile may not set — otherwise a profile could append a
    /// second value for it, and a host parser takes the last one it is given. Written as a derivation from the
    /// composed command rather than as a second list, because two lists is how they would come to disagree.
    /// </summary>
    [Fact]
    public void Every_flag_the_runtime_composes_is_one_a_profile_may_not_set()
    {
        var host = new ClaudeCodeHost();
        var launch = host.Compose(Revision(), ["claude"]);

        var composed = launch.Command
            .Skip(1)
            .Where(token => token.StartsWith('-'))
            .ToList();

        Assert.NotEmpty(composed);
        Assert.DoesNotContain(composed, flag => !host.ReservedFlags.Contains(flag));
    }

    /// <summary>
    /// The confinement is only as good as the flags a profile cannot undo. Composing project-only settings and a
    /// strict MCP configuration means nothing if a profile may add a settings file, a plugin directory, another
    /// MCP configuration or a second working directory after them, so each of those is refused as well —
    /// verified against Claude Code 2.1.275, where every one of them widens what a session can reach.
    /// </summary>
    [Theory]
    [InlineData("--verbose")]
    [InlineData("--setting-sources")]
    [InlineData("--strict-mcp-config")]
    [InlineData("--mcp-config")]
    [InlineData("--plugin-dir")]
    [InlineData("--plugin-url")]
    [InlineData("--settings")]
    [InlineData("--add-dir")]
    [InlineData("--disable-slash-commands")]
    public void A_profile_may_not_widen_what_a_launched_session_can_reach(string flag)
    {
        Assert.Contains(flag, new ClaudeCodeHost().ReservedFlags);
    }

    private static ExecutionProfileRevision Revision(params string[] args) => new()
    {
        Number = 1,
        Host = AgentHostKind.ClaudeCode,
        Program = "claude",
        Args = [.. args],
    };
}
