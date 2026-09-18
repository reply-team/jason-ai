using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Execution;

namespace Jason.FakeAgentHost;

/// <summary>
/// The repertoire. Each behaviour is one way an agent host can end — the good one, and every bad one the
/// dispatcher has to survive: a host that dies mid-run, one that never reports, one that goes quiet, one that
/// reports twice, one that spills the token into its own output.
/// </summary>
internal static class Behaviours
{
    public const int Success = 0;
    public const int UnknownBehaviour = 2;
    public const int Crashed = 3;
    public const int UnexpectedAnswer = 2;

    /// <summary>How far a chain of <c>script</c> files may point at further <c>script</c> files before it is a loop.</summary>
    private const int MaxScriptDepth = 8;

    private static readonly TimeSpan HeartbeatSpacing = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Resolves what will actually run, following <c>script &lt;file&gt;</c> to the behaviour named in the file.
    /// Done before the host says anything, so its one diagnostic line names the behaviour that ran.
    /// </summary>
    public static (string Behaviour, IReadOnlyList<string> Options) Resolve(IReadOnlyList<string> arguments)
    {
        var current = arguments;
        for (var depth = 0; depth <= MaxScriptDepth; depth++)
        {
            if (current.Count == 0)
            {
                return (string.Empty, []);
            }

            if (!string.Equals(current[0], "script", StringComparison.Ordinal) || current.Count < 2)
            {
                return (current[0], [.. current.Skip(1)]);
            }

            string[] named;
            try
            {
                named = File.ReadAllText(current[1]).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                return ("script", [.. current.Skip(1)]);
            }

            current = named;
        }

        return ("script", []);
    }

    /// <summary>The names this host answers to, so a caller can tell whether argv named one of them.</summary>
    public static bool Known(string behaviour) =>
        behaviour is "succeed" or "hang" or "mute" or "crash" or "silent" or "stale" or "leak" or "echo-envelope";

    /// <summary>
    /// The behaviour a launched host is told to perform through its brief rather than its arguments. A host
    /// started through an execution profile does not choose what is on its command line — the runtime composes
    /// that from the profile and its own constants — so a test that needs a particular behaviour says so in the
    /// work item's context, which is where everything else about the job already travels.
    /// </summary>
    public static (string Behaviour, IReadOnlyList<string> Options) FromContext(LaunchEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.Context["behaviour"]?.GetValue<string>() is not { } named)
        {
            return (string.Empty, []);
        }

