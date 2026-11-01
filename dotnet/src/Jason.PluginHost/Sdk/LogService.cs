using Jint;
using Jint.Native;

namespace Jason.PluginHost.Sdk;

/// <summary>
/// <c>host.log(level, message, data?)</c>: the plugin's only way to say anything at all. There is no
/// <c>console</c>, and stdout belongs to the outcome, so every word a plugin writes goes through here — one JSON
/// line on stderr, redacted, capped and counted.
/// </summary>
public sealed class LogService(HostServices services)
{
    public const string Function = "host.log";

    public static IReadOnlyList<string> Levels { get; } = ["debug", "info", "warn", "error"];

    public JsValue Log(Engine engine, JsValue[] args)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0 || !args[0].IsString() || !Levels.Contains(args[0].AsString(), StringComparer.Ordinal))
        {
            throw ArgumentReader.TypeError(engine, $"{Function}: the level must be one of {string.Join(", ", Levels)}.");
        }

        if (args.Length < 2 || !args[1].IsString())
        {
            throw ArgumentReader.TypeError(engine, $"{Function}: the message must be a string.");
        }

        var data = args.Length > 2 ? JsJson.ToJson(engine, args[2]) : null;
        services.Diagnostics.Plugin(args[0].AsString(), args[1].AsString(), data);
        return JsValue.Undefined;
    }
}
