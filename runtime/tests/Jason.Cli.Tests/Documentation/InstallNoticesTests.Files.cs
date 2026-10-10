namespace Jason.Cli.Tests.Documentation;

/// <summary>
/// The two notice files beside the executable. One rule holds for each of <c>LICENSE</c> and <c>THIRD-PARTY-NOTICES</c>:
/// <c>jason-</c> and that name is written from the archive's file of that name when the archive has it, and removed when
/// it does not. The archive's file is a regular file whose entry is exactly that name at the archive's top level, and
/// nothing else.
/// </summary>
public sealed partial class InstallNoticesTests
{
    private const string Licence = "jason-LICENSE";

    private const string Notices = "jason-THIRD-PARTY-NOTICES";

    [Theory]
    [InlineData(Sh)]
    [InlineData(Ps1)]
    public void A_fresh_install_keeps_both_notices_beside_the_executable_under_their_names(string script)
    {
        var installer = Installer.For(script, _tree);

        var run = installer.Run(installer.Feed("0.6.0"));

        Assert.True(run.Exit == 0, run.Stderr);
        Assert.Equal(Names(installer.ExecutableName, Licence, Notices), Listing(installer.InstallDir));
        Assert.Equal(LicenceText("0.6.0"), Text(installer, Licence));
        Assert.Equal(NoticesText("0.6.0"), Text(installer, Notices));
    }

    [Theory]
    [InlineData(Sh)]
    [InlineData(Ps1)]
    public void An_upgrade_writes_the_new_release_s_notices_over_the_old_ones(string script)
    {
        var installer = Installer.For(script, _tree);
        Assert.Equal(0, installer.Run(installer.Feed("0.6.0")).Exit);

        var run = installer.Run(installer.Feed("0.7.0"));

        Assert.True(run.Exit == 0, run.Stderr);
        Assert.Equal(Names(installer.ExecutableName, Licence, Notices), Listing(installer.InstallDir));
        Assert.Equal(LicenceText("0.7.0"), Text(installer, Licence));
        Assert.Equal(NoticesText("0.7.0"), Text(installer, Notices));
    }

    [Theory]
    [InlineData(Sh)]
    [InlineData(Ps1)]
    public void An_upgrade_over_an_install_without_notices_writes_both(string script)
    {
        var installer = Installer.For(script, _tree);
        Assert.Equal(0, installer.Run(installer.Feed("0.5.0", []), "--version", "0.5.0").Exit);
        Assert.Equal(Names(installer.ExecutableName), Listing(installer.InstallDir));

        var run = installer.Run(installer.Feed("0.6.0"));

        Assert.True(run.Exit == 0, run.Stderr);
        Assert.Equal(Names(installer.ExecutableName, Licence, Notices), Listing(installer.InstallDir));
    }

    /// <summary>
    /// Releases from 0.2.0 to 0.5.0 carry a <c>LICENSE</c> and no <c>THIRD-PARTY-NOTICES</c>. Installing one over a later
    /// release leaves its own licence notice and no third-party notices of a release that is no longer there.
    /// </summary>
    [Theory]
    [InlineData(Sh)]
    [InlineData(Ps1)]
    public void An_archive_with_LICENSE_alone_writes_it_and_removes_the_notices_of_the_release_before(string script)
    {
        var installer = Installer.For(script, _tree);
        Assert.Equal(0, installer.Run(installer.Feed("0.6.0")).Exit);

        var run = installer.Run(installer.Feed("0.5.0", [new Entry("LICENSE", LicenceText("0.5.0"))]), "--version", "0.5.0");

        Assert.True(run.Exit == 0, run.Stderr);
        Assert.Equal(Names(installer.ExecutableName, Licence), Listing(installer.InstallDir));
        Assert.Equal(LicenceText("0.5.0"), Text(installer, Licence));
    }

