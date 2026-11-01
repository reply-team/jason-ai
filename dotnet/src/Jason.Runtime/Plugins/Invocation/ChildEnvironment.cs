using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Registry;

namespace Jason.Runtime.Plugins.Invocation;

/// <summary>
/// The environment a plugin-host child is given: built from nothing, filled with the machine configuration any
/// program needs and with the variables this plugin was granted, and never with a <c>JASON_</c> name. A child
/// that dumps its own environment therefore reveals nothing about the runtime that started it — not even where
/// the data directory is. The same call resolves the environment a version check runs under, so what a reload
/// proved about a program is what an invocation gets.
/// </summary>
public static class ChildEnvironment
{
    public static Dictionary<string, string> Build(IEnumerable<string> grantedVariables) =>
        BaseEnvironment.Build(PluginLoader.CurrentEnvironment(), grantedVariables);
}
