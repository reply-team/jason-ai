using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;
using Xunit.Sdk;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// What the shared helper says when an operation did not succeed. Every test in this area drives its operations
/// through <see cref="ReplyOperations.RunEachAsync"/>, which asserts success on the way past, so this helper is
/// the last thing to hold the outcome before it is thrown away — and an intermittent failure that reaches it is
/// only ever seen once. A helper that reports the type it wanted and not the failure it got turns that one
/// sighting into "expected Succeeded, found Failed", which names no operation, no code and no reason.
/// </summary>
public class ReplyOutcomeReportingTests
{
    private static readonly OutcomeDiagnostics NoCost = new(0, 0, 0, 0);

    [Fact]
    public void A_declared_failure_is_reported_with_the_operation_and_the_whole_failure()
    {
        var failure = new InvocationOutcome.Failed(
            new OutcomeError(
                FailureClass.Permanent,
                "sequence_not_found",
                "No sequence 7 in this account.",
                new JsonObject { ["sequence"] = 7 },
                ExternalIds: null),
            NoCost);

        var thrown = Assert.ThrowsAny<XunitException>(
            () => ReplyOperations.Succeeded(ReplyOperations.CampaignGet, Result(failure)));

        Assert.Contains(ReplyOperations.CampaignGet, thrown.Message, StringComparison.Ordinal);
        Assert.Contains("sequence_not_found", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("No sequence 7 in this account.", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("Permanent", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_protocol_failure_is_reported_with_its_exit_code_and_the_tail_of_stderr()
    {
        var failure = new InvocationOutcome.ProtocolFailure(
            "host_failure",
            "The plugin host did not answer.",
            ExitCode: 4,
            StderrTail: "Error: reply-cli not found on PATH");

        var thrown = Assert.ThrowsAny<XunitException>(
            () => ReplyOperations.Succeeded(ReplyOperations.MembershipAdd, Result(failure)));

        Assert.Contains(ReplyOperations.MembershipAdd, thrown.Message, StringComparison.Ordinal);
        Assert.Contains("host_failure", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("The plugin host did not answer.", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("4", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("Error: reply-cli not found on PATH", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>A success still passes straight through: the instrument is for the outcomes that end a run.</summary>
    [Fact]
    public void A_success_is_still_returned_to_the_caller()
    {
        var answered = new JsonObject
        {
            ["campaign"] = new JsonObject
            {
                ["external_id"] = "7",
                ["name"] = "Q3 LatAm founders",
                ["status"] = "live",
                ["counts"] = new JsonObject { ["enrolled"] = 0 },
            },
        };
        var outcome = new InvocationOutcome.Succeeded(answered, ExternalIds: null, NoCost);

        var result = ReplyOperations.Succeeded(ReplyOperations.CampaignGet, Result(outcome));

        Assert.Equal("7", result["campaign"]!["external_id"]!.GetValue<string>());
    }

    private static PluginInvocationResult Result(InvocationOutcome outcome) =>
        new(outcome, Provenance, Launch: null);

    private static InvocationProvenance Provenance { get; } = new(
        "reply",
        "0.1.0",
        "sha256:0000000000000000000000000000000000000000000000000000000000000000",
        ProtocolVersion: 1,
        OperationContractVersion: 1,
        InvocationId: "inv_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
        CorrelationId: "att_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
        SnapshotId: "snp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD");
}
