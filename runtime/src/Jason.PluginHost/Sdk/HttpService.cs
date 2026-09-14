using Jint;
using Jint.Native;

namespace Jason.PluginHost.Sdk;

/// <summary>
/// <c>host.http</c>: one narrow request to a host the manifest declared and the user granted, over HTTPS or
/// loopback, with no redirects, no cookies and nothing the runtime adds of its own.
/// </summary>
public sealed class HttpService(HostServices services) : IDisposable
{
    public const string Function = "host.http";

    public static IReadOnlySet<string> Allowed { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "method", "url", "headers", "body", "timeout_ms" };

    public JsValue Invoke(Engine engine, JsValue[] args)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _ = services;
        _ = ArgumentReader.Options(engine, args, Function, Allowed);
        throw new NotSupportedException("host.http is not wired in this build.");
    }

    public void Dispose()
    {
    }
}
