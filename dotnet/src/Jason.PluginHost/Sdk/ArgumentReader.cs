using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jint;
using Jint.Native;
using Jint.Runtime;

namespace Jason.PluginHost.Sdk;

/// <summary>
/// How every host function reads its one argument: strictly. An unknown key is a mistake the plugin author
/// wants to hear about at once — silently ignoring <c>timeoutMs</c> next to <c>timeout_ms</c> would make a
/// capped call look uncapped — so it is a <c>TypeError</c> the plugin can catch and fix, not a failed
/// invocation. A key whose value is <c>undefined</c> is absent: that is what JSON says, and what a plugin
/// building a request object means.
/// </summary>
public static class ArgumentReader
{
    /// <summary>A JavaScript <c>TypeError</c>, thrown where the plugin stands so it can be caught and read.</summary>
    public static JavaScriptException TypeError(Engine engine, string message)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return new JavaScriptException(engine.Intrinsics.TypeError, message);
    }

    public static JsonObject Options(Engine engine, JsValue[] args, string function, IReadOnlySet<string> allowed)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(allowed);

        if (args.Length == 0 || args[0].IsUndefined() || args[0].IsNull() || !args[0].IsObject() || args[0].IsArray())
        {
            throw TypeError(engine, $"{function}: one options object is required.");
        }

        if (JsJson.ToJson(engine, args[0]) is not JsonObject options)
        {
            throw TypeError(engine, $"{function}: the options must be a plain object.");
        }

        foreach (var (key, _) in options)
        {
            if (!allowed.Contains(key))
            {
                throw TypeError(engine, $"{function}: '{key}' is not one of {string.Join(", ", allowed.Order(StringComparer.Ordinal))}.");
            }
        }

        return options;
    }

    public static string RequiredString(Engine engine, JsonObject options, string key, string function, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(options);
        return OptionalString(engine, options, key, function, maxLength)
            ?? throw TypeError(engine, $"{function}: '{key}' is required and must be a string.");
    }

    public static string? OptionalString(Engine engine, JsonObject options, string key, string function, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.TryGetPropertyValue(key, out var node) || node is null)
        {
            return null;
        }

        if (node.GetValueKind() != JsonValueKind.String)
        {
            throw TypeError(engine, $"{function}: '{key}' must be a string.");
        }

        var value = node.GetValue<string>();
        return value.Length > maxLength
            ? throw TypeError(engine, string.Create(CultureInfo.InvariantCulture, $"{function}: '{key}' is longer than {maxLength} characters."))
            : value;
    }

    public static int? OptionalInt(Engine engine, JsonObject options, string key, string function, int min, int max)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.TryGetPropertyValue(key, out var node) || node is null)
        {
            return null;
        }

        if (node.GetValueKind() != JsonValueKind.Number || !node.AsValue().TryGetValue<double>(out var number) || number != Math.Floor(number))
        {
            throw TypeError(engine, $"{function}: '{key}' must be a whole number.");
        }

        return number < min || number > max
            ? throw TypeError(engine, string.Create(CultureInfo.InvariantCulture, $"{function}: '{key}' must be between {min} and {max}."))
            : (int)number;
    }

    /// <summary>An optional array of strings, bounded in count and in the bytes it adds up to.</summary>
    public static IReadOnlyList<string> OptionalStrings(Engine engine, JsonObject options, string key, string function, int maxCount, int maxTotalBytes)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.TryGetPropertyValue(key, out var node) || node is null)
        {
            return [];
        }

        if (node is not JsonArray array)
        {
            throw TypeError(engine, $"{function}: '{key}' must be an array of strings.");
        }

        if (array.Count > maxCount)
        {
            throw TypeError(engine, string.Create(CultureInfo.InvariantCulture, $"{function}: '{key}' holds at most {maxCount} entries."));
        }

        var values = new List<string>(array.Count);
        var bytes = 0L;
        foreach (var entry in array)
        {
            if (entry is null || entry.GetValueKind() != JsonValueKind.String)
            {
                throw TypeError(engine, $"{function}: every entry of '{key}' must be a string.");
            }

            var value = entry.GetValue<string>();
            bytes += System.Text.Encoding.UTF8.GetByteCount(value);
            if (bytes > maxTotalBytes)
            {
                throw TypeError(engine, string.Create(CultureInfo.InvariantCulture, $"{function}: '{key}' is larger than {maxTotalBytes} bytes."));
            }

            values.Add(value);
        }

        return values;
    }

    /// <summary>An optional object of string values — an environment, a set of headers — bounded the same way.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> OptionalStringMap(
        Engine engine,
        JsonObject options,
        string key,
        string function,
        int maxCount,
        int maxValueBytes)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.TryGetPropertyValue(key, out var node) || node is null)
        {
            return [];
        }

        if (node is not JsonObject map)
        {
            throw TypeError(engine, $"{function}: '{key}' must be an object of string values.");
        }

        if (map.Count > maxCount)
        {
            throw TypeError(engine, string.Create(CultureInfo.InvariantCulture, $"{function}: '{key}' holds at most {maxCount} entries."));
        }

        var entries = new List<KeyValuePair<string, string>>(map.Count);
        foreach (var (name, value) in map)
        {
            if (value is null || value.GetValueKind() != JsonValueKind.String)
            {
                throw TypeError(engine, $"{function}: every value of '{key}' must be a string.");
            }

            var text = value.GetValue<string>();
            if (System.Text.Encoding.UTF8.GetByteCount(text) > maxValueBytes)
            {
                throw TypeError(engine, string.Create(CultureInfo.InvariantCulture, $"{function}: '{name}' is larger than {maxValueBytes} bytes."));
            }

            entries.Add(new KeyValuePair<string, string>(name, text));
        }

        return entries;
    }
}
