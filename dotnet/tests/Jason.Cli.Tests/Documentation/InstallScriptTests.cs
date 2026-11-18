using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Cli.Installation;
using Jason.Cli.Uninstall;
using Jason.Contracts.Json;
using Jason.Contracts.Update;
using Jason.Runtime.Tests;
using Microsoft.Win32;

namespace Jason.Cli.Tests.Documentation;

/// <summary>
/// The two install scripts, held to the shape the release gives them to work with. Most of what is here is a
/// shape guard and says so: what those assertions catch is a name that drifted from <see cref="ReleaseAssets"/>
/// or a step that went missing from the text.
/// <para>
/// The rules that can be <em>run</em> are run instead, because a rule read out of a script's source is only ever
/// a claim about it. <c>install.ps1</c> is PowerShell, so the tests below hand it to <c>pwsh</c> with an
/// environment of their own and a feed of their own and read what it does; they run on Windows, because the
/// script refuses any other platform before it does anything else. <c>install.sh</c> has no such luck — the
/// machine that runs this suite may have no <c>sh</c> at all, and the one Windows has is Git Bash, which the
/// script refuses by name — so its rules here stay shape guards, and the ubuntu job is what runs it. What the two
/// scripts do with the licence is run on each script's own platforms, in <see cref="InstallNoticesTests"/>.
/// </para>
/// <para>
/// The proof that a script installs a real archive and answers with the right version is the CI job that runs
/// each of them against the archives the same run packaged — <c>install.sh</c> on ubuntu, <c>install.ps1</c> on
/// windows — and one test here is that those jobs exist.
/// </para>
/// </summary>
public partial class InstallScriptTests
{
    /// <summary>
    /// Read from the product rather than restated here. <c>jason uninstall</c> undoes what this script wrote,
    /// so the marker has one definition -- <see cref="Jason.Cli.Uninstall.PathEntry.Marker"/> -- and this is
    /// the assertion that the script still spells it that way.
    /// </summary>
    private const string Marker = Jason.Cli.Uninstall.PathEntry.Marker;

