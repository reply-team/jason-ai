using System.Diagnostics;
using System.Runtime.Versioning;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Jason.Contracts.Update;

namespace Jason.Cli.Tests.Documentation;

/// <summary>
/// The install scripts where they install Jason Desktop: <c>install.ps1</c> on Windows and <c>install.sh</c> on macOS. Each
/// fetches the release's package for the machine, checks it against <c>checksums.txt</c> and its signature, places the
/// application without a person, runs the jason the application carries as <c>jason install --roles</c>, reads its one
/// report, starts a runtime again where one answered before and none answers after or where the report says it stopped
/// one, ends non-zero saying which act failed when jason was not put in the terminal, and on success ends with the full
/// path of jason for a shell that was already open.
/// </summary>
/// <remarks>
/// <para>
/// Nothing real is installed or run. On Windows the feed's setup is a stand-in built by the .NET Framework's C# compiler,
/// which every Windows carries: run with <c>/S /D=&lt;folder&gt;</c>, it puts a stand-in carried jason and its
/// <c>version.json</c> in that folder's <c>resources\bundle</c>, under a <c>LOCALAPPDATA</c> of the test's own. The stand-in
/// jason writes each call it is given into a log, answers <c>runtime status</c> by whether a flag file of the test's exists,
/// makes the file at <c>runtime start</c>, and prints the report a row gives it for <c>install</c>. A stand-in carries no
/// signature, so a row runs a copy of the script whose signature check returns at once: a test build, and only here; one
/// row runs the published script to see the check refuse.
/// </para>
/// <para>
/// On macOS's route the commands a Mac has that a test machine may not — <c>uname</c>, <c>sysctl</c>, <c>ditto</c> and
/// <c>codesign</c> — are stand-ins first on the PATH, the application's zip is a tar the stand-in <c>ditto</c> unpacks, and
/// the script's copy names a folder of the test's own as the shared applications folder. Those rows run on Linux and macOS,
/// wherever there is a <c>/bin/sh</c>; on Windows they are skipped, and the Windows rows run there alone.
/// </para>
/// </remarks>
public sealed class InstallDesktopScriptTests : IDisposable
{
    private const string Version = "0.49.0";

    private const string Setup = "jason-desktop-windows-x64-setup.exe";

    /// <summary>The line the published script's signature check opens with, and the test build's in its place.</summary>
    private const string SignatureCheck = "    function Assert-Signed([string] $file) {";

    private const string ShortCircuited = "    function Assert-Signed([string] $file) { return";

    private static readonly Lazy<byte[]> StandIn = new(Compile);

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
        Assert.Equal(1, Count(sh, "\nSYSTEM_APPLICATIONS=\"/Applications\"\n"));
        Assert.Contains("codesign --verify --deep --strict \"$STAGE/Jason.app\"", sh, StringComparison.Ordinal);
        Assert.Contains("[ \"$TEAM\" = \"$APP_TEAM\" ]", sh, StringComparison.Ordinal);
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
        // The application's own executable is named once, to look for its window, and never started.
        Assert.Equal(1, Count(ps1, "Jason.exe"));
        Assert.DoesNotMatch(new Regex(@"(Start-Process|Invoke-Item|&)\s+(-FilePath\s+)?\$window"), ps1);
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
        File.WriteAllBytes(window, StandIn.Value);
        using var open = System.Diagnostics.Process.Start(new ProcessStartInfo(window) { UseShellExecute = false, CreateNoWindow = true })!;
        try
        {
            var run = windows.Run(windows.Feed("0.49.1"));

            Assert.Equal(1, run.Exit);
            AssertSaid("Close Jason Desktop and run this line again.", run.Stderr);
            Assert.DoesNotContain(windows.Calls(), call => call.StartsWith("setup", StringComparison.Ordinal));
            Assert.Contains(Version, File.ReadAllText(Path.Combine(windows.App, "resources", "bundle", "version.json")), StringComparison.Ordinal);
        }
        finally
        {
            open.Kill();
            open.WaitForExit();
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

    [Theory]
    [InlineData(1, "a Jason process runs from")]
    [InlineData(2, "another install of Jason ran at the same time")]
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
        Assert.Equal(
            ["ditto -x -k", "codesign --verify --deep --strict", "codesign --display --verbose=2", $"install --roles|{mac.Data}"],
            mac.Calls().Select(call => Regex.Replace(call, @" /\S+", string.Empty)));
        var lines = Lines(run.Stdout);
        Assert.Equal($"Jason {Version} is licensed under the MIT licence: https://github.com/reply-team/jason-ai/blob/v{Version}/LICENSE", lines[^3]);
        Assert.Equal($"A terminal opened from now on finds jason by name; this one, and an agent's session already running, call it by its full path: {Path.Combine(mac.Home, ".local", "bin", "jason")}", lines[^1]);
    }

