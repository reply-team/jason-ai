using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Jason.Cli.Process;

namespace Jason.Cli.Uninstall;

/// <summary>A removal this build will not perform, in words the operator can act on.</summary>
/// <remarks>
/// Named for the act rather than for the verb, because <see cref="CliErrors.UninstallRefused"/> is the code
/// an envelope carries and two things with one name in one file is how the next reader picks the wrong one.
/// </remarks>
public sealed class RemovalRefused(string code, string message) : Exception(message)
{
    /// <summary>The snake_case code the error envelope carries, so an agent branches on one shape.</summary>
    public string Code { get; } = code;
}

/// <summary>
/// The one place uninstall touches the machine: files go away here, the PATH entry is edited here, and nothing
/// else in this product knows how either is done.
/// </summary>
/// <remarks>
/// <para>
/// <b>Defaulted the fail-closed way, like the autostart registrar and the harness locator.</b> An HTTP
/// handler, a process table or an install path is safe to default to the real thing in a test, because a real
/// one points at a temporary directory or an unreachable host. This one is not. The file it removes is decided
/// by <see cref="CliEnvironment.InstallPath"/>, whose null means "ask the operating system" — under a test,
/// the test host — and the PATH it edits belongs to whoever ran the suite. Three guards in this repository
/// type documented command lines <em>for real</em>, and one of the pages they will read prints this verb.
/// </para>
/// <para>
/// So <see cref="CliEnvironment.Default"/> names this machine's remover and nothing else does: null is
/// <c>remover_unsupported</c>, refused by the verb before anything at all is read or removed. That is the
/// wrong answer to get by accident and a harmless one to get.
/// </para>
/// </remarks>
public interface IInstallationRemover
{
    /// <summary>
    /// Whether this platform can delete the image of the process that is running. Windows cannot, which is
    /// why the last step of an uninstall is the one that can honestly fail.
    /// </summary>
    bool CanRemoveRunningImage { get; }

    /// <summary>Removes one file. Removing what is not there is not an error.</summary>
    void RemoveFile(string path);

    /// <summary>
    /// Removes the installed executable — which may be the image of the process doing the removing.
    /// </summary>
    /// <remarks>
    /// Its own member rather than <see cref="RemoveFile"/>, because it is the one step whose answer is not
    /// simply yes or no, and because how it is done is a property of the platform and therefore belongs to
    /// the seam that owns the machine.
    /// </remarks>
    ExecutableOutcome RemoveExecutable(string path);

    /// <summary>
    /// Removes a directory only if nothing is left in it, and says whether it went. A directory holding
    /// something this installer did not write is somebody else's, and it stays.
    /// </summary>
    bool RemoveDirectoryIfEmpty(string path);

    /// <summary>
    /// Removes a directory and everything under it. Only ever one of the entries this product keeps in the data
    /// directory — never the data directory itself, which is whatever <c>JASON_DATA_DIR</c> names — and only on
    /// the explicit word, which is why it is a separate member rather than a flag on the one above.
    /// </summary>
    void RemoveTree(string path);

    /// <summary>
    /// How this account's PATH carries that directory, read from the machine.
    /// </summary>
    /// <remarks>
    /// Behind this seam rather than in the reader, for the reason the whole seam exists. On Windows the
    /// answer lives in this account's registry and on Unix in this account's login profiles, so a reader that
    /// worked it out for itself would make every plan depend on the machine the suite happens to run on.
    /// </remarks>
    PathEntryPlan ReadPathEntry(string directory);

    /// <summary>Takes the install directory off this account's PATH, in the way the installer put it on.</summary>
    PathEntryOutcome RemovePathEntry(PathEntryPlan plan);