        var parts = named.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? (string.Empty, []) : (parts[0], [.. parts.Skip(1)]);
    }

    public static async Task<int> RunAsync(string behaviour, IReadOnlyList<string> options, LaunchEnvelope envelope, string rawEnvelope, RuntimeApi api)
    {
        switch (behaviour)
        {
            case "succeed":
                return await SucceedAsync(options, envelope, api).ConfigureAwait(false);
            case "hang":
                return await HangAsync(envelope, api).ConfigureAwait(false);
            case "mute":
                return await MuteAsync().ConfigureAwait(false);
            case "crash":
                return await CrashAsync(envelope, api).ConfigureAwait(false);
            case "silent":
                return Success;
            case "stale":
                return await StaleAsync(envelope, api).ConfigureAwait(false);
            case "leak":
                return await LeakAsync(envelope, api).ConfigureAwait(false);
            case "echo-envelope":
                return await EchoEnvelopeAsync(rawEnvelope).ConfigureAwait(false);
            default:
                await Diagnostics.WriteAsync($"unknown behaviour '{behaviour}'").ConfigureAwait(false);
                return UnknownBehaviour;
        }
    }

    /// <summary>The whole happy path: progress, proof of life, a result.</summary>
    private static async Task<int> SucceedAsync(IReadOnlyList<string> options, LaunchEnvelope envelope, RuntimeApi api)
    {
        var result = ParseResult(Option(options, "--result")) ?? new JsonObject { ["summary"] = "done", ["attempt"] = envelope.AttemptNumber };
        var heartbeats = int.TryParse(Option(options, "--heartbeats"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ? count : 1;

        await SetHalfDoneAsync(envelope, api).ConfigureAwait(false);

        for (var beat = 0; beat < heartbeats; beat++)
        {
            await Task.Delay(HeartbeatSpacing).ConfigureAwait(false);
            await api.CallAsync(Operations.WorkItemHeartbeat, new WorkItemHeartbeatRequest(envelope.WorkItemId, envelope.AttemptId)).ConfigureAwait(false);
        }

        await CompleteAsync(envelope, api, result).ConfigureAwait(false);
        return Success;
    }

    /// <summary>Alive and reporting it, forever: the lease holds, the work never ends. Only a kill stops this.</summary>
    private static async Task<int> HangAsync(LaunchEnvelope envelope, RuntimeApi api)
    {
        var spacing = TimeSpan.FromSeconds(Math.Max(1, envelope.HeartbeatSeconds / 4));
        while (true)
        {
            await api.CallAsync(Operations.WorkItemHeartbeat, new WorkItemHeartbeatRequest(envelope.WorkItemId, envelope.AttemptId)).ConfigureAwait(false);
            await Task.Delay(spacing).ConfigureAwait(false);
        }
    }

    /// <summary>Started and then nothing: no heartbeat, no result. This is what a missed heartbeat looks like.</summary>
    private static async Task<int> MuteAsync()
    {
        await Task.Delay(Timeout.Infinite).ConfigureAwait(false);
        return Success;
    }

    /// <summary>Half the work reported, then gone — the exit code is all the runtime gets.</summary>
    private static async Task<int> CrashAsync(LaunchEnvelope envelope, RuntimeApi api)
    {
        await SetHalfDoneAsync(envelope, api).ConfigureAwait(false);
        return Crashed;
    }

    /// <summary>Reports twice. The second report must be refused by the fencing, or the fencing is not there.</summary>
    private static async Task<int> StaleAsync(LaunchEnvelope envelope, RuntimeApi api)
    {
        await CompleteAsync(envelope, api, new JsonObject { ["summary"] = "done" }).ConfigureAwait(false);

        var (status, body) = await CompleteAsync(envelope, api, new JsonObject { ["summary"] = "done again" }).ConfigureAwait(false);
        if (status == 409 && body.Contains("stale_attempt", StringComparison.Ordinal))
        {
            await Diagnostics.WriteAsync("stale_attempt observed").ConfigureAwait(false);
            return Success;
        }

        await Diagnostics.WriteAsync($"expected 409 stale_attempt, got {status.ToString(CultureInfo.InvariantCulture)}").ConfigureAwait(false);
        return UnexpectedAnswer;
    }

    /// <summary>A badly written host that echoes its credentials. Nothing it writes may reach a stored artifact.</summary>
    private static async Task<int> LeakAsync(LaunchEnvelope envelope, RuntimeApi api)
    {
        var token = api.ReadDescriptor()?.Token ?? "n/a";
        await Console.Out.WriteLineAsync($"token={token}").ConfigureAwait(false);
        await Diagnostics.WriteAsync($"token={token}").ConfigureAwait(false);
        await CompleteAsync(envelope, api, new JsonObject { ["summary"] = "done" }).ConfigureAwait(false);
        return Success;
    }

    /// <summary>Writes back exactly what it was handed, so a test can assert on the envelope the launcher composed.</summary>
    private static async Task<int> EchoEnvelopeAsync(string rawEnvelope)
    {
        await Console.Out.WriteLineAsync(rawEnvelope.Trim()).ConfigureAwait(false);
        return Success;
    }

    private static Task<(int Status, string Body)> SetHalfDoneAsync(LaunchEnvelope envelope, RuntimeApi api) =>
        api.CallAsync(Operations.WorkItemSetResult, new WorkItemSetResultRequest(envelope.WorkItemId, envelope.AttemptId, new JsonObject { ["progress"] = "half" }));

    /// <summary>
    /// Finishing, and saying so when the runtime refuses to let it finish. A host told its answer is not the
    /// shape that was asked for and then exiting silently would leave an attempt that ended for no visible
    /// reason; a real one would try again, and this one at least says what it was told.
    /// </summary>
    private static async Task<(int Status, string Body)> CompleteAsync(LaunchEnvelope envelope, RuntimeApi api, JsonNode? result)
    {
        var answer = await api.CallAsync(
            Operations.WorkItemComplete,
            new WorkItemCompleteRequest(envelope.WorkItemId, envelope.AttemptId, CompletionStatus.Succeeded, result, null, null))
            .ConfigureAwait(false);

        if (answer.Status != 200)
        {
            await Diagnostics.WriteAsync($"complete refused with {answer.Status.ToString(CultureInfo.InvariantCulture)}: {answer.Body}").ConfigureAwait(false);
        }

        return answer;
    }

    private static JsonNode? ParseResult(string? json)
    {
        try
        {
            return json is null ? null : JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Option(IReadOnlyList<string> options, string name)
    {
        for (var index = 0; index < options.Count - 1; index++)
        {
            if (string.Equals(options[index], name, StringComparison.Ordinal))
            {
                return options[index + 1];
            }
        }

        return null;
    }
}
