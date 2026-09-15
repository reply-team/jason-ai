namespace Jason.Contracts.Tests.OperationContracts;

/// <summary>
/// Where the published contract package is on disk. The tests read the very files the repository publishes rather
/// than a copy under the test binaries, because the point of the package is that one document governs.
/// </summary>
internal static class ContractFiles
{
    /// <summary>The repository root, found by walking up from the test binaries to the file that pins the SDK.</summary>
    public static string Root { get; } = FindRoot();

    public static string Package => Path.Combine(Root, "docs", "contracts");

    public static string Operations => Path.Combine(Package, "operations");

    public static string Fixtures => Path.Combine(Package, "fixtures");

    public static IReadOnlyList<string> FixtureFiles(string prefix) =>
        Directory.Exists(Fixtures)
            ? [.. Directory.EnumerateFiles(Fixtures, prefix + "-*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal)]
            : [];

    /// <summary>The path as the repository writes it, so a failure message names a file a reader can open.</summary>
    public static string Relative(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("No global.json was found above the test binaries, so the repository root is unknown.");
    }
}