    [Fact]
    public void Both_scripts_exist()
    {
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "install", "install.sh")));
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "install", "install.ps1")));
    }

    /// <summary>
    /// A release asset is served as <c>application/octet-stream</c>, and <c>irm</c> decodes such a body as text
    /// byte for byte only because every byte is ASCII: a dash or a quote above 0x7E would reach <c>iex</c> as two
    /// characters of nonsense, and the one-liner would fail on a line nobody wrote.
    /// </summary>
    [Fact]
    public void Install_ps1_is_ascii_because_a_release_serves_it_as_bytes()
    {
        var bytes = File.ReadAllBytes(Path.Combine(RepositoryRoot(), "install", "install.ps1"));
        var above = bytes.Index().Where(pair => pair.Item > 0x7E).Select(pair => pair.Index).ToList();
        Assert.True(above.Count == 0, $"install.ps1 has {above.Count} bytes above 0x7E, the first at offset {above.FirstOrDefault()}.");
    }

    /// <summary>The one-liner pipes into <c>sh</c>, so the script has to be one for <c>sh</c> and not for bash.</summary>
    [Fact]
    public void The_shell_script_is_written_for_sh() =>
        Assert.StartsWith("#!/bin/sh", Script("install.sh"), StringComparison.Ordinal);

    /// <summary>
    /// Every download a script names is one a release publishes, and no other: a script that named another would be
    /// offering a download that does not exist. <c>install.sh</c> installs the Linux archive and, on macOS, Jason Desktop's
    /// zip for the machine's architecture, both of which a release publishes; <c>install.ps1</c> installs the Windows setup.
    /// Neither names another platform's archive but in the sentence that refuses a machine no release is built for.
    /// </summary>
    [Fact]
    public void The_downloads_a_script_names_are_ones_a_release_publishes()
    {
        var sh = Script("install.sh");
        Assert.Equal(new HashSet<string>(StringComparer.Ordinal) { ReleaseAssets.For("linux-x64") }, AssetNames().Matches(sh).Select(match => match.Value).ToHashSet(StringComparer.Ordinal));
        Assert.Contains("ASSET=\"jason-desktop-macos-$ARCH.zip\"", Code("install.sh"), StringComparison.Ordinal);
        foreach (var arch in new[] { "arm64", "x64" })
        {
            Assert.Contains($"jason-desktop-macos-{arch}.zip", ReleaseAssets.Desktop);
            Assert.Contains($"ARCH=\"{arch}\"", Code("install.sh"), StringComparison.Ordinal);
        }

        var ps1 = Script("install.ps1");
        Assert.Empty(AssetNames().Matches(ps1));
        Assert.Contains("$asset = 'jason-desktop-windows-x64-setup.exe'", Code("install.ps1"), StringComparison.Ordinal);
        Assert.Contains("jason-desktop-windows-x64-setup.exe", ReleaseAssets.Desktop);

        foreach (var text in new[] { sh, ps1 })
        {
            Assert.Contains(ReleaseAssets.Checksums, text, StringComparison.Ordinal);
            Assert.Contains(ReleaseAssets.Manifest, text, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The digest is compared before the archive is opened — an archive that fails its checksum is never
    /// unpacked, let alone installed. What can be read from the text is the order: the digest is computed
    /// before the first unpack, on the lines that are code and not on a comment that happens to name both.
    /// </summary>
    [Fact]
    [Critical(Family.I1, "install.sh verifies the checksum first")]
    public void Install_sh_checks_the_digest_before_it_unpacks()
    {
        var code = Code("install.sh");
        Assert.True(code.IndexOf("sha256", StringComparison.Ordinal) < code.IndexOf("tar -x", StringComparison.Ordinal), "The checksum is not computed before tar extracts.");
    }

    /// <summary>
    /// The setup is run only after its digest matches its line in <c>checksums.txt</c> and its signer is the release's
    /// publisher, both on the lines that are code; <see cref="InstallDesktopScriptTests"/> runs both refusals.
    /// </summary>
    [Fact]
    [Critical(Family.I1, "install.ps1 verifies the checksum first")]
    public void Install_ps1_checks_the_digest_and_the_signer_before_it_runs_the_installer()
    {
        var code = Code("install.ps1");
        var run = code.IndexOf("Start-Process -FilePath $setup", StringComparison.Ordinal);
        Assert.True(run > 0, "install.ps1 no longer runs the setup with Start-Process.");
        Assert.InRange(code.IndexOf("Get-FileHash", StringComparison.Ordinal), 0, run);
        Assert.InRange(code.IndexOf("    Assert-Signed $setup", StringComparison.Ordinal), 0, run);
    }

    /// <summary>
    /// PA9: on Linux the PATH edit can be declined, the way rustup's can. Where a script installs Jason Desktop, every
    /// install puts jason in the terminal, so the option is refused by name, before anything is read or written, rather
    /// than taken and not honoured.
    /// </summary>
    [Fact]
    public void The_path_edit_is_declined_on_linux_and_refused_where_jason_desktop_is_installed()
    {
        Assert.Contains("--no-modify-path) MODIFY_PATH=0 ;;", Code("install.sh"), StringComparison.Ordinal);
        Assert.Contains("--no-modify-path is not taken on macOS", Code("install.sh"), StringComparison.Ordinal);
        Assert.Contains("-NoModifyPath is not taken", Code("install.ps1"), StringComparison.Ordinal);
        Assert.Contains("-InstallDir is not taken", Code("install.ps1"), StringComparison.Ordinal);
    }

    /// <summary>Both can be pointed at a different feed — a directory or a base URL — which is how CI runs them with no network.</summary>
    [Fact]
    public void Both_scripts_take_the_feed_override()
    {
        Assert.Contains("JASON_INSTALL_FEED", Script("install.sh"), StringComparison.Ordinal);
        Assert.Contains("JASON_INSTALL_FEED", Script("install.ps1"), StringComparison.Ordinal);
    }

    /// <summary>
    /// A release is fetched from the product's own address, the base <see cref="ProductAddress.Releases"/> names,
    /// which redirects every path under it to the same path in the public repository's releases. Each script holds
    /// that base as one literal and composes both shapes from it, the latest release and a pinned one, so a one-liner
    /// and the runtime's own feed ask the same address.
    /// </summary>
    [Fact]
    public void Both_scripts_fetch_a_release_from_the_product_address()
    {
        var sh = Code("install.sh");
        var ps1 = Code("install.ps1");

        var shBase = Regex.Match(sh, "^RELEASES_URL=\"([^\"]*)\"$", RegexOptions.Multiline);
        var ps1Base = Regex.Match(ps1, @"^\s*\$releasesUrl = '([^']*)'$", RegexOptions.Multiline);
        Assert.True(shBase.Success, "install.sh has no RELEASES_URL of its own.");
        Assert.True(ps1Base.Success, "install.ps1 has no $releasesUrl of its own.");
        Assert.Equal(ProductAddress.Releases, shBase.Groups[1].Value);
        Assert.Equal(ProductAddress.Releases, ps1Base.Groups[1].Value);

        Assert.Contains("BASE=\"$RELEASES_URL/download/v$VERSION\"", sh, StringComparison.Ordinal);
        Assert.Contains("BASE=\"$RELEASES_URL/latest/download\"", sh, StringComparison.Ordinal);
        Assert.Contains("\"$releasesUrl/download/v$Version\"", ps1, StringComparison.Ordinal);
        Assert.Contains("\"$releasesUrl/latest/download\"", ps1, StringComparison.Ordinal);
    }

    /// <summary>
    /// PA9: the line in the profile is marked, and the profile is searched before it is appended to, so that a
    /// second run adds nothing. What is searched for is the whole line the script wrote — the one naming the
    /// install directory — and not the comment above it: that comment is an English sentence a person's own
    /// profile could carry, pasted from somewhere or typed by hand, and keying the decision on it also meant
    /// that a run with a different <c>--install-dir</c> found "the mark", wrote nothing, and left the directory
    /// it had just installed into off the PATH.
    /// </summary>
    /// <remarks>
    /// The order in the text is what can be read here; the ubuntu job runs the script three times and counts
    /// the marks.
    /// </remarks>
    [Fact]
    public void The_path_mark_is_matched_by_the_whole_line_the_script_wrote()
    {
        var code = Code("install.sh");
        var looked = code.IndexOf("grep -qxF \"$LINE\"", StringComparison.Ordinal);
        var written = code.IndexOf(">>", StringComparison.Ordinal);

        Assert.Contains($"MARKER=\"{Marker}\"", code, StringComparison.Ordinal);
        Assert.True(looked >= 0 && looked < written, "The profile is not searched for the line the script wrote before it is appended to.");
        Assert.DoesNotContain("grep -qF \"$MARKER\"", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// And the repair <c>jason status</c> prints appends with the same <c>printf</c> the script appends with.
    /// </summary>
    /// <remarks>
    /// Two spellings of "how a line goes into a profile" is how the verb that undoes an installer drifts away
    /// from it, and a repair is not allowed to be a paraphrase of the script: it is the script's own format,
    /// its own marker and its own line. The repair used to be the bare export line, which lasts one shell.
    /// </remarks>
    [Fact]
    public void The_status_repair_appends_with_the_format_the_script_appends_with()
    {
        var format = Jason.Cli.Uninstall.PathEntry.AppendFormat;

        Assert.Contains($"printf '{format}' \"$MARKER\" \"$LINE\"", Code("install.sh"), StringComparison.Ordinal);
        var repair = Jason.Cli.Uninstall.PathEntry.AppendCommand("/opt/jason/bin", "/home/a/.profile");
        var looked = repair.IndexOf("grep -qxF ", StringComparison.Ordinal);
        var appended = repair.IndexOf($"printf '{format}' ", StringComparison.Ordinal);

        // And, like the script, it looks for the whole line before it appends: typed twice, it adds nothing.
        Assert.True(looked >= 0 && looked < appended, "The repair appends without looking for the line the script would have written.");
    }

    /// <summary>
    /// The Unix repair, typed twice, leaves one block in the profile and the directory once on the PATH.
    /// </summary>
    /// <remarks>
    /// Typed after the installer is the ordinary case rather than the unusual one: an agent standing in the
    /// shell the installer ran in reads <c>path</c> as failed and types what it is told. It appended a second
    /// block, and the export half put the directory on the running PATH twice.
    /// </remarks>
    [Fact]
    public void The_unix_repair_typed_twice_changes_nothing_the_second_time()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The Unix repair is written for sh, on the platforms that print it.");

        using var tree = new TempTree();
        var profile = Path.Combine(tree.NewDirectory("home"), ".profile");
        File.WriteAllText(profile, "# mine\n");
        var command = Jason.Cli.Uninstall.PathEntry.AppendCommand("/opt/jason bin", profile);

        var (exit, stdout, stderr) = Run(
            "/bin/sh",
            new Dictionary<string, string>(),
            "-c", $"{command}; {command}; echo \"$PATH\" | tr ':' '\\n' | grep -cxF '/opt/jason bin'");

        Assert.True(exit == 0, stderr);
        Assert.Equal("1", stdout.Trim());
        Assert.Equal(
            "# mine\n\n" + Marker + "\n" + Jason.Cli.Uninstall.PathEntry.ExportLine("/opt/jason bin") + "\n",
            File.ReadAllText(profile));
    }

    /// <summary>
    /// And with the file an interactive shell reads, as the installer writes it for bash and zsh: typed twice, each file
    /// carries one block, the one that did not exist is made, and the directory is on the PATH once.
    /// </summary>
    [Fact]
    public void The_unix_repair_writes_the_interactive_file_too_and_typed_twice_changes_nothing()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The Unix repair is written for sh, on the platforms that print it.");

        using var tree = new TempTree();
        var home = tree.NewDirectory("home");
        var profile = Path.Combine(home, ".profile");
        var bashrc = Path.Combine(home, ".bashrc");
        File.WriteAllText(profile, "# mine\n");
        var command = Jason.Cli.Uninstall.PathEntry.AppendCommand("/opt/jason bin", profile, bashrc);

        var (exit, stdout, stderr) = Run(
            "/bin/sh",
            new Dictionary<string, string>(),
            "-c", $"{command}; {command}; echo \"$PATH\" | tr ':' '\\n' | grep -cxF '/opt/jason bin'");

        Assert.True(exit == 0, stderr);
        Assert.Equal("1", stdout.Trim());
        var block = Marker + "\n" + Jason.Cli.Uninstall.PathEntry.ExportLine("/opt/jason bin") + "\n";
        Assert.Equal("# mine\n\n" + block, File.ReadAllText(profile));
        Assert.Equal("\n" + block, File.ReadAllText(bashrc));
    }

    /// <summary>
    /// <c>--version X</c> names a release, and the manifest that comes back has to be that release's. Neither
    /// script looked: a feed that answered with another version — a stale mirror, a directory holding the wrong
    /// release, a download URL that resolved to something else — was installed anyway, under the version that
    /// had been asked for, and the only thing that noticed was the executable's own <c>--version</c> line,
    /// compared against the manifest rather than against the request.
    /// </summary>
    /// <remarks>
    /// <c>install.sh</c>'s half of the same rule is read rather than run, for the reason given at the top of
    /// this file; the two scripts' checks are written to say the same thing.
    /// </remarks>
    [Fact]
    public void A_pinned_install_refuses_a_manifest_naming_another_version()
    {
        using var tree = new TempTree();
        var feed = tree.NewDirectory("feed");
        File.WriteAllText(Path.Combine(feed, "manifest.json"), """{ "schema": 1, "version": "0.0.1" }""");

        var (exit, _, stderr) = RunInstallPs1(tree, feed, "-Version", "9.9.9");

        Assert.NotEqual(0, exit);
        Assert.Contains("9.9.9", stderr, StringComparison.Ordinal);
        Assert.Contains("0.0.1", stderr, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same rule in <c>install.sh</c>: compared, and compared before the archive is downloaded. The download is a
    /// function, written above the steps and called from two of them, so what is read is the first call.
    /// </summary>
    [Fact]
    public void Install_sh_compares_the_manifest_with_the_version_that_was_asked_for()
    {
        var lines = Code("install.sh").Split('\n');
        var compared = Array.FindIndex(lines, line => line.Contains("\"$LATEST\"", StringComparison.Ordinal) && line.Contains("\"$VERSION\"", StringComparison.Ordinal));
        var downloaded = Array.FindIndex(lines, line => line.Trim() == "download_archive");

        Assert.Contains(lines, line => line.Contains("fetch \"$ASSET\"", StringComparison.Ordinal));
        Assert.True(compared >= 0, "install.sh never compares the manifest's version with the one --version asked for.");
        Assert.True(downloaded > compared, "install.sh downloads the archive before checking that the manifest names the version that was asked for.");
        Assert.Contains(lines, line => line.Contains("fail ", StringComparison.Ordinal) && line.Contains("$VERSION", StringComparison.Ordinal) && line.Contains("$LATEST", StringComparison.Ordinal));
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

    /// <summary>
    /// <c>install.sh</c> says the two lines on where the files will be with <c>printf</c>, each text in single quotes so
    /// that its backticks are text rather than a command to substitute, the folder being <c>Jason</c> in the home as the
    /// runtime makes it. Read here because no Windows runs the script; Linux and macOS run what it prints, in
    /// <see cref="InstallNoticesTests"/>.
    /// </summary>
    [Fact]
    public void Install_sh_prints_where_the_files_will_be_as_text()
    {
        var code = Code("install.sh");

        Assert.Contains($"printf '{HarnessRootLines.Where("%s")}\\n' \"$ROOT_HOME/Jason\"", code, StringComparison.Ordinal);

        // The home loses every separator at its end, not one: `/home/marta//` is the runtime's `/home/marta` too.
        Assert.Contains("ROOT_HOME=\"$HOME\"\nwhile [ \"${ROOT_HOME%/}\" != \"$ROOT_HOME\" ]; do\n    ROOT_HOME=\"${ROOT_HOME%/}\"\ndone\n", code, StringComparison.Ordinal);
        Assert.Contains($"printf '%s\\n' '{HarnessRootLines.DefaultProject}'", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Install_sh_sets_the_execute_bit() =>
        Assert.Contains("chmod", Script("install.sh"), StringComparison.Ordinal);

    /// <summary>
    /// The unpacked executable is asked for its version before it is renamed into place, so that nothing that
    /// does not start is ever installed over something that did — a Linux without the ICU library the runtime
    /// needs, say, where the program aborts before it prints anything. In the text: the last <c>--version</c>
    /// before the executable's own rename into place comes after the executable's own unpack. Those two lines are
    /// named whole: each script now also unpacks and renames its notice files, in functions above the steps.
    /// </summary>
    [Theory]
    [InlineData("install.sh", "tar -xzf \"$TMP/$ASSET\" -C \"$TMP/unpacked\" jason", "mv -f \"$INSTALL_DIR/jason.installing.$$\" \"$TARGET\"")]
    public void The_unpacked_executable_is_started_before_it_is_installed(string script, string unpack, string install)
    {
        var code = Code(script);
        var unpacked = code.IndexOf(unpack, StringComparison.Ordinal);
        var installed = code.IndexOf(install, unpacked, StringComparison.Ordinal);
        Assert.True(unpacked >= 0 && installed > unpacked, $"{script} does not unpack and then install.");

        var started = code.LastIndexOf("--version", installed, StringComparison.Ordinal);
        Assert.True(started > unpacked, $"{script} does not run the unpacked executable before installing it.");
    }

    /// <summary>
    /// And CI runs the published executable's own uninstall, from the file it removes, on every platform it
    /// packages for.
    /// </summary>
    /// <remarks>
    /// The one shape the suite cannot reach: it runs as a test host, not as a single-file build, and only a
    /// single-file build loses the ability to load a new assembly once its own file has been moved or deleted.
    /// A published build removed everything and then answered with one line about a type initializer; every
    /// test of this verb was green.
    /// </remarks>
    [Fact]
    public void Ci_uninstalls_the_published_executable_from_its_own_image()
    {
        var ci = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "workflows", "ci.yml"));
        var step = ci.IndexOf("- name: Uninstall the published executable, from its own image", StringComparison.Ordinal);

        Assert.True(step >= 0, "ci.yml no longer uninstalls the published executable.");
        Assert.True(ci.IndexOf("steps.package.outputs.executable", step, StringComparison.Ordinal) > step, "the uninstall step does not run the executable the job packaged.");
        Assert.True(ci.IndexOf("uninstall --purge-data --yes", step, StringComparison.Ordinal) > step, "the uninstall step does not purge, which is the shape that failed.");

        // And it refuses, before anything is removed, wherever the account has something of its own: the logon
        // registration and the harnesses are the account's, and this step is rehearsed on developer machines.
        var guard = ci.IndexOf("$plan.autostart_registered -or @($plan.roots).Count -gt 0 -or $plan.path_entry.ours", step, StringComparison.Ordinal);
        Assert.True(guard > step && guard < ci.IndexOf("uninstall --purge-data --yes", step, StringComparison.Ordinal), "the uninstall step no longer refuses on an account with a registration, recorded skills or a PATH entry.");
    }

    /// <summary>
    /// On Windows that step waits for the cleanup the uninstall names, by its process, before it looks for the moved copy.
    /// The cleanup's own retries begin only once the verb has exited and Windows PowerShell has started, so a wait of the
    /// same length counted from the verb's exit could end while the cleanup was still within its own. The step reads the
    /// process and the directory out of what the uninstall says, so what it reads is held here against the sentence the
    /// uninstall writes.
    /// </summary>
    [Fact]
    public void Ci_waits_for_the_cleanup_the_uninstall_names_before_it_looks_for_the_moved_copy()
    {
        var ci = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "workflows", "ci.yml"));
        var start = ci.IndexOf("- name: Uninstall the published executable, from its own image", StringComparison.Ordinal);
        Assert.True(start >= 0, "ci.yml no longer uninstalls the published executable.");
        var next = Regex.Match(ci[(start + 1)..], @"\n      - name: |\n  [a-z]");
        var step = next.Success ? ci.Substring(start, next.Index + 1) : ci[start..];

        var read = Regex.Match(step, @"\[regex\]::Match\(\(\$report\.kept -join ' '\), ""(?<pattern>[^""]+)""\)");
        Assert.True(read.Success, $"The uninstall step no longer reads the report's notes with one pattern:\n{step}");
        var pattern = read.Groups["pattern"].Value;

        using var dir = new TempPaths();
        var executable = Path.Combine(Directory.CreateDirectory(Path.Combine(dir.Paths.Root, "install")).FullName, "jason.exe");
        File.WriteAllText(executable, "an executable");
        var temporary = Directory.CreateDirectory(Path.Combine(dir.Paths.Root, "tmp")).FullName;
        var outcome = InstallationRemovers.MoveAside(executable, temporary, (from, to) => File.Move(from, to), _ => 4242);

        var found = Regex.Match(outcome.Note!, pattern);
        Assert.True(found.Success && found.Groups.Count == 3, $"The step's pattern \"{pattern}\" does not read a process and a directory out of what the uninstall says: {outcome.Note}");
        Assert.Equal("4242", found.Groups[1].Value);
        Assert.Equal(Path.GetDirectoryName(outcome.MovedTo), found.Groups[2].Value);

        var waits = step.IndexOf(".WaitForExit(", StringComparison.Ordinal);
        var looks = step.LastIndexOf("Test-Path -LiteralPath $aside", StringComparison.Ordinal);
        Assert.True(waits > read.Index && waits < looks, $"The uninstall step does not wait for the cleanup's process between reading it ({read.Index}) and looking for the moved copy ({looks}); it waits at {waits}.");
    }

    /// <summary>
    /// No artifact in CI but the three platform ones is named so that the install jobs' download takes it.
    /// </summary>
    /// <remarks>
    /// They download <c>jason-*</c> and merge what they get into one directory. The update job's second version
    /// was uploaded as <c>jason-&lt;rid&gt;-next</c>, carrying an archive with the same file name as the real
    /// one; when it landed before an install job's download — which it did once the platform jobs grew a step —
    /// its archive went over the real one beside the other artifact's fragment, and the feed refused itself.
    /// </remarks>
    [Fact]
    public void Only_the_platform_artifacts_are_named_like_the_ones_the_install_jobs_download()
    {
        var ci = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "workflows", "ci.yml"));
        Assert.Contains("pattern: jason-*", ci, StringComparison.Ordinal);

        var named = ci.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("artifact:", StringComparison.Ordinal))
            .Select(line => line["artifact:".Length..].Trim())
            .ToList();

        Assert.NotEmpty(named);
        Assert.All(named, name => Assert.False(
            name.StartsWith("jason-", StringComparison.Ordinal),
            $"ci.yml uploads '{name}', which the install jobs' `jason-*` download would merge over a platform archive."));
    }

    /// <summary>
    /// And CI holds the Windows installer to the kind of the account's Path on a real machine: the one proof of
    /// that installer's PATH edit which is not against a registry key a test made for itself.
    /// </summary>
    /// <remarks>
    /// The agent run on a fresh account cannot give it: the prompt's one-liner fetches the installer from
    /// <c>main</c>, so a branch that changes the installer is installed there with the old one.
    /// </remarks>
    [Fact]
    public void Ci_holds_the_installer_to_the_kind_of_the_accounts_path()
    {
        var ci = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "workflows", "ci.yml"));
        var job = ci.IndexOf("name: install.ps1 on windows", StringComparison.Ordinal);

        Assert.True(job >= 0, "ci.yml no longer runs install.ps1 on Windows.");
        // A step of its own, named, gives the account's Path the shape an ordinary account has before the install leg:
        // ExpandString, with an entry spelled through a variable. The install leg holds both by name.
        var shape = ci.IndexOf("- name: Give the account's Path the shape an ordinary account has, ExpandString with an entry spelled through a variable", job, StringComparison.Ordinal);
        var install = ci.IndexOf("Test-InstallationWindows.ps1 -Step Install ", job, StringComparison.Ordinal);
        Assert.True(shape > job, "the install.ps1 job no longer gives the account's Path its ExpandString shape in a step of its own.");
        Assert.True(ci.IndexOf("-Step PathShape", shape, StringComparison.Ordinal) is var runs && runs > shape && runs < install, "the Path's shape is not given by the proof's PathShape step before the install leg.");
        var script = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "scripts", "Test-InstallationWindows.ps1"));
        Assert.Contains("'DoNotExpandEnvironmentNames'", script, StringComparison.Ordinal);
        Assert.Contains("$keptEntry = '%USERPROFILE%\\jason-ci-kept'", script, StringComparison.Ordinal);
        Assert.Contains("[Microsoft.Win32.RegistryValueKind]::ExpandString", script, StringComparison.Ordinal);
        Assert.Contains("the install turned the user Path from ExpandString into $($after.Kind)", script, StringComparison.Ordinal);
        Assert.Contains("the install expanded or dropped the user Path entry '$keptEntry'", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// PowerShell names ignore case, and a script parameter keeps its type for every assignment made at the script's own
    /// scope: a local given a parameter's name is converted to the parameter's type, and throws, as a result object
    /// assigned to a switch does, or turns into a string, and every check on its members passes for nothing. The Windows
    /// proof script writes no variable whose name, compared case-insensitively, is one of its script parameters: no
    /// assignment, compound or not, no increment or decrement, no foreach loop variable, with or without the script: scope.
    /// It is read line by line, so a function's own local of such a name is refused as well, which keeps the rule simple;
    /// only a function's own signature line, whose parameters may take defaults, is not read.
    /// </summary>
    [Fact]
    public void The_Windows_proof_script_assigns_no_variable_named_as_one_of_its_parameters()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "scripts", "Test-InstallationWindows.ps1")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var block = Regex.Match(text, @"^param\(\n(?<body>.*?)\n\)\n", RegexOptions.Multiline | RegexOptions.Singleline);
        Assert.True(block.Success, "Test-InstallationWindows.ps1 no longer opens with one param() block.");
        var names = Regex.Matches(block.Groups["body"].Value, @"\]\s*\$(?<name>[A-Za-z_][A-Za-z0-9_]*)")
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Step", names);
        Assert.Contains("Again", names);
        var first = text[..(block.Index + block.Length)].Count(character => character == '\n') + 1;
        string[] writes =
        [
            @"(?<![\w$`])\$(?:script:)?(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:=|\+=|-=|\*=|/=)(?!=)",
            @"(?<![\w$`])\$(?:script:)?(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:\+\+|--)",
            @"(?:\+\+|--)\$(?:script:)?(?<name>[A-Za-z_][A-Za-z0-9_]*)",
            @"(?i)\bforeach\s*\(\s*\$(?:script:)?(?<name>[A-Za-z_][A-Za-z0-9_]*)\s+in\b",
        ];
        var found = text[(block.Index + block.Length)..].Split('\n')
            .Select((line, at) => (line, at))
            .Where(item => !item.line.TrimStart().StartsWith("function ", StringComparison.OrdinalIgnoreCase))
            .SelectMany(item => writes.SelectMany(write => Regex.Matches(item.line, write))
                .Where(match => names.Contains(match.Groups["name"].Value))
                .Select(match => $"line {first + item.at}: {item.line.Trim()}"))
            .Distinct()
            .ToList();
        Assert.True(found.Count == 0, "Test-InstallationWindows.ps1 assigns a variable named as one of its parameters: " + string.Join("; ", found));
    }

    /// <summary>
    /// A Windows leg that fails part way, or whose step is ended by its timeout, never keeps the next one from running
    /// with what it made: every place a leg refuses to find there already is one of the places a clean begin takes
    /// (Take-Scratch), and every helper process the script starts - a pwsh or a python of its own - is either waited for
    /// at once, failing on a timeout, or recorded at its start (Register-Scratch) for a clean begin to stop by its id. A
    /// clean begin takes the scratch and, after a leg of the previous release, the updater's folder that release keeps,
    /// before it reads the machine, and moves the harness root aside after its reset, before it records what it found.
    /// </summary>
    [Fact]
    public void A_clean_Windows_begin_takes_every_place_and_helper_a_leg_can_leave()
    {
        var windows = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "scripts", "Test-InstallationWindows.ps1")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var placesLine = Regex.Match(windows, @"(?m)^\$scratchPlaces = @\((?<names>[^)]*)\)$");
        Assert.True(placesLine.Success, "Test-InstallationWindows.ps1 no longer names its scratch places in one $scratchPlaces line.");
        var places = Regex.Matches(placesLine.Groups["names"].Value, @"\$(?<name>scratch\w+)").Select(match => match.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);
        var wrong = new List<string>();

        // Each place a refusal reads, by the assignment nearest before it.
        var refused = Regex.Matches(windows, @"Fail ""\$(?<name>\w+) is there before ").ToList();
        Assert.NotEmpty(refused);
        foreach (var refusal in refused)
        {
            var name = refusal.Groups["name"].Value;
            var assigned = Regex.Matches(windows[..refusal.Index], $@"(?m)^\s*\${name} = (?<value>.+)$").LastOrDefault()?.Groups["value"].Value.Trim();
            if (assigned is null) wrong.Add($"${name} is refused and never assigned before");
            else if (!Regex.IsMatch(assigned, @"^\$scratch\w+$") || !places.Contains(assigned[1..])) wrong.Add($"${name}, {assigned}, is refused where it is found, and is not one of the places Take-Scratch takes");
        }

        // Each helper the script starts: recorded on the next line, or waited for at once with a bound that fails.
        var lines = windows.Split('\n');
        var helpers = 0;
        for (var at = 0; at < lines.Length; at++)
        {
            if (!Regex.IsMatch(lines[at], @"\bStart-Process (?:pwsh|python)\b")) continue;
            helpers++;
            var started = Regex.Match(lines[at], @"^\s*\$(?<name>\w+) = Start-Process ");
            if (!started.Success) { wrong.Add($"line {at + 1} starts a helper and keeps no process to record or wait for: {lines[at].Trim()}"); continue; }
            var name = started.Groups["name"].Value;
            var next = at + 1 < lines.Length ? lines[at + 1] : string.Empty;
            var recorded = Regex.IsMatch(next, $@"^\s*Register-Scratch (?:'[^']*(?:''[^']*)*'|""[^""]*"") \${name}$");
            var waited = next.Trim() == $"$null = ${name}.Handle" && at + 2 < lines.Length && lines[at + 2].Contains($"if (-not ${name}.WaitForExit(", StringComparison.Ordinal) && lines[at + 2].Contains("Fail ", StringComparison.Ordinal);
            if (!recorded && !waited) wrong.Add($"line {at + 1} starts a helper, ${name}, that is neither recorded by Register-Scratch on the next line nor waited for at once: {lines[at].Trim()}");
        }

        Assert.True(helpers >= 6, $"Test-InstallationWindows.ps1 starts {helpers} helper(s) by pwsh or python; the row reads too few to mean anything.");
        Assert.True(wrong.Count == 0, string.Join("; ", wrong));

        // The order of a clean begin.
        var begin = windows[windows.IndexOf("\n    'Begin' {\n", StringComparison.Ordinal)..];
        begin = begin[..begin.IndexOf("\n    'End' {\n", StringComparison.Ordinal)];
        var clean = begin.IndexOf("\n        if ($Clean) {\n", StringComparison.Ordinal);
        var take = begin.IndexOf("\n            if (-not (Take-Scratch $Leg)) { return }\n", StringComparison.Ordinal);
        var prior = begin.IndexOf("\n            $prior = ", StringComparison.Ordinal);
        var cache = begin.IndexOf("\n            if (-not (Take-UpdaterCache $Leg $prior)) { return }\n", StringComparison.Ordinal);
        var read = begin.IndexOf("\n            $found = @(Get-LeftOver)\n", StringComparison.Ordinal);
        var resetCache = begin.IndexOf("$null = Take-UpdaterCache $Leg $last", StringComparison.Ordinal);
        var root = begin.IndexOf("Move-Item -LiteralPath $harnessRoot -Destination $aside", StringComparison.Ordinal);
        var inherited = begin.IndexOf("Write-Inherited $prior 'leg-start'", StringComparison.Ordinal);
        Assert.True(clean >= 0 && take > clean && prior > take && cache > prior && read > cache, "a clean Windows begin does not take the scratch, then the updater's folder by the leg before it, before it reads the machine");
        Assert.True(resetCache > read && root > resetCache && inherited > root, "a clean Windows begin does not take the updater's folder after its reset's uninstalls, and move the harness root aside, before it records what it found");
        Assert.DoesNotContain("$before049", windows, StringComparison.Ordinal);
        var updater = windows[windows.IndexOf("\nfunction Take-UpdaterCache(", StringComparison.Ordinal)..];
        updater = updater[..updater.IndexOf("\n}\n", StringComparison.Ordinal)];
        Assert.Contains("if ($previousLegs -cnotcontains $by) { return $true }", updater, StringComparison.Ordinal);
    }

    /// <summary>
    /// A clean Windows begin stops a helper an earlier leg left by its recorded id alone, and only while that id still runs
    /// the command line recorded at its start; it signals the holder and the reader first, so they let go by themselves; and
    /// it puts the person's own quick-start back over a stand-in before it removes any place, never removing the folder it
    /// was moved out to while the person's copy is still in it.
    /// </summary>
    [Fact]
    public void A_clean_Windows_begin_stops_only_the_helpers_it_recorded_and_puts_the_person_s_own_quick_start_back_first()
    {
        var windows = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "scripts", "Test-InstallationWindows.ps1")).Replace("\r\n", "\n", StringComparison.Ordinal);
        string Function(string name)
        {
            var body = windows[windows.IndexOf($"\nfunction {name}", StringComparison.Ordinal)..];
            return body[..body.IndexOf("\n}\n", StringComparison.Ordinal)];
        }

        var running = Function("Get-RunningScratch");
        Assert.Contains("if (-not $record.command_line) { continue }", running, StringComparison.Ordinal);
        Assert.Contains("\"$($now[0].CommandLine)\" -ceq $record.command_line", running, StringComparison.Ordinal);
        var take = Function("Take-Scratch");
        Assert.Single(Regex.Matches(take, @"\bStop-Process\b"));
        Assert.Contains("foreach ($record in $stopped) {\n            Stop-Process -Id ([int]$record.pid) -Force", take, StringComparison.Ordinal);
        var signals = take.IndexOf("foreach ($signal in $scratchSignals) { New-Item", StringComparison.Ordinal);
        var stop = take.IndexOf("Stop-Process -Id", StringComparison.Ordinal);
        var check = take.IndexOf("if (Test-Path -LiteralPath $outSkill -PathType Container) {", StringComparison.Ordinal);
        var standIn = take.IndexOf("Remove-Item -LiteralPath $ownSkill -Recurse -Force", StringComparison.Ordinal);
        var back = take.IndexOf("Move-Item -LiteralPath $outSkill -Destination $ownSkill", StringComparison.Ordinal);
        var kept = take.IndexOf("if (Test-Path -LiteralPath $outSkill) { Skip-Leg \"not run: the person's own quick-start could not be put back", StringComparison.Ordinal);
        var removal = take.IndexOf("foreach ($place in $scratchPlaces) {\n        if (-not (Test-Path -LiteralPath $place)) { continue }\n", StringComparison.Ordinal);
        Assert.True(signals >= 0 && stop > signals, "Take-Scratch does not signal the holder and the reader before it stops a helper");
        Assert.True(check > stop && standIn > check && back > standIn && kept > back && removal > kept, "Take-Scratch does not remove the stand-in, then put the person's copy back, and stop where it could not, before it removes any place");
        Assert.Contains("$outside = $scratchOwnOut", windows, StringComparison.Ordinal);
        Assert.Contains("$release = $scratchHeldFile", windows, StringComparison.Ordinal);
        Assert.Contains("$stopReading = $scratchStopReading", windows, StringComparison.Ordinal);
    }

    /// <summary>
    /// The Windows leg of silent setups ends with the previous release in place (its sub-leg 5); this build is put back
    /// over it by its own silent setup before the leg's closing uninstall, so that uninstall is this build's and the leg's
    /// compare excuses nothing the previous release's own uninstall keeps.
    /// </summary>
    [Fact]
    public void The_Windows_silent_setups_leg_puts_this_build_back_before_its_closing_uninstall()
    {
        var windows = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "scripts", "Test-InstallationWindows.ps1")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var silent = windows[windows.IndexOf("\n    'SilentUpdateLegs' {\n", StringComparison.Ordinal)..];
        silent = silent[..silent.IndexOf("\n    'UninstallWaiterShown' {\n", StringComparison.Ordinal)];
        var fifth = silent.IndexOf("$null = Test-Leg 'leg 5, ", StringComparison.Ordinal);
        var back = silent.IndexOf("$null = Test-Leg 'this build put back, --updated /S over the previous release not running' @('--updated', '/S') @(0)", StringComparison.Ordinal);
        var version = silent.IndexOf("if ((Read-Version $carried) -ne $Version) { $failures.Add(\"after this build was put back", StringComparison.Ordinal);
        var end = silent.IndexOf("if ($failures.Count -gt 0) { Fail ($failures -join '; ') }", StringComparison.Ordinal);
        Assert.True(fifth >= 0 && back > fifth && version > back && end > version, "the silent setups leg does not put this build back after its sub-leg 5 and before it ends");
        // Sub-leg 5's holder is released and waited for even where the sub-leg throws, before the put-back.
        var cleared = silent.IndexOf("\n        $holder = $null\n        try {\n            From-Previous $false\n", StringComparison.Ordinal);
        var released = silent.IndexOf("} finally {\n            # Released, and waited for as the sub-leg's own release waits, so the put-back below never meets the file held.\n            New-Item -ItemType File -Path $scratchHeldFile -Force | Out-Null\n            if ($holder) { $null = $holder.WaitForExit(15000) }\n        }\n", StringComparison.Ordinal);
        Assert.True(cleared >= 0 && fifth > cleared && released > fifth && back > released, "sub-leg 5 does not release its holder and wait for it in its finally, before the put-back");
        var ci = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "workflows", "ci.yml")).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains("-Step SilentUpdateLegs -Out $env:OUT -Feed $env:FEED -Version $env:VERSION -Previous $env:PREVIOUS\n          ./.github/scripts/Test-InstallationWindows.ps1 -Step UninstallFromInstalledApps -Out $env:OUT\n", ci, StringComparison.Ordinal);
    }

    /// <summary>
    /// The Windows leg that opens the window while jason uninstall runs, so the waiter asks, holds the verb where the window
    /// can open rather than hoping for the moment: a skill file the verb removes after it stops the runtime is held open,
    /// sharing read alone, before the verb starts, and the hold is seen refusing a writer; the verb then tries that file
    /// again for its wait, and the leg lets go only once the window's process runs and the verb has not ended. A verb that
    /// ended before that fails the leg, and so does a held file still there after the verb, so the leg cannot end green
    /// having proven nothing; where the window was opened, the leg first closes it and ends every uninstaller run the
    /// waiter shows, so the next leg meets neither.
    /// </summary>
    [Fact]
    public void The_Windows_waiter_leg_holds_the_verb_while_the_window_opens_and_cannot_end_green_having_proven_nothing()
    {
        var windows = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "scripts", "Test-InstallationWindows.ps1")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var leg = windows[windows.IndexOf("\n    'UninstallWaiterShown' {\n", StringComparison.Ordinal)..];
        leg = leg[..leg.IndexOf("\n    'UpdateApply' {\n", StringComparison.Ordinal)];

        // The hold, the silent leg's own: a file of this build's skills record, held sharing read alone, seen refusing a writer.
        var record = leg.IndexOf("$skillsRecord = Join-Path $env:USERPROFILE '.claude\\skills\\.jason-skills.json'", StringComparison.Ordinal);
        var holder = leg.IndexOf("$holder = Start-Process pwsh -ArgumentList '-NoProfile', '-Command', \"`$f = [System.IO.File]::Open('$held', 'Open', 'Read', 'Read'); while (-not (Test-Path -LiteralPath '$release')) { Start-Sleep -Milliseconds 200 }; `$f.Dispose()\" -PassThru -WindowStyle Hidden", StringComparison.Ordinal);
        var registered = leg.IndexOf("Register-Scratch ", StringComparison.Ordinal);
        var refusing = leg.IndexOf("[System.IO.File]::Open($held, 'Open', 'ReadWrite', 'ReadWrite')", StringComparison.Ordinal);
        var verb = leg.IndexOf("$verb = Start-Process -FilePath $carried -ArgumentList 'uninstall', '--yes' -PassThru -WindowStyle Hidden", StringComparison.Ordinal);
        Assert.True(record >= 0 && holder > record && registered > holder && refusing > registered && verb > refusing, "the waiter leg does not hold a file of this build's skills, recorded, and see the hold refuse a writer, before jason uninstall starts");

        // Past the stop, the window opened; the verb must still run once the window's process does, and only then is it let go.
        var stopped = leg.IndexOf("Wait-Until { -not (Test-Path -LiteralPath $descriptor) -or $verb.HasExited }", StringComparison.Ordinal);
        var early = leg.IndexOf("if ($verb.HasExited) { Fail ", StringComparison.Ordinal);
        var window = leg.IndexOf("Start-Process -FilePath (Join-Path $app 'Jason.exe') | Out-Null", StringComparison.Ordinal);
        var seen = leg.IndexOf("Wait-Until { $verb.HasExited -or @(Get-OwnProcesses $app | Where-Object { $_.Name -ceq 'Jason.exe' }).Count -gt 0 }", StringComparison.Ordinal);
        var landed = leg.IndexOf("$landed = -not $verb.HasExited", StringComparison.Ordinal);
        var release = leg.IndexOf("New-Item -ItemType File -Path $release -Force | Out-Null", StringComparison.Ordinal);
        var unlanded = leg.IndexOf("$noProof = if (-not $landed) { \"", StringComparison.Ordinal);
        var ended = leg.IndexOf("if (-not $verb.WaitForExit(300000)) { Fail ", StringComparison.Ordinal);
        var taken = leg.IndexOf("if (-not $noProof -and (Test-Path -LiteralPath $held)) { $noProof = \"", StringComparison.Ordinal);
        Assert.True(stopped > verb && early > stopped && window > early && seen > window && landed > seen && release > landed && unlanded > release && ended > unlanded && taken > ended,
            "the waiter leg does not open the window past the stop, see its process while the verb still runs, let go only then, and keep the reason where the verb ended first or the held file outlived the verb");
        Assert.DoesNotContain("Start-Sleep", leg[verb..release], StringComparison.Ordinal);
        Assert.DoesNotContain("if (-not $landed) { Fail ", leg, StringComparison.Ordinal);
        Assert.DoesNotContain("if (Test-Path -LiteralPath $held) { Fail ", leg, StringComparison.Ordinal);

        // A leg that proved nothing still cleans up before it fails by that reason: the window closed, then while the waiter
        // this verb left still runs, every uninstaller run it shows ended by its id; the shown run is looked for only after.
        var cleanUp = leg.IndexOf("        if ($noProof) {\n", StringComparison.Ordinal);
        var closed = leg.IndexOf("Get-OwnProcesses $app | Where-Object { $_.Name -ceq 'Jason.exe' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }", cleanUp, StringComparison.Ordinal);
        var waiter = leg.IndexOf("$waiterLine = \"Wait-Process -Id $($verb.Id) -Timeout 600;\"", cleanUp, StringComparison.Ordinal);
        var shownEnded = leg.IndexOf("$_.Name -ceq 'Uninstall Jason.exe' -and $_.CommandLine -notmatch '(^|\\s)/S(\\s|$)' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }", cleanUp, StringComparison.Ordinal);
        var failed = leg.IndexOf("            Fail $noProof\n        }\n", cleanUp, StringComparison.Ordinal);
        var looked = leg.IndexOf("        $shown = $null\n", StringComparison.Ordinal);
        Assert.True(cleanUp > taken && closed > cleanUp && waiter > closed && shownEnded > waiter && failed > shownEnded && looked > failed,
            "the waiter leg does not close the window and end the uninstaller runs the waiter shows, while the waiter runs, before it fails by the reason it kept, and before it looks for the shown run");

        // Let go and waited for even where the leg fails.
        Assert.Contains("} finally {\n            New-Item -ItemType File -Path $scratchHeldFile -Force | Out-Null\n            if ($holder) { $null = $holder.WaitForExit(15000) }\n        }", leg, StringComparison.Ordinal);
    }

    /// <summary>
    /// The Windows proof reads whether a process it watched ended once, after its loop, by the reading the loop used.
    /// <c>HasExited</c> answers by the exit code Windows reports, which can come before the process's handle is signalled,
    /// so a zero wait asked after a loop on it can say no of a process the loop saw end, and the step then fails a process
    /// that ended and tries to end it. No loop on <c>HasExited</c> is followed by a zero wait on the same process; the
    /// setup's watcher takes its one reading from <c>HasExited</c>, waits for the handle, bounded, before it reads the exit
    /// code where the process ended, and ends the process only where it did not.
    /// </summary>
    [Fact]
    public void The_Windows_proof_reads_once_whether_a_process_it_watched_ended()
    {
        var windows = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "scripts", "Test-InstallationWindows.ps1")).Replace("\r\n", "\n", StringComparison.Ordinal);

        // Every loop on a process's HasExited, its own or Wait-Until's, and what follows it to the end of its function: no
        // zero wait on that process.
        var loops = Regex.Matches(windows, @"\n[ ]*(?:while \(|Wait-Until \{)(?<condition>[^\n]*?\$(?<name>\w+)\.HasExited[^\n]*)");
        Assert.True(loops.Count >= 3, $"Test-InstallationWindows.ps1 has {loops.Count} loop(s) on HasExited; the row reads too few to mean anything.");
        foreach (Match loop in loops)
        {
            var after = windows[(loop.Index + loop.Length)..];
            var end = after.IndexOf("\n}\n", StringComparison.Ordinal);
            after = end >= 0 ? after[..end] : after;
            Assert.False(Regex.IsMatch(after, @"\$" + Regex.Escape(loop.Groups["name"].Value) + @"\.WaitForExit\(\s*0\s*\)"),
                $"a loop on ${loop.Groups["name"].Value}.HasExited is followed by a zero wait on the same process, a second reading of whether it ended: {loop.Groups["condition"].Value}");
        }
        Assert.DoesNotMatch(@"\.WaitForExit\(\s*0\s*\)", windows);

        var watch = windows[windows.IndexOf("\nfunction Watch-Setup(", StringComparison.Ordinal)..];
        watch = watch[..watch.IndexOf("\n}\n", StringComparison.Ordinal)];
        var loopAt = watch.IndexOf("    while (-not $process.HasExited -and ", StringComparison.Ordinal);
        var read = watch.IndexOf("\n    $ended = $process.HasExited\n", StringComparison.Ordinal);
        var settled = watch.IndexOf("\n    if ($ended -and -not $process.WaitForExit(10000)) { ", StringComparison.Ordinal);
        var code = watch.IndexOf("Exit = $(if ($ended) { $process.ExitCode } else { $null })", StringComparison.Ordinal);
        var notEnded = watch.IndexOf("\n    if (-not $ended) {\n", StringComparison.Ordinal);
        var stop = watch.IndexOf("Stop-Process -Id $process.Id", StringComparison.Ordinal);
        Assert.True(loopAt >= 0 && read > loopAt && settled > read && code > settled && notEnded > code && stop > notEnded,
            "Watch-Setup does not read once, after its loop, by HasExited whether the setup ended, wait for its handle where it did before reading its exit code, and end it only where it did not");
        Assert.Single(Regex.Matches(watch, @"\$ended = "));
        Assert.Single(Regex.Matches(watch, @"Stop-Process"));
    }

    /// <summary>
    /// The line over a running command-line install on Windows: the setup's own <c>jason install</c> retires that install and
    /// stops its runtime, and its report is kept nowhere, so the leg holds of the line's own <c>jason install --roles</c> what
    /// is true of it - no runtime running, nothing retired - and the sentence install.ps1 says when it starts a runtime again,
    /// read from install.ps1 itself; the old runtime, by its process and its start, and the old executable both gone, with no
    /// order claimed; and it says that the order is held by the set-up's own rows, not by this leg.
    /// </summary>
    [Fact]
    public void The_Windows_line_over_a_running_command_line_holds_of_its_own_report_what_is_true_of_it()
    {
        var root = RepositoryRoot();
        var windows = File.ReadAllText(Path.Combine(root, ".github", "scripts", "Test-InstallationWindows.ps1")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var script = File.ReadAllText(Path.Combine(root, "install", "install.ps1")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var leg = windows[windows.IndexOf("\n    'InstallOverCommandLine' {\n", StringComparison.Ordinal)..];
        leg = leg[..leg.IndexOf("\n    'OverBothPrevious' {\n", StringComparison.Ordinal)];

        Assert.DoesNotContain("-cne 'stopped'", leg, StringComparison.Ordinal);
        Assert.Contains("if ([string]$report.runtime -cne 'not_running') { Fail ", leg, StringComparison.Ordinal);
        Assert.Contains("if ($retired -and $retired.Value) { Fail ", leg, StringComparison.Ordinal);

        // The started-again sentence, as install.ps1 says it, with the application's jason in place of its variable.
        var said = Regex.Match(script, @"Write-Host ""(?<sentence>A runtime answered before the install and none answers now, so one is started again from )\$from\.""");
        Assert.True(said.Success, "install.ps1 no longer says, in one sentence, that it starts a runtime again from $from.");
        Assert.Contains($"$startedAgain = \"{said.Groups["sentence"].Value}$carried.\"", leg, StringComparison.Ordinal);
        Assert.Contains("if (-not $over.Said.Contains($startedAgain)) { Fail ", leg, StringComparison.Ordinal);

        // Both gone, no order claimed, and the order said to be the set-up's own rows'.
        Assert.Contains("@($oldRuntimes | Where-Object { Test-SameProcess $_ })", leg, StringComparison.Ordinal);
        Assert.Contains("if (Test-Path -LiteralPath $jason) { Fail ", leg, StringComparison.Ordinal);
        Assert.Contains("is held by MachineSetup's own rows, not by this leg", leg, StringComparison.Ordinal);
    }

    /// <summary>
    /// Going back to the previous release on Windows leaves that release's own skills record, which its uninstall keeps where
    /// the person's own quick-start stands. One name is the previous release's own and no other; the way-back leg records
    /// its exact listing line only after reading the file as that release's own, outside the leg that moves again; a compare
    /// excuses a line only where a leg of the previous release recorded it exactly; and a clean begin takes it after that leg,
    /// before it reads the machine, so the next leg runs.
    /// </summary>
    [Fact]
    public void The_Windows_compare_excuses_and_a_clean_begin_takes_only_the_previous_release_s_own_record_going_back_leaves()
    {
        var windows = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "scripts", "Test-InstallationWindows.ps1")).Replace("\r\n", "\n", StringComparison.Ordinal);
        string Function(string name)
        {
            var body = windows[windows.IndexOf($"\nfunction {name}", StringComparison.Ordinal)..];
            return body[..body.IndexOf("\n}\n", StringComparison.Ordinal)];
        }

        Assert.Contains("\n$previousOwn = [ordered]@{ 'agents skills\\.jason-skills.json' = (Join-Path $env:USERPROFILE '.agents\\skills\\.jason-skills.json') }\n", windows, StringComparison.Ordinal);
        Assert.Contains("($previousLegs -ccontains $leg) -and (@(Get-Content -LiteralPath $previousOwnRows) -ccontains \"$leg`t$line\")", Function("Test-PreviousOwn"), StringComparison.Ordinal);
        Assert.Contains("if (-not (Test-PreviousOwn $by $line)) { continue }", Function("Take-PreviousOwn"), StringComparison.Ordinal);
        Assert.Contains("if (Test-PreviousOwn $by $line) { $own.Add(", Function("Compare-Listings"), StringComparison.Ordinal);

        // The one writer of a row: the way-back leg's own end, outside -Again, after the record is read as the previous release's.
        Assert.Single(Regex.Matches(windows, @"Add-Content -LiteralPath \$previousOwnRows"));
        var wayBack = windows[windows.IndexOf("\n    'WayBack' {\n", StringComparison.Ordinal)..];
        wayBack = wayBack[..wayBack.IndexOf("\n    'InstallOverCommandLine' {\n", StringComparison.Ordinal)];
        var otherwise = wayBack.IndexOf("        } else {\n            # The leg's own end: the runtime stopped, the logon start taken off, and the previous release's own uninstall.\n", StringComparison.Ordinal);
        var refs = wayBack.IndexOf("is not $previousVersion's own skills record", StringComparison.Ordinal);
        var row = wayBack.IndexOf("Add-Content -LiteralPath $previousOwnRows", StringComparison.Ordinal);
        var said = wayBack.IndexOf("Say \"going back leaves $leftRecord", StringComparison.Ordinal);
        var ownSkill = wayBack.IndexOf("        if ($way -ceq 'put-back') {\n            $own4", StringComparison.Ordinal);
        Assert.True(otherwise >= 0 && refs > otherwise && row > refs && said > row && ownSkill > said, "the way-back leg does not record the previous release's own record only after reading it as that release's, in its own end and outside -Again, and say so");

        var begin = windows[windows.IndexOf("\n    'Begin' {\n", StringComparison.Ordinal)..];
        var cache = begin.IndexOf("\n            if (-not (Take-UpdaterCache $Leg $prior)) { return }\n", StringComparison.Ordinal);
        var take = begin.IndexOf("\n            if (-not (Take-PreviousOwn $Leg $prior)) { return }\n", StringComparison.Ordinal);
        var read = begin.IndexOf("\n            $found = @(Get-LeftOver)\n", StringComparison.Ordinal);
        Assert.True(cache >= 0 && take > cache && read > take, "a clean Windows begin does not take the previous release's own record by the leg before it, before it reads the machine");
    }

    /// <summary>
    /// Going back and forward again on Windows, with the person's own quick-start put back under Jason's name, leaves the
    /// skills record in ~\.agents\skills: this build's own uninstall reports that file as edited, keeps it without --force,
    /// and keeps the record that names it. One name is this build's own kept record and no other; the leg that moves again
    /// records its exact listing line only where that uninstall's own report names exactly the person's file, under that
    /// root, as the one edited file, and the record names it; and a compare excuses the line only where that leg recorded it.
    /// </summary>
    [Fact]
    public void The_Windows_compare_excuses_this_build_s_own_kept_record_only_where_its_uninstall_reports_the_person_s_file_edited()
    {
        var windows = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "scripts", "Test-InstallationWindows.ps1")).Replace("\r\n", "\n", StringComparison.Ordinal);
        string Function(string name)
        {
            var body = windows[windows.IndexOf($"\nfunction {name}", StringComparison.Ordinal)..];
            return body[..body.IndexOf("\n}\n", StringComparison.Ordinal)];
        }

        Assert.Contains("\n$thisBuildKept = [ordered]@{ 'agents skills\\.jason-skills.json' = (Join-Path $env:USERPROFILE '.agents\\skills\\.jason-skills.json') }\n", windows, StringComparison.Ordinal);
        Assert.Contains("@(Get-Content -LiteralPath $thisBuildKeptRows) -ccontains \"$leg`t$line\"", Function("Test-ThisBuildKept"), StringComparison.Ordinal);
        Assert.Contains("elseif (Test-ThisBuildKept $by $line) { $keptOwn.Add(", Function("Compare-Listings"), StringComparison.Ordinal);

        // The one writer of a row: the leg that moves again, after its own uninstall, once the report and the record are read.
        Assert.Single(Regex.Matches(windows, @"Add-Content -LiteralPath \$thisBuildKeptRows"));
        var wayBack = windows[windows.IndexOf("\n    'WayBack' {\n", StringComparison.Ordinal)..];
        wayBack = wayBack[..wayBack.IndexOf("\n    'InstallOverCommandLine' {\n", StringComparison.Ordinal)];
        var again = wayBack.IndexOf("        if ($Again) {\n", StringComparison.Ordinal);
        var uninstall = wayBack.IndexOf("$ran = Invoke-Program $carried @('uninstall', '--purge-data', '--yes') $quiet 300", StringComparison.Ordinal);
        var edited = wayBack.IndexOf("$edited = @($roots | ForEach-Object { $root = $_; @($root.files) | Where-Object { $_.edited -eq $true } | ForEach-Object { \"$($root.root)`t$($_.path)\" } })", StringComparison.Ordinal);
        var exact = wayBack.IndexOf("if ($edited.Count -ne 1 -or $edited[0] -ine \"$keptRoot`t$keptFile\") { Fail ", StringComparison.Ordinal);
        var names = wayBack.IndexOf("if ($recorded -cnotcontains 'quick-start/SKILL.md') { Fail ", StringComparison.Ordinal);
        var row = wayBack.IndexOf("Add-Content -LiteralPath $thisBuildKeptRows", StringComparison.Ordinal);
        var said = wayBack.IndexOf("Say \"going back and forward again leaves the person ", StringComparison.Ordinal);
        var otherwise = wayBack.IndexOf("        } else {\n            # The leg's own end: the runtime stopped, the logon start taken off, and the previous release's own uninstall.\n", StringComparison.Ordinal);
        Assert.True(again >= 0 && uninstall > again && edited > uninstall && exact > edited && names > exact && row > names && said > row && otherwise > said,
            "the leg that moves again does not record this build's own kept record only after its own uninstall's report names exactly the person's file as edited and the record names it, and say so");
    }

    /// <summary>
    /// The macOS proof script runs under the system's bash, 3.2, which misreads a here-document opened inside a command
    /// substitution once the document holds an odd number of apostrophes, and then cannot parse the script at all, so every
    /// step of the job fails at once. No here-document is opened inside a command substitution there: a program fed one
    /// writes to a file the script reads after. The script is read line by line, so a substitution left open at a line's
    /// end, where a here-document on a later line would escape the reading, is refused as well.
    /// </summary>
    [Fact]
    public void The_macOS_proof_script_opens_no_here_document_inside_a_command_substitution()
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot(), ".github", "scripts", "test-installation-macos.sh"));
        var opened = lines
            .Select((line, at) => (line, at))
            .Where(item => Regex.IsMatch(item.line, @"\$\((?:[^()]|\([^()]*\))*<<-?\s*['""]?\w") || Regex.IsMatch(item.line, @"\$\(\s*$"))
            .Select(item => $"line {item.at + 1}: {item.line.Trim()}")
            .ToList();
        Assert.True(opened.Count == 0, "test-installation-macos.sh opens a here-document inside a command substitution: " + string.Join("; ", opened));
    }

    /// <summary>
    /// A person's start of Jason Desktop gets the PATH a terminal is given, which does not name ~/.local/bin, where the job's
    /// runner puts it on every step's. An application started with the job's PATH finds on it whatever the job put there,
    /// and its set-up reads a machine no person has, so the macOS proof script starts every application with the PATH
    /// install.sh already runs under: <c>env PATH="$(plain_path)"</c> before the application's executable, on the line
    /// that starts it.
    /// </summary>
    [Fact]
    public void The_macOS_proof_script_starts_every_application_with_a_terminals_PATH()
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot(), ".github", "scripts", "test-installation-macos.sh"));
        var starts = lines
            .Select((line, at) => (line, at))
            .Where(item => Regex.IsMatch(item.line, @"/Contents/MacOS/Jason""?\s*(?:>|&|$)") && !Regex.IsMatch(item.line, @"^\s*#"))
            .ToList();
        Assert.NotEmpty(starts);
        var plain = starts
            .Where(item => !Regex.IsMatch(item.line, @"^\s*env PATH=""\$\(plain_path\)"" ""\$[a-z_]+/Contents/MacOS/Jason"" >"))
            .Select(item => $"line {item.at + 1}: {item.line.Trim()}")
            .ToList();
        Assert.True(plain.Count == 0, "test-installation-macos.sh starts an application with the job's own PATH: " + string.Join("; ", plain));
    }

    /// <summary>
    /// The legs that hold what a person's opening of Jason Desktop sets up - this build put in Applications by hand, and the
    /// previous release's bundle replaced by this build's - open the application through LaunchServices, which gives it the
    /// environment a person's application has; every other leg starts it with a terminal's PATH, and no leg does both. The
    /// opening is by the bundle's full path, never by name, since LaunchServices may know an older copy the runner's
    /// temporary folder holds, and the one helper that opens it holds the process it then finds to run from that bundle.
    /// </summary>
    [Fact]
    public void The_macOS_proof_script_opens_the_application_by_its_bundle_in_the_legs_of_a_persons_opening_alone()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "scripts", "test-installation-macos.sh")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var steps = Regex.Matches(text, @"(?m)^(?<name>[a-z][a-z0-9-]*)\)\n(?<body>.*?)\n    ;;$", RegexOptions.Singleline)
            .ToDictionary(match => match.Groups["name"].Value, match => match.Groups["body"].Value);
        string[] opening = ["by-hand", "bundle-swap"];
        var wrong = opening.Where(name => !steps.ContainsKey(name)).Select(name => $"no step {name}").ToList();
        foreach (var (name, body) in steps)
        {
            var opens = Regex.IsMatch(body, @"(?m)^\s*window=""\$\(open_app ""\$[a-z_]+"" ");
            var starts = Regex.IsMatch(body, @"(?m)^\s*env PATH=""\$\(plain_path\)"" ""\$[a-z_]+/Contents/MacOS/Jason""");
            if (opening.Contains(name) && !opens) wrong.Add($"{name} does not open the application with open_app");
            if (opening.Contains(name) && starts) wrong.Add($"{name} also starts the application with a terminal's PATH");
            if (!opening.Contains(name) && opens) wrong.Add($"{name} opens the application with open_app, which only the legs of a person's opening do");
        }
        var byName = text.Split('\n').Where(line => !line.TrimStart().StartsWith('#') && Regex.IsMatch(line, @"\bopen\s+-[ab]\b")).ToList();
        wrong.AddRange(byName.Select(line => $"opens by name: {line.Trim()}"));
        var opened = Regex.Matches(text, @"(?m)^\s*within \d+ ""open \$bundle"" open --stdout ""\$log"" --stderr ""\$log"" ""\$bundle"" ").Count;
        if (opened != 1) wrong.Add($"open_app's one open by the bundle's path is found {opened} times");
        if (!text.Contains("\"$bundle/Contents/MacOS/Jason\"|\"$bundle/Contents/MacOS/Jason \"*) ;;", StringComparison.Ordinal)) wrong.Add("open_app does not hold the process it finds to run from the bundle it opened");
        Assert.True(wrong.Count == 0, string.Join("; ", wrong));
    }

    /// <summary>
    /// The proof scripts change the machine they run on: they install, uninstall, write the account's Path and its
    /// profiles. Each holds every step but the five that only read (the listing, the compare, the held list, a leg's begin
    /// and end, which stub trials run) behind one guard at its entry, before any step runs: a GitHub-hosted runner, or
    /// the step refuses. A step added to either script falls under it by default.
    /// </summary>
    [Fact]
    public void The_proof_scripts_change_a_machine_on_a_GitHub_hosted_runner_alone()
    {
        var windows = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "scripts", "Test-InstallationWindows.ps1")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var reading = windows.IndexOf("$readingSteps = @('Listing', 'Compare', 'Held', 'Begin', 'End')\n", StringComparison.Ordinal);
        var guard = windows.IndexOf("if ($Step -cnotin $readingSteps -and ($env:GITHUB_ACTIONS -cne 'true' -or $env:RUNNER_ENVIRONMENT -cne 'github-hosted')) {", StringComparison.Ordinal);
        var steps = windows.IndexOf("\nswitch ($Step) {\n", StringComparison.Ordinal);
        Assert.True(reading >= 0 && guard > reading && steps > guard, "Test-InstallationWindows.ps1 no longer guards every changing step at its entry, before its steps.");
        Assert.Equal(1, windows.Split("\nswitch ($Step) {\n").Length - 1);
        var set = System.Text.RegularExpressions.Regex.Match(windows, @"\[ValidateSet\((?<steps>[^)]*)\)\] \[string\] \$Step");
        Assert.True(set.Success, "Test-InstallationWindows.ps1 no longer names its steps in one ValidateSet.");
        var named = set.Groups["steps"].Value.Split(',').Select(step => step.Trim().Trim('\'')).ToList();
        Assert.Subset(named.ToHashSet(), new HashSet<string> { "Listing", "Compare", "Held", "Begin", "End" });
        Assert.True(named.Count > 5, "Test-InstallationWindows.ps1 names no step that changes the machine.");

        var macos = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "scripts", "test-installation-macos.sh")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var entry = macos.IndexOf("step=\"${1:-}\"; shift || true\n", StringComparison.Ordinal);
        var exempt = macos.IndexOf("\n    listing|compare|held|begin|end) ;;\n", StringComparison.Ordinal);
        var refuse = macos.IndexOf("*) if [ \"${GITHUB_ACTIONS:-}\" != true ] || [ \"${RUNNER_ENVIRONMENT:-}\" != github-hosted ]; then fail \"the step '$step' changes the machine it runs on", StringComparison.Ordinal);
        var first = macos.IndexOf("\nstand-in)\n", StringComparison.Ordinal);
        Assert.True(entry >= 0 && exempt > entry && refuse > exempt && first > refuse, "test-installation-macos.sh no longer guards every changing step at its entry, before its steps.");
    }

    /// <summary>
    /// A leg whose own compare differs never reads as succeeded: wherever either proof script records a divergence, the
    /// open leg is recorded as failed by its name and as dirty in the same act, and the leg's end, which still runs so that
    /// what the leg does after its compare is done, fails its step. A divergence once ended a leg's step green while the job
    /// failed only at its held step, and no leg was named for it.
    /// </summary>
    [Fact]
    public void A_leg_whose_own_compare_differs_is_recorded_failed_and_its_end_fails_its_step()
    {
        var root = RepositoryRoot();
        var windows = File.ReadAllText(Path.Combine(root, ".github", "scripts", "Test-InstallationWindows.ps1")).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var macos = File.ReadAllText(Path.Combine(root, ".github", "scripts", "test-installation-macos.sh")).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var wrong = new List<string>();
        void Hold(string[] lines, string script, string records, string marks)
        {
            var at = Enumerable.Range(0, lines.Length).Where(index => !lines[index].TrimStart().StartsWith('#') && lines[index].Contains(records, StringComparison.Ordinal)).ToList();
            if (at.Count == 0) wrong.Add($"{script} records no divergence");
            wrong.AddRange(at.Where(index => index + 1 >= lines.Length || !lines[index + 1].TrimStart().StartsWith(marks, StringComparison.Ordinal)).Select(index => $"{script}:{index + 1} records a divergence and does not mark the open leg with {marks.Trim()}"));
        }

        Hold(windows, "Test-InstallationWindows.ps1", "-LiteralPath (Join-Path $Out 'divergent.txt') -Value", "Set-LegDirty ");
        Hold(macos, "test-installation-macos.sh", ">> \"$OUT/divergent.txt\"", "dirty_leg ");

        var windowsText = string.Join('\n', windows);
        var windowsEnd = windowsText.IndexOf("\n    'End' {\n", StringComparison.Ordinal);
        var windowsFails = windowsText.IndexOf("if ((Test-Path -LiteralPath $dirty) -and (@(Get-Content -LiteralPath $dirty) -ccontains $Leg)) {", windowsEnd, StringComparison.Ordinal);
        if (windowsEnd < 0 || windowsFails < 0 || windowsText.IndexOf("exit 1", windowsFails, StringComparison.Ordinal) > windowsText.IndexOf("\n    'Held' {\n", windowsEnd, StringComparison.Ordinal)) wrong.Add("Test-InstallationWindows.ps1's End does not fail the step of a dirty leg");
        var macosText = string.Join('\n', macos);
        var macosEnd = macosText.IndexOf("\nend)\n", StringComparison.Ordinal);
        var macosFails = macosText.IndexOf("if grep -qxF -- \"$1\" \"$OUT/legs-dirty.txt\" 2>/dev/null; then", macosEnd, StringComparison.Ordinal);
        if (macosEnd < 0 || macosFails < 0 || macosText.IndexOf("exit 1", macosFails, StringComparison.Ordinal) > macosText.IndexOf("\nheld)\n", macosEnd, StringComparison.Ordinal)) wrong.Add("test-installation-macos.sh's end does not fail the step of a dirty leg");
        if (!macosText.Contains("    printf '%s\\n' \"the leg '$open' failed: $1\" >> \"$OUT/failed-legs.txt\"\n    printf '%s\\n' \"$open\" >> \"$OUT/legs-dirty.txt\"\n", StringComparison.Ordinal)) wrong.Add("test-installation-macos.sh's dirty_leg does not record the open leg as failed and dirty");
        if (!windowsText.Contains("    Add-Content -LiteralPath (Join-Path $Out 'failed-legs.txt') -Value \"the leg '$open' failed: $why\"\n    Add-Content -LiteralPath $dirty -Value $open\n", StringComparison.Ordinal)) wrong.Add("Test-InstallationWindows.ps1's Set-LegDirty does not record the open leg as failed and dirty");
        Assert.True(wrong.Count == 0, string.Join("; ", wrong));
    }

    /// <summary>
    /// The macOS legs that open this build's application hold its profiles by the PATH a new login shell answered before
    /// the open, never by what the product then did: where that PATH named ~/.local/bin already the product writes no
    /// profile, and the leg holds the report's done to the product's own sentence for it, which is the sentence
    /// MachineSetup writes; elsewhere every profile the product names carries its line. Both legs read that PATH between
    /// saying the open's environment and opening.
    /// </summary>
    [Fact]
    public void The_macOS_legs_hold_the_profiles_by_the_PATH_a_new_login_shell_answered_before_the_open()
    {
        var root = RepositoryRoot();
        var macos = File.ReadAllText(Path.Combine(root, ".github", "scripts", "test-installation-macos.sh")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var setup = File.ReadAllText(Path.Combine(root, "dotnet", "src", "Jason.Cli", "Installation", "MachineSetup.cs"));
        Assert.Contains("Done.Add($\"{places.LocalBin} is on the PATH a terminal has already, so no profile was written.\");", setup, StringComparison.Ordinal);
        Assert.Contains("grep -qxF \"$HOME/.local/bin is on the PATH a terminal has already, so no profile was written.\"", macos, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(macos, @"\n    say_open_environment\n    read_terminal_before\n    window=""\$\(open_app ""\$placed"" ").Count);
        var hold = macos[macos.IndexOf("\nhold_account_terminal() {\n", StringComparison.Ordinal)..];
        hold = hold[..hold.IndexOf("\n}\n", StringComparison.Ordinal)];
        Assert.Contains("if [ -s \"$OUT/terminal-before-names-local-bin.txt\" ]; then", hold, StringComparison.Ordinal);
        Assert.Contains("grep -qx '# added by jason install' \"$profile\"", hold, StringComparison.Ordinal);
    }

    /// <summary>
    /// The hold reads the set-up report's done by the members the product's own document has: a report MachineSetup's type
    /// makes, serialized as jason install prints it and kept as the application's first start keeps it, walked by the
    /// members the hold's reader names, in its order, gives the sentence back. And the hold says what it read, so a reader
    /// of the run can quote the record's done.
    /// </summary>
    [Fact]
    public void The_macOS_hold_reads_the_set_up_report_s_done_where_the_product_s_own_document_has_it()
    {
        var root = RepositoryRoot();
        var macos = File.ReadAllText(Path.Combine(root, ".github", "scripts", "test-installation-macos.sh")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var hold = macos[macos.IndexOf("\nhold_account_terminal() {\n", StringComparison.Ordinal)..];
        hold = hold[..hold.IndexOf("\n}\n", StringComparison.Ordinal)];
        var reader = Regex.Match(hold, @"done_list=""\$\(python3 -c '(?<code>[^']*)' ""\$record""\)""");
        Assert.True(reader.Success, "the hold reads no done from the set-up record by one python3 reader");
        var members = Regex.Matches(reader.Groups["code"].Value, @"\.get\(""(?<member>[a-z_]+)""\)").Select(match => match.Groups["member"].Value).ToArray();

        // The record as the application's first start writes it: the report is the document jason install printed.
        var desktop = File.ReadAllText(Path.Combine(root, "desktop", "src", "machine-setup.ts"));
        Assert.Contains("JSON.stringify({ ran_for: version, ran_from: place, report: printed }, null, 2)", desktop, StringComparison.Ordinal);
        const string sentence = "/Users/someone/.local/bin is on the PATH a terminal has already, so no profile was written.";
        var report = new InstallReport(
            "0.49.0",
            "/Applications/Jason.app/Contents/Resources/bundle/jason",
            new InstallCommandOutcome("/Users/someone/.local/bin/jason", true, "/bin/zsh", false, [], [], "/Users/someone/.local/bin/jason", true),
            [],
            null,
            null,
            "not_running",
            [sentence],
            [],
            []);
        JsonNode? said = new JsonObject
        {
            ["ran_for"] = "0.49.0",
            ["ran_from"] = "/Applications/Jason.app",
            ["report"] = JsonNode.Parse(JsonSerializer.Serialize(report, JasonJson.Options)),
        };
        foreach (var member in members)
        {
            said = said is JsonObject holding ? holding[member] : null;
        }

        Assert.True(said is JsonArray, $"the hold reads {string.Join('.', members)} of the set-up record, which the product's own document does not hold as a list");
        Assert.Equal([sentence], said!.AsArray().Select(item => item!.GetValue<string>()));
        Assert.Contains("say \"the set-up record at $record: its report's done says: ", hold, StringComparison.Ordinal);
    }

    /// <summary>
    /// The disk image leg's end takes this build's extraction folder by the id the build was read to extract to, and .NET
    /// spells that id in the URL-safe base64 alphabet: a letter, a digit, '_' or '-'. The pattern the end refuses an id by,
    /// read from the script, takes an id holding both '_' and '-' (one a run's build had) and refuses one that is empty or
    /// would name anything but one folder. Read as a glob everywhere; run by bash itself where there is one.
    /// </summary>
    [Fact]
    public void The_disk_image_leg_s_end_takes_an_extraction_id_holding_both_underscore_and_hyphen()
    {
        var macos = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "scripts", "test-installation-macos.sh")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var end = macos[macos.IndexOf("\nran-from-image-end)\n", StringComparison.Ordinal)..];
        end = end[..end.IndexOf("\n    ;;\n", StringComparison.Ordinal)];
        var refusal = Regex.Match(end, @"\n *case ""\$id"" in (?<pattern>[^)\n]+)\) line=""  \$folder: not an id of the shape a build's is, left"" ;; \*\)\n");
        Assert.True(refusal.Success, "the disk image leg's end refuses no id by one case pattern");
        var pattern = refusal.Groups["pattern"].Value;
        var taken = new[] { "4_Z-4a36gQYZ", "5NxLCBGAJEwA", "a_b", "a-b" };
        var refused = new[] { "", "../x", "a/b", "a b", "a.b", "*" };
        var glob = new Regex(GlobAsRegex(pattern));
        foreach (var id in taken)
        {
            Assert.False(glob.IsMatch(id), $"the disk image leg's end refuses the id '{id}' by '{pattern}', and a build's id may be spelled so");
        }

        foreach (var id in refused)
        {
            Assert.True(glob.IsMatch(id), $"the disk image leg's end takes the id '{id}' by '{pattern}', which names no build's folder");
        }

        if (!OperatingSystem.IsWindows())
        {
            foreach (var id in taken.Concat(refused))
            {
                var (exit, stdout, stderr) = Run("/bin/bash", new Dictionary<string, string>(), "-c", $"case \"$1\" in {pattern}) echo refused ;; *) echo taken ;; esac", "row", id);
                Assert.True(exit == 0, stderr);
                Assert.Equal(taken.Contains(id) ? "taken" : "refused", stdout.Trim());
            }
        }

        // A case pattern of the shell's, as a regular expression: alternatives by '|', '*' any run, '?' one character,
        // a bracket as a class ('!' negating it), and '' the empty word.
        static string GlobAsRegex(string cases)
        {
            var alternatives = new List<string>();
            foreach (var alternative in cases.Split('|'))
            {
                var regex = new StringBuilder();
                for (var at = 0; at < alternative.Length; at++)
                {
                    var c = alternative[at];
                    if (c == '*') { regex.Append(".*"); }
                    else if (c == '?') { regex.Append('.'); }
                    else if (c == '\'' && alternative.IndexOf('\'', at + 1) is var close and > 0) { regex.Append(Regex.Escape(alternative[(at + 1)..close])); at = close; }
                    else if (c == '[')
                    {
                        var close2 = alternative.IndexOf(']', at + 2);
                        var inside = alternative[(at + 1)..close2];
                        regex.Append('[').Append(inside.StartsWith('!') ? "^" + inside[1..] : inside).Append(']');
                        at = close2;
                    }
                    else { regex.Append(Regex.Escape(c.ToString())); }
                }

                alternatives.Add(regex.ToString());
            }

            return "^(?:" + string.Join('|', alternatives) + ")$";
        }
    }

    /// <summary>
    /// Going back to the previous release on macOS leaves that release's own skills record, which its uninstall keeps where
    /// the person's own quick-start stands. One name is the previous release's own and no other; the way-back leg records
    /// its exact listing line only after reading the file as that release's own, outside the leg that moves again; a compare
    /// excuses a line only where a leg of the previous release recorded it exactly; and a clean begin takes it after that leg,
    /// before it reads the machine, so the next leg runs.
    /// </summary>
    [Fact]
    public void The_macOS_compare_excuses_and_a_clean_begin_takes_only_the_previous_release_s_own_record_going_back_leaves()
    {
        var macos = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "scripts", "test-installation-macos.sh")).Replace("\r\n", "\n", StringComparison.Ordinal);
        string Function(string name)
        {
            var at = macos.IndexOf($"\n{name}() {{\n", StringComparison.Ordinal);
            Assert.True(at >= 0, $"test-installation-macos.sh has no function {name}");
            var body = macos[at..];
            return body[..body.IndexOf("\n}\n", StringComparison.Ordinal)];
        }

        string Step(string name, string next)
        {
            var at = macos.IndexOf($"\n{name})\n", StringComparison.Ordinal);
            Assert.True(at >= 0, $"test-installation-macos.sh has no step {name}");
            var body = macos[at..];
            return body[..body.IndexOf($"\n{next})\n", StringComparison.Ordinal)];
        }

        Assert.Contains("\nPREVIOUS_OWN_NAME='agents skills/.jason-skills.json'\nPREVIOUS_OWN_FILE=\"$HOME/.agents/skills/.jason-skills.json\"\n", macos, StringComparison.Ordinal);
        var isOwn = Function("is_previous_own");
        Assert.Contains("grep -qxF -- \"$1\" \"$OUT/previous-legs.txt\" 2>/dev/null || return 1", isOwn, StringComparison.Ordinal);
        Assert.Contains("grep -qxF -- \"$1\t$2\" \"$OUT/previous-own.txt\"", isOwn, StringComparison.Ordinal);
        var take = Function("take_previous_own");
        Assert.Contains("is_previous_own \"$by\" \"$line\" || return 0", take, StringComparison.Ordinal);
        Assert.Contains("[ \"${GITHUB_ACTIONS:-}\" != true ] || [ \"${RUNNER_ENVIRONMENT:-}\" != github-hosted ]", take, StringComparison.Ordinal);
        Assert.Contains(">> \"$OUT/resets.txt\"", take, StringComparison.Ordinal);
        Assert.Contains("if is_previous_own \"$by\" \"$line\"; then own_here=", Step("compare", "begin"), StringComparison.Ordinal);

        // The one writer of a row: the way-back leg's own end, outside 'again', after the record is read as the previous release's.
        Assert.Single(Regex.Matches(macos, @">> ""\$OUT/previous-own\.txt"""));
        var wayBack = Step("way-back", "over-previous-desktop");
        var otherwise = wayBack.IndexOf("        say \"$old's own runtime stop, autostart disable and uninstall --purge-data --yes each ended 0\"\n", StringComparison.Ordinal);
        var refs = wayBack.IndexOf("is not $old's own skills record", StringComparison.Ordinal);
        var row = wayBack.IndexOf(">> \"$OUT/previous-own.txt\"", StringComparison.Ordinal);
        var said = wayBack.IndexOf("say \"going back leaves $PREVIOUS_OWN_FILE", StringComparison.Ordinal);
        var ownSkill = wayBack.IndexOf("    if [ \"$way\" = put-back ]; then\n        own4=", StringComparison.Ordinal);
        Assert.True(otherwise >= 0 && refs > otherwise && row > refs && said > row && ownSkill > said, "the macOS way-back leg does not record the previous release's own record only after reading it as that release's, in its own end and outside 'again', and say so");

        var begin = Step("begin", "end");
        var scratch = begin.IndexOf("\n        take_scratch \"$LEG\"\n", StringComparison.Ordinal);
        var prior = begin.IndexOf("\n        prior=\"$(cat \"$OUT/last-leg.txt\" 2>/dev/null || true)\"\n", StringComparison.Ordinal);
        var taken = begin.IndexOf("\n        take_previous_own \"$LEG\" \"$prior\"\n", StringComparison.Ordinal);
        var read = begin.IndexOf("\n        found=\"$(left_over)\"\n", StringComparison.Ordinal);
        Assert.True(scratch >= 0 && prior > scratch && taken > prior && read > taken, "a clean macOS begin does not take the previous release's own record by the leg before it, before it reads the machine");
    }

    /// <summary>
    /// After going back and moving again on macOS, this build's own uninstall keeps the person's own quick-start, which it
    /// reports edited, and the skills record naming it. The leg excuses that record's line by attribution to this build's
    /// own uninstall only where that uninstall's own report names exactly the person's own SKILL.md as the one edited file
    /// it kept under ~/.agents/skills: the reader is run on a report the product's own types and serializer wrote, among
    /// the words the verb says on stderr, beside a report that names another file or another root. The leg says what a
    /// person is left with, records the line once, and a compare excuses exactly the recorded line.
    /// </summary>
    [Fact]
    public void The_macOS_second_way_back_leg_excuses_this_build_s_own_record_only_where_its_uninstall_reports_the_person_s_file_edited()
    {
        var macos = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "scripts", "test-installation-macos.sh")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var function = macos.IndexOf("\nis_this_build_own() {\n", StringComparison.Ordinal);
        Assert.True(function >= 0, "test-installation-macos.sh has no is_this_build_own");
        var isOwn = macos[function..];
        isOwn = isOwn[..isOwn.IndexOf("\n}\n", StringComparison.Ordinal)];
        Assert.Contains("grep -qxF -- \"$1\t$2\" \"$OUT/this-build-own.txt\"", isOwn, StringComparison.Ordinal);
        var compare = macos[macos.IndexOf("\ncompare)\n", StringComparison.Ordinal)..];
        compare = compare[..compare.IndexOf("\nbegin)\n", StringComparison.Ordinal)];
        Assert.Contains("elif is_this_build_own \"$by\" \"$line\"; then own_here=", compare, StringComparison.Ordinal);

        // The one writer of a row: the second way-back leg's own end, after its uninstall's report is read, before 'else'.
        Assert.Single(Regex.Matches(macos, @">> ""\$OUT/this-build-own\.txt"""));
        var wayBack = macos[macos.IndexOf("\nway-back)\n", StringComparison.Ordinal)..];
        wayBack = wayBack[..wayBack.IndexOf("\nover-previous-desktop)\n", StringComparison.Ordinal)];
        var ended = wayBack.IndexOf("        say \"$version's own uninstall --purge-data --yes, after moving again, ended 0\"\n", StringComparison.Ordinal);
        var reader = Regex.Match(wayBack, @"kept_read=""\$\(python3 -c '(?<code>[^']*)' ""\$OUT/way-back-end\.txt"" ""\$HOME/\.agents/skills""\)""");
        var held = wayBack.IndexOf("[ \"$kept_read\" = \"edited: $OWN_SKILL/SKILL.md\" ] || fail ", StringComparison.Ordinal);
        var row = wayBack.IndexOf(">> \"$OUT/this-build-own.txt\"", StringComparison.Ordinal);
        var said = wayBack.IndexOf("say \"a person who went back and moved again with a skill of their own under one of Jason's names is left with ", StringComparison.Ordinal);
        var otherwise = wayBack.IndexOf("\n    else\n        # The leg's own end: the runtime stopped", StringComparison.Ordinal);
        Assert.True(reader.Success, "the second way-back leg reads no report of its uninstall by one python3 reader");
        Assert.True(ended >= 0 && reader.Index > ended && held > reader.Index && row > held && said > row && otherwise > said, "the second way-back leg does not record this build's own record only after its uninstall's report names the person's own file edited, and say what a person is left with, in its own end");

        // The reader, run on what the verb printed: a line of stderr, then the report the product's types and serializer make.
        const string home = "/Users/someone";
        UninstallReport Report(params RootRemoval[] roots) => new(
            new UninstallPlan(false, null, null, null, roots, [], null, null, null, null, null, true, $"{home}/.jason"),
            ["Removed 66 file(s)."],
            [],
            [],
            null,
            null);
        RootRemoval Root(string root, params (string File, bool Edited)[] files) =>
            new(root, ["jason-runtime-skills", "jason-business-skills"], [.. files.Select(file => new FileRemoval($"{root}/{file.File}", true, file.Edited))]);
        var agents = $"{home}/.agents/skills";
        var claude = $"{home}/.claude/skills";
        var cases = new (string Name, UninstallReport Report, string Said)[]
        {
            ("the person's own file edited", Report(Root(claude, ("quick-start/SKILL.md", false)), Root(agents, ("quick-start/SKILL.md", true), ("inbox-triage/SKILL.md", false))), $"edited: {agents}/quick-start/SKILL.md"),
            ("another file edited", Report(Root(agents, ("quick-start/SKILL.md", false), ("inbox-triage/SKILL.md", true))), $"edited: {agents}/inbox-triage/SKILL.md"),
            ("the person's own file edited under another root alone", Report(Root(claude, ("quick-start/SKILL.md", true)), Root(agents, ("quick-start/SKILL.md", false))), "edited: "),
            ("two files edited", Report(Root(agents, ("quick-start/SKILL.md", true), ("inbox-triage/SKILL.md", true))), $"edited: {agents}/quick-start/SKILL.md | {agents}/inbox-triage/SKILL.md"),
        };
        var python = OperatingSystem.IsWindows() ? "python" : "python3";
        Assert.SkipUnless(Run(python, new Dictionary<string, string>(), "--version").Exit == 0, $"{python} is not on this machine's PATH, so the reader cannot be run here");
        using var tree = new TempTree();
        var folder = tree.NewDirectory("said");
        foreach (var (name, report, expected) in cases)
        {
            var printed = Path.Combine(folder, "way-back-end.txt");
            File.WriteAllText(printed, "Stopping the runtime...\n" + JsonSerializer.Serialize(report, JasonJson.Options) + "\n");
            var (exit, stdout, stderr) = Run(python, new Dictionary<string, string>(), "-c", reader.Groups["code"].Value, printed, agents);
            Assert.True(exit == 0, $"{name}: {stderr}");
            Assert.True(expected == stdout.TrimEnd('\r', '\n'), $"{name}: the reader said '{stdout.Trim()}', not '{expected}'");
        }

        // Only the first is the one the leg excuses: the person's own SKILL.md, exactly, and nothing else.
        Assert.Contains("OWN_SKILL=\"$HOME/.agents/skills/quick-start\"", macos, StringComparison.Ordinal);
    }

    /// <summary>
    /// The macOS way-back leg reads the skills record going back leaves as the previous release's own only where it has a
    /// pack and every pack names that release, as "v0.48.5" or "0.48.5": a pack with no ref, an empty ref or another
    /// release's makes the leg fail. The leg's own reader is run on such records and its count of packs naming anything
    /// else is held.
    /// </summary>
    [Fact]
    public void The_macOS_way_back_leg_reads_a_record_as_the_previous_release_s_own_only_where_every_pack_names_that_release()
    {
        var macos = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "scripts", "test-installation-macos.sh")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var reader = Regex.Match(macos, @"record_read=""\$\(python3 -c '(?<code>[^']*)' ""\$PREVIOUS_OWN_FILE"" ""\$old""\)""");
        Assert.True(reader.Success, "the way-back leg does not read the record by one python3 reader handed the previous release's version");
        Assert.Contains("[ \"$(printf '%s\\n' \"$record_read\" | sed -n 5p)\" = 0 ] || fail \"after $remover, $PREVIOUS_OWN_FILE is left, and it is not $old's own skills record", macos, StringComparison.Ordinal);

        var python = OperatingSystem.IsWindows() ? "python" : "python3";
        Assert.SkipUnless(Run(python, new Dictionary<string, string>(), "--version").Exit == 0, $"{python} is not on this machine's PATH, so the reader cannot be run here");
        using var tree = new TempTree();
        var folder = tree.NewDirectory("records");
        var cases = new (string Name, string Record, bool Own)[]
        {
            ("every pack at v0.48.5", """{"packs":[{"ref":"v0.48.5","files":[{"path":"quick-start/SKILL.md"}]},{"ref":"v0.48.5","files":[]}]}""", true),
            ("every pack at 0.48.5", """{"packs":[{"ref":"0.48.5","files":[]}]}""", true),
            ("a pack with no ref", """{"packs":[{"ref":"v0.48.5","files":[]},{"files":[]}]}""", false),
            ("a pack with an empty ref", """{"packs":[{"ref":"v0.48.5","files":[]},{"ref":"","files":[]}]}""", false),
            ("a pack of another release", """{"packs":[{"ref":"v0.48.5","files":[]},{"ref":"v0.49.0","files":[]}]}""", false),
            ("no pack", """{"packs":[]}""", false),
        };
        foreach (var (name, record, own) in cases)
        {
            var file = Path.Combine(folder, ".jason-skills.json");
            File.WriteAllText(file, record);
            var (exit, stdout, stderr) = Run(python, new Dictionary<string, string>(), "-c", reader.Groups["code"].Value, file, "0.48.5");
            Assert.True(exit == 0, $"{name}: {stderr}");
            var lines = stdout.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            Assert.True(lines.Length >= 5, $"{name}: the reader said '{stdout.Trim()}'");
            Assert.True(own == (lines[4] == "0"), $"{name}: the reader counts {lines[4]} pack(s) naming another release, so the leg would {(own ? "refuse" : "take")} it");
        }
    }

    /// <summary>
    /// Every place a macOS leg refuses to start because it is there already is a leg's own scratch, which a clean begin
    /// takes by name before it reads the machine (take_scratch), so a leg that failed part way never keeps the next one
    /// from running.
    /// </summary>
    [Fact]
    public void A_clean_begin_takes_every_place_a_macOS_leg_refuses_to_find_there_already()
    {
        var macos = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "scripts", "test-installation-macos.sh")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var scratch = string.Join('\n', macos.Split('\n').Where(line => line.StartsWith("SCRATCH_", StringComparison.Ordinal)));
        Assert.Contains("\n        take_scratch \"$LEG\"\n", macos, StringComparison.Ordinal);
        // Each name a refusal reads, by the assignment nearest before it.
        var refused = Regex.Matches(macos, @"\|\| fail ""(?<names>[^""]*) is there before ")
            .SelectMany(match => Regex.Matches(match.Groups["names"].Value, @"\$(?<name>[a-z_]+)").Select(name => (Name: name.Groups["name"].Value, At: match.Index)))
            .ToList();
        Assert.NotEmpty(refused);
        var wrong = new List<string>();
        foreach (var (name, at) in refused)
        {
            var assigned = Regex.Matches(macos[..at], $@"(?m)^\s*{name}=""(?<value>[^""]+)""$").LastOrDefault()?.Groups["value"].Value;
            if (assigned is null) wrong.Add($"${name} is refused and never assigned before");
            else if (!scratch.Contains("/" + assigned.Split('/')[^1], StringComparison.Ordinal)) wrong.Add($"${name}, {assigned}, is refused where it is found, and take_scratch does not take it");
        }

        Assert.True(wrong.Count == 0, string.Join("; ", wrong));
    }

    /// <summary>
    /// While the person's own quick-start is out of its place, whatever stands in its place is a stand-in (the previous
    /// release's skills write one): a clean begin puts the person's copy back as the leg's own put-back does, the stand-in
    /// removed first, never only where the place is empty, and never removes the folder it was moved out to while the
    /// person's copy is still in it.
    /// </summary>
    [Fact]
    public void A_clean_begin_puts_the_person_s_own_quick_start_back_over_a_stand_in_before_it_removes_any_scratch()
    {
        var macos = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "scripts", "test-installation-macos.sh")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var take = macos[macos.IndexOf("\ntake_scratch() {\n", StringComparison.Ordinal)..];
        take = take[..take.IndexOf("\n}\n", StringComparison.Ordinal)];
        var check = take.IndexOf("    if [ -d \"$SCRATCH_OWN_OUT/quick-start\" ]; then\n", StringComparison.Ordinal);
        var standIn = take.IndexOf("rm -rf \"$OWN_SKILL\"", StringComparison.Ordinal);
        var back = take.IndexOf("mv \"$SCRATCH_OWN_OUT/quick-start\" \"$OWN_SKILL\"", StringComparison.Ordinal);
        var removal = take.IndexOf("    for place in $SCRATCH_STAGED $SCRATCH_ASIDE $SCRATCH_OWN_OUT $SCRATCH_IMAGE; do\n        { [ -e \"$place\" ] || [ -L \"$place\" ]; } || continue\n", StringComparison.Ordinal);
        Assert.True(check >= 0, "take_scratch does not put the person's own quick-start back whenever it is out of its place");
        Assert.True(standIn > check && back > standIn && removal > back, "take_scratch does not remove the stand-in, then put the person's copy back, before it removes any scratch");
        Assert.DoesNotContain("[ -d \"$SCRATCH_OWN_OUT/quick-start\" ] && [ ! -e \"$OWN_SKILL\" ]", take, StringComparison.Ordinal);
        Assert.Contains("skip_leg \"not run: the person's own quick-start could not be put back", take, StringComparison.Ordinal);
    }

    /// <summary>
    /// The macOS legs serve their feed by serve-feed.py, which does nothing between bind and listen, and wait 30 s for it,
    /// printing the server's log, its process and what holds the port where it never answers.
    /// </summary>
    [Fact]
    public void The_macOS_legs_serve_their_feed_by_the_proofs_own_server_and_say_why_it_never_answered()
    {
        var root = RepositoryRoot();
        var macos = File.ReadAllText(Path.Combine(root, ".github", "scripts", "test-installation-macos.sh")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var server = File.ReadAllText(Path.Combine(root, ".github", "scripts", "serve-feed.py")).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.DoesNotContain("http.server 80", macos, StringComparison.Ordinal);
        Assert.Equal(3, Regex.Matches(macos, @"(?m)^\s*serve_feed ""\$feed"" ""\$OUT/[a-z-]+\.log""$").Count);
        Assert.Contains("    python3 \"$(dirname \"$0\")/serve-feed.py\" \"$folder\" 8099 \"$log\" &\n", macos, StringComparison.Ordinal);
        Assert.Contains("    until=$(( $(date +%s) + 30 ))\n", macos, StringComparison.Ordinal);
        foreach (var said in new[] { "lsof -nP -iTCP:8099", "netstat -anv -p tcp", "cat \"$log\"", "kill -0 \"$server\"" })
        {
            Assert.Contains(said, macos, StringComparison.Ordinal);
        }

        Assert.Contains("        def server_bind(self):\n", server, StringComparison.Ordinal);
        Assert.Contains("        def server_activate(self):\n", server, StringComparison.Ordinal);
        Assert.DoesNotContain("getfqdn(", server[server.IndexOf("def main", StringComparison.Ordinal)..], StringComparison.Ordinal);
        Assert.Contains("said(f\"bound {address}:", server, StringComparison.Ordinal);
        Assert.Contains("said(f\"listening on {address}:", server, StringComparison.Ordinal);
    }

    /// <summary>
    /// The uninstall the application's Uninstall Jason item starts writes both its streams into one file, and a wait's
    /// sentence goes to standard error: the macOS menu leg reads that file's JSON line only to SAY what the report says of
    /// itself (completed, the refusal code, kept and problems) and every other line as said, so a finding the report
    /// carries is in the log; the reading never fails the leg, which stands on the uninstall's exit and the files.
    /// </summary>
    [Fact]
    public void The_macOS_menu_leg_says_what_its_report_says_and_never_fails_on_reading_it()
    {
        var macos = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "scripts", "test-installation-macos.sh")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var leg = macos[macos.IndexOf("\nuninstall-menu)\n", StringComparison.Ordinal)..];
        leg = leg[..leg.IndexOf("\n    ;;\n", StringComparison.Ordinal)];
        Assert.Contains("nohup \"$carried\" uninstall --wait-for \"$window\" > \"$OUT/uninstall-menu.json\" 2>&1 &", leg, StringComparison.Ordinal);
        Assert.Contains("    (reports if isinstance(value, dict) else said).append(line)\n", leg, StringComparison.Ordinal);
        foreach (var field in new[] { "report.get(\"completed\")", "report.get(\"refusal_code\")", "for name in (\"kept\", \"problems\"):" })
        {
            Assert.Contains(field, leg, StringComparison.Ordinal);
        }

        Assert.Contains("wrote no JSON line, so no report is read\")\n    sys.exit(0)\n", leg, StringComparison.Ordinal);
        Assert.Contains("\"$OUT/uninstall-menu.json\" \"$OUT/uninstall-menu-report.json\" || say ", leg, StringComparison.Ordinal);
        Assert.DoesNotContain("read_failed", leg, StringComparison.Ordinal);
        Assert.DoesNotContain("sys.exit(f", leg, StringComparison.Ordinal);
        Assert.Contains("[ \"$status\" -eq 0 ] || fail \"the uninstall the menu item starts exited with $status\"", leg, StringComparison.Ordinal);
    }

    /// <summary>
    /// And CI holds install.sh, the path check and the uninstall to one login profile on a real machine: a home
    /// with <c>~/.bash_profile</c>, where bash never reads <c>~/.profile</c>. All three used to mean
    /// <c>~/.profile</c> there, and the check said <c>ok</c> about a file no login shell opens.
    /// </summary>
    [Fact]
    public void Ci_holds_the_installer_the_check_and_the_uninstall_to_the_profile_bash_reads()
    {
        var ci = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "workflows", "ci.yml"));
        var step = ci.IndexOf("name: Install into the profile bash reads, and take it back out", StringComparison.Ordinal);

        Assert.True(step >= 0, "ci.yml no longer installs into a home where bash reads ~/.bash_profile.");
        Assert.True(ci.IndexOf("bash -l -c 'command -v jason'", step, StringComparison.Ordinal) > step, "the step no longer asks a login bash to find jason.");
        Assert.True(ci.IndexOf("printf '# mine\\n' | cmp - \"$HOME/.bash_profile\"", step, StringComparison.Ordinal) > step, "the step no longer holds the uninstall to the profile's own bytes.");
    }

    /// <summary>
    /// And CI holds the uninstall to the directory rule on a real machine: installed into a <c>~/.local/bin</c> that
    /// another tool is in, the line it is found through stays — and the installer, run with no <c>SHELL</c> under
    /// its own <c>set -eu</c>, writes the profile rather than dying before it.
    /// </summary>
    [Fact]
    public void Ci_holds_the_uninstall_to_a_directory_other_tools_share_and_the_installer_to_no_shell()
    {
        var ci = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "workflows", "ci.yml"));
        var step = ci.IndexOf("name: Install beside another tool with no SHELL set, and leave the line it is found through", StringComparison.Ordinal);

        Assert.True(step >= 0, "ci.yml no longer installs into a directory another tool shares.");
        Assert.True(ci.IndexOf("env -u SHELL sh install/install.sh", step, StringComparison.Ordinal) > step, "the step no longer runs the installer with no SHELL.");
        Assert.True(ci.IndexOf("the uninstall took away the line another tool is found through", step, StringComparison.Ordinal) > step, "the step no longer asserts the line stays.");
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

    // --- install.ps1, run rather than read ----------------------------------------------------------------

    /// <summary>
    /// <c>PROCESSOR_ARCHITECTURE</c> says what the <em>shell</em> is, not what the machine is. A 32-bit
    /// PowerShell on 64-bit Windows — which is what a scheduled task, an installer's "run this afterwards" or
    /// an old shortcut still gives people — reads <c>x86</c> there, and Windows puts the machine's real
    /// architecture in <c>PROCESSOR_ARCHITEW6432</c> for exactly that shell. A script that consults only the
    /// first refuses to install on an ordinary x64 machine and blames the machine for it.
    /// </summary>
    /// <remarks>
    /// Neither variable can be handed to a child process: Windows writes both itself, from what the child
    /// really is, and an environment given to <see cref="System.Diagnostics.ProcessStartInfo"/> is overwritten
    /// for exactly these two names. So the wrong shell is started rather than imitated — the 32-bit Windows
    /// PowerShell every 64-bit Windows carries in <c>SysWOW64</c>, which is a machine saying <c>x86</c> and
    /// <c>AMD64</c> for real. The feed it is pointed at is an empty directory, so a run that gets past the
    /// machine check fails on the manifest that is not there: nothing is downloaded and nothing is installed.
    /// </remarks>
    [Fact]
    public void A_thirty_two_bit_shell_on_a_sixty_four_bit_machine_installs_the_x64_build()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "install.ps1 refuses a PowerShell that is not on Windows before it reads the architecture, so the rule can only be run where the script is meant to run.");

        var wow = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), "WindowsPowerShell", "v1.0", "powershell.exe");
        Assert.SkipWhen(!File.Exists(wow), "This Windows has no 32-bit PowerShell, so there is no wrong shell to be in.");

        using var tree = new TempTree();
        var (_, _, stderr) = Run(
            wow,
            new Dictionary<string, string> { ["LOCALAPPDATA"] = tree.Root },
            "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
            "-File", Path.Combine(RepositoryRoot(), "install", "install.ps1"),
            "-Feed", tree.NewDirectory("feed"));

        Assert.DoesNotContain("No release is built", stderr, StringComparison.Ordinal);
        Assert.Contains("manifest.json", stderr, StringComparison.Ordinal);
    }

    /// <summary>
    /// And the refusal is still there for a machine no release is built for, naming what it refused. This one
    /// is read rather than run: the only way to run it is to be on such a machine, and the suite would then
    /// have no way to prove the other half.
    /// </summary>
    [Fact]
    public void A_machine_no_release_is_built_for_is_refused_by_name()
    {
        var code = Code("install.ps1");
        Assert.Contains("PROCESSOR_ARCHITEW6432", code, StringComparison.Ordinal);
        Assert.Contains("No release is built for Windows on", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// The documented one-liner is <c>irm … | iex</c>, and that runs this script's text in the session the
    /// person is sitting in. Everything it assigns at the top level stays behind when it finishes:
    /// <c>$ErrorActionPreference = 'Stop'</c> and <c>Set-StrictMode -Version Latest</c> change how every later
    /// command in that session behaves, and <c>$rid</c> and its neighbours are ordinary names the person may be
    /// using themselves. So the installer is a function, and the call is the only thing the session sees.
    /// </summary>
    [Fact]
    public void The_one_liner_leaves_the_callers_shell_as_it_found_it()
    {
        using var tree = new TempTree();
        var harness = Path.Combine(tree.Root, "one-liner.ps1");
        File.WriteAllText(harness, OneLiner.Replace("@SCRIPT@", Path.Combine(RepositoryRoot(), "install", "install.ps1"), StringComparison.Ordinal));

        // The feed is an empty directory, so the install fails on the manifest that is not there — after
        // everything the script sets on its way to it, which is the point.
        var (exit, stdout, stderr) = Pwsh(
            new Dictionary<string, string>
            {
                ["JASON_INSTALL_FEED"] = tree.NewDirectory("feed"),
                ["LOCALAPPDATA"] = tree.Root,
            },
            "-File", harness);

        Assert.True(exit == 0, stderr);
        Assert.Contains("ErrorActionPreference=Continue", stdout, StringComparison.Ordinal);
        Assert.Contains("strict mode=off", stdout, StringComparison.Ordinal);

        // The function the script defines is checked by the same line as its variables. Without it, the only
        // thing in this repository that noticed the function surviving was one job on one CI runner.
        Assert.DoesNotContain("left behind:", stdout, StringComparison.Ordinal);
    }

    /// <summary>What <c>irm … | iex</c> does, and what the session looks like afterwards.</summary>
    private const string OneLiner = """
        try { Invoke-Expression (Get-Content -LiteralPath '@SCRIPT@' -Raw) } catch { }

        "ErrorActionPreference=$ErrorActionPreference"
        foreach ($name in 'rid', 'asset', 'base', 'releasesUrl', 'licenceBase', 'core', 'fromWeb', 'tmp', 'latest', 'target', 'installed', 'archive', 'expected', 'actual', 'unpacked', 'started', 'previous', 'directory', 'userPath', 'entries', 'harnessRoot') {
            if (Test-Path "Variable:$name") { "left behind: $name" }
        }
        if (Test-Path Function:Install-Jason) { "left behind: Install-Jason" }
        try { $null = $aNameThisSessionNeverDefined; "strict mode=off" } catch { "strict mode=on" }
        """;

    private const string PowerShell7 = "pwsh";

    private const string WindowsPowerShell = "Windows PowerShell 5.1";

    /// <summary>
    /// A failure inside <c>install.ps1</c> stops the install with exit code 1, in every form the pages show it run in and
    /// under both shells, with what failed on stderr. A failure used to end the run with 0: an exception from a .NET call
    /// left the installer's function and reached the call at the bottom of the script, whose <c>try</c> had a
    /// <c>finally</c> and no <c>catch</c>, and the shell printed it and went on to the end; a CI step or an agent that reads
    /// the exit code took a failed install for an installed one. The <c>catch</c> that throws it on is what every form
    /// ends through.
    /// </summary>
    /// <remarks>
    /// The failure is a setup whose digest is not its line in <c>checksums.txt</c>: nothing is run, so nothing is
    /// installed under the <c>LOCALAPPDATA</c> of the test's own, and the account's Path, read before and after each row,
    /// its text and its kind, is left as it was. <c>irm … | iex</c> passes no parameters; the feed reaches it through
    /// <c>JASON_INSTALL_FEED</c>, as it reaches the other two.
    /// </remarks>
    [Theory]
    [InlineData(PowerShell7, "-File")]
    [InlineData(PowerShell7, "[scriptblock]::Create")]
    [InlineData(PowerShell7, "| iex")]
    [InlineData(WindowsPowerShell, "-File")]
    [InlineData(WindowsPowerShell, "[scriptblock]::Create")]
    [InlineData(WindowsPowerShell, "| iex")]
    [SupportedOSPlatform("windows")]
    public void A_failure_stops_the_install_with_exit_1_in_every_form_the_pages_show(string shell, string form)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "install.ps1 refuses a PowerShell that is not on Windows, so its rules can only be run on Windows.");

        using var tree = new TempTree();
        var feed = tree.NewDirectory("feed");
        const string setup = "jason-desktop-windows-x64-setup.exe";
        File.WriteAllText(Path.Combine(feed, setup), "This is not the setup the checksums name.\n");
        File.WriteAllText(Path.Combine(feed, ReleaseAssets.Manifest), """{ "schema": 1, "version": "0.49.0" }""");
        File.WriteAllText(Path.Combine(feed, ReleaseAssets.Checksums), new string('0', 64) + "  " + setup + "\n");

        var script = Path.Combine(RepositoryRoot(), "install", "install.ps1");
        string[] arguments = form switch
        {
            "-File" => ["-File", script, "-Feed", feed],
            "[scriptblock]::Create" => ["-Command", $"& ([scriptblock]::Create((Get-Content -LiteralPath {Quoted(script)} -Raw))) -Feed {Quoted(feed)}"],
            _ => ["-Command", $"Get-Content -LiteralPath {Quoted(script)} -Raw | iex"],
        };
        var temp = tree.NewDirectory("temp");
        var environment = new Dictionary<string, string>
        {
            ["LOCALAPPDATA"] = tree.Root,
            ["JASON_INSTALL_FEED"] = feed,
            ["JASON_DATA_DIR"] = tree.NewDirectory("data"),
            ["TEMP"] = temp,
            ["TMP"] = temp,
        };

        var path = AccountPath();
        var (exit, _, stderr) = PowerShell(shell, environment, arguments);

        Assert.Equal(path, AccountPath());
        Assert.True(exit == 1, $"{shell} {form} exited {exit}. It said: {stderr}");
        Assert.False(Directory.Exists(Path.Combine(tree.Root, "Programs")), $"{shell} {form} installed something.");
        Assert.True(
            Regex.Replace(stderr, @"\s+", string.Empty).Contains("doesnotmatchchecksums.txt", StringComparison.Ordinal),
            $"{shell} {form} did not say the digest was refused. It said: {stderr}");
    }

    /// <summary>
    /// And <c>install.sh</c> stops on a command that fails without a check of the script's own around it: a feed whose
    /// <c>checksums.txt</c> cannot be read makes <c>cp</c> fail, and <c>set -e</c> is what ends the run there, non-zero,
    /// in <c>cp</c>'s own words and with nothing installed. Without it the run went on, and stopped later on a check of
    /// its own, which is not where it failed.
    /// </summary>
    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void Install_sh_stops_where_a_command_fails_with_nothing_installed()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "install.sh refuses a Windows shell by name; CI's Linux leg runs this row.");
        Assert.SkipUnless(
            OperatingSystem.IsLinux() && RuntimeInformation.OSArchitecture == Architecture.X64,
            "install.sh takes an install directory on Linux on x64 alone: macOS installs Jason Desktop, and no other release is built.");

        using var tree = new TempTree();
        var feed = tree.NewDirectory("feed");
        var asset = ReleaseAssets.For("linux-x64");
        File.WriteAllText(Path.Combine(feed, ReleaseAssets.Manifest), "{\n  \"schema\": 1,\n  \"version\": \"0.7.0\"\n}\n");
        File.WriteAllText(Path.Combine(feed, asset), "Not an archive.\n");
        var checksums = Path.Combine(feed, ReleaseAssets.Checksums);
        File.WriteAllText(checksums, new string('0', 64) + "  " + asset + "\n");
        File.SetUnixFileMode(checksums, UnixFileMode.None);
        Assert.SkipWhen(Readable(checksums), "This account reads a file whatever its mode says, as root does, so there is no refusal to make.");

        var install = tree.NewDirectory("bin");
        var (exit, _, stderr) = Run(
            "/bin/sh",
            new Dictionary<string, string> { ["HOME"] = tree.NewDirectory("home") },
            Path.Combine(RepositoryRoot(), "install", "install.sh"), "--feed", feed, "--install-dir", install, "--no-modify-path");

        Assert.True(exit != 0, $"install.sh exited 0. It said: {stderr}");
        Assert.Empty(Directory.EnumerateFileSystemEntries(install));
        Assert.Contains(ReleaseAssets.Checksums, stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("install.sh:", stderr, StringComparison.Ordinal);
    }

    /// <summary>True when this account can open the file for reading.</summary>
    private static bool Readable(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// The account's own Path as the registry holds it, its text unexpanded and its kind, or null where it has none. Read,
    /// never written: a row that compares it before and after is how a run that wrote it would fail rather than pass.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static (string Value, RegistryValueKind Kind)? AccountPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Jason.Cli.Uninstall.UserPathValue.EnvironmentKey);
        return key?.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string value
            ? (value, key.GetValueKind("Path"))
            : null;
    }

    /// <summary>
    /// The shell named, run to completion: PowerShell 7 from this machine's PATH, or the Windows PowerShell 5.1 every
    /// Windows carries, which is what a fresh Windows has and what the one-liner is typed into.
    /// </summary>
    private static (int Exit, string Stdout, string Stderr) PowerShell(string shell, Dictionary<string, string> environment, params string[] arguments)
    {
        if (shell == PowerShell7)
        {
            return Pwsh(environment, ["-ExecutionPolicy", "Bypass", .. arguments]);
        }

        // A test run from PowerShell 7, as CI's is, hands its children PowerShell 7's module path, and Windows PowerShell
        // started under it finds no Expand-Archive of its own. One started from Windows has the machine's.
        environment["PSModulePath"] = Environment.GetEnvironmentVariable("PSModulePath", EnvironmentVariableTarget.Machine) ?? string.Empty;
        return Run(
            Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
            environment,
            ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", .. arguments]);
    }

    /// <summary>A path as a PowerShell literal.</summary>
    private static string Quoted(string path) => "'" + path.Replace("'", "''", StringComparison.Ordinal) + "'";

    /// <summary>
    /// <c>install.ps1</c>, run to completion by <c>pwsh</c> with a feed of the test's own and an install
    /// directory inside the test's tree. Nothing here ever writes to the real install directory or to the PATH.
    /// </summary>
    /// <summary>
    /// Runs the real <c>install.ps1</c>, and only where it is meant to run: the script refuses a PowerShell that
    /// is not on Windows before it does anything else, so off Windows every test through here would be asserting
    /// the text of that refusal rather than the rule it came to check.
    /// </summary>
    /// <remarks>
    /// The skip is here rather than in each test, because a test that forgets it does not fail on the machine
    /// its author is using — it fails on two of the three CI runners, which is the worst place to find out.
    /// </remarks>
    private static (int Exit, string Stdout, string Stderr) RunInstallPs1(TempTree tree, string feed, params string[] arguments)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "install.ps1 refuses a PowerShell that is not on Windows, so its rules can only be run on Windows.");

        string[] fixedArguments =
        [
            "-File", Path.Combine(RepositoryRoot(), "install", "install.ps1"), "-Feed", feed,
        ];

        return Pwsh(new Dictionary<string, string> { ["LOCALAPPDATA"] = tree.Root }, [.. fixedArguments, .. arguments]);
    }

    /// <summary>
    /// <c>pwsh</c>, found on this machine's PATH, run to completion. A machine without it skips the test rather
    /// than failing it: every CI runner has pwsh, and there is nothing here to install.
    /// </summary>
    private static (int Exit, string Stdout, string Stderr) Pwsh(IReadOnlyDictionary<string, string> environment, params string[] arguments)
    {
        var pwsh = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(directory => new[] { Path.Combine(directory, "pwsh.exe"), Path.Combine(directory, "pwsh") })
            .FirstOrDefault(File.Exists);
        Assert.SkipWhen(pwsh is null, "pwsh is not on this machine's PATH; every CI runner has it.");

        return Run(pwsh!, environment, ["-NoProfile", "-NonInteractive", .. arguments]);
    }

    /// <summary>One program, run to completion, with the environment a test wants it to have.</summary>
    private static (int Exit, string Stdout, string Stderr) Run(string executable, IReadOnlyDictionary<string, string> environment, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        // PowerShell reports telemetry over the network unless told not to, and no test in this repository
        // reaches the network.
        start.Environment["POWERSHELL_TELEMETRY_OPTOUT"] = "1";
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in environment)
        {
            start.Environment[name] = value;
        }

        using var process = System.Diagnostics.Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromSeconds(120)))
        {
            // What it had said before it stopped is the only evidence of where it stopped, so it goes into the
            // failure rather than out with the process.
            process.Kill(entireProcessTree: true);
            Task.WhenAny(Task.WhenAll(stdout, stderr), Task.Delay(TimeSpan.FromSeconds(10))).GetAwaiter().GetResult();
            Assert.Fail(
                $"{Path.GetFileName(executable)} did not finish in two minutes.\n"
                + $"stdout so far: {(stdout.IsCompletedSuccessfully ? stdout.Result : "(not readable)")}\n"
                + $"stderr so far: {(stderr.IsCompletedSuccessfully ? stderr.Result : "(not readable)")}");
        }

        return (process.ExitCode, stdout.Result, stderr.Result);
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

/// <summary>
/// A temporary directory tree one test owns and one test deletes — the files a script under test is pointed
/// at, and the install directory it is told to use, so that nothing here touches the machine's own.
/// </summary>
internal sealed class TempTree : IDisposable
{
    public TempTree()
    {
        Root = Path.Combine(Path.GetTempPath(), "jason-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string NewDirectory(string name)
    {
        var directory = Path.Combine(Root, Guid.NewGuid().ToString("N"), name);
        Directory.CreateDirectory(directory);
        return directory;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A child process or the OS may still hold a handle; a temporary directory left behind is harmless.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
