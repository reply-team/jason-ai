using Jason.Contracts.Discovery;
using Jason.Runtime.Configuration;

namespace Jason.Runtime.Harness;

/// <summary>
/// The rule for where the harness root may be, written once for the validator that refuses a setting and for the folders
/// that are made under the root. A refusal compares where the paths lead, not how they are spelled, and compares them as
/// the platform's file systems do by default: Windows and macOS ignore case, Linux does not.
/// </summary>
public static class HarnessRoot
{
    /// <summary>How many links one path is followed through before it is compared as it was reached: a chain that goes round is not followed for ever.</summary>
    private const int MaxLinks = 40;

    /// <summary>How two paths are compared here: as the file system of this platform would tell them apart by default.</summary>
    public static StringComparison Comparison { get; } =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

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
        var root = Real(Of(options, home));
        var data = Real(paths.Root);

        // Where the root stands to the data directory, the runtime's own: it may be none of the three.
        var toData = Same(root, data) ? ("is the data directory", "be the data directory", "is the data directory")
            : Inside(root, data) ? ("is inside the data directory", "be inside the data directory", "is inside it")
            : Inside(data, root) ? ("holds the data directory", "hold the data directory", "holds it")
            : default((string Is, string Rule, string Written)?);
        if (written is null)
        {
            return toData is { } standing
                ? $"{Setting} is not set, and its default, {HarnessOptions.DefaultFolder} in the home directory, {standing.Is}, which is the runtime's own; set {Setting} to an absolute path apart from it."
                : null;
        }

        // A CLAUDE.md at the root is read by every agent session started anywhere below it, so the root is never where
        // every session of the account starts below: the home directory, a folder that holds it, the top of a drive.
        var account = Real(home);
        if (Same(root, account))
        {
            return $"{Setting} must not be the home directory itself: every agent session started anywhere in it would read the memory files kept at the root; the value written is the home directory.";
        }

        if (Same(root, Normal(Path.GetPathRoot(root) ?? root)))
        {
            return $"{Setting} must not be the top of a drive, a share or the file system: every agent session started anywhere below it would read the memory files kept at the root; the value written is such a top.";
        }

        if (Inside(account, root))
        {
            return $"{Setting} must not hold the home directory: every agent session started anywhere in the home would read the memory files kept at the root; the value written holds it.";
        }

        return toData is { } place
            ? $"{Setting} must not {place.Rule}, which is the runtime's own; the value written {place.Written}."
            : null;
    }

    private static string? Written(HarnessOptions options) => string.IsNullOrWhiteSpace(options.Root) ? null : options.Root;

    private static string Normal(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    /// <summary>
    /// Where a path leads, as the file system finds it rather than as it is spelled: full, without a device prefix, and
    /// with every part of it that is a link — a junction or a symbolic link — replaced by where the link leads, however
    /// deep. A part that is not there yet, or cannot be read, is taken as written, and so is everything after it.
    /// </summary>
    private static string Real(string path)
    {
        var real = Normal(Undevice(path));
        for (var hops = 0; hops < MaxLinks && Linked(real) is { } further; hops++)
        {
            real = further;
        }

        return real;
    }

    /// <summary>The path with its first part that is a link replaced by where that link finally leads, or null when no part of it is one.</summary>
    private static string? Linked(string path)
    {
        var top = Path.GetPathRoot(path) ?? string.Empty;
        var parts = path[top.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var at = top;
        for (var part = 0; part < parts.Length; part++)
        {
            at = Path.Combine(at, parts[part]);
            try
            {
                if (new DirectoryInfo(at).ResolveLinkTarget(returnFinalTarget: true) is { } target)
                {
                    return Normal(Undevice(Path.Combine([target.FullName, .. parts[(part + 1)..]])));
                }
            }
            catch (Exception unread) when (unread is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// A Windows device path — <c>\\?\C:\…</c>, <c>\\.\C:\…</c>, <c>\??\C:\…</c>, <c>\\?\UNC\server\share\…</c> — as the
    /// drive or share path behind its prefix, which turns off Windows' own reading of a path and names the same folder.
    /// One whose rest names no drive and no share is left a relative path, which the rule refuses. Elsewhere a path is
    /// as it is written.
    /// </summary>
    private static string Undevice(string path)
    {
        var device = OperatingSystem.IsWindows()
            && path.Length >= 4
            && (path.StartsWith(@"\??\", StringComparison.Ordinal)
                || (IsSeparator(path[0]) && IsSeparator(path[1]) && (path[2] is '?' or '.') && IsSeparator(path[3])));
        if (!device)
        {
            return path;
        }

        var rest = path[4..];
        return rest.StartsWith(@"UNC", StringComparison.OrdinalIgnoreCase) && rest.Length > 3 && IsSeparator(rest[3])
            ? @"\\" + rest[4..]
            : rest;
    }

    private static bool IsSeparator(char c) => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;

    private static bool Same(string left, string right) => string.Equals(left, right, Comparison);

    /// <summary>Below the folder, by whole names: <c>.jason-old</c> begins with <c>.jason</c> and is beside it, not inside.</summary>
    private static bool Inside(string path, string folder)
    {
        var prefix = Path.EndsInDirectorySeparator(folder) ? folder : folder + Path.DirectorySeparatorChar;
        return path.Length > prefix.Length && path.StartsWith(prefix, Comparison);
    }
}
