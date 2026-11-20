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
    /// <returns>
    /// Each of the routine's folders taken, and each of them left because the process it was renamed from still runs: an
    /// install, an update and a move say nothing of either and leave the second for the next sweep; the removal, after
    /// which no sweep comes, says both.
    /// </returns>
    public static RetiredSweepOutcome Sweep(SetupPlaces places, string? retired = null)
    {
        ArgumentNullException.ThrowIfNull(places);
        var swept = new List<string>();
        var left = new List<string>();
        if (places.Platform is not SetupSystem.Windows)
        {
            return new RetiredSweepOutcome(swept, left);
        }

        var parents = new List<string> { places.Temporary, Beside(places.DefaultCommandLineExecutable) };
        if (retired is { Length: > 0 })
        {
            parents.Add(Beside(retired));
        }

        foreach (var parent in parents.Where(parent => parent.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                SweepIn(places, parent, swept, left);
            }
            catch (Exception unexpected) when (unexpected is not OutOfMemoryException)
            {
                // Quiet whatever it meets: the sweep is housekeeping, and an update, a move or a setup it runs in is never
                // failed by it. What it could not take is left for the next sweep.
            }
        }

        return new RetiredSweepOutcome(swept, left);
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

    private static void SweepIn(SetupPlaces places, string parent, List<string> swept, List<string> left)
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
            var routines = false;
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
                routines = true;
                foreach (var entry in entries)
                {
                    places.Delete(entry.FullName);
                }

                info.Delete();
                swept.Add(folder);
            }
            catch (Exception held) when (held is IOException or UnauthorizedAccessException)
            {
                // The process it was renamed from still runs: left for the next sweep, and said where it is the routine's.
                if (routines)
                {
                    left.Add(folder);
                }
            }
        }
    }

    [GeneratedRegex("^\\.?jason-retired-[0-9a-f]{32}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex Name();
}

/// <summary>What a sweep took, and what it left because the process it was renamed from still runs.</summary>
/// <param name="Swept">Each folder of the routine's that went.</param>
/// <param name="Left">Each folder of the routine's that a running process held.</param>
public sealed record RetiredSweepOutcome(IReadOnlyList<string> Swept, IReadOnlyList<string> Left);
