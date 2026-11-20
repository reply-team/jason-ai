using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Runtime.Versioning;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Jason.Contracts.Update;

namespace Jason.Cli.Tests.Documentation;

/// <summary>
/// The install scripts where they install Jason Desktop: <c>install.ps1</c> on Windows and <c>install.sh</c> on macOS and on
/// Linux (its Linux routes are in <c>InstallDesktopScriptTests.Linux.cs</c>). Each
/// fetches the release's package for the machine, checks it against <c>checksums.txt</c> and its signature, places the
/// application without a person, runs the jason the application carries as <c>jason install --roles</c>, reads its one
/// report, starts a runtime again where one answered before and none answers after or where the report says it stopped
/// one, ends non-zero saying which act failed when jason was not put in the terminal, and on success ends with the full
/// path of jason for a shell that was already open.
/// </summary>
/// <remarks>
/// <para>
/// Nothing real is installed or run. On Windows the feed's setup is a stand-in built by the .NET Framework's C# compiler,
/// which every Windows carries, as a windowed program as the real setup is, so the script's start of it opens no console
/// window on the machine the rows run on: run with <c>/S /D=&lt;folder&gt;</c>, it puts a stand-in carried jason and its
/// <c>version.json</c> in that folder's <c>resources\bundle</c>, under a <c>LOCALAPPDATA</c> of the test's own. The stand-in
/// jason writes each call it is given into a log, answers <c>runtime status</c> by whether a flag file of the test's exists,
/// makes the file at <c>runtime start</c>, and prints the report a row gives it for <c>install</c>. A stand-in carries no
/// signature, so a row runs a copy of the script whose signature check returns at once: a test build, and only here; one
/// row runs the published script to see the check refuse.
/// </para>
/// <para>
/// On macOS's route the commands a Mac has that a test machine may not — <c>uname</c>, <c>sysctl</c>, <c>ditto</c> and
/// <c>codesign</c> — are stand-ins first on the PATH, with an <c>mv</c> that can make a Jason.app appear just before the
/// move into its place, the application's zip is a tar the stand-in <c>ditto</c> unpacks, and
/// the script's copy names a folder of the test's own as the shared applications folder. Those rows run on Linux and macOS,
/// wherever there is a <c>/bin/sh</c>; on Windows they are skipped, and the Windows rows run there alone.
/// </para>
/// </remarks>
public sealed partial class InstallDesktopScriptTests : IDisposable
{
    private const string Version = "0.49.0";

    private const string Setup = "jason-desktop-windows-x64-setup.exe";

    /// <summary>What install.ps1 says where Jason Desktop runs: its own look before the setup, and the setup's exit 3.</summary>
    private const string DesktopRunning = "Jason Desktop is running, in its window or in the tray, or Jason could not tell whether it is, so nothing was installed: quit it - Quit in its tray menu - and run this line again.";

    /// <summary>The line the published script's signature check opens with, and the test build's in its place.</summary>
    private const string SignatureCheck = "    function Assert-Signed([string] $file) {";

    private const string ShortCircuited = "    function Assert-Signed([string] $file) { return";

    /// <summary>
    /// The line the published script's elevation check opens with. A test build answers it as a row says, not elevated by
    /// default, so no row depends on how the shell that runs the tests was started; a published run reads the real token.
    /// </summary>
    private const string ElevationCheck = "    function Test-Elevated {";

    /// <summary>What install.ps1 says in an elevated PowerShell, before it reads the feed.</summary>
    private const string ElevatedRefusal =
        "This PowerShell runs as an administrator, elevated above this account's ordinary rights, or as another account than the one signed in to this session, "
        + "and Jason Desktop, jason in the terminal and the skills would be set up the same way or in another account's profile, so nothing was downloaded or installed: "
        + "run the install line again from an ordinary PowerShell of the account signed in here, not run as an administrator.";

    /// <summary>The verification install.sh asks of codesign, as the stand-in logs it with the application's path left out.</summary>
    private const string VerifyCall = "codesign --verify --deep --strict -R=" + Requirement;

    /// <summary>A test build's signature check that says it was reached and waits for the test's word before it returns.</summary>
    private const string HeldUntilGo =
        "    function Assert-Signed([string] $file) { New-Item -ItemType File -Path $env:JASON_TEST_ASKED | Out-Null; "
        + "while (-not (Test-Path -LiteralPath $env:JASON_TEST_GO)) { Start-Sleep -Milliseconds 50 }; return";

    /// <summary>The requirement a Developer ID application of the team that signs Jason satisfies, as install.sh tests it.</summary>
    private const string Requirement =
        "anchor apple generic and certificate 1[field.1.2.840.113635.100.6.2.6] and certificate leaf[field.1.2.840.113635.100.6.1.13] and certificate leaf[subject.OU] = \"BD8M7U5YBV\"";

    /// <summary>
    /// The stand-in as the carried jason, a console program as jason is: the script runs it inside its own console and
    /// reads what it prints.
    /// </summary>
    private static readonly Lazy<byte[]> StandIn = new(() => Compile("exe"));

    /// <summary>
    /// The same stand-in as the setup and as the application's Jason.exe, windowed programs as the real ones are: the script
    /// starts the setup with Start-Process, which gives a console program a console window of its own, shown on the desktop
    /// of whoever runs the rows. Neither part prints anything.
    /// </summary>
    private static readonly Lazy<byte[]> WindowedStandIn = new(() => Compile("winexe"));

    private readonly TempTree _tree = new();

    public void Dispose() => _tree.Dispose();

    // --- the published scripts carry the release's own signers ---------------------------------------------------------

    /// <summary>
    /// The checks a test build takes out are in every published script: install.ps1 runs a setup signed by ReplyApp Inc.
    /// alone, by the simple name of its certificate, and install.sh an application of the team that signs Jason, in the
    /// shared applications folder every Mac has. A copy that changes either is a test's, never the release's.
    /// </summary>
    [Fact]
    public void The_published_scripts_hold_the_release_s_own_signers()
    {
        var ps1 = Script("install.ps1");
        Assert.Equal(1, Count(ps1, SignatureCheck + "\n        $signer = 'ReplyApp Inc.'\n"));
        Assert.DoesNotContain(ShortCircuited, ps1, StringComparison.Ordinal);
        Assert.Contains("if ($signature.Status -ne 'Valid')", ps1, StringComparison.Ordinal);
        Assert.Contains("if ($name -cne $signer)", ps1, StringComparison.Ordinal);

        var sh = Script("install.sh");
        Assert.Equal(1, Count(sh, "\nAPP_TEAM=\"BD8M7U5YBV\"\n"));
        // The team is proven by Apple's chain, not read from the signature's own claim: one line, which a test build may
        // change and the release never does.
        Assert.Equal(1, Count(sh, "\nAPP_REQUIREMENT='" + Requirement + "'\n"));
        Assert.Equal(1, Count(sh, "\nSYSTEM_APPLICATIONS=\"/Applications\"\n"));
        var code = Code("install.sh");
        Assert.Equal(1, Count(code, "codesign --verify --deep --strict -R=\"$APP_REQUIREMENT\" \"$1\""));
        Assert.Equal(1, Count(code, "codesign --verify"));
        // A new application and a kept one are held to it, the kept one before its jason is run.
        Assert.Contains("if ! signed_as_jason \"$STAGE/Jason.app\"; then", code, StringComparison.Ordinal);
        Assert.Contains("signed_as_jason \"$DEST\" || fail", code, StringComparison.Ordinal);
        Assert.True(
            code.IndexOf("signed_as_jason \"$DEST\" || fail", StringComparison.Ordinal) < code.IndexOf("if answers \"$CARRIED\"", StringComparison.Ordinal),
            "install.sh asks a kept application's jason before its signature is verified.");
    }

    /// <summary>
    /// install.sh's --help says what is true of a Mac on this release: the script installs Jason Desktop on Apple silicon
    /// only and refuses an Intel Mac, by its own choice and not for want of a build (every release publishes the Intel
    /// image), and the script, not Homebrew's formula, installs Jason Desktop.
    /// </summary>
    [Fact]
    public void The_shell_script_s_help_says_an_intel_mac_is_refused_and_homebrew_is_the_command_line_alone()
    {
        var sh = Script("install.sh");
        var start = sh.IndexOf("usage() {", StringComparison.Ordinal);
        var help = Regex.Replace(sh[start..sh.IndexOf("\nEOF\n", start, StringComparison.Ordinal)], @"\s+", " ");

        Assert.Contains("This script installs Jason Desktop on Apple silicon Macs only: on an Intel Mac it stops before it downloads anything.", help, StringComparison.Ordinal);
        Assert.DoesNotContain("build for one is published", help, StringComparison.Ordinal);
        Assert.DoesNotContain("Intel Mac is not published", help, StringComparison.Ordinal);
        Assert.Contains("On a Mac this script is the way to install Jason; Homebrew's formula is the command line alone, without Jason Desktop.", help, StringComparison.Ordinal);
    }

