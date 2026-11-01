using Jason.Contracts.Discovery;

namespace Jason.Runtime.Plugins.Registry;

/// <summary>One directory that might hold a plugin, and whether it holds a manifest at all.</summary>
public sealed record CandidateDirectory(string Name, string Path, bool HasManifest);

/// <summary>
/// What is installed, as directories: the direct subdirectories of the plugins directory, in one stable order.
/// A name starting with a dot is not a candidate — that is where a version-control directory or an editor's
/// scratch folder lives, and neither is a plugin.
/// </summary>
public static class PackageScanner
{
    public const string ManifestFileName = "plugin.yaml";

    public static IReadOnlyList<CandidateDirectory> Scan(JasonPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!Directory.Exists(paths.PluginsDirectory))
        {
            return [];
        }

        var candidates = new List<CandidateDirectory>();
        foreach (var directory in Directory.EnumerateDirectories(paths.PluginsDirectory))
        {
            var name = Path.GetFileName(directory);
            if (name.Length == 0 || name.StartsWith('.'))
            {
                continue;
            }

            candidates.Add(new CandidateDirectory(name, directory, File.Exists(Path.Combine(directory, ManifestFileName))));
        }

        candidates.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));
        return candidates;
    }
}
