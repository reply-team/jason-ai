using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;

namespace Jason.PluginHost.Sdk;

/// <summary>
/// How many times one invocation may reach outside itself. It also answers the question the timeout class rule
/// asks: did anything leave this process before the clock ran out? If it did, a timeout is ambiguous, because
/// the provider may already have acted.
/// </summary>
public sealed class CallBudget(int maxExec, int maxHttp, int maxApi = 0, int maxFiles = 0)
{
    public int ExecCalls { get; private set; }

    public int HttpCalls { get; private set; }

    /// <summary>The Runtime API calls made through the door; an invocation without one has none to make.</summary>
    public int ApiCalls { get; private set; }

    /// <summary>The appends started through the file door: a line may be on the disk whatever became of the call.</summary>
    public int FileCalls { get; private set; }

    /// <summary>The calls that reach past this machine's disk: a repeat of the invocation would make them again.</summary>
    public int ActingCallsStarted => ExecCalls + HttpCalls + ApiCalls;

    /// <summary>Every call that was started, whatever came of it — the reason a timeout may be ambiguous.</summary>
    public int ExternalCallsStarted => ActingCallsStarted + FileCalls;

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

    public void Files()
    {
        if (FileCalls >= maxFiles)
        {
            throw new HostRuleException(
                HostOutcomeCodes.FileLimit,
                string.Create(CultureInfo.InvariantCulture, $"An invocation may append at most {maxFiles} lines."),
                new JsonObject { ["limit"] = "files.max_calls", ["max"] = maxFiles });
        }

        FileCalls++;
    }
}
