using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Execution;
using Jason.Contracts.Hosts;
using Jason.Runtime.Execution.Hosts;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Tests.Execution;

/// <summary>
/// The shape one headless Antigravity CLI session is started with, and what Jason can and cannot say about it. Built from
/// the host's documentation for version 1.4.10 and not yet run on an account: the rows prove the command Jason composes,
/// the one line of standard input around the brief, and the refusals that follow from the host taking its permission rules
/// from the person's own settings and from nowhere a run could carry them.
/// </summary>
public class AntigravityCliHostTests
{
    private static readonly MemoryFileLocations NoFiles = new(null, null, null);

    private static readonly AntigravityCliHost Host = new();

    [Fact]
    public void The_host_is_found_by_the_directory_only_it_keeps_and_reads_skills_from_its_own_root()
    {
        var facts = AgentHosts.Of(AgentHostKind.AntigravityCli);

        Assert.Equal("agy", facts.CommandWord);
        Assert.Equal(".gemini/antigravity-cli", facts.HomeMarker);
        Assert.Equal(HomeMarkerKind.Directory, facts.MarkerKind);
        Assert.Equal(".gemini/antigravity-cli/skills", facts.HomeSkills);
        Assert.Equal(".agents/skills", facts.WorkSkills);

        // AGENTS.md and GEMINI.md are both read by the host itself, so the harness folder carries no import file for it.
        Assert.Null(facts.Instructions);

        // The folder Gemini CLI shares with it is not a marker: it exists on every install of either.
        Assert.NotEqual(".gemini", facts.HomeMarker);
    }

    /// <summary>
    /// The brief is on standard input as a stream-json line, which the host reads when it is told so and ends on when
    /// the stream ends; no print flag, so no prompt word; and no limit of the host's own, so the attempt's limit is the
    /// only one and a run is never cut short with an exit that looks like a success.
    /// </summary>
    [Fact]
    public void The_session_reads_a_stream_of_lines_answers_in_one_and_is_limited_by_the_attempt_alone()
    {
        var launch = Host.Compose(Revision(), ["agy"], NoFiles);

        Assert.Equal(
            ["agy", "--input-format", "stream-json", "--output-format", "stream-json", "--print-timeout=0s"],
            launch.Command);
    }

    [Fact]
    public void The_profiles_arguments_come_last_and_nothing_else_is_composed_after_them()
    {
        var launch = Host.Compose(Revision("--model=made-up", "--effort=low"), ["/opt/agy/agy"], NoFiles);

        Assert.Equal("/opt/agy/agy", launch.Command[0]);
        Assert.Equal(["--model=made-up", "--effort=low"], launch.Command.TakeLast(2));
    }

    /// <summary>
    /// What the host would do with the session if it were told: act without asking, widen the workspace, take another
    /// project, resume a conversation, or write a log elsewhere. None is composed and none may be added.
    /// </summary>
    [Fact]
    public void Nothing_that_widens_the_session_is_composed_and_every_such_flag_is_reserved()
    {
        var composed = Host.Compose(Revision(), ["agy"], NoFiles).Command;

        string[] widening =
        [
            "--dangerously-skip-permissions", "--mode", "--sandbox", "--add-dir", "--project", "--new-project",
            "--conversation", "-c", "--continue", "-i", "--prompt-interactive", "-p", "--print", "--prompt",
            "--remote-control", "--log-file", "--agent", "--json-schema",
        ];
        Assert.All(widening, flag =>
        {
            Assert.DoesNotContain(flag, composed);
            Assert.Contains(flag, Host.ReservedFlags);
        });
        Assert.Contains("--print-timeout", Host.ReservedFlags);
        Assert.Contains("--input-format", Host.ReservedFlags);
        Assert.Contains("--output-format", Host.ReservedFlags);

        // The model and the effort are the person's to choose.
        Assert.DoesNotContain("--model", Host.ReservedFlags);
        Assert.DoesNotContain("--effort", Host.ReservedFlags);
    }

    /// <summary>
    /// A word that is not a flag would be read by the host as a prompt in some forms, and its handling beside a
    /// stream of lines is not documented, so an argument is a flag with its value joined by an equals sign, and no
    /// double dash is taken.
    /// </summary>
    [Fact]
    public void An_argument_that_is_a_bare_word_or_a_double_dash_is_refused()
    {
        Assert.Null(Host.ArgsProblem([]));
        Assert.Null(Host.ArgsProblem(["--model=made-up", "--effort=low"]));
        Assert.NotNull(Host.ArgsProblem(["--model", "made-up"]));
        Assert.NotNull(Host.ArgsProblem(["bare"]));
        Assert.NotNull(Host.ArgsProblem(["--"]));
    }

