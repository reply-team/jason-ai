using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;

namespace Jason.Runtime.Configuration;

/// <summary>
/// The user's settings file, for an act that has to work from it as it stands rather than from what its watch has told
/// the runtime so far. A service, because the configuration it reads is the runtime's own root, which the host's
/// services do not otherwise see.
/// </summary>
public sealed class SettingsFile(IConfiguration configuration)
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
    /// Whether a <c>JASON_</c> environment variable sets this path or anything under it. The variables are read after the
    /// file, so what one says is what the runtime works from whatever the file says: a write there would answer success
    /// for a setting that is not the one in force. Matched without regard to case, as the configuration matches keys.
    /// </summary>
    public bool Shadowed(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return configuration is IConfigurationRoot root
            && root.Providers.OfType<EnvironmentVariablesConfigurationProvider>()
                .Any(provider => provider.TryGet(path, out _) || provider.GetChildKeys([], path).Any());
    }
}
