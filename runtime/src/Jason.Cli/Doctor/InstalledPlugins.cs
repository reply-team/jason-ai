using System.Collections.Frozen;
using System.Security;
using System.Text.RegularExpressions;
using Jason.Contracts.Discovery;

namespace Jason.Cli.Doctor;

/// <summary>
/// The plugins installed in a data directory, as a settings key that names one is told: by the name of its directory.
/// </summary>
/// <remarks>
/// The same rule the runtime holds a key to — every directory directly under the plugins directory whose name does not
/// start with a dot, that holds a manifest and is named the way a plugin's id has to be — listed here because the
/// client cannot reference the runtime and a bundle is most wanted when the runtime is not running. A copy of a rule is
/// only as good as the test that holds it to the original, and the runtime's tests do.
/// </remarks>
public static partial class InstalledPlugins
{
    /// <summary>The file whose presence makes a directory a package, as the runtime's plugin loader names it.</summary>
    private const string ManifestFileName = "plugin.yaml";

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
                    .Where(name => name is not null && PluginId().IsMatch(name))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)!
                : None;
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException or SecurityException)
        {
            return None;
        }
    }

    /// <summary>
    /// What a plugin's id has to be, as a manifest's own <c>id</c> is held to it: a lowercase letter, then one to sixty-three
    /// lowercase letters, digits or dashes. A name starting with a dot is never one.
    /// </summary>
    [GeneratedRegex(@"^[a-z][a-z0-9-]{1,63}\z")]
    private static partial Regex PluginId();
}
