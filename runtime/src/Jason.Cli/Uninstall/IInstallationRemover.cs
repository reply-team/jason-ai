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
/// <c>remover_unsupported</c>, refused before anything at all is removed. That is the wrong answer to get by
/// accident and a harmless one to get.
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
    /// Removes a directory and everything under it. Only ever the data directory, and only on the explicit
    /// word — which is why it is a separate member rather than a flag on the one above.
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
}

/// <summary>What became of the executable, which is the one step that can honestly half-succeed.</summary>
/// <param name="Removed">Whether the file is gone.</param>
/// <param name="MovedTo">
/// Where it went instead, when it could not be deleted because it is the image of the running process. The
/// installation is off the machine either way — the install directory is empty and goes — but one copy of the
/// binary is somewhere else, and this verb names it rather than calling that a clean uninstall.
/// </param>
/// <param name="Note">What happened, in words, where that is not simply "it is gone".</param>
public sealed record ExecutableOutcome(bool Removed, string? MovedTo, string? Note);

/// <summary>The removers this build knows how to make.</summary>
public static class InstallationRemovers
{
    /// <summary>
    /// What an environment that named none gets: every member refuses, so a forgotten seam cannot no-op its
    /// way to a success report. Silence and success is the worst answer to a destructive verb.
    /// </summary>
    public static IInstallationRemover Unsupported { get; } = new RefusingRemover();

    /// <summary>
    /// This machine's. The file operations are what they look like; taking a directory off this account's
    /// PATH is the installer's own act read backwards and arrives with the rules that describe it.
    /// </summary>
    /// <param name="pathKey">
    /// The key under <c>HKEY_CURRENT_USER</c> whose <c>Path</c> this reads and edits on Windows. Always
    /// <c>Environment</c>, except in a test that points it at a key of its own.
    /// </param>
    public static IInstallationRemover ForThisMachine(string pathKey = UserPathValue.EnvironmentKey) => new MachineRemover(pathKey);

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
        /// Deleted where that is allowed; moved aside where it is not.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Windows will not delete the image of a running process, and this verb is ordinarily run from the
        /// very file it is removing. It <em>will</em> rename one, though: the file object stays open under its
        /// new name, so moving the executable into the system temporary directory empties the install
        /// directory, which then goes, and leaves one copy of the binary somewhere the operating system
        /// clears — named in the report, with the line that removes it now.
        /// </para>
        /// <para>
        /// Two alternatives were considered. Re-executing from a copy of itself, the way the update applier
        /// does, solves <em>replacing</em> the file rather than deleting it: the copy would still have to
        /// outlive this process to delete its image, so it is a new detached-process surface whose own copy
        /// leaks in exactly the same place this one does. And marking the file for deletion at the next
        /// reboot needs an administrator, which nothing in this product does.
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

            var aside = Path.Combine(Path.GetTempPath(), $"jason-uninstall-{Guid.NewGuid():N}");
            Directory.CreateDirectory(aside);
            var moved = Path.Combine(aside, Path.GetFileName(path));
            File.Move(path, moved);

            return new ExecutableOutcome(
                false,
                moved,
                "This is the file the uninstall is running from, and Windows does not delete the image of a "
                + "running process. It was moved out of the install directory instead, so the installation is "
                + $"off this machine; one copy of it is at '{moved}' until the system clears its temporary "
                + "files, and `del` on that path removes it now.");
        }

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
                var value = new UserPathValue(pathKey).Read()?.Raw ?? string.Empty;

                // No marker here, and none is wanted: install.ps1 puts the directory on this account's own
                // Path value, "where a directory is its own mark". So being there is what makes it ours.
                return new PathEntryPlan(directory, [], value, PathEntry.Carries(value, directory));
            }

            var line = PathEntry.ExportLine(directory);
            var profiles = PathEntry
                .ProfileFiles(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetEnvironmentVariable("SHELL"))
                .Where(profile => Carries(profile, line))
                .ToList();

            return new PathEntryPlan(directory, profiles, null, profiles.Count > 0);
        }

        public PathEntryOutcome RemovePathEntry(PathEntryPlan plan)
        {
            ArgumentNullException.ThrowIfNull(plan);

            if (!plan.Ours)
            {
                return new PathEntryOutcome(false, [], "This installer did not put that directory on the PATH, so it was left there.");
            }

            if (OperatingSystem.IsWindows())
            {
                // Read again rather than taken from the plan, and written back with the kind it had: the plan
                // is what was shown, this is what is edited, and an account's REG_EXPAND_SZ Path stays one.
                var userPath = new UserPathValue(pathKey);
                var stored = userPath.Read();
                var value = stored?.Raw ?? string.Empty;
                var without = PathEntry.WithoutDirectory(value, plan.Directory);
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
                var text = File.ReadAllText(profile);
                var without = PathEntry.WithoutEntry(text, plan.Directory);

                // The same instance back means the line is not in there, and the file is not opened for
                // writing at all. Rewriting a login profile identically is still rewriting it.
                if (ReferenceEquals(without, text))
                {
                    continue;
                }

                File.WriteAllText(profile, without);
                touched.Add(profile);
            }

            return new PathEntryOutcome(touched.Count > 0, touched, touched.Count > 0 ? null : "That line is not in any login profile.");
        }

        private static bool Carries(string profile, string line)
        {
            try
            {
                return File.Exists(profile) && File.ReadAllText(profile).Contains(line, StringComparison.Ordinal);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    private sealed class RefusingRemover : IInstallationRemover
    {
        public bool CanRemoveRunningImage => false;

        public void RemoveFile(string path) => throw Refusal();

        public ExecutableOutcome RemoveExecutable(string path) => throw Refusal();

        public bool RemoveDirectoryIfEmpty(string path) => throw Refusal();

        public void RemoveTree(string path) => throw Refusal();

        public PathEntryPlan ReadPathEntry(string directory) => throw Refusal();

        public PathEntryOutcome RemovePathEntry(PathEntryPlan plan) => throw Refusal();

        private static RemovalRefused Refusal() => new(
            CliErrors.RemoverUnsupported,
            "This environment does not remove anything from this machine, so nothing was removed. "
            + "That is deliberate: a removal is composed here and performed by one seam, and an environment "
            + "that named none gets a refusal rather than the machine.");
    }
}