    /// <summary>
    /// A short flag glued to a value or to another short flag is read by a parser as the flags it expands to, which the
    /// reserved list, matched by whole word, cannot see. So every word of one dash and more than one character, and the
    /// bare dash, is refused, whatever the letters; and for each short flag the host reserves, the words that glue a
    /// letter or a value to it are among them.
    /// </summary>
    [Fact]
    public void A_short_flag_glued_to_another_or_to_a_value_and_the_bare_dash_are_refused()
    {
        foreach (var word in new[] { "-pfoo", "-cp", "-ic", "-xy", "-", "-m=made-up", "-1" })
        {
            Assert.NotNull(Host.ArgsProblem([word]));
        }

        Assert.Null(Host.ArgsProblem(["--model=made-up", "--effort=low"]));

        var reservedShort = Host.ReservedFlags.Where(flag => flag.Length == 2 && flag[0] == '-' && flag[1] != '-').ToList();
        Assert.NotEmpty(reservedShort);
        Assert.All(reservedShort, flag =>
        {
            Assert.NotNull(Host.ArgsProblem([flag + "y"]));
            Assert.NotNull(Host.ArgsProblem(["-y" + flag[1]]));
            Assert.NotNull(Host.ArgsProblem([flag + "value"]));
        });
    }

    /// <summary>
    /// The host's parser could not be read: no copy of it was available to inspect, so the reserved list is the flags its
    /// reference table names plus the one its mode page says it accepts for another host's sake, which the host reads as
    /// its own mode flag.
    /// </summary>
    [Fact]
    public void The_permission_mode_flag_the_host_accepts_for_compatibility_is_reserved_beside_its_own()
    {
        Assert.Contains("--permission-mode", Host.ReservedFlags);
        Assert.Contains("--mode", Host.ReservedFlags);
    }

    /// <summary>The host mints the conversation's id itself, so the runtime names no session and none is recorded.</summary>
    [Fact]
    public void The_host_mints_its_own_conversation_so_no_session_is_given_or_recorded()
    {
        Assert.False(Host.TakesSessionId);
        Assert.Null(Host.Compose(Revision(), ["agy"], NoFiles).SessionId);
    }

    /// <summary>
    /// The host takes its permission rules from the person's own settings files under the home folder and from nowhere
    /// a run could carry them, and the runtime never writes that folder: so nothing is allowed or denied through a
    /// launch, and the work directory is left as it was.
    /// </summary>
    [Fact]
    public void No_rule_is_allowed_or_denied_through_the_launch_or_written_into_the_work_directory()
    {
        var launch = Host.Compose(Revision(tools: [AgentTool.WebFetch]), ["agy"], new("/h/AGENTS.md", "/p/AGENTS.md", "/c/AGENTS.md"));
        using var directory = new TempDirectory();

        Host.WriteWorkDirectory(directory.Path, launch.Allow, launch.Deny);

        Assert.Empty(launch.Allow);
        Assert.Empty(launch.Deny);
        Assert.Empty(Directory.GetFileSystemEntries(directory.Path));
    }

    /// <summary>
    /// A memory file outside the work directory is read only where the person's own rule allows it, which Jason cannot
    /// write, so every level the launch names is reported unreadable and the role says it was refused.
    /// </summary>
    [Fact]
    public void Every_memory_file_the_launch_names_is_unreadable_and_a_level_it_does_not_is_not_named()
    {
        Assert.Equal(["client", "project", "campaign"], Host.Compose(Revision(), ["agy"], new("/h/AGENTS.md", "/p/AGENTS.md", "/c/AGENTS.md")).Unreadable);
        Assert.Equal(["campaign"], Host.Compose(Revision(), ["agy"], new(null, null, "/c/AGENTS.md")).Unreadable);
        Assert.Empty(Host.Compose(Revision(), ["agy"], NoFiles).Unreadable);
    }

    [Fact]
    public void A_fetch_has_the_hosts_name_and_a_search_has_none()
    {
        Assert.Equal("read_url(*)", Host.ToolRule(AgentTool.WebFetch));
        Assert.Null(Host.ToolRule(AgentTool.WebSearch));
    }