    /// <summary>
    /// Under either name, an entry that is not a regular file of exactly that name at the archive's top level is not the
    /// release's notice: it is never taken, and the per-file rule removes what an earlier release left under that name.
    /// The other name is written as usual. A name listed twice is taken from neither entry by <c>install.sh</c>: GNU tar
    /// extracts the last and macOS's bsdtar the first, and no flag makes both take the same one.
    /// </summary>
    [Theory]
    [InlineData(Sh, "LICENSE", "nested")]
    [InlineData(Sh, "THIRD-PARTY-NOTICES", "nested")]
    [InlineData(Sh, "LICENSE", "parent")]
    [InlineData(Sh, "THIRD-PARTY-NOTICES", "parent")]
    [InlineData(Sh, "LICENSE", "directory")]
    [InlineData(Sh, "THIRD-PARTY-NOTICES", "directory")]
    [InlineData(Sh, "LICENSE", "link")]
    [InlineData(Sh, "THIRD-PARTY-NOTICES", "link")]
    [InlineData(Sh, "LICENSE", "duplicate")]
    [InlineData(Sh, "THIRD-PARTY-NOTICES", "duplicate")]
    [InlineData(Sh, "LICENSE", "case")]
    [InlineData(Sh, "THIRD-PARTY-NOTICES", "case")]
    [InlineData(Sh, "LICENSE", "dot-slash")]
    [InlineData(Sh, "THIRD-PARTY-NOTICES", "dot-slash")]
    [InlineData(Ps1, "LICENSE", "nested")]
    [InlineData(Ps1, "THIRD-PARTY-NOTICES", "nested")]
    [InlineData(Ps1, "LICENSE", "directory")]
    [InlineData(Ps1, "THIRD-PARTY-NOTICES", "directory")]
    [InlineData(Ps1, "LICENSE", "case")]
    [InlineData(Ps1, "THIRD-PARTY-NOTICES", "case")]
    public void A_notice_that_is_not_a_top_level_file_of_its_name_is_never_taken(string script, string name, string kind)
    {
        var installer = Installer.For(script, _tree);
        Assert.Equal(0, installer.Run(installer.Feed("0.6.0")).Exit);

        var run = installer.Run(installer.Feed("0.7.0", Hostile(name, kind, "0.7.0")));

        Assert.True(run.Exit == 0, run.Stderr);
        var other = name == "LICENSE" ? Notices : Licence;
        Assert.Equal(Names(installer.ExecutableName, other), Listing(installer.InstallDir));
        Assert.Equal(other == Licence ? LicenceText("0.7.0") : NoticesText("0.7.0"), Text(installer, other));
    }

    /// <summary>
    /// <c>Expand-Archive</c> refuses an archive that names a file twice, or a file outside the directory it unpacks into,
    /// and <c>install.ps1</c> unpacks before it installs anything: what was installed stays as it was.
    /// </summary>
    [Theory]
    [InlineData("LICENSE", "duplicate")]
    [InlineData("THIRD-PARTY-NOTICES", "duplicate")]
    [InlineData("LICENSE", "parent")]
    [InlineData("THIRD-PARTY-NOTICES", "parent")]
    public void Install_ps1_refuses_an_archive_naming_a_notice_twice_or_outside_itself_before_installing_anything(string name, string kind)
    {
        var installer = Installer.For(Ps1, _tree);
        Assert.Equal(0, installer.Run(installer.Feed("0.6.0")).Exit);
        var before = Snapshot(installer.InstallDir);

        var run = installer.Run(installer.Feed("0.7.0", Hostile(name, kind, "0.7.0")));

        Assert.True(run.Exit != 0, run.Stdout + run.Stderr);
        Assert.Equal(before, Snapshot(installer.InstallDir));
        Assert.DoesNotContain("islicensedunder", Squeezed(run.Stdout + run.Stderr), StringComparison.Ordinal);
    }

