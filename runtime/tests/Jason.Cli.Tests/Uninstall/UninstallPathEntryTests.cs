using Jason.Cli.Uninstall;

namespace Jason.Cli.Tests.Uninstall;

/// <summary>
/// Taking the install directory off this account's PATH — only where the installer put it there, and without
/// touching another byte of the file it is in.
/// </summary>
/// <remarks>
/// A login profile is somebody's own document with years of their work in it, and an uninstall that rewrote
/// one would be remembered for that rather than for removing Jason. These are pure functions over text for
/// exactly that reason: a rule that could only be exercised by editing a real profile would be exercised by
/// nobody.
/// </remarks>
public class UninstallPathEntryTests
{
    private const string Directory = "/home/a/.local/bin";

    /// <summary>
    /// The marker has one definition and the install script is held to it. Two copies of a string that has to
    /// match is how the verb that undoes an installer drifts away from the installer.
    /// </summary>
    [Fact]
    public void The_marker_is_what_the_install_script_writes()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot(), "install", "install.sh"));

        Assert.Contains($"MARKER=\"{PathEntry.Marker}\"", script, StringComparison.Ordinal);
        Assert.Contains("export PATH=\\\"$INSTALL_DIR:\\$PATH\\\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void A_profile_loses_the_three_lines_the_installer_appended_and_keeps_every_other_byte()
    {
        const string before = "# mine\nexport EDITOR=vim\n\n# added by jason install\nexport PATH=\"/home/a/.local/bin:$PATH\"\n\nexport LANG=C\n";

        Assert.Equal("# mine\nexport EDITOR=vim\n\nexport LANG=C\n", PathEntry.WithoutEntry(before, Directory));
    }

    /// <summary>
    /// A profile that never had it comes back as the same string, not an equal one, so the caller can tell
    /// "nothing to do" from "rewritten identically" and never opens the file for writing.
    /// </summary>
    [Fact]
    public void A_profile_that_never_had_it_is_returned_unchanged()
    {
        const string before = "# mine\nexport EDITOR=vim\n";

        Assert.Same(before, PathEntry.WithoutEntry(before, Directory));
    }

    /// <summary>
    /// The line names the directory, so a second installation elsewhere keeps its own line. This is the same
    /// reasoning install.sh gives for matching the line rather than the marker above it.
    /// </summary>
    [Fact]
    public void Another_install_directorys_line_is_left_alone()
    {
        const string before = "# added by jason install\nexport PATH=\"/opt/jason:$PATH\"\n";

        Assert.Same(before, PathEntry.WithoutEntry(before, Directory));
    }

    /// <summary>A marker with no line under it is a person's own comment, and it stays.</summary>
    [Fact]
    public void A_lone_marker_is_not_removed()
    {
        const string before = "# added by jason install\n";

        Assert.Same(before, PathEntry.WithoutEntry(before, Directory));
    }

    /// <summary>And a line somebody wrote themselves, with no marker, loses the line and nothing else.</summary>
    [Fact]
    public void A_hand_written_line_loses_the_line_and_no_neighbour()
    {
        const string before = "# mine\nexport PATH=\"/home/a/.local/bin:$PATH\"\nexport LANG=C\n";

        Assert.Equal("# mine\nexport LANG=C\n", PathEntry.WithoutEntry(before, Directory));
    }

    /// <summary>A profile written with Windows line endings keeps them.</summary>
    [Fact]
    public void The_line_endings_of_the_file_survive()
    {
        const string before = "# mine\r\n\r\n# added by jason install\r\nexport PATH=\"/home/a/.local/bin:$PATH\"\r\nexport LANG=C\r\n";

        Assert.Equal("# mine\r\nexport LANG=C\r\n", PathEntry.WithoutEntry(before, Directory));
    }

    [Theory]
    [InlineData(@"C:\other;C:\Users\a\AppData\Local\Programs\jason", @"C:\other")]
    [InlineData(@"C:\Users\a\AppData\Local\Programs\jason;C:\other", @"C:\other")]
    [InlineData(@"C:\Users\a\AppData\Local\Programs\jason\;C:\other", @"C:\other")]
    [InlineData(@"C:\other;c:\users\a\appdata\local\programs\jason", @"C:\other")]
    public void The_registry_value_loses_that_directory(string before, string expected) =>
        Assert.Equal(expected, PathEntry.WithoutDirectory(before, @"C:\Users\a\AppData\Local\Programs\jason"));

    /// <summary>Every other entry verbatim, in order, with its own spelling: this edit is about one directory.</summary>
    [Fact]
    public void Every_other_entry_keeps_its_spelling_and_its_place() =>
        Assert.Equal(@"C:\other;C:\OTHER2", PathEntry.WithoutDirectory(@"C:\other;C:\d;C:\OTHER2", @"C:\d"));

    /// <summary>A value that does not carry it comes back as the same string, so nothing is written.</summary>
    [Fact]
    public void A_value_without_that_directory_is_returned_unchanged()
    {
        const string before = @"C:\other;C:\OTHER2";

        Assert.Same(before, PathEntry.WithoutDirectory(before, @"C:\Users\a\AppData\Local\Programs\jason"));
    }

    /// <summary>The same directory twice — a re-install that read a stale PATH — goes twice.</summary>
    [Fact]
    public void A_directory_listed_twice_is_removed_twice() =>
        Assert.Equal(@"C:\other", PathEntry.WithoutDirectory(@"C:\d;C:\other;C:\d", @"C:\d"));

    [Theory]
    [InlineData("/bin/zsh", 2)]
    [InlineData("/bin/bash", 1)]
    [InlineData("", 1)]
    [InlineData(null, 1)]
    public void Zsh_gets_both_profiles_and_everything_else_gets_one(string? shell, int expected) =>
        Assert.Equal(expected, PathEntry.ProfileFiles("/home/a", shell).Count);

    /// <summary>
    /// The block at the end of the file, which is what <c>install.sh</c> leaves behind whenever nothing was
    /// appended after it — so it is the ordinary profile rather than the unusual one.
    /// </summary>
    /// <remarks>
    /// This threw <see cref="ArgumentOutOfRangeException"/> out of <c>jason uninstall</c>. The walk goes
    /// backwards from a length it measured once, and taking three lines off the end left it reading past the
    /// end of a shorter list; every profile written by a test until now happened to carry a line after the
    /// block, so nothing had ever asked.
    /// </remarks>
    [Fact]
    public void A_block_at_the_end_of_the_profile_is_removed_rather_than_thrown_over()
    {
        const string before = "# mine\nexport EDITOR=vim\n\n# added by jason install\nexport PATH=\"/home/a/.local/bin:$PATH\"\n";

        Assert.Equal("# mine\nexport EDITOR=vim\n", PathEntry.WithoutEntry(before, Directory));
    }

    /// <summary>And two of them — a profile two runs appended to — go in the one pass.</summary>
    [Fact]
    public void Two_blocks_for_the_same_directory_both_go()
    {
        const string before = "# mine\n\n# added by jason install\nexport PATH=\"/home/a/.local/bin:$PATH\"\n\n"
            + "# added by jason install\nexport PATH=\"/home/a/.local/bin:$PATH\"\n";

        Assert.Equal("# mine\n", PathEntry.WithoutEntry(before, Directory));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }
}
