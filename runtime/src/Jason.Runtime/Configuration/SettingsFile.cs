using Microsoft.Extensions.Configuration;

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
}
