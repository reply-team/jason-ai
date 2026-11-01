using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins;

/// <summary>
/// The plugin host with a kill that never comes back, started where the shipped executable would be: the same mode,
/// runner and host functions, so what the runtime meets is a real host that will not exit before its kill has ended.
/// </summary>
public sealed class StuckKillHostLocator : IPluginHostLocator
{
    /// <summary>The line the stand-in writes on stderr as it starts, with its own process id, for a test to clean up by.</summary>
    public const string StartedMessage = "stuck_kill_host_started";

    /// <summary>The process the stand-in runs in: the .NET host it is started through.</summary>
    public const string ProcessName = "dotnet";

    public IReadOnlyList<string> Command => ["dotnet", Path.Combine(AppContext.BaseDirectory, "Jason.StuckKillPluginHost.dll")];
}
