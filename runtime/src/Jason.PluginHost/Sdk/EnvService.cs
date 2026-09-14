using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;
using Jint;
using Jint.Native;

namespace Jason.PluginHost.Sdk;

/// <summary>
/// <c>host.env(name)</c>: the value of a variable the manifest declared and the user granted, read from this
/// process's own environment — the only place such a value exists, since no secret ever travels in the envelope.
/// Anything else reads as <c>undefined</c> rather than as an error, but the attempt is recorded, so a plugin
/// probing for what it was not given is visible to whoever reads the log.
/// </summary>
public sealed class EnvService(HostServices services)
{
    public const string Function = "host.env";

    /// <summary>What a probe for an ungranted variable is called on stderr. Never its value: there is none to log.</summary>
    public const string ProbeMessage = "env_probe";

    public JsValue Read(Engine engine, JsValue[] args)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0 || !args[0].IsString())
        {
            throw ArgumentReader.TypeError(engine, $"{Function}: the variable name must be a string.");
        }

        var name = args[0].AsString();
        var granted = services.Grants.Env?.Variables ?? [];

        // A reserved name is never readable, whatever a grant says. Manifest validation already refuses to
        // declare one, and the runtime never puts one in the child's environment; this is the same rule kept in
        // the one other place that could hand one back.
        if (name.StartsWith(BaseEnvironment.ReservedPrefix, StringComparison.OrdinalIgnoreCase)
            || !granted.Contains(name, StringComparer.Ordinal))
        {
            services.Diagnostics.Host("debug", ProbeMessage, new JsonObject { ["variable"] = name });
            return JsValue.Undefined;
        }

        var value = Environment.GetEnvironmentVariable(name);
        return value is null ? JsValue.Undefined : value;
    }
}
