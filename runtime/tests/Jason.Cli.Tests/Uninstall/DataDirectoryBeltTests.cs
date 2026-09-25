using Jason.Cli.Uninstall;

namespace Jason.Cli.Tests.Uninstall;

/// <summary>
/// The directories <c>--purge-data</c> refuses before anything is removed. The cases about a drive's root, the
/// profile and the installation are decided as text: no directory there is created or removed, which is the only
/// safe way to ask this question about a drive's root. The ones about a second spelling of a directory are asked
/// of directories these tests make.
/// </summary>
public class DataDirectoryBeltTests
{
    private static readonly string Temporary = Path.GetTempPath();

    private static readonly string Profile = Path.Combine(Temporary, "belt-home", "someone");

    private static readonly string Install = Path.Combine(Temporary, "belt-apps", "jason");

    public static TheoryData<string, string> Refused() => new()
    {
        { "the root of a file system", Path.GetPathRoot(Temporary)! },
        { "the profile", Profile },
        { "the profile, spelled with a trailing separator", Profile + Path.DirectorySeparatorChar },
        { "an ancestor of the profile", Path.GetDirectoryName(Profile)! },
        { "the temporary directory", Temporary },
        { "the installation", Install },
        { "an ancestor of the installation", Path.GetDirectoryName(Install)! },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public void A_directory_that_is_not_a_data_directory_is_refused(string what, string directory)
    {
        var refusal = DataDirectoryBelt.Refusal(directory, Profile, Temporary, Install);

        Assert.True(refusal is not null, $"{what} ('{directory}') was not refused.");
    }

    public static TheoryData<string, string> Allowed() => new()
    {
        { "the default, inside the profile", Path.Combine(Profile, ".jason") },
        { "a directory of its own under the temporary directory", Path.Combine(Temporary, "jason-data") },
        { "a sibling of the installation", Path.Combine(Temporary, "belt-apps", "jason-data") },
        { "a directory whose name only begins like the profile's", Profile + "-data" },
    };

    [Theory]
    [MemberData(nameof(Allowed))]
    public void A_data_directory_of_its_own_is_not(string what, string directory)
    {
        var refusal = DataDirectoryBelt.Refusal(directory, Profile, Temporary, Install);

        Assert.True(refusal is null, $"{what} ('{directory}') was refused: {refusal}");
    }

    /// <summary>Where nothing names an installation or a profile, those two simply have nothing to hold.</summary>
    [Fact]
    public void An_unnamed_installation_or_profile_holds_nothing()
    {
        Assert.Null(DataDirectoryBelt.Refusal(Path.Combine(Temporary, "jason-data"), null, Temporary, null));
    }

    /// <summary>
    /// The profile spelled with the prefix that turns off Windows' path parsing is the profile. The belt compared
    /// strings, and <c>\\?\C:\Users\…</c> got past it.
    /// </summary>
    [Fact]
    public void The_profile_spelled_with_the_long_path_prefix_is_refused()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The prefix is Windows' own; elsewhere it is an ordinary, if odd, directory name.");

        Assert.NotNull(DataDirectoryBelt.Refusal(@"\\?\" + Profile, Profile, Temporary, Install));
    }

    /// <summary>
    /// And a link whose target is the profile is the profile — a junction on Windows, which any account may make. The
    /// belt compared the link's own path, and it got past.
    /// </summary>
    [Fact]
    public void A_link_to_the_profile_is_refused()
    {
        using var tree = new Documentation.TempTree();
        var profile = tree.NewDirectory("someone");
        var link = Path.Combine(tree.Root, "link-to-someone");
        Link(link, profile);

        Assert.NotNull(DataDirectoryBelt.Refusal(link, profile, Temporary, Install));
        Assert.NotNull(DataDirectoryBelt.Refusal(Path.Combine(link, "."), Path.Combine(profile, "inside"), Temporary, Install));
    }

    /// <summary>
    /// A repository's working tree is not a data directory. <c>JASON_DATA_DIR=.</c> typed in a checkout of this
    /// repository would have the purge remove its <c>plugins</c> and <c>skills</c>, which are names Jason keeps in a
    /// data directory too.
    /// </summary>
    [Fact]
    public void A_repositorys_working_tree_is_refused()
    {
        using var tree = new Documentation.TempTree();
        var checkout = tree.NewDirectory("checkout");
        Directory.CreateDirectory(Path.Combine(checkout, ".git"));

        var refusal = DataDirectoryBelt.Refusal(checkout, Profile, Temporary, Install);

        Assert.NotNull(refusal);
        Assert.Contains(".git", refusal, StringComparison.Ordinal);
    }

    /// <summary>A link at <paramref name="link"/> to the directory <paramref name="target"/>, of the kind this account may make.</summary>
    private static void Link(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }

        // A junction: a symbolic link needs a privilege an ordinary Windows account does not hold, and a junction
        // does not — which is what makes it the one a stale JASON_DATA_DIR is likely to go through.
        var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe")) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "/c", "mklink", "/J", link, target })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = System.Diagnostics.Process.Start(start)!;
        process.WaitForExit();
        Assert.True(Directory.Exists(link), $"the junction at '{link}' was not made.");
    }
}
