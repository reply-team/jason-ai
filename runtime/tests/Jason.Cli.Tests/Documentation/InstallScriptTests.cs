using System.Text.RegularExpressions;
using Jason.Contracts.Update;

namespace Jason.Cli.Tests.Documentation;

/// <summary>
/// The two install scripts, held to the shape the release gives them to work with. This is a shape guard and
/// says so: no shell script is run here, and what these assertions catch is a name that drifted from
/// <see cref="ReleaseAssets"/> or a step that went missing from the text. The proof that a script installs a
/// real archive and answers with the right version is the CI job that runs each of them against the archives
/// the same run packaged — <c>install.sh</c> on ubuntu, <c>install.ps1</c> on windows — and the last test here
/// is that those jobs exist.
/// </summary>
public partial class InstallScriptTests
{
    private const string Marker = "# added by jason install";

    [Fact]
    public void Both_scripts_exist()
    {
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "install", "install.sh")));
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "install", "install.ps1")));
    }

    /// <summary>The one-liner pipes into <c>sh</c>, so the script has to be one for <c>sh</c> and not for bash.</summary>
    [Fact]
    public void The_shell_script_is_written_for_sh() =>
        Assert.StartsWith("#!/bin/sh", Script("install.sh"), StringComparison.Ordinal);

    /// <summary>
    /// Every platform the code publishes for is named, and no other: a script that offered a fourth would be
    /// offering a download that does not exist. Each script installs on its own platforms and names the others
    /// in the sentence that sends the reader to the other script, which is why both name all three.
    /// </summary>
    [Theory]
    [InlineData("install.sh")]
    [InlineData("install.ps1")]
    public void The_platforms_a_script_names_are_exactly_the_ones_the_code_publishes(string script)
    {
        var text = Script(script);

        var assets = AssetNames().Matches(text).Select(match => match.Value).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(ReleaseAssets.Rids.Select(ReleaseAssets.For).ToHashSet(StringComparer.Ordinal), assets);

        var rids = Rids().Matches(text).Select(match => match.Value).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(ReleaseAssets.Rids.ToHashSet(StringComparer.Ordinal), rids);

        Assert.Contains(ReleaseAssets.Checksums, text, StringComparison.Ordinal);
        Assert.Contains(ReleaseAssets.Manifest, text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The digest is compared before the archive is opened — an archive that fails its checksum is never
    /// unpacked, let alone installed. What can be read from the text is the order: the digest is computed
    /// before the first unpack, on the lines that are code and not on a comment that happens to name both.
    /// </summary>
    [Fact]
    public void Install_sh_checks_the_digest_before_it_unpacks()
    {
        var code = Code("install.sh");
        Assert.True(code.IndexOf("sha256", StringComparison.Ordinal) < code.IndexOf("tar -x", StringComparison.Ordinal), "The checksum is not computed before tar extracts.");
    }

    [Fact]
    public void Install_ps1_checks_the_digest_before_it_unpacks()
    {
        var code = Code("install.ps1");
        Assert.True(code.IndexOf("Get-FileHash", StringComparison.Ordinal) < code.IndexOf("Expand-Archive", StringComparison.Ordinal), "The checksum is not computed before Expand-Archive.");
    }

    /// <summary>PA9: the PATH edit can be declined, the way rustup's can.</summary>
    [Fact]
    public void Both_scripts_can_be_told_to_leave_the_path_alone()
    {
        Assert.Contains("--no-modify-path", Script("install.sh"), StringComparison.Ordinal);
        Assert.Contains("NoModifyPath", Script("install.ps1"), StringComparison.Ordinal);
    }

    /// <summary>Both can be pointed at a different feed — a directory or a base URL — which is how CI runs them with no network.</summary>
    [Fact]
    public void Both_scripts_take_the_feed_override()
    {
        Assert.Contains("JASON_INSTALL_FEED", Script("install.sh"), StringComparison.Ordinal);
        Assert.Contains("JASON_INSTALL_FEED", Script("install.ps1"), StringComparison.Ordinal);
    }

    /// <summary>
    /// PA9: the line in the profile is marked, and the mark is looked for before the line is written, so that a
    /// second run adds nothing. The order in the text is what can be read here; the ubuntu job runs the script
    /// three times and counts the marks.
    /// </summary>
    [Fact]
    public void Install_sh_marks_its_line_in_the_profile_and_looks_for_the_mark_before_writing_it()
    {
        var code = Code("install.sh");
        var looked = code.IndexOf("grep -qF \"$MARKER\"", StringComparison.Ordinal);
        var written = code.IndexOf(">>", StringComparison.Ordinal);

        Assert.Contains($"MARKER=\"{Marker}\"", code, StringComparison.Ordinal);
        Assert.True(looked >= 0 && looked < written, "The profile is not searched for the mark before it is appended to.");
    }

    /// <summary>
    /// Windows keeps the user's PATH in the registry rather than in a profile file, where a directory is its own
    /// mark: the script adds it to the user's PATH only when it is not there. No profile file is edited.
    /// </summary>
    [Fact]
    public void Install_ps1_adds_the_directory_to_the_user_path_only_when_it_is_not_there()
    {
        var text = Script("install.ps1");
        Assert.Contains("'User'", text, StringComparison.Ordinal);
        Assert.Contains("-split ';'", text, StringComparison.Ordinal);
        Assert.DoesNotContain("$PROFILE", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Expand-Archive hands the zip's mark of the web on to what it unpacks, and an executable carrying it is
    /// stopped by SmartScreen the first time it runs. The mark is cleared after unpacking and before installing.
    /// </summary>
    [Fact]
    public void Install_ps1_clears_the_mark_of_the_web()
    {
        var code = Code("install.ps1");
        var unpacked = code.IndexOf("Expand-Archive", StringComparison.Ordinal);
        var unblocked = code.IndexOf("Unblock-File", StringComparison.Ordinal);
        Assert.True(unpacked >= 0 && unpacked < unblocked, "Unblock-File does not follow Expand-Archive.");
    }

    /// <summary>
    /// PB8: Git Bash is not a Linux. `uname -s` there answers MINGW64_NT-… and a script that mapped it to
    /// linux-x64 would download a Linux binary onto Windows. Both spellings are refused by name, with the
    /// sentence that points at the other script.
    /// </summary>
    [Fact]
    public void Install_sh_refuses_git_bash_by_name_and_points_at_the_other_script()
    {
        var text = Script("install.sh");
        Assert.Contains("MINGW*", text, StringComparison.Ordinal);
        Assert.Contains("MSYS*", text, StringComparison.Ordinal);
        Assert.Contains("install.ps1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Install_sh_sets_the_execute_bit() =>
        Assert.Contains("chmod", Script("install.sh"), StringComparison.Ordinal);

    /// <summary>
    /// The unpacked executable is asked for its version before it is renamed into place, so that nothing that
    /// does not start is ever installed over something that did — a Linux without the ICU library the runtime
    /// needs, say, where the program aborts before it prints anything. In the text: the last <c>--version</c>
    /// before the install rename comes after the unpack.
    /// </summary>
    [Theory]
    [InlineData("install.sh", "tar -x", "mv -f")]
    [InlineData("install.ps1", "Expand-Archive", "Move-Item")]
    public void The_unpacked_executable_is_started_before_it_is_installed(string script, string unpack, string install)
    {
        var code = Code(script);
        var unpacked = code.IndexOf(unpack, StringComparison.Ordinal);
        var installed = code.IndexOf(install, unpacked, StringComparison.Ordinal);
        Assert.True(unpacked >= 0 && installed > unpacked, $"{script} does not unpack and then install.");

        var started = code.LastIndexOf("--version", installed, StringComparison.Ordinal);
        Assert.True(started > unpacked, $"{script} does not run the unpacked executable before installing it.");
    }

    /// <summary>The real proof: CI runs each script against the archives the same run packaged, from a local directory.</summary>
    [Fact]
    public void Ci_runs_both_scripts_against_the_archives_it_built()
    {
        var ci = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "workflows", "ci.yml"));
        Assert.Contains("install/install.sh", ci, StringComparison.Ordinal);
        Assert.Contains("install/install.ps1", ci, StringComparison.Ordinal);
        Assert.Contains("JASON_INSTALL_FEED", ci, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"jason-[a-z0-9]+-[a-z0-9]+\.(?:zip|tar\.gz)")]
    private static partial Regex AssetNames();

    [GeneratedRegex(@"\b(?:win|linux|osx)-[a-z0-9]+\b")]
    private static partial Regex Rids();

    private static string Script(string name) => File.ReadAllText(Path.Combine(RepositoryRoot(), "install", name));

    /// <summary>
    /// The script without its comments: the lines that run. Both scripts comment with <c>#</c>, and the
    /// PowerShell one opens with a <c>&lt;# … #&gt;</c> help block; an order asserted on the whole text could be
    /// satisfied, or broken, by a sentence that names a command.
    /// </summary>
    private static string Code(string name)
    {
        var code = new List<string>();
        var inHelp = false;
        foreach (var line in Script(name).Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("<#", StringComparison.Ordinal))
            {
                inHelp = true;
            }

            if (!inHelp && !trimmed.StartsWith('#'))
            {
                code.Add(line);
            }

            if (trimmed.EndsWith("#>", StringComparison.Ordinal))
            {
                inHelp = false;
            }
        }

        return string.Join('\n', code);
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
