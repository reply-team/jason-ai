using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;
using Jason.Runtime.Events;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Events;

/// <summary>
/// A plugin that reads events without a child: each page is answered by the next answer the test queued, and every
/// request is kept, so a test reads what each page was asked for. With nothing queued it answers as <see cref="Otherwise"/>
/// says, or with an empty page that hands back the cursor it was given, or <c>c-0</c>.
/// </summary>
internal sealed class FakeEventsInvoker : IEventsInvoker
{
    private static readonly OutcomeDiagnostics Diagnostics = new(220, 1, 0, 0);

    private readonly ConcurrentQueue<Func<PluginInvocationRequest, CancellationToken, InvocationOutcome>> _answers = new();
    private readonly ConcurrentQueue<PluginInvocationRequest> _requests = new();

    public IReadOnlyList<PluginInvocationRequest> Requests => [.. _requests];

    /// <summary>How a page is answered when nothing is queued for it.</summary>
    public Func<PluginInvocationRequest, CancellationToken, InvocationOutcome>? Otherwise { get; set; }

    /// <summary>The next pages are answered with these, in order.</summary>
    public void Answer(params InvocationOutcome[] outcomes)
    {
        foreach (var outcome in outcomes)
        {
            _answers.Enqueue((_, _) => outcome);
        }
    }

    /// <summary>The next page is answered by a function of what it asked for and the token it was given.</summary>
    public void Answer(Func<PluginInvocationRequest, CancellationToken, InvocationOutcome> answer) => _answers.Enqueue(answer);

    public Task<PluginInvocationResult> InvokeAsync(PluginInvocationRequest request, CancellationToken cancellationToken)
    {
        _requests.Enqueue(request);
        var outcome = _answers.TryDequeue(out var answer)
            ? answer(request, cancellationToken)
            : Otherwise?.Invoke(request, cancellationToken) ?? Page((string?)request.Input["cursor"] ?? "c-0", false);
        var provenance = new InvocationProvenance(request.PluginId, "1.0.0", "sha256:0", 1, 1, "pin_01JB6K8TQ2W9V4MZ0C3Y7H5NRD", request.CorrelationId, "snp_1");
        return Task.FromResult(new PluginInvocationResult(outcome, provenance, null));
    }

    public static InvocationOutcome Page(string cursor, bool more, params JsonObject[] events) =>
        Result(new JsonObject { ["events"] = new JsonArray([.. events]), ["cursor"] = cursor, ["has_more"] = more });

    public static InvocationOutcome Result(JsonNode result, JsonObject? externalIds = null) =>
        new InvocationOutcome.Succeeded(result, externalIds, Diagnostics);

    public static InvocationOutcome Failure(string code, FailureClass failureClass, JsonObject? externalIds = null) =>
        new InvocationOutcome.Failed(new OutcomeError(failureClass, code, "the provider said no", null, externalIds), Diagnostics);

    public static InvocationOutcome Protocol(string code) => new InvocationOutcome.ProtocolFailure(code, "the protocol did not complete", null, null);
}
