using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Contracts.Plugins;
using Jint;
using Jint.Native;
using Jint.Native.Object;

namespace Jason.PluginHost.Sdk;

/// <summary>
/// <c>host.fail</c>: the one way a plugin says "this did not work, and here is what kind of not working it was".
/// It hands back an ordinary JavaScript error for the plugin to throw, so the failure travels the language's own
/// way — out through <c>finally</c> blocks, out of a rejected promise — and the host reads the class off it.
/// </summary>
public static partial class HostFailure
{
    /// <summary>The name that marks an error as ours. A plugin's own errors are exceptions, not failures.</summary>
    public const string Name = "JasonFailure";

    public const string Function = "host.fail";

    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal) { "class", "code", "message", "details", "external_ids" };

    public static JsValue Create(Engine engine, JsValue[] args)
    {
        ArgumentNullException.ThrowIfNull(engine);

        var specification = ArgumentReader.Options(engine, args, Function, Allowed);
        var failureClass = ReadClass(engine, specification);
        var code = ArgumentReader.RequiredString(engine, specification, "code", Function, PluginProtocol.MaxErrorCodeLength);
        if (!CodePattern().IsMatch(code))
        {
            throw ArgumentReader.TypeError(engine, $"{Function}: 'code' must be lower snake_case, like 'rate_limited'.");
        }

        var message = ArgumentReader.RequiredString(engine, specification, "message", Function, PluginProtocol.MaxErrorMessageLength);
        var details = ReadDetails(engine, specification);
        var externalIds = ReadExternalIds(engine, specification);

        var error = engine.Intrinsics.Error.Construct(message);
        error.FastSetDataProperty("name", Name);
        error.FastSetDataProperty("class", failureClass);
        error.FastSetDataProperty("code", code);
        error.FastSetDataProperty("details", JsJson.FromJson(engine, details));
        error.FastSetDataProperty("external_ids", JsJson.FromJson(engine, externalIds));
        return error;
    }

    /// <summary>
    /// Whether what the plugin threw is one of ours. Everything is checked again here rather than trusted:
    /// nothing stops a plugin from building an object that calls itself a failure, and a class the runtime does
    /// not know would be worse than an honest <c>plugin_exception</c>.
    /// </summary>
    public static bool TryRead(Engine engine, JsValue thrown, out OutcomeError error)
    {
        ArgumentNullException.ThrowIfNull(engine);
        error = null!;

        if (thrown is not ObjectInstance instance)
        {
            return false;
        }

        var name = instance.Get("name");
        if (!name.IsString() || !string.Equals(name.AsString(), Name, StringComparison.Ordinal))
        {
            return false;
        }

        var failureClass = instance.Get("class");
        var code = instance.Get("code");
        var message = instance.Get("message");
        if (!failureClass.IsString() || !TryParseClass(failureClass.AsString(), out var parsed)
            || !code.IsString() || !CodePattern().IsMatch(code.AsString())
            || !message.IsString())
        {
            return false;
        }

        var details = JsJson.ToJson(engine, instance.Get("details"));
        if (details is not null && Bytes(details) > PluginProtocol.MaxDetailsBytes)
        {
            details = null;
        }

        TryReadExternalIds(JsJson.ToJson(engine, instance.Get("external_ids")), out var externalIds);

        var text = message.AsString();
        error = new OutcomeError(
            parsed,
            code.AsString(),
            text.Length > PluginProtocol.MaxErrorMessageLength ? text[..PluginProtocol.MaxErrorMessageLength] : text,
            details,
            externalIds);
        return true;
    }

    /// <summary>
    /// The identifiers a provider answered with: an object of short strings, keyed by the provider's own names.
    /// Null or absent is fine; anything else is not identifiers.
    /// </summary>
    public static bool TryReadExternalIds(JsonNode? node, out JsonObject? externalIds)
    {
        externalIds = null;
        if (node is null)
        {
            return true;
        }

        if (node is not JsonObject map || map.Count > PluginProtocol.MaxExternalIds)
        {
            return false;
        }

        foreach (var (_, value) in map)
        {
            if (value is null || value.GetValueKind() != JsonValueKind.String || value.GetValue<string>().Length > PluginProtocol.MaxExternalIdLength)
            {
                return false;
            }
        }

        externalIds = map;
        return true;
    }

    public static bool TryParseClass(string value, out FailureClass failureClass)
    {
        switch (value)
        {
            case "transient":
                failureClass = FailureClass.Transient;
                return true;
            case "permanent":
                failureClass = FailureClass.Permanent;
                return true;
            case "validation":
                failureClass = FailureClass.Validation;
                return true;
            case "ambiguous":
                failureClass = FailureClass.Ambiguous;
                return true;
            default:
                failureClass = FailureClass.Permanent;
                return false;
        }
    }

    private static string ReadClass(Engine engine, JsonObject specification)
    {
        var value = ArgumentReader.RequiredString(engine, specification, "class", Function, 32);
        return TryParseClass(value, out _)
            ? value
            : throw ArgumentReader.TypeError(engine, $"{Function}: 'class' must be transient, permanent, validation or ambiguous.");
    }

    private static JsonNode? ReadDetails(Engine engine, JsonObject specification)
    {
        if (!specification.TryGetPropertyValue("details", out var details) || details is null)
        {
            return null;
        }

        return Bytes(details) > PluginProtocol.MaxDetailsBytes
            ? throw ArgumentReader.TypeError(
                engine,
                string.Create(CultureInfo.InvariantCulture, $"{Function}: 'details' is larger than {PluginProtocol.MaxDetailsBytes} bytes."))
            : details.DeepClone();
    }

    private static JsonObject? ReadExternalIds(Engine engine, JsonObject specification)
    {
        if (!specification.TryGetPropertyValue("external_ids", out var node) || node is null)
        {
            return null;
        }

        if (!TryReadExternalIds(node.DeepClone(), out var externalIds))
        {
            throw ArgumentReader.TypeError(
                engine,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{Function}: 'external_ids' must be an object of at most {PluginProtocol.MaxExternalIds} strings of at most {PluginProtocol.MaxExternalIdLength} characters."));
        }

        return externalIds;
    }

    private static int Bytes(JsonNode node) => Encoding.UTF8.GetByteCount(node.ToJsonString());

    [GeneratedRegex(@"^[a-z][a-z0-9_]{0,63}\z")]
    private static partial Regex CodePattern();
}