    /// <summary>
    /// A grant is a rule the host would have to be given, and it can be given none: the person's own settings decide
    /// what the session may do. So a grant of either tool is refused, and the refusal says whose rule it is; the host is named where the profile is written.
    /// </summary>
    [Theory]
    [InlineData(AgentTool.WebFetch)]
    [InlineData(AgentTool.WebSearch)]
    public void A_grant_of_either_tool_is_refused_naming_the_host(AgentTool tool)
    {
        var problem = Host.GrantProblem(tool);

        Assert.NotNull(problem);
        Assert.Contains("settings", problem, StringComparison.Ordinal);
    }

    /// <summary>A deny is a rule too, in a file Jason does not write and a run cannot carry, so every word is refused.</summary>
    [Theory]
    [InlineData("web_fetch")]
    [InlineData("read_url(*)")]
    [InlineData("command(rm)")]
    [InlineData("write_file(.env)")]
    [InlineData("any words at all")]
    public void A_deny_of_any_word_is_refused_naming_the_host(string word)
    {
        var problem = Host.DenyProblem(word);

        Assert.NotNull(problem);
        Assert.Contains("settings", problem, StringComparison.Ordinal);
    }

    /// <summary>
    /// The host says it is not signed in when it has no sign-in or one that expired, in words its pages give. An end that
    /// is not one of those, a model error or a command that failed among them, is an ordinary end.
    /// </summary>
    [Theory]
    [InlineData(1, "Error: authentication required")]
    [InlineData(1, "AUTHENTICATION REQUIRED. Run agy to sign in")]
    [InlineData(1, "Authentication expired. Please log back in.")]
    [InlineData(2, "authentication required")]
    public void A_non_zero_end_that_says_the_sign_in_is_missing_or_expired_is_the_hosts_one_sentence(int exit, string tail)
    {
        Assert.Equal(AntigravityCliHost.NotSignedIn, Host.Unavailable(exit, tail));
    }

    [Theory]
    [InlineData(0, "authentication required")]
    [InlineData(1, "")]
    [InlineData(1, null)]
    [InlineData(3, "AGY_ERROR: the model service answered 500")]
    [InlineData(1, "command failed: git push")]
    public void Any_other_end_says_nothing(int exit, string? tail)
    {
        Assert.Null(Host.Unavailable(exit, tail));
    }

    /// <summary>The sentence is Jason's own: not a byte of the host's words, an address or an id of the person's among them.</summary>
    [Fact]
    public void The_sentence_carries_nothing_the_tail_said()
    {
        var one = Host.Unavailable(1, "authentication required for person@example.test (plan 7f3a9c-made-up)");
        var other = Host.Unavailable(1, "authentication required for someone.else@example.org (plan 0b11e2-made-up)");

        Assert.Equal(one, other);
        Assert.DoesNotContain("@", one, StringComparison.Ordinal);
        Assert.DoesNotContain("example", one, StringComparison.Ordinal);
        Assert.DoesNotContain("7f3a9c", one, StringComparison.Ordinal);
        Assert.Contains("agy", one!, StringComparison.Ordinal);
    }

    /// <summary>
    /// The one line of standard input: a user event of the stream, the brief as its content, the serializer's escapes and
    /// one line feed at its end and nowhere inside it, however the brief is written.
    /// </summary>
    [Fact]
    public void The_brief_is_one_user_event_line_whose_content_is_the_brief_byte_for_byte()
    {
        const string Brief = "{\"a\":\"say \\\"hi\\\"\",\"b\":\"C:\\\\path\\\\x\",\"c\":\"two\nlines\r\nhere\",\"d\":\"\u00fcn\u00ef \u2014 \u65e5\u672c \ud83d\ude00\"}";

        var framed = Host.FrameStdin(Brief);

        Assert.EndsWith("\n", framed, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', framed[..^1]);
        Assert.DoesNotContain('\r', framed);
        using var line = JsonDocument.Parse(framed);
        Assert.Equal("user", line.RootElement.GetProperty("event").GetString());
        Assert.Equal(Brief, line.RootElement.GetProperty("message").GetProperty("content").GetString());
        Assert.Equal(["event", "message"], line.RootElement.EnumerateObject().Select(property => property.Name));
    }

    private static ExecutionProfileRevision Revision(string[]? args = null, List<AgentTool>? tools = null) => new()
    {
        Number = 1,
        Host = AgentHostKind.AntigravityCli,
        Program = "agy",
        Args = [.. args ?? []],
        Tools = tools ?? [],
    };

    private static ExecutionProfileRevision Revision(params string[] args) => Revision(args, null);

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateDirectory(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jason-tests", "agy", Guid.NewGuid().ToString("N"))).FullName;

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // The temporary tree is the machine's to clear.
            }
        }
    }
}
