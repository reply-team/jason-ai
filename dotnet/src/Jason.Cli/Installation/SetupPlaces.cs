using Jason.Cli.Uninstall;

namespace Jason.Cli.Installation;

/// <summary>The two systems <c>jason install</c> sets up.</summary>
public enum SetupSystem
{
    MacOs,
    Windows,
}

/// <summary>
/// The places on this machine <c>jason install</c> writes into: the account's home, its local application data on
/// Windows, the registry key whose <c>Path</c> it edits, and the account's shell.
/// </summary>
/// <remarks>
/// <para>
/// <b>A seam whose default is the refusal</b>, for the reason <see cref="CliEnvironment.Removes"/> gives: the profiles,
/// the <c>~/.local/bin</c> and the Path this edits belong to whoever runs the suite. So
/// <see cref="CliEnvironment.Default()"/> names this machine's and nothing else does, and a null is
/// <c>install_unsupported</c>, refused before anything is read or written.
/// </para>
/// <para>
/// Null on Linux as well, where the terminal command and the skills are <c>install.sh</c>'s and
/// <c>jason skills install</c>'s, and nothing here sets anything up.
/// </para>
/// </remarks>
/// <param name="Platform">Which system's places these are.</param>
/// <param name="Home">The account's home directory.</param>
/// <param name="LocalApplicationData">
/// <c>%LOCALAPPDATA%</c> on Windows, where an older command-line install put <c>Programs\jason</c> and the application its
/// <c>Microsoft\WindowsApps</c> files; null on macOS.
/// </param>
/// <param name="PathKey">
/// The key under <c>HKEY_CURRENT_USER</c> whose <c>Path</c> this edits on Windows. Always <c>Environment</c>, except in a
/// test that points it at a key of its own.
/// </param>
/// <param name="Shell">The account's shell as <c>SHELL</c> names it, which decides the profiles on macOS.</param>
public sealed record SetupPlaces(SetupSystem Platform, string Home, string? LocalApplicationData, string PathKey, string? Shell)
{
    /// <summary>
    /// How a file is deleted: <see cref="File.Delete"/>, except in a test that stands for an executable a running process
    /// holds, which Windows renames and does not delete.
    /// </summary>
    internal Action<string> Delete { get; init; } = File.Delete;

    /// <summary>
    /// The key under <c>HKEY_CURRENT_USER</c> whose values start programs at sign-in, where Electron writes "Open Jason at
    /// Login" on Windows; a key of a test's own in a test.
    /// </summary>
    internal string RunKey { get; init; } = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Where Electron writes each <see cref="RunKey"/> value's state, under the same name.</summary>
    internal string StartupApprovedKey { get; init; } = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    /// <summary>The key under <c>HKEY_CURRENT_USER</c> link schemes are registered under: <c>Software\Classes</c>, except in a test.</summary>
    internal string ClassesKey { get; init; } = @"Software\Classes";

    /// <summary>The system's temporary directory, where a held executable is renamed aside when it is on that volume.</summary>
    internal string Temporary { get; init; } = Path.GetTempPath();

    /// <summary>
    /// Whether this process runs elevated: as an administrator with its elevation on Windows, as root on macOS. A carried
    /// update refuses there (<c>update_elevated</c>); read from the process for this machine's places, and set by a test.
    /// </summary>
    public bool Elevated { get; init; }

    /// <summary>This machine's, on macOS and Windows; null anywhere else.</summary>
    public static SetupPlaces? ForThisMachine()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
        {
            return new SetupPlaces(SetupSystem.Windows, home, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), UserPathValue.EnvironmentKey, null)
            {
                Elevated = Environment.IsPrivilegedProcess,
            };
        }

        return OperatingSystem.IsMacOS()
            ? new SetupPlaces(SetupSystem.MacOs, home, null, UserPathValue.EnvironmentKey, Environment.GetEnvironmentVariable("SHELL")) { Elevated = Environment.IsPrivilegedProcess }
            : null;
    }

    /// <summary>The folder the terminal command goes in on macOS, and the one <c>install.sh</c> installed into: <c>~/.local/bin</c>.</summary>
    public string LocalBin => Path.Combine(Home, ".local", "bin");

    /// <summary>Where an older command-line install put its executable by default.</summary>
    public string DefaultCommandLineExecutable =>
        Platform is SetupSystem.Windows
            ? Path.Combine(LocalApplicationData ?? Home, "Programs", "jason", "jason.exe")
            : Path.Combine(LocalBin, "jason");

    /// <summary>The folder the application put its Windows command files in, which every Windows user Path holds by default.</summary>
    public string WindowsApps => Path.Combine(LocalApplicationData ?? Home, "Microsoft", "WindowsApps");
}
