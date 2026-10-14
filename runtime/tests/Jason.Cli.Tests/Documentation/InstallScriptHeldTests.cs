using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

namespace Jason.Cli.Tests.Documentation;

/// <summary>
/// <c>install.ps1</c> clears what an earlier install left, steps the old executable aside and moves the new one in, and
/// each of those waits out a file another program holds open, as jason itself does: ten seconds, every quarter second, a
/// sharing or lock violation and nothing else. A file still held at the end is said in a sentence of its own, and when the
/// new executable cannot move in, the old one goes back first, so that "nothing was installed" is true.
/// </summary>
/// <remarks>
/// <para>
/// The script is run for real, under PowerShell 7 and under Windows PowerShell 5.1, which is what the documented one-liner
/// runs in, against a feed in a directory. The executable in it is a copy of the system's own <c>tar.exe</c>: the script
/// runs nothing of what it installs but the unpacked file's <c>--version</c>, whose first line the feed's manifest names.
/// </para>
/// <para>
/// The holds are real, and released on the script's own progress rather than on a clock where it matters: a held file a
/// step is waiting on is the moment a test can reach the next file. A file held to the end costs the script's whole ten
/// seconds, so those cases run under Windows PowerShell 5.1 alone; the cases that release run under both shells, which is
/// where the two could differ — in what a refusal carries.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class InstallScriptHeldTests : IDisposable
{
    private const string Pwsh = "pwsh";

    private const string WindowsPowerShell = "Windows PowerShell 5.1";

    private const string Held = "is held open by another program; it was tried for 10 seconds.";

    private const string NewText = "The notice the archive holds.\n";

    /// <summary>The two refusals the script's own wait tries again, as their HRESULTs: a sharing and a lock violation.</summary>
    private const int SharingViolation = unchecked((int)0x80070020);

    private const int LockViolation = unchecked((int)0x80070021);

    /// <summary>
    /// The time the archive's executable carries. Windows PowerShell's Expand-Archive writes an entry, closes it, and then
    /// opens it again to set this time on it: a hold taken in between refuses that second open, and the script stops at
    /// unpacking instead of reaching the step a test means to hold. The file carries this time only once the extractor
    /// is done with it, so it is the one moment a hold cannot meet the extractor.
    /// </summary>
    private static readonly DateTimeOffset Unpacked = new(2001, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly TempTree _tree = new();

    private readonly string _feed;

    private readonly string _version;

    private readonly byte[] _standIn;

    private readonly string _temp;

    private readonly string _install;

    public InstallScriptHeldTests()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "install.ps1 is the Windows installer.");
        _standIn = File.ReadAllBytes(Path.Combine(Environment.SystemDirectory, "tar.exe"));
        _version = FirstLine(Path.Combine(Environment.SystemDirectory, "tar.exe"), "--version").Split('+')[0];
        _feed = _tree.NewDirectory("feed");
        _temp = _tree.NewDirectory("temp");
        _install = _tree.NewDirectory("programs");

        var archive = Path.Combine(_feed, "jason-win-x64.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            var executable = zip.CreateEntry("jason.exe");
            executable.LastWriteTime = Unpacked;
            using (var entry = executable.Open())
            {
                entry.Write(_standIn);
            }

            foreach (var name in (string[])["LICENSE", "THIRD-PARTY-NOTICES"])
            {
                using var entry = zip.CreateEntry(name).Open();
                entry.Write(Encoding.UTF8.GetBytes(NewText));
            }
        }

        File.WriteAllText(Path.Combine(_feed, "manifest.json"), "{ \"schema\": 1, \"version\": \"" + _version + "\" }");
        File.WriteAllText(
            Path.Combine(_feed, "checksums.txt"),
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(archive))) + "  jason-win-x64.zip\n");
    }

    public void Dispose() => _tree.Dispose();

    private string Target => Path.Combine(_install, "jason.exe");

    private string Previous => Path.Combine(_install, "jason.previous.exe");

    /// <summary>The old executable, held for a moment as it is stepped aside, is moved once it is let go.</summary>
    [Theory]
    [InlineData(Pwsh)]
    [InlineData(WindowsPowerShell)]
    public async Task An_old_executable_held_for_a_moment_steps_aside_once_it_is_let_go(string shell)
    {
        Placeholder(Target);
        var holder = Hold(Target);
        var install = Start(shell);
        await Task.Delay(1500, Ct);
        holder.Dispose();

        var (exit, _, stderr) = await install;

        Assert.True(exit == 0, stderr);
        Assert.Equal(_standIn, File.ReadAllBytes(Target));
        Assert.False(File.Exists(Previous));
    }

    /// <summary>What an earlier install left, held for a moment, is cleared once it is let go.</summary>
    [Theory]
    [InlineData(Pwsh)]
    [InlineData(WindowsPowerShell)]
    public async Task A_leftover_held_for_a_moment_is_cleared_once_it_is_let_go(string shell)
    {
        Placeholder(Target);
        Placeholder(Previous);
        var holder = Hold(Previous);
        var install = Start(shell);
        await Task.Delay(1500, Ct);
        holder.Dispose();

        var (exit, _, stderr) = await install;

        Assert.True(exit == 0, stderr);
        Assert.Equal(_standIn, File.ReadAllBytes(Target));
        Assert.False(File.Exists(Previous));
    }

    /// <summary>The old executable held for the whole wait: said, and nothing was installed.</summary>
    [Fact]
    public async Task An_old_executable_held_for_the_whole_wait_is_said_and_nothing_is_installed()
    {
        var placeholder = Placeholder(Target);

        int exit;
        string stderr;
        using (Hold(Target))
        {
            (exit, _, stderr) = await Start(WindowsPowerShell);
        }

        Assert.NotEqual(0, exit);
        AssertSaid($"'{FromHome(Target)}' {Held} Nothing was installed: run this script again once it is free.", stderr);
        Assert.Equal(placeholder, File.ReadAllBytes(Target));
    }

    /// <summary>What an earlier install left, held for the whole wait: said, and nothing was installed or moved.</summary>
    [Fact]
    public async Task A_leftover_held_for_the_whole_wait_is_said_and_nothing_is_installed()
    {
        var placeholder = Placeholder(Target);
        Placeholder(Previous);

        int exit;
        string stderr;
        using (Hold(Previous))
        {
            (exit, _, stderr) = await Start(WindowsPowerShell);
        }

        Assert.NotEqual(0, exit);
        AssertSaid($"'{FromHome(Previous)}' {Held} Nothing was installed: run this script again once it is free.", stderr);
        Assert.Equal(placeholder, File.ReadAllBytes(Target));
    }

    /// <summary>
    /// A leftover that a program still runs from is no hold, and nothing waits for it: it is refused at once, in
    /// PowerShell's own words, exactly as before — a running program ends when somebody stops it, not in ten seconds.
    /// </summary>
    /// <remarks>
    /// "At once" is counted, not timed: the script runs with its pause stood in for by one that only writes each pause down,
    /// and none may be written. A whole run of the script on a loaded machine takes as long as a wait would.
    /// </remarks>
    [Theory]
    [InlineData(Pwsh)]
    [InlineData(WindowsPowerShell)]
    public async Task A_leftover_a_program_still_runs_from_is_refused_at_once_as_before(string shell)
    {
        Placeholder(Target);
        File.Copy(Path.Combine(Environment.SystemDirectory, "PING.EXE"), Previous);
        using var running = System.Diagnostics.Process.Start(new ProcessStartInfo(Previous, "-n 60 127.0.0.1") { CreateNoWindow = true, UseShellExecute = false })!;
        try
        {
            var pauses = Path.Combine(_tree.NewDirectory("pauses"), "pauses.txt");
            var (exit, _, stderr) = await Start(shell, pauses);

            Assert.False(File.Exists(pauses), $"The script paused {(File.Exists(pauses) ? File.ReadAllLines(pauses).Length : 0)} time(s) before it refused: {stderr}");
            Assert.NotEqual(0, exit);
            Assert.Contains("Cannot create a file when that file already exists.", stderr, StringComparison.Ordinal);
            Assert.DoesNotContain("held open", stderr, StringComparison.Ordinal);
            Assert.True(File.Exists(Target));
        }
        finally
        {
            running.Kill();
            await running.WaitForExitAsync(Ct);
        }
    }

    /// <summary>
    /// The new executable held for the whole wait as it moves in: the old one goes back first, and the sentence says it is
    /// back and that nothing was installed.
    /// </summary>
    [Fact]
    public async Task A_new_executable_held_for_the_whole_wait_puts_the_old_one_back()
    {
        var placeholder = Placeholder(Target);

        // The old one held keeps the script waiting at its step aside, with the unpacked copy already there to reach.
        var old = Hold(Target);
        var install = Start(WindowsPowerShell);
        int exit;
        string stderr;
        using (await HoldOnceItOpensAsync(await UnpackedAsync()))
        {
            old.Dispose();
            (exit, _, stderr) = await install;
        }

        Assert.NotEqual(0, exit);
        AssertSaid(
            $"The downloaded jason.exe {Held} The jason that was installed is back at '{FromHome(Target)}'; nothing was installed: run this script again.",
            stderr);
        Assert.Equal(placeholder, File.ReadAllBytes(Target));
        Assert.False(File.Exists(Previous));
    }

    /// <summary>And where the old one cannot go back either, the sentence says where it is and where it belongs.</summary>
    [Fact]
    public async Task A_new_executable_held_whose_old_one_cannot_go_back_says_where_the_old_one_is()
    {
        var placeholder = Placeholder(Target);
        var old = Hold(Target);
        var install = Start(WindowsPowerShell);
        int exit;
        string stderr;
        using (await HoldOnceItOpensAsync(await UnpackedAsync()))
        {
            old.Dispose();
            using (await HoldOnceFreeAsync(await AppearsAsync(Previous, install)))
            {
                (exit, _, stderr) = await install;
            }
        }

        Assert.NotEqual(0, exit);
        AssertSaid(
            $"The downloaded jason.exe {Held} The jason that was installed is at '{FromHome(Previous)}': move it back to '{FromHome(Target)}' by hand.",
            stderr);
        Assert.Equal(placeholder, File.ReadAllBytes(Previous));
        Assert.False(File.Exists(Target));
    }

    /// <summary>On a first install there is no old one to put back, and the sentence says only that nothing was installed.</summary>
    [Fact]
    public async Task A_new_executable_held_on_a_first_install_says_nothing_was_installed()
    {
        // A leftover held keeps the script waiting at its clearing, with the unpacked copy already there to reach.
        Placeholder(Previous);
        var leftover = Hold(Previous);
        var install = Start(WindowsPowerShell);
        int exit;
        string stderr;
        string stdout;
        using (await HoldOnceItOpensAsync(await UnpackedAsync()))
        {
            leftover.Dispose();
            (exit, stdout, stderr) = await install;
        }

        Assert.True(exit != 0, $"The install exited 0.{Environment.NewLine}stdout: {stdout}{Environment.NewLine}stderr: {stderr}");
        AssertSaid($"The downloaded jason.exe {Held} Nothing was installed: run this script again.", stderr);
        Assert.False(File.Exists(Target));
    }

    /// <summary>
    /// A refusal of the new executable that is not a hold puts the old one back too, before it is said in PowerShell's own
    /// words — at once, with nothing waited for.
    /// </summary>
    [Theory]
    [InlineData(Pwsh)]
    [InlineData(WindowsPowerShell)]
    public async Task A_new_executable_refused_otherwise_puts_the_old_one_back_and_is_said_at_once(string shell)
    {
        var placeholder = Placeholder(Target);
        var old = Hold(Target);
        var install = Start(shell);
        var unpacked = await UnpackedAsync();

        // Opened once, to know the archive has finished writing it, then denied its deletion: no hold, a refusal.
        (await HoldOnceItOpensAsync(unpacked)).Dispose();
        var undo = DenyDeletion(unpacked);
        int exit;
        string stderr;
        try
        {
            old.Dispose();
            (exit, _, stderr) = await install;
        }
        finally
        {
            undo();
        }

        Assert.NotEqual(0, exit);
        Assert.DoesNotContain("held open", stderr, StringComparison.Ordinal);
        Assert.Equal(placeholder, File.ReadAllBytes(Target));
        Assert.False(File.Exists(Previous));
    }

    /// <summary>
    /// A notice file beside the executable, held for a moment as the new one is renamed onto it, is replaced once it is
    /// let go. It is let go once the new notice is staged beside it, which is the moment the script starts waiting.
    /// </summary>
    [Theory]
    [InlineData(Pwsh)]
    [InlineData(WindowsPowerShell)]
    public async Task A_notice_held_for_a_moment_is_replaced_once_it_is_let_go(string shell)
    {
        var notice = Path.Combine(_install, "jason-LICENSE");
        File.WriteAllText(notice, "The notice of the release before.\n");
        var holder = Hold(notice);
        var install = Start(shell);
        await AppearsAsync(notice + ".installing", install);
        await Task.Delay(500, Ct);
        holder.Dispose();

        var (exit, _, stderr) = await install;

        Assert.True(exit == 0, stderr);
        Assert.Equal(_standIn, File.ReadAllBytes(Target));
        Assert.Equal(NewText, File.ReadAllText(notice));
        Assert.False(File.Exists(notice + ".installing"));
    }

    /// <summary>
    /// A notice file held for the whole wait: Jason is installed, the notice is not, and the run fails with 1 and says
    /// both, with the advice to run the script again.
    /// </summary>
    [Fact]
    public async Task A_notice_held_for_the_whole_wait_is_said_and_Jason_stays_installed()
    {
        var notice = Path.Combine(_install, "jason-THIRD-PARTY-NOTICES");
        File.WriteAllText(notice, "The notices of the release before.\n");

        int exit;
        string stderr;
        using (Hold(notice))
        {
            (exit, _, stderr) = await Start(WindowsPowerShell);
        }

        Assert.Equal(1, exit);
        AssertSaid(
            $"Jason {_version} is installed at {Target}, and its licence files could not be written beside it: '{FromHome(notice)}' {Held} Run this script again.",
            stderr);
        Assert.Equal(_standIn, File.ReadAllBytes(Target));
        Assert.Equal(NewText, File.ReadAllText(Path.Combine(_install, "jason-LICENSE")));
        Assert.Equal("The notices of the release before.\n", File.ReadAllText(notice));
        Assert.False(File.Exists(notice + ".installing"));
    }

    /// <summary>A file that is not a program, where an earlier jason would be: its <c>--version</c> fails, so it is replaced.</summary>
    private static byte[] Placeholder(string path)
    {
        var bytes = Encoding.ASCII.GetBytes("not a program: " + Path.GetFileName(path));
        File.WriteAllBytes(path, bytes);
        return bytes;
    }

    /// <summary>Opened as a reader opens a file: reading it, and letting others read it, and nothing else.</summary>
    private static FileStream Hold(string file) => new(file, FileMode.Open, FileAccess.Read, FileShare.Read);

    /// <summary>A path as the script says it: from <c>~</c> where it is inside the home directory.</summary>
    private static string FromHome(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\');
        return path.StartsWith(home + "\\", StringComparison.OrdinalIgnoreCase) ? "~" + path[home.Length..] : path;
    }

    /// <summary>The sentence, as said: PowerShell wraps an error at its width, so both are read without white space.</summary>
    private static void AssertSaid(string sentence, string stderr) =>
        Assert.True(Regex.Replace(stderr, @"\s+", string.Empty).Contains(Regex.Replace(sentence, @"\s+", string.Empty), StringComparison.Ordinal), $"Expected: {sentence}{Environment.NewLine}Said: {stderr}");

    /// <summary>The unpacked copy in the script's temporary directory, once it is there.</summary>
    private async Task<string> UnpackedAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            var found = Directory.EnumerateDirectories(_temp, "jason-install-*")
                .Select(directory => Path.Combine(directory, "unpacked", "jason.exe"))
                .FirstOrDefault(File.Exists);
            if (found is not null)
            {
                return found;
            }

            await Task.Delay(20, Ct);
        }

        throw new TimeoutException("install.ps1 never unpacked the archive.");
    }

    /// <summary>
    /// Held as <see cref="Hold"/> holds it, once the extractor has set the archive's time on it — its last act on the file
    /// — and whatever else still has it open, the script's own run of it, lets a reader in.
    /// </summary>
    private static async Task<FileStream> HoldOnceItOpensAsync(string path)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (true)
        {
            try
            {
                if (File.GetLastWriteTime(path) == Unpacked.DateTime)
                {
                    return Hold(path);
                }
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException($"{path} was never unpacked whole and free to hold.");
            }

            await Task.Delay(20, Ct);
        }
    }

    /// <summary>Held as <see cref="Hold"/> holds it, once whatever has the file the script just made lets go of it.</summary>
    private static async Task<FileStream> HoldOnceFreeAsync(string path)
    {
        // The rename that made it, or a scanner reading it, can still have it open for a moment. Two seconds of trying is
        // well inside the ten the script spends on the held new executable before it touches this file again; a sharing
        // or lock violation is all that is tried again, and anything else — the file gone — ends the test at once.
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (true)
        {
            try
            {
                return Hold(path);
            }
            catch (IOException ex) when (ex.HResult is SharingViolation or LockViolation && DateTime.UtcNow < deadline)
            {
            }

            await Task.Delay(20, Ct);
        }
    }

    /// <summary>
    /// The file, once the script has put it there. A script that ended first never will, and waiting out the rest of the
    /// minute would only hide why: the failure says what it said.
    /// </summary>
    private static async Task<string> AppearsAsync(string path, Task<(int Exit, string Stdout, string Stderr)> install)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (!File.Exists(path))
        {
            // Looked at again once the script has ended: it may have put the file there just before it did.
            if (install.IsCompleted && !File.Exists(path))
            {
                var (exit, stdout, stderr) = await install;
                throw new TimeoutException(
                    $"{path} never appeared: the script ended first, with exit {exit}.{Environment.NewLine}stdout: {stdout}{Environment.NewLine}stderr: {stderr}");
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException($"{path} never appeared.");
            }

            await Task.Delay(20, Ct);
        }

        return path;
    }

    /// <summary>Denies this account the file's deletion, on the file and as its directory's child, and returns the undoing.</summary>
    private static Action DenyDeletion(string path)
    {
        var account = WindowsIdentity.GetCurrent().User!;
        var file = new FileInfo(path);
        var directory = file.Directory!;
        var onFile = new FileSystemAccessRule(account, FileSystemRights.Delete, AccessControlType.Deny);
        var onChildren = new FileSystemAccessRule(account, FileSystemRights.DeleteSubdirectoriesAndFiles, AccessControlType.Deny);
        var fileSecurity = file.GetAccessControl();
        fileSecurity.AddAccessRule(onFile);
        file.SetAccessControl(fileSecurity);
        var directorySecurity = directory.GetAccessControl();
        directorySecurity.AddAccessRule(onChildren);
        directory.SetAccessControl(directorySecurity);
        return () =>
        {
            var directoryNow = directory.GetAccessControl();
            directoryNow.RemoveAccessRule(onChildren);
            directory.SetAccessControl(directoryNow);
            if (file.Exists)
            {
                var fileNow = file.GetAccessControl();
                fileNow.RemoveAccessRule(onFile);
                file.SetAccessControl(fileNow);
            }
        };
    }

    /// <summary>The real <c>install.ps1</c> under the shell named, against this test's feed and directories.</summary>
    /// <param name="shell">Which PowerShell runs it.</param>
    /// <param name="pauses">
    /// Where to count the script's pauses, or null to run it as a file as a person's shell would. With a file, the script is
    /// run under a <c>Start-Sleep</c> of the test's own, a function that outranks the cmdlet and writes each pause down
    /// instead of taking it.
    /// </param>
    private Task<(int Exit, string Stdout, string Stderr)> Start(string shell, string? pauses = null)
    {
        var script = Path.Combine(RepositoryRoot(), "install", "install.ps1");
        var start = shell == Pwsh
            ? new ProcessStartInfo(PwshPath())
            : new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"));
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        string[] run = pauses is null
            ? ["-File", script, "-InstallDir", _install, "-NoModifyPath", "-Feed", _feed]
            : ["-Command", "function global:Start-Sleep { param([int] $Milliseconds) Add-Content -LiteralPath $env:JASON_TEST_PAUSES -Value $Milliseconds }; "
                + $"& {Quoted(script)} -InstallDir {Quoted(_install)} -NoModifyPath -Feed {Quoted(_feed)}"];
        foreach (var argument in (string[])["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", .. run])
        {
            start.ArgumentList.Add(argument);
        }

        if (pauses is not null)
        {
            start.Environment["JASON_TEST_PAUSES"] = pauses;
        }

        start.Environment["POWERSHELL_TELEMETRY_OPTOUT"] = "1";
        start.Environment["LOCALAPPDATA"] = _tree.Root;

        // A test run from PowerShell 7 - as CI's is - hands its children PowerShell 7's module path, and Windows PowerShell
        // started under it finds no Get-FileHash. A person's Windows PowerShell builds its own, so this one does too.
        if (shell == WindowsPowerShell)
        {
            start.Environment.Remove("PSModulePath");
        }
        start.Environment["TEMP"] = _temp;
        start.Environment["TMP"] = _temp;
        return RunAsync(start);
    }

    /// <summary>A path as a PowerShell literal.</summary>
    private static string Quoted(string path) => "'" + path.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static async Task<(int Exit, string Stdout, string Stderr)> RunAsync(ProcessStartInfo start)
    {
        using var process = System.Diagnostics.Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(Ct);
        var stderr = process.StandardError.ReadToEndAsync(Ct);
        await process.WaitForExitAsync(Ct).WaitAsync(TimeSpan.FromSeconds(120), Ct);
        return (process.ExitCode, await stdout, await stderr);
    }

    private static string PwshPath()
    {
        var pwsh = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, "pwsh.exe"))
            .FirstOrDefault(File.Exists);
        Assert.SkipWhen(pwsh is null, "pwsh is not on this machine's PATH; every CI runner has it.");
        return pwsh!;
    }

    private static string FirstLine(string program, string argument)
    {
        using var process = System.Diagnostics.Process.Start(new ProcessStartInfo(program, argument) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true })!;
        var line = process.StandardOutput.ReadLine() ?? string.Empty;
        process.WaitForExit();
        return line;
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