    /// <summary>
    /// Where this build unpacked the native libraries it carries, when <paramref name="executable"/> is the
    /// image of this very process — or null, for any other file and for a build that unpacked nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A single-file build carries SQLite's native library inside itself and unpacks it, on first run, into a
    /// directory of its own — under the system's temporary directory on Windows, under the home directory's
    /// <c>.net</c> elsewhere: one per build, named for the bundle. The executable went and that directory stayed,
    /// on every uninstall, and neither place is ever cleared by anything else.
    /// </para>
    /// <para>
    /// Only the running process can name its own, because only the host that unpacked it knows which it was.
    /// So this answers for the running image and for nothing else, and a directory it cannot name for certain
    /// is left alone rather than guessed at — the same rule the receipts keep.
    /// </para>
    /// </remarks>
    string? ReadExtractedLibraries(string executable);

    /// <summary>Removes that directory and everything this build unpacked into it.</summary>
    void RemoveExtractedLibraries(string directory);
}

/// <summary>What became of the executable, which is the one step that can honestly half-succeed.</summary>
/// <param name="Removed">Whether the file is gone.</param>
/// <param name="MovedTo">
/// Where it went instead, when it could not be deleted because it is the image of the running process. The
/// installation is off the machine either way — the install directory is empty and goes — but one copy of the
/// binary is somewhere else, and this verb names it rather than calling that a clean uninstall.
/// </param>
/// <param name="Note">What happened, in words, where that is not simply "it is gone".</param>
/// <param name="LeftBehind">
/// True when a copy is left that nothing will remove — no cleanup could be started for it. That is something
/// this uninstall set out to remove and did not, so it is a problem and the verb exits 1, rather than a note
/// beside a success.
/// </param>
public sealed record ExecutableOutcome(bool Removed, string? MovedTo, string? Note, bool LeftBehind = false);

/// <summary>The removers this build knows how to make.</summary>
public static class InstallationRemovers
{
    /// <summary>
    /// This machine's. The file operations are what they look like; taking a directory off this account's
    /// PATH is the installer's own act read backwards and arrives with the rules that describe it.
    /// </summary>
    /// <param name="pathKey">
    /// The key under <c>HKEY_CURRENT_USER</c> whose <c>Path</c> this reads and edits on Windows. Always
    /// <c>Environment</c>, except in a test that points it at a key of its own.
    /// </param>
    public static IInstallationRemover ForThisMachine(string pathKey = UserPathValue.EnvironmentKey) => new MachineRemover(pathKey);

    /// <summary>
    /// Moves an executable out of its directory into <see cref="AsideDirectory"/>, having first started what
    /// removes the copy, and says what became of it.
    /// </summary>
    /// <param name="path">The executable: on Windows, the image of the running process.</param>
    /// <param name="temporaryDirectory">The system's temporary directory.</param>
    /// <param name="move">How a file is moved. <see cref="File.Move(string, string)"/>, except in a test.</param>
    /// <param name="startCleanup">
    /// What starts the process that removes the directory the copy is in once this one has exited, answering its
    /// id — or null where none could be started.
    /// </param>
    /// <remarks>
    /// <para>
    /// Public, and handed the move and the cleanup, because both of its failures are the machine's to produce and
    /// no test machine produces them: a move across volumes that copies a running image and leaves it in place,
    /// and a cleanup that cannot be started.
    /// </para>
    /// <para>
    /// <b>The cleanup is started before the move, never after.</b> A single-file build reads each assembly it has
    /// not yet loaded out of its own file, by the path it started from; once that path is gone, the first type
    /// from a new assembly fails to load, and starting a process can need assemblies nothing has loaded yet.
    /// </para>
    /// </remarks>
    public static ExecutableOutcome MoveAside(string path, string temporaryDirectory, Action<string, string> move, Func<string, int?> startCleanup)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(move);
        ArgumentNullException.ThrowIfNull(startCleanup);

        var aside = AsideDirectory(path, temporaryDirectory);
        Directory.CreateDirectory(aside);
        var moved = Path.Combine(aside, Path.GetFileName(path));

        var cleanup = startCleanup(aside);
        move(path, moved);

