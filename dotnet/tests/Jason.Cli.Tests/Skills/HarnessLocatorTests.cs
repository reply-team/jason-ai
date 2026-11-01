using Jason.Cli.Skills;
using Jason.Contracts.Api;

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

        Assert.Equal(AgentHostKind.ClaudeCode, root.Host);
        Assert.Equal(Path.Combine(home, ".claude", "skills"), root.Directory);
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
    /// <c>--root</c> replaces detection rather than narrowing it. That makes containment one flag instead of a
    /// careful combination, which is what a person needs when installing into a harness this build has never
    /// heard of.
    /// </summary>
    [Fact]
    public void A_named_directory_is_the_only_root_and_nothing_else_is_consulted()
    {
        using var tree = new TempPaths();

        var root = Assert.Single(HarnessLocators.At(tree.Paths.Root).Detect());

        Assert.Equal(tree.Paths.Root, root.Directory);
        Assert.Equal(AgentHostKind.ClaudeCode, root.Host);
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
}
