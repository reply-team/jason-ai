using System.Text.Json.Nodes;
using Jint;
using Jint.Native;
using Jint.Native.Json;

namespace Jason.PluginHost.Sdk;

/// <summary>
/// The one bridge between the runtime's JSON and the engine's values, and it is deliberately narrow: JSON in,
/// JSON out, through the engine's own parser and serialiser. Nothing crosses as a CLR object, so no .NET type
/// ever becomes reachable from a plugin, and what the plugin sees is exactly what the protocol carried.
/// </summary>
public static class JsJson
{
    public static JsValue FromJson(Engine engine, JsonNode? node)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return new JsonParser(engine).Parse(node?.ToJsonString() ?? "null");
    }

    /// <summary>
    /// The JSON a value serialises to, or null when it has none — <c>undefined</c>, a function, a symbol. The
    /// engine's serialiser is used rather than a CLR conversion so that <c>toJSON</c> and the JSON grammar
    /// behave exactly as they would inside the plugin.
    /// </summary>
    public static JsonNode? ToJson(Engine engine, JsValue value)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (value is null || value.IsUndefined() || value.IsNull())
        {
            return null;
        }

        var serialised = new JsonSerializer(engine).Serialize(value);
        return serialised.IsUndefined() ? null : JsonNode.Parse(serialised.AsString());
    }
}
