using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Plugins;
using Jason.Runtime.Execution;
using Jason.Runtime.Routing;

namespace Jason.Runtime.Tests.Routing;

/// <summary>
/// A read of a send or a search, and a take-back of either, act by the reference the provider gave the work that made it:
/// the claim refuses work the campaign does not hold, work that kept no reference, and work made under another plugin or
/// account, and it composes the reference itself, from nothing the caller wrote.
/// </summary>
public partial class ProviderOpPreflightTests
{
    private const string Status = "message.status";
    private const string Cancel = "message.cancel";
    private const string SendItem = "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRG";
    private const string Search = "audience.search";
    private const string SearchStatus = "audience.search_status";
    private const string SearchCancel = "audience.search_cancel";
    private const string SearchItem = "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRH";

    /// <summary>
    /// Each reason the named send or search cannot be acted on by its reference, for each operation that acts on one, and
    /// the one that can: the campaign's, made by the operation that makes it, succeeded with a reference, under the plugin
    /// and binding the work is routed to. A search the provider finished inside its own call kept no reference, so its
    /// status is never read. The package's digest is not among the reasons, since a reference outlives a version. A read or
    /// a stop of a search goes where the search ran, whatever its own operation resolves to: an override that sends it to
    /// another plugin leaves the search readable and stoppable at the account that is spending.
    /// </summary>
    [Theory]
    [InlineData(Status, "none", AttemptErrors.SendUnknown)]
    [InlineData(Status, "another operation", AttemptErrors.SendUnknown)]
    [InlineData(Status, "failed", AttemptErrors.SendReferenceAbsent)]
    [InlineData(Status, "no reference", AttemptErrors.SendReferenceAbsent)]
    [InlineData(Status, "another plugin", AttemptErrors.SendRouteMismatch)]
    [InlineData(Status, "another binding", AttemptErrors.SendRouteMismatch)]
    [InlineData(Status, "the one", null)]
    [InlineData(Cancel, "none", AttemptErrors.SendUnknown)]
    [InlineData(Cancel, "failed", AttemptErrors.SendReferenceAbsent)]
    [InlineData(Cancel, "another plugin", AttemptErrors.SendRouteMismatch)]
    [InlineData(Cancel, "the one", null)]
    [InlineData(SearchStatus, "none", AttemptErrors.SearchUnknown)]
    [InlineData(SearchStatus, "another operation", AttemptErrors.SearchUnknown)]
    [InlineData(SearchStatus, "no reference", AttemptErrors.SearchReferenceAbsent)]
    [InlineData(SearchStatus, "another binding", AttemptErrors.SearchRouteMismatch)]
    [InlineData(SearchStatus, "the one", null)]
    [InlineData(SearchStatus, "the one, its read routed elsewhere", null)]
    [InlineData(SearchCancel, "none", AttemptErrors.SearchUnknown)]
    [InlineData(SearchCancel, "failed", AttemptErrors.SearchReferenceAbsent)]
    [InlineData(SearchCancel, "another plugin", AttemptErrors.SearchRouteMismatch)]
    [InlineData(SearchCancel, "the one", null)]
    [InlineData(SearchCancel, "the one, its stop routed elsewhere", null)]
    public void A_send_or_a_search_is_acted_on_only_where_this_campaign_made_it_with_a_reference_under_this_route(string operation, string made, string? expected)
    {
        var scenario = Naming(operation, made switch
        {
            "none" => null,
            "another operation" => MadeFor(operation) with { Operation = Add },
            "failed" => MadeFor(operation) with { Status = WorkItemStatus.Failed, Reference = null },
            "no reference" => MadeFor(operation) with { Reference = null },
            "another plugin" => MadeFor(operation) with { PluginId = "another-provider" },
            "another binding" => MadeFor(operation) with { BindingIdentity = "sha256:east" },
            _ => MadeFor(operation),
        });
        if (made.EndsWith("routed elsewhere", StringComparison.Ordinal))
        {
            scenario.CampaignRoute = new Route("another-provider", null, null);
            scenario.Others = [Scenario.Package("another-provider", operations: [operation])];
        }

        var verdict = scenario.Check();

        Assert.Equal(expected, verdict.Code);
        if (expected is not null)
        {
            Assert.Equal(FailureClass.Permanent, verdict.Class);
            Assert.True(verdict.Refused);
        }
        else
        {
            Assert.Equal(Scenario.PluginId, verdict.Plan!.Plugin.Manifest.Id);
        }
    }

    /// <summary>
    /// The send or the search travels as the runtime found it, beside the arguments as the caller wrote them: the work item,
    /// the reference its attempt returned, and what else the operation's document reads of it — the operation that made a
    /// send, and where a status read of a search starts, from the beginning while no cursor is kept.
    /// </summary>
    [Theory]
    [InlineData(Status, "send", """{"work_item":"wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRG","operation":"message.send","reference":"email-1001"}""")]
    [InlineData(Cancel, "send", """{"work_item":"wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRG","operation":"message.send","reference":"email-1001"}""")]
    [InlineData(SearchStatus, "search", """{"work_item":"wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRH","reference":"srch-1001","cursor":null}""")]
    [InlineData(SearchCancel, "search", """{"work_item":"wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRH","reference":"srch-1001"}""")]
    public void The_reference_is_composed_from_the_named_work_s_own_attempt(string operation, string key, string composed)
    {
        var scenario = Naming(operation, MadeFor(operation));
        var plan = scenario.Check().Plan!;

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(composed), plan.Input[key]), plan.Input.ToJsonString());
        Assert.True(JsonNode.DeepEquals(scenario.Arguments, plan.Input["args"]));
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

    private static Scenario Naming(string operation, NamedReference? made)
    {
        var searches = operation is SearchStatus or SearchCancel;
        return new Scenario
        {
            Operation = operation,
            Route = Scenario.ToTheInstalledPackage,
            Plugin = Scenario.Package(operations: [operation]),
            Arguments = !searches
                ? new JsonObject { ["send"] = SendItem }
                : operation == SearchStatus
                    ? new JsonObject { ["search"] = SearchItem, ["limit"] = 25 }
                    : new JsonObject { ["search"] = SearchItem },
            Named = made as NamedSend,
            Searched = made as NamedSearch,
        };
    }

    /// <summary>The send or the search the operation acts on, made by this campaign and succeeded with its reference under the route the scenarios resolve.</summary>
    private static NamedReference MadeFor(string operation) => operation is SearchStatus or SearchCancel ? Started() : Made();

    /// <summary>A send the campaign made, succeeded with its reference, under the route the scenarios resolve.</summary>
    private static NamedSend Made() => new(SendItem, Send, WorkItemStatus.Succeeded, "email-1001", Scenario.PluginId, null);

    /// <summary>A search the campaign started, which the provider is still running under its reference, under the route the scenarios resolve.</summary>
    private static NamedSearch Started() => new(SearchItem, Search, WorkItemStatus.Succeeded, "srch-1001", Scenario.PluginId, null);
}
