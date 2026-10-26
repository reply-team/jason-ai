using System.Collections.Frozen;
using System.Security;
using Jason.Contracts.Discovery;
using Jason.Contracts.Plugins;

namespace Jason.Cli.Doctor;

/// <summary>
/// The plugins installed in a data directory, as a settings key that names one is told: by the name of its directory.
/// </summary>
/// <remarks>
/// The same rule the runtime holds a key to — every directory directly under the plugins directory that holds a manifest
/// and is named the way a plugin's id has to be (<see cref="PluginIds"/>, the one spelling of that rule) — listed here
/// because the client cannot reference the runtime and a bundle is most wanted when the runtime is not running. The
/// listing is a copy of the runtime's, and a copy is only as good as the test that holds it to the original, which the
/// runtime's tests are.
/// </remarks>
public static class InstalledPlugins
{
    /// <summary>The file whose presence makes a directory a package, as the runtime's plugin loader names it.</summary>
    internal const string ManifestFileName = "plugin.yaml";

    private static readonly IReadOnlySet<string> None = FrozenSet<string>.Empty;

    /// <summary>
    /// The names of the plugin directories, compared without regard to case as the keys of a settings file are. A
    /// directory that does not exist or cannot be listed has none: a listing that cannot be read names nothing, so a
    /// key that would have named a plugin is left out of a description rather than printed on a guess.
    /// </summary>
    public static IReadOnlySet<string> Ids(JasonPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        try
        {
            return Directory.Exists(paths.PluginsDirectory)
                ? Directory.EnumerateDirectories(paths.PluginsDirectory)
                    .Where(directory => File.Exists(Path.Combine(directory, ManifestFileName)))
                    .Select(Path.GetFileName)
                    .Where(PluginIds.IsWellFormed)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)!
                : None;
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException or SecurityException)
        {
            return None;
        }
    }
}