    /// <summary>
    /// install.ps1 looks for an open window again right before it runs the setup: nothing of the download or its checks
    /// stands between that look and the setup.
    /// </summary>
    [Fact]
    public void Windows_the_window_is_looked_for_again_right_before_the_setup_runs()
    {
        var code = Code("install.ps1");
        var setup = code.IndexOf("$installer = Start-Process -FilePath $setup", StringComparison.Ordinal);
        var look = code.LastIndexOf("if (Test-DesktopRunning) {", setup, StringComparison.Ordinal);
        Assert.True(setup > 0 && look > 0, "install.ps1 does not look for the window before the setup.");
        var between = code[look..setup];
        foreach (var step in new[] { "Fetch ", "Get-FileHash", "Assert-Signed", "Unblock-File" })
        {
            Assert.DoesNotContain(step, between, StringComparison.Ordinal);
        }

        Assert.Equal(2, Count(code, "if (Test-DesktopRunning) {"));
    }

    /// <summary>
    /// install.ps1 and the installer agree on what a running Jason Desktop is and on what is said of it. The script looks for
    /// the process of the application's own executable by its exact path, as the installer does, never for a window, so a
    /// Desktop left in the tray, which has no window, is found; the installer run silently closes no application and ends
    /// with 3, said only there; and the script says that exit in the same sentence as its own look's.
    /// </summary>
    [Fact]
    public void Windows_the_script_and_the_installer_agree_on_a_running_desktop_and_say_its_exit_3_in_one_sentence()
    {
        var code = Code("install.ps1");
        var look = code[code.IndexOf("function Test-DesktopRunning {", StringComparison.Ordinal)..];
        look = look[..(look.IndexOf("\n    }\n", StringComparison.Ordinal) + 6)];
        Assert.Contains("Get-CimInstance -ClassName Win32_Process | Where-Object { $_.ExecutablePath -and [string]::Equals($_.ExecutablePath, $application, [StringComparison]::OrdinalIgnoreCase) }", look, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex("MainWindow|Get-Process|WindowTitle", RegexOptions.IgnoreCase), look);
        Assert.Contains($"$desktopRunning = '{DesktopRunning}'", code, StringComparison.Ordinal);
        Assert.Equal(2, Count(code, "if (Test-DesktopRunning) {\n                throw $desktopRunning\n            }"));
        Assert.Contains("                3 { throw $desktopRunning }", code, StringComparison.Ordinal);
        Assert.Equal(4, Count(code, "$desktopRunning"));

        var include = File.ReadAllText(Path.Combine(RepositoryRoot(), "desktop", "installer", "uninstall.nsh")).ReplaceLineEndings("\n");
        // The installer's filter for the application is the same exact-path look, by the path the environment hands it.
        Assert.Contains("[string]::Equals($$_.ExecutablePath, $$env:JASON_DESKTOP_APP, [StringComparison]::OrdinalIgnoreCase)", include, StringComparison.Ordinal);
        // Said where the application is found or its look failed, and where no look can be made at all: in a silent run only.
        Assert.Equal(2, new Regex(@"\$\{if\} \$\{Silent\}\s+SetErrorLevel 3\s+Quit\s+\$\{endIf\}").Matches(include).Count);
        Assert.Equal(2, Count(include, "SetErrorLevel 3"));
    }

    /// <summary>
    /// Neither script ever opens the application: an open of an application the system has never seen waits for a person
    /// to answer, and the install by an agent's prompt opens nothing. macOS reads the machine's architecture, not the
    /// shell's, which under Rosetta says x86_64.
    /// </summary>
    [Fact]
    public void Neither_script_opens_the_application_and_macos_reads_the_machine_s_architecture()
    {
        var sh = Code("install.sh");
        // A command named open, at the start of a line or after a separator, whatever it is given; a word in a sentence
        // ("open a new terminal") is not one.
        Assert.DoesNotMatch(new Regex(@"(^\s*|[;&|]\s*|\$\(\s*)open\s+[-""'$]", RegexOptions.Multiline), sh);
        Assert.DoesNotContain("/usr/bin/open", sh, StringComparison.Ordinal);
        Assert.Contains("sysctl -n hw.optional.arm64", sh, StringComparison.Ordinal);
        Assert.Contains("ditto -x -k", sh, StringComparison.Ordinal);
        var ps1 = Code("install.ps1");
        // The application's own executable is named once, to look for its process, and never started.
        Assert.Equal(1, Count(ps1, "Jason.exe"));
        Assert.DoesNotMatch(new Regex(@"(Start-Process|Invoke-Item|&)\s+(-FilePath\s+)?\$application"), ps1);
        Assert.DoesNotContain("--force-run", ps1, StringComparison.Ordinal);
        Assert.Contains("Start-Process -FilePath $setup -ArgumentList \"/S /D=$app\" -Wait -PassThru", ps1, StringComparison.Ordinal);
    }

    // --- Windows: install.ps1 ------------------------------------------------------------------------------------

    [Fact]
    public void Windows_a_fresh_install_runs_the_setup_silently_then_sets_the_machine_up_and_ends_with_the_full_path()
    {
        var windows = Windows.Start(_tree);

        var run = windows.Run(windows.Feed());

        Assert.True(run.Exit == 0, run.Stdout + run.Stderr);
        Assert.Equal(
            [$"setup /S /D={windows.App}", $"install --roles|{windows.Data}"],
            windows.Calls());
        var lines = Lines(run.Stdout);
        Assert.Contains(lines, line => line.StartsWith("{\"version\":", StringComparison.Ordinal));
        Assert.Equal($"Jason {Version} is licensed under the MIT licence: https://github.com/reply-team/jason-ai/blob/v{Version}/LICENSE", lines[^3]);
        Assert.Equal("Next, run `jason status`: it says what this installation still needs, and the command for each.", lines[^2]);
        Assert.Equal($"A terminal opened from now on finds jason by name; this one, and an agent's session already running, call it by its full path: {windows.Carried}", lines[^1]);
        Assert.False(File.Exists(windows.Running), "a runtime was started where none answered before");
    }

    [Fact]
    public void Windows_the_version_already_installed_downloads_nothing_and_sets_the_machine_up_again()
    {
        var windows = Windows.Start(_tree);
        var feed = windows.Feed();
        Assert.True(windows.Run(feed).Exit == 0);
        File.Delete(Path.Combine(feed, Setup));
        File.Delete(windows.Log);

        var run = windows.Run(feed);

        Assert.True(run.Exit == 0, run.Stdout + run.Stderr);
        Assert.Contains($"Jason Desktop {Version} is already installed at {windows.App}; nothing to download.", run.Stdout, StringComparison.Ordinal);
        Assert.Equal([$"runtime status|{windows.Data}", $"install --roles|{windows.Data}"], windows.Calls());
    }

    /// <summary>The ruling on an application already there: a later one is kept as it is and set up, never taken back.</summary>
    [Fact]
    public void Windows_a_later_version_already_installed_is_kept_and_set_up()
    {
        var windows = Windows.Start(_tree);
        Assert.True(windows.Run(windows.Feed("0.49.5")).Exit == 0);
        File.Delete(windows.Log);

        var run = windows.Run(windows.Feed());

        Assert.True(run.Exit == 0, run.Stdout + run.Stderr);
        Assert.Contains($"Jason Desktop 0.49.5 is already installed at {windows.App}, later than {Version}; it is kept as it is", run.Stdout, StringComparison.Ordinal);
        Assert.Equal([$"runtime status|{windows.Data}", $"install --roles|{windows.Data}"], windows.Calls());
        Assert.Contains("Jason 0.49.5 is licensed under the MIT licence", run.Stdout, StringComparison.Ordinal);
    }

    /// <summary>An earlier one is installed over, as its own update does, while its window is closed.</summary>
    [Fact]
    public void Windows_an_earlier_version_is_installed_over_while_its_window_is_closed()
    {
        var windows = Windows.Start(_tree);
        Assert.True(windows.Run(windows.Feed()).Exit == 0);
        File.Delete(windows.Log);

        var run = windows.Run(windows.Feed("0.49.1"));

        Assert.True(run.Exit == 0, run.Stdout + run.Stderr);
        Assert.Equal([$"runtime status|{windows.Data}", $"setup /S /D={windows.App}", $"install --roles|{windows.Data}"], windows.Calls());
    }

