using Jason.Contracts.Api;
using Jason.Contracts.Execution;
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
        var launch = new ClaudeCodeHost().Compose(Revision(), ["/opt/claude/claude"], NoFiles);

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
        var launch = new ClaudeCodeHost().Compose(Revision(), ["claude"], NoFiles);

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
                $"PowerShell({ProgramResolver.DefaultCliCommand} *)",
                "--permission-mode",
                "dontAsk",
                "--session-id",
                launch.SessionId!,
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
        var launch = new ClaudeCodeHost().Compose(Revision(), ["claude"], NoFiles);

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
    /// Where a launch names no memory file, the allow list is the callback and nothing else — through either shell
    /// tool the host offers, since on Windows a role may reach for PowerShell rather than Bash and a callback refused
    /// there fails the attempt. A rule for the file-editing tools was tried against 2.1.275 and granted neither
    /// writing nor editing, so it is not composed here: an unproven rule on the command line reads afterwards as a
    /// permission the role had.
    /// </summary>
    [Fact]
    [Critical(Family.S6, "the launched session's allow list")]
    public void The_allow_list_is_the_callback_through_either_shell_and_nothing_else()
    {
        var launch = new ClaudeCodeHost().Compose(Revision(), ["claude"], NoFiles);

        string[] expected = [$"Bash({ProgramResolver.DefaultCliCommand} *)", $"PowerShell({ProgramResolver.DefaultCliCommand} *)"];
        Assert.Equal(expected, AllowList(launch));
    }

    /// <summary>
    /// Each memory file the launch names is readable by one rule of its own, in the host's form for "this file, by its
    /// absolute path": the drive letter lower-cased and every separator a forward slash, as measured against Claude
    /// Code 2.1.309 on Windows, where the role read the file by the path the envelope gave it, drive letter upper-cased.
    /// A rule names a file and never a folder, so the file beside it and another campaign's file of the same name stay
    /// refused.
    /// </summary>
    [Fact]
    [Critical(Family.S6, "the read rules, exactly: a rule for a folder or a pattern beside the files' own turns this red")]
    public void Each_memory_file_the_launch_names_is_readable_by_a_rule_of_its_own()
    {
        var files = new MemoryFileLocations(
            @"C:\Users\Marta\Jason\AGENTS.md",
            @"C:\Users\Marta\Jason\projects\acme-corp\AGENTS.md",
            @"C:\Users\Marta\Jason\projects\acme-corp\campaigns\q3-marketing-leads\AGENTS.md");

        var launch = new ClaudeCodeHost(windows: true).Compose(Revision(), ["claude"], files);

        string[] expected =
            [
                $"Bash({ProgramResolver.DefaultCliCommand} *)",
                $"PowerShell({ProgramResolver.DefaultCliCommand} *)",
                "Read(//c/Users/Marta/Jason/AGENTS.md)",
                "Read(//c/Users/Marta/Jason/projects/acme-corp/AGENTS.md)",
                "Read(//c/Users/Marta/Jason/projects/acme-corp/campaigns/q3-marketing-leads/AGENTS.md)",
            ];
        Assert.Equal(expected, AllowList(launch));
    }

    /// <summary>
    /// The tools a person granted on the profile join the allow list in the host's own names — the bare words
    /// <c>WebSearch</c> and <c>WebFetch</c> — after the callback and the reads, which they never displace. Run against
    /// Claude Code 2.1.310 on Windows, in a work directory prepared as the runtime prepares one: with the two words both
    /// tools ran, and without them the mode refused both, but for a fetch of a page the host lets through on its own.
    /// </summary>
    [Fact]
    [Critical(Family.S6, "the granted tools join the allow list in the host's names, after the callback and the reads")]
    public void The_tools_a_profile_grants_join_the_allow_list_in_the_hosts_own_names()
    {
        var files = new MemoryFileLocations(@"C:\Users\Marta\Jason\AGENTS.md", null, null);
        var revision = Revision();
        revision.Tools = [AgentTool.WebSearch, AgentTool.WebFetch];

        var launch = new ClaudeCodeHost(windows: true).Compose(revision, ["claude"], files);

        string[] expected =
            [
                $"Bash({ProgramResolver.DefaultCliCommand} *)",
                $"PowerShell({ProgramResolver.DefaultCliCommand} *)",
                "Read(//c/Users/Marta/Jason/AGENTS.md)",
                "WebSearch",
                "WebFetch",
            ];
        Assert.Equal(expected, AllowList(launch));
    }

    /// <summary>
    /// The grant is closed by default: a web tool the revision does not grant is denied by the host's own bare name for
    /// it, so the host does not offer it at all — without the deny it fetched pages of its own documentation list
    /// unasked — and the allow list names neither.
    /// </summary>
    [Fact]
    [Critical(Family.S6, "a web tool the revision does not grant is denied by name, so the host never offers it")]
    public void A_profile_that_grants_no_web_tool_denies_both_by_the_hosts_own_names()
    {
        var launch = new ClaudeCodeHost().Compose(Revision(), ["claude"], NoFiles);

        Assert.Equal(["WebSearch", "WebFetch"], launch.Deny);
        Assert.DoesNotContain("WebSearch", AllowList(launch));
        Assert.DoesNotContain("WebFetch", AllowList(launch));
    }

    /// <summary>One grant is that tool's allow and the other's deny: the two lists never name the same tool.</summary>
    [Theory]
    [InlineData(AgentTool.WebSearch, "WebSearch", "WebFetch")]
    [InlineData(AgentTool.WebFetch, "WebFetch", "WebSearch")]
    public void One_grant_is_its_allow_and_the_other_tools_deny(AgentTool tool, string allowed, string denied)
    {
        var revision = Revision();
        revision.Tools = [tool];

        var launch = new ClaudeCodeHost().Compose(revision, ["claude"], NoFiles);

        Assert.Equal(allowed, AllowList(launch)[^1]);
        Assert.Equal([denied], launch.Deny);
        Assert.Empty(AllowList(launch).Intersect(launch.Deny, StringComparer.Ordinal));
    }

    /// <summary>A revision that grants both tools leaves nothing for the host to deny on its account.</summary>
    [Fact]
    public void A_revision_that_grants_both_tools_composes_no_deny()
    {
        var revision = Revision();
        revision.Tools = [AgentTool.WebSearch, AgentTool.WebFetch];

        var launch = new ClaudeCodeHost().Compose(revision, ["claude"], NoFiles);

        Assert.Empty(launch.Deny);
    }

    /// <summary>Each tool is granted alone: naming one never brings the other with it.</summary>
    [Theory]
    [InlineData(AgentTool.WebSearch, "WebSearch")]
    [InlineData(AgentTool.WebFetch, "WebFetch")]
    public void One_granted_tool_is_one_rule(AgentTool tool, string rule)
    {
        var revision = Revision();
        revision.Tools = [tool];

        var launch = new ClaudeCodeHost().Compose(revision, ["claude"], NoFiles);

        Assert.Equal(rule, AllowList(launch)[^1]);
        Assert.Equal(3, AllowList(launch).Count);
    }

    /// <summary>
    /// A grant is part of the allow list the runtime composes, never a second one: the flag appears once, and the
    /// profile's own arguments still come last with nothing after them.
    /// </summary>
    [Fact]
    public void A_grant_adds_to_the_one_allow_list_and_the_profiles_arguments_still_come_last()
    {
        var revision = Revision("--model", "sonnet");
        revision.Tools = [AgentTool.WebFetch];

        var launch = new ClaudeCodeHost().Compose(revision, ["claude"], SomeFiles);

        Assert.Contains("WebFetch", AllowList(launch));
        Assert.Equal(["--model", "sonnet"], launch.Command.TakeLast(2));
    }

    /// <summary>
    /// Off Windows the form is the host's documented one for an absolute path, two slashes and then the path below the
    /// root. Measured on Windows only: on Linux and macOS this is the host's documentation, not a run.
    /// </summary>
    [Fact]
    public void Off_windows_a_memory_file_is_named_from_the_root_of_the_filesystem()
    {
        var files = new MemoryFileLocations("/home/marta/Jason/AGENTS.md", null, "/home/marta/Jason/projects/acme-corp/campaigns/q3/AGENTS.md");

        var launch = new ClaudeCodeHost(windows: false).Compose(Revision(), ["claude"], files);

        string[] expected = ["Read(//home/marta/Jason/AGENTS.md)", "Read(//home/marta/Jason/projects/acme-corp/campaigns/q3/AGENTS.md)"];
        Assert.Equal(expected, AllowList(launch).Skip(2));
    }

    /// <summary>
    /// A level the launch does not know is null, and a null level is no rule at all — never a rule for a folder, a
    /// pattern or the root's own file standing in for it.
    /// </summary>
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    public void A_level_the_launch_does_not_know_gets_no_rule(bool client, bool project, bool campaign)
    {
        var files = new MemoryFileLocations(
            client ? @"C:\Users\Marta\Jason\AGENTS.md" : null,
            project ? @"C:\Users\Marta\Jason\projects\acme-corp\AGENTS.md" : null,
            campaign ? @"C:\Users\Marta\Jason\projects\acme-corp\campaigns\q3\AGENTS.md" : null);

        var reads = AllowList(new ClaudeCodeHost(windows: true).Compose(Revision(), ["claude"], files)).Skip(2).ToList();

        var named = new[] { client, project, campaign }.Count(known => known);
        Assert.True(reads.Count == named, $"{named} known levels gave the rules [{string.Join(", ", reads)}].");
        Assert.All(reads, rule => Assert.EndsWith("/AGENTS.md)", rule, StringComparison.Ordinal));
        Assert.DoesNotContain(reads, rule => rule.Contains('*', StringComparison.Ordinal));
    }

    /// <summary>
    /// A space or a comma in a path is part of the one value it travels in, inside the rule's parentheses — the host reads
    /// its allow list as "comma or space-separated", and measured against 2.1.309 on Windows it splits neither inside a
    /// rule — so such a path gets its rule like any other.
    /// </summary>
    [Theory]
    [InlineData(true, @"C:\Users\Marta Lee\Jason\AGENTS.md", "Read(//c/Users/Marta Lee/Jason/AGENTS.md)")]
    [InlineData(true, @"C:\Users\Marta\Acme, Inc\Jason\AGENTS.md", "Read(//c/Users/Marta/Acme, Inc/Jason/AGENTS.md)")]
    [InlineData(false, "/home/marta lee/Jason/AGENTS.md", "Read(//home/marta lee/Jason/AGENTS.md)")]
    [InlineData(false, "/home/marta/acme, inc/Jason/AGENTS.md", "Read(//home/marta/acme, inc/Jason/AGENTS.md)")]
    public void A_path_with_a_space_or_a_comma_is_one_rule(bool windows, string path, string rule)
    {
        var launch = new ClaudeCodeHost(windows).Compose(Revision(), ["claude"], new MemoryFileLocations(path, null, null));

        Assert.Equal([rule], AllowList(launch).Skip(2));
    }

    /// <summary>
    /// The drive letter is lower-cased whichever way the path spells it, so a root recorded as <c>c:\</c> and one
    /// recorded as <c>C:\</c> are one rule — the form the host's documentation writes and the one measured.
    /// </summary>
    [Theory]
    [InlineData(@"C:\Users\Marta\Jason\AGENTS.md")]
    [InlineData(@"c:\Users\Marta\Jason\AGENTS.md")]
    [InlineData("C:/Users/Marta/Jason/AGENTS.md")]
    public void The_drive_letter_is_lower_cased_and_every_separator_is_a_forward_slash(string path)
    {
        var launch = new ClaudeCodeHost(windows: true).Compose(Revision(), ["claude"], new MemoryFileLocations(path, null, null));

        Assert.Equal(["Read(//c/Users/Marta/Jason/AGENTS.md)"], AllowList(launch).Skip(2));
    }

    /// <summary>
    /// The host does not escape a rule written by hand, so a path holding a character its patterns read — a star, a
    /// question mark, a bracket, and off Windows the backslash that escapes them — would be a pattern rather than one
    /// file, and an escaped guess could grant more than the file. A parenthesis is the other kind: the host splits its
    /// allow list at a space or a comma that follows a closing one, so a folder named <c>Acme (EU) Ltd</c> would be read
    /// as a rule for part of its name and a word that names no tool. Such a path gets no rule: the envelope still names
    /// it, the role's read is refused, and the role reports the refusal. So does a path the host's form does not
    /// cover: on Windows one that does not start at a drive letter, anywhere one that is not absolute.
    /// </summary>
    [Theory]
    [InlineData(true, @"C:\Users\Marta\[Work]\Jason\AGENTS.md")]
    [InlineData(true, @"C:\Users\Marta\Work]\Jason\AGENTS.md")]
    [InlineData(true, @"C:\Users\Marta\Wo*rk\Jason\AGENTS.md")]
    [InlineData(true, @"C:\Users\Marta\Wo?rk\Jason\AGENTS.md")]
    [InlineData(true, @"C:\Clients\Acme (EU) Ltd\AGENTS.md")]
    [InlineData(true, @"C:\Clients\Smith (2025), archive\AGENTS.md")]
    [InlineData(true, @"C:\Users\Marta Lee (Work)\Jason\AGENTS.md")]
    [InlineData(true, @"C:\Users\Marta\Documents\) notes\AGENTS.md")]
    [InlineData(true, @"C:\Users\Marta\Documents\(notes\AGENTS.md")]
    [InlineData(true, @"\\fileserver\share\Jason\AGENTS.md")]
    [InlineData(true, @"\\?\C:\Users\Marta\Jason\AGENTS.md")]
    [InlineData(true, @"Jason\AGENTS.md")]
    [InlineData(true, @"C:Jason\AGENTS.md")]
    [InlineData(false, "/home/marta/[work]/Jason/AGENTS.md")]
    [InlineData(false, "/home/marta/wo*rk/Jason/AGENTS.md")]
    [InlineData(false, "/home/marta/wo?rk/Jason/AGENTS.md")]
    [InlineData(false, @"/home/marta/wo\rk/Jason/AGENTS.md")]
    [InlineData(false, "/home/marta/acme (eu) ltd/Jason/AGENTS.md")]
    [InlineData(false, "/home/marta/) notes/Jason/AGENTS.md")]
    [InlineData(false, "home/marta/Jason/AGENTS.md")]
    public void A_path_the_host_would_read_as_a_pattern_or_cannot_name_gets_no_rule(bool windows, string path)
    {
        var plain = windows ? @"C:\Users\Marta\Jason\projects\acme-corp\AGENTS.md" : "/home/marta/Jason/projects/acme-corp/AGENTS.md";

        var launch = new ClaudeCodeHost(windows).Compose(Revision(), ["claude"], new MemoryFileLocations(path, plain, null));

        var reads = AllowList(launch).Skip(2).ToList();
        Assert.True(reads.Count == 1, $"'{path}' and '{plain}' gave the rules [{string.Join(", ", reads)}].");
        Assert.EndsWith("/projects/acme-corp/AGENTS.md)", reads[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// A folder whose name starts with a closing parenthesis and a space is where the host's splitting would have done
    /// harm rather than nothing: <c>Read(//c/Users/Marta/Documents/) notes/AGENTS.md)</c> is read by the host as a rule
    /// for the whole of <c>Documents</c>, the folder above, which always exists. So nothing is composed for it — no rule,
    /// and no piece of its path anywhere on the command line — and the level is reported as one the role cannot read.
    /// </summary>
    [Fact]
    [Critical(Family.S6, "a folder that cannot be a rule's path composes nothing")]
    public void A_folder_that_opens_with_a_closing_parenthesis_composes_nothing()
    {
        var files = new MemoryFileLocations(@"C:\Users\Marta\Documents\) notes\AGENTS.md", null, null);

        var launch = new ClaudeCodeHost(windows: true).Compose(Revision(), ["claude"], files);

        string[] callbacks = [$"Bash({ProgramResolver.DefaultCliCommand} *)", $"PowerShell({ProgramResolver.DefaultCliCommand} *)"];
        Assert.Equal(callbacks, AllowList(launch));
        Assert.DoesNotContain(launch.Command, token => token.Contains("Documents", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(["client"], launch.Unreadable);
    }

    /// <summary>
    /// A file the launch named and the host could not write as a rule is reported by its level, so the claim can say
    /// which of the person's files the role will not be able to read without saying where it is. A level that was null
    /// named nothing and is not reported; a file that got its rule is not either.
    /// </summary>
    [Fact]
    public void A_named_file_that_gets_no_rule_is_reported_by_its_level()
    {
        var files = new MemoryFileLocations(
            @"C:\Users\Marta\[Work]\Jason\AGENTS.md",
            null,
            @"C:\Users\Marta\[Work]\Jason\projects\acme-corp\campaigns\q3\AGENTS.md");

        var launch = new ClaudeCodeHost(windows: true).Compose(Revision(), ["claude"], files);

        Assert.Equal(["client", "campaign"], launch.Unreadable);
        Assert.Empty(new ClaudeCodeHost().Compose(Revision(), ["claude"], SomeFiles).Unreadable);
        Assert.Empty(new ClaudeCodeHost().Compose(Revision(), ["claude"], NoFiles).Unreadable);
    }

    /// <summary>The same file named at two levels is read by one rule: a rule per file, not per level.</summary>
    [Fact]
    public void One_file_named_twice_is_one_rule()
    {
        const string path = @"C:\Users\Marta\Jason\AGENTS.md";

        var launch = new ClaudeCodeHost(windows: true).Compose(Revision(), ["claude"], new MemoryFileLocations(path, path, null));

        Assert.Equal(["Read(//c/Users/Marta/Jason/AGENTS.md)"], AllowList(launch).Skip(2));
    }

    [Fact]
    public void The_profiles_own_arguments_come_last_and_nothing_follows_them()
    {
        var launch = new ClaudeCodeHost().Compose(Revision("--model", "opus"), ["claude"], SomeFiles);

        string[] last = ["--model", "opus"];
        Assert.Equal(last, launch.Command.TakeLast(2));
        Assert.Equal("claude", launch.Command[0]);
    }

    [Fact]
    public void The_allow_rule_names_the_word_the_runtime_puts_within_the_childs_reach()
    {
        var launch = new ClaudeCodeHost().Compose(Revision(), ["claude"], NoFiles);

        // A test runs under whatever host started it, so the name itself is never the claim. The rule is: the
        // word is a bare command word — no directory in it — and it is the word the allow rule is built from.
        var word = ProgramResolver.DefaultCliCommand;
        Assert.False(word.Contains(Path.DirectorySeparatorChar), $"'{word}' carries a directory separator.");
        Assert.False(word.Contains(Path.AltDirectorySeparatorChar), $"'{word}' carries a directory separator.");
        Assert.False(Path.IsPathRooted(word), $"'{word}' is a path rather than a command word.");
        Assert.Contains($"Bash({word} *)", launch.Command);
        Assert.Contains($"PowerShell({word} *)", launch.Command);
    }

    [Fact]
    public void A_profile_that_names_its_own_command_word_is_the_one_in_the_allow_rule()
    {
        var revision = Revision();
        revision.CliCommand = "jason-dev";

        var launch = new ClaudeCodeHost().Compose(revision, ["claude"], NoFiles);

        Assert.Contains("Bash(jason-dev *)", launch.Command);
        Assert.Contains("PowerShell(jason-dev *)", launch.Command);
        Assert.DoesNotContain($"Bash({ProgramResolver.DefaultCliCommand} *)", launch.Command);
        Assert.DoesNotContain($"PowerShell({ProgramResolver.DefaultCliCommand} *)", launch.Command);
    }

    [Fact]
    public void Every_attempt_gets_a_session_of_its_own_and_it_is_a_uuid()
    {
        var host = new ClaudeCodeHost();
        var revision = Revision();

        var first = host.Compose(revision, ["claude"], NoFiles);
        var second = host.Compose(revision, ["claude"], NoFiles);

        Assert.NotEqual(first.SessionId, second.SessionId);
        Assert.True(Guid.TryParseExact(first.SessionId, "D", out _), $"'{first.SessionId}' is not a UUID.");

        // Never the attempt's own id: it is a prefixed ULID, which no host would accept as a session.
        Assert.DoesNotContain("att_", first.SessionId, StringComparison.Ordinal);
    }

    [Fact]
    public void The_session_it_reports_is_the_session_it_told_the_host_to_use()
    {
        var launch = new ClaudeCodeHost().Compose(Revision(), ["claude"], NoFiles);

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
        var launch = new ClaudeCodeHost().Compose(Revision(), ["claude"], NoFiles);

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
        var launch = host.Compose(Revision(), ["claude"], SomeFiles);

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
    /// verified against Claude Code 2.1.275, where every one of them widens what a session can reach. The host's own
    /// deny flag is refused beside them: a deny carried in the arguments would take away a grant where the check made
    /// when a profile is written never sees it, so the profile's <c>deny</c> is the one way a deny arrives.
    /// </summary>
    [Theory]
    // Critical: a profile cannot widen the agent's permissions.
    [InlineData("--verbose", Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.S6)])]
    [InlineData("--setting-sources")]
    [InlineData("--strict-mcp-config")]
    [InlineData("--mcp-config")]
    [InlineData("--plugin-dir")]
    [InlineData("--plugin-url")]
    [InlineData("--settings")]
    [InlineData("--add-dir")]
    [InlineData("--disable-slash-commands")]
    [InlineData("--disallowed-tools")]
    [InlineData("--disallowedTools")]
    public void A_profile_may_not_widen_what_a_launched_session_can_reach(string flag)
    {
        Assert.Contains(flag, new ClaudeCodeHost().ReservedFlags);
    }

    /// <summary>
    /// A session served for attaching reads its launch folder's settings without <c>claudeMdExcludes</c> — Claude Code
    /// 2.1.309 drops that key, among others, from a launch folder's settings in that mode — so a profile that added the
    /// flag would bring every <c>CLAUDE.md</c> back into a launched role without a word. It is refused like the others.
    /// </summary>
    [Fact]
    public void A_profile_may_not_serve_the_session_for_attaching()
    {
        Assert.Contains("--attach-serve", new ClaudeCodeHost().ReservedFlags);
    }

    /// <summary>A launch that names no memory file: no root, and a campaign with no folder recorded.</summary>
    private static MemoryFileLocations NoFiles { get; } = new(null, null, null);

    /// <summary>A launch that names all three, written as this platform writes an absolute path.</summary>
    private static MemoryFileLocations SomeFiles { get; } = OperatingSystem.IsWindows()
        ? new(@"C:\Users\Marta\Jason\AGENTS.md", @"C:\Users\Marta\Jason\projects\acme-corp\AGENTS.md", @"C:\Users\Marta\Jason\projects\acme-corp\campaigns\q3\AGENTS.md")
        : new("/home/marta/Jason/AGENTS.md", "/home/marta/Jason/projects/acme-corp/AGENTS.md", "/home/marta/Jason/projects/acme-corp/campaigns/q3/AGENTS.md");

    /// <summary>
    /// The values the one <c>--allowed-tools</c> takes: everything after it up to the next flag. The host reads the
    /// flag as a list, so the next flag is what ends it — which is why it never comes last.
    /// </summary>
    private static List<string> AllowList(HostLaunch launch)
    {
        var flag = launch.Command.ToList().IndexOf("--allowed-tools");
        Assert.True(flag >= 0, "the command grants nothing");
        Assert.True(
            launch.Command.Count(token => token is "--allowed-tools" or "--allowedTools") == 1,
            $"the command names the allow list more than once: {string.Join(' ', launch.Command)}");
        var values = launch.Command.Skip(flag + 1).TakeWhile(token => !token.StartsWith("--", StringComparison.Ordinal)).ToList();
        Assert.True(flag + 1 + values.Count < launch.Command.Count, "the allow list is the last thing on the command line, so nothing ends it");
        return values;
    }

    private static ExecutionProfileRevision Revision(params string[] args) => new()
    {
        Number = 1,
        Host = AgentHostKind.ClaudeCode,
        Program = "claude",
        Args = [.. args],
    };
}