        if (File.Exists(path))
        {
            // A move that copied. Across volumes Windows copies and then deletes the source, and where the source
            // is the image of a running process the delete fails and the move still succeeds. The aside directory
            // is chosen on the executable's own volume so that this does not happen; a volume mounted into a
            // folder can still put two paths with one drive letter on two volumes, and then the executable is
            // still here -- which is a problem, not a move.
            try
            {
                File.Delete(moved);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The cleanup started above removes the whole directory once this process has gone.
            }

            throw new RemovalRefused(
                CliErrors.UninstallRefused,
                $"It is the file this uninstall is running from, and it could not be moved out of its directory: '{aside}' "
                + "is on another volume, where Windows copies a running image rather than moving it and leaves the "
                + $"original in place. Remove '{path}' once this has exited.");
        }

        const string Why = "This is the file the uninstall is running from, and Windows does not delete the image of a "
            + "running process. It was moved out of the install directory instead, so the installation is off "
            + "this machine";

        return cleanup is { } pid
            ? new ExecutableOutcome(
                false,
                moved,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{Why}, to '{moved}'; a cleanup, process {pid}, removes '{aside}' as soon as this process has "
                    + $"exited. If it is still there afterwards, `Remove-Item -Recurse '{aside}'` removes it."))
            : new ExecutableOutcome(
                false,
                moved,
                $"{Why}; but one copy of it is left at '{moved}', and no cleanup could be started to remove it. "
                    + $"`Remove-Item -Recurse '{aside}'` removes it once this has exited.",
                LeftBehind: true);
    }

    /// <summary>
    /// Where the running image is moved to on Windows: a directory of its own on <b>the executable's own
    /// volume</b> — the temporary directory where that is on it, and otherwise beside the install directory.
    /// </summary>
    /// <remarks>
    /// Across volumes Windows does not move a file, it copies it and deletes the source — and where the source
    /// cannot be deleted, which is exactly the image of a running process, the move <em>succeeds</em> and leaves
    /// the source where it was. An installation on another drive than the temporary directory was reported as
    /// "moved out", with its directory kept for holding "something this installer did not write": the
    /// executable itself.
    /// </remarks>
    public static string AsideDirectory(string executable, string temporaryDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(temporaryDirectory);

        var name = $"jason-uninstall-{Guid.NewGuid():N}";
        var image = Path.GetFullPath(executable);
        var volume = Path.GetPathRoot(image);
        if (string.Equals(volume, Path.GetPathRoot(Path.GetFullPath(temporaryDirectory)), StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(temporaryDirectory, name);
        }

        // Beside the install directory rather than in it, because the install directory is about to be removed
        // once it is empty; at the root of the volume where the executable sits at the root itself.
        var installed = Path.GetDirectoryName(image)!;
        return Path.Combine(Path.GetDirectoryName(installed) ?? installed, "." + name);
    }

    private sealed class MachineRemover(string pathKey) : IInstallationRemover
    {
        // A process cannot delete its own image on Windows. Everywhere else the directory entry goes and the
        // file lives on until the last handle closes, which is exactly what is wanted.
        public bool CanRemoveRunningImage => !OperatingSystem.IsWindows();

        public void RemoveFile(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            // File.Delete forgives a missing file and not a missing directory: the second throws
            // DirectoryNotFoundException, which made "removing what is not there is not an error" false of
            // the commonest case there is -- a root somebody had already cleared out by hand.
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        /// <summary>
        /// Deleted where that is allowed; moved aside where it is not, and the copy removed once this process
        /// has gone.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Windows will not delete the image of a running process, and this verb is ordinarily run from the
        /// very file it is removing. It <em>will</em> rename one, though: the file object stays open under its
        /// new name, so moving the executable into the system temporary directory empties the install
        /// directory, which then goes, before this verb returns — which is what anybody checking afterwards
        /// looks at.
        /// </para>
        /// <para>
        /// The copy used to be left there "until the system clears its temporary files", and a Windows left to
        /// itself never does: every uninstall left one executable of the whole product behind, named in the
        /// report and removed by nobody. So a cleanup is started that waits for this process to exit and then
        /// removes the directory the copy is in. It is Windows PowerShell, which every Windows this runs on
        /// carries and <c>install.ps1</c> already needs, started by its full path, holding none of this
        /// process's streams, and deleting nothing but the one directory this call created for the copy.
        /// </para>
        /// <para>
        /// <b>Started before the move, never after.</b> A single-file build reads each assembly it has not yet
        /// loaded out of its own file, by the path it started from; once that path is gone, the first type
        /// from a new assembly fails to load. Starting a process can need assemblies nothing has loaded yet,
        /// so it goes first. The verb's report is rendered once before this step for the same reason — a
        /// published build answered <c>--purge-data</c> with "the type initializer for JsonSerializer threw",
        /// after it had removed everything, because its report was the first JSON it wrote.
        /// </para>
        /// <para>
        /// Two alternatives were considered. Re-executing from a copy of itself, the way the update applier
        /// does, solves <em>replacing</em> the file rather than deleting it: the copy would still have to
        /// outlive this process to delete its image, so it needs exactly this cleanup as well, and a caller
        /// waiting for the verb's answer would get it from a process that had not yet done the work. And
        /// marking the file for deletion at the next reboot needs an administrator, which nothing in this
        /// product does.
        /// </para>
        /// </remarks>
        public ExecutableOutcome RemoveExecutable(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            if (!File.Exists(path))
            {
                return new ExecutableOutcome(true, null, "It was already gone.");
            }

            if (CanRemoveRunningImage || !IsRunningImage(path))
            {
                File.Delete(path);
                return new ExecutableOutcome(true, null, null);
            }

            return InstallationRemovers.MoveAside(path, Path.GetTempPath(), (from, to) => File.Move(from, to), StartCleanup);
        }

        /// <summary>
        /// Starts the process that removes the moved copy once this one has exited, and says which it is — or
        /// null, where it could not be started and the report has to say so instead.
        /// </summary>
        /// <remarks>
        /// It waits for this process by its id, bounded, and then tries the directory a few times: whatever was
        /// holding the file — this process, or something scanning a new executable — lets go within moments.
        /// Its three streams are its own, and this process's are kept out of it: a caller reading this verb's
        /// output to the end would otherwise wait on the cleanup as well.
        /// </remarks>
        private static int? StartCleanup(string directory)
        {
            var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            if (!File.Exists(powershell))
            {
                return null;
            }

            var target = "'" + directory.Replace("'", "''", StringComparison.Ordinal) + "'";
            var script = string.Create(
                CultureInfo.InvariantCulture,
                $"$ErrorActionPreference = 'SilentlyContinue'; Wait-Process -Id {Environment.ProcessId} -Timeout 600; "
                + $"for ($attempt = 0; $attempt -lt 40 -and (Test-Path -LiteralPath {target}); $attempt++) "
                + $"{{ Remove-Item -LiteralPath {target} -Recurse -Force; if (Test-Path -LiteralPath {target}) {{ Start-Sleep -Milliseconds 250 }} }}");

            var start = new ProcessStartInfo(powershell)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", script })
            {
                start.ArgumentList.Add(argument);
            }

            StandardStreams.KeepOutOfChildren();

            try
            {
                using var process = System.Diagnostics.Process.Start(start);
                if (process is null)
                {
                    return null;
                }

                process.StandardInput.Close();
                return process.Id;
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                return null;
            }
        }

        public string? ReadExtractedLibraries(string executable)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(executable);

            // The host passes the directories it resolved native libraries from, and for a build that unpacked
            // them the one it unpacked into is among them -- measured on a published build, where it is the
            // only entry. Nothing else in this process knows which directory that was.
            if (!IsRunningImage(executable) || AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") is not string searched)
            {
                return null;
            }

            var name = Path.GetFileNameWithoutExtension(executable);
            var installed = Trimmed(Path.GetDirectoryName(Path.GetFullPath(executable))!);

            foreach (var entry in searched.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var directory = Trimmed(Path.GetFullPath(entry));

                // The host names it <base>/<program>/<bundle id>, and holds nothing in it but files it unpacked.
                // Anything else it searches -- the directory the executable itself is in, above all -- is not
                // something it unpacked, and is not this verb's to remove.
                if (string.Equals(directory, installed, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(Path.GetFileName(Path.GetDirectoryName(directory)), name, StringComparison.OrdinalIgnoreCase)
                    || !Directory.Exists(directory)
                    || Directory.EnumerateDirectories(directory).Any())
                {
                    continue;
                }

                return directory;
            }

            return null;
        }

        public void RemoveExtractedLibraries(string directory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(directory);

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        private static string Trimmed(string path) => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        /// <summary>Whether that path is the image this process is running as.</summary>
        private static bool IsRunningImage(string path) =>
            Environment.ProcessPath is { } self
            && string.Equals(Path.GetFullPath(self), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);

        public bool RemoveDirectoryIfEmpty(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            if (!Directory.Exists(path) || Directory.EnumerateFileSystemEntries(path).Any())
            {
                return false;
            }

            // Never recursive. Something arriving between the check above and this call keeps the directory,
            // which is the answer this verb wants: a directory holding somebody else's file stays.
            Directory.Delete(path, recursive: false);
            return true;
        }

        public void RemoveTree(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }

        public PathEntryPlan ReadPathEntry(string directory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(directory);

            if (OperatingSystem.IsWindows())
            {
                // As the registry stores it, %VARIABLE%s and all. This is the value the plan prints and the
                // one the removal edits, so reading it expanded here is where a whole Path's entries turned
                // into fixed strings.
                var stored = new UserPathValue(pathKey).Read();
                var value = stored?.Raw ?? string.Empty;
                var carried = PathEntry.Carries(value, directory, PathEntry.ReadAs(stored?.Kind != Microsoft.Win32.RegistryValueKind.String));

                // No marker here: install.ps1 puts the directory on this account's own Path value, "where a
                // directory is its own mark". Being there used to be what made it ours, and it is not: the
                // installer writes nothing into a Path that already names the directory, and a build copied
                // "somewhere on your PATH" lands where everything else is on the Path through the same entry.
                // So the entry is ours only where the directory is Jason's own.
                return new PathEntryPlan(
                    directory,
                    [],
                    value,
                    carried && PathEntry.JasonsOwn(directory, windows: true),
                    carried ? "this account's Path value" : null);
            }

            return PathEntry.ReadProfiles(directory, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetEnvironmentVariable("SHELL"));
        }

        public PathEntryOutcome RemovePathEntry(PathEntryPlan plan)
        {
            ArgumentNullException.ThrowIfNull(plan);

            if (!plan.Ours)
            {
                return new PathEntryOutcome(false, [], PathEntry.WhyLeft(plan, OperatingSystem.IsWindows()));
            }

            if (OperatingSystem.IsWindows())
            {
                // Read again rather than taken from the plan, and written back with the kind it had: the plan
                // is what was shown, this is what is edited, and an account's REG_EXPAND_SZ Path stays one.
                var userPath = new UserPathValue(pathKey);
                var stored = userPath.Read();
                var value = stored?.Raw ?? string.Empty;
                var without = PathEntry.WithoutDirectory(value, plan.Directory, PathEntry.ReadAs(stored?.Kind != Microsoft.Win32.RegistryValueKind.String));
                if (stored is null || ReferenceEquals(without, value))
                {
                    return new PathEntryOutcome(false, [], "That directory is not on this account's PATH.");
                }

                userPath.Write(without, stored.Kind);
                return new PathEntryOutcome(true, ["the Path value for this account"], null);
            }

            var touched = new List<string>();
            foreach (var profile in plan.Profiles)
            {
                // Bytes in, bytes out: every byte of the file that is not the installer's block is written back
                // as it was, whatever encoding or line endings the file is in.
                var bytes = File.ReadAllBytes(profile);
                var without = PathEntry.WithoutEntry(bytes, plan.Directory);

                // The same instance back means the block is not in there, and the file is not opened for
                // writing at all. Rewriting a login profile identically is still rewriting it.
                if (ReferenceEquals(without, bytes))
                {
                    continue;
                }

                File.WriteAllBytes(profile, without);
                touched.Add(profile);
            }

            return new PathEntryOutcome(touched.Count > 0, touched, touched.Count > 0 ? null : "That block is not in any login profile.");
        }

    }
}
