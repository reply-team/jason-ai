using Jason.Cli.Session;
using Jason.Contracts.Api;
using Jason.Contracts.Hosts;

namespace Jason.Cli.Tests.Session;

/// <summary>
/// The first message of a session on Jason itself: two lines, then a short core that names the skill teaching the rest.
/// It reaches the host as one word of its command line, the only way a first message reaches an interactive session, so
/// it is held short: how long a first message may be and still survive every launch is not measured. Its last line follows
/// the host's skills receipt, in one of two forms.
/// </summary>
public sealed class SetupBriefTests
{
    /// <summary>Both forms of the brief for every host, as a session may be started with either.</summary>
    public static TheoryData<string, bool> Forms()
    {
        var forms = new TheoryData<string, bool>();
        foreach (var kind in Enum.GetValues<AgentHostKind>())
        {
            forms.Add(AgentHosts.Word(kind), true);
            forms.Add(AgentHosts.Word(kind), false);
        }

        return forms;
    }

    /// <summary>
    /// The text as it was agreed, word for word: the first thing a person's own AI reads about changing Jason. The two
    /// forms differ in the last line alone.
    /// </summary>
    [Fact]
    public void The_brief_is_the_text_that_was_agreed()
    {
        const string Before =
            "You are in a session for changing this Jason installation itself: its roles, its plugins and its settings.\n"
            + "Follow the instructions below; ask the person what they want changed if they have not said.\n"
            + "\n"
            + "Where the parts are: run `jason status` and read the `setup` section of what it prints. It names the role skills folder, the plugins folder, the custom UI folder and the settings file. Take every path from there; never assume one.\n"
            + "\n"
            + "What you never touch:\n"
            + "- The settings file, while `settings_file_holds_secrets` is true in that section: plugins' secrets are kept in it. Never open it.\n"
            + "- Any file of Jason's outside the role skills folder and the plugins folder: routes and grants are changed with jason's own commands, which the skill names.\n"
            + "- A secret: it is the person's own to type. Never ask them to give one to you.\n"
            + "\n";

        Assert.Equal(
            Before + "The skill `customising-jason` teaches the rest of this job; use it.",
            SetupBrief.For("claude_code", deployed: true));
        Assert.Equal(
            Before + "The skill `customising-jason` teaches the rest of this job, and it may not be installed for this host. If you do not have it, ask the person, and on their yes run `jason skills install --host codex`; do not change anything of Jason's before you have it.",
            SetupBrief.For("codex", deployed: false));
    }

    /// <summary>The two lines say what the session is for, and the core follows after one blank line.</summary>
    [Theory]
    [MemberData(nameof(Forms))]
    public void The_brief_is_the_two_lines_then_a_blank_line_then_the_core(string host, bool deployed)
    {
        Assert.Equal(
            "You are in a session for changing this Jason installation itself: its roles, its plugins and its settings.\n"
            + "Follow the instructions below; ask the person what they want changed if they have not said.",
            SetupBrief.Lead);
        Assert.Equal(SetupBrief.Lead + "\n\n" + SetupBrief.Core(host, deployed), SetupBrief.For(host, deployed));
    }

    /// <summary>
    /// How long a first message may be and still survive every launch, the hand-off from the console host to Windows
    /// Terminal among them, is not measured; so the core is held short, at most 6,000 units in either form for every host,
    /// counting the growth a quote or a backslash takes when the argument is escaped.
    /// </summary>
    [Theory]
    [MemberData(nameof(Forms))]
    public void The_core_fits_any_launch_with_room_for_the_programs_and_the_folders(string host, bool deployed)
    {
        var core = SetupBrief.Core(host, deployed);
        var escaped = core.Length + core.Count(character => character is '"' or '\\');

        Assert.True(escaped <= 6_000, $"The setup brief's core for {host} is {escaped} units once escaped, past the 6,000 it is held to.");
    }

    /// <summary>The skill the brief names is one the runtime pack holds, so the name leads somewhere.</summary>
    [Fact]
    public void The_brief_names_a_skill_the_runtime_pack_holds()
    {
        Assert.Contains($"The skill `{SetupBrief.SkillName}` teaches the rest of this job", SetupBrief.Core("claude_code", deployed: true), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "skills", "runtime", SetupBrief.SkillName, "SKILL.md")));
    }

    /// <summary>A first word that begins with a dash would be read by a host as an option rather than as its first message.</summary>
    [Theory]
    [MemberData(nameof(Forms))]
    public void The_brief_begins_with_nothing_a_host_reads_as_an_option(string host, bool deployed)
    {
        Assert.False(SetupBrief.For(host, deployed).StartsWith('-'));
    }

    /// <summary>
    /// It spells no folder, so a layout that moves one changes nothing in it, and it carries nothing private, since a command
    /// line is readable by every process of the account: no path, no home, no token.
    /// </summary>
    [Theory]
    [MemberData(nameof(Forms))]
    public void The_brief_spells_no_path(string host, bool deployed)
    {
        var brief = SetupBrief.For(host, deployed);

        Assert.DoesNotContain('/', brief);
        Assert.DoesNotContain('\\', brief);
        Assert.DoesNotContain('~', brief);
        Assert.DoesNotContain(".json", brief, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        return root.FullName;
    }
}
