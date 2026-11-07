using Jason.Contracts.Discovery;
using Jason.Contracts.Harness;
using Jason.Runtime.Configuration;

namespace Jason.Runtime.Harness;

/// <summary>
/// The rule for where the harness root may be, written once for the validator that refuses a setting and for the folders
/// that are made under the root. A refusal compares where the paths lead, not how they are spelled, and compares them as
/// the platform's file systems do by default: Windows and macOS ignore case, Linux does not (<see cref="FolderPaths"/>).
/// </summary>
public static class HarnessRoot
{
    /// <summary>How two paths are compared here: as the file system of this platform would tell them apart by default.</summary>
    public static StringComparison Comparison => FolderPaths.Comparison;

    /// <summary>
    /// The root the options name, as a full path without a separator at its end, or <see cref="HarnessOptions.DefaultFolder"/>
    /// in <paramref name="home"/> when they name none. Call it on options that validated: a relative path is resolved
    /// against nothing in particular.
    /// </summary>
    public static string Of(HarnessOptions options, string home)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(home);
        return Normal(Written(options) is { } root ? root : Path.Combine(home, HarnessOptions.DefaultFolder));
    }

    /// <summary>
    /// Why the root the options name may not be the root, as the sentence a refused setting is said in, or null when it may.
    /// The sentence describes what was written and never says it back: a path names the account it is in.
    /// </summary>
    public static string? Refusal(HarnessOptions options, JasonPaths paths, string home)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(home);

        const string Setting = HarnessOptions.Section + ":Root";
        var written = Written(options);
        if (written is not null && (written.Contains('\0', StringComparison.Ordinal) || !Path.IsPathFullyQualified(Undevice(written))))
        {
            return $"{Setting} must be an absolute path, one that starts at a drive, a share or /, and ~ is not expanded; {OptionRules.Written("value", written, "is not one")}";
        }

        // Every place is compared where it leads: a device prefix, a junction or a symbolic link does not make the home or
        // the data directory another folder.
        var root = Of(options, home);

        // Where the root stands to the data directory, the runtime's own: it may be none of the three. Decided apart from
        // the home's relations, since an unset root is held to this one alone: a default that leads to the home, through a
        // junction say, and so holds the data directory beside it, is refused for that.
        var toData = ToData(Real(root), paths) switch
        {
            Place.Data => ("is the data directory", "be the data directory", "is the data directory"),
            Place.InsideData => ("is inside the data directory", "be inside the data directory", "is inside it"),
            Place.HoldsData => ("holds the data directory", "hold the data directory", "holds it"),
            _ => default((string Is, string Rule, string Written)?),
        };
        if (written is null)
        {
            return toData is { } standing
                ? $"{Setting} is not set, and its default, {HarnessOptions.DefaultFolder} in the home directory, {standing.Is}, which is the runtime's own; set {Setting} to an absolute path apart from it."
                : null;
        }

        // A CLAUDE.md at the root is read by every agent session started anywhere below it, so the root is never where
        // every session of the account starts below: the home directory, a folder that holds it, the top of a drive.
        return Where(root, paths, home) switch
        {
            Place.Home => $"{Setting} must not be the home directory itself: every agent session started anywhere in it would read the memory files kept at the root; the value written is the home directory.",
            Place.Top => $"{Setting} must not be the top of a drive, a share or the file system: every agent session started anywhere below it would read the memory files kept at the root; the value written is such a top.",
            Place.HoldsHome => $"{Setting} must not hold the home directory: every agent session started anywhere in the home would read the memory files kept at the root; the value written holds it.",
            _ => toData is { } where ? $"{Setting} must not {where.Rule}, which is the runtime's own; the value written {where.Written}." : null,
        };
    }

    /// <summary>
    /// Why a folder a person names for a project may not be one, as the message of a refused field, or null when it may:
    /// the places the root may not be are no level's — a <c>CLAUDE.md</c> in the home directory, a folder holding it or the
    /// top of a drive is read by every agent session below it, and the data directory is the runtime's own. The sentence
    /// never says the path back.
    /// </summary>
    public static string? FolderRefusal(string folder, JasonPaths paths, string home)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(home);
        return Where(folder, paths, home) switch
        {
            Place.Home => "path must not be the home directory: every agent session started anywhere in it would read the project's memory file.",
            Place.Top => "path must not be the top of a drive, a share or the file system: every agent session started below it would read the project's memory file.",
            Place.HoldsHome => "path must not hold the home directory: every agent session started anywhere in the home would read the project's memory file.",
            Place.Data => "path must not be the data directory, which is the runtime's own.",
            Place.InsideData => "path must not be inside the data directory, which is the runtime's own.",
            Place.HoldsData => "path must not hold the data directory, which is the runtime's own.",
            _ => null,
        };
    }

    /// <summary>
    /// Why a directory a person grants a plugin to append lines in may not be one, as the sentence of the package problem,
    /// or null when it may: a directory is named whole, and the places no level of the harness may be are no plugin's
    /// either. The home, a folder holding it and the top of a drive would open the account to a package's lines, the data
    /// directory is the runtime's own, and the harness root holds the memory files the runtime promises never to write a
    /// word of, which its door would then write for a plugin. Each is compared where it leads; the sentence never says the
    /// path back.
    /// </summary>
    public static string? GrantRefusal(string? directory, JasonPaths paths, string home, string root)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(root);
        if (string.IsNullOrWhiteSpace(directory) || directory.Contains('\0', StringComparison.Ordinal) || !Path.IsPathFullyQualified(Undevice(directory)))
        {
            return "a file grant must be an absolute path, one that starts at a drive, a share or the file system's top, and neither a star nor a tilde is expanded.";
        }

        return Where(directory, paths, home) switch
        {
            Place.Home => "a file grant must not be the home directory; name a folder of its own.",
            Place.Top => "a file grant must not be the top of a drive, a share or the file system; name a folder of its own.",
            Place.HoldsHome => "a file grant must not hold the home directory; name a folder of its own.",
            Place.Data => "a file grant must not be the data directory, which is the runtime's own.",
            Place.InsideData => "a file grant must not be inside the data directory, which is the runtime's own.",
            Place.HoldsData => "a file grant must not hold the data directory, which is the runtime's own.",
            _ => ToHarnessRoot(directory, root),
        };
    }

    /// <summary>Why a directory may not be granted by where it stands to the harness root, or null when it stands apart from it.</summary>
    private static string? ToHarnessRoot(string directory, string root)
    {
        var at = Real(directory);
        var level = Real(root);
        return Same(at, level) ? "a file grant must not be the harness root, whose folders hold the memory files the runtime never writes."
            : Inside(at, level) ? "a file grant must not be inside the harness root, whose folders hold the memory files the runtime never writes."
            : Inside(level, at) ? "a file grant must not hold the harness root, whose folders hold the memory files the runtime never writes."
            : null;
    }

    /// <summary>
    /// Why a folder a person names for a project may not be one by where it stands to the harness root, as the message of a
    /// refused field, or null when it may: the root is the client's level, a folder holding it would have its memory file
    /// read by every session started in any project or campaign, and the root's <c>projects/</c> holds every project's
    /// folder and is no level's. Each is compared where it leads; the sentence never says the path back.
    /// </summary>
    public static string? RootRefusal(string folder, string root)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(root);
        var at = Real(folder);
        var level = Real(root);
        return Same(at, level) ? "path must not be the harness root, which is the client's level and no project's."
            : Inside(level, at) ? "path must not hold the harness root: every agent session started in any project or campaign would read the project's memory file."
            : Same(at, Real(Path.Combine(root, HarnessFolders.ProjectsDirectory))) ? "path must not be the root's projects directory, which holds every project's folder and is no level's."
            : null;
    }

    /// <summary>Where a folder stands to the places no level of the harness may be, each compared where it leads.</summary>
    private enum Place
    {
        Apart,
        Home,
        Top,
        HoldsHome,
        Data,
        InsideData,
        HoldsData,
    }

    /// <summary>
    /// Where <paramref name="folder"/> stands, the home's relations first — the home itself, the top of a drive, a folder
    /// holding the home — and then the data directory's: the data directory itself, a folder inside it, one holding it.
    /// </summary>
    private static Place Where(string folder, JasonPaths paths, string home)
    {
        var at = Real(folder);
        var account = Real(home);
        return Same(at, account) ? Place.Home
            : Same(at, Normal(Path.GetPathRoot(at) ?? at)) ? Place.Top
            : Inside(account, at) ? Place.HoldsHome
            : ToData(at, paths);
    }

    /// <summary>Where a folder, already where it leads, stands to the data directory alone: the directory itself, a folder inside it, one holding it, or apart.</summary>
    private static Place ToData(string at, JasonPaths paths)
    {
        var data = Real(paths.Root);
        return Same(at, data) ? Place.Data
            : Inside(at, data) ? Place.InsideData
            : Inside(data, at) ? Place.HoldsData
            : Place.Apart;
    }

    private static string? Written(HarnessOptions options) => string.IsNullOrWhiteSpace(options.Root) ? null : options.Root;

    private static string Normal(string path) => FolderPaths.Normal(path);

    private static string Real(string path) => FolderPaths.Real(path);

    private static string Undevice(string path) => FolderPaths.Undevice(path);

    private static bool Same(string left, string right) => FolderPaths.Same(left, right);

    private static bool Inside(string path, string folder) => FolderPaths.Inside(path, folder);
}
