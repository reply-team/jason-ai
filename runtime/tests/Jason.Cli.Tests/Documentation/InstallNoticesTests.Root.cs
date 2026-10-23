using Jason.Contracts.Update;

namespace Jason.Cli.Tests.Documentation;

/// <summary>
/// What the install scripts say, in the same runs, about where Jason will keep what it learns about the person's business:
/// two lines, the folder the harness root's default is on that machine and how to keep the files elsewhere, then where a
/// campaign that names no project goes (<see cref="HarnessRootLines"/>). Said by every run that succeeds, after what was
/// installed and before the PATH step, so that the PATH step and the licence stay the last two things said. An install
/// makes no folder for the files: the runtime makes the root at the first project or campaign it creates.
/// </summary>
public sealed partial class InstallNoticesTests
{
    /// <summary>A fresh install says the two lines after the line that says what it installed, and the licence after them.</summary>
    [Theory]
    [InlineData(Sh)]
    [InlineData(Ps1)]
    public void A_fresh_install_says_where_the_files_will_be_after_what_it_installed(string script)
    {
        var installer = Installer.For(script, _tree);

        var run = installer.Run(installer.Feed("0.6.0"));

        Assert.True(run.Exit == 0, run.Stderr);
        AssertSaysWhereTheFilesWillBe(installer, Lines(run.Stdout), "Installed Jason 0.6.0 to ");
    }

    /// <summary>A run over the version already there describes the installation as a fresh one does, these two lines included.</summary>
    [Theory]
    [InlineData(Sh)]
    [InlineData(Ps1)]
    public void An_install_already_there_says_where_the_files_will_be_too(string script)
    {
        var installer = Installer.For(script, _tree);
        var feed = installer.Feed("0.6.0");
        Assert.Equal(0, installer.Run(feed).Exit);

        var again = installer.Run(feed);

        Assert.True(again.Exit == 0, again.Stderr);
        AssertSaysWhereTheFilesWillBe(installer, Lines(again.Stdout), "Jason 0.6.0 is already installed at ");
    }

    /// <summary>A run that installs nothing says nothing about files an installation would keep.</summary>
    [Theory]
    [InlineData(Sh)]
    [InlineData(Ps1)]
    public void A_failed_install_says_nothing_about_where_the_files_will_be(string script)
    {
        var installer = Installer.For(script, _tree);
        var feed = installer.Feed("0.6.0");
        File.WriteAllText(Path.Combine(feed, ReleaseAssets.Checksums), new string('0', 64) + "  " + installer.Asset + "\n");

        var run = installer.Run(feed);

        Assert.NotEqual(0, run.Exit);
        Assert.DoesNotContain(HarnessRootLines.Opening, run.Stdout + run.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("go to the project `Default`", run.Stdout + run.Stderr, StringComparison.Ordinal);
    }

    /// <summary>
    /// The sentence says where the folder will be, and the install makes none: the home <c>install.sh</c> is given holds
    /// nothing after a run that leaves the PATH alone. <c>install.ps1</c>'s home is the account's own, which this test
    /// does not write either, so there it is held to whether the folder was there before.
    /// </summary>
    [Theory]
    [InlineData(Sh)]
    [InlineData(Ps1)]
    public void An_install_makes_no_folder_for_the_files(string script)
    {
        var installer = Installer.For(script, _tree);
        var there = Directory.Exists(installer.DefaultRoot);

        var run = installer.Run(installer.Feed("0.6.0"));

        Assert.True(run.Exit == 0, run.Stderr);
        Assert.Equal(there, Directory.Exists(installer.DefaultRoot));
        if (!installer.Windows)
        {
            Assert.Empty(Directory.EnumerateFileSystemEntries(installer.Home));
        }
    }

    /// <summary>
    /// A home written with separators at its end, one or several, is the same home, and the runtime makes its default
    /// root there as <c>Jason</c> under it; the line says that folder, not one with a separator twice.
    /// </summary>
    [Theory]
    [InlineData("/")]
    [InlineData("//")]
    [InlineData("///")]
    public void Install_sh_says_the_root_of_a_home_written_with_trailing_slashes_as_the_runtime_makes_it(string trailing)
    {
        var installer = Installer.For(Sh, _tree);
        installer.HomeVariable = installer.Home + trailing;

        var run = installer.Run(installer.Feed("0.6.0"));

        Assert.True(run.Exit == 0, run.Stderr);
        Assert.Contains(HarnessRootLines.Where(installer.DefaultRoot), Lines(run.Stdout));
    }

    /// <summary>
    /// Where the PATH step writes a profile, the two lines come before what it says: the PATH step and the licence are the
    /// last two things said. On Windows that step writes the account's registry, so its half is the order in the text below.
    /// </summary>
    [Fact]
    public void Install_sh_says_where_the_files_will_be_before_the_path_step()
    {
        var installer = Installer.For(Sh, _tree);

        var run = installer.Run(installer.Feed("0.6.0"), modifyPath: true);

        Assert.True(run.Exit == 0, run.Stderr);
        var lines = Lines(run.Stdout);
        Assert.Equal(HarnessRootLines.Where(installer.DefaultRoot), lines[^4]);
        Assert.Equal(HarnessRootLines.DefaultProject, lines[^3]);
        Assert.StartsWith($"Added {installer.InstallDir} to the PATH in ", lines[^2], StringComparison.Ordinal);
    }

    [Fact]
    public void Install_ps1_says_where_the_files_will_be_before_the_path_step()
    {
        var code = Code(Ps1);
        var where = code.IndexOf(HarnessRootLines.Opening, StringComparison.Ordinal);
        var path = code.IndexOf("is on your PATH; open a new terminal", StringComparison.Ordinal);

        Assert.True(where >= 0 && path > where, "install.ps1 does not say where the files will be before the PATH step.");
    }

    /// <summary>The same two lines from the Windows PowerShell a fresh Windows has, which reads the script as PowerShell 5.1 does.</summary>
    [Fact]
    public void Install_ps1_says_where_the_files_will_be_under_windows_powershell_too()
    {
        var installer = Installer.For(Ps1, _tree);

        var run = installer.RunUnderWindowsPowerShell(installer.Feed("0.6.0"));

        Assert.True(run.Exit == 0, run.Stderr);
        AssertSaysWhereTheFilesWillBe(installer, Lines(run.Stdout), "Installed Jason 0.6.0 to ");
    }

    /// <summary>
    /// The two lines, each said once, the first naming the root's default on this machine; after the line that says what
    /// was installed; and the licence still the last thing said.
    /// </summary>
    private static void AssertSaysWhereTheFilesWillBe(Installer installer, List<string> lines, string installed)
    {
        var at = lines.FindIndex(line => line.StartsWith(installed, StringComparison.Ordinal));
        var where = lines.IndexOf(HarnessRootLines.Where(installer.DefaultRoot));
        var all = string.Join(Environment.NewLine, lines);

        Assert.True(at >= 0 && where > at, $"The run does not say, after \"{installed}…\": {HarnessRootLines.Where(installer.DefaultRoot)}{Environment.NewLine}It said:{Environment.NewLine}{all}");
        Assert.True(where + 1 < lines.Count && lines[where + 1] == HarnessRootLines.DefaultProject, $"The line after it is not: {HarnessRootLines.DefaultProject}{Environment.NewLine}It said:{Environment.NewLine}{all}");
        Assert.Single(lines, line => line.StartsWith(HarnessRootLines.Opening, StringComparison.Ordinal));
        Assert.Single(lines, line => line == HarnessRootLines.DefaultProject);
        Assert.Equal(LicenceLine("0.6.0"), lines[^1]);
    }
}
