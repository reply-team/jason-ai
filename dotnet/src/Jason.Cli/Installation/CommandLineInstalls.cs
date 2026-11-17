using System.Text.Json;
using Jason.Cli.Uninstall;
using Jason.Cli.Update;
using Jason.Contracts.Update;

namespace Jason.Cli.Installation;

/// <summary>
/// An older command-line install of Jason: how one is found, and how what it placed is taken off. One rule, read by
/// <c>jason install</c>, which retires such an install once the application's jason answers in its place, and by
/// <c>jason uninstall</c> run as the application's jason, which takes it off with the rest.
/// </summary>
internal static class CommandLineInstalls
{
    /// <summary>
    /// The executable an older command-line install placed, or null where none is here: the one <c>--retire</c> names,
    /// else the one the last <c>jason update apply</c> recorded, else the installer's default place. A file is that only
    /// where it is not the application's (no <c>version.json</c> beside it), not the application's wrapper, and Jason's
    /// by a sign of its own: named by the flag or the record, a notice file of Jason's beside it, or on Windows the
    /// installer's own folder.
    /// </summary>
    public static string? Find(CliEnvironment env, SetupPlaces places, string? retire, List<string> problems)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(places);
        ArgumentNullException.ThrowIfNull(problems);

        var windows = places.Platform is SetupSystem.Windows;
        var pathCase = windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var recorded = Recorded(env);
        if (retire is { } named)
        {
            var full = Path.GetFullPath(named);
            var name = windows ? "jason.exe" : "jason";
            if (!CommandFiles.IsRegularFile(full))
            {
                problems.Add($"--retire names {full}, and there is no regular file there (nothing, a folder or a link), so nothing was retired.");
                return null;
            }

            if (CarriedExecutable.Of(full) is not null)
            {
                problems.Add($"--retire names {full}, which an application carries (a version.json beside it), so it was not retired.");
                return null;
            }

            if (!string.Equals(Path.GetFileName(full), name, pathCase) || CommandFiles.IsWrapperFile(full))
            {
                problems.Add($"--retire names {full}, which is not a command-line install's {name}, so it was left as it is.");
                return null;
            }

            if (!Signed(places, full, recorded))
            {
                problems.Add($"--retire names {full}, and nothing says it is Jason's (no {string.Join(" or ", ReleaseAssets.Notices.Select(notice => Path.GetFileName(ReleaseAssets.NoticeBeside(full, notice))))} beside it, and it is neither the one the last `jason update apply` recorded nor the installer's own place), so it was left as it is.");
                return null;
            }

            return full;
        }

