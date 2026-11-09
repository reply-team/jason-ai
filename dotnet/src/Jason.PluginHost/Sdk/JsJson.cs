using System.Globalization;
using System.Text;
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
    private const char Backslash = '\\';
    private const string EscapeStart = "\\u";
    private const string EscapedReplacement = "\\uFFFD";
    private const char Replacement = '\uFFFD';
    private const char FirstSurrogate = '\uD800';
    private const char LastSurrogate = '\uDFFF';

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
        return serialised.IsUndefined() ? null : JsonNode.Parse(WellFormed(serialised.AsString()));
    }

    /// <summary>
    /// The JSON text with every half of a surrogate pair that stands alone replaced by U+FFFD, and every whole pair kept as
    /// written. A string the engine holds may carry such a half, from a plugin that cut a provider's text at a count of
    /// UTF-16 units, and the engine's serialiser spells it as an escape: valid JSON text, but no string .NET can read, so the
    /// first read of it would throw and the invocation would end with no outcome. Every value a plugin hands the host
    /// crosses here — a result, an identifier, a failure's message, details and block, a log line's data, any host
    /// function's options — so this is the one place the half is replaced, keys included.
    /// </summary>
    internal static string WellFormed(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json.AsSpan().IndexOfAnyInRange(FirstSurrogate, LastSurrogate) < 0 && !json.Contains(EscapeStart, StringComparison.Ordinal))
        {
            return json;
        }

        var text = new StringBuilder(json.Length);
        var i = 0;
        while (i < json.Length)
        {
            var c = json[i];
            if (c == Backslash && i + 1 < json.Length)
            {
                if (EscapedUnit(json, i) is { } unit && char.IsSurrogate(unit))
                {
                    if (char.IsHighSurrogate(unit) && EscapedUnit(json, i + 6) is { } next && char.IsLowSurrogate(next))
                    {
                        text.Append(json, i, 12);
                        i += 12;
                    }
                    else
                    {
                        text.Append(EscapedReplacement);
                        i += 6;
                    }
                }
                else
                {
                    // Any other escape, a backslash's own among them, is two characters at least and is kept as written.
                    text.Append(json, i, 2);
                    i += 2;
                }
            }
            else if (char.IsHighSurrogate(c) && i + 1 < json.Length && char.IsLowSurrogate(json[i + 1]))
            {
                text.Append(json, i, 2);
                i += 2;
            }
            else
            {
                text.Append(char.IsSurrogate(c) ? Replacement : c);
                i++;
            }
        }

        return text.ToString();
    }

    /// <summary>The UTF-16 unit a <c>\uXXXX</c> escape at this index spells, or null where there is none.</summary>
    private static char? EscapedUnit(string json, int index)
    {
        if (index + 6 > json.Length || json[index] != Backslash || json[index + 1] != 'u')
        {
            return null;
        }

        return int.TryParse(json.AsSpan(index + 2, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var unit)
            ? (char)unit
            : null;
    }
}