    /// <summary>
    /// And never while it is open: a silent installer closes the window without asking, so the script installs nothing and
    /// says to close it and run the line again.
    /// </summary>
    [Fact]
    public void Windows_an_earlier_version_is_never_installed_over_while_its_window_is_open()
    {
        var windows = Windows.Start(_tree);
        Assert.True(windows.Run(windows.Feed()).Exit == 0);
        File.Delete(windows.Log);
        var window = Path.Combine(windows.App, "Jason.exe");
        File.WriteAllBytes(window, WindowedStandIn.Value);
        using var open = System.Diagnostics.Process.Start(new ProcessStartInfo(window) { UseShellExecute = false, CreateNoWindow = true })!;
        try
        {
            var run = windows.Run(windows.Feed("0.49.1"));

            Assert.Equal(1, run.Exit);
            AssertSaid(DesktopRunning, run.Stderr);
            Assert.DoesNotContain(windows.Calls(), call => call.StartsWith("setup", StringComparison.Ordinal));
            Assert.Contains(Version, File.ReadAllText(Path.Combine(windows.App, "resources", "bundle", "version.json")), StringComparison.Ordinal);
        }
        finally
        {
            open.Kill();
            open.WaitForExit();
        }
    }

    /// <summary>
    /// A window opened while the setup is downloaded and checked stops the install as well: the window is looked for again
    /// right before the setup runs. The test build holds the script inside its signature check until the window is open.
    /// </summary>
    [Fact]
    public async Task Windows_a_window_opened_while_the_setup_is_downloaded_and_checked_still_stops_the_install()
    {
        var windows = Windows.Start(_tree);
        Assert.True(windows.Run(windows.Feed()).Exit == 0);
        File.Delete(windows.Log);
        var window = Path.Combine(windows.App, "Jason.exe");
        File.WriteAllBytes(window, WindowedStandIn.Value);
        var asked = Path.Combine(_tree.NewDirectory("asked"), "asked");
        var go = Path.Combine(_tree.NewDirectory("go"), "go");
        windows.Variables["JASON_TEST_ASKED"] = asked;
        windows.Variables["JASON_TEST_GO"] = go;
        var cancellation = TestContext.Current.CancellationToken;
        System.Diagnostics.Process? open = null;
        var opener = Task.Run(async () =>
        {
            var until = DateTime.UtcNow.AddSeconds(90);
            while (!File.Exists(asked) && DateTime.UtcNow < until)
            {
                await Task.Delay(50, cancellation);
            }

            if (File.Exists(asked))
            {
                open = System.Diagnostics.Process.Start(new ProcessStartInfo(window) { UseShellExecute = false, CreateNoWindow = true })!;
                await File.WriteAllTextAsync(go, string.Empty, cancellation);
            }
        }, cancellation);
        try
        {
            var run = windows.Run(windows.Feed("0.49.1"), signatureCheck: HeldUntilGo);

            Assert.True(open is not null, "the script never reached its signature check: " + run.Stdout + run.Stderr);
            Assert.Equal(1, run.Exit);
            AssertSaid(DesktopRunning, run.Stderr);
            Assert.DoesNotContain(windows.Calls(), call => call.StartsWith("setup", StringComparison.Ordinal));
            Assert.Contains(Version, File.ReadAllText(Path.Combine(windows.App, "resources", "bundle", "version.json")), StringComparison.Ordinal);
        }
        finally
        {
            await opener.WaitAsync(TimeSpan.FromSeconds(100), cancellation);
            if (open is not null)
            {
                open.Kill();
                open.WaitForExit();
                open.Dispose();
            }
        }
    }

    [Fact]
    public void Windows_a_setup_without_the_release_s_signature_is_never_run()
    {
        var windows = Windows.Start(_tree);

        var run = windows.Run(windows.Feed(), published: true);

        Assert.Equal(1, run.Exit);
        AssertSaid("does not carry a valid signature", run.Stderr);
        Assert.Empty(windows.Calls());
        Assert.False(Directory.Exists(windows.App), "something was installed from a setup that is not signed");
    }

    [Theory]
    [InlineData("-InstallDir", "-InstallDir is not taken")]
    [InlineData("-NoModifyPath", "-NoModifyPath is not taken")]
    public void Windows_the_options_of_the_command_line_install_are_refused_before_anything_is_read(string option, string said)
    {
        var windows = Windows.Start(_tree);
        string[] arguments = option == "-InstallDir" ? [option, _tree.NewDirectory("elsewhere")] : [option];

        var run = windows.Run(_tree.NewDirectory("empty-feed"), arguments: arguments);

        Assert.Equal(1, run.Exit);
        AssertSaid(said, run.Stderr);
        Assert.DoesNotContain("manifest.json", run.Stderr, StringComparison.Ordinal);
        Assert.Empty(windows.Calls());
    }

    /// <summary>
    /// An elevated PowerShell - an administrator's, with User Account Control on, or another account's than the session's -
    /// would run the setup, `jason install` and the runtime the same way, or into another account's profile: the script
    /// stops before it reads the feed, under both shells. The published script reads the token's kind and the session's
    /// account, as <c>jason install</c> and an update do, and refuses before the feed.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Windows_an_elevated_powershell_is_refused_before_anything_is_downloaded(bool windowsPowerShell)
    {
        var windows = Windows.Start(_tree);

        var run = windows.Run(_tree.NewDirectory("empty-feed"), windowsPowerShell: windowsPowerShell, elevated: true);

        Assert.Equal(1, run.Exit);
        AssertSaid(ElevatedRefusal, run.Stderr);
        Assert.DoesNotContain("manifest.json", run.Stderr, StringComparison.Ordinal);
        Assert.Empty(windows.Calls());
        Assert.False(Directory.Exists(windows.App));

        var code = Script("install.ps1");
        Assert.DoesNotContain(ElevationCheck + " return", code, StringComparison.Ordinal);
        Assert.Contains("GetTokenInformation", code, StringComparison.Ordinal);
        Assert.Contains("WTSQuerySessionInformation", code, StringComparison.Ordinal);
        var refused = code.IndexOf("    if (Test-Elevated) {", StringComparison.Ordinal);
        Assert.True(refused > 0 && refused < code.IndexOf("# --- the feed ---", StringComparison.Ordinal), "install.ps1 does not refuse an elevated PowerShell before the feed.");
    }

    [Fact]
    public void Windows_a_release_before_0_49_0_is_left_to_its_own_script()
    {
        var windows = Windows.Start(_tree);

        var run = windows.Run(windows.Feed("0.48.2"));

        Assert.Equal(1, run.Exit);
        AssertSaid("installed by its own release's script", run.Stderr);
        Assert.Empty(windows.Calls());
    }

    /// <summary>The ruling: the script ends non-zero, saying which act failed, when the terminal command was not placed.</summary>
    [Theory]
    [InlineData("not placed", "did not put jason in the terminal")]
    [InlineData("refused", "refused (install_not_carried)")]
    [InlineData("no report", "printed no report that could be read")]
    public void Windows_an_install_that_did_not_put_jason_in_the_terminal_ends_non_zero_and_says_why(string kind, string said)
    {
        var windows = Windows.Start(_tree);
        windows.Variables["JASON_STANDIN_REPORT"] = kind switch
        {
            "not placed" => Report(placed: false, runtime: "not_running", completed: false),
            "refused" => "{\"error\":{\"code\":\"install_not_carried\",\"message\":\"Not carried.\",\"retryable\":false}}",
            _ => "not a report",
        };
        windows.Variables["JASON_STANDIN_INSTALL_EXIT"] = "1";

        var run = windows.Run(windows.Feed());

        Assert.Equal(1, run.Exit);
        AssertSaid(said, run.Stderr);
        Assert.DoesNotContain("call it by its full path", run.Stdout, StringComparison.Ordinal);
    }

