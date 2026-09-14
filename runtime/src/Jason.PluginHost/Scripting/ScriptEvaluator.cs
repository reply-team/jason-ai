using Jint;

namespace Jason.PluginHost.Scripting;

/// <summary>Bootstrap check that the embedded JavaScript engine works on this platform with bounded execution.</summary>
public static class ScriptEvaluator
{
    public static object? Evaluate(string script)
    {
        ArgumentNullException.ThrowIfNull(script);
        var engine = new Engine(options =>
        {
            options.LimitRecursion(64);
            options.MaxStatements(100_000);
            options.TimeoutInterval(TimeSpan.FromSeconds(2));
        });
        return engine.Evaluate(script).ToObject();
    }
}
