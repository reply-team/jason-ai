namespace Jason.Contracts.Discovery;

/// <summary>
/// Where a single-file build unpacks the native libraries it carries: on its first run, into a folder of its own named
/// for the bundle, <c>&lt;base&gt;/&lt;program&gt;/&lt;bundle id&gt;</c>, which it reads again on every start. One rule
/// for every part of Jason that has to know that folder: the uninstall that removes it, and an agent host whose sandbox
/// lists what calling home reads.
/// </summary>
public static class ExtractedLibraries
{
    /// <summary>The runtime property the .NET host sets to the folders it resolved native libraries from.</summary>
    public const string SearchDirectoriesProperty = "NATIVE_DLL_SEARCH_DIRECTORIES";

    /// <summary>The variable that tells the .NET host which base to unpack under.</summary>
    public const string BaseVariable = "DOTNET_BUNDLE_EXTRACT_BASE_DIR";

    /// <summary>
    /// The running build's own folder, or null for a process that is no single-file build and for one whose host names
    /// none it could have unpacked into. Only the running process can name its own, because only the host that unpacked
    /// it knows which it was; measured on a published build, it is the only entry the host lists.
    /// </summary>
    public static string? OfThisProcess() =>
        SelfExecutable.InstalledImage is { } image && AppContext.GetData(SearchDirectoriesProperty) is string searched
            ? Candidates(searched, image).FirstOrDefault(Directory.Exists)
            : null;

    /// <summary>
    /// The folders of the host's search list that <paramref name="executable"/>'s libraries could have been unpacked into,
    /// in the host's order: each one whose parent is named for the program, and never the folder the executable itself is
    /// in, which the host searches too and which holds nothing it unpacked.
    /// </summary>
    public static IEnumerable<string> Candidates(string searched, string executable)
    {
        ArgumentNullException.ThrowIfNull(searched);
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);

        var name = Path.GetFileNameWithoutExtension(executable);
        var installed = Trimmed(Path.GetDirectoryName(Path.GetFullPath(executable))!);
        foreach (var entry in searched.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var directory = Trimmed(Path.GetFullPath(entry));
            if (!string.Equals(directory, installed, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetFileName(Path.GetDirectoryName(directory)), name, StringComparison.OrdinalIgnoreCase))
            {
                yield return directory;
            }
        }
    }

    /// <summary>
    /// The base a build unpacks under where nothing names its own folder: the one the host is told to use, and otherwise
    /// <c>.net</c> in the temporary folder on Windows and in the home everywhere else.
    /// </summary>
    public static string DefaultBase(string? configured, bool windows, string home, string temp)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(temp);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return $"{(windows ? temp : home).TrimEnd('/', '\\')}{(windows ? '\\' : '/')}.net";
    }

    private static string Trimmed(string path) => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
