using Jason.Cli.Session;

namespace Jason.Cli.Tests.Session;

/// <summary>
/// The first message of a session on Jason itself: two lines, then a short core that names the skill teaching the rest.
/// It reaches the host as one word of its command line, the only way a first message reaches an interactive session, so
/// it is held short enough for any launch, Windows' command interpreter's included.
/// </summary>
public sealed class SetupBriefTests
{
    /// <summary>The two lines say what the session is for, and the core follows after one blank line.</summary>
    [Fact]
    public void The_brief_is_the_two_lines_then_a_blank_line_then_the_core()
    {
        Assert.Equal(
            "You are in a session for changing this Jason installation itself: its roles, its plugins and its settings.\n"
            + "Follow the instructions below; ask the person what they want changed if they have not said.",
            SetupBrief.Lead);
        Assert.Equal(SetupBrief.Lead + "\n\n" + SetupBrief.Core, SetupBrief.Text);
    }

    /// <summary>
    /// A host installed as a script shim is started through Windows' command interpreter, whose command line holds about
    /// 8,000 units, and a long first message is not known to survive the hand-off to Windows Terminal; so the core is at
    /// most 6,000 units, counting the growth a quote or a backslash takes when the argument is escaped.
    /// </summary>
    [Fact]
    public void The_core_fits_any_launch_with_room_for_the_programs_and_the_folders()
    {
        var escaped = SetupBrief.Core.Length + SetupBrief.Core.Count(character => character is '"' or '\\');

        Assert.True(escaped <= 6_000, $"The setup brief's core is {escaped} units once escaped, past the 6,000 it is held to.");
    }

    /// <summary>The skill the brief names is one the runtime pack holds, so the name leads somewhere.</summary>
    [Fact]
    public void The_brief_names_a_skill_the_runtime_pack_holds()
    {
        Assert.Contains($"The skill `{SetupBrief.SkillName}` teaches the rest of this job; use it.", SetupBrief.Core, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "skills", "runtime", SetupBrief.SkillName, "SKILL.md")));
    }

    /// <summary>A first word that begins with a dash would be read by a host as an option rather than as its first message.</summary>
    [Fact]
    public void The_brief_begins_with_nothing_a_host_reads_as_an_option()
    {
        Assert.False(SetupBrief.Text.StartsWith('-'));
    }

    /// <summary>
    /// It spells no folder, so a layout that moves one changes nothing in it, and it carries nothing private, since a command
    /// line is readable by every process of the account: no path, no home, no token.
    /// </summary>
    [Fact]
    public void The_brief_spells_no_path()
    {
        Assert.DoesNotContain('/', SetupBrief.Text);
        Assert.DoesNotContain('\\', SetupBrief.Text);
        Assert.DoesNotContain('~', SetupBrief.Text);
        Assert.DoesNotContain(".json", SetupBrief.Text, StringComparison.Ordinal);
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
