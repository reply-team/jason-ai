using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;
using Jason.Runtime.Contacts;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Contacts;

/// <summary>
/// A provider read without a child: each read is answered by the next outcome the test queued, and every request is
/// kept, so a test reads what was asked for. With nothing queued, a read fails the test rather than answer for it.
/// </summary>
internal sealed class FakeProviderReads : IProviderReadInvoker
{
    private static readonly OutcomeDiagnostics Diagnostics = new(180, 1, 0, 0);

    private readonly ConcurrentQueue<InvocationOutcome> _answers = new();
    private readonly ConcurrentQueue<PluginInvocationRequest> _requests = new();

    public IReadOnlyList<PluginInvocationRequest> Requests => [.. _requests];

    public void Answer(params InvocationOutcome[] outcomes)
    {
        foreach (var outcome in outcomes)
        {
            _answers.Enqueue(outcome);
        }
    }

    public Task<PluginInvocationResult> InvokeAsync(PluginInvocationRequest request, CancellationToken cancellationToken)
    {
        _requests.Enqueue(request);
        var outcome = _answers.TryDequeue(out var queued)
            ? queued
            : throw new InvalidOperationException($"No answer was queued for {request.Operation}.");
        var provenance = new InvocationProvenance(request.PluginId, "1.0.0", "sha256:0", 1, 1, "pin_01JB6K8TQ2W9V4MZ0C3Y7H5NRD", request.CorrelationId, "snp_1");
        return Task.FromResult(new PluginInvocationResult(outcome, provenance, null));
    }

    public static InvocationOutcome Result(JsonNode result) => new InvocationOutcome.Succeeded(result, null, Diagnostics);

    public static InvocationOutcome Failure(string code, FailureClass failureClass) =>
        new InvocationOutcome.Failed(new OutcomeError(failureClass, code, "the provider said no", null, null), Diagnostics);

    public static InvocationOutcome Protocol(string code) => new InvocationOutcome.ProtocolFailure(code, "the protocol did not complete", null, null);
}
