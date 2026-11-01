using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins;

/// <summary>
/// A child of the test's own choosing in place of the plugin host. The invoker appends the mode word and the
/// four protocol values to whatever this names, and the stand-in ignores them — which is how a test provokes
/// the answers a correct host can never give: no outcome, two outcomes, a flood, the wrong exit code.
/// </summary>
public sealed class CommandLocator(params string[] command) : IPluginHostLocator
{
    public IReadOnlyList<string> Command { get; } = command;
}
