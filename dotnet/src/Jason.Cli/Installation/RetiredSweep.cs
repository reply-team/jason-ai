using System.Text.RegularExpressions;

namespace Jason.Cli.Installation;

/// <summary>
/// The folders <c>jason install</c> renamed a still-running command-line executable into on Windows, taken away once that
/// process has ended: <c>jason-retired-&lt;id&gt;</c> under the temporary directory, or <c>.jason-retired-&lt;id&gt;</c> beside the
/// command-line install's folder where that folder is on another volume (<see cref="CommandLineRemoval"/>). The routine starts
/// nothing to wait for that process, so the removal is left to the next run of this sweep.
/// </summary>
/// <remarks>
/// Quiet: Windows refuses to delete an image a process still runs, and that folder is simply left for the next sweep. Only a
/// folder of exactly that name, holding nothing but the <c>jason.exe</c> the routine put there, is taken; anything else of
/// that name is not the routine's, and stays.
/// </remarks>
public static partial class RetiredSweep
{
    /// <summary>
    /// Sweeps the places the routine renames into: the temporary directory, the folder beside the default command-line
    /// install's, and the one beside <paramref name="retired"/>'s folder, the executable a move retires. Windows only.
    /// </summary>
    public static void Sweep(SetupPlaces places, string? retired = null)
    {
        ArgumentNullException.ThrowIfNull(places);
        if (places.Platform is not SetupSystem.Windows)
        {
            return;
        }

        var parents = new List<string> { places.Temporary, Beside(places.DefaultCommandLineExecutable) };
        if (retired is { Length: > 0 })
        {
            parents.Add(Beside(retired));
        }

        foreach (var parent in parents.Where(parent => parent.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            SweepIn(places, parent);
        }
    }

    /// <summary>The folder that holds a command-line install's folder: where its aside folder goes on another volume.</summary>
    private static string Beside(string executable)
    {
        try
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(executable));
            return folder is null ? string.Empty : Path.GetDirectoryName(folder) ?? string.Empty;
        }
        catch (Exception unreadable) when (unreadable is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Empty;
        }
    }

    private static void SweepIn(SetupPlaces places, string parent)
    {
        IEnumerable<string> folders;
        try
        {
            folders = Directory.EnumerateDirectories(parent).Where(folder => Name().IsMatch(Path.GetFileName(folder))).ToList();
        }
        catch (Exception unread) when (unread is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var folder in folders)
        {
            try
            {
                var info = new DirectoryInfo(folder);
                if (info.LinkTarget is not null)
                {
                    continue;
                }

                var entries = info.GetFileSystemInfos();
                if (entries.Length > 1 || entries.Any(entry => entry is not FileInfo { LinkTarget: null } || !string.Equals(entry.Name, "jason.exe", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                // Through the setup's own delete, which a test stands a running process's image in for.
                foreach (var entry in entries)
                {
                    places.Delete(entry.FullName);
                }

                info.Delete();
            }
            catch (Exception held) when (held is IOException or UnauthorizedAccessException)
            {
                // The process it was renamed from still runs: left for the next sweep.
            }
        }
    }

    [GeneratedRegex("^\\.?jason-retired-[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex Name();
}
