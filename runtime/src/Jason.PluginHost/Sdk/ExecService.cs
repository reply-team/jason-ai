using Jint;
using Jint.Native;

namespace Jason.PluginHost.Sdk;

/// <summary>
/// <c>host.exec</c>: starts one of the programs the manifest declared and the user granted, by name, with an
/// argument array and never a shell.
/// </summary>
public sealed class ExecService(HostServices services)
{
    public const string Function = "host.exec";

    public static IReadOnlySet<string> Allowed { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "executable", "args", "stdin", "timeout_ms", "env" };

    public JsValue Invoke(Engine engine, JsValue[] args)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _ = services;
        _ = ArgumentReader.Options(engine, args, Function, Allowed);
        throw new NotSupportedException("host.exec is not wired in this build.");
    }
}
