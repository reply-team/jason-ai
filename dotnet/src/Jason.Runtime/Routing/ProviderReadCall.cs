using System.Text.Json.Nodes;
using Jason.Contracts.Ids;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Contacts;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Plugins.Registry;
using Microsoft.AspNetCore.Http;

namespace Jason.Runtime.Routing;

/// <summary>
/// One read made on request, from the composed input to the answer: the input held to the operation's schema, the plugin
/// invoked on the package the route was checked against, and what comes back held to the document — a result to its output
/// schema, a failure to the codes it declares. It is information, not work: nothing is recorded, pinned or journaled, and
/// nothing is retried; a request the provider could not answer is refused with what it said, and only a transient class
/// says try again.
/// </summary>
internal static class ProviderReadCall
{
    public static async Task<JsonObject> InvokeAsync(
        IProviderReadInvoker invoker,
        string pluginId,
        OperationContract contract,
        JsonObject input,
        JsonObject? binding,
        PinnedPlugin plugin,
        string invocationPrefix,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(invoker);
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(input);

        // The runtime composed this input from what it holds and what the caller gave, checked first; a problem left here
        // is the runtime's own, never the caller's to fix.
        if (contract.CheckInput(input, PatternTiming.Default) is { Count: > 0 } problems)
        {
            throw new InvalidOperationException($"The input composed for '{contract.Id}' does not satisfy its schema: {problems.Count} problem(s).");
        }

        var request = new PluginInvocationRequest(
            pluginId,
            contract.Id,
            input,
            binding?.DeepClone().AsObject(),
            PublicId.New(invocationPrefix),
            Timeout: TimeSpan.FromMilliseconds(contract.TimeoutMs),
            Pinned: plugin);
        var invoked = await invoker.InvokeAsync(request, ct).ConfigureAwait(false);

        switch (invoked.Outcome)
        {
            case InvocationOutcome.Succeeded { Result: JsonObject result }
                when SchemaValidator.Verdicts(OutcomeContract.CheckResult(contract, result)).Count == 0:
                return result;
            case InvocationOutcome.Succeeded:
                throw Failed(AttemptErrors.ResultInvalid, $"The provider's answer to '{contract.Id}' does not satisfy the schema that operation publishes.", FailureClass.Ambiguous);
            case InvocationOutcome.Failed { Error: var error }:
                // A blocked read is final, as a permanent one is, under the code the plugin gave. The block itself does
                // not travel: an API error carries a code, a message and whether to retry, and nothing else. The code is
                // held to the operation's list by the one check a work item's failure meets, host endings included.
                throw OutcomeContract.CheckFailure(contract, error) is null
                    ? Failed(error.Code, error.Message, OutcomeContract.Recorded(error, out _))
                    : Failed(AttemptErrors.ResultInvalid, $"The provider failed '{contract.Id}' outside what that operation declares.", FailureClass.Ambiguous);
            case InvocationOutcome.ProtocolFailure protocol:
                throw Failed("provider_read_failed", protocol.Message, OutcomeClassification.ClassOf(protocol.Code));
            default:
                throw new InvalidOperationException($"An invocation of '{contract.Id}' ended in an outcome nothing reads.");
        }
    }

    /// <summary>The provider could not answer: the request is answered with what it said, and only a transient class says try again.</summary>
    private static DomainException Failed(string code, string message, FailureClass failureClass) =>
        new(StatusCodes.Status502BadGateway, code, message, retryable: failureClass == FailureClass.Transient);
}
