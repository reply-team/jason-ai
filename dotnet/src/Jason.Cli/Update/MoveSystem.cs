
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
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

    /// <summary>Another install of Jason ran at the same time.</summary>
    public const int AnotherInstall = 2;

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
    /// Windows: null where <paramref name="file"/> carries a valid Authenticode signature whose signer's simple name is
    /// <paramref name="publisher"/> in full and in the same case; else why not, in a sentence.
    /// </summary>
    string? WindowsSignatureRefusal(string file, string publisher);

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
}

/// <summary>
/// The publisher and the requirement the move holds Jason Desktop to before anything of it runs: the same two the install
/// scripts hold it to, each a second copy of a line in another language, so a guard holds them equal to
/// <c>install/install.ps1</c>'s signer and <c>install/install.sh</c>'s requirement.
/// </summary>
/// <remarks>
/// A test build of the command line (built with <c>-p:JasonTestBuild=true</c>, which defines <c>JASON_TEST_BUILD</c>) reads
/// another signer from <c>JASON_TEST_BUILD_SIGNER</c> and another requirement from <c>JASON_TEST_BUILD_REQUIREMENT</c>, so a
/// proof on a runner can move an install into an application signed by that runner's own certificate. No released build
/// is a test build: a guard holds the property out of the release workflow and the package action.
/// </remarks>
public static class MoveSignatures
{
    /// <summary>The simple name of the certificate that signs every Windows installer a release publishes.</summary>
    public const string Publisher = "ReplyApp Inc.";

    /// <summary>
    /// The requirement every application a release publishes for macOS satisfies: a chain to Apple's own root, an
    /// intermediate that is Apple's Developer ID certification authority, a leaf that is a Developer ID Application
    /// certificate, and that leaf's team.
    /// </summary>
    public const string Requirement = "anchor apple generic and certificate 1[field.1.2.840.113635.100.6.2.6] and certificate leaf[field.1.2.840.113635.100.6.1.13] and certificate leaf[subject.OU] = \"BD8M7U5YBV\"";

#if JASON_TEST_BUILD
    /// <summary>Whether this build is a test build, whose checks may be pointed at another signer.</summary>
    public static bool TestBuild => true;

    /// <summary>The publisher the move holds an installer to: in a test build, the one <c>JASON_TEST_BUILD_SIGNER</c> names.</summary>
    public static string PublisherInForce =>
        Environment.GetEnvironmentVariable("JASON_TEST_BUILD_SIGNER") is { Length: > 0 } named ? named : Publisher;

    /// <summary>The requirement the move holds an application to: in a test build, the one <c>JASON_TEST_BUILD_REQUIREMENT</c> names.</summary>
    public static string RequirementInForce =>
        Environment.GetEnvironmentVariable("JASON_TEST_BUILD_REQUIREMENT") is { Length: > 0 } named ? named : Requirement;
#else
    /// <summary>Whether this build is a test build: never a released one.</summary>
    public static bool TestBuild => false;

    /// <summary>The publisher the move holds an installer to.</summary>
    public static string PublisherInForce => Publisher;

    /// <summary>The requirement the move holds an application to.</summary>
    public static string RequirementInForce => Requirement;
#endif
}

/// <summary>This machine's: the programs the move runs, each started by <see cref="ChildProcess"/>.</summary>
public sealed class MoveSystem : IMoveSystem
{
    public static MoveSystem Instance { get; } = new();

    private static readonly TimeSpan ToolWait = TimeSpan.FromMinutes(5);

    public bool IsAppleSilicon()
    {
        var (exit, output, _) = RunTool("/usr/sbin/sysctl", ["-n", "hw.optional.arm64"], TimeSpan.FromSeconds(10), CancellationToken.None).GetAwaiter().GetResult();
        return exit == 0 && output.Trim() == "1";
    }

    public string? WindowsSignatureRefusal(string file, string publisher)
    {
        if (!OperatingSystem.IsWindows())
        {
            return "an Authenticode signature is read on Windows only";
        }

        var trust = Trust.Verify(file);
        if (trust != 0)
        {
            return $"it does not carry a valid signature (WinVerifyTrust answered 0x{trust:X8})";
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
            return $"its signer could not be read ({unread.Message})";
        }

        return string.Equals(name, publisher, StringComparison.Ordinal) ? null : $"it is signed by '{name}', not by {publisher}";
    }

    public async Task<string?> MacSignatureRefusalAsync(string application, string requirement, CancellationToken cancellationToken)
    {
        var (exit, _, error) = await RunTool("/usr/bin/codesign", ["--verify", "--deep", "--strict", "-R=" + requirement, application], ToolWait, cancellationToken).ConfigureAwait(false);
        return exit == 0 ? null : (error.Trim() is { Length: > 0 } said ? said : $"codesign exited with {exit}");
    }

    public async Task<string?> UnzipAsync(string zip, string folder, CancellationToken cancellationToken)
    {
        var (exit, _, error) = await RunTool("/usr/bin/ditto", ["-x", "-k", zip, folder], ToolWait, cancellationToken).ConfigureAwait(false);
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