    /// <summary>A run over the installed version with both notices beside it fetches nothing but the manifest.</summary>
    [Theory]
    [InlineData(Sh)]
    [InlineData(Ps1)]
    public void An_install_already_there_with_both_notices_downloads_nothing(string script)
    {
        var installer = Installer.For(script, _tree);
        var feed = installer.Feed("0.6.0");
        Assert.Equal(0, installer.Run(feed).Exit);
        File.Delete(Path.Combine(feed, installer.Asset));
        File.Delete(Path.Combine(feed, "checksums.txt"));

        var run = installer.Run(feed);

        Assert.True(run.Exit == 0, run.Stderr);
        Assert.Contains($"Jason 0.6.0 is already installed at {installer.Target}; nothing to download.", Lines(run.Stdout));
        Assert.Equal(Names(installer.ExecutableName, Licence, Notices), Listing(installer.InstallDir));
    }

    /// <summary>
    /// An installation updated from 0.5.x by <c>jason update apply</c> is the right version without its notices. Running the
    /// install line again downloads the archive, checks it, and writes them, and the executable is not touched.
    /// </summary>
    [Theory]
    [InlineData(Sh, Licence)]
    [InlineData(Sh, Notices)]
    [InlineData(Ps1, Licence)]
    [InlineData(Ps1, Notices)]
    public void An_install_already_there_missing_a_notice_writes_it_and_leaves_the_executable_alone(string script, string missing)
    {
        var installer = Installer.For(script, _tree);
        var feed = installer.Feed("0.6.0");
        Assert.Equal(0, installer.Run(feed).Exit);
        File.Delete(Path.Combine(installer.InstallDir, missing));
        var executable = File.ReadAllBytes(installer.Target);
        var written = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(installer.Target, written);

        var run = installer.Run(feed);

        Assert.True(run.Exit == 0, run.Stderr);
        var lines = Lines(run.Stdout);
        Assert.Contains($"Jason 0.6.0 is already installed at {installer.Target}; its licence files were missing and are written beside it now.", lines);
        Assert.Equal(LicenceLine("0.6.0"), lines[^1]);
        Assert.Equal(Names(installer.ExecutableName, Licence, Notices), Listing(installer.InstallDir));
        Assert.Equal(LicenceText("0.6.0"), Text(installer, Licence));
        Assert.Equal(NoticesText("0.6.0"), Text(installer, Notices));
        Assert.Equal(executable, File.ReadAllBytes(installer.Target));
        Assert.Equal(written, File.GetLastWriteTimeUtc(installer.Target));
    }

    /// <summary>The archive the repair downloads is checked as an install's is, and one that does not match writes nothing.</summary>
    [Theory]
    [InlineData(Sh)]
    [InlineData(Ps1)]
    public void A_repair_whose_archive_does_not_match_its_checksum_writes_nothing(string script)
    {
        var installer = Installer.For(script, _tree);
        var feed = installer.Feed("0.6.0");
        Assert.Equal(0, installer.Run(feed).Exit);
        File.Delete(Path.Combine(installer.InstallDir, Notices));
        File.WriteAllText(Path.Combine(feed, "checksums.txt"), new string('0', 64) + "  " + installer.Asset + "\n");

        var run = installer.Run(feed);

        Assert.Equal(1, run.Exit);
        AssertSaid("does not match checksums.txt", run.Stderr);
        Assert.Equal(Names(installer.ExecutableName, Licence), Listing(installer.InstallDir));
    }

    /// <summary>
    /// A release before 0.6.0 never had <c>THIRD-PARTY-NOTICES</c>, so its installation has none to repair: a run over it
    /// downloads nothing, as before.
    /// </summary>
    [Theory]
    [InlineData(Sh)]
    [InlineData(Ps1)]
    public void A_release_before_0_6_0_installed_again_downloads_nothing_although_it_has_no_notices_file(string script)
    {
        var installer = Installer.For(script, _tree);
        var feed = installer.Feed("0.5.0", [new Entry("LICENSE", LicenceText("0.5.0"))]);
        Assert.Equal(0, installer.Run(feed, "--version", "0.5.0").Exit);
        File.Delete(Path.Combine(feed, installer.Asset));

        var run = installer.Run(feed, "--version", "0.5.0");

        Assert.True(run.Exit == 0, run.Stderr);
        Assert.Contains($"Jason 0.5.0 is already installed at {installer.Target}; nothing to download.", Lines(run.Stdout));
        Assert.Equal(Names(installer.ExecutableName, Licence), Listing(installer.InstallDir));
    }

