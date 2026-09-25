using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Jason.Contracts.Update;

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
/// script refuses by name — so its rules stay shape guards, and the ubuntu job is what runs it.
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
    /// <c>install.ps1</c> runs the statements the Windows repair prints: one definition of how Jason goes on a
    /// Windows PATH, held to the script line by line and in order.
    /// </summary>
    /// <remarks>
    /// Two spellings of it are how the kind of an account's Path was lost in three places at once. What the
    /// statements do is run, against a registry key of the test's own, in <c>UserPathRegistryTests</c>.
    /// </remarks>
    [Fact]
    public void The_installer_runs_the_statements_the_repair_prints()
    {
        var script = Code("install.ps1").Split('\n').Select(line => line.Trim()).ToList();

        // One block, line after line, rather than each statement somewhere after the one before it: a line
        // between two of them — an edit to the value, a second SetValue — would have kept an order-only match
        // green while the installer ran something the repair does not print.
        var statements = Jason.Cli.Uninstall.PathEntry.RegistryStatements("$directory", $"'{Jason.Cli.Uninstall.UserPathValue.EnvironmentKey}'");
        var at = script.IndexOf(statements[0]);
        Assert.True(at >= 0, $"install.ps1 does not run: {statements[0]}");
        for (var index = 1; index < statements.Count; index++)
        {
            Assert.True(
                at + index < script.Count && script[at + index] == statements[index],
                $"install.ps1 does not run, on the line after the one before it: {statements[index]}");
        }

        Assert.DoesNotContain(script, line => line.Contains("GetEnvironmentVariable('Path'", StringComparison.Ordinal));
        Assert.DoesNotContain(script, line => line.Contains("SetEnvironmentVariable('Path'", StringComparison.Ordinal));
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

    /// <summary>The same rule in <c>install.sh</c>: compared, and compared before the archive is downloaded.</summary>
    [Fact]
    public void Install_sh_compares_the_manifest_with_the_version_that_was_asked_for()
    {
        var lines = Code("install.sh").Split('\n');
        var compared = Array.FindIndex(lines, line => line.Contains("\"$LATEST\"", StringComparison.Ordinal) && line.Contains("\"$VERSION\"", StringComparison.Ordinal));
        var downloaded = Array.FindIndex(lines, line => line.Contains("fetch \"$ASSET\"", StringComparison.Ordinal));

        Assert.True(compared >= 0, "install.sh never compares the manifest's version with the one --version asked for.");
        Assert.True(downloaded > compared, "install.sh downloads the archive before checking that the manifest names the version that was asked for.");
        Assert.Contains(lines, line => line.Contains("fail ", StringComparison.Ordinal) && line.Contains("$VERSION", StringComparison.Ordinal) && line.Contains("$LATEST", StringComparison.Ordinal));
    }

    /// <summary>
    /// Windows keeps the user's PATH in the registry rather than in a profile file, where a directory is its own
    /// mark: the script adds it to the user's PATH only when it is not there. No profile file is edited.
    /// </summary>
    [Fact]
    public void Install_ps1_adds_the_directory_to_the_user_path_only_when_it_is_not_there()
    {
        var text = Script("install.ps1");
        Assert.Contains("CurrentUser.CreateSubKey('Environment')", text, StringComparison.Ordinal);
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
        Assert.True(ci.IndexOf("'DoNotExpandEnvironmentNames'", job, StringComparison.Ordinal) > job, "the install.ps1 job no longer reads the account's Path as the registry holds it.");
        Assert.True(ci.IndexOf("the install turned the user Path from ExpandString", job, StringComparison.Ordinal) > job, "the install.ps1 job no longer asserts the kind of the account's Path.");
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
            "-InstallDir", tree.NewDirectory("programs"), "-NoModifyPath",
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
        foreach ($name in 'rid', 'asset', 'base', 'repositoryUrl', 'fromWeb', 'tmp', 'latest', 'target', 'installed', 'archive', 'expected', 'actual', 'unpacked', 'started', 'previous', 'directory', 'userPath', 'entries') {
            if (Test-Path "Variable:$name") { "left behind: $name" }
        }
        if (Test-Path Function:Install-Jason) { "left behind: Install-Jason" }
        try { $null = $aNameThisSessionNeverDefined; "strict mode=off" } catch { "strict mode=on" }
        """;

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
            "-File", Path.Combine(RepositoryRoot(), "install", "install.ps1"),
            "-InstallDir", tree.NewDirectory("programs"), "-NoModifyPath", "-Feed", feed,
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