    /// <summary>
    /// "A terminal opened from now on finds jason by name" is said only where the report says that terminal finds this
    /// jason: where it finds another file first, the script ends non-zero naming it, and the report above says what removes
    /// it.
    /// </summary>
    [Fact]
    public void Windows_a_report_naming_another_jason_first_ends_non_zero_and_says_so()
    {
        var windows = Windows.Start(_tree);
        windows.Variables["JASON_STANDIN_REPORT"] = Report(placed: true, runtime: "not_running", completed: false, finds: @"C:\\Tools\\jason.exe", findsThis: false);
        windows.Variables["JASON_STANDIN_INSTALL_EXIT"] = "1";

        var run = windows.Run(windows.Feed());

        Assert.Equal(1, run.Exit);
        AssertSaid(@"a terminal opened from now on finds C:\Tools\jason.exe first by that name; its problems above say what to do.", run.Stderr);
        Assert.DoesNotContain("finds jason by name", run.Stdout, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether a new terminal finds this jason is decided once, by <c>jason install</c>, and the script reads that member
    /// and compares no path: a file it names that is neither path the script knows still ends saying jason is found where
    /// <c>finds_this</c> is true, and a report without the member, from an older kept application, reads as before it.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public void Windows_the_script_reads_finds_this_and_compares_no_path(bool? findsThis)
    {
        var windows = Windows.Start(_tree);
        windows.Variables["JASON_STANDIN_REPORT"] = Report(placed: true, runtime: "not_running", completed: true, finds: @"C:\\Users\\somebody\\jason.EXE", findsThis: findsThis);

        var run = windows.Run(windows.Feed());

        Assert.True(run.Exit == 0, run.Stdout + run.Stderr);
        Assert.Equal($"A terminal opened from now on finds jason by name; this one, and an agent's session already running, call it by its full path: {windows.Carried}", Lines(run.Stdout)[^1]);
    }

    [Fact]
    public void Windows_a_report_that_left_other_things_undone_still_ends_with_jason_in_the_terminal()
    {
        var windows = Windows.Start(_tree);
        windows.Variables["JASON_STANDIN_REPORT"] = Report(placed: true, runtime: "not_running", completed: false);
        windows.Variables["JASON_STANDIN_INSTALL_EXIT"] = "1";

        var run = windows.Run(windows.Feed());

        Assert.True(run.Exit == 0, run.Stdout + run.Stderr);
        Assert.Contains("named above what else it did not get done", run.Stdout, StringComparison.Ordinal);
        Assert.EndsWith(windows.Carried, Lines(run.Stdout)[^1], StringComparison.Ordinal);
    }

    /// <summary>
    /// The runtime rule: jason install starts no runtime, and the caller that asked for the install starts one again from
    /// the jason that is whole, on success and on failure alike: where the report says it stopped one, and where one
    /// answered before the install and none answers after it, which is how an update's installer, which stops the
    /// application's runtime, and its own setup, which retires a command-line install's, leave it.
    /// </summary>
    [Theory]
    [InlineData("the report says stopped", true)]
    [InlineData("the report says stopped", false)]
    [InlineData("the setup stopped it", true)]
    public void Windows_a_runtime_stopped_by_the_install_is_started_again_from_the_carried_jason(string how, bool placed)
    {
        var windows = Windows.Start(_tree);
        if (how == "the setup stopped it")
        {
            windows.PlantCommandLineInstall();
            File.WriteAllText(windows.Running, string.Empty);
            windows.Variables["JASON_STANDIN_SETUP_STOPS"] = "1";
        }
        else
        {
            windows.Variables["JASON_STANDIN_REPORT"] = Report(placed, runtime: "stopped", completed: placed);
        }

        var run = windows.Run(windows.Feed());

        Assert.Equal(placed ? 0 : 1, run.Exit);
        Assert.Contains($"runtime start|{windows.Data}", windows.Calls());
        Assert.True(File.Exists(windows.Running), "no runtime was started again");
        Assert.Contains("so one is started again from " + windows.Carried, run.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void Windows_a_runtime_that_still_answers_is_left_alone()
    {
        var windows = Windows.Start(_tree);
        windows.PlantCommandLineInstall();
        File.WriteAllText(windows.Running, string.Empty);

        var run = windows.Run(windows.Feed());

        Assert.True(run.Exit == 0, run.Stdout + run.Stderr);
        Assert.DoesNotContain(windows.Calls(), call => call.StartsWith("runtime start", StringComparison.Ordinal));
    }

    /// <summary>
    /// On failure too: a setup that stopped a runtime and then ended without installing leaves one started again, from the
    /// jason that is whole at that step, here the command-line install's, since the application never arrived.
    /// </summary>
    [Fact]
    public void Windows_a_setup_that_stopped_a_runtime_and_failed_leaves_one_started_from_the_jason_that_is_whole()
    {
        var windows = Windows.Start(_tree);
        windows.PlantCommandLineInstall();
        File.WriteAllText(windows.Running, string.Empty);
        windows.Variables["JASON_STANDIN_SETUP_STOPS"] = "1";
        windows.Variables["JASON_STANDIN_SETUP_EXIT"] = "2";

        var run = windows.Run(windows.Feed());

        Assert.Equal(1, run.Exit);
        AssertSaid("another install of Jason ran at the same time", run.Stderr);
        Assert.False(File.Exists(windows.Carried), "the stand-in setup installed although it failed");
        Assert.Equal($"runtime start|{windows.Data}", windows.Calls()[^1]);
        Assert.True(File.Exists(windows.Running), "no runtime was started again after the setup failed");
        Assert.Contains("so one is started again from " + Path.Combine(windows.Root, "Programs", "jason", "jason.exe"), run.Stdout, StringComparison.Ordinal);
    }

    /// <summary>
    /// A line jason install writes on stderr goes through to stderr and never fails the install: Windows PowerShell 5.1
    /// turns such a line into an error record once the script's streams are redirected, as an agent's are.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Windows_a_line_on_stderr_from_jason_install_goes_through_and_the_install_succeeds(bool windowsPowerShell)
    {
        var windows = Windows.Start(_tree);
        windows.Variables["JASON_STANDIN_INSTALL_STDERR"] = "A diagnostic line from jason install.";

        var run = windows.Run(windows.Feed(), windowsPowerShell: windowsPowerShell);

        Assert.True(run.Exit == 0, run.Stdout + run.Stderr);
        Assert.Contains("A diagnostic line from jason install.", run.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("A diagnostic line from jason install.", run.Stdout, StringComparison.Ordinal);
        Assert.Contains(Lines(run.Stdout), line => line.StartsWith("{\"version\":", StringComparison.Ordinal));
        Assert.EndsWith(windows.Carried, Lines(run.Stdout)[^1], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, "a Jason process runs from")]
    [InlineData(2, "another install of Jason ran at the same time")]
    [InlineData(3, DesktopRunning)]
    [InlineData(5, "The installer exited with 5")]
    public void Windows_an_installer_that_stopped_is_said_in_words_and_nothing_is_set_up(int code, string said)
    {
        var windows = Windows.Start(_tree);
        windows.Variables["JASON_STANDIN_SETUP_EXIT"] = code.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var run = windows.Run(windows.Feed());

        Assert.Equal(1, run.Exit);
        AssertSaid(said, run.Stderr);
        Assert.DoesNotContain(windows.Calls(), call => call.StartsWith("install", StringComparison.Ordinal));
    }

    [Fact]
    public void Windows_powershell_5_1_installs_the_same_way()
    {
        var windows = Windows.Start(_tree);

        var run = windows.Run(windows.Feed(), windowsPowerShell: true);

        Assert.True(run.Exit == 0, run.Stdout + run.Stderr);
        Assert.Equal([$"setup /S /D={windows.App}", $"install --roles|{windows.Data}"], windows.Calls());
        Assert.EndsWith(windows.Carried, Lines(run.Stdout)[^1], StringComparison.Ordinal);
    }

    // --- macOS: install.sh ---------------------------------------------------------------------------------------

    [Fact]
    public void Macos_a_fresh_install_places_the_application_by_one_rename_and_sets_the_machine_up()
    {
        var mac = Mac.Start(_tree);

        var run = mac.Run(mac.Feed());

        Assert.True(run.Exit == 0, run.Stdout + run.Stderr);
        var app = Path.Combine(mac.Shared, "Jason.app");
        Assert.True(File.Exists(Path.Combine(app, "Contents", "Resources", "bundle", "jason")), run.Stdout + run.Stderr);
        Assert.Equal(["Jason.app"], Directory.EnumerateFileSystemEntries(mac.Shared).Select(Path.GetFileName));
        Assert.Equal(["ditto -x -k", VerifyCall, $"install --roles|{mac.Data}"], mac.PathlessCalls());
        var lines = Lines(run.Stdout);
        Assert.Equal($"Jason {Version} is licensed under the MIT licence: https://github.com/reply-team/jason-ai/blob/v{Version}/LICENSE", lines[^3]);
        Assert.Equal($"A terminal opened from now on finds jason by name; this one, and an agent's session already running, call it by its full path: {Path.Combine(mac.Home, ".local", "bin", "jason")}", lines[^1]);
    }

    /// <summary>
    /// Run as root - <c>curl … | sudo sh</c> - the script would place Jason.app owned by root, which the application's own
    /// updater then cannot replace without a password, and through a home sudo keeps, a data directory, a wrapper and skills
    /// the person's own runtime cannot write. So it stops before it downloads anything, and says to run it as yourself.
    /// </summary>
    [Fact]
    public void Macos_root_is_refused_before_anything_is_downloaded()
    {
        var mac = Mac.Start(_tree);
        mac.Variables["STUB_UID"] = "0";

        var run = mac.Run(mac.Feed());

        Assert.NotEqual(0, run.Exit);
        Assert.Contains("this script runs as root, and Jason Desktop and everything it sets up are your account's own, so nothing was downloaded or installed: run the install line again as yourself, without sudo.", run.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("Downloading", run.Stdout, StringComparison.Ordinal);
        Assert.Empty(mac.Calls());
        Assert.Empty(Directory.EnumerateFileSystemEntries(mac.Shared));
        Assert.Empty(Directory.EnumerateFileSystemEntries(mac.Home));
    }

    /// <summary>
    /// The script installs Jason Desktop on Apple silicon only, as the install page says, so it refuses an Intel Mac before
    /// it downloads anything. It gives that reason and no other: every release publishes the Intel image, so it says nothing
    /// of what is published, and it points nowhere else.
    /// </summary>
    [Fact]
    public void Macos_an_intel_mac_is_refused_before_anything_is_downloaded()
    {
        var mac = Mac.Start(_tree);
        mac.Variables["STUB_ARM64"] = "absent";

        var run = mac.Run(mac.Feed(arch: "x64"));

        Assert.NotEqual(0, run.Exit);
        Assert.Contains("This script installs Jason Desktop on Apple silicon Macs only, and this is an Intel Mac; nothing was installed.", run.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("published", run.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("The releases are built for", run.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("Downloading", run.Stdout, StringComparison.Ordinal);
        Assert.Empty(mac.Calls());
        Assert.Empty(Directory.EnumerateFileSystemEntries(mac.Shared));
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void Macos_a_shared_folder_this_account_may_not_write_to_sends_the_application_to_its_own()
    {
        var mac = Mac.Start(_tree);
        File.SetUnixFileMode(mac.Shared, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            Assert.SkipWhen(Writable(mac.Shared), "This account writes into a folder whatever its mode says, as root does.");

            var run = mac.Run(mac.Feed());

            Assert.True(run.Exit == 0, run.Stdout + run.Stderr);
            Assert.True(Directory.Exists(Path.Combine(mac.Home, "Applications", "Jason.app")), run.Stdout);
        }
        finally
        {
            File.SetUnixFileMode(mac.Shared, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Theory]
    [InlineData("team", "signed by the team 'SOMEONEELSE'")]
    [InlineData("verify", "does not verify")]
    public void Macos_an_application_not_signed_as_the_release_s_is_never_placed(string kind, string said)
    {
        var mac = Mac.Start(_tree);
        mac.Variables[kind == "team" ? "STUB_TEAM" : "STUB_VERIFY"] = kind == "team" ? "SOMEONEELSE" : "1";

        var run = mac.Run(mac.Feed());

        Assert.NotEqual(0, run.Exit);
        Assert.Contains(said, run.Stderr, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(mac.Shared));
        Assert.DoesNotContain(mac.Calls(), call => call.StartsWith("install", StringComparison.Ordinal));
    }

    /// <summary>
    /// The ruling on an application already there: never renamed, replaced or deleted from a terminal. One before 0.49.0
    /// cannot be driven from one: the script touches nothing, says to update it from its own menu, and ends non-zero.
    /// </summary>
    [Fact]
    public void Macos_an_application_before_0_49_0_already_there_is_left_as_it_is_and_the_script_ends_non_zero()
    {
        var mac = Mac.Start(_tree);
        var bundle = Path.Combine(mac.Shared, "Jason.app", "Contents", "Resources", "bundle");
        Directory.CreateDirectory(bundle);
        File.WriteAllText(Path.Combine(bundle, "version.json"), "{\"version\": \"0.48.2\"}\n");

        var run = mac.Run(mac.Feed());

        Assert.NotEqual(0, run.Exit);
        Assert.Contains("Jason Desktop 0.48.2 at", run.Stderr, StringComparison.Ordinal);
        Assert.Contains("Update Jason Desktop from its own menu (Check for Updates), then run the install line again.", run.Stderr, StringComparison.Ordinal);
        Assert.Equal(["version.json"], Directory.EnumerateFileSystemEntries(bundle).Select(Path.GetFileName));
        Assert.Empty(mac.Calls());
    }

    /// <summary>One of 0.49.0 or later carries the routine and the update: it is kept, never replaced, and its routine run.</summary>
    [Fact]
    public void Macos_a_later_application_already_there_is_kept_and_set_up()
    {
        var mac = Mac.Start(_tree);
        Assert.True(mac.Run(mac.Feed("0.49.5")).Exit == 0);
        File.Delete(mac.Log);

        var run = mac.Run(mac.Feed());

        Assert.True(run.Exit == 0, run.Stdout + run.Stderr);
        Assert.Contains($"Jason Desktop 0.49.5 is already installed at {Path.Combine(mac.Shared, "Jason.app")}; it is kept as it is", run.Stdout, StringComparison.Ordinal);
        Assert.Equal([VerifyCall, $"runtime status|{mac.Data}", $"install --roles|{mac.Data}"], mac.PathlessCalls());
        Assert.DoesNotContain("update apply", run.Stdout, StringComparison.Ordinal);
        Assert.StartsWith("A terminal opened from now on", Lines(run.Stdout)[^1], StringComparison.Ordinal);
    }

    /// <summary>
    /// One older than the release asked for is kept as well, and the script's last line names the update, still ending with
    /// the full path of jason.
    /// </summary>
    [Fact]
    public void Macos_an_earlier_application_already_there_is_kept_and_the_last_line_names_the_update()
    {
        var mac = Mac.Start(_tree);
        Assert.True(mac.Run(mac.Feed()).Exit == 0);
        File.Delete(mac.Log);

        var run = mac.Run(mac.Feed("0.49.1"));

        Assert.True(run.Exit == 0, run.Stdout + run.Stderr);
        Assert.DoesNotContain(mac.Calls(), call => call.StartsWith("ditto", StringComparison.Ordinal));
        var jason = Path.Combine(mac.Home, ".local", "bin", "jason");
        var last = Lines(run.Stdout)[^1];
        Assert.StartsWith($"Jason Desktop {Version} is kept, older than 0.49.1: `{jason} update apply` updates it.", last, StringComparison.Ordinal);
        Assert.EndsWith("call it by its full path: " + jason, last, StringComparison.Ordinal);
    }

    /// <summary>The script looks in both folders before it chooses: an application in the account's own is the place.</summary>
    [Fact]
    public void Macos_an_application_in_the_account_s_own_folder_is_the_place()
    {
        var mac = Mac.Start(_tree);
        var feed = mac.Feed();
        Assert.True(mac.Run(feed).Exit == 0);
        var own = Path.Combine(mac.Home, "Applications");
        Directory.CreateDirectory(own);
        Directory.Move(Path.Combine(mac.Shared, "Jason.app"), Path.Combine(own, "Jason.app"));
        File.Delete(mac.Log);

        var run = mac.Run(feed);

        Assert.True(run.Exit == 0, run.Stdout + run.Stderr);
        Assert.Contains($"Jason Desktop {Version} is already installed at {Path.Combine(own, "Jason.app")}; nothing to download.", run.Stdout, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(mac.Shared));
    }

    [Fact]
    public void Macos_the_version_already_there_downloads_nothing_and_sets_the_machine_up_again()
    {
        var mac = Mac.Start(_tree);
        var feed = mac.Feed();
        Assert.True(mac.Run(feed).Exit == 0);
        File.Delete(Path.Combine(feed, "jason-desktop-macos-arm64.zip"));
        File.Delete(mac.Log);

        var run = mac.Run(feed);

        Assert.True(run.Exit == 0, run.Stdout + run.Stderr);
        Assert.Contains("nothing to download", run.Stdout, StringComparison.Ordinal);
        Assert.Equal([VerifyCall, $"runtime status|{mac.Data}", $"install --roles|{mac.Data}"], mac.PathlessCalls());
    }

    /// <summary>
    /// A kept application's jason is run only once the application verifies as a new one must: an application at the
    /// place that does not is left as it is, and its jason is never asked anything.
    /// </summary>
    [Fact]
    public void Macos_a_kept_application_that_does_not_verify_is_left_as_it_is_and_its_jason_is_never_run()
    {
        var mac = Mac.Start(_tree);
        var feed = mac.Feed();
        Assert.True(mac.Run(feed).Exit == 0);
        File.Delete(mac.Log);
        mac.Variables["STUB_VERIFY"] = "1";

        var run = mac.Run(feed);

        Assert.NotEqual(0, run.Exit);
        Assert.Contains("does not verify as signed by BD8M7U5YBV, the team that signs Jason (said above), so its jason was not run; nothing was changed", run.Stderr, StringComparison.Ordinal);

        // And the way on, which every run again would otherwise meet the same refusal without: that application out of the
        // way, the next run downloads one and puts it in its place.
        var app = Path.Combine(mac.Shared, "Jason.app");
        Assert.Contains($"Move {app} to the Trash, then run the install line again: it downloads Jason Desktop and puts it there.", run.Stderr, StringComparison.Ordinal);
        Assert.Equal([VerifyCall], mac.PathlessCalls());
        Assert.True(File.Exists(mac.Carried));
    }

    /// <summary>The application in the zip has to be the release's version, or it is never placed.</summary>
    [Fact]
    public void Macos_an_application_of_another_version_than_the_release_is_never_placed()
    {
        var mac = Mac.Start(_tree);

        var run = mac.Run(mac.Feed(carries: "0.49.3"));

        Assert.NotEqual(0, run.Exit);
        Assert.Contains($"is not Jason {Version}; nothing was installed", run.Stderr, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(mac.Shared));
        Assert.DoesNotContain(mac.Calls(), call => call.StartsWith("install", StringComparison.Ordinal));
    }

    /// <summary>
    /// A Jason.app that appears between the check and the rename would take the new one inside it: the new one is taken
    /// back out, the one that appeared is left as it was, and nothing is set up.
    /// </summary>
    [Fact]
    public void Macos_an_application_that_appears_just_before_the_rename_is_left_as_it_was()
    {
        var mac = Mac.Start(_tree);
        var dest = Path.Combine(mac.Shared, "Jason.app");
        mac.Variables["STUB_RACE"] = dest;

        var run = mac.Run(mac.Feed());

        Assert.NotEqual(0, run.Exit);
        Assert.Contains("appeared while this ran and is left as it was; nothing was installed", run.Stderr, StringComparison.Ordinal);
        Assert.Equal(["Jason.app"], Directory.EnumerateFileSystemEntries(mac.Shared).Select(Path.GetFileName));
        Assert.Equal(["Contents"], Directory.EnumerateFileSystemEntries(dest).Select(Path.GetFileName));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(dest, "Contents")));
        Assert.DoesNotContain(mac.Calls(), call => call.StartsWith("install", StringComparison.Ordinal));
    }

    /// <summary>Stopped by a signal while it unpacks, the script still removes the staging folder beside the place.</summary>
    [Theory]
    [InlineData("TERM", 143)]
    [InlineData("INT", 130)]
    [InlineData("HUP", 129)]
    public void Macos_a_script_stopped_by_a_signal_leaves_no_staging_folder(string signal, int exit)
    {
        var mac = Mac.Start(_tree);
        mac.Variables["STUB_SIGNAL"] = signal;

        var run = mac.Run(mac.Feed());

        Assert.Equal(exit, run.Exit);
        Assert.Empty(Directory.EnumerateFileSystemEntries(mac.Shared));
        Assert.DoesNotContain(mac.Calls(), call => call.StartsWith("install", StringComparison.Ordinal));
    }

    /// <summary>
    /// The runtime rule on a Mac: where a runtime answered before the install and none answers after it, though the report
    /// does not say it stopped one, a runtime is started again from the application's jason.
    /// </summary>
    [Fact]
    public void Macos_a_runtime_that_answered_before_and_not_after_is_started_again_from_the_carried_jason()
    {
        var mac = Mac.Start(_tree);
        mac.PlantCommandLineInstall();
        File.WriteAllText(mac.Running, string.Empty);
        mac.Variables["STANDIN_INSTALL_STOPS"] = "1";

        var run = mac.Run(mac.Feed());

        Assert.True(run.Exit == 0, run.Stdout + run.Stderr);
        Assert.Equal($"runtime start|{mac.Data}", mac.Calls()[^1]);
        Assert.True(File.Exists(mac.Running), "no runtime was started again");
        Assert.Contains("so one is started again from " + mac.Carried, run.Stdout, StringComparison.Ordinal);
    }

    /// <summary>A refusal and a run that printed no report end non-zero as well, each saying which.</summary>
    [Theory]
    [InlineData("refused", "jason install refused, as its report above says, so jason was not put in the terminal")]
    [InlineData("no report", "jason install printed no report, so whether jason is in the terminal is not known")]
    public void Macos_an_install_that_refused_or_printed_no_report_ends_non_zero_and_says_why(string kind, string said)
    {
        var mac = Mac.Start(_tree);
        mac.Variables["STANDIN_REPORT"] = kind == "refused"
            ? "{\"error\":{\"code\":\"install_not_carried\",\"message\":\"Not carried.\",\"retryable\":false}}"
            : "not a report";

        var run = mac.Run(mac.Feed());

        Assert.NotEqual(0, run.Exit);
        Assert.Contains(said, run.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("call it by its full path", run.Stdout, StringComparison.Ordinal);
    }

    /// <summary>
    /// "A terminal opened from now on finds jason by name" is said only where the report says that terminal finds the
    /// wrapper or this jason: where it finds another file first, the script ends non-zero naming it.
    /// </summary>
    [Fact]
    public void Macos_a_report_naming_another_jason_first_ends_non_zero_and_says_so()
    {
        var mac = Mac.Start(_tree);
        mac.Variables["STANDIN_REPORT"] = Report(placed: true, runtime: "not_running", completed: false, finds: "/usr/local/bin/jason", findsThis: false);

        var run = mac.Run(mac.Feed());

        Assert.NotEqual(0, run.Exit);
        Assert.Contains("a terminal opened from now on finds /usr/local/bin/jason first by that name; its problems above say what to do.", run.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("finds jason by name", run.Stdout, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether a new terminal finds this jason is decided once, by <c>jason install</c>, and the script reads that member
    /// and compares no path: a file it names that is not the path the script knows still ends saying jason is found where
    /// <c>finds_this</c> is true, and a report without the member, from an older kept application, reads as before it.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public void Macos_the_script_reads_finds_this_and_compares_no_path(bool? findsThis)
    {
        var mac = Mac.Start(_tree);
        mac.Variables["STANDIN_REPORT"] = Report(placed: true, runtime: "not_running", completed: true, finds: "/Users/somebody/.local/bin/jason", findsThis: findsThis);

        var run = mac.Run(mac.Feed());

        Assert.True(run.Exit == 0, run.Stdout + run.Stderr);
        Assert.Contains("A terminal opened from now on finds jason by name", run.Stdout, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Macos_a_runtime_the_report_says_was_stopped_is_started_again_and_an_unplaced_command_ends_non_zero(bool placed)
    {
        var mac = Mac.Start(_tree);
        mac.Variables["STANDIN_REPORT"] = Report(placed, runtime: "stopped", completed: placed);

        var run = mac.Run(mac.Feed());

        Assert.Equal(placed, run.Exit == 0);
        Assert.Contains($"runtime start|{mac.Data}", mac.Calls());
        if (!placed)
        {
            Assert.Contains("did not put jason in the terminal", run.Stderr, StringComparison.Ordinal);
            Assert.DoesNotContain("call it by its full path", run.Stdout, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("--install-dir", "--install-dir is not taken on macOS")]
    [InlineData("--no-modify-path", "--no-modify-path is not taken on macOS")]
    [InlineData("--version=0.48.2", "installed by its own release's script")]
    public void Macos_what_the_application_route_cannot_do_is_refused_before_anything_is_placed(string option, string said)
    {
        var mac = Mac.Start(_tree);
        string[] arguments = option == "--install-dir" ? [option, _tree.NewDirectory("elsewhere")] : [option];

        var run = mac.Run(mac.Feed(version: option == "--version=0.48.2" ? "0.48.2" : Version), arguments);

        Assert.NotEqual(0, run.Exit);
        Assert.Contains(said, run.Stderr, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(mac.Shared));
    }

    // --- the two machines --------------------------------------------------------------------------------------

    /// <summary>A report as <c>jason install</c> prints it, with what a row needs to vary.</summary>
    private static string Report(bool placed, string runtime, bool completed, string finds = "{self}", bool? findsThis = true) =>
        "{\"version\":\"" + Version + "\",\"executable\":\"{self}\",\"command\":{\"runs\":\"{dir}\",\"placed\":" + (placed ? "true" : "false")
        + ",\"open_shell\":\"{self}\",\"changed\":true,\"profiles\":[],\"taken_back\":[],\"finds\":\"" + finds + "\""
        + (findsThis is { } yes ? ",\"finds_this\":" + (yes ? "true" : "false") : string.Empty)
        + "},\"skills\":[],\"roles\":null,\"retired\":null,\"runtime\":\""
        + runtime + "\",\"done\":[],\"left\":[],\"problems\":[" + (completed ? string.Empty : "\"Something was not done.\"") + "],\"completed\":"
        + (completed ? "true" : "false") + "}";

    /// <summary>install.ps1, run by pwsh or Windows PowerShell under a <c>LOCALAPPDATA</c> and a data directory of the test's own.</summary>
    private sealed class Windows
    {
        private readonly TempTree _tree;

        private Windows(TempTree tree)
        {
            _tree = tree;
            Root = tree.NewDirectory("local");
            Data = tree.NewDirectory("data");
            Log = Path.Combine(tree.NewDirectory("log"), "calls.log");
            Running = Path.Combine(tree.NewDirectory("flag"), "running");
            App = Path.Combine(Root, "Programs", "jason-desktop");
            Carried = Path.Combine(App, "resources", "bundle", "jason.exe");
            var standIn = Path.Combine(tree.NewDirectory("standin"), "jason.exe");
            File.WriteAllBytes(standIn, StandIn.Value);
            Variables["JASON_STANDIN_CARRIED"] = standIn;
        }

        public string Root { get; }

        public string Data { get; }

        public string Log { get; }

        public string Running { get; }

        public string App { get; }

        public string Carried { get; }

        public Dictionary<string, string> Variables { get; } = new(StringComparer.Ordinal);

        public static Windows Start(TempTree tree)
        {
            Assert.SkipUnless(OperatingSystem.IsWindows(), "install.ps1 is the Windows installer; CI's Windows leg runs this row.");
            return new Windows(tree);
        }

        /// <summary>A command-line install of an earlier release in its installer's place, which a runtime may run from.</summary>
        public void PlantCommandLineInstall()
        {
            var folder = Path.Combine(Root, "Programs", "jason");
            Directory.CreateDirectory(folder);
            File.Copy(Variables["JASON_STANDIN_CARRIED"], Path.Combine(folder, "jason.exe"));
        }

        /// <summary>A feed holding the stand-in setup of <paramref name="version"/>, its manifest and its checksums.</summary>
        public string Feed(string version = Version)
        {
            var feed = _tree.NewDirectory("feed");
            var setup = Path.Combine(feed, Setup);
            File.WriteAllBytes(setup, WindowedStandIn.Value);
            File.WriteAllText(Path.Combine(feed, ReleaseAssets.Manifest), "{\n  \"schema\": 1,\n  \"version\": \"" + version + "\"\n}\n");
            File.WriteAllText(Path.Combine(feed, ReleaseAssets.Checksums), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(setup))) + "  " + Setup + "\n");
            Variables["JASON_STANDIN_VERSION"] = version;
            return feed;
        }

        /// <summary>The calls the stand-ins were given, in order: <c>setup &lt;arguments&gt;</c>, else <c>&lt;arguments&gt;|&lt;data directory&gt;</c>.</summary>
        public List<string> Calls() => File.Exists(Log) ? [.. File.ReadAllLines(Log)] : [];

        public (int Exit, string Stdout, string Stderr) Run(string feed, bool published = false, bool windowsPowerShell = false, string signatureCheck = ShortCircuited, bool elevated = false, params string[] arguments)
        {
            var script = Path.Combine(RepositoryRoot(), "install", "install.ps1");
            if (!published)
            {
                var text = File.ReadAllText(script);
                Assert.Equal(1, Count(text.ReplaceLineEndings("\n"), SignatureCheck + "\n"));
                Assert.Equal(1, Count(text.ReplaceLineEndings("\n"), ElevationCheck + "\n"));
                script = Path.Combine(_tree.NewDirectory("test-build"), "install.ps1");
                File.WriteAllText(
                    script,
                    text.Replace(SignatureCheck, signatureCheck, StringComparison.Ordinal)
                        .Replace(ElevationCheck, ElevationCheck + (elevated ? " return $true" : " return $false"), StringComparison.Ordinal));
            }

            var environment = new Dictionary<string, string>(Variables)
            {
                ["LOCALAPPDATA"] = Root,
                ["JASON_DATA_DIR"] = Data,
                ["JASON_STANDIN_LOG"] = Log,
                ["JASON_STANDIN_RUNNING"] = Running,
                ["POWERSHELL_TELEMETRY_OPTOUT"] = "1",
            };
            string[] call = ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-Feed", feed, .. arguments];
            if (windowsPowerShell)
            {
                environment["PSModulePath"] = Environment.GetEnvironmentVariable("PSModulePath", EnvironmentVariableTarget.Machine) ?? string.Empty;
                return RunToEnd(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"), environment, call);
            }

            return RunToEnd(Jason.Runtime.Tests.ChildPwsh.StartInfo(Root), environment, call);
        }
    }

    /// <summary>install.sh's macOS route, run by sh with a Mac's commands stood in for and a home of the test's own.</summary>
    private sealed class Mac
    {
        private readonly TempTree _tree;

        private Mac(TempTree tree)
        {
            _tree = tree;
            Home = tree.NewDirectory("home");
            Shared = tree.NewDirectory("Applications");
            Data = tree.NewDirectory("data");
            Log = Path.Combine(tree.NewDirectory("log"), "calls.log");
            Running = Path.Combine(tree.NewDirectory("flag"), "running");
            Stubs = tree.NewDirectory("stubs");
            Stub("uname", "case \"${1:-}\" in -m) echo arm64 ;; *) echo Darwin ;; esac\n");
            // The user id a Mac's first account has, or root where a row says so, whoever runs the row.
            Stub("id", "[ \"${1:-}\" = -u ] && { echo \"${STUB_UID:-501}\"; exit 0; }\nexec /usr/bin/id \"$@\"\n");
            Stub("sysctl", "[ \"${STUB_ARM64:-1}\" = absent ] && { echo \"sysctl: unknown oid 'hw.optional.arm64'\" >&2; exit 1; }\necho \"${STUB_ARM64:-1}\"\n");
            // ditto unpacks, and where a row says so the script is then sent a signal, as an agent's timeout sends one.
            Stub("ditto", $"echo \"ditto $*\" >> '{Log}'\ntar -xzf \"$3\" -C \"$4\"\n[ -z \"${{STUB_SIGNAL:-}}\" ] || kill -\"$STUB_SIGNAL\" \"$PPID\"\n");
            // codesign verifies only against the release's requirement, given as -R=, and only an application of that team.
            Stub(
                "codesign",
                $"echo \"codesign $*\" >> '{Log}'\n"
                + "case \"$1\" in\n"
                + "  --verify)\n"
                + "    GIVEN=''\n"
                + "    for A in \"$@\"; do [ \"$A\" = \"-R=$STUB_REQUIREMENT\" ] && GIVEN=1; done\n"
                + "    [ -n \"$GIVEN\" ] || { echo 'test-requirement not given' >&2; exit 3; }\n"
                + "    [ \"${STUB_TEAM:-BD8M7U5YBV}\" = BD8M7U5YBV ] || { echo 'test-requirement not satisfied' >&2; exit 3; }\n"
                + "    exit \"${STUB_VERIFY:-0}\" ;;\n"
                + "  --display) echo \"TeamIdentifier=${STUB_TEAM:-BD8M7U5YBV}\" >&2 ;;\n"
                + "esac\n");
            // mv as it is, except that where a row names a place, a Jason.app appears there just before the move into it.
            Stub("mv", "[ -n \"${STUB_RACE:-}\" ] && [ \"$2\" = \"$STUB_RACE\" ] && mkdir -p \"$2/Contents\"\nexec /bin/mv \"$@\"\n");
        }

        /// <summary>The stand-in jason: it logs each call, answers runtime status by the flag file, makes it at runtime start, and prints a row's report.</summary>
        private string StandInJason =>
            "#!/bin/sh\n"
            + $"printf '%s|%s\\n' \"$*\" \"${{JASON_DATA_DIR:-}}\" >> '{Log}'\n"
            + "case \"$1 ${2:-}\" in\n"
            + $"  'runtime status') [ -f '{Running}' ] ;;\n"
            + $"  'runtime start') : > '{Running}' ;;\n"
            + $"  'install --roles') [ -z \"${{STANDIN_INSTALL_STOPS:-}}\" ] || rm -f '{Running}'\n"
            + "    printf '%s\\n' \"${STANDIN_REPORT:-$STANDIN_DEFAULT}\" | sed \"s|{self}|$HOME/.local/bin/jason|g; s|{dir}|$HOME/.local/bin|g\" ;;\n"
            + "esac\n";

        /// <summary>The application's jason where the feed's application is put.</summary>
        public string Carried => Path.Combine(Shared, "Jason.app", "Contents", "Resources", "bundle", "jason");

        /// <summary>A command-line install of an earlier release in its installer's place, its notice beside it: a runtime may run from it.</summary>
        public void PlantCommandLineInstall()
        {
            var bin = Path.Combine(Home, ".local", "bin");
            Directory.CreateDirectory(bin);
            File.WriteAllText(Path.Combine(bin, "jason"), StandInJason);
            File.WriteAllText(Path.Combine(bin, "jason-LICENSE"), "MIT\n");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(Path.Combine(bin, "jason"), (UnixFileMode)0b111_101_101);
            }
        }

        public string Home { get; }

        public string Shared { get; }

        public string Data { get; }

        public string Log { get; }

        public string Running { get; }

        private string Stubs { get; }

        public Dictionary<string, string> Variables { get; } = new(StringComparer.Ordinal);

        public static Mac Start(TempTree tree)
        {
            Assert.SkipWhen(OperatingSystem.IsWindows() || !File.Exists("/bin/sh"), "install.sh's macOS route is run by sh, on Linux and macOS; CI's legs there run this row.");
            return new Mac(tree);
        }

        public List<string> Calls() => File.Exists(Log) ? [.. File.ReadAllLines(Log)] : [];

        /// <summary>The calls with the paths they were given left out.</summary>
        public List<string> PathlessCalls() => [.. Calls().Select(call => Regex.Replace(call, @" /\S+", string.Empty))];

        /// <summary>A feed holding the application's zip for <paramref name="arch"/> — a tar the stand-in ditto unpacks — its manifest and its checksums.</summary>
        public string Feed(string version = Version, string arch = "arm64", string? carries = null)
        {
            var feed = _tree.NewDirectory("feed");
            var asset = $"jason-desktop-macos-{arch}.zip";
            var zip = Path.Combine(feed, asset);
            var jason = StandInJason;
            using (var file = File.Create(zip))
            using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
            using (var tar = new TarWriter(gzip, TarEntryFormat.Pax))
            {
                tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "Jason.app/Contents/Resources/bundle/jason")
                {
                    Mode = (UnixFileMode)0b111_101_101,
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes(jason)),
                });
                tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "Jason.app/Contents/Resources/bundle/version.json")
                {
                    Mode = (UnixFileMode)0b110_100_100,
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes("{\"version\": \"" + (carries ?? version) + "\"}\n")),
                });
            }

            File.WriteAllText(Path.Combine(feed, ReleaseAssets.Manifest), "{\n  \"schema\": 1,\n  \"version\": \"" + version + "\"\n}\n");
            File.WriteAllText(Path.Combine(feed, ReleaseAssets.Checksums), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(zip))) + "  " + asset + "\n");
            return feed;
        }

        public (int Exit, string Stdout, string Stderr) Run(string feed, params string[] arguments)
        {
            var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "install", "install.sh"));
            Assert.Equal(1, Count(text, "\nSYSTEM_APPLICATIONS=\"/Applications\"\n"));
            var script = Path.Combine(_tree.NewDirectory("test-build"), "install.sh");
            File.WriteAllText(script, text.Replace("\nSYSTEM_APPLICATIONS=\"/Applications\"\n", $"\nSYSTEM_APPLICATIONS=\"{Shared}\"\n", StringComparison.Ordinal));

            var environment = new Dictionary<string, string>(Variables)
            {
                ["HOME"] = Home,
                ["JASON_DATA_DIR"] = Data,
                ["PATH"] = Stubs + ":" + (Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin"),
                ["STANDIN_DEFAULT"] = Report(placed: true, runtime: "not_running", completed: true),
                ["STUB_REQUIREMENT"] = Requirement,
            };
            return RunToEnd("/bin/sh", environment, [script, "--feed", feed, .. arguments]);
        }

        private void Stub(string name, string body)
        {
            var path = Path.Combine(Stubs, name);
            File.WriteAllText(path, "#!/bin/sh\n" + body);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, (UnixFileMode)0b111_101_101);
            }
        }
    }

    /// <summary>
    /// The parts the script starts as windowed programs, the setup and the application's Jason.exe, are windowed programs,
    /// read from the header of the program built for them, so that starting one opens no console window; the carried jason
    /// the script runs in its own console is a console program, as jason is.
    /// </summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public void Windows_the_setup_and_the_window_the_script_starts_are_windowed_programs_and_the_carried_jason_a_console_one()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The stand-ins are built by the .NET Framework's C# compiler, which Windows carries.");

        Assert.Equal(Subsystem.WindowsGui, StandInPrograms.SubsystemOf(WindowedStandIn.Value));
        Assert.Equal(Subsystem.WindowsCui, StandInPrograms.SubsystemOf(StandIn.Value));
    }

    // --- helpers -----------------------------------------------------------------------------------------------

    /// <summary>
    /// The stand-in, one program for both parts by its own file name: as the setup it puts itself in a folder's
    /// <c>resources\bundle</c> as the carried jason, with a <c>version.json</c>; as jason it logs, answers and reports.
    /// </summary>
    private static byte[] Compile(string target)
    {
        var csc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework64", "v4.0.30319", "csc.exe");
        Assert.SkipUnless(File.Exists(csc), "This Windows has no .NET Framework C# compiler to build the stand-in with.");
        var directory = Path.Combine(Path.GetTempPath(), "jason-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "standin.cs");
            var output = Path.Combine(directory, "standin.exe");
            File.WriteAllText(source, StandInSource);
            var (exit, stdout, stderr) = RunToEnd(csc, new Dictionary<string, string>(), ["/nologo", "/target:" + target, "/out:" + output, source]);
            Assert.True(exit == 0, stdout + stderr);
            return File.ReadAllBytes(output);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private const string StandInSource =
        """
        using System; using System.IO;
        static class Program {
          static string E(string n) { var v = Environment.GetEnvironmentVariable(n); return string.IsNullOrEmpty(v) ? null : v; }
          static int Main(string[] a) {
            var self = System.Reflection.Assembly.GetEntryAssembly().Location;
            var log = E("JASON_STANDIN_LOG");
            var running = E("JASON_STANDIN_RUNNING");
            if (Path.GetFileName(self) == "Jason.exe") { System.Threading.Thread.Sleep(120000); return 0; }
            if (Path.GetFileName(self).Contains("setup")) {
              if (log != null) File.AppendAllText(log, "setup " + string.Join(" ", a) + "\n");
              if (E("JASON_STANDIN_SETUP_STOPS") != null && running != null) File.Delete(running);
              var code = E("JASON_STANDIN_SETUP_EXIT");
              if (code != null && code != "0") return int.Parse(code);
              var line = Environment.CommandLine; var at = line.IndexOf(" /D=");
              var bundle = Path.Combine(line.Substring(at + 4).Trim(), "resources", "bundle");
              Directory.CreateDirectory(bundle);
              File.Copy(E("JASON_STANDIN_CARRIED"), Path.Combine(bundle, "jason.exe"), true);
              File.WriteAllText(Path.Combine(bundle, "version.json"), "{\"version\": \"" + E("JASON_STANDIN_VERSION") + "\"}\n");
              return 0;
            }
            if (log != null) File.AppendAllText(log, string.Join(" ", a) + "|" + E("JASON_DATA_DIR") + "\n");
            if (a.Length >= 2 && a[0] == "runtime" && a[1] == "status") return running != null && File.Exists(running) ? 0 : 3;
            if (a.Length >= 2 && a[0] == "runtime" && a[1] == "start") { if (running != null) File.WriteAllText(running, ""); Console.WriteLine("{\"instance\":\"standin\"}"); return 0; }
            if (a.Length >= 1 && a[0] == "install") {
              var said = E("JASON_STANDIN_INSTALL_STDERR"); if (said != null) Console.Error.WriteLine(said);
              var report = E("JASON_STANDIN_REPORT") ?? "{\"version\":\"0.49.0\",\"executable\":\"{self}\",\"command\":{\"runs\":\"{dir}\",\"placed\":true,\"open_shell\":\"{self}\",\"changed\":true,\"profiles\":[],\"taken_back\":[],\"finds\":\"{self}\",\"finds_this\":true},\"skills\":[],\"roles\":null,\"retired\":null,\"runtime\":\"not_running\",\"done\":[],\"left\":[],\"problems\":[],\"completed\":true}";
              Console.WriteLine(report.Replace("{self}", self.Replace("\\", "\\\\")).Replace("{dir}", Path.GetDirectoryName(self).Replace("\\", "\\\\")));
              var code = E("JASON_STANDIN_INSTALL_EXIT");
              return code == null ? 0 : int.Parse(code);
            }
            return 0;
          }
        }
        """;

    private static bool Writable(string directory)
    {
        try
        {
            var probe = Path.Combine(directory, ".probe");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static int Count(string text, string part) => Regex.Matches(text, Regex.Escape(part)).Count;

    private static List<string> Lines(string output) =>
        [.. output.ReplaceLineEndings("\n").Split('\n').Select(line => line.TrimEnd()).Where(line => line.Length > 0)];

    /// <summary>
    /// A sentence said on stderr, read without PowerShell's colours, its gutter and white space: its error view wraps a
    /// message at the console's width, inside a word as well.
    /// </summary>
    private static void AssertSaid(string sentence, string said)
    {
        static string Squeezed(string text) =>
            Regex.Replace(Regex.Replace(Regex.Replace(text, @"\x1b\[[0-9;?]*[A-Za-z]", string.Empty), @"(?m)^\s*\|", string.Empty), @"\s+", string.Empty);
        Assert.True(Squeezed(said).Contains(Squeezed(sentence), StringComparison.Ordinal), $"Expected: {sentence}{Environment.NewLine}Said: {said}");
    }

    private static string Script(string name) => File.ReadAllText(Path.Combine(RepositoryRoot(), "install", name)).ReplaceLineEndings("\n");

    /// <summary>A script without its comments: the lines that run.</summary>
    private static string Code(string name) =>
        string.Join('\n', Script(name).Split('\n').Where(line => !line.TrimStart().StartsWith('#')));

    private static (int Exit, string Stdout, string Stderr) RunToEnd(string executable, IReadOnlyDictionary<string, string> environment, string[] arguments, string? workingDirectory = null) =>
        RunToEnd(new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = workingDirectory ?? string.Empty }, environment, arguments);

    private static (int Exit, string Stdout, string Stderr) RunToEnd(ProcessStartInfo start, IReadOnlyDictionary<string, string> environment, string[] arguments)
    {
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.StandardOutputEncoding = Encoding.UTF8;
        start.StandardErrorEncoding = Encoding.UTF8;
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
            process.Kill(entireProcessTree: true);
            Assert.Fail($"{Path.GetFileName(start.FileName)} did not finish in two minutes.");
        }

        return (process.ExitCode, stdout.Result, stderr.Result);
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
