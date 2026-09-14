using System.Globalization;
using Jason.Contracts.Plugins;

namespace Jason.Runtime.Plugins.Invocation;

/// <summary>
/// The whole command line of a plugin-host child, and nothing else ever. Four public values say what is running,
/// so a process listing explains itself, while the input, the binding and every grant travel on stdin — argv is
/// readable by every process on the machine, and what a plugin was asked to do is not public.
/// </summary>
public static class PluginHostCommand
{
    /// <summary>The mode word, as the executable's router reads it.</summary>
    public const string Mode = "plugin-host";

    public static IReadOnlyList<string> Build(IPluginHostLocator locator, string pluginId, string operation, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        return
        [
            .. locator.Command,
            Mode,
            "--protocol",
            PluginProtocol.CurrentVersion.ToString(CultureInfo.InvariantCulture),
            "--plugin",
            pluginId,
            "--operation",
            operation,
            "--correlation",
            correlationId,
        ];
    }
}
