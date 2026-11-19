
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Jason.Cli.Status;
using Jason.Contracts.Discovery;

namespace Jason.Cli.Update;

/// <summary>How a setup run as a waited child ended, or that it had not ended within its bound.</summary>
/// <param name="Ended">Whether it ended within the bound. One that did not is left running: never killed.</param>
/// <param name="ExitCode">Its exit code, where it ended.</param>
public sealed record SetupRun(bool Ended, int ExitCode);

/// <summary>The exit codes of Jason Desktop's installer, run silently, that the move reads as more than a failure.</summary>
public static class SetupExits
{
    /// <summary>A Jason process runs from the place the installer was to replace files in.</summary>
    public const int ProcessRunsFromPlace = 1;

    /// <summary>
    /// The installer's exit 2: it stopped before Jason Desktop was installed. Run silently it has two causes. Another installer
    /// of Jason Desktop holds the installer's mutex, and the template's <c>.onInit</c> aborts, which NSIS ends with 2 when no
    /// error level was set (the template's own <c>quitSuccess</c> sets 0 to avoid it); nothing is changed. Or the installed
    /// version's own uninstaller refused, a file of it held, and the include's <c>customUnInstallCheck</c> hooks set 2 and
    /// quit before a file is replaced, that version left as its uninstaller put it back.
    /// </summary>
    public const int StoppedBeforeInstalling = 2;

    /// <summary>
    /// The installer's exit 3: run silently, an update or not, it found Jason Desktop's own Jason.exe running at the install
    /// path, in its window or the tray, or could not tell because its look failed; it killed nothing, showed no box and
    /// changed nothing, the installed version staying as it was. The move ends pending on it.
    /// </summary>
    public const int ApplicationRunning = 3;
}

/// <summary>
/// What the move asks of the operating system: the machine's architecture, a signature, an unzip, a setup run to its end,
/// a window. A seam, so a test asks none of it of the machine running the suite.
/// </summary>
public interface IMoveSystem
{
    /// <summary>macOS: whether the machine is Apple silicon, by <c>sysctl hw.optional.arm64</c>, never <c>uname</c>, which answers for the shell under Rosetta.</summary>
    bool IsAppleSilicon();

    /// <summary>
    /// Windows: who signed <paramref name="file"/>, where its Authenticode signature verifies (WinVerifyTrust: valid, and
    /// chained to a root this machine trusts): the signer certificate's simple name, and no refusal; else no signer, and why
    /// not in a sentence. The one routine every signature the move reads goes through, this jason's own included.
    /// </summary>
    (string? Signer, string? Refusal) WindowsSigner(string file);

    /// <summary>The file this process runs from: the jason that asks for the move, whose own signer the move holds Jason Desktop to.</summary>
    string RunningExecutable { get; }

    /// <summary>
    /// macOS: null where the application at <paramref name="application"/> verifies, deep and strict, against
    /// <paramref name="requirement"/>; else what <c>codesign</c> said.
    /// </summary>
    Task<string?> MacSignatureRefusalAsync(string application, string requirement, CancellationToken cancellationToken);

    /// <summary>macOS: <paramref name="zip"/> unpacked into <paramref name="folder"/> with <c>ditto -x -k</c>; null where it was, else why not.</summary>
    Task<string?> UnzipAsync(string zip, string folder, CancellationToken cancellationToken);

    /// <summary>
    /// Windows: the setup run silently into <paramref name="place"/> by the raw command line <see cref="MoveSystem.SetupCommandLine"/>
    /// makes, in the environment <see cref="MoveSystem.SetupEnvironment"/> makes with <paramref name="marker"/>, and waited for up
    /// to <paramref name="bound"/>. A setup that has not ended by then is left running and never killed.
    /// </summary>
    Task<SetupRun> RunSetupAsync(string setup, string place, string marker, TimeSpan bound, CancellationToken cancellationToken);

    /// <summary>Whether a program runs from <paramref name="executable"/>, found by its exact path: an application's open window.</summary>
    bool Runs(string executable);

    /// <summary>macOS: the folder every account's applications share, where an application is looked for after the account's own.</summary>
    string SharedApplications { get; }

    /// <summary>Whether this account may write into <paramref name="folder"/>: a new application goes into the shared folder only then.</summary>
    bool MayWriteInto(string folder);

    /// <summary>Linux: whether apt installs packages here, by the install line's rule: <c>apt-get</c> and <c>dpkg</c> both on the PATH.</summary>
    bool HasApt();

