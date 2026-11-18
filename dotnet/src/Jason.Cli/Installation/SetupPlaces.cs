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

    /// <summary>
    /// macOS: the account's own home as the account database names it (<c>getpwuid</c>), which <c>HOME</c> may differ from;
    /// null where it cannot be read and everywhere else. A test's own home in a test.
    /// </summary>
    internal Func<string?> AccountHome { get; init; } = AccountDatabase.Home;

    /// <summary>
    /// Windows: the machine's <c>Path</c> as the registry stores it, which a new terminal reads before the account's; null
    /// where there is none, and everywhere but Windows. Read only, and read from the registry for this machine's places alone: any
    /// other places have none unless a test gives one of its own, so no test reads the machine's.
    /// </summary>
    internal Func<StoredPath?> MachinePath { get; init; } = static () => null;

    /// <summary>
    /// Windows: the account's <c>Path</c> as a new terminal reads it and as <c>jason install</c> appends its entry and
    /// takes a retired one off; null is the one under <see cref="PathKey"/>. A value of a test's own in a test, so a row
    /// writes no registry key.
    /// </summary>
    internal IAccountPath? AccountPath { get; init; }

    /// <summary>The account's <c>Path</c> this run reads and writes: <see cref="AccountPath"/>, else the value under <see cref="PathKey"/>.</summary>
    internal IAccountPath AccountPathValue => AccountPath ?? new UserPathValue(PathKey);

    /// <summary>Windows: the extensions a shell tries, in order, as <c>PATHEXT</c> lists them; null reads this process's.</summary>
    internal string? PathExtensions { get; init; }

    /// <summary>The system's temporary directory, where a held executable is renamed aside when it is on that volume.</summary>
    internal string Temporary { get; init; } = Path.GetTempPath();

    /// <summary>
    /// Whether this process runs elevated (<see cref="Elevation"/>): on Windows a token elevated above the account's ordinary
    /// one, or another account than the session's; on macOS root. A carried update refuses there (<c>update_elevated</c>);
    /// read from the process for this machine's places, and set by a test.
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
                Elevated = Elevation.OfThisProcess(),
                MachinePath = MachineEnvironment.ReadPath,
            };
        }

        return OperatingSystem.IsMacOS()
            ? new SetupPlaces(SetupSystem.MacOs, home, null, UserPathValue.EnvironmentKey, Environment.GetEnvironmentVariable("SHELL")) { Elevated = Elevation.OfThisProcess() }
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
