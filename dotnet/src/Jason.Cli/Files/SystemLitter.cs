namespace Jason.Cli.Files;

/// <summary>
/// The files a system leaves in any folder it shows: one list, read wherever a folder that holds nothing else is taken for
/// one that holds nothing — the data directory the update's finishing step looks at, and a skill folder a deployment meets.
/// </summary>
internal static class SystemLitter
{
    /// <summary>The names, matched exactly: macOS's, then Windows's two.</summary>
    public static readonly IReadOnlyList<string> Names = [".DS_Store", "desktop.ini", "Thumbs.db"];

    /// <summary>Whether a path is a regular file, not a link, under one of <see cref="Names"/>.</summary>
    public static bool Is(string path) =>
        File.Exists(path) && new FileInfo(path).LinkTarget is null && Names.Contains(Path.GetFileName(path), StringComparer.Ordinal);
}