    /// <summary>
    /// Linux: whether a program of the file name <paramref name="name"/> runs under this account, from any folder: Jason
    /// Desktop runs from the deb's folder or from an AppImage's mount, whose folder changes at each start.
    /// </summary>
    bool RunsNamed(string name);

    /// <summary>
    /// Linux: whether this system lets an application make the user namespaces Chromium's sandbox needs, by
    /// <c>unshare -Ur true</c>; true where there is no <c>unshare</c> to ask, as the install line takes it.
    /// </summary>
    bool UserNamespacesAllowed();

    /// <summary>Linux: whether an AppImage can be mounted here: a setuid <c>fusermount3</c> or <c>fusermount</c> on the PATH.</summary>
    bool FuseMounts();

    /// <summary>
    /// Linux: what an AppImage carries under <paramref name="pattern"/>, read out by the file's own runtime into
    /// <c>squashfs-root</c> in <paramref name="folder"/> (<see cref="AppImageCopy.ExtractAsync"/>); true where it was.
    /// </summary>
    Task<bool> ExtractAppImageAsync(string appImage, string pattern, string folder, CancellationToken cancellationToken);

    /// <summary>
    /// Linux: <paramref name="package"/> installed by <c>apt-get install</c>, with the one password a system package needs,
    /// asked as the install line asks it: by <c>sudo</c> where this process has a terminal, else by the desktop's own dialog
    /// through <c>pkexec</c>, waited for up to <paramref name="bound"/>. <paramref name="progress"/> is told which, before
    /// either asks.
    /// </summary>
    Task<PackageRun> InstallPackageAsync(string package, TimeSpan bound, Action<string> progress, CancellationToken cancellationToken);
}

/// <summary>How apt's install or purge of a package ended.</summary>
/// <param name="Asked">Whether a password could be asked at all: false where there was neither a terminal for sudo nor pkexec.</param>
/// <param name="ExitCode">Its exit code; <see cref="TimedOut"/> where it had not ended within its bound.</param>
/// <param name="Said">What sudo, pkexec and apt said on standard error, which tells their refusals apart.</param>
public sealed record PackageRun(bool Asked, int ExitCode, string Said)
{
    /// <summary>The exit a run that had not ended within its bound is given, as <c>timeout</c> gives it.</summary>
    public const int TimedOut = 124;

    /// <summary>
    /// Why a run that did not succeed did not, read from the words of sudo, pkexec and apt as the install line reads them,
    /// never from an exit code alone.
    /// </summary>
    public AptRefusal Refusal
    {
        get
        {
            bool Says(params string[] words) => words.Any(word => Said.Contains(word, StringComparison.Ordinal));
            // The lock first: apt waited on it and timed out with the password given.
            if (Says("Could not get lock", "Unable to acquire the dpkg frontend lock"))
            {
                return AptRefusal.LockHeld;
            }

            // sudo's and sudo-rs's words (Ubuntu 26.04 ships sudo-rs) alike.
            if (ExitCode == TimedOut || Says("Request dismissed", "Not authorized", "incorrect password attempt", "incorrect authentication attempt", "Authentication failed", "a password is required", "is not in the sudoers file", "may not run sudo", "afraid I can't do that"))
            {
                return AptRefusal.NoPassword;
            }

            // No agent in this session, or no polkit daemon to reach at all.
            if (!Asked || Says("No authentication agent found", "Error getting authority"))
            {
                return AptRefusal.NoSession;
            }

            if (Says("Message recipient disconnected", "No session for cookie"))
            {
                return AptRefusal.DialogFailed;
            }

            return AptRefusal.Apt;
        }
    }

    /// <summary>apt's own last line, where it said one.</summary>
    public string? LastLine => Said.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
}

/// <summary>Why apt's run with the one password did not succeed (<see cref="PackageRun.Refusal"/>).</summary>
public enum AptRefusal
{
    /// <summary>No password was given: the dialog dismissed, a wrong one, an account that is not an administrator, or the bound passed.</summary>
    NoPassword,

    /// <summary>This session cannot ask for one: no terminal for sudo, and no pkexec or no agent to show its dialog.</summary>
    NoSession,

    /// <summary>The system's password dialog failed.</summary>
    DialogFailed,

    /// <summary>Another installation held the package system for the whole of apt's wait.</summary>
    LockHeld,

    /// <summary>apt itself failed.</summary>
    Apt,
}

