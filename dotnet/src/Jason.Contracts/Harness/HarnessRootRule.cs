using System.Globalization;

namespace Jason.Contracts.Harness;

/// <summary>Where a folder stands to the places no level of the harness may be, each compared where it leads.</summary>
public enum HarnessPlace
{
    /// <summary>Apart from all of them.</summary>
    Apart,

    /// <summary>The home directory itself.</summary>
    Home,

    /// <summary>The top of a drive, a share or the file system.</summary>
    Top,

    /// <summary>A folder holding the home directory.</summary>
    HoldsHome,

    /// <summary>The data directory itself.</summary>
    Data,

    /// <summary>A folder inside the data directory.</summary>
    InsideData,

    /// <summary>A folder holding the data directory.</summary>
    HoldsData,
}

/// <summary>
/// The rule for what the harness root may not be, written once for the runtime, which refuses a setting by it, and for the
/// CLI, whose <c>export</c> and <c>import</c> read and place a harness root while no runtime runs: it is an absolute path,
/// and not the home directory, a folder holding it or the top of a drive, nor the data directory, a folder inside it or one
/// holding it. Every place is compared where it leads, not how it is spelt, and as the platform's file systems compare
/// names by default (<see cref="FolderPaths"/>).
/// </summary>
public static class HarnessRootRule
{
    /// <summary>The setting that names the root.</summary>
    public const string Setting = "Harness:Root";

    /// <summary>The folder in the home directory the root is when nothing names another.</summary>
    public const string DefaultFolder = "Jason";

    /// <summary>
    /// The root a value names, as a full path without a separator at its end, or <see cref="DefaultFolder"/> in
    /// <paramref name="home"/> when the value is null or blank. Call it on a value the rule took: a relative path is
    /// resolved against nothing in particular.
    /// </summary>
    public static string Of(string? written, string home)
    {
        ArgumentNullException.ThrowIfNull(home);
        return FolderPaths.Normal(Written(written) is { } root ? root : Path.Combine(home, DefaultFolder));
    }

    /// <summary>
    /// Why the root a value names may not be the root, as the sentence a refused setting is said in, or null when it may.
    /// A null or blank value names the default, which is held to the data directory's rule alone. The sentence describes
    /// what was written and never says it back: a path names the account it is in.
    /// </summary>
    public static string? Refusal(string? written, string dataDirectory, string home)
    {
        ArgumentNullException.ThrowIfNull(dataDirectory);
        ArgumentNullException.ThrowIfNull(home);

        var value = Written(written);
        if (value is not null && (value.Contains('\0', StringComparison.Ordinal) || !Path.IsPathFullyQualified(FolderPaths.Undevice(value))))
        {
            return $"{Setting} must be an absolute path, one that starts at a drive, a share or /, and ~ is not expanded; the value written is {Characters(value.Length)} long and is not one.";
        }

        // Every place is compared where it leads: a device prefix, a junction or a symbolic link does not make the home or
        // the data directory another folder.
        var root = Of(value, home);

        // Where the root stands to the data directory, the runtime's own: it may be none of the three. Decided apart from
        // the home's relations, since an unset root is held to this one alone: a default that leads to the home, through a
        // junction say, and so holds the data directory beside it, is refused for that.
        var toData = ToData(FolderPaths.Real(root), dataDirectory) switch
        {
            HarnessPlace.Data => ("is the data directory", "be the data directory", "is the data directory"),
            HarnessPlace.InsideData => ("is inside the data directory", "be inside the data directory", "is inside it"),
            HarnessPlace.HoldsData => ("holds the data directory", "hold the data directory", "holds it"),
            _ => default((string Is, string Rule, string Written)?),
        };
        if (value is null)
        {
            return toData is { } standing
                ? $"{Setting} is not set, and its default, {DefaultFolder} in the home directory, {standing.Is}, which is the runtime's own; set {Setting} to an absolute path apart from it."
                : null;
        }

        // A CLAUDE.md at the root is read by every agent session started anywhere below it, so the root is never where
        // every session of the account starts below: the home directory, a folder that holds it, the top of a drive.
        return Where(root, dataDirectory, home) switch
        {
            HarnessPlace.Home => $"{Setting} must not be the home directory itself: every agent session started anywhere in it would read the memory files kept at the root; the value written is the home directory.",
            HarnessPlace.Top => $"{Setting} must not be the top of a drive, a share or the file system: every agent session started anywhere below it would read the memory files kept at the root; the value written is such a top.",
            HarnessPlace.HoldsHome => $"{Setting} must not hold the home directory: every agent session started anywhere in the home would read the memory files kept at the root; the value written holds it.",
            _ => toData is { } where ? $"{Setting} must not {where.Rule}, which is the runtime's own; the value written {where.Written}." : null,
        };
    }

    /// <summary>
    /// Where <paramref name="folder"/> stands, the home's relations first — the home itself, the top of a drive, a folder
    /// holding the home — and then the data directory's: the data directory itself, a folder inside it, one holding it.
    /// </summary>
    public static HarnessPlace Where(string folder, string dataDirectory, string home)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(dataDirectory);
        ArgumentNullException.ThrowIfNull(home);
        var at = FolderPaths.Real(folder);
        var account = FolderPaths.Real(home);
        return FolderPaths.Same(at, account) ? HarnessPlace.Home
            : FolderPaths.Same(at, FolderPaths.Normal(Path.GetPathRoot(at) ?? at)) ? HarnessPlace.Top
            : FolderPaths.Inside(account, at) ? HarnessPlace.HoldsHome
            : ToData(at, dataDirectory);
    }

    /// <summary>Where a folder, already where it leads, stands to the data directory alone: the directory itself, a folder inside it, one holding it, or apart.</summary>
    private static HarnessPlace ToData(string at, string dataDirectory)
    {
        var data = FolderPaths.Real(dataDirectory);
        return FolderPaths.Same(at, data) ? HarnessPlace.Data
            : FolderPaths.Inside(at, data) ? HarnessPlace.InsideData
            : FolderPaths.Inside(data, at) ? HarnessPlace.HoldsData
            : HarnessPlace.Apart;
    }

    private static string? Written(string? written) => string.IsNullOrWhiteSpace(written) ? null : written;

    private static string Characters(int count) =>
        count == 1 ? "1 character" : string.Create(CultureInfo.InvariantCulture, $"{count} characters");
}
