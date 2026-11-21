namespace Jason.Cli.Installation;

/// <summary>
/// Whether Homebrew's cask <c>jason</c> placed Jason Desktop's application, by the rule <see cref="PackageOwner"/> holds
/// every manager to: the path says where; the record says whose.
/// </summary>
/// <remarks>
/// <para>
/// The application is Homebrew's only where, under one of Homebrew's prefixes, the cask's own record stands
/// (<see cref="ManagerRecords.ReadHomebrewCask"/>) and the application is the <c>Jason.app</c> in the folder that record
/// says the cask put it in: the one the install was given with <c>--appdir</c>, or <c>/Applications</c>. The link
/// <c>&lt;prefix&gt;/bin/jason</c> is not the signal: Homebrew skips it while the older formula's keg is linked there.
/// Asking <c>brew</c> itself would be a child process, slow and dependent on the PATH, for an answer its own files give.
/// </para>
/// <para>
/// Wrong in two ways, both said on the page. A record that outlived the application, deleted by hand and placed again
/// from the disk image, reads as Homebrew's, and <c>jason uninstall</c> then names <c>brew uninstall --zap --cask jason</c>,
/// which takes it off: one more command, nothing lost. A prefix this does not know, or a record it cannot read, reads as
/// no record, and the application is deleted as any other; Homebrew's record stands, and the next
/// <c>brew uninstall --cask jason</c> needs <c>--force</c>.
/// </para>
/// </remarks>
public static class HomebrewCask
{
    /// <summary>The variable Homebrew's shell environment sets to its prefix.</summary>
    public const string PrefixVariable = "HOMEBREW_PREFIX";

    /// <summary>Homebrew's default prefixes: Apple silicon's, Intel's, then Linux's.</summary>
    public static readonly IReadOnlyList<string> DefaultPrefixes = ["/opt/homebrew", "/usr/local", "/home/linuxbrew/.linuxbrew"];

    /// <summary>The folder a cask puts its application in where the install names none.</summary>
    public const string DefaultAppDir = "/Applications";

    /// <summary>
    /// The prefix whose cask record placed <paramref name="application"/>, or null where none did, or where the environment
    /// names no records (every test environment that does not plant them, and the answer before this rule existed).
    /// </summary>
    public static string? Placed(string application, CliEnvironment env)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(application);
        ArgumentNullException.ThrowIfNull(env);

        if (env.Managers is not { } records)
        {
            return null;
        }

        var given = env.Variables is { } variables ? variables.GetValueOrDefault(PrefixVariable) : Environment.GetEnvironmentVariable(PrefixVariable);
        return Placed(application, given, records);
    }

    /// <summary>The rule itself, over the prefix the environment names (or none) and the managers' records.</summary>
    public static string? Placed(string application, string? prefixVariable, IManagerRecords records)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(application);
        ArgumentNullException.ThrowIfNull(records);

        var placed = Trimmed(application);
        foreach (var prefix in Prefixes(prefixVariable))
        {
            if (records.HomebrewCask(prefix) is { NamesTheApplication: true } record
                && string.Equals(Trimmed(Path.Combine(record.AppDir ?? DefaultAppDir, "Jason.app")), placed, StringComparison.OrdinalIgnoreCase))
            {
                return prefix;
            }
        }

        return null;
    }

    /// <summary>The prefixes asked, in order: the variable's, where it is an absolute path, then the defaults, once each.</summary>
    public static IReadOnlyList<string> Prefixes(string? prefixVariable)
    {
        var prefixes = new List<string>();
        if (prefixVariable is { Length: > 1 } given && Path.IsPathFullyQualified(given))
        {
            prefixes.Add(Path.TrimEndingDirectorySeparator(given));
        }

        foreach (var prefix in DefaultPrefixes.Where(prefix => !prefixes.Contains(prefix, StringComparer.Ordinal)))
        {
            prefixes.Add(prefix);
        }

        return prefixes;
    }

    /// <summary>The cask's link to the jason the application carries, under the prefix that placed it.</summary>
    public static string Link(string prefix) => Path.Combine(prefix, "bin", "jason");

    private static string Trimmed(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