/// <summary>
/// What the move holds Jason Desktop to before anything of it runs. On Windows, the publisher that signed the jason asking for
/// the move: its own signature verified by the routine that verifies the installer, and its signer's name compared with the
/// installer's and a found application's jason's, in full and in the same case. The name is read, never written here, so a
/// fork that signs its own builds is held to its own signer. On macOS, the requirement <c>install/install.sh</c> holds an
/// application to, a second copy of that line, held equal to it by a guard.
/// </summary>
/// <remarks>
/// A test build (built with <c>-p:JasonTestBuild=true</c>, which defines <c>JASON_TEST_BUILD</c> in <c>Jason.Contracts</c> alone,
/// read here through <see cref="Jason.Contracts.JasonVersion.TestBuild"/> and nothing else, so a build that relaxes these
/// checks is always one that says it is a test build) takes the signer <c>JASON_TEST_BUILD_SIGNER</c> names before its own and another requirement from <c>JASON_TEST_BUILD_REQUIREMENT</c>, so a
/// proof on a runner can move an install into an application signed by that runner's own certificate. No released build
/// is a test build: a guard holds the property out of the release workflow and the package action.
/// </remarks>
public static class MoveSignatures
{
    /// <summary>
    /// The requirement every application a release publishes for macOS satisfies: a chain to Apple's own root, an
    /// intermediate that is Apple's Developer ID certification authority, a leaf that is a Developer ID Application
    /// certificate, and that leaf's team.
    /// </summary>
    public const string Requirement = "anchor apple generic and certificate 1[field.1.2.840.113635.100.6.2.6] and certificate leaf[field.1.2.840.113635.100.6.1.13] and certificate leaf[subject.OU] = \"BD8M7U5YBV\"";

    /// <summary>
    /// The signer a test build is pointed at, which comes before the running jason's own: the one <c>JASON_TEST_BUILD_SIGNER</c>
    /// names in <paramref name="variables"/> (the process's own where null), and only in a test build, which this reads from
    /// <see cref="Jason.Contracts.JasonVersion.TestBuild"/> itself: no caller can say it is one. Null in every other build.
    /// </summary>
    public static string? TestSigner(IReadOnlyDictionary<string, string>? variables) =>
        Jason.Contracts.JasonVersion.TestBuild && Variable(variables, "JASON_TEST_BUILD_SIGNER") is { Length: > 0 } named ? named : null;

    /// <summary>
    /// The requirement the move holds an application to on macOS: in a test build, read from
    /// <see cref="Jason.Contracts.JasonVersion.TestBuild"/> itself, the one <c>JASON_TEST_BUILD_REQUIREMENT</c> names in
    /// <paramref name="variables"/> (the process's own where null); <see cref="Requirement"/> in every other build.
    /// </summary>
    public static string RequirementInForce(IReadOnlyDictionary<string, string>? variables) =>
        Jason.Contracts.JasonVersion.TestBuild && Variable(variables, "JASON_TEST_BUILD_REQUIREMENT") is { Length: > 0 } named ? named : Requirement;

    private static string? Variable(IReadOnlyDictionary<string, string>? variables, string name) =>
        variables is null ? Environment.GetEnvironmentVariable(name) : variables.GetValueOrDefault(name);

    /// <summary>
    /// Null where <paramref name="file"/>'s signature verifies and its signer's simple name is <paramref name="publisher"/> in
    /// full and in the same case; else why not, in a sentence.
    /// </summary>
    public static string? Refusal(IMoveSystem system, string file, string publisher)
    {
        ArgumentNullException.ThrowIfNull(system);
        var (signer, refusal) = system.WindowsSigner(file);
        return refusal ?? (string.Equals(signer, publisher, StringComparison.Ordinal) ? null : $"it is signed by '{signer}', not by {publisher}");
    }
}

/// <summary>This machine's: the programs the move runs, each started by <see cref="ChildProcess"/>.</summary>
public sealed class MoveSystem : IMoveSystem
{
    public static MoveSystem Instance { get; } = new();

    private static readonly TimeSpan ToolWait = TimeSpan.FromMinutes(5);

    public bool IsAppleSilicon()
    {
        var (exit, output, _) = RunTool(Sysctl, ["-n", "hw.optional.arm64"], TimeSpan.FromSeconds(10), CancellationToken.None).GetAwaiter().GetResult();
        return exit == 0 && output.Trim() == "1";
    }

    public string RunningExecutable => Environment.ProcessPath ?? throw new InvalidOperationException("This process names no file it runs from.");

