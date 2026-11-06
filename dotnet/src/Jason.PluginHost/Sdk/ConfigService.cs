using Jint;
using Jint.Native;

namespace Jason.PluginHost.Sdk;

/// <summary>
/// <c>host.config(key)</c>: what the settings say for this plugin under <c>Plugins:Config:&lt;id&gt;</c>, as the runtime froze
/// it at the load and handed it over in the envelope: the text, or <c>null</c> for a key they do not hold. A key is matched
/// as a configuration key is, without regard to case. An absent key is an optional setting's ordinary answer, so nothing
/// is recorded for it, and neither key nor value is ever written anywhere from here.
/// </summary>
public sealed class ConfigService(HostServices services)
{
    public const string Function = "host.config";

    public JsValue Read(Engine engine, JsValue[] args)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length == 0 || !args[0].IsString())
        {
            throw ArgumentReader.TypeError(engine, $"{Function}: the key must be a string.");
        }

        return services.Invocation.Config?.GetValueOrDefault(args[0].AsString()) is { } text ? text : JsValue.Null;
    }
}
