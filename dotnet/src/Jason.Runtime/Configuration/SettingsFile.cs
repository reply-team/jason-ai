using System.Collections;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;

namespace Jason.Runtime.Configuration;

/// <summary>
/// The user's settings file, for an act that has to work from it as it stands rather than from what its watch has told
/// the runtime so far. A service, because the configuration it reads is the runtime's own root, which the host's
/// services do not otherwise see.
/// </summary>
/// <param name="configuration">The runtime's own configuration root.</param>
/// <param name="environmentPrefix">The prefix its environment variables are read under, which a test may set.</param>
public sealed class SettingsFile(IConfiguration configuration, string environmentPrefix = JasonConfiguration.EnvironmentPrefix)
{
    /// <summary>
    /// Reads the file now, and answers the failure of a file that is there and could not be used, or null;
    /// <see cref="JasonConfiguration.ReadSettingsFile"/> says what is refused and how.
    /// </summary>
    public SettingsFileFailure? Read() => JasonConfiguration.ReadSettingsFile(configuration);

    /// <summary>The file's bytes now, or null where it is not there (<see cref="JasonConfiguration.ReadSettingsBytes"/>).</summary>
    public byte[]? Bytes() => JasonConfiguration.ReadSettingsBytes(configuration);

    /// <summary>The writer's compare-and-replace (<see cref="JasonConfiguration.CommitSettingsFile"/>).</summary>
    public SettingsCommit Commit(byte[]? expected, byte[]? next) => JasonConfiguration.CommitSettingsFile(configuration, expected, next);

    /// <summary>
    /// The name of a <c>JASON_</c> environment variable that sets this path or anything under it, as the environment
    /// spells it, or null where none does. The variables are read after the file, so what one says is what the runtime
    /// works from whatever the file says: a write there would answer success for a setting that is not the one in force.
    /// Matched without regard to case, as the configuration matches keys; of several, the first by ordinal order.
    /// </summary>
    public string? Shadowing(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!(configuration is IConfigurationRoot root
            && root.Providers.OfType<EnvironmentVariablesConfigurationProvider>()
                .Any(provider => provider.TryGet(path, out _) || provider.GetChildKeys([], path).Any())))
        {
            return null;
        }

        return Environment.GetEnvironmentVariables().Keys.OfType<string>()
            .Where(name => name.StartsWith(environmentPrefix, StringComparison.OrdinalIgnoreCase)
                && AtOrUnder(name[environmentPrefix.Length..].Replace("__", ":", StringComparison.Ordinal), path))
            .Order(StringComparer.Ordinal)
            .FirstOrDefault()
            ?? environmentPrefix + path.Replace(":", "__", StringComparison.Ordinal);
    }

    private static bool AtOrUnder(string key, string path) =>
        key.StartsWith(path, StringComparison.OrdinalIgnoreCase) && (key.Length == path.Length || key[path.Length] == ':');
}