        foreach (var candidate in new[] { recorded, places.DefaultCommandLineExecutable }.OfType<string>())
        {
            var full = Path.GetFullPath(candidate);
            if (!CommandFiles.IsRegularFile(full) || CarriedExecutable.Of(full) is not null || CommandFiles.IsWrapperFile(full))
            {
                continue;
            }

            if (Signed(places, full, recorded))
            {
                return full;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether a file is Jason's by a sign of its own: the one the update ledger records, a notice file of Jason's beside
    /// it, or on Windows the installer's own place.
    /// </summary>
    private static bool Signed(SetupPlaces places, string full, string? recorded) =>
        string.Equals(full, recorded, places.Platform is SetupSystem.Windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
        || ReleaseAssets.Notices.Any(notice => File.Exists(ReleaseAssets.NoticeBeside(full, notice)))
        || (places.Platform is SetupSystem.Windows && string.Equals(full, Path.GetFullPath(places.DefaultCommandLineExecutable), StringComparison.OrdinalIgnoreCase));

    /// <summary>The install path the update ledger names, where there is a readable one; reading it writes nothing.</summary>
    private static string? Recorded(CliEnvironment env)
    {
        try
        {
            return UpdateLedger.ReadFile(new UpdatePaths(env.Paths).Ledger)?.InstallPath is { Length: > 0 } path ? Path.GetFullPath(path) : null;
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException or UpdateLedgerException or JsonException or ArgumentException)
        {
            return null;
        }
    }
}

/// <summary>
/// What a command-line install placed besides its logon registration, taken off: the executable, its notice files, on
/// Windows <c>jason.previous.exe</c> and its Path entry, on macOS its profile block, and its folder once empty.
/// </summary>
/// <param name="places">The machine's places, and how a file is deleted.</param>
/// <param name="left">What was left, and why, one sentence each.</param>
/// <param name="problems">What could not be done, one sentence each.</param>
internal sealed class CommandLineRemoval(SetupPlaces places, List<string> left, List<string> problems)
{
    private readonly bool windows = places.Platform is SetupSystem.Windows;

    /// <summary>
    /// Takes the install at <paramref name="old"/> off. Where the application's wrapper has replaced its executable at
    /// <c>~/.local/bin/jason</c>, that file, its folder and its profile block are the wrapper's now and stay.
    /// </summary>
    /// <returns>Where the executable was renamed aside, on Windows while a running process held it; and what went.</returns>
    public (string? Aside, List<string> Removed) Remove(string old, bool replacedByWrapper)
    {
        var removed = new List<string>();
        string? aside = null;
        if (!replacedByWrapper)
        {
            aside = RemoveExecutable(old, removed);
        }

        var beside = ReleaseAssets.Notices.Select(notice => ReleaseAssets.NoticeBeside(old, notice)).ToList();
        if (windows)
        {
            beside.Add(Path.Combine(Path.GetDirectoryName(old)!, "jason.previous.exe"));
        }

        foreach (var file in beside.Where(File.Exists))
        {
            try
            {
                places.Delete(file);
                removed.Add(file);
            }
            catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
            {
                problems.Add($"{file} could not be removed: {refused.Message}");
            }
        }

        var directory = Path.GetDirectoryName(old)!;
        if (windows)
        {
            TakePathEntry(directory, removed);
        }
        else if (!replacedByWrapper)
        {
            TakeProfileBlock(directory, removed);
        }

        if (!replacedByWrapper && Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            try
            {
                Directory.Delete(directory);
                removed.Add(directory);
            }
            catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
            {
                left.Add($"{directory} is empty and was left: {refused.Message}");
            }
        }

        return (aside, removed);
    }

    /// <summary>
    /// Deleted where nothing runs from it; on Windows, where a running process holds it, renamed into a folder of its
    /// own on its own volume and left there, with nothing started to wait for that process.
    /// </summary>
    private string? RemoveExecutable(string old, List<string> removed)
    {
        try
        {
            places.Delete(old);
            removed.Add(old);
            return null;
        }
        catch (Exception held) when (windows && held is IOException or UnauthorizedAccessException)
        {
            var folder = AsideFolder(old);
            try
            {
                Directory.CreateDirectory(folder);
                var aside = Path.Combine(folder, Path.GetFileName(old));
                File.Move(old, aside);
                left.Add($"{old} is held by a running process, so it was renamed to {aside}; nothing waits to remove it, and it can be deleted once nothing runs from it.");
                return aside;
            }
            catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
            {
                problems.Add($"{old} could neither be removed nor renamed aside: {refused.Message}");
                return null;
            }
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
        {
            problems.Add($"{old} could not be removed: {refused.Message}");
            return null;
        }
    }

    /// <summary>The temporary directory where it is on the executable's volume, else beside its folder: a rename never crosses one.</summary>
    private string AsideFolder(string old)
    {
        var name = $"jason-retired-{Guid.NewGuid():N}";
        if (string.Equals(Path.GetPathRoot(old), Path.GetPathRoot(Path.GetFullPath(places.Temporary)), StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(places.Temporary, name);
        }

        var folder = Path.GetDirectoryName(old)!;
        return Path.Combine(Path.GetDirectoryName(folder) ?? folder, "." + name);
    }

    /// <summary>The old folder's Path entry, by the uninstall's rule: only where the folder holds nothing but what <c>install.ps1</c> wrote there before 0.49.</summary>
    private void TakePathEntry(string directory, List<string> removed)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var value = new UserPathValue(places.PathKey);
            var stored = value.Read();
            var expand = PathEntry.ReadAs(stored?.Kind != Microsoft.Win32.RegistryValueKind.String);
            if (stored is null || !PathEntry.Carries(stored.Raw, directory, expand))
            {
                return;
            }

            if (!PathEntry.JasonsOwn(directory, windows: true))
            {
                left.Add(PathEntry.WhyLeft(new PathEntryPlan(directory, [], stored.Raw, false, "this account's Path value"), windows: true));
                return;
            }

            value.Write(PathEntry.WithoutDirectory(stored.Raw, directory, expand), stored.Kind);
            removed.Add($"the Path entry {directory}");
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            problems.Add($"The Path entry {directory} could not be taken off: {refused.Message}");
        }
    }

    /// <summary>A retired folder's profile block on macOS, by the uninstall's rule, where it was not <c>~/.local/bin</c>.</summary>
    private void TakeProfileBlock(string directory, List<string> removed)
    {
        var plan = PathEntry.ReadProfiles(directory, places.Home, places.Shell);
        if (plan.Profiles.Count == 0)
        {
            return;
        }

        if (!plan.Ours)
        {
            left.Add(PathEntry.WhyLeft(plan, windows: false));
            return;
        }

        foreach (var profile in plan.Profiles)
        {
            try
            {
                var bytes = File.ReadAllBytes(profile);
                var without = PathEntry.WithoutEntry(bytes, directory);
                if (!ReferenceEquals(without, bytes))
                {
                    removed.Add(CreatedProfiles.WriteBackOrRemove(places.Home, profile, without)
                        ? $"the block for {directory} in {profile}, and {profile} itself, which jason install created and which held nothing else"
                        : $"the block for {directory} in {profile}");
                }
            }
            catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
            {
                problems.Add($"{profile} could not be rewritten ({refused.Message}): take the line {PathEntry.ExportLine(directory)} and the line {PathEntry.Marker} above it out by hand.");
            }
        }
    }
}
