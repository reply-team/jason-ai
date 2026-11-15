using System.Security;
using Jason.Contracts.Discovery;
using Jason.Contracts.Files;

namespace Jason.Runtime.Configuration;

/// <summary>What the start's sweep of the settings writers' temporary files did, by counts alone.</summary>
/// <param name="Removed">Files of the writers' shapes removed.</param>
/// <param name="Kept">Files of those shapes kept: younger than <see cref="SettingsTemporarySweep.MinimumAge"/>, held open, or a link.</param>
/// <param name="Refused">Files of those shapes the system would not let go, left where they are.</param>
internal sealed record SettingsTemporarySweepReport(int Removed, int Kept, int Refused)
{
    /// <summary>How many files of the writers' shapes the sweep found.</summary>
    public int Found => Removed + Kept + Refused;

    /// <summary>Two sweeps' counts as one.</summary>
    public static SettingsTemporarySweepReport operator +(SettingsTemporarySweepReport left, SettingsTemporarySweepReport right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return new(left.Removed + right.Removed, left.Kept + right.Kept, left.Refused + right.Refused);
    }
}

/// <summary>
/// The runtime's start removes the temporary files a settings writer left beside the file it writes — the settings file,
/// and the secrets file where the layout keeps it apart (<see cref="SettingsTemporaryFiles.WrittenFiles"/>); the
/// runtime's own and the desktop's, by the two shapes <see cref="SettingsTemporaryFiles"/> names and no other file of the
/// folder — when no writer can still be at work on one. It runs only once the start holds this data directory's instance
/// lock: a start the lock refuses owns nothing of the installation another runtime runs on, and removes nothing of it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The rule.</b> A file is removed only when its last write is at least <see cref="MinimumAge"/> old by the clock, and
/// then only through an open that shares nothing and deletes the file as it closes. On Windows the check that no handle
/// holds the file and its removal are that one act; the age is read from the folder's listing just before. Either writer holds its file for well under a second: it creates it, writes and flushes it,
/// closes it and renames it over the settings file (the runtime tries the rename five times a tenth of a second apart).
/// </para>
/// <para>
/// <b>Why a live writer's file never matches, per platform.</b> On every platform a writer's file was written within the
/// last second, so the age alone keeps it, and a write time ahead of the clock is kept too. On Windows the open on top of
/// it is refused for any file another handle holds — Node's open, which the desktop's writer makes, shares read, write and
/// delete, and the runtime's shares nothing; either way an open that shares nothing is a sharing violation — so a writer
/// that has the file open is kept however old the file looks. Linux and macOS have no such refusal for the desktop's
/// writer, and there the age alone decides. Were a writer ever stopped for longer than the age between its write and its
/// rename — a machine asleep at that instant — the removal makes its rename fail, and both writers answer a rename that
/// failed as a write that did not happen: the settings file keeps its old bytes whole and the person is told. Nothing is
/// ever written over the settings file by the sweep, and it is never touched.
/// </para>
/// <para>
/// <b>What it costs.</b> A stray file younger than the age is left until a start after it is older, and one held open
/// until a start after it is let go; it holds the settings' secrets with the settings file's own permissions meanwhile.
/// A link of the shape is no writer's and is left, so what it leads to is never touched.
/// </para>
/// </remarks>
internal static class SettingsTemporarySweep
{
    /// <summary>How old a stray file's last write must be before the sweep takes it: ten minutes, far above any write's duration.</summary>
    internal static readonly TimeSpan MinimumAge = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Sweeps beside every file <see cref="SettingsTemporaryFiles.WrittenFiles"/> names for <paramref name="paths"/>, each by
    /// <see cref="Sweep(string, DateTimeOffset, Action{string}?)"/>, and adds up what each did.
    /// </summary>
    internal static SettingsTemporarySweepReport Sweep(JasonPaths paths, DateTimeOffset now, Action<string>? remove = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var report = new SettingsTemporarySweepReport(0, 0, 0);
        foreach (var file in SettingsTemporaryFiles.WrittenFiles(paths))
        {
            report += Sweep(file, now, remove);
        }

        return report;
    }

    /// <summary>
    /// Sweeps the folder of <paramref name="settingsFile"/>; never throws for a file it could not remove or a folder it could
    /// not read, which it counts or passes over.
    /// </summary>
    /// <param name="settingsFile">The settings file, whose name the shapes are built on and whose folder is swept.</param>
    /// <param name="now">The moment the ages are measured against.</param>
    /// <param name="remove">How a file old enough is removed; null means <see cref="RemoveUnheld"/>.</param>
    internal static SettingsTemporarySweepReport Sweep(string settingsFile, DateTimeOffset now, Action<string>? remove = null)
    {
        ArgumentNullException.ThrowIfNull(settingsFile);
        remove ??= RemoveUnheld;
        var directory = Path.GetDirectoryName(settingsFile)!;
        var settingsName = Path.GetFileName(settingsFile);
        FileInfo[] candidates;
        try
        {
            candidates = [.. new DirectoryInfo(directory).EnumerateFiles().Where(file => SettingsTemporaryFiles.IsOne(settingsName, file.Name))];
        }
        catch (Exception unread) when (unread is IOException or UnauthorizedAccessException or SecurityException)
        {
            // A folder that is not there holds nothing to sweep; one that cannot be read is the settings file's read to say.
            return new SettingsTemporarySweepReport(0, 0, 0);
        }

        int removed = 0, kept = 0, refused = 0;
        foreach (var file in candidates)
        {
            try
            {
                if (file.LinkTarget is not null || now.UtcDateTime - file.LastWriteTimeUtc < MinimumAge)
                {
                    kept++;
                    continue;
                }

                remove(file.FullName);
                removed++;
            }
            catch (FileNotFoundException)
            {
                // Gone in between: a writer renamed it, or something else took it. Nothing of it is left to count.
            }
            catch (IOException held) when (Held(held))
            {
                kept++;
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or SecurityException)
            {
                refused++;
            }
        }

        return new SettingsTemporarySweepReport(removed, kept, refused);
    }

    /// <summary>
    /// Removes <paramref name="path"/> through an open that shares nothing and deletes the file as it closes: on Windows that
    /// open is refused while any other handle holds the file, so a file a writer holds is never removed.
    /// </summary>
    internal static void RemoveUnheld(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None, bufferSize: 1, FileOptions.DeleteOnClose);
    }

    private const int SharingViolation = unchecked((int)0x80070020);

    private const int LockViolation = unchecked((int)0x80070021);

    /// <summary>Whether a refused open says another handle holds the file: Windows' sharing or lock violation.</summary>
    private static bool Held(IOException failure) =>
        OperatingSystem.IsWindows() && failure.HResult is SharingViolation or LockViolation;
}
