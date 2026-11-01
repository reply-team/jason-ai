using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Plugins;
using Jason.Runtime.Execution;
using Jason.Runtime.Routing;

namespace Jason.Runtime.Tests.Routing;

/// <summary>
/// A read of a send, and a take-back of one, act by the reference the send's own attempt returned: the claim refuses a
/// send the campaign does not hold, one that kept no reference, and one made under another plugin or account, and it
/// composes the reference itself, from nothing the caller wrote.
/// </summary>
public partial class ProviderOpPreflightTests
{
    private const string Status = "message.status";
    private const string Cancel = "message.cancel";
    private const string SendItem = "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRG";

    /// <summary>
    /// Each reason the named send cannot be acted on by its reference, for both operations, and the one send that can: the
    /// campaign's, a send, succeeded with a reference, under the plugin and binding the work is routed to. The package's
    /// digest is not among them, since a reference outlives a version.
    /// </summary>
    [Theory]
    [InlineData(Status, "none", AttemptErrors.SendUnknown)]
    [InlineData(Status, "not a send", AttemptErrors.SendUnknown)]
    [InlineData(Status, "failed", AttemptErrors.SendReferenceAbsent)]
    [InlineData(Status, "no reference", AttemptErrors.SendReferenceAbsent)]
    [InlineData(Status, "another plugin", AttemptErrors.SendRouteMismatch)]
    [InlineData(Status, "another binding", AttemptErrors.SendRouteMismatch)]
    [InlineData(Status, "the send", null)]
    [InlineData(Cancel, "none", AttemptErrors.SendUnknown)]
    [InlineData(Cancel, "failed", AttemptErrors.SendReferenceAbsent)]
    [InlineData(Cancel, "another plugin", AttemptErrors.SendRouteMismatch)]
    [InlineData(Cancel, "the send", null)]
    public void A_send_is_acted_on_only_where_this_campaign_made_it_with_a_reference_under_this_route(string operation, string send, string? expected)
    {
        var scenario = Naming(operation, send switch
        {
            "none" => null,
            "not a send" => Made() with { Operation = Add },
            "failed" => Made() with { Status = WorkItemStatus.Failed, Reference = null },
            "no reference" => Made() with { Reference = null },
            "another plugin" => Made() with { PluginId = "another-provider" },
            "another binding" => Made() with { BindingIdentity = "sha256:east" },
            _ => Made(),
        });

        var verdict = scenario.Check();

        Assert.Equal(expected, verdict.Code);
        if (expected is not null)
        {
            Assert.Equal(FailureClass.Permanent, verdict.Class);
            Assert.True(verdict.Refused);
        }
    }

    /// <summary>
    /// The send travels as the runtime found it, beside the arguments as the caller wrote them: the work item, the operation
    /// that made it and the reference its attempt returned.
    /// </summary>
    [Theory]
    [InlineData(Status)]
    [InlineData(Cancel)]
    public void The_reference_is_composed_from_the_send_s_own_attempt(string operation)
    {
        var plan = Naming(operation, Made()).Check().Plan!;

        Assert.True(JsonNode.DeepEquals(
            new JsonObject { ["work_item"] = SendItem, ["operation"] = Send, ["reference"] = "email-1001" },
            plan.Input["send"]),
            plan.Input.ToJsonString());
        Assert.True(JsonNode.DeepEquals(new JsonObject { ["send"] = SendItem }, plan.Input["args"]));
    }

    /// <summary>
    /// A suppressed person's send is still read and still taken back: the register keeps a send from reaching them, and
    /// neither operation reaches anybody.
    /// </summary>
    [Theory]
    [InlineData(Status)]
    [InlineData(Cancel)]
    public void A_suppressed_person_s_send_is_still_read_and_taken_back(string operation)
    {
        var scenario = Naming(operation, Made());
        scenario.Contact = Scenario.Person(("email", "ada@example.test"));
        scenario.Suppressed = true;

        Assert.True(scenario.Check().Passed);
    }

    private static Scenario Naming(string operation, NamedSend? send) => new()
    {
        Operation = operation,
        Route = Scenario.ToTheInstalledPackage,
        Plugin = Scenario.Package(operations: [operation]),
        Arguments = new JsonObject { ["send"] = SendItem },
        Named = send,
    };

    /// <summary>A send the campaign made, succeeded with its reference, under the route the scenarios resolve.</summary>
    private static NamedSend Made() => new(SendItem, Send, WorkItemStatus.Succeeded, "email-1001", Scenario.PluginId, null);
}