    public (string? Signer, string? Refusal) WindowsSigner(string file)
    {
        if (!OperatingSystem.IsWindows())
        {
            return (null, "an Authenticode signature is read on Windows only");
        }

        var trust = Trust.Verify(file);
        if (trust != 0)
        {
            return (null, $"it does not carry a valid signature (WinVerifyTrust answered 0x{trust:X8})");
        }

        string? name;
        try
        {
#pragma warning disable SYSLIB0057 // The signer of a signed file: X509CertificateLoader reads no Authenticode signature.
            using var signer = new X509Certificate2(X509Certificate.CreateFromSignedFile(file));
#pragma warning restore SYSLIB0057
            name = signer.GetNameInfo(X509NameType.SimpleName, false);
        }
        catch (CryptographicException unread)
        {
            return (null, $"its signer could not be read ({unread.Message})");
        }

        return string.IsNullOrEmpty(name) ? (null, "its signer names no one") : (name, null);
    }

    public async Task<string?> MacSignatureRefusalAsync(string application, string requirement, CancellationToken cancellationToken)
    {
        var (exit, _, error) = await RunTool(Codesign, ["--verify", "--deep", "--strict", "-R=" + requirement, application], ToolWait, cancellationToken).ConfigureAwait(false);
        return exit == 0 ? null : (error.Trim() is { Length: > 0 } said ? said : $"codesign exited with {exit}");
    }

    public async Task<string?> UnzipAsync(string zip, string folder, CancellationToken cancellationToken)
    {
        var (exit, _, error) = await RunTool(Ditto, ["-x", "-k", zip, folder], ToolWait, cancellationToken).ConfigureAwait(false);
        return exit == 0 ? null : (error.Trim() is { Length: > 0 } said ? said : $"ditto exited with {exit}");
    }

    /// <summary>
    /// The environment the setup is given, from this process's: nothing of the update's lock, whatever nonce this process was
    /// handed, and the move's marker (<see cref="MoveMarker"/>), under which the setup's own <c>jason install</c>, a grandchild
    /// through the installer, does nothing while the move's record stands not done.
    /// </summary>
    internal static void SetupEnvironment(IDictionary<string, string?> environment, string marker)
    {
        ArgumentNullException.ThrowIfNull(environment);
        environment.Remove(UpdateLock.Variable);
        environment[MoveMarker.Variable] = marker;
    }

    /// <summary>
    /// The setup's command line, raw: <c>/S /D=&lt;place&gt;</c>, the place last and never quoted. NSIS takes <c>/D=</c> only as the
    /// last parameter and only unquoted, spaces included (NSIS's documentation, "Installer Usage", 3.2.1), as install.ps1 passes it;
    /// an argument list would quote a place with a space in it, and the setup would install into its default instead.
    /// </summary>
    internal static string SetupCommandLine(string place) => "/S /D=" + place;

