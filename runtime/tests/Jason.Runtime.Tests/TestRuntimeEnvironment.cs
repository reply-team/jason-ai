using Jason.Contracts.Discovery;
using Jason.Runtime.Configuration;
using Jason.Runtime.Notifications;

namespace Jason.Runtime.Tests;

/// <summary>
/// The environment a spawned test runtime is started with: its own data directory, no update check, no
/// notifications, and a port the OS chooses. One helper for the eight places that spawn one, because a variable
/// spelled by hand at each is one the ninth forgets — and the check is the one thing a runtime does that reaches
/// past the machine without being asked.
/// </summary>
/// <remarks>
/// The switch travels as <c>JASON_Update__CheckEnabled</c>: the runtime reads <c>JASON_</c>-prefixed variables
/// into its configuration, and the double underscore is how a nested key is spelled there. That it really
/// arrives that way is not taken on the configuration system's word —
/// <c>EndToEndTests.A_spawned_runtime_says_in_its_own_log_that_it_will_not_ask_the_release_feed</c> reads the
/// line the child writes about itself, and goes red when this assignment is removed.
/// </remarks>
public static class TestRuntimeEnvironment
{
    /// <summary>Built from the names the runtime binds, so a renamed section moves this with it — and the test that pins the literal says so.</summary>
    public static readonly string CheckEnabledVariable =
        JasonConfiguration.EnvironmentPrefix + UpdateOptions.Section + "__" + nameof(UpdateOptions.CheckEnabled);

    /// <summary>The notifications switch, built the same way: a spawned test runtime notifies nobody.</summary>
    public static readonly string NotificationsVariable =
        JasonConfiguration.EnvironmentPrefix + NotificationsOptions.Section + "__" + nameof(NotificationsOptions.Enabled);

    /// <summary>
    /// The port switch: the default, 52766, is the bookmark's, and a suite that took it would push the person's own
    /// runtime off it, as <see cref="TestRuntimeOptions"/> says for the runtimes started in this process.
    /// </summary>
    public static readonly string PortVariable =
        JasonConfiguration.EnvironmentPrefix + RuntimeOptions.Section + "__" + nameof(RuntimeOptions.Port);

    public static void Offline(IDictionary<string, string?> environment, string dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(environment);
        environment[JasonPaths.DataDirectoryVariable] = dataDirectory;
        environment[CheckEnabledVariable] = "false";
        environment[NotificationsVariable] = "false";
        environment[PortVariable] = "0";
    }
}
