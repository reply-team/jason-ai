using Jason.Cli.Skills;
using Jason.Contracts.Api;
using Jason.Contracts.Hosts;

namespace Jason.Cli.Tests.Skills;

/// <summary>
/// Where a person's own agent harness keeps skills. Proved without any test going near the home directory of
/// whoever ran the suite: the composition takes a home, and the one that reads the real one is a call away
/// from it.
/// </summary>
public class HarnessLocatorTests
{
    [Fact]
    public void The_claude_code_root_is_the_skills_directory_under_the_hosts_own_folder()
    {
        using var tree = new TempPaths();
        var home = Directory.CreateDirectory(Path.Combine(tree.Paths.Root, "home")).FullName;
        Directory.CreateDirectory(Path.Combine(home, ".claude"));

        var root = Assert.Single(HarnessLocators.ForHome(home).Detect());

        Assert.Equal([AgentHostKind.ClaudeCode], root.Hosts);
        Assert.Equal(Path.Combine(home, ".claude", "skills"), root.Directory);
    }

    /// <summary>Every host of the table is detected by its own marker, and its root is where the table says.</summary>
    [Theory]
    [MemberData(nameof(Kinds))]
    public void Each_host_of_the_table_is_detected_by_its_marker_at_its_skills_root(AgentHostKind kind)
    {
        using var tree = new TempPaths();
        var home = Directory.CreateDirectory(Path.Combine(tree.Paths.Root, "home")).FullName;
        var facts = AgentHosts.Of(kind);
        Directory.CreateDirectory(AgentHosts.Under(home, facts.HomeMarker));

        var root = Assert.Single(HarnessLocators.ForHome(home).Detect(), root => root.Hosts.Contains(kind));

        Assert.Equal(AgentHosts.Under(home, facts.HomeSkills), root.Directory);
    }

    /// <summary>
    /// A harness that is not on this machine is not a root to write into. Detection answers what is there, and
    /// creating the directory would be installing a harness on somebody's behalf.
    /// </summary>
    [Fact]
    public void A_home_with_no_harness_in_it_detects_nothing()
    {
        using var tree = new TempPaths();

        Assert.Empty(HarnessLocators.ForHome(tree.Paths.Root).Detect());
    }

    /// <summary>
    /// A marker is a path under home, nested or not, and a file says a host is installed as well as a directory does:
    /// two hosts may keep one folder, and only a file of one of them inside it tells them apart.
    /// </summary>
    [Fact]
    public void A_marker_may_be_a_nested_directory_or_a_file_and_a_host_without_its_marker_is_not_detected()
    {
        using var tree = new TempPaths();
        var home = Directory.CreateDirectory(Path.Combine(tree.Paths.Root, "home")).FullName;
        Directory.CreateDirectory(Path.Combine(home, ".shared", "first"));
        File.WriteAllText(Path.Combine(home, ".shared", "second.json"), "{}");

        var roots = HarnessLocators.ForHome(home, StandInTable(".shared/first", ".shared/second.json", ".shared/third")).Detect();

        Assert.Equal(
            [(AgentHostKind.ClaudeCode, Path.Combine(home, ".shared", "one-skills")), ((AgentHostKind)1, Path.Combine(home, ".shared", "two-skills"))],
            roots.Select(root => (Assert.Single(root.Hosts), root.Directory)));
    }

    /// <summary>
    /// Two hosts that read one skills folder are one root naming both: one deployment, one record, in the order of
    /// the table, however the two spell the path.
    /// </summary>
    [Fact]
    public void Two_hosts_reading_one_skills_folder_are_one_root_naming_both()
    {
        using var tree = new TempPaths();
        var home = Directory.CreateDirectory(Path.Combine(tree.Paths.Root, "home")).FullName;
        Directory.CreateDirectory(Path.Combine(home, ".one"));
        Directory.CreateDirectory(Path.Combine(home, ".two"));
        AgentHostFacts[] table =
        [
            new(AgentHostKind.ClaudeCode, "one", ".one", ".agents/skills", ".agents/skills", null),
            new((AgentHostKind)1, "two", ".two", OperatingSystem.IsWindows() ? ".Agents/skills/" : ".agents/skills/", ".agents/skills", null),
        ];

        var root = Assert.Single(HarnessLocators.ForHome(home, table).Detect());

        Assert.Equal([AgentHostKind.ClaudeCode, (AgentHostKind)1], root.Hosts);
        Assert.Equal(Path.Combine(home, ".agents", "skills"), root.Directory);
    }

    /// <summary>
    /// <c>--root</c> replaces detection rather than narrowing it. That makes containment one flag instead of a
    /// careful combination, which is what a person needs when installing into a harness this build has never
    /// heard of: so the named root is no host's unless <c>--host</c> labels it.
    /// </summary>
    [Fact]
    public void A_named_directory_is_the_only_root_and_nothing_else_is_consulted()
    {
        using var tree = new TempPaths();

        var root = Assert.Single(HarnessLocators.At(tree.Paths.Root).Detect());

        Assert.Equal(tree.Paths.Root, root.Directory);
        Assert.Empty(root.Hosts);
    }

    [Fact]
    public void A_named_directory_labelled_with_a_host_names_that_host()
    {
        using var tree = new TempPaths();

        var root = Assert.Single(HarnessLocators.At(tree.Paths.Root, AgentHostKind.ClaudeCode).Detect());

        Assert.Equal([AgentHostKind.ClaudeCode], root.Hosts);
    }

    /// <summary>The one this machine would use is a composition over this machine's home, and nothing more.</summary>
    [Fact]
    public void The_locator_for_this_machine_is_the_same_composition_over_the_users_own_home()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.Equal(
            HarnessLocators.ForHome(home).Detect().Select(root => root.Directory),
            HarnessLocators.ForThisMachine().Detect().Select(root => root.Directory));
    }

    public static TheoryData<AgentHostKind> Kinds() => [.. Enum.GetValues<AgentHostKind>()];

    /// <summary>
    /// A table of stand-in hosts, so the rules are proved for more hosts than this build ships. The kinds past the
    /// first are values no member names, which the locator carries as given.
    /// </summary>
    private static AgentHostFacts[] StandInTable(params string[] markers) =>
    [
        .. markers.Select((marker, index) => new AgentHostFacts(
            (AgentHostKind)index,
            "host" + index,
            marker,
            ".shared/" + new[] { "one", "two", "three" }[index] + "-skills",
            ".agents/skills",
            null)),
    ];
}
