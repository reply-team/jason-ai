using System.Globalization;
using System.Text.Json.Nodes;

namespace Jason.PluginHost.Sdk;

/// <summary>
/// How many times one invocation may reach outside itself. It also answers the question the timeout class rule
/// asks: did anything leave this process before the clock ran out? If it did, a timeout is ambiguous, because
/// the provider may already have acted.
/// </summary>
public sealed class CallBudget(int maxExec, int maxHttp)
{
    public int ExecCalls { get; private set; }

    public int HttpCalls { get; private set; }

    /// <summary>Every call that was started, whatever came of it — the reason a timeout may be ambiguous.</summary>
    public int ExternalCallsStarted => ExecCalls + HttpCalls;

    public void Exec()
    {
        if (ExecCalls >= maxExec)
        {
            throw new HostRuleException(
                OutcomeCodes.ExecLimit,
                string.Create(CultureInfo.InvariantCulture, $"An invocation may start at most {maxExec} programs."),
                new JsonObject { ["limit"] = "exec.max_calls", ["max"] = maxExec });
        }

        ExecCalls++;
    }

    public void Http()
    {
        if (HttpCalls >= maxHttp)
        {
            throw new HostRuleException(
                OutcomeCodes.HttpLimit,
                string.Create(CultureInfo.InvariantCulture, $"An invocation may make at most {maxHttp} HTTP requests."),
                new JsonObject { ["limit"] = "http.max_calls", ["max"] = maxHttp });
        }

        HttpCalls++;
    }
}
