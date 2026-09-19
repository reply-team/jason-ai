using Jason.Contracts.Discovery;
using Jason.Runtime.Configuration;

namespace Jason.Runtime.Tests;

/// <summary>
/// The environment a spawned test runtime is started with: its own data directory, and no update check. One
/// helper for the eight places that spawn one, because a variable spelled by hand at each is one the ninth
/// forgets — and the check is the one thing a runtime does that reaches past the machine without being asked.
/// </summary>
/// <remarks>
/// The switch travels as <c>JASON_Update__CheckEnabled</c>: the runtime reads <c>JASON_</c>-prefixed variables
/// into its configuration, and the double underscore is how a nested key is spelled there. That the flag reaches
/// the child through that mapping is the configuration system's contract rather than something a test here can
/// watch happen inside another process; the in-process half of the same guarantee is asserted on the fixture.
/// </remarks>
public static class TestRuntimeEnvironment
{
    /// <summary>Built from the names the runtime binds, so a renamed section moves this with it — and the test that pins the literal says so.</summary>
    public static readonly string CheckEnabledVariable =
        JasonConfiguration.EnvironmentPrefix + UpdateOptions.Section + "__" + nameof(UpdateOptions.CheckEnabled);

    public static void Offline(IDictionary<string, string?> environment, string dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(environment);
        environment[JasonPaths.DataDirectoryVariable] = dataDirectory;
        environment[CheckEnabledVariable] = "false";
    }
}
