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
    /// Removes a directory only if nothing is left in it, and says whether it went. A directory holding
    /// something this installer did not write is somebody else's, and it stays.
    /// </summary>
    bool RemoveDirectoryIfEmpty(string path);

    /// <summary>
    /// Removes a directory and everything under it. Only ever the data directory, and only on the explicit
    /// word — which is why it is a separate member rather than a flag on the one above.
    /// </summary>
    void RemoveTree(string path);

    /// <summary>Takes the install directory off this account's PATH, in the way the installer put it on.</summary>
    PathEntryOutcome RemovePathEntry(PathEntryPlan plan);
}

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
    public static IInstallationRemover ForThisMachine() => new MachineRemover();

    private sealed class MachineRemover : IInstallationRemover
    {
        // A process cannot delete its own image on Windows. Everywhere else the directory entry goes and the
        // file lives on until the last handle closes, which is exactly what is wanted.
        public bool CanRemoveRunningImage => !OperatingSystem.IsWindows();

        public void RemoveFile(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            File.Delete(path);
        }

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

        public PathEntryOutcome RemovePathEntry(PathEntryPlan plan) => throw new RemovalRefused(
            CliErrors.UninstallRefused,
            "This build does not yet know how to take a directory off this account's PATH, so it refused "
            + "rather than reporting a PATH entry removed that is still there.");
    }

    private sealed class RefusingRemover : IInstallationRemover
    {
        public bool CanRemoveRunningImage => false;

        public void RemoveFile(string path) => throw Refusal();

        public bool RemoveDirectoryIfEmpty(string path) => throw Refusal();

        public void RemoveTree(string path) => throw Refusal();

        public PathEntryOutcome RemovePathEntry(PathEntryPlan plan) => throw Refusal();

        private static RemovalRefused Refusal() => new(
            CliErrors.RemoverUnsupported,
            "This environment does not remove anything from this machine, so nothing was removed. "
            + "That is deliberate: a removal is composed here and performed by one seam, and an environment "
            + "that named none gets a refusal rather than the machine.");
    }
}
