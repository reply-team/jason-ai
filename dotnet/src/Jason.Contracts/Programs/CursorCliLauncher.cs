using System.Globalization;
using System.Text.RegularExpressions;

namespace Jason.Contracts.Programs;

/// <summary>
/// Where Cursor CLI's program is on Windows. The word on the search path is a <c>.cmd</c> that runs a PowerShell script,
/// which in turn runs the newest version's <c>node.exe</c> with its <c>index.js</c>. A script is never started through a
/// shell, so <see cref="ProgramLayout"/> answers what the launcher script itself would start, by the rule that script
/// uses. Read by both halves: the runtime, which launches the host for a role, and the CLI, which starts it for a
/// person in a terminal of their own. It reads the file system and starts nothing.
/// </summary>
/// <param name="windows">
/// Whether this machine is Windows, which decides whether a launcher script has a layout. The caller says, so a test
/// says it either way on every platform.
/// </param>
public sealed partial class CursorCliLauncher(bool windows)
{
    /// <summary>The two names the installer gives the launcher script, which is the host's own and nobody else's.</summary>
    private static readonly string[] LauncherNames = ["agent.cmd", "cursor-agent.cmd"];

    /// <summary>The script both launchers run, which chooses the version directory.</summary>
    private const string LauncherScript = "cursor-agent.ps1";

    /// <summary>
    /// Whether a file name is one of the two this host's installer gives its launcher script. A name only: a profile
    /// is written before it is known whether the file is there.
    /// </summary>
    public bool NamesItsLauncher(string fileName) =>
        windows && LauncherNames.Contains(fileName, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// What the launcher script starts, as a command: the interpreter, then the entry script. Where a <c>node.exe</c>
    /// and an <c>index.js</c> stand beside the launcher the script starts those, and otherwise the newest directory
    /// under <c>versions</c> whose name reads as a version, newest by its date taken as a number — so, within one year, a month written
    /// with one digit is older than one written with two — and by name when two share a day, which the script leaves
    /// unordered. A staged or previous directory is never one. Nothing is remembered between calls: a version installed
    /// since is the answer the next time. Null where this is not Windows, where the file is not one of the two
    /// launchers or its script is not beside it, or where the chosen directory lacks either file.
    /// </summary>
    public IReadOnlyList<string>? ProgramLayout(string shimFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shimFile);

        try
        {
            if (!windows || !NamesItsLauncher(Path.GetFileName(shimFile)) || Path.GetDirectoryName(shimFile) is not { Length: > 0 } root
                || !File.Exists(shimFile) || !File.Exists(Path.Combine(root, LauncherScript)))
            {
                return null;
            }

            if (Pair(root) is { } beside)
            {
                return beside;
            }

            var versions = Path.Combine(root, "versions");
            if (!Directory.Exists(versions))
            {
                return null;
            }

            var newest = Directory.EnumerateDirectories(versions)
                .Select(directory => (Directory: directory, Date: DateOf(Path.GetFileName(directory))))
                .Where(version => version.Date is not null)
                .OrderByDescending(version => version.Date)
                .ThenByDescending(version => Path.GetFileName(version.Directory), StringComparer.Ordinal)
                .Select(version => version.Directory)
                .FirstOrDefault();

            return newest is null ? null : Pair(newest);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>A version directory's name as the launcher script reads one: a date, an optional time, and a hash.</summary>
    [GeneratedRegex(@"^(?<year>[0-9]{4})\.(?<month>[0-9]{1,2})\.(?<day>[0-9]{1,2})(-[0-9]{2}-[0-9]{2}-[0-9]{2})?-[a-f0-9]+\z", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex VersionName();

    private static string[]? Pair(string directory)
    {
        var node = Path.Combine(directory, "node.exe");
        var entry = Path.Combine(directory, "index.js");
        return File.Exists(node) && File.Exists(entry) ? [node, entry] : null;
    }

    private static long? DateOf(string name)
    {
        var match = VersionName().Match(name);
        return match.Success
            && long.TryParse(match.Groups["year"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            && long.TryParse(match.Groups["month"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var month)
            && long.TryParse(match.Groups["day"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var day)
            ? (year * 10_000) + (month * 100) + day
            : null;
    }
}
