using System.Diagnostics;
using System.IO.Compression;
using System.Reflection.PortableExecutable;
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
/// scripts do with the licence is run on each script's own platforms, in <see cref="InstallDesktopScriptTests"/>.
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
    /// offering a download that does not exist. <c>install.sh</c> installs Jason Desktop: on macOS its zip for the machine's
    /// architecture, on Linux its deb or its AppImage, all of which a release publishes; <c>install.ps1</c> installs the
    /// Windows setup.
    /// Neither names another platform's archive but in the sentence that refuses a machine no release is built for.
    /// </summary>
    [Fact]
    public void The_downloads_a_script_names_are_ones_a_release_publishes()
    {
        var sh = Script("install.sh");
        Assert.Empty(AssetNames().Matches(sh));
        foreach (var (name, asset) in new[] { ("DEB_ASSET", "jason-desktop-linux-x64.deb"), ("APPIMAGE_ASSET", "jason-desktop-linux-x64.AppImage") })
        {
            Assert.Contains($"{name}=\"{asset}\"", Code("install.sh"), StringComparison.Ordinal);
            Assert.Contains(asset, ReleaseAssets.Desktop);
        }

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
    /// The digest is compared before a download is opened, run or installed — a file that fails its checksum is never
    /// unpacked, run or handed to apt. What can be read from the text is the order: the digest is computed before the
    /// first unpack (ditto on macOS), the first run of an AppImage, and the first install by apt, on the lines that are code
    /// and not on a comment that happens to name both; <see cref="InstallDesktopScriptTests"/> runs each route.
    /// </summary>
    [Fact]
    [Critical(Family.I1, "install.sh verifies the checksum first")]
    public void Install_sh_checks_the_digest_before_it_opens_runs_or_installs_a_download()
    {
        var code = Code("install.sh");
        var digest = code.IndexOf("sha256sum", StringComparison.Ordinal);
        foreach (var opened in new[] { "ditto -x", "--appimage-extract", "\"$APT_GET\" install" })
        {
            var at = code.IndexOf(opened, StringComparison.Ordinal);
            Assert.True(digest >= 0 && at > digest, $"The checksum is not computed before {opened}.");
        }
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
    /// <see cref="InstallDesktopScriptTests"/>.
    /// </summary>
    [Fact]
    public void Install_sh_prints_where_the_files_will_be_as_text()
    {
        var code = Code("install.sh");

        Assert.Contains($"printf '{HarnessRootLines.Where("%s")}\\n' \"$ROOT_HOME/Jason\"", code, StringComparison.Ordinal);

        // The home loses every separator at its end, not one: `/home/marta//` is the runtime's `/home/marta` too.
        Assert.Contains("    ROOT_HOME=\"$HOME\"\n    while [ \"${ROOT_HOME%/}\" != \"$ROOT_HOME\" ]; do\n        ROOT_HOME=\"${ROOT_HOME%/}\"\n    done\n", code, StringComparison.Ordinal);
        Assert.Contains($"printf '%s\\n' '{HarnessRootLines.DefaultProject}'", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Install_sh_sets_the_execute_bit() =>
        Assert.Contains("chmod", Script("install.sh"), StringComparison.Ordinal);

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
        // The one Stop-Process is inside the block for a setup that did not end, never after it.
        var block = watch[notEnded..];
        block = block[..(block.IndexOf("\n    }\n", StringComparison.Ordinal) + 1)];
        Assert.True(block.Contains("Stop-Process -Id $process.Id", StringComparison.Ordinal), "Watch-Setup ends the setup outside the block for a setup that did not end");
    }

    /// <summary>
    /// The Windows proof reads the machine after an uninstall only once the uninstaller it watched has ended. The uninstaller
    /// removes the application's folder before it has finished - its Installed apps entry goes after - so a reading taken
    /// when the folder goes races it, and a key listed can be gone when it is read. After every wait for the application's
    /// folder to go, one helper, <c>Wait-UninstallerEnded</c>, waits up to 150 s for the uninstaller runs to end and says each
    /// one's exit code, failing by its own sentence where one still runs; no reading of the machine, no program run, no return
    /// and no end of the step stands between such a wait and that helper. Listing the Installed apps entries treats a key that
    /// went between the listing and its read as no entry: a defence under the wait, not a replacement for it.
    /// </summary>
    [Fact]
    public void The_Windows_proof_reads_the_machine_after_an_uninstall_only_once_the_uninstaller_it_watched_has_ended()
    {
        var windows = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "scripts", "Test-InstallationWindows.ps1")).Replace("\r\n", "\n", StringComparison.Ordinal);
        // Comments aside: a word in a comment is neither a wait nor a reading.
        var code = Regex.Replace(windows, @"(?m)^[ \t]*#.*$", string.Empty);

        // Every wait for the application's folder to go, and what follows it to the end of its step or function: the helper
        // stands before the first reading of the machine, program run or return, and before the end.
        var waits = Regex.Matches(code, @"\n[^\n]*(?:Wait-Until \{|while \()[^\n]*Test-Path -LiteralPath \$app\)[^\n]*");
        Assert.True(waits.Count >= 8, $"Test-InstallationWindows.ps1 has {waits.Count} wait(s) for the application's folder to go; the row reads too few to mean anything.");
        var ends = Regex.Matches(code, @"\n    '\w+' \{\n|\nfunction |\n}\n").Select(end => end.Index).ToArray();
        var reading = new Regex(@"Get-JasonUninstallEntries|Count-Entry |Test-Path -LiteralPath \$data|Test-Path -LiteralPath \$harnessRoot|Test-Path -LiteralPath \(Join-Path|Get-OwnProcesses |Get-LeftOver|Read-OwnSkill |Find-InNewShell |Wait-Program |Invoke-Program |\breturn\b");
        foreach (Match wait in waits)
        {
            var from = wait.Index + wait.Length;
            var to = ends.Where(end => end >= from).DefaultIfEmpty(code.Length).Min();
            var after = code[from..to];
            var waited = after.IndexOf("Wait-UninstallerEnded ", StringComparison.Ordinal);
            var read = reading.Match(after);
            Assert.True(waited >= 0, $"after the wait '{wait.Value.Trim()}' the step ends without waiting for the uninstaller it watched to end (Wait-UninstallerEnded)");
            Assert.True(!read.Success || waited < read.Index, $"after the wait '{wait.Value.Trim()}' the machine is read by '{read.Value}' before the wait for the uninstaller it watched to end (Wait-UninstallerEnded)");
        }

        // The helper: the runs it is given and every uninstaller running now, each waited for by its handle within 150 s,
        // its exit code said, and one still running at the bound failing the step by its own sentence.
        var helperAt = code.IndexOf("\nfunction Wait-UninstallerEnded(", StringComparison.Ordinal);
        Assert.True(helperAt >= 0, "Test-InstallationWindows.ps1 has no Wait-UninstallerEnded, the one wait for an uninstaller's end before the machine is read");
        var helper = code[helperAt..];
        helper = helper[..helper.IndexOf("\n}\n", StringComparison.Ordinal)];
        // The copy NSIS runs on is matched by any program in any ~nsu*.tmp folder, whatever NSIS names the folder or the
        // copy: a narrower pattern can miss a run and say none runs. PowerShell's -match is .NET's, ignoring case.
        var copy = Regex.Match(helper, @"""\$\(\$_\.ExecutablePath\)"" -match '(?<pattern>[^']+)'");
        Assert.True(copy.Success, "Wait-UninstallerEnded no longer matches the copy NSIS runs on by its path");
        var temporary = @"C:\Users\runneradmin\AppData\Local\Temp\2\";
        foreach (var path in new[] { "~nsu.tmp\\Au_.exe", "~nsuA.tmp\\Un_A.exe", "~nsu1.tmp\\Un.exe", "~nsuG.tmp\\Un_A.exe", "~NSU1F.TMP\\Un.EXE", "~nsu12.tmp\\other.exe" })
        {
            Assert.True(Regex.IsMatch(temporary + path, copy.Groups["pattern"].Value, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                $"Wait-UninstallerEnded's pattern '{copy.Groups["pattern"].Value}' misses an NSIS copy at {temporary}{path}");
        }

        foreach (var said in new[]
        {
            "$seconds = 150",
            "$_.Name -like 'Uninstall Jason*.exe' -or \"$($_.ExecutablePath)\" -match '\\\\~nsu[^\\\\]*\\.tmp\\\\[^\\\\]+\\.exe$'",
            "$null = $process.Handle",
            "if (-not $process.WaitForExit($left)) {",
            "Fail \"the uninstaller, process $id, still runs $seconds s after $since\"",
            "exit $($process.ExitCode)",
        })
        {
            Assert.True(helper.Contains(said, StringComparison.Ordinal), $"Wait-UninstallerEnded does not say: {said}");
        }

        // The leg that watches the uninstaller runs by their ids hands those runs to the helper.
        Assert.Contains("Wait-UninstallerEnded @($runs.Values | ForEach-Object { $_.Process })", code, StringComparison.Ordinal);

        // A key listed and gone before it is read is no entry; a key still there that cannot be read still fails.
        var entries = code[code.IndexOf("\nfunction Get-JasonUninstallEntries {", StringComparison.Ordinal)..];
        entries = entries[..entries.IndexOf("\n}\n", StringComparison.Ordinal)];
        Assert.Contains("$key = $_.PSPath; try { Get-ItemProperty -LiteralPath $key -ErrorAction Stop } catch { if (Test-Path -LiteralPath $key) { throw } }", entries, StringComparison.Ordinal);
    }

    /// <summary>
    /// The two helpers the Windows proof reads the machine with after an uninstall are run, not only read: a row that reads
    /// their text let one through that failed the first time it was handed a run, indexing an ordered dictionary by a process
    /// id, which it takes for a position. Each is taken from the script's own text into a <c>pwsh</c> of the row's own, with
    /// <c>Say</c>, <c>Fail</c> and the process listing stood in, and the uninstaller runs are stand-ins the row starts, windowed
    /// programs as an NSIS uninstaller is. <c>Wait-UninstallerEnded</c> is kept by its <c>-under</c> to the row's own folder, so
    /// that it never waits on a program of the machine the row runs on; the proof gives no <c>-under</c>. A run handed to it that
    /// ends by itself is said with its exit code, once though the scan sees it too; one still running at a bound of 2 s fails by
    /// the helper's own sentence, and the row ends that one process by its id, the only one it ends; with no run it says none
    /// runs, and an <c>Uninstall Jason.exe</c> outside its folder is not taken; an <c>Uninstall Jason … .exe</c> in its folder,
    /// handed to it by nobody, is found by its scan, its handle taken, waited for and said with its exit code; and so is the
    /// copy NSIS runs on from a <c>~nsu*.tmp</c> folder. Every other stand-in ends by itself and is waited for.
    /// <c>Get-JasonUninstallEntries</c> is run against stand-ins
    /// of the two registry reads, so that no row writes the registry: a key listed and gone before it is read is no entry, a
    /// key of another application is none, and a key still there that cannot be read fails.
    /// </summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public void The_Windows_proof_helpers_after_an_uninstall_run_as_the_proof_runs_them()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The helpers wait on Windows processes and read Windows' Installed apps entries, and the proof runs them on Windows only.");

        var script = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "scripts", "Test-InstallationWindows.ps1")).Replace("\r\n", "\n", StringComparison.Ordinal);
        string Function(string opening)
        {
            var at = script.IndexOf("\nfunction " + opening, StringComparison.Ordinal);
            Assert.True(at >= 0, $"Test-InstallationWindows.ps1 has no function {opening}");
            var end = script.IndexOf("\n}\n", at, StringComparison.Ordinal);
            return script[(at + 1)..(end + 2)];
        }

        // The proof's own prelude, taken from its text: the block of lines after param()'s closing parenthesis, up to the first
        // blank line. Of it, the lines that set the session's own settings, a preference variable or the strict mode, are
        // taken; the one other line it holds, making the folder of the step's -Out parameter, reads a parameter the row does
        // not give and writes outside the row's tree, and is left. A line of any other form fails the row, for its author to
        // decide which side it is on.
        var paramsEnd = script.IndexOf("\n)\n\n", StringComparison.Ordinal);
        Assert.True(paramsEnd >= 0, "Test-InstallationWindows.ps1 has no param() block followed by its prelude");
        var preludeAt = paramsEnd + "\n)\n\n".Length;
        var block = script[preludeAt..script.IndexOf("\n\n", preludeAt, StringComparison.Ordinal)].Split('\n');
        var prelude = block.Where(line => Regex.IsMatch(line, @"^(?:\$\w+Preference = .+|Set-StrictMode .+)$")).ToArray();
        Assert.Equal(["New-Item -ItemType Directory -Force -Path $Out | Out-Null"], block.Except(prelude));
        Assert.Contains("$ErrorActionPreference = 'Stop'", prelude);
        Assert.Contains("Set-StrictMode -Version Latest", prelude);

        var standIn = UninstallerRunStandIn.Value;
        Assert.Equal(Subsystem.WindowsGui, StandInPrograms.SubsystemOf(standIn));

        using var tree = new TempTree();
        var under = tree.NewDirectory("uninstallers");
        var outside = tree.NewDirectory("outside");
        var started = Path.Combine(tree.Root, "started.txt");
        string[] programs = [Path.Combine(under, "Uninstall Jason.exe"), Path.Combine(under, "Uninstall Jason 0.49.0.exe"), Path.Combine(under, "~nsuT.tmp", "Un.exe"), Path.Combine(outside, "Uninstall Jason.exe")];
        foreach (var program in programs)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(program)!);
            File.WriteAllBytes(program, standIn);
        }

        static string Quoted(string text) => "'" + text.Replace("'", "''", StringComparison.Ordinal) + "'";
        var harness = Path.Combine(tree.Root, "harness.ps1");
        File.WriteAllText(harness, HelperHarness
            .Replace("@PRELUDE@", string.Join('\n', prelude), StringComparison.Ordinal)
            .Replace("@UNDER@", Quoted(under), StringComparison.Ordinal)
            .Replace("@OUTSIDE@", Quoted(outside), StringComparison.Ordinal)
            .Replace("@STARTED@", Quoted(started), StringComparison.Ordinal)
            .Replace("@KEYS@", Quoted(tree.NewDirectory("keys")), StringComparison.Ordinal)
            .Replace("@FUNCTIONS@", Function("Wait-UninstallerEnded(") + Function("Get-JasonUninstallEntries {"), StringComparison.Ordinal));

        int exit;
        string stdout, stderr;
        try
        {
            (exit, stdout, stderr) = Pwsh(new Dictionary<string, string>(), "-File", harness);
        }
        finally
        {
            EndStandIns(started, programs);
        }

        var output = "\n" + stdout + stderr;
        // What each case said, kept with the row's result whether it passes or not.
        TestContext.Current.TestOutputHelper?.WriteLine(output);
        Assert.True(exit == 0, output);
        var lines = stdout.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        string Started(string row) => Regex.Match(stdout, $@"^\({row}\) started (\d+)\r?$", RegexOptions.Multiline).Groups[1].Value;
        string[] Said(string row) => [.. lines.Where(line => line.StartsWith($"({row}) ", StringComparison.Ordinal) && !line.StartsWith($"({row}) started ", StringComparison.Ordinal))];
        double Seconds(string line, string pattern)
        {
            var match = Regex.Match(line, pattern);
            Assert.True(match.Success, $"'{line}' is not '{pattern}'{output}");
            return double.Parse(match.Groups["seconds"].Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        // (a) A run handed to the helper that ends by itself: said once, with its exit code, though the scan sees it too.
        var a = Started("a");
        Assert.True(a.Length > 0, output);
        var saidA = Assert.Single(Said("a"));
        Seconds(saidA, $@"^\(a\) said: the uninstaller, process {a}, ended (?<seconds>[0-9.]+) s into the wait that began once the application folder went, exit 7$");

        // (b) A run still going at the bound: the processes listed, the helper's own sentence, at the bound and not at 150 s,
        // and that one process ended by the row, by its id.
        var b = Started("b");
        var saidB = Said("b");
        Assert.True(saidB.Length == 3, output);
        Assert.Equal("(b) listed the processes", saidB[0]);
        var waited = Seconds(saidB[1], $@"^\(b\) failed after (?<seconds>[0-9.]+) s: the uninstaller, process {b}, still runs 2 s after the application folder went$");
        Assert.InRange(waited, 1.5, 30);
        Assert.Equal($"(b) ended {b}: True", saidB[2]);

        // (c) No run in the helper's folder, while an Uninstall Jason.exe runs outside it: none runs, and that one ends by itself.
        var c = Started("c");
        Assert.Equal(["(c) said: no uninstaller runs once the application folder went", $"(c) ended by itself {c}: True"], Said("c"));

        // (d) An Uninstall Jason … .exe in the helper's folder, handed to it by nobody: found by the scan, its handle taken,
        // waited for, and said with its exit code.
        var d = Started("d");
        Seconds(Assert.Single(Said("d")), $@"^\(d\) said: the uninstaller, process {d}, ended (?<seconds>[0-9.]+) s into the wait that began once the application folder went, exit 5$");

        // (e) The copy NSIS runs on from a ~nsu*.tmp folder, handed to the helper by nobody: found by the scan and waited for.
        var e = Started("e");
        Seconds(Assert.Single(Said("e")), $@"^\(e\) said: the uninstaller, process {e}, ended (?<seconds>[0-9.]+) s into the wait that began once the asking run was ended, exit 3$");

        // (f) The Installed apps entries: the key that went is no entry, another application's is none, an unreadable one fails.
        Assert.Equal(["(f) entries: there", "(f) failed on an unreadable key: Requested registry access is not allowed."], Said("f"));

        // Every stand-in the harness started and did not end ended by itself before the harness's end.
        Assert.DoesNotContain(lines, line => line.StartsWith("left ", StringComparison.Ordinal));
    }

    /// <summary>
    /// The pwsh the helper row runs. Say, Fail and the process listing are stood in, each line said prefixed by its case; every
    /// stand-in it starts is written to a file by its id, for the row to end by that id should the harness not get to its end.
    /// The harness ends one process, the stand-in of (b) by its id; every other stand-in ends by itself and is waited for. It
    /// opens with the settings lines of the proof's own prelude, so the helpers run as strictly as the proof runs them.
    /// </summary>
    private const string HelperHarness = """
        @PRELUDE@
        $under = @UNDER@
        $outside = @OUTSIDE@
        $started = @STARTED@
        $uninstallKeys = @KEYS@
        $all = [System.Collections.Generic.List[System.Diagnostics.Process]]::new()
        $case = ''
        function Say([string] $line) { [Console]::Out.WriteLine("$case said: $line") }
        function Fail([string] $why) { throw $why }
        function Write-JasonProcesses { [Console]::Out.WriteLine("$case listed the processes") }
        function Write-Failed($record) { [Console]::Out.WriteLine("$case failed: $($record.Exception.Message)") }
        function Start-StandIn([string] $file, [int] $milliseconds, [int] $exit) {
            $process = Start-Process -FilePath $file -ArgumentList "$milliseconds", "$exit" -PassThru
            $null = $process.Handle
            Add-Content -LiteralPath $started -Value $process.Id
            $all.Add($process)
            [Console]::Out.WriteLine("$case started $($process.Id)")
            return $process
        }
        function Wait-StandIn([System.Diagnostics.Process] $process) {
            [Console]::Out.WriteLine("$case ended by itself $($process.Id): $($process.WaitForExit(15000))")
        }
        function End-StandIn([System.Diagnostics.Process] $process) {
            if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force; $null = $process.WaitForExit(10000) }
            [Console]::Out.WriteLine("$case ended $($process.Id): $($process.HasExited)")
        }

        @FUNCTIONS@

        $case = '(a)'
        try { $run = Start-StandIn (Join-Path $under 'Uninstall Jason.exe') 500 7; Wait-UninstallerEnded @($run) -under $under } catch { Write-Failed $_ }

        $case = '(b)'
        $run = Start-StandIn (Join-Path $under 'Uninstall Jason.exe') 20000 0
        $from = Get-Date
        try { Wait-UninstallerEnded @($run) -seconds 2 -under $under; [Console]::Out.WriteLine("$case did not fail") } catch { [Console]::Out.WriteLine("$case failed after $([Math]::Round(((Get-Date) - $from).TotalSeconds, 1)) s: $($_.Exception.Message)") }
        End-StandIn $run

        $case = '(c)'
        $run = Start-StandIn (Join-Path $outside 'Uninstall Jason.exe') 3000 0
        try { Wait-UninstallerEnded @() -under $under } catch { Write-Failed $_ }
        Wait-StandIn $run

        $case = '(d)'
        try { $null = Start-StandIn (Join-Path $under 'Uninstall Jason 0.49.0.exe') 3000 5; Wait-UninstallerEnded @() -under $under } catch { Write-Failed $_ }

        $case = '(e)'
        try { $null = Start-StandIn (Join-Path $under '~nsuT.tmp\Un.exe') 3000 3; Wait-UninstallerEnded @() 'the asking run was ended' -under $under } catch { Write-Failed $_ }

        $case = '(f)'
        & {
            $there = Join-Path $uninstallKeys 'there'
            $gone = Join-Path $uninstallKeys 'gone'
            $other = Join-Path $uninstallKeys 'other'
            $unreadable = Join-Path $uninstallKeys 'unreadable'
            $null = New-Item -ItemType Directory -Path $there, $other, $unreadable
            $listed = @($there, $gone, $other)
            function Get-ChildItem { [CmdletBinding()] param([string] $LiteralPath) if ($LiteralPath -cne $uninstallKeys) { throw "listed $LiteralPath" }; foreach ($key in $listed) { [pscustomobject]@{ PSPath = $key } } }
            function Get-ItemProperty {
                [CmdletBinding()] param([string] $LiteralPath)
                switch (Split-Path -Leaf $LiteralPath) {
                    'there' { [pscustomobject]@{ DisplayName = 'Jason Desktop 0.49.0'; PSChildName = 'there' } }
                    'other' { [pscustomobject]@{ DisplayName = 'Another application'; PSChildName = 'other' } }
                    'gone' { throw [System.Management.Automation.ItemNotFoundException]::new("Cannot find path '$LiteralPath' because it does not exist.") }
                    default { throw [System.UnauthorizedAccessException]::new('Requested registry access is not allowed.') }
                }
            }
            try { [Console]::Out.WriteLine("$case entries: $((@(Get-JasonUninstallEntries) | ForEach-Object { $_.PSChildName }) -join ', ')") } catch { Write-Failed $_ }
            $listed = @($there, $unreadable)
            try { $found = @(Get-JasonUninstallEntries); [Console]::Out.WriteLine("$case read an unreadable key as $($found.Count) entries") } catch { [Console]::Out.WriteLine("$case failed on an unreadable key: $($_.Exception.Message)") }
        }

        $case = ''
        foreach ($process in $all) { if (-not $process.WaitForExit(15000)) { [Console]::Out.WriteLine("left $($process.Id)") } }
        """;

    /// <summary>
    /// Every stand-in the helper row's harness wrote down that still runs, ended by its id, and only where the process of that
    /// id is one of the row's own programs: a harness that did not get to its own end leaves none behind.
    /// </summary>
    private static void EndStandIns(string started, string[] programs)
    {
        if (!File.Exists(started))
        {
            return;
        }

        foreach (var line in File.ReadAllLines(started))
        {
            if (!int.TryParse(line, out var id))
            {
                continue;
            }

            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(id);
                if (!process.HasExited && programs.Contains(process.MainModule?.FileName, StringComparer.OrdinalIgnoreCase))
                {
                    process.Kill();
                    process.WaitForExit(10000);
                }
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Ended already, or no longer one of the row's own.
            }
        }
    }

    /// <summary>
    /// A stand-in for an uninstaller run, built by the .NET Framework's C# compiler every Windows carries as a program with no
    /// window, as an NSIS uninstaller is: it waits the milliseconds its first argument gives and ends with the code its second
    /// gives.
    /// </summary>
    private static readonly Lazy<byte[]> UninstallerRunStandIn = new(() =>
    {
        var csc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework64", "v4.0.30319", "csc.exe");
        Assert.SkipUnless(File.Exists(csc), "This Windows has no .NET Framework C# compiler to build the stand-in with.");
        using var tree = new TempTree();
        var source = Path.Combine(tree.Root, "uninstaller.cs");
        var output = Path.Combine(tree.Root, "uninstaller.exe");
        File.WriteAllText(source, "static class Program { static int Main(string[] a) { System.Threading.Thread.Sleep(int.Parse(a[0])); return int.Parse(a[1]); } }\n");
        var (exit, stdout, stderr) = Run(csc, new Dictionary<string, string>(), "/nologo", "/target:winexe", "/out:" + output, source);
        Assert.True(exit == 0, stdout + stderr);
        return File.ReadAllBytes(output);
    });

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
    /// The Windows leg that updates from the one-installation release holds what that release's own <c>jason update
    /// apply</c> printed by two readers, run here as the proof runs them: taken from the script's own text into a
    /// <c>pwsh</c> of the row's own that opens with the settings lines of the proof's prelude, and handed what the verb
    /// said, its standard error and its document as <c>Invoke-Program</c> keeps them, with its exit code.
    /// <c>Read-Document</c> takes the document; <c>Get-HandedFinishFault</c> says what the run lacks, its exit first, or
    /// nothing (<see cref="HandOverCases"/>). The leg fails on what they say.
    /// </summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public void The_Windows_proof_reads_the_one_installation_release_s_update_as_the_proof_runs_its_readers()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The Windows proof's readers run in the PowerShell 7 the proof runs on Windows only.");

        var script = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "scripts", "Test-InstallationWindows.ps1")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var step = WindowsProofStep(script, "FromOneInstallation");
        var read = step.IndexOf("$fault = Get-HandedFinishFault $update.Exit (Read-Document $update.Said) $fromVersion $Version\n", StringComparison.Ordinal);
        var held = step.IndexOf("if ($fault) { Fail ", StringComparison.Ordinal);
        Assert.True(read >= 0 && held > read, "the step FromOneInstallation does not fail on what the readers say of the update's exit and document");
        Assert.DoesNotContain("$update.Exit -ne 0", step, StringComparison.Ordinal);
        AssertTheFinishSaysWhatTheReadersRead();

        using var tree = new TempTree();
        var cases = HandOverCases(tree);
        var listed = Path.Combine(tree.Root, "cases.json");
        File.WriteAllText(listed, JsonSerializer.Serialize(cases.Select(item => new { name = item.Name, file = item.File, exit = item.Exit, from = item.From, to = item.To })));
        var harness = Path.Combine(tree.Root, "harness.ps1");
        File.WriteAllText(harness, HandOverHarness
            .Replace("@PRELUDE@", WindowsProofSettings(script), StringComparison.Ordinal)
            .Replace("@CASES@", "'" + listed.Replace("'", "''", StringComparison.Ordinal) + "'", StringComparison.Ordinal)
            .Replace("@FUNCTIONS@", WindowsProofFunction(script, "Read-Document(") + WindowsProofFunction(script, "Get-HandedFinishFault("), StringComparison.Ordinal));

        var (exit, stdout, stderr) = Pwsh(new Dictionary<string, string>(), "-File", harness);
        var output = "\n" + stdout + stderr;
        TestContext.Current.TestOutputHelper?.WriteLine(output);
        Assert.True(exit == 0, output);
        var said = stdout.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t', 2))
            .ToDictionary(parts => parts[0], parts => parts.Length > 1 ? parts[1] : "(no tab)", StringComparer.Ordinal);
        Assert.Equal(cases.Select(item => item.Name).Order(StringComparer.Ordinal), said.Keys.Order(StringComparer.Ordinal));
        foreach (var item in cases)
        {
            Assert.True(said[item.Name] == item.Fault, $"{item.Name}: the Windows readers said '{said[item.Name]}', not '{item.Fault}'{output}");
        }
    }

    /// <summary>
    /// The macOS leg that updates from the one-installation release reads what that release's own <c>jason update apply</c>
    /// printed, and how it ended, by one python3 reader, isolated, by the Windows readers' rule and in their words, and fails
    /// on what it says; the reader is run on the same cases (<see cref="HandOverCases"/>), each the verb's document as the
    /// leg keeps its standard output, and its exit status as the leg keeps it.
    /// </summary>
    [Fact]
    public void The_macOS_proof_reads_the_one_installation_release_s_update_by_the_Windows_proof_s_rule_and_words()
    {
        var macos = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "scripts", "test-installation-macos.sh")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var step = MacProofStep(macos, "from-one-installation");
        var reader = Regex.Match(step, @"handed_read=""\$\(python3 -I -c '(?<code>[^']*)' ""\$OUT/update-from-one-installation\.json"" ""\$old"" ""\$version"" ""\$status""\)"" \|\| fail """);
        var ran = step.IndexOf(" > \"$OUT/update-from-one-installation.json\" 2> \"$OUT/update-from-one-installation.err\" || status=$?\n", StringComparison.Ordinal);
        var held = step.IndexOf("[ -z \"$handed_read\" ] || fail ", StringComparison.Ordinal);
        Assert.True(reader.Success, "the step from-one-installation does not read the update's document by one isolated python3 reader handed the version it went from, the version it went to and the verb's exit status");
        Assert.True(ran >= 0 && reader.Index > ran && held > reader.Index, "the step from-one-installation does not fail on what its reader says of the update's exit status and document");
        Assert.DoesNotContain("[ \"$status\" -eq 0 ] || fail ", step[ran..], StringComparison.Ordinal);
        AssertTheFinishSaysWhatTheReadersRead();

        var python = OperatingSystem.IsWindows() ? "python" : "python3";
        Assert.SkipUnless(Run(python, new Dictionary<string, string>(), "--version").Exit == 0, $"{python} is not on this machine's PATH, so the reader cannot be run here");
        using var tree = new TempTree();
        foreach (var item in HandOverCases(tree))
        {
            var (exit, stdout, stderr) = Run(python, new Dictionary<string, string>(), "-I", "-c", reader.Groups["code"].Value, item.File, item.From, item.To, item.Exit.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Assert.True(exit == 0, $"{item.Name}: {stderr}");
            Assert.True(item.Fault == stdout.TrimEnd('\r', '\n'), $"{item.Name}: the macOS reader said '{stdout.Trim()}', not '{item.Fault}'");
        }
    }

    /// <summary>
    /// The Windows leg that updates from the one-installation release begins from the release its fetched manifest.json
    /// names, installed with nothing of Jason's running after its install.ps1, and a runtime started as the previous
    /// release's is (a wait only where the start refuses because the runtime it started is still running). Its verb runs
    /// under a bound that keeps what it said in the leg's file as it says it; past the bound, what the verb set going and
    /// that outlived it - Jason Desktop and whatever runs from the updater's folder - is ended by what it is. After the
    /// update, Installed apps is read once its one entry is this version's, or at the bound, and nothing of the update may
    /// be left running 30 s on, the setup it ran being given that long to end, each one left named.
    /// </summary>
    [Fact]
    public void The_Windows_one_installation_leg_begins_from_its_pinned_release_and_leaves_nothing_of_the_update_running()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "scripts", "Test-InstallationWindows.ps1")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var step = WindowsProofStep(script, "FromOneInstallation");
        int At(string text)
        {
            var at = step.IndexOf(text, StringComparison.Ordinal);
            Assert.True(at >= 0, $"the step FromOneInstallation does not say: {text}");
            return at;
        }

        var updater = At("$updater = Join-Path $env:LOCALAPPDATA 'jason-desktop-updater'\n");
        var manifest = At("$manifest = Get-Content -LiteralPath (Join-Path $OneInstallation 'manifest.json') -Raw | ConvertFrom-Json\n");
        var named = At("if (-not $manifestVersion -or $manifestVersion -ceq $Version) { Fail ");
        var install = At("(Join-Path $OneInstallation 'install.ps1')");
        var pinned = At("if ($fromVersion -cne $manifestVersion) { Fail ");
        var nothing = At("$running = @(Get-OwnProcesses $app)\nif ($running.Count -ne 0) { Fail ".Replace("\n", "\n        ", StringComparison.Ordinal));
        var runtime = At("if (-not (Start-PreviousRuntime $carried $quiet \"the one-installation release's runtime\")) { Fail ");
        Assert.True(manifest < named && named < install && install < pinned && pinned < nothing && nothing < runtime,
            "the step FromOneInstallation does not read its manifest's version, install, hold the version installed to it and that nothing runs, then start a runtime, in that order");
        Assert.DoesNotContain("Wait-Until { (Invoke-Program $carried @('runtime', 'status')", step, StringComparison.Ordinal);
        Assert.Contains("function Start-PreviousRuntime([string] $jason, [hashtable] $environment = @{}, [string] $who = \"the previous release's runtime\") {", script, StringComparison.Ordinal);

        var kept = At("$kept = Join-Path $Out 'update-from-one-installation.json'\n");
        var bounded = At("'--drain-seconds', '0') $quiet 900 $kept\n");
        var outlived = At("foreach ($outlived in @(Get-OwnProcesses $app | Where-Object { $_.Name -ceq 'Jason.exe' }) + @(Get-OwnProcesses $updater)) {\n");
        var ended = At("Stop-Process -Id $outlived.ProcessId -Force -ErrorAction SilentlyContinue\n");
        var rethrown = At("    throw\n        } finally {\n");
        Assert.True(updater < kept && kept < bounded && bounded < outlived && outlived < ended && ended < rethrown,
            "the step FromOneInstallation does not keep what its verb says, and end what that verb set going past its bound before it fails");
        Assert.True(step[..bounded].LastIndexOf("try {\n", StringComparison.Ordinal) > kept && step[bounded..outlived].Contains("} catch {\n", StringComparison.Ordinal),
            "the step FromOneInstallation ends what its verb set going only where the verb's run threw");
        Assert.DoesNotContain("Set-Content -LiteralPath (Join-Path $Out 'update-from-one-installation.json')", step, StringComparison.Ordinal);

        var read = At("$fault = Get-HandedFinishFault $update.Exit (Read-Document $update.Said) $fromVersion $Version\n");
        var waited = At("while (-not (& $oneAtVersion) -and [DateTime]::UtcNow -lt $until) { Start-Sleep -Milliseconds 500 }\n");
        var entries = At("$entries = @(Get-JasonUninstallEntries)\n");
        var still = At("$leftOf = { @(Get-OwnProcesses $app | Where-Object { $_.Name -ceq 'Jason.exe' }) + @(Get-OwnProcesses $updater) }\n");
        var grace = At("$graceUntil = [DateTime]::UtcNow.AddSeconds(30)\n");
        var given = At("while ($stillRunning.Count -ne 0 -and [DateTime]::UtcNow -lt $graceUntil) { Start-Sleep -Seconds 1; $stillRunning = @(& $leftOf) }\n");
        var named2 = At("if ($stillRunning.Count -ne 0) { Fail ");
        var shell = At("$found = @(Find-InNewShell 'jason')\n");
        Assert.True(read < waited && waited < entries && entries < still && still < grace && grace < given && given < named2 && named2 < shell,
            "the step FromOneInstallation does not wait for Installed apps' entry, then give what the update left 30 s to end and hold that nothing of it runs, before a new shell is asked for jason");
        Assert.Contains("$oneAtVersion = { $now = @(Get-JasonUninstallEntries); $now.Count -eq 1 -and $now[0].PSObject.Properties['DisplayVersion'] -and [string]$now[0].DisplayVersion -ceq $Version }\n", step, StringComparison.Ordinal);
        Assert.Contains("$until = [DateTime]::UtcNow.AddSeconds(120)\n", step, StringComparison.Ordinal);
        Assert.Contains("$($_.ProcessId) $($_.CommandLine)", step[named2..shell], StringComparison.Ordinal);
    }

    /// <summary>
    /// The Windows proof's <c>Invoke-Program</c>, run as the proof runs it: given a file to keep what the program says, it
    /// writes each line there as it is said, so a run its bound ends leaves the lines said before it; at the bound it ends
    /// the program and every process under it, the one the program started included, and fails the step. It ends nothing
    /// else: not the feed server the step started before the program, not a stranger whose dead parent's id the process
    /// table gives as the program's, not one of the runner's own processes, and not one the table makes this shell's
    /// parent, the last two shown under the program as created after it. A run within the bound gives its exit code, its
    /// file holding every line. <c>Write-JasonProcesses</c>, which names the machine's own processes of Jason's, is a
    /// stand-in here, so the row reads none of them, and so is <c>Get-CimInstance</c>, which shows the real table as the
    /// row makes it.
    /// </summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public void The_Windows_proof_s_bound_on_a_program_keeps_what_it_said_and_ends_every_process_under_it()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The Windows proof's Invoke-Program runs in the PowerShell 7 the proof runs on Windows only.");

        var script = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "scripts", "Test-InstallationWindows.ps1")).Replace("\r\n", "\n", StringComparison.Ordinal);
        using var tree = new TempTree();
        string Quoted(string path) => "'" + path.Replace("'", "''", StringComparison.Ordinal) + "'";
        var harness = Path.Combine(tree.Root, "harness.ps1");
        File.WriteAllText(harness, BoundHarness
            .Replace("@PRELUDE@", WindowsProofSettings(script), StringComparison.Ordinal)
            .Replace("@OUT@", Quoted(tree.NewDirectory("out")), StringComparison.Ordinal)
            .Replace("@NORMAL@", Quoted(Path.Combine(tree.Root, "kept-within.txt")), StringComparison.Ordinal)
            .Replace("@BOUNDED@", Quoted(Path.Combine(tree.Root, "kept-at-bound.txt")), StringComparison.Ordinal)
            .Replace("@PIDFILE@", Path.Combine(tree.Root, "started.txt").Replace("'", "''", StringComparison.Ordinal), StringComparison.Ordinal)
            .Replace("@FUNCTIONS@", WindowsProofFunction(script, "Fail(") + WindowsProofFunction(script, "Invoke-Program("), StringComparison.Ordinal));

        var (exit, stdout, stderr) = Pwsh(new Dictionary<string, string>(), "-File", harness);
        var output = "\n" + stdout + stderr;
        TestContext.Current.TestOutputHelper?.WriteLine(output);
        Assert.True(exit == 0, output);
        var said = stdout.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        string Line(string label) => said.FirstOrDefault(line => line.StartsWith(label + ": ", StringComparison.Ordinal))?[(label.Length + 2)..] ?? $"(no line '{label}')";
        Assert.True(Line("exit") == "3", $"a run within its bound did not give its exit code:{output}");
        Assert.True(Line("kept") == "one|two", $"a run within its bound did not keep every line it said:{output}");
        Assert.True(Line("bound").EndsWith("did not end within 15 s", StringComparison.Ordinal), $"a run past its bound did not fail the step at the bound:{output}");
        Assert.True(Line("kept at the bound") == "said before the bound", $"a run past its bound did not leave the lines it said before it:{output}");
        Assert.True(Line("the process the program started") == "ended", $"a run past its bound left the process the program started:{output}");
        Assert.True(Line("the feed server") == "left alone", $"a run past its bound ended the feed server the step started before it:{output}");
        Assert.True(Line("a stranger under a reused id") == "left alone", $"a run past its bound ended a stranger whose dead parent's id is the program's:{output}");
        Assert.True(Line("one of the runner's") == "left alone", $"a run past its bound ended one of the runner's processes:{output}");
        Assert.True(Line("this shell's parent") == "left alone", $"a run past its bound ended this shell's parent:{output}");
    }

    /// <summary>
    /// The pwsh the bound's row runs: the settings lines of the proof's own prelude, a folder of its own for the step's
    /// failure record, a stand-in for <c>Write-JasonProcesses</c>, and the proof's own <c>Fail</c> and
    /// <c>Invoke-Program</c>; then a program that ends by itself, and one that says a line, starts a process of its own
    /// and never ends, under a bound of 15 s. Four processes of the row's own, started before that program, stand for
    /// what the bound must leave alone; <c>Get-CimInstance</c> shows the real process table with the stranger's parent
    /// given as the program, the runner's process and this shell's parent shown under the program, created a second after
    /// it, and this shell's parent being the last of the four. One line per finding, its label and what was seen, apart by
    /// a colon.
    /// </summary>
    private const string BoundHarness = """
        @PRELUDE@
        $Out = @OUT@
        function Write-JasonProcesses { Write-Host '  (the machine''s own processes are not named by this row)' }
        function Start-Sleeper { Start-Process -FilePath pwsh -ArgumentList '-NoProfile', '-NonInteractive', '-Command', 'Start-Sleep -Seconds 120' -PassThru -NoNewWindow }
        function Get-CimInstance {
            $real = @(CimCmdlets\Get-CimInstance -ClassName Win32_Process)
            $program = @($real | Where-Object { $_.ParentProcessId -eq $PID -and "$($_.CommandLine)".Contains('said before the bound') }) | Select-Object -First 1
            foreach ($process in $real) {
                $shown = [pscustomobject]@{ ProcessId = $process.ProcessId; ParentProcessId = $process.ParentProcessId; Name = $process.Name; CreationDate = $process.CreationDate; ExecutablePath = $process.ExecutablePath; CommandLine = $process.CommandLine }
                if ($program) {
                    if ($process.ProcessId -eq $stranger.Id) { $shown.ParentProcessId = $program.ProcessId }
                    if ($process.ProcessId -eq $runner.Id) { $shown.ParentProcessId = $program.ProcessId; $shown.Name = 'Runner.Listener.exe'; $shown.CreationDate = $program.CreationDate.AddSeconds(1) }
                    if ($process.ProcessId -eq $ancestor.Id) { $shown.ParentProcessId = $program.ProcessId; $shown.CreationDate = $program.CreationDate.AddSeconds(1) }
                    if ($process.ProcessId -eq $PID) { $shown.ParentProcessId = $ancestor.Id }
                }
                $shown
            }
        }
        function Write-Left([string] $label, [System.Diagnostics.Process] $process) {
            [Console]::Out.WriteLine("${label}: $(if (Get-Process -Id $process.Id -ErrorAction SilentlyContinue) { 'left alone' } else { 'ended' })")
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        }

        @FUNCTIONS@

        $normal = @NORMAL@
        $bounded = @BOUNDED@
        $ran = Invoke-Program 'pwsh' @('-NoProfile', '-NonInteractive', '-Command', "Write-Output 'one'; Write-Output 'two'; exit 3") @{} 120 $normal
        [Console]::Out.WriteLine("exit: $($ran.Exit)")
        [Console]::Out.WriteLine("kept: $(@(Get-Content -LiteralPath $normal) -join '|')")
        $server = Start-Sleeper
        $stranger = Start-Sleeper
        $runner = Start-Sleeper
        $ancestor = Start-Sleeper
        Start-Sleep -Seconds 1
        $inner ="Write-Output 'said before the bound'; `$p = Start-Process -FilePath pwsh -ArgumentList '-NoProfile', '-NonInteractive', '-Command', 'Start-Sleep -Seconds 120' -PassThru -NoNewWindow; Set-Content -LiteralPath '@PIDFILE@' -Value `$p.Id; Start-Sleep -Seconds 120"
        try {
            $null = Invoke-Program 'pwsh' @('-NoProfile', '-NonInteractive', '-Command', $inner) @{} 15 $bounded
            [Console]::Out.WriteLine('bound: not reached')
        } catch {
            [Console]::Out.WriteLine("bound: $($_.Exception.Message)")
        }
        $started = 0
        if (Test-Path -LiteralPath '@PIDFILE@') { $started = [int] "$(Get-Content -LiteralPath '@PIDFILE@' -Raw)".Trim() }
        $until = [DateTime]::UtcNow.AddSeconds(10)
        while ($started -gt 0 -and [DateTime]::UtcNow -lt $until -and (Get-Process -Id $started -ErrorAction SilentlyContinue)) { Start-Sleep -Milliseconds 200 }
        [Console]::Out.WriteLine("kept at the bound: $(@(Get-Content -LiteralPath $bounded -ErrorAction SilentlyContinue) -join '|')")
        [Console]::Out.WriteLine("the process the program started: $(if ($started -le 0) { 'never started' } elseif (Get-Process -Id $started -ErrorAction SilentlyContinue) { 'still running' } else { 'ended' })")
        if ($started -gt 0) { Stop-Process -Id $started -Force -ErrorAction SilentlyContinue }
        Write-Left 'the feed server' $server
        Write-Left 'a stranger under a reused id' $stranger
        Write-Left 'one of the runner''s' $runner
        Write-Left 'this shell''s parent' $ancestor
        exit 0
        """;

    /// <summary>
    /// The macOS leg that updates from the one-installation release keeps, by one trap for its way out however it ends, the
    /// feed server it starts ended, what of Jason Desktop and its ShipIt is still running printed and kept, and the
    /// desktop's log and ShipIt's logs in the leg's folder before the uninstall after it takes them, each item ending true
    /// whatever its own commands end with, so the items after it are run under the prelude's errexit. It begins from the
    /// release its fetched manifest.json names, read by an isolated python3. Its runtime start and its verb each run in a
    /// subshell, so a bound reached ends in a line of the leg's own: the start waits only where it refuses because the
    /// runtime it started is still running. After the update, the processes of Jason Desktop and of its own ShipIt, never
    /// another application's, are printed and given 30 s to end, launchd's ShipIt job is printed whole, and one still
    /// running fails the leg by name.
    /// </summary>
    [Fact]
    public void The_macOS_one_installation_leg_begins_from_its_pinned_release_and_says_what_the_update_leaves()
    {
        var macos = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "scripts", "test-installation-macos.sh")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var step = MacProofStep(macos, "from-one-installation");
        int At(string text)
        {
            var at = step.IndexOf(text, StringComparison.Ordinal);
            Assert.True(at >= 0, $"the step from-one-installation does not say: {text}");
            return at;
        }

        var trap = Assert.Single(Regex.Matches(step, @"(?m)^    trap '(?<said>[^']*)' EXIT$"));
        foreach (var said in new[]
        {
            "[ -z \"$server\" ] || kill \"$server\" 2>/dev/null || true",
            "[ -z \"${placed:-}\" ] || { left=\"$(left_running \"$placed\")\"; echo \"${left:-none}\" > \"$OUT/one-installation-left-running.txt\"; echo \"left running as this step ends: ${left:-none}\"; } || true;",
            "keep_desktop_log one-installation-desktop.jsonl",
            "for log in \"$HOME\"/Library/Caches/ai.jasonharness.desktop.ShipIt/ShipIt_*.log; do [ ! -f \"$log\" ] || cp \"$log\" \"$OUT/one-installation-$(basename \"$log\")\" 2>/dev/null || true; done",
        })
        {
            Assert.Contains(said, trap.Groups["said"].Value, StringComparison.Ordinal);
        }

        Assert.Single(Regex.Matches(step, @"(?m)^\s*trap\b"));
        var manifest = At("manifest_version=\"$(python3 -I -c '");
        var read = At("' \"$one/manifest.json\")\" || fail ");
        var install = At("sh \"$one/install.sh\" || fail ");
        var pinned = At("[ \"$old\" = \"$manifest_version\" ] || fail ");
        Assert.True(trap.Index < manifest && manifest < read && read < install && install < pinned,
            "the step from-one-installation does not set its trap, read its manifest's version, install, and hold the version installed to it, in that order");

        var start = At("( within 120 \"$old's jason runtime start\" \"$carried\" runtime start ) > \"$OUT/one-installation-runtime-start.txt\" 2>&1 || status=$?\n");
        var refused = At("{ grep -qF '\"runtime_start_failed\"' \"$OUT/one-installation-runtime-start.txt\" && grep -qF 'is still running' \"$OUT/one-installation-runtime-start.txt\"; }");
        var waited = At("wait_until 180 \"a runtime answering $old's carried jason runtime status\"");
        Assert.True(pinned < start && start < refused && refused < waited, "the step from-one-installation does not wait for its runtime only where the start refused because the runtime it started is still running");
        Assert.DoesNotContain("if ! within 120", step, StringComparison.Ordinal);

        var update = At("( within 900 \"$old's jason update apply --version $version\" \"$carried\" update apply --version \"$version\" --feed http://127.0.0.1:8099/manifest.json --drain-seconds 0 ) > \"$OUT/update-from-one-installation.json\" 2> \"$OUT/update-from-one-installation.err\" || status=$?\n");
        var handed = At("[ -z \"$handed_read\" ] || fail ");
        var processes = At("still_running=\"$(left_running \"$placed\")\"\n");
        var job = At("launchctl print \"gui/$(id -u)/ai.jasonharness.desktop.ShipIt\" > \"$OUT/one-installation-shipit-job.txt\" 2>&1");
        var shown = At("cat \"$OUT/one-installation-shipit-job.txt\"\n");
        var left = At("[ -z \"$still_running\" ] || fail ");
        var shell = At("fresh 'command -v jason; jason --version'");
        Assert.True(waited < update && update < handed && handed < processes && processes < job && job < shown && shown < left && left < shell,
            "the step from-one-installation does not print what the update leaves running and launchd's ShipIt job, then fail on what is left, before a new login shell is asked for jason");
        Assert.Contains("deadline=$(( $(date +%s) + 30 ))\n", step, StringComparison.Ordinal);
        Assert.DoesNotContain("pgrep -lf ShipIt", macos, StringComparison.Ordinal);
    }

    /// <summary>
    /// What the macOS proof names as left running of Jason Desktop at its place, run as the proof runs it: the
    /// application's own processes, and its ShipIt, known by launchd's job of Jason Desktop's or by the Squirrel.framework
    /// inside the application; another application's ShipIt, or its own processes, are never named. <c>pgrep</c> is a
    /// stand-in here, matching its pattern to a process table of the row's own as <c>pgrep -lf</c> does, so the row reads
    /// none of the machine's processes. It runs in bash: /bin/bash on Linux and macOS, Git Bash on a Windows that has it.
    /// </summary>
    [Fact]
    public void The_macOS_proof_names_only_its_own_desktop_and_ShipIt_as_left_running()
    {
        var bash = OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe") : "/bin/bash";
        Assert.SkipUnless(File.Exists(bash), $"This machine has no {bash} to run the macOS proof's functions in.");

        var script = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "scripts", "test-installation-macos.sh")).Replace("\r\n", "\n", StringComparison.Ordinal);
        using var tree = new TempTree();
        var harness = Path.Combine(tree.Root, "harness.sh");
        File.WriteAllText(harness, LeftRunningHarness
            .Replace("@SETTINGS@", MacProofSettings(script), StringComparison.Ordinal)
            .Replace("@FUNCTIONS@", MacProofFunction(script, "left_running"), StringComparison.Ordinal));

        var (exit, stdout, stderr) = Run(bash, new Dictionary<string, string>(), "--noprofile", "--norc", harness.Replace('\\', '/'));
        var output = "\n" + stdout + stderr;
        TestContext.Current.TestOutputHelper?.WriteLine(output);
        Assert.True(exit == 0, output);
        var left = SaidLine(stdout, "left").Split(';', StringSplitOptions.RemoveEmptyEntries).Select(process => process.Split(' ')[0]).ToList();
        Assert.True(left.SequenceEqual(["4101", "4102", "4105", "4106"]), $"what is left running of Jason Desktop at its place is not its own process and its own ShipIt alone, known by either half of the pattern:{output}");
        Assert.True(SaidLine(stdout, "another's alone") == string.Empty, $"another application's ShipIt and processes were named as left running:{output}");
    }

    /// <summary>
    /// The bash the row of what is left running runs: the shell options of the proof's own prelude, a stand-in for
    /// <c>pgrep</c> matching its pattern to a table of the row's own as <c>pgrep -lf</c> does, and the proof's own
    /// <c>left_running</c>, asked of Jason Desktop at its place with its own processes running, and then with another
    /// application's alone. Its ShipIt is there three times: by both halves of the pattern, by the framework's path alone,
    /// and by the job's label and cache alone, so each half is held on its own. One line per finding, its label and what
    /// was seen, apart by a colon.
    /// </summary>
    private const string LeftRunningHarness = """
        @SETTINGS@
        TABLE='4101 /Applications/Jason.app/Contents/MacOS/Jason --update-apply --await-go
        4102 /Applications/Jason.app/Contents/Frameworks/Squirrel.framework/Resources/ShipIt ai.jasonharness.desktop.ShipIt /Users/runner/Library/Caches/ai.jasonharness.desktop.ShipIt/ShipItState.plist
        4103 /Applications/Other.app/Contents/Frameworks/Squirrel.framework/Resources/ShipIt com.example.other.ShipIt /Users/runner/Library/Caches/com.example.other.ShipIt/ShipItState.plist
        4104 /Applications/Other.app/Contents/MacOS/Other
        4105 /Applications/Jason.app/Contents/Frameworks/Squirrel.framework/Resources/ShipIt
        4106 ShipIt ai.jasonharness.desktop.ShipIt /Users/runner/Library/Caches/ai.jasonharness.desktop.ShipIt/ShipItState.plist'
        pgrep() { [ "$1" = -lf ] || return 2; printf '%s\n' "$TABLE" | grep -E -- "$2"; }

        @FUNCTIONS@

        echo "left: $(left_running /Applications/Jason.app)"
        TABLE='4103 /Applications/Other.app/Contents/Frameworks/Squirrel.framework/Resources/ShipIt com.example.other.ShipIt /Users/runner/Library/Caches/com.example.other.ShipIt/ShipItState.plist
        4104 /Applications/Other.app/Contents/MacOS/Other'
        echo "another's alone: $(left_running /Applications/Jason.app)"
        """;

    /// <summary>
    /// Each leg of both proofs says its time where it ends, in whole seconds from its begin, with the job's time so far
    /// beside it, so that a leg's step and the job are bounded by what a run measured. A begin closes the leg before it,
    /// which says its own time where it never ended, and only then starts this leg's clock; an end reads the clock before it
    /// fails a leg whose compare differed, and says it either way. Every such line is said, and so is in the job's summary.
    /// </summary>
    [Fact]
    public void Each_proof_leg_says_its_time_and_the_job_s_where_it_ends()
    {
        var root = RepositoryRoot();
        var windows = File.ReadAllText(Path.Combine(root, ".github", "scripts", "Test-InstallationWindows.ps1")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var macos = File.ReadAllText(Path.Combine(root, ".github", "scripts", "test-installation-macos.sh")).Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Equal(2, WindowsClockFiles(windows).Count);
        Assert.Contains("\n        Close-Leg\n        Start-LegClock\n", WindowsProofStep(windows, "Begin"), StringComparison.Ordinal);
        var end = WindowsProofStep(windows, "End");
        var read = end.IndexOf("\n        $clock = Get-LegClock\n", StringComparison.Ordinal);
        var differing = end.IndexOf("\n            Say \"$Leg - ended $clock, its compare differing\"\n            exit 1\n", StringComparison.Ordinal);
        Assert.True(read >= 0 && differing > read, "the Windows End does not read the leg's clock first, and say it where the leg's compare differed before it fails the step");
        Assert.EndsWith("\n        Say \"$Leg - ended $clock\"", end, StringComparison.Ordinal);
        Assert.Contains("\n    Say \"the leg '$open' did not end: it is closed $(Get-LegClock)\"\n", WindowsProofFunction(windows, "Close-Leg {"), StringComparison.Ordinal);

        Assert.Contains("\n    close_leg\n    start_leg_clock\n", MacProofStep(macos, "begin"), StringComparison.Ordinal);
        var macEnd = MacProofStep(macos, "end");
        var macRead = macEnd.IndexOf("\n    clock=\"$(leg_clock)\"\n", StringComparison.Ordinal);
        var macDiffering = macEnd.IndexOf("\n        say \"$1 - ended $clock, its compare differing\"\n        exit 1\n", StringComparison.Ordinal);
        Assert.True(macRead >= 0 && macDiffering > macRead, "the macOS end does not read the leg's clock first, and say it where the leg's compare differed before it fails the step");
        Assert.EndsWith("\n    say \"$1 - ended $clock\"", macEnd, StringComparison.Ordinal);
        Assert.Contains("\n    say \"the leg '$open' did not end: it is closed $(leg_clock)\"\n", MacProofFunction(macos, "close_leg"), StringComparison.Ordinal);
    }

    /// <summary>
    /// The Windows proof's leg clock, run as the proof runs it: the first leg's begin counts the job from the start of the
    /// runner's worker that runs it, and says so; where no worker is found, or its start cannot be read, from that begin,
    /// saying why; a later begin keeps the job's start. An end says the leg's time from its begin and the job's so far, in
    /// whole seconds. <c>Get-Process</c> is a stand-in here, so the row reads none of the machine's processes, and the
    /// summary is a file of the row's own.
    /// </summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public void The_Windows_proof_s_leg_clock_counts_the_job_from_its_runner_s_worker_or_else_from_the_first_begin()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The Windows proof's leg clock runs in the PowerShell 7 the proof runs on Windows only.");

        var script = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "scripts", "Test-InstallationWindows.ps1")).Replace("\r\n", "\n", StringComparison.Ordinal);
        using var tree = new TempTree();
        string Quoted(string path) => "'" + path.Replace("'", "''", StringComparison.Ordinal) + "'";
        var summary = Path.Combine(tree.Root, "summary.md");
        var harness = Path.Combine(tree.Root, "harness.ps1");
        File.WriteAllText(harness, WindowsClockHarness
            .Replace("@PRELUDE@", WindowsProofSettings(script), StringComparison.Ordinal)
            .Replace("@SUMMARY@", Quoted(summary), StringComparison.Ordinal)
            .Replace("@FILES@", string.Join('\n', WindowsClockFiles(script)), StringComparison.Ordinal)
            .Replace("@NONE@", Quoted(tree.NewDirectory("none")), StringComparison.Ordinal)
            .Replace("@WORKER@", Quoted(tree.NewDirectory("worker")), StringComparison.Ordinal)
            .Replace("@UNREAD@", Quoted(tree.NewDirectory("unread")), StringComparison.Ordinal)
            .Replace("@FUNCTIONS@", WindowsProofFunction(script, "Say(") + WindowsProofFunction(script, "Start-LegClock {") + WindowsProofFunction(script, "Get-LegClock {"), StringComparison.Ordinal));

        var (exit, stdout, stderr) = Pwsh(new Dictionary<string, string>(), "-File", harness);
        var output = "\n" + stdout + stderr;
        TestContext.Current.TestOutputHelper?.WriteLine(output);
        Assert.True(exit == 0, output);
        AssertTheLegClockSaid(stdout, File.ReadAllText(summary), output, @" \([^)\n]+\)");
    }

    /// <summary>
    /// The macOS proof's leg clock, run as the proof runs it, as the Windows one's row runs that: the job counted from the
    /// start of the runner's worker, read as ps writes how long a process has run, days included, or else from the first
    /// leg's begin, saying why; a later begin keeps the job's start; an end says the leg's time and the job's. The worker is
    /// the one this script descends from, found by walking up its parents, never the first a runner lists. <c>pgrep</c>
    /// and <c>ps</c> are stand-ins here, so the row reads none of the machine's processes. It runs in bash: /bin/bash on
    /// Linux and macOS, Git Bash on a Windows that has it.
    /// </summary>
    [Fact]
    public void The_macOS_proof_s_leg_clock_counts_the_job_from_its_runner_s_worker_or_else_from_the_first_begin()
    {
        var bash = OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe") : "/bin/bash";
        Assert.SkipUnless(File.Exists(bash), $"This machine has no {bash} to run the macOS proof's functions in.");

        var script = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "scripts", "test-installation-macos.sh")).Replace("\r\n", "\n", StringComparison.Ordinal);
        using var tree = new TempTree();
        string Quoted(string path) => "'" + path.Replace('\\', '/').Replace("'", "'\\''", StringComparison.Ordinal) + "'";
        var summary = Path.Combine(tree.Root, "summary.md");
        var harness = Path.Combine(tree.Root, "harness.sh");
        File.WriteAllText(harness, MacClockHarness
            .Replace("@SETTINGS@", MacProofSettings(script), StringComparison.Ordinal)
            .Replace("@SUMMARY@", Quoted(summary), StringComparison.Ordinal)
            .Replace("@NONE@", Quoted(tree.NewDirectory("none")), StringComparison.Ordinal)
            .Replace("@WORKER@", Quoted(tree.NewDirectory("worker")), StringComparison.Ordinal)
            .Replace("@UNREAD@", Quoted(tree.NewDirectory("unread")), StringComparison.Ordinal)
            .Replace("@DAYS@", Quoted(tree.NewDirectory("days")), StringComparison.Ordinal)
            .Replace("@FUNCTIONS@", MacProofFunction(script, "say") + MacProofFunction(script, "start_leg_clock") + MacProofFunction(script, "leg_clock"), StringComparison.Ordinal));

        var (exit, stdout, stderr) = Run(bash, new Dictionary<string, string>(), "--noprofile", "--norc", harness.Replace('\\', '/'));
        var output = "\n" + stdout + stderr;
        TestContext.Current.TestOutputHelper?.WriteLine(output);
        Assert.True(exit == 0, output);
        AssertTheLegClockSaid(stdout, File.ReadAllText(summary), output, string.Empty);
        Assert.True(SaidLine(stdout, "days") == "93784", $"a runner's worker a day, two hours, three minutes and four seconds old, as ps writes it, was not read so:{output}");
    }

    /// <summary>
    /// What both proofs' leg clocks say in their rows: the job begun at the leg's begin with no worker, or with one whose
    /// start could not be read (<paramref name="why"/> matching what the Windows proof adds of why), and 90 s before it with
    /// a worker 90 s old, each said once, in the summary; the job's start kept by a later begin; and an end 5 s after its
    /// begin, the job 100 s old. A second boundary passed between two readings makes either a second more.
    /// </summary>
    private static void AssertTheLegClockSaid(string stdout, string summary, string output, string why)
    {
        Assert.True(SaidLine(stdout, "none") == "0", $"with no runner's worker the job was not counted from the first leg's begin:{output}");
        Assert.True(SaidLine(stdout, "worker") is "90" or "91", $"the job was not counted from the start of a runner's worker 90 s old:{output}");
        Assert.True(SaidLine(stdout, "a later begin keeps the job's start") == "True", $"a later leg's begin moved the job's start:{output}");
        Assert.True(SaidLine(stdout, "unread") == "0", $"with a runner's worker whose start could not be read the job was not counted from the first leg's begin:{output}");
        Assert.Matches("^(5|6) s after its begin, with the job at (100|101) s$", SaidLine(stdout, "ended"));
        var lines = summary.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length == 3, $"the summary does not hold one line for each job's clock started:\n{summary}{output}");
        Assert.Equal("- the job's time is counted from this leg's begin: no runner's worker was found; it had run 0 s when this leg began", lines[0]);
        Assert.Matches("^- the job's time is counted from the start of the runner's worker that runs it; it had run (90|91) s when this leg began$", lines[1]);
        Assert.Matches("^- the job's time is counted from this leg's begin: the start of the runner's worker could not be read" + why + "; it had run 0 s when this leg began$", lines[2]);
    }

    /// <summary>The words a harness said after its label and a colon, on the first line it said them.</summary>
    private static string SaidLine(string stdout, string label) =>
        stdout.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .FirstOrDefault(line => line.StartsWith(label + ": ", StringComparison.Ordinal))?[(label.Length + 2)..] ?? $"(no line '{label}')";

    /// <summary>
    /// The pwsh the Windows leg clock's row runs: the settings lines of the proof's own prelude, a summary of the row's own,
    /// a stand-in for <c>Get-Process</c> answering the workers the row sets, the proof's own <c>Say</c>,
    /// <c>Start-LegClock</c> and <c>Get-LegClock</c>, and its two clock files' lines, set again for each leg's folder. One
    /// line per finding, its label and what was seen, apart by a colon.
    /// </summary>
    private const string WindowsClockHarness = """
        @PRELUDE@
        $env:GITHUB_STEP_SUMMARY = @SUMMARY@
        $script:workers = @()
        function Get-Process { $script:workers }

        @FUNCTIONS@

        $clockFiles = {
        @FILES@
        }
        function Read-Moment([string] $file) { [long] "$(Get-Content -LiteralPath $file -Raw)".Trim() }

        $Out = @NONE@
        . $clockFiles
        Start-LegClock
        [Console]::Out.WriteLine("none: $((Read-Moment $legBegan) - (Read-Moment $jobBegan))")
        $now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
        Set-Content -LiteralPath $jobBegan -Value ($now - 100)
        Set-Content -LiteralPath $legBegan -Value ($now - 5)
        [Console]::Out.WriteLine("ended: $(Get-LegClock)")

        $Out = @WORKER@
        . $clockFiles
        $script:workers = @([pscustomobject]@{ StartTime = [DateTime]::Now.AddSeconds(-90) })
        Start-LegClock
        [Console]::Out.WriteLine("worker: $((Read-Moment $legBegan) - (Read-Moment $jobBegan))")
        $first = Read-Moment $jobBegan
        $script:workers = @([pscustomobject]@{ StartTime = [DateTime]::Now.AddSeconds(-500) })
        Start-LegClock
        [Console]::Out.WriteLine("a later begin keeps the job's start: $((Read-Moment $jobBegan) -eq $first)")

        $Out = @UNREAD@
        . $clockFiles
        $script:workers = @([pscustomobject]@{ Id = 4242 })
        Start-LegClock
        [Console]::Out.WriteLine("unread: $((Read-Moment $legBegan) - (Read-Moment $jobBegan))")
        exit 0
        """;

    /// <summary>
    /// The bash the macOS leg clock's row runs: the shell options of the proof's own prelude, a summary of the row's own,
    /// stand-ins for <c>pgrep</c> and <c>ps</c>, and the proof's own <c>say</c>, <c>start_leg_clock</c> and
    /// <c>leg_clock</c>; the last leg's folder says its clock to no summary. The <c>pgrep</c> answers only
    /// <c>pgrep -a -x Runner.Worker</c>: the worker is an ancestor of the step's shell, and macOS's pgrep leaves its own
    /// ancestors out of what it matches unless <c>-a</c> is given, so without it the job was never counted from the worker
    /// on a Mac. It lists the workers the row sets, another job's first: a runner may run more than one, and only the one
    /// this script descends from is its job's. The <c>ps</c> answers a process's parent for each step from the harness's
    /// own shell up to its worker and on to the first process, and how long each worker has run, as ps writes it; the
    /// other job's worker has run an hour less a second. One line per finding, its label and what was seen, apart by a
    /// colon.
    /// </summary>
    private const string MacClockHarness = """
        @SETTINGS@
        GITHUB_STEP_SUMMARY=@SUMMARY@
        WORKERS=""
        ETIME=""
        pgrep() { [ "$*" = "-a -x Runner.Worker" ] || return 2; [ -n "$WORKERS" ] && printf '%s\n' $WORKERS; }
        ps() {
            case "$*" in
                "-o ppid= -p $$") echo "  500" ;;
                "-o ppid= -p 500") echo " 4242" ;;
                "-o ppid= -p 4242") echo "    1" ;;
                "-o etime= -p 4242") printf '   %s\n' "$ETIME" ;;
                "-o etime= -p 4343") printf '   %s\n' "59:59" ;;
                *) return 1 ;;
            esac
        }

        @FUNCTIONS@

        apart() { echo $(( $(cat "$OUT/leg-began.txt") - $(cat "$OUT/job-began.txt") )); }

        OUT=@NONE@
        start_leg_clock
        echo "none: $(apart)"
        now="$(date +%s)"
        printf '%s\n' "$(( now - 100 ))" > "$OUT/job-began.txt"
        printf '%s\n' "$(( now - 5 ))" > "$OUT/leg-began.txt"
        echo "ended: $(leg_clock)"

        OUT=@WORKER@
        WORKERS="4343 4242"
        ETIME="01:30"
        start_leg_clock
        echo "worker: $(apart)"
        first="$(cat "$OUT/job-began.txt")"
        ETIME="08:20"
        start_leg_clock
        if [ "$(cat "$OUT/job-began.txt")" = "$first" ]; then echo "a later begin keeps the job's start: True"; else echo "a later begin keeps the job's start: False"; fi

        OUT=@UNREAD@
        ETIME="??"
        start_leg_clock
        echo "unread: $(apart)"

        GITHUB_STEP_SUMMARY=""
        OUT=@DAYS@
        ETIME="1-02:03:04"
        start_leg_clock
        echo "days: $(apart)"
        """;

    /// <summary>
    /// What the one-installation release's own <c>jason update apply</c> printed, each case in a file of its own in the row's
    /// tree: lines it said on standard error, then its document as the product's own type and serializer write it; with its
    /// exit code, the version it went from and the version the leg asked for, and what the readers say of it. That release's
    /// own part ends at the swap, in its own words (CarriedUpdate at v0.49.0): Jason Desktop asked to download the version
    /// and wait for the go, then the application is that version. What follows is the finishing jason's, in this build's
    /// words (<see cref="AssertTheFinishSaysWhatTheReadersRead"/>): the runtime healthy at the version, the update complete.
    /// Handed and finished on Windows, on a Mac, and at a candidate's version; then one case for each way it was not, a
    /// document of members the product never writes so among them, which both readers say in the same words.
    /// </summary>
    private static List<(string Name, string File, int Exit, string From, string To, string Fault)> HandOverCases(TempTree tree)
    {
        const string from = "0.49.0";
        const string to = "0.49.2";
        const string request = "cli-01JDX4M3V7Q2ZK8R";
        string Asked(string version) => $"asked: Jason Desktop asked to download {version} and wait for the go, request {request}";
        string[] Handed(string version, bool moves) =>
        [
            $"staged: {version}, for Jason Desktop to install",
            Asked(version),
            .. (moves ? [$@"moved: this jason's own file to C:\Users\runneradmin\AppData\Local\jason-desktop-updater\jason-carried\{request}\jason.exe, where it goes on running, so the installer finds none in the application's folder"] : Array.Empty<string>()),
            $"stopped: Jason Desktop told to go, request {request}",
            $"kept: Jason Desktop handed {version} to its installer",
            $"swapped: the application is {version}",
        ];
        string[] Finished(string fromVersion, string version, string runtime) =>
        [
            $"started: {version}",
            $"healthy: {runtime}, 31 migrations applied",
            $"complete: {fromVersion} -> {version}",
            "settle: Jason Desktop recorded the update as kept",
        ];
        static string Document(string documentFrom, string documentTo, string step, string[] steps) =>
            JsonSerializer.Serialize(new Jason.Contracts.Api.UpdateApplyResponse(documentFrom, documentTo, step, steps), JasonJson.Options);
        // A document of members the product's type never writes so, member by member, in the order the product writes them.
        static string Shaped(JsonNode? documentFrom, JsonNode? documentTo, JsonNode? step, JsonNode? steps) =>
            new JsonObject { ["from"] = documentFrom, ["to"] = documentTo, ["step"] = step, ["steps"] = steps }.ToJsonString();

        var askedTo = $"asked: Jason Desktop asked to download {to} and wait for the go, request <id>";
        var swappedTo = $"swapped: the application is {to}";
        var healthyTo = $"healthy: {to}, <n> migrations applied";
        string[] whole = [.. Handed(to, moves: true), .. Finished(from, to, $"{to}+3f9c1e7a")];
        JsonArray Steps() => new([.. whole.Select(line => (JsonNode?)JsonValue.Create(line))]);
        var error = """{"error":{"code":"update_refused_by_desktop","message":"Jason Desktop refused the update.","retryable":false}}""";
        var cases = new (string Name, string Document, int Exit, string From, string To, string Fault)[]
        {
            ("handed and finished, as on Windows", Document(from, to, "complete", whole), 0, from, to, ""),
            ("handed and finished, as on a Mac, a runtime already answering", Document(from, to, "complete", [.. Handed(to, moves: false), "started: a runtime is already answering", $"healthy: {to}, 31 migrations applied", $"complete: {from} -> {to}"]), 0, from, to, ""),
            ("handed and finished at a candidate's version", Document(from, "0.49.2-rc.1", "complete", [.. Handed("0.49.2-rc.1", moves: true), .. Finished(from, "0.49.2-rc.1", "0.49.2-rc.1+3f9c1e7a")]), 0, from, "0.49.2-rc.1", ""),
            ("handed and finished, the verb exiting 1", Document(from, to, "complete", whole), 1, from, to, "it exited with 1"),
            ("refused, exiting 1, with an error's document", error, 1, from, to, "it exited with 1"),
            ("the application already the version asked for", Document(from, to, "complete", [$"staged: {to}, for Jason Desktop to install", Asked(to), $"swapped: the application is {to} already", .. Finished(from, to, to)]), 0, from, to,
                $"no step \"{swappedTo}\" follows \"{Asked(to)}\""),
            ("finished before the swap", Document(from, to, "complete", [$"staged: {to}, for Jason Desktop to install", Asked(to), .. Finished(from, to, to), $"swapped: the application is {to}"]), 0, from, to,
                $"no step \"{healthyTo}\" follows \"{swappedTo}\""),
            ("Jason Desktop never asked", Document(from, to, "complete", [$"staged: {to}, for Jason Desktop to install", $"swapped: the application is {to}", .. Finished(from, to, to)]), 0, from, to,
                $"no step \"{askedTo}\""),
            ("the runtime healthy at the version it came from", Document(from, to, "complete", [.. Handed(to, moves: true), .. Finished(from, to, $"{from}+5b7d2c11")]), 0, from, to,
                $"no step \"{healthyTo}\" follows \"{swappedTo}\""),
            ("complete from another version", Document(from, to, "complete", [.. Handed(to, moves: true), .. Finished("0.48.7", to, $"{to}+3f9c1e7a")]), 0, from, to,
                $"no step \"complete: {from} -> {to}\" follows \"healthy: {to}+3f9c1e7a, 31 migrations applied\""),
            ("stopped at kept", Document(from, to, "kept", [.. Handed(to, moves: true)[..^1]]), 0, from, to,
                "its document ends at the step kept, not complete"),
            ("gone to another version", Document(from, "0.49.1", "complete", [.. Handed("0.49.1", moves: true), .. Finished(from, "0.49.1", "0.49.1")]), 0, from, to,
                $"its document says {from} -> 0.49.1, not {from} -> {to}"),
            ("its document's from alone another version", Document("0.48.7", to, "complete", whole), 0, from, to,
                $"its document says 0.48.7 -> {to}, not {from} -> {to}"),
            ("refused, with no document", "", 0, from, to, "it printed no document"),
            ("an error's document", error, 0, from, to, "its document has no from"),
            ("a document whose from is null", Shaped(null, to, "complete", Steps()), 0, from, to,
                $"its document says  -> {to}, not {from} -> {to}"),
            ("a document whose step is null", Shaped(from, to, null, Steps()), 0, from, to,
                "its document ends at the step , not complete"),
            ("a document whose steps are null", Shaped(from, to, "complete", null), 0, from, to,
                $"no step \"{askedTo}\""),
            ("a document whose steps are one line, not a list", Shaped(from, to, "complete", $"complete: {from} -> {to}"), 0, from, to,
                $"no step \"{askedTo}\""),
            ("a document whose steps are the asking line alone, not a list", Shaped(from, to, "complete", Asked(to)), 0, from, to,
                $"no step \"{swappedTo}\" follows \"{Asked(to)}\""),
        };

        var folder = tree.NewDirectory("updates");
        return [.. cases.Select((item, at) =>
        {
            var file = Path.Combine(folder, $"update-{at}.txt");
            File.WriteAllText(file, $"Stopping the runtime before the update to {item.To}...\nJason Desktop is downloading {item.To}.\n" + (item.Document.Length > 0 ? item.Document + "\n" : string.Empty));
            return (Name: item.Name, File: file, Exit: item.Exit, From: item.From, To: item.To, Fault: item.Fault);
        })];
    }

    /// <summary>
    /// The words the jason the one-installation release hands the rest to says once it has finished, which both proof
    /// scripts read: this build's own, where <c>UpdateApplier</c> says them, and the swap its finish records, where
    /// <c>UpdateCommands</c> adds it to the handing's steps.
    /// </summary>
    private static void AssertTheFinishSaysWhatTheReadersRead()
    {
        var applier = File.ReadAllText(Path.Combine(RepositoryRoot(), "dotnet", "src", "Jason.Cli", "Update", "UpdateApplier.cs"));
        Assert.Contains("Say(UpdateStep.Healthy, $\"{info.RuntimeVersion}, {info.Database.AppliedMigrations.Count} migrations applied\");", applier, StringComparison.Ordinal);
        Assert.Contains("Say(UpdateStep.Complete, $\"{ledger.FromVersion} -> {ledger.ToVersion}\");", applier, StringComparison.Ordinal);
        Assert.Contains("private void Say(UpdateStep step, string what) => Say(step.ToString().ToLowerInvariant(), what);", applier, StringComparison.Ordinal);
        var commands = File.ReadAllText(Path.Combine(RepositoryRoot(), "dotnet", "src", "Jason.Cli", "Commands", "UpdateCommands.cs"));
        Assert.Contains("handing.Steps.Add($\"swapped: the application is {inFlight.ToVersion}\");", commands, StringComparison.Ordinal);
    }

    /// <summary>
    /// The settings lines of the Windows proof's own prelude, taken from its text as the helper row takes them: of the block
    /// after param()'s closing parenthesis, the lines that set a preference variable or the strict mode.
    /// </summary>
    private static string WindowsProofSettings(string script)
    {
        var paramsEnd = script.IndexOf("\n)\n\n", StringComparison.Ordinal);
        Assert.True(paramsEnd >= 0, "Test-InstallationWindows.ps1 has no param() block followed by its prelude");
        var preludeAt = paramsEnd + "\n)\n\n".Length;
        var settings = script[preludeAt..script.IndexOf("\n\n", preludeAt, StringComparison.Ordinal)].Split('\n')
            .Where(line => Regex.IsMatch(line, @"^(?:\$\w+Preference = .+|Set-StrictMode .+)$"))
            .ToArray();
        Assert.Contains("$ErrorActionPreference = 'Stop'", settings);
        Assert.Contains("Set-StrictMode -Version Latest", settings);
        return string.Join('\n', settings);
    }

    /// <summary>One function of the Windows proof, by the start of its line, to the closing brace that ends it.</summary>
    private static string WindowsProofFunction(string script, string opening)
    {
        var at = script.IndexOf("\nfunction " + opening, StringComparison.Ordinal);
        Assert.True(at >= 0, $"Test-InstallationWindows.ps1 has no function {opening}");
        var end = script.IndexOf("\n}\n", at, StringComparison.Ordinal);
        return script[(at + 1)..(end + 2)];
    }

    /// <summary>
    /// The pwsh the Windows readers' row runs: the settings lines of the proof's own prelude, the proof's own two readers, and
    /// each case read from its file as the proof keeps what the verb said, with its exit code, one line per case: its name
    /// and what the readers say of it, apart by a tab.
    /// </summary>
    private const string HandOverHarness = """
        @PRELUDE@

        @FUNCTIONS@

        foreach ($case in @(Get-Content -LiteralPath @CASES@ -Raw | ConvertFrom-Json)) {
            $said = [IO.File]::ReadAllText($case.file)
            [Console]::Out.WriteLine("$($case.name)`t$(Get-HandedFinishFault ([int] $case.exit) (Read-Document $said) $case.from $case.to)")
        }
        """;

    /// <summary>One step of the Windows proof, from its label to the brace that closes it.</summary>
    private static string WindowsProofStep(string script, string label)
    {
        var at = script.IndexOf($"\n    '{label}' {{\n", StringComparison.Ordinal);
        Assert.True(at >= 0, $"Test-InstallationWindows.ps1 has no step {label}");
        var step = script[at..];
        return step[..step.IndexOf("\n    }\n", StringComparison.Ordinal)];
    }

    /// <summary>One step of the macOS proof, from its label to the two semicolons that end it.</summary>
    private static string MacProofStep(string script, string label)
    {
        var at = script.IndexOf($"\n{label})\n", StringComparison.Ordinal);
        Assert.True(at >= 0, $"test-installation-macos.sh has no step {label}");
        var step = script[at..];
        return step[..step.IndexOf("\n    ;;\n", StringComparison.Ordinal)];
    }

    /// <summary>One function of the macOS proof, from its name to the line of the closing brace that ends it.</summary>
    private static string MacProofFunction(string script, string name)
    {
        var at = script.IndexOf($"\n{name}() {{\n", StringComparison.Ordinal);
        Assert.True(at >= 0, $"test-installation-macos.sh has no function {name}");
        var end = script.IndexOf("\n}\n", at, StringComparison.Ordinal);
        return script[(at + 1)..(end + 3)];
    }

    /// <summary>The shell options of the macOS proof's own prelude: the lines of its text that set them.</summary>
    private static string MacProofSettings(string script)
    {
        var settings = Regex.Matches(script, "(?m)^set -[a-z]+(?: [a-z]+)*$").Select(match => match.Value).ToList();
        Assert.Contains("set -euo pipefail", settings);
        return string.Join('\n', settings);
    }

    /// <summary>The Windows proof's two clock files: the lines of its text that name them in the leg's folder.</summary>
    private static List<string> WindowsClockFiles(string script) =>
        [.. Regex.Matches(script, @"(?m)^\$(?:legBegan|jobBegan) = Join-Path \$Out '[a-z]+-began\.txt'$").Select(match => match.Value)];

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
    /// The macOS legs serve their feed by serve-feed.py, which does nothing between bind and listen, and wait 90 s for it,
    /// printing the server's log, its process and what holds the port where it never answers.
    /// </summary>
    [Fact]
    public void The_macOS_legs_serve_their_feed_by_the_proofs_own_server_and_say_why_it_never_answered()
    {
        var root = RepositoryRoot();
        var macos = File.ReadAllText(Path.Combine(root, ".github", "scripts", "test-installation-macos.sh")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var server = File.ReadAllText(Path.Combine(root, ".github", "scripts", "serve-feed.py")).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.DoesNotContain("http.server 80", macos, StringComparison.Ordinal);
        Assert.Equal(4, Regex.Matches(macos, @"(?m)^\s*serve_feed ""\$feed"" ""\$OUT/[a-z-]+\.log""$").Count);
        Assert.Contains("    python3 \"$(dirname \"$0\")/serve-feed.py\" \"$folder\" 8099 \"$log\" &\n", macos, StringComparison.Ordinal);
        Assert.Contains("    until=$(( $(date +%s) + 90 ))\n", macos, StringComparison.Ordinal);
        Assert.Contains("fail \"the feed on 127.0.0.1:8099 answering did not happen within 90 s; ", macos, StringComparison.Ordinal);
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
    /// On Windows the line every real install takes reads its feed over the web in Windows PowerShell 5.1, which a feed in a
    /// directory never reaches. The proof's Windows job holds it in a leg of its own: the job's feed served on this machine
    /// by the proof's one feed server, install.ps1 run by <c>powershell.exe</c> with that address and that shell said and
    /// held to Windows PowerShell 5.1, the server's own record
    /// of the manifest, the checksums and the setup each answered, and a runtime over the installed role skills saying
    /// <c>role_skills</c> is ok. The role skills come from the skills beside the application's jason, so their record is
    /// held to a folder of the application and never to the feed.
    /// </summary>
    [Fact]
    public void The_windows_proof_installs_from_a_feed_served_on_the_machine_in_Windows_PowerShell()
    {
        var ci = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "workflows", "ci.yml")).ReplaceLineEndings("\n");
        var job = ci.IndexOf("\n  union-windows:\n", StringComparison.Ordinal);
        Assert.True(job >= 0, "ci.yml no longer has the union-windows job.");
        var leg = ci.IndexOf("- name: Install from a feed served on this machine, over the web in Windows PowerShell 5.1, and uninstall from a new terminal\n", job, StringComparison.Ordinal);
        Assert.True(leg > job, "union-windows no longer installs from a feed served on this machine in Windows PowerShell 5.1.");
        var next = ci.IndexOf("\n      - ", leg + 1, StringComparison.Ordinal);
        var step = next < 0 ? ci[leg..] : ci[leg..next];
        var begin = step.IndexOf("-Step Begin -Out $env:OUT -Leg $env:LEG -Clean ", StringComparison.Ordinal);
        var served = step.IndexOf("-Step ServedFeed -Out $env:OUT -Feed $env:FEED -Version $env:VERSION -Script ", StringComparison.Ordinal);
        var end = step.IndexOf("-Step End -Out $env:OUT -Leg $env:LEG", StringComparison.Ordinal);
        Assert.True(begin >= 0 && served > begin && end > served, $"the served feed's leg is not framed by a clean begin and its end:\n{step}");

        var script = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "scripts", "Test-InstallationWindows.ps1")).ReplaceLineEndings("\n");
        var at = script.IndexOf("\n    'ServedFeed' {\n", StringComparison.Ordinal);
        Assert.True(at >= 0, "Test-InstallationWindows.ps1 has no ServedFeed step.");
        var body = script[at..script.IndexOf("\n    }\n", at, StringComparison.Ordinal)];
        foreach (var said in new[]
        {
            "$server = Start-FeedServer $Feed 8099 ",
            "Invoke-Program 'powershell.exe' @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $Script, '-Feed', $served) @{ JASON_INSTALL_FEED = $null }",
            "$served = 'http://127.0.0.1:8099'",
            "Say \"install.ps1 runs in $shellPath, PowerShell $shellVersion, edition $shellEdition\"",
            "$shellEdition -cne 'Desktop' -or -not \"$shellVersion\".StartsWith('5.1.')",
            "@('manifest.json', 'checksums.txt', 'jason-desktop-windows-x64-setup.exe')",
            "' HTTP/1\\.[01]\" 200'",
            "skills\\roles\\.jason-skills.json",
            "\"name\":\"role_skills\",\"required\":true,\"state\":\"ok\"",
        })
        {
            Assert.True(body.Contains(said, StringComparison.Ordinal), $"Test-InstallationWindows.ps1's ServedFeed step does not say: {said}");
        }

        // Every feed the script serves is served by the one feed server, which looks no name up between bind and listen.
        Assert.Contains("$arguments = @((Join-Path $PSScriptRoot 'serve-feed.py'), $folder, \"$port\", $log)", script, StringComparison.Ordinal);
        Assert.DoesNotContain("'-m', 'http.server'", script, StringComparison.Ordinal);
        // Its answer waited for up to 90 s: the server is the proof's own, and a runner's image can start it slowly.
        Assert.Contains("    $until = [DateTime]::UtcNow.AddSeconds(90)\n", script, StringComparison.Ordinal);
        Assert.Contains("\"the feed on 127.0.0.1:$port answering did not happen within 90 s\"", script, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), ".github", "scripts", "serve-feed.py")), "the one feed server is not in the tree.");
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
    /// The real proof: CI runs each script against what the same run packaged: install.ps1 from a local directory, and
    /// install.sh on Ubuntu through the apt route's leg, against the product's address as the leg serves it. The leg
    /// moves a build older than install.sh's floor for Linux up to it, reading the floor from the one line that refuses
    /// below it: a rewrite of that line would otherwise fail only the leg, late in the run.
    /// </summary>
    [Fact]
    public void Ci_runs_both_scripts_against_what_it_built()
    {
        var ci = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "workflows", "ci.yml"));
        Assert.Contains("install/install.ps1", ci, StringComparison.Ordinal);
        Assert.Contains("JASON_INSTALL_FEED", ci, StringComparison.Ordinal);
        Assert.Contains("bash .github/scripts/test-apt-route.sh apt\n", ci, StringComparison.Ordinal);
        var leg = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "scripts", "test-apt-route.sh"));
        Assert.Contains("\"$repository/install/install.sh\"", leg, StringComparison.Ordinal);
        Assert.Contains("floor=$(sed -n 's/^ *if older_than \"\\$LATEST\" \\([0-9.]*\\) .*/\\1/p' \"$repository/install/install.sh\")", leg, StringComparison.Ordinal);
        var floor = Assert.Single(Script("install.sh").Split('\n'), line => Regex.IsMatch(line, @"^ *if older_than ""\$LATEST"" [0-9.]+ "));
        Assert.Contains("if older_than \"$LATEST\" 0.49.1 ", floor, StringComparison.Ordinal);
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
        // A start cache of the child's own, gone with the child.
        using var tree = new TempTree();
        return Run(Jason.Runtime.Tests.ChildPwsh.StartInfo(tree.Root), environment, ["-NoProfile", "-NonInteractive", .. arguments]);
    }

    /// <summary>One program, run to completion, with the environment a test wants it to have.</summary>
    private static (int Exit, string Stdout, string Stderr) Run(string executable, IReadOnlyDictionary<string, string> environment, params string[] arguments) =>
        Run(new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true }, environment, arguments);

    /// <summary>One program, run to completion from a start made for it, with the environment a test wants it to have.</summary>
    private static (int Exit, string Stdout, string Stderr) Run(ProcessStartInfo start, IReadOnlyDictionary<string, string> environment, params string[] arguments)
    {
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.StandardOutputEncoding = Encoding.UTF8;
        start.StandardErrorEncoding = Encoding.UTF8;
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
                $"{Path.GetFileName(start.FileName)} did not finish in two minutes.\n"
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
