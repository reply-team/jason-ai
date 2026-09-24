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

    /// <summary>
    /// And a line somebody wrote themselves, with no marker above it, is theirs and stays. The installer and the
    /// repair both write the marker; a bare line is neither's, and an uninstall that removed it would be taking
    /// ~/.local/bin off somebody's PATH along with everything else in it.
    /// </summary>
    [Fact]
    public void A_hand_written_line_is_somebodys_own_and_stays()
    {
        const string before = "# mine\nexport PATH=\"/home/a/.local/bin:$PATH\"\nexport LANG=C\n";

        Assert.Same(before, PathEntry.WithoutEntry(before, Directory));
    }

    /// <summary>
    /// A profile with one CRLF in it keeps every line ending as it was. It used to come back all CRLF, which puts a
    /// carriage return at the end of every line a shell reads.
    /// </summary>
    [Fact]
    public void A_profile_with_one_crlf_keeps_every_line_ending_as_it_was()
    {
        const string before = "# mine\r\nexport EDITOR=vim\n\n# added by jason install\nexport PATH=\"/home/a/.local/bin:$PATH\"\nexport LANG=C\n";

        Assert.Equal("# mine\r\nexport EDITOR=vim\nexport LANG=C\n", PathEntry.WithoutEntry(before, Directory));
    }

    /// <summary>
    /// And every byte that is not the block comes back as it was, whatever the file's encoding: read as UTF-8, a
    /// byte that was not UTF-8 came back as U+FFFD.
    /// </summary>
    [Fact]
    public void Every_byte_outside_the_block_comes_back_as_it_was()
    {
        byte[] mine = [.. "# caf"u8, 0xE9, .. "\n"u8];
        byte[] block = [.. "\n# added by jason install\nexport PATH=\"/home/a/.local/bin:$PATH\"\n"u8];
        byte[] after = [.. "export LANG=C\n"u8];

        Assert.Equal([.. mine, .. after], PathEntry.WithoutEntry([.. mine, .. block, .. after], Directory));
    }

    /// <summary>A directory with non-ASCII in its name is found in the file's own bytes all the same.</summary>
    [Fact]
    public void A_directory_named_beyond_ascii_is_found_in_the_bytes()
    {
        const string directory = "/home/\u00e9l\u00e8ve/.local/bin";
        var before = System.Text.Encoding.UTF8.GetBytes($"# mine\n\n{PathEntry.Marker}\n{PathEntry.ExportLine(directory)}\n");

        Assert.Equal("# mine\n"u8.ToArray(), PathEntry.WithoutEntry(before, directory));
        Assert.True(PathEntry.CarriesLine(before, directory));
    }

    /// <summary>
    /// Whether a profile carries the line is a question about whole lines: a line commented out, or one that only
    /// contains the text, does not put anything on a PATH.
    /// </summary>
    [Theory]
    [InlineData("export PATH=\"/home/a/.local/bin:$PATH\"\n", true)]
    [InlineData("export PATH=\"/home/a/.local/bin:$PATH\"   \r\n", true)]
    [InlineData("# export PATH=\"/home/a/.local/bin:$PATH\"\n", false)]
    [InlineData("#export PATH=\"/home/a/.local/bin:$PATH\"\n", false)]
    [InlineData("[ -d x ] && export PATH=\"/home/a/.local/bin:$PATH\"\n", false)]
    [InlineData("", false)]
    public void A_profile_carries_the_line_only_as_a_whole_line(string profile, bool carries) =>
        Assert.Equal(carries, PathEntry.CarriesLine(System.Text.Encoding.UTF8.GetBytes(profile), Directory));

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

    /// <summary>
    /// The removal looks in every profile the installer or a repair may have written into, whatever this account's
    /// shell is now: the shell somebody uses today is not the one they installed under.
    /// </summary>
    [Fact]
    public void The_removal_looks_in_every_profile_the_installer_may_have_written() =>
        Assert.Equal(
            [".profile", ".bash_profile", ".bash_login", ".zprofile"],
            PathEntry.ProfileFiles("/home/a").Select(Path.GetFileName));

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

    /// <summary>
    /// The profile a repair writes into is the one the running shell reads at login — which for zsh is not
    /// <c>~/.profile</c> at all.
    /// </summary>
    /// <remarks>
    /// And bash does not read <c>~/.profile</c> when <c>~/.bash_profile</c> or <c>~/.bash_login</c> exists — the
    /// skeleton of every Fedora, RHEL and Arch account, and a macOS account on bash. The rule said
    /// <c>~/.profile</c> for bash, so the installer wrote there, the repair wrote there and the check read there,
    /// and all three agreed about a file no login shell of that account opens.
    /// </remarks>
    [Theory]
    [MemberData(nameof(LoginProfiles))]
    public void A_repair_writes_into_the_profile_this_shell_reads_at_login(string? shell, string[] existing, string expected) =>
        Assert.Equal(expected, Path.GetFileName(PathEntry.LoginProfile("/home/a", shell, path => existing.Contains(Path.GetFileName(path)))));

    public static TheoryData<string?, string[], string> LoginProfiles() => new()
    {
        { "/bin/zsh", [], ".zprofile" },
        { "/usr/bin/zsh", [".profile"], ".zprofile" },
        { "/bin/bash", [], ".profile" },
        { "/bin/bash", [".profile"], ".profile" },
        { "/bin/bash", [".bash_profile", ".profile"], ".bash_profile" },
        { "/bin/bash", [".bash_login", ".profile"], ".bash_login" },
        { "/bin/bash", [".bash_profile", ".bash_login"], ".bash_profile" },
        { "/bin/sh", [".bash_profile"], ".profile" },
        { "/bin/dash", [], ".profile" },
        { "", [".bash_profile"], ".profile" },
        { null, [], ".profile" },
    };

    /// <summary>
    /// <c>install.sh</c> carries the rule as the function the C# spells, word for word — and runs it, so the
    /// installer, the repair and the check cannot disagree about which file a login shell reads.
    /// </summary>
    [Fact]
    public void The_installer_carries_the_login_profile_rule_word_for_word()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot(), "install", "install.sh")).Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains(PathEntry.LoginProfileFunction.Replace("\r\n", "\n", StringComparison.Ordinal), script, StringComparison.Ordinal);
        Assert.Contains("add_to_profile \"$(login_profile)\"", script, StringComparison.Ordinal);
    }

    /// <summary>And the function, run in <c>sh</c> against real files, answers what the C# answers, case by case.</summary>
    [Theory]
    [MemberData(nameof(LoginProfiles))]
    public void The_installers_function_answers_what_the_rule_answers(string? shell, string[] existing, string expected)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "install.sh runs in sh, on the platforms it installs on.");

        using var tree = new Jason.Cli.Tests.Documentation.TempTree();
        var home = tree.NewDirectory("home");
        foreach (var name in existing)
        {
            File.WriteAllText(Path.Combine(home, name), "# mine\n");
        }

        var start = new System.Diagnostics.ProcessStartInfo("/bin/sh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add(PathEntry.LoginProfileFunction + "\nlogin_profile");
        start.Environment["HOME"] = home;
        start.Environment["SHELL"] = shell ?? string.Empty;

        using var process = System.Diagnostics.Process.Start(start)!;
        var said = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();

        Assert.Equal(Path.Combine(home, expected), said);
        Assert.Equal(PathEntry.LoginProfile(home, shell), said);
    }

    /// <summary>
    /// Read from real files under a home: the removal finds the installer's block in every profile it may have
    /// written, and the check is answered by the one file a login shell reads — never by one it does not.
    /// </summary>
    [Fact]
    public void A_block_in_a_profile_bash_does_not_read_is_ours_to_remove_and_no_answer_to_the_check()
    {
        using var tree = new Jason.Cli.Tests.Documentation.TempTree();
        var home = tree.NewDirectory("home");
        File.WriteAllText(Path.Combine(home, ".bash_profile"), "# mine\n");
        File.WriteAllText(Path.Combine(home, ".profile"), $"# mine\n\n{PathEntry.Marker}\n{PathEntry.ExportLine(Directory)}\n");

        var plan = PathEntry.ReadProfiles(Directory, home, "/bin/bash");

        Assert.True(plan.Ours);
        Assert.Equal([Path.Combine(home, ".profile")], plan.Profiles);
        Assert.Null(plan.Persisted);
    }

    /// <summary>And a line a login shell does read answers the check, marked or not — while only a marked one is ours.</summary>
    [Fact]
    public void A_hand_written_line_in_the_login_profile_answers_the_check_and_is_not_ours()
    {
        using var tree = new Jason.Cli.Tests.Documentation.TempTree();
        var home = tree.NewDirectory("home");
        File.WriteAllText(Path.Combine(home, ".zprofile"), $"{PathEntry.ExportLine(Directory)}\n");

        var plan = PathEntry.ReadProfiles(Directory, home, "/usr/bin/zsh");

        Assert.False(plan.Ours);
        Assert.Empty(plan.Profiles);
        Assert.Equal(Path.Combine(home, ".zprofile"), plan.Persisted);
    }

    /// <summary>A commented-out line answers nothing.</summary>
    [Fact]
    public void A_commented_line_answers_nothing()
    {
        using var tree = new Jason.Cli.Tests.Documentation.TempTree();
        var home = tree.NewDirectory("home");
        File.WriteAllText(Path.Combine(home, ".profile"), $"# {PathEntry.ExportLine(Directory)}\n");

        Assert.Null(PathEntry.ReadProfiles(Directory, home, "/bin/sh").Persisted);
    }

    /// <summary>
    /// On Windows a directory is Jason's own only where nothing is in it but what the installer writes there —
    /// and only then is its entry on the Path Jason's to take off.
    /// </summary>
    [Theory]
    [InlineData(new[] { "jason.exe" }, true)]
    [InlineData(new[] { "JASON.EXE", "jason.previous.exe" }, true)]
    [InlineData(new string[0], true)]
    [InlineData(new[] { "jason.exe", "python.exe" }, false)]
    [InlineData(new[] { "winget.exe", "jason.exe", "Microsoft.DesktopAppInstaller_8wekyb3d8bbwe" }, false)]
    public void A_directory_is_jasons_own_only_where_it_holds_nothing_else(string[] entries, bool own) =>
        Assert.Equal(own, PathEntry.OnlyInstallerFiles(entries.Select(entry => Path.Combine(@"C:\somewhere", entry))));

    /// <summary>
    /// The Unix repair appends the marked line to that profile <b>and</b> exports it for the shell it is
    /// typed in.
    /// </summary>
    /// <remarks>
    /// Both halves, or it is not a repair. The bare export line lasts exactly one shell, so the next
    /// <c>jason status</c> says what the last one said; and a line appended to a profile does nothing for the
    /// shell the operator is standing in. The Windows half of this repair writes the registry and persists,
    /// so with only one half the two platforms differed in kind and neither said so.
    /// </remarks>
    [Fact]
    public void The_unix_repair_persists_the_line_and_exports_it_for_this_shell() =>
        Assert.Equal(
            "(grep -qxF 'export PATH=\"/home/a/.local/bin:$PATH\"' '/home/a/.profile' 2>/dev/null"
            + " || printf '\\n%s\\n%s\\n' '# added by jason install' 'export PATH=\"/home/a/.local/bin:$PATH\"' >> '/home/a/.profile')"
            + " && case \":$PATH:\" in *:'/home/a/.local/bin':*) ;; *) export PATH=\"/home/a/.local/bin:$PATH\" ;; esac",
            PathEntry.AppendCommand(Directory, "/home/a/.profile"));

    /// <summary>
    /// And what the repair appends is what <c>jason uninstall</c> takes back out: marker, line and the blank
    /// line above them.
    /// </summary>
    /// <remarks>
    /// A repair whose line the removal could not find again would leave a profile carrying Jason after Jason
    /// was gone. The appended text is composed here rather than by running the command — what is being held
    /// is the order and the count of the newlines, and <c>InstallScriptTests</c> holds that format against
    /// the one <c>install.sh</c> itself appends with.
    /// </remarks>
    [Fact]
    public void What_the_repair_appends_is_what_the_removal_takes_back_out()
    {
        const string before = "# mine\nexport EDITOR=vim\n";
        var appended = before + "\n" + PathEntry.Marker + "\n" + PathEntry.ExportLine(Directory) + "\n";

        Assert.Equal(before, PathEntry.WithoutEntry(appended, Directory));
    }

    /// <summary>
    /// A directory with an apostrophe in it is one shell word rather than a syntax error.
    /// </summary>
    /// <remarks>
    /// A repair that does not parse is a command that appears to have run, printed by the verb whose whole
    /// job is to be trusted about readiness.
    /// </remarks>
    [Fact]
    public void A_directory_with_an_apostrophe_stays_one_shell_word()
    {
        var command = PathEntry.AppendCommand("/home/o'brien/bin", "/home/o'brien/.profile");

        Assert.Contains(">> '/home/o'\\''brien/.profile'", command, StringComparison.Ordinal);
        Assert.Contains("'export PATH=\"/home/o'\\''brien/bin:$PATH\"'", command, StringComparison.Ordinal);
    }

    /// <summary>
    /// The Windows repair is the installer's own rule: the empty entries are dropped before the directory is
    /// appended, and the running session is told as well.
    /// </summary>
    /// <remarks>
    /// An account whose user <c>Path</c> is empty — a fresh one — would otherwise be left with a leading
    /// separator, and an empty PATH entry is the current directory. What the line does, rather than what it
    /// says, is run in <see cref="UserPathRegistryTests"/>.
    /// </remarks>
    [Fact]
    public void The_windows_repair_drops_the_empty_entries_the_way_the_installer_does()
    {
        var command = PathEntry.RegistryCommand(@"C:\Users\a\AppData\Local\Programs\jason\");

        Assert.Contains("Where-Object { $_ }", command, StringComparison.Ordinal);
        Assert.Contains("CreateSubKey('Environment')", command, StringComparison.Ordinal);
        Assert.Contains("$env:Path +=", command, StringComparison.Ordinal);

        // The trailing separator the caller wrote is not part of the entry, exactly as install.ps1 trims it.
        Assert.Contains(@"'C:\Users\a\AppData\Local\Programs\jason'", command, StringComparison.Ordinal);
        Assert.DoesNotContain(@"jason\'", command, StringComparison.Ordinal);
    }

    /// <summary>
    /// The value is read unexpanded, so an entry is compared the way Windows will read it: a directory the
    /// Path spells with a variable is still that directory.
    /// </summary>
    [Fact]
    public void An_entry_spelled_with_a_variable_is_the_directory_it_expands_to()
    {
        static string Expand(string entry) => entry.Replace("%LOCALAPPDATA%", @"C:\Users\a\AppData\Local", StringComparison.OrdinalIgnoreCase);

        Assert.True(PathEntry.Carries(@"%USERPROFILE%\bin;%LOCALAPPDATA%\Programs\jason", @"C:\Users\a\AppData\Local\Programs\jason", Expand));
        Assert.Equal(@"%USERPROFILE%\bin", PathEntry.WithoutDirectory(@"%USERPROFILE%\bin;%LOCALAPPDATA%\Programs\jason", @"C:\Users\a\AppData\Local\Programs\jason", Expand));
    }

    /// <summary>
    /// And every entry it keeps is kept as it was written, variables unexpanded. Expanding them on the way
    /// through is what turned an account's whole Path into fixed strings.
    /// </summary>
    [Fact]
    public void The_entries_it_keeps_keep_their_variables()
    {
        static string Expand(string entry) => entry.Replace("%USERPROFILE%", @"C:\Users\a", StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            @"%USERPROFILE%\AppData\Local\Microsoft\WindowsApps;%USERPROFILE%\.local\bin",
            PathEntry.WithoutDirectory(
                @"%USERPROFILE%\AppData\Local\Microsoft\WindowsApps;%USERPROFILE%\.local\bin;C:\Users\a\AppData\Local\Programs\jason",
                @"C:\Users\a\AppData\Local\Programs\jason",
                Expand));
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
