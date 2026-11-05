using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Plugins.Subscriptions;

namespace Jason.Runtime.Tests.Plugins.Subscriptions;

/// <summary>
/// Subscribers whose handlers answer without a child: each plugin's next invocation is answered by the next answer the
/// test queued for it, and every request is kept. With nothing queued a handler answers the envelope it was handed with
/// one member of its own added, its id naming the line's id and the attempt, so a test reads the order a chain ran in
/// and what each handler was handed from the envelope the last one returned.
/// </summary>
internal sealed class FakeSubscriptionInvoker : ISubscriptionInvoker
{
    private static readonly OutcomeDiagnostics Diagnostics = new(40, 1, 0, 0);

    private readonly ConcurrentDictionary<string, ConcurrentQueue<Func<PluginInvocationRequest, CancellationToken, InvocationOutcome>>> _answers =
        new(StringComparer.Ordinal);

    private readonly ConcurrentQueue<PluginInvocationRequest> _requests = new();

    public IReadOnlyList<PluginInvocationRequest> Requests => [.. _requests];

    /// <summary>The plugins invoked, in order.</summary>
    public IReadOnlyList<string> Invoked => [.. _requests.Select(request => request.PluginId)];

    /// <summary>The plugin's next invocations are answered with these, in order.</summary>
    public void Answer(string plugin, params InvocationOutcome[] outcomes)
    {
        foreach (var outcome in outcomes)
        {
            Answer(plugin, (_, _) => outcome);
        }
    }

    /// <summary>The plugin's next invocation is answered by a function of what it was handed and the token it was given.</summary>
    public void Answer(string plugin, Func<PluginInvocationRequest, CancellationToken, InvocationOutcome> answer) =>
        _answers.GetOrAdd(plugin, _ => new()).Enqueue(answer);

    public Task<PluginInvocationResult> InvokeAsync(PluginInvocationRequest request, CancellationToken cancellationToken)
    {
        _requests.Enqueue(request);
        var outcome = _answers.TryGetValue(request.PluginId, out var queue) && queue.TryDequeue(out var answer)
            ? answer(request, cancellationToken)
            : Marked(request);
        var provenance = new InvocationProvenance(request.PluginId, "1.0.0", "sha256:0", 1, 1, "pin_01JB6K8TQ2W9V4MZ0C3Y7H5NRD", request.CorrelationId, "snp_1");
        return Task.FromResult(new PluginInvocationResult(outcome, provenance, null));
    }

    /// <summary>The envelope handed in, with the plugin's own member added: <c>line id#attempt</c>.</summary>
    public static InvocationOutcome Marked(PluginInvocationRequest request)
    {
        var envelope = (JsonObject)request.Input["envelope"]!.DeepClone();
        envelope[request.PluginId] = string.Create(
            CultureInfo.InvariantCulture,
            $"{request.Input["line"]!["id"]!.GetValue<string>()}#{request.Input["attempt"]!.GetValue<int>()}");
        return Envelope(envelope);
    }

    public static InvocationOutcome Envelope(JsonObject envelope) => Result(new JsonObject { ["envelope"] = envelope });

    public static InvocationOutcome Result(JsonNode? result, JsonObject? externalIds = null) =>
        new InvocationOutcome.Succeeded(result, externalIds, Diagnostics);

    /// <summary>A handler's own failure, with a message no record of the runtime may carry.</summary>
    public static InvocationOutcome Failure(string code, FailureClass failureClass) =>
        new InvocationOutcome.Failed(new OutcomeError(failureClass, code, Sentinel, null, null), Diagnostics);

    public static InvocationOutcome Protocol(string code) => new InvocationOutcome.ProtocolFailure(code, Sentinel, null, null);

    /// <summary>The text a handler's failure carries, which reaches neither the journal nor a run's record.</summary>
    public const string Sentinel = "SENTINEL handler words 7f3a";
}
