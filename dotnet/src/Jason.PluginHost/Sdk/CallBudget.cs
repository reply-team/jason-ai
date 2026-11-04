using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;

namespace Jason.PluginHost.Sdk;

/// <summary>
/// How many times one invocation may reach outside itself. It also answers the question the timeout class rule
/// asks: did anything leave this process before the clock ran out? If it did, a timeout is ambiguous, because
/// the provider may already have acted.
/// </summary>
public sealed class CallBudget(int maxExec, int maxHttp, int maxApi = 0)
{
    public int ExecCalls { get; private set; }

    public int HttpCalls { get; private set; }

    /// <summary>The Runtime API calls made through the door; an invocation without one has none to make.</summary>
    public int ApiCalls { get; private set; }

    /// <summary>Every call that was started, whatever came of it — the reason a timeout may be ambiguous.</summary>
    public int ExternalCallsStarted => ExecCalls + HttpCalls + ApiCalls;

    public void Exec()
    {
        if (ExecCalls >= maxExec)
        {
            throw new HostRuleException(
                HostOutcomeCodes.ExecLimit,
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
                HostOutcomeCodes.HttpLimit,
                string.Create(CultureInfo.InvariantCulture, $"An invocation may make at most {maxHttp} HTTP requests."),
                new JsonObject { ["limit"] = "http.max_calls", ["max"] = maxHttp });
        }

        HttpCalls++;
    }

    public void Api()
    {
        if (ApiCalls >= maxApi)
        {
            throw new HostRuleException(
                HostOutcomeCodes.ApiLimit,
                string.Create(CultureInfo.InvariantCulture, $"An invocation may make at most {maxApi} Runtime API calls."),
                new JsonObject { ["limit"] = "api.max_calls", ["max"] = maxApi });
        }

        ApiCalls++;
    }
}