    /// <summary>
    /// The notices are written after the executable is in place, so a notice that cannot be written leaves Jason installed
    /// and the run failed with 1, saying both. Its advice is true: the run again takes the repair path and writes them.
    /// </summary>
    [Theory]
    [InlineData(Sh)]
    [InlineData(Ps1)]
    public void Notices_that_cannot_be_written_fail_the_run_saying_what_is_installed_and_a_run_again_writes_them(string script)
    {
        var installer = Installer.For(script, _tree);
        var feed = installer.Feed("0.6.0");
        var blocking = Path.Combine(installer.InstallDir, Notices);
        Directory.CreateDirectory(blocking);
        File.WriteAllText(Path.Combine(blocking, "kept"), "a directory where the notices go\n");

        var run = installer.Run(feed);

        Assert.Equal(1, run.Exit);
        AssertSaid($"Jason 0.6.0 is installed at {installer.Target}, and its licence files could not be written beside it:", run.Stderr);
        AssertSaid("Run this script again.", run.Stderr);
        Assert.True(File.Exists(installer.Target));
        Assert.DoesNotContain("islicensedunder", Squeezed(run.Stdout + run.Stderr), StringComparison.Ordinal);

        Directory.Delete(blocking, recursive: true);
        var again = installer.Run(feed);

        Assert.True(again.Exit == 0, again.Stderr);
        Assert.Contains($"Jason 0.6.0 is already installed at {installer.Target}; its licence files were missing and are written beside it now.", Lines(again.Stdout));
        Assert.Equal(Names(installer.ExecutableName, Licence, Notices), Listing(installer.InstallDir));
    }

    /// <summary>The archive's entries for one hostile case, under <paramref name="name"/>, with the other name a plain file.</summary>
    private static Entry[] Hostile(string name, string kind, string version)
    {
        var other = name == "LICENSE"
            ? new Entry("THIRD-PARTY-NOTICES", NoticesText(version))
            : new Entry("LICENSE", LicenceText(version));
        var text = $"Not the release's {name}.\n";
        Entry[] hostile = kind switch
        {
            "nested" => [new Entry("x/" + name, text)],
            "parent" => [new Entry(OperatingSystem.IsWindows() ? "..\\" + name : "../" + name, text)],
            "directory" => [new Entry(name, string.Empty, EntryKind.Directory)],
            "link" => [new Entry(name, "/etc/hostname", EntryKind.SymbolicLink)],
            "duplicate" => [new Entry(name, text), new Entry(name, "And not this one either.\n")],
            "case" => [new Entry(name.ToLowerInvariant(), text)],

            // install.sh's alone: GNU tar does not take "./LICENSE" when asked for "LICENSE", and macOS's bsdtar is
            // where the rule that the whole entry name is the notice's is seen holding.
            "dot-slash" => [new Entry("./" + name, text)],
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        return [other, .. hostile];
    }

    /// <summary>Names as <see cref="Listing"/> orders them.</summary>
    private static List<string> Names(params string[] names) => [.. names.Order(StringComparer.Ordinal)];

    /// <summary>The names in a directory, files and directories alike, in ordinal order.</summary>
    private static List<string> Listing(string directory) =>
        [.. Directory.EnumerateFileSystemEntries(directory).Select(Path.GetFileName).Select(name => name!).Order(StringComparer.Ordinal)];

    private static string Text(Installer installer, string name) => File.ReadAllText(Path.Combine(installer.InstallDir, name));

    /// <summary>Every file under a directory with its bytes, so that "nothing changed" is a comparison.</summary>
    private static Dictionary<string, string> Snapshot(string directory) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(file => Path.GetRelativePath(directory, file), file => Convert.ToHexString(File.ReadAllBytes(file)), StringComparer.Ordinal);
}
