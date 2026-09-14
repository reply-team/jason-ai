using Jint;
using Jint.Native;
using Jint.Runtime.Descriptors;
using Jint.Runtime.Interop;

namespace Jason.PluginHost.Sdk;

/// <summary>
/// The <c>host</c> object, built by hand rather than wrapped around a CLR instance. That is the whole point:
/// a wrapped object would carry a type, a constructor and a member surface a plugin could walk; this one has
/// five functions, no prototype of its own and nothing that can be added to it or replaced.
/// </summary>
public static class HostObject
{
    public const string Name = "host";

    public static JsObject Build(
        Engine engine,
        HostServices services,
        ExecService exec,
        HttpService http,
        EnvService env,
        LogService log)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(exec);
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(log);

        var host = new JsObject(engine);
        Add(engine, host, "exec", 1, (_, args) => exec.Invoke(engine, args));
        Add(engine, host, "http", 1, (_, args) => http.Invoke(engine, args));
        Add(engine, host, "env", 1, (_, args) => env.Read(engine, args));
        Add(engine, host, "log", 2, (_, args) => log.Log(engine, args));
        Add(engine, host, "fail", 1, (_, args) => HostFailure.Create(engine, args));
        host.PreventExtensions();
        return host;
    }

    private static void Add(Engine engine, JsObject host, string name, int length, Func<JsValue, JsValue[], JsValue> function) =>
        host.FastSetProperty(
            name,
            new PropertyDescriptor(
                new ClrFunction(engine, name, function, length, PropertyFlag.Configurable),
                writable: false,
                enumerable: true,
                configurable: false));
}
