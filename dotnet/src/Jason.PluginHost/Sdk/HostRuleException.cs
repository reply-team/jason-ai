using System.Text.Json.Nodes;

namespace Jason.PluginHost.Sdk;

/// <summary>
/// A rule of the Host SDK that the plugin broke: an ungranted capability, an executable it may not start, a
/// budget it has spent. It is a .NET exception thrown out of a host function, which JavaScript cannot catch, and
/// that is the point — a plugin must not be able to probe what it was granted and quietly fall back. The runner
/// turns it into a failed outcome with this code.
/// </summary>
public sealed class HostRuleException(string code, string message, JsonNode? details = null) : Exception(message)
{
    public string Code { get; } = code;

    public JsonNode? Details { get; } = details;
}