    public async Task<SetupRun> RunSetupAsync(string setup, string place, string marker, TimeSpan bound, CancellationToken cancellationToken)
    {
        var start = ChildProcess.StartInfo(setup, ChildKind.SystemToolWithInput);
        start.Arguments = SetupCommandLine(place);
        SetupEnvironment(start.Environment, marker);
        Process.StandardStreams.KeepOutOfChildren();
        using var process = ChildProcess.Start(start) ?? throw new IOException($"{setup} could not be started.");
        process.StandardInput.Close();
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(bound);
        try
        {
            await process.WaitForExitAsync(bounded.Token).ConfigureAwait(false);
            return new SetupRun(true, process.ExitCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Never killed: a setup killed part-way leaves an application half installed. It goes on by itself.
            return new SetupRun(false, 0);
        }
    }

    public string SharedApplications => "/Applications";

    public bool MayWriteInto(string folder)
    {
        var probe = Path.Combine(folder, $".jason-move-probe.{Guid.NewGuid():N}");
        try
        {
            using (File.Create(probe, 1, FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public bool Runs(string executable)
    {
        var name = Path.GetFileNameWithoutExtension(executable);
        foreach (var process in System.Diagnostics.Process.GetProcessesByName(name))
        {
            using (process)
            {
                try
                {
                    if (string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch (Exception unread) when (unread is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // Another account's, or gone already: not this account's open window.
                }
            }
        }

        return false;
    }

    public bool HasApt() => Executables.OnPath("apt-get", null) is not null && Executables.OnPath("dpkg", null) is not null;

    public bool RunsNamed(string name)
    {
        foreach (var process in Process.ProcessImages.Named(name))
        {
            using (process)
            {
                try
                {
                    if (process.MainModule?.FileName is { } image && string.Equals(Path.GetFileName(Process.ProcessImages.AtItsPath(image)), name, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
                catch (Exception unread) when (unread is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // Another account's, or gone already.
                }
            }
        }

        return false;
    }

    public bool UserNamespacesAllowed()
    {
        if (Executables.OnPath("unshare", null) is not { } unshare)
        {
            return true;
        }

        var (exit, _, _) = RunTool(unshare, ["-Ur", Executables.OnPath("true", null) ?? "/bin/true"], TimeSpan.FromSeconds(10), CancellationToken.None).GetAwaiter().GetResult();
        return exit == 0;
    }

    public bool FuseMounts()
    {
        if (OperatingSystem.IsWindows())
        {
            return false;
        }

        foreach (var tool in new[] { "fusermount3", "fusermount" })
        {
            if (Executables.OnPath(tool, null) is { } found && File.GetUnixFileMode(found).HasFlag(UnixFileMode.SetUser))
            {
                return true;
            }
        }

        return false;
    }

    public Task<bool> ExtractAppImageAsync(string appImage, string pattern, string folder, CancellationToken cancellationToken) =>
        AppImageCopy.ExtractAsync(appImage, pattern, folder, cancellationToken);

    public Task<PackageRun> InstallPackageAsync(string package, TimeSpan bound, Action<string> progress, CancellationToken cancellationToken) =>
        AptAsync("installing Jason Desktop's package with apt", ["install", .. AptOptions(Environment.GetEnvironmentVariable("http_proxy"), Environment.GetEnvironmentVariable("https_proxy")), package], bound, progress, cancellationToken);

    /// <summary>
    /// Linux: <paramref name="package"/> purged by <c>apt-get purge</c>, its own scripts taking back what root placed, with
    /// the one password asked as an install asks it, and the package lock waited for as an install waits for it.
    /// </summary>
    internal static Task<PackageRun> PurgePackageAsync(string package, TimeSpan bound, Action<string> progress, CancellationToken cancellationToken) =>
        AptAsync("taking Jason Desktop's package off with apt", ["purge", "-y", "-o", "DPkg::Lock::Timeout=600", package], bound, progress, cancellationToken);

    /// <summary>
    /// apt-get run with the one password a system package needs: by <c>sudo</c> where this process has a terminal, else by
    /// the desktop's own dialog through <c>pkexec</c>, waited for up to <paramref name="bound"/>; not asked at all where
    /// there is neither. <paramref name="progress"/> is told which, after <paramref name="doing"/>, before either asks.
    /// </summary>
    private static async Task<PackageRun> AptAsync(string doing, IReadOnlyList<string> arguments, TimeSpan bound, Action<string> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var aptGet = Executables.OnPath("apt-get", null) ?? throw new IOException("apt-get is not on the PATH.");
        string asker;
        if (HasTerminal() && Executables.OnPath("sudo", null) is { } sudo)
        {
            progress($"{doing}; sudo asks for your password");
            asker = sudo;
        }
        else if (Executables.OnPath("pkexec", null) is { } pkexec)
        {
            progress($"{doing}; your desktop asks for your password in its own window");
            asker = pkexec;
        }
        else
        {
            return new PackageRun(false, 127, string.Empty);
        }

        var (exit, _, error) = await RunTool(asker, [aptGet, .. arguments], bound, cancellationToken).ConfigureAwait(false);
        return new PackageRun(true, exit == -1 ? PackageRun.TimedOut : exit, error);
    }

    /// <summary>
    /// The options every install of the package takes, the install line's own: the package lock waited for, a configuration
    /// file the person changed kept, and the person's proxy, which sudo and pkexec both drop.
    /// </summary>
    internal static IReadOnlyList<string> AptOptions(string? httpProxy, string? httpsProxy) =>
    [
        "-y", "-o", "DPkg::Lock::Timeout=600", "-o", "Dpkg::Options::=--force-confdef", "-o", "Dpkg::Options::=--force-confold",
        .. string.IsNullOrEmpty(httpProxy) ? Array.Empty<string>() : ["-o", "Acquire::http::Proxy=" + httpProxy],
        .. string.IsNullOrEmpty(httpsProxy) ? Array.Empty<string>() : ["-o", "Acquire::https::Proxy=" + httpsProxy],
    ];

    /// <summary>Whether this process has a terminal sudo can ask in: whether <c>/dev/tty</c> opens, as the install line tests it.</summary>
    private static bool HasTerminal()
    {
        try
        {
            using var tty = File.OpenHandle("/dev/tty", FileMode.Open, FileAccess.Read);
            return true;
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The tools a move runs on macOS, each where macOS keeps it.</summary>
    internal const string Sysctl = "/usr/sbin/sysctl";

    internal const string Codesign = "/usr/bin/codesign";

    internal const string Ditto = "/usr/bin/ditto";

    private static async Task<(int Exit, string Output, string Error)> RunTool(string program, IReadOnlyList<string> arguments, TimeSpan bound, CancellationToken cancellationToken)
    {
        var start = ChildProcess.StartInfo(program, ChildKind.SystemToolWithInput);
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment.Remove(UpdateLock.Variable);
        using var process = ChildProcess.Start(start) ?? throw new IOException($"{program} could not be started.");
        process.StandardInput.Close();
        var output = new StringBuilder();
        var error = new StringBuilder();
        process.OutputDataReceived += (_, line) => { if (line.Data is not null) { lock (output) { output.AppendLine(line.Data); } } };
        process.ErrorDataReceived += (_, line) => { if (line.Data is not null) { lock (error) { error.AppendLine(line.Data); } } };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(bound);
        try
        {
            await process.WaitForExitAsync(bounded.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // A system tool that does not answer within its bound has no answer: it is ended, and said as one that failed.
            try
            {
                process.Kill(entireProcessTree: false);
            }
            catch (Exception gone) when (gone is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // It ended meanwhile.
            }

            return (-1, string.Empty, $"{program} did not answer within {bound.TotalSeconds:F0} seconds");
        }

        lock (output)
        {
            lock (error)
            {
                return (process.ExitCode, output.ToString(), error.ToString());
            }
        }
    }

    /// <summary>WinVerifyTrust, asked whether a file's Authenticode signature is valid, with no window and no revocation fetch.</summary>
    private static class Trust
    {
        private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        public static int Verify(string file)
        {
            var fileInfo = Marshal.AllocHGlobal(Marshal.SizeOf<TrustFile>());
            var data = Marshal.AllocHGlobal(Marshal.SizeOf<TrustData>());
            var path = Marshal.StringToCoTaskMemUni(file);
            try
            {
                Marshal.StructureToPtr(new TrustFile { Size = (uint)Marshal.SizeOf<TrustFile>(), FilePath = path }, fileInfo, false);
                var trust = new TrustData
                {
                    Size = (uint)Marshal.SizeOf<TrustData>(),
                    UiChoice = 2, // WTD_UI_NONE
                    RevocationChecks = 0, // WTD_REVOKE_NONE
                    UnionChoice = 1, // WTD_CHOICE_FILE
                    File = fileInfo,
                    StateAction = 1, // WTD_STATEACTION_VERIFY
                    ProviderFlags = 0x10, // WTD_REVOCATION_CHECK_NONE
                };
                Marshal.StructureToPtr(trust, data, false);
                var action = GenericVerifyV2;
                var result = WinVerifyTrust(IntPtr.Zero, ref action, data);

                // The state the verify opened is closed, whatever it answered.
                var closing = Marshal.PtrToStructure<TrustData>(data);
                closing.StateAction = 2; // WTD_STATEACTION_CLOSE
                Marshal.StructureToPtr(closing, data, true);
                _ = WinVerifyTrust(IntPtr.Zero, ref action, data);
                return result;
            }
            finally
            {
                Marshal.FreeCoTaskMem(path);
                Marshal.FreeHGlobal(data);
                Marshal.FreeHGlobal(fileInfo);
            }
        }

        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false)]
        private static extern int WinVerifyTrust(IntPtr window, ref Guid action, IntPtr data);

        [StructLayout(LayoutKind.Sequential)]
        private struct TrustFile
        {
            public uint Size;
            public IntPtr FilePath;
            public IntPtr File;
            public IntPtr KnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TrustData
        {
            public uint Size;
            public IntPtr PolicyCallbackData;
            public IntPtr SipClientData;
            public uint UiChoice;
            public uint RevocationChecks;
            public uint UnionChoice;
            public IntPtr File;
            public uint StateAction;
            public IntPtr StateData;
            public IntPtr UrlReference;
            public uint ProviderFlags;
            public uint UiContext;
            public IntPtr SignatureSettings;
        }
    }
}