    [Fact]
    public void Macos_an_intel_mac_is_given_the_x64_application()
    {
        var mac = Mac.Start(_tree);
        mac.Variables["STUB_ARM64"] = "absent";

        var run = mac.Run(mac.Feed(arch: "x64"));

        Assert.True(run.Exit == 0, run.Stdout + run.Stderr);
        Assert.Contains("for macOS on x64", run.Stdout, StringComparison.Ordinal);
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
        Assert.Equal([$"runtime status|{mac.Data}", $"install --roles|{mac.Data}"], mac.Calls());
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
        Assert.Equal([$"runtime status|{mac.Data}", $"install --roles|{mac.Data}"], mac.Calls());
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
    private static string Report(bool placed, string runtime, bool completed) =>
        "{\"version\":\"" + Version + "\",\"executable\":\"{self}\",\"command\":{\"runs\":\"{dir}\",\"placed\":" + (placed ? "true" : "false")
        + ",\"open_shell\":\"{self}\",\"changed\":true,\"profiles\":[],\"taken_back\":[]},\"skills\":[],\"roles\":null,\"retired\":null,\"runtime\":\""
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
            File.WriteAllBytes(setup, StandIn.Value);
            File.WriteAllText(Path.Combine(feed, ReleaseAssets.Manifest), "{\n  \"schema\": 1,\n  \"version\": \"" + version + "\"\n}\n");
            File.WriteAllText(Path.Combine(feed, ReleaseAssets.Checksums), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(setup))) + "  " + Setup + "\n");
            Variables["JASON_STANDIN_VERSION"] = version;
            return feed;
        }

        /// <summary>The calls the stand-ins were given, in order: <c>setup &lt;arguments&gt;</c>, else <c>&lt;arguments&gt;|&lt;data directory&gt;</c>.</summary>
        public List<string> Calls() => File.Exists(Log) ? [.. File.ReadAllLines(Log)] : [];

        public (int Exit, string Stdout, string Stderr) Run(string feed, bool published = false, bool windowsPowerShell = false, params string[] arguments)
        {
            var script = Path.Combine(RepositoryRoot(), "install", "install.ps1");
            if (!published)
            {
                var text = File.ReadAllText(script);
                Assert.Equal(1, Count(text.ReplaceLineEndings("\n"), SignatureCheck + "\n"));
                script = Path.Combine(_tree.NewDirectory("test-build"), "install.ps1");
                File.WriteAllText(script, text.Replace(SignatureCheck, ShortCircuited, StringComparison.Ordinal));
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

            return RunToEnd(Pwsh(), environment, call);
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
            Stub("sysctl", "[ \"${STUB_ARM64:-1}\" = absent ] && { echo \"sysctl: unknown oid 'hw.optional.arm64'\" >&2; exit 1; }\necho \"${STUB_ARM64:-1}\"\n");
            Stub("ditto", $"echo \"ditto $*\" >> '{Log}'\ntar -xzf \"$3\" -C \"$4\"\n");
            Stub("codesign", $"echo \"codesign $*\" >> '{Log}'\ncase \"$1\" in --verify) exit \"${{STUB_VERIFY:-0}}\" ;; --display) echo \"TeamIdentifier=${{STUB_TEAM:-BD8M7U5YBV}}\" >&2 ;; esac\n");
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

        /// <summary>A feed holding the application's zip for <paramref name="arch"/> — a tar the stand-in ditto unpacks — its manifest and its checksums.</summary>
        public string Feed(string version = Version, string arch = "arm64")
        {
            var feed = _tree.NewDirectory("feed");
            var asset = $"jason-desktop-macos-{arch}.zip";
            var zip = Path.Combine(feed, asset);
            var jason =
                "#!/bin/sh\n"
                + $"printf '%s|%s\\n' \"$*\" \"${{JASON_DATA_DIR:-}}\" >> '{Log}'\n"
                + "case \"$1 ${2:-}\" in\n"
                + $"  'runtime status') [ -f '{Running}' ] ;;\n"
                + $"  'runtime start') : > '{Running}' ;;\n"
                + "  'install --roles') printf '%s\\n' \"${STANDIN_REPORT:-$STANDIN_DEFAULT}\" | sed \"s|{self}|$HOME/.local/bin/jason|g; s|{dir}|$HOME/.local/bin|g\" ;;\n"
                + "esac\n";
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
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes("{\"version\": \"" + version + "\"}\n")),
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

    // --- helpers -----------------------------------------------------------------------------------------------

    /// <summary>
    /// The stand-in, one program for both parts by its own file name: as the setup it puts itself in a folder's
    /// <c>resources\bundle</c> as the carried jason, with a <c>version.json</c>; as jason it logs, answers and reports.
    /// </summary>
    private static byte[] Compile()
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
            var (exit, stdout, stderr) = RunToEnd(csc, new Dictionary<string, string>(), ["/nologo", "/target:exe", "/out:" + output, source]);
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
              var code = E("JASON_STANDIN_SETUP_EXIT");
              if (code != null && code != "0") return int.Parse(code);
              var line = Environment.CommandLine; var at = line.IndexOf(" /D=");
              var bundle = Path.Combine(line.Substring(at + 4).Trim(), "resources", "bundle");
              Directory.CreateDirectory(bundle);
              File.Copy(E("JASON_STANDIN_CARRIED"), Path.Combine(bundle, "jason.exe"), true);
              File.WriteAllText(Path.Combine(bundle, "version.json"), "{\"version\": \"" + E("JASON_STANDIN_VERSION") + "\"}\n");
              if (E("JASON_STANDIN_SETUP_STOPS") != null && running != null) File.Delete(running);
              return 0;
            }
            if (log != null) File.AppendAllText(log, string.Join(" ", a) + "|" + E("JASON_DATA_DIR") + "\n");
            if (a.Length >= 2 && a[0] == "runtime" && a[1] == "status") return running != null && File.Exists(running) ? 0 : 3;
            if (a.Length >= 2 && a[0] == "runtime" && a[1] == "start") { if (running != null) File.WriteAllText(running, ""); Console.WriteLine("{\"instance\":\"standin\"}"); return 0; }
            if (a.Length >= 1 && a[0] == "install") {
              var report = E("JASON_STANDIN_REPORT") ?? "{\"version\":\"0.49.0\",\"executable\":\"{self}\",\"command\":{\"runs\":\"{dir}\",\"placed\":true,\"open_shell\":\"{self}\",\"changed\":true,\"profiles\":[],\"taken_back\":[]},\"skills\":[],\"roles\":null,\"retired\":null,\"runtime\":\"not_running\",\"done\":[],\"left\":[],\"problems\":[],\"completed\":true}";
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

    private static string Pwsh()
    {
        var pwsh = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(directory => new[] { Path.Combine(directory, "pwsh.exe"), Path.Combine(directory, "pwsh") })
            .FirstOrDefault(File.Exists);
        Assert.SkipWhen(pwsh is null, "pwsh is not on this machine's PATH; every CI runner has it.");
        return pwsh!;
    }

    private static (int Exit, string Stdout, string Stderr) RunToEnd(string executable, IReadOnlyDictionary<string, string> environment, string[] arguments)
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
            Assert.Fail($"{Path.GetFileName(executable)} did not finish in two minutes.");
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
