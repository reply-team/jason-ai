using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Plugins;
using Jason.Runtime.Execution;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.WorkItems;

namespace Jason.Runtime.Tests.Routing;

/// <summary>
/// Everything that can stop a provider work item, decided before a child process exists — and decided in one
/// order, because a manager reading the failure should meet the reason the item cannot run at all before the
/// reasons that need a planner's attention.
/// </summary>
public class ProviderOpPreflightTests
{
    private const string Add = "list_membership.add";
    private const string Enroll = "campaign.enroll";
    private const string Get = "campaign.get";

    /// <summary>
    /// The order, proven by repairing one thing at a time: everything is wrong at the start, and each repair
    /// reveals exactly the next check. A test per code would say that each one can fail; only this one says which
    /// of them wins when several are true at once, which is what a person reading the item actually gets.
    /// </summary>
    [Fact]
    public void The_first_failing_check_is_the_one_that_names_the_failure()
    {
        var met = new List<string>();
        var scenario = new Scenario { Operation = "nothing.published" };
        met.Add(Refused(scenario, FailureClass.Permanent));

        scenario.Operation = Add;
        met.Add(Refused(scenario, FailureClass.Permanent));

        scenario.Route = new Route("ghost-provider", null, null);
        met.Add(Refused(scenario, FailureClass.Permanent));

        // From here the route names the package that is installed, and the package itself is what is wrong.
        scenario.Route = Scenario.ToTheInstalledPackage;
        scenario.Plugin = Scenario.Package(status: PluginStatus.Unavailable);
        met.Add(Refused(scenario, FailureClass.Permanent));

        scenario.Plugin = Scenario.Package(operations: []);
        met.Add(Refused(scenario, FailureClass.Permanent));

        scenario.Plugin = Scenario.Package(contractVersions: [2]);
        met.Add(Refused(scenario, FailureClass.Permanent));

        scenario.Plugin = Scenario.Package(binding: Scenario.BindingSchema);
        met.Add(Refused(scenario, FailureClass.Permanent));

        scenario.Route = new Route(Scenario.PluginId, new JsonObject { ["workspace"] = "west" }, "sha256:west");

        // The approval gate sits between the package and the planner's own mistakes: an operation a person has to
        // confirm is refused here, although this item also names nobody for an operation that needs somebody.
        scenario.Operation = Enroll;
        met.Add(Refused(scenario, FailureClass.Permanent));

        scenario.Operation = Add;
        scenario.Arguments = new JsonObject { ["channel"] = "email" };
        met.Add(Refused(scenario, FailureClass.Permanent));

        scenario.Contact = Scenario.Person(("phone", "+15555550100"));
        met.Add(Refused(scenario, FailureClass.Permanent));

        scenario.Contact = Scenario.Person(("email", "ada@example.test"));
        scenario.Suppressed = true;
        met.Add(Refused(scenario, FailureClass.Permanent));

        scenario.Suppressed = false;
        met.Add(Refused(scenario, FailureClass.Validation));
        Assert.Equal("/args/list", Assert.Single(scenario.Check().Details!).Field);

        // Every check in the published order, met one at a time, and then an item with nothing left wrong.
        Assert.Equal(ProviderOpPreflight.Codes, met);

        scenario.Arguments = Scenario.CompleteArguments;
        var verdict = scenario.Check();
        Assert.True(verdict.Passed);
        Assert.Null(verdict.Code);
        Assert.Null(verdict.Class);
    }

    /// <summary>
    /// None of the twelve is worth another attempt: nothing about the work changed between two scans, so an item
    /// released back into the queue would fail the same way for as long as the queue existed. The claim says so
    /// itself — it fails a refused item with <c>retriable: false</c> — and this is the table agreeing, so that a
    /// code later added to both places cannot come to mean two different things.
    /// </summary>
    [Fact]
    public void No_reason_the_pre_flight_gives_is_ever_worth_another_attempt() =>
        Assert.All(ProviderOpPreflight.Codes, code => Assert.DoesNotContain(code, FailureClassifier.RetriableCodes));

    /// <summary>
    /// A plugin that cannot be invoked at all is the same fact as one that does not implement the operation, and
    /// it is caught by the same check: a notification plugin implements no canonical operation by construction.
    /// </summary>
    [Fact]
    public void A_plugin_that_only_notifies_is_never_asked_to_perform_an_operation()
    {
        var scenario = new Scenario
        {
            Operation = Get,
            Route = Scenario.ToTheInstalledPackage,
            Plugin = Scenario.Package(kind: PluginKind.Notification),
        };

        Assert.Equal(AttemptErrors.PluginOperationUnsupported, scenario.Check().Code);
    }

    /// <summary>
    /// Never a fallback to another plugin: an operation the routed plugin cannot perform ends the item with the
    /// reason, rather than being handed quietly to whichever other plugin happens to implement it.
    /// </summary>
    [Fact]
    public void An_operation_the_routed_plugin_cannot_perform_is_never_handed_to_one_that_can()
    {
        var scenario = new Scenario
        {
            Operation = Get,
            Route = Scenario.ToTheInstalledPackage,
            Plugin = Scenario.Package(operations: []),
            Others = [Scenario.Package(id: "willing-provider", operations: [Get])],
        };

        var verdict = scenario.Check();

        Assert.Equal(AttemptErrors.PluginOperationUnsupported, verdict.Code);
        Assert.Null(verdict.Plan);
    }

    /// <summary>
    /// A6. The planner's arguments contradict Jason's own record of what the provider calls this campaign. The
    /// rule is to work by the pinned identifier, so the contradiction is shown rather than silently resolved
    /// either way — and the message names the pin, because that is the value everything else would use.
    /// </summary>
    [Fact]
    public void An_identifier_in_the_arguments_that_contradicts_the_pin_is_refused_by_pointer()
    {
        var scenario = new Scenario
        {
            Operation = Get,
            Route = Scenario.ToTheInstalledPackage,
            Arguments = new JsonObject { ["campaign"] = new JsonObject { ["external_id"] = "cmp-provider-9" } },
            CampaignPins = new Dictionary<string, string>(StringComparer.Ordinal) { ["campaign"] = "cmp-provider-7" },
        };

        var verdict = scenario.Check();

        Assert.Equal(AttemptErrors.InputInvalid, verdict.Code);
        var detail = Assert.Single(verdict.Details!);
        Assert.Equal("/args/campaign/external_id", detail.Field);
        Assert.Contains("cmp-provider-7", detail.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The other half of A6, and the reason it is not "refuse whenever both are present": a planner repeating
    /// what Jason already knows contradicts nothing, and an item that says the same thing twice still runs.
    /// </summary>
    [Fact]
    public void An_identifier_in_the_arguments_that_agrees_with_the_pin_passes()
    {
        var scenario = new Scenario
        {
            Operation = Get,
            Route = Scenario.ToTheInstalledPackage,
            Arguments = new JsonObject { ["campaign"] = new JsonObject { ["external_id"] = "cmp-provider-7" } },
            CampaignPins = new Dictionary<string, string>(StringComparer.Ordinal) { ["campaign"] = "cmp-provider-7" },
        };

        Assert.True(scenario.Check().Passed);
    }

    /// <summary>
    /// The rule over the whole document is a claim-time check: creation validates the caller's own arguments, and
    /// only the composed document can say whether the campaign is named at all — by the arguments, or by a pin.
    /// </summary>
    [Fact]
    public void A_document_whose_properties_are_all_right_and_whose_root_rule_is_not_is_still_refused()
    {
        var scenario = new Scenario { Operation = Get, Route = Scenario.ToTheInstalledPackage };

        var verdict = scenario.Check();

        Assert.Equal(AttemptErrors.InputInvalid, verdict.Code);
        Assert.Equal("/", Assert.Single(verdict.Details!).Field);

        scenario.CampaignPins = new Dictionary<string, string>(StringComparer.Ordinal) { ["campaign"] = "cmp-provider-7" };
        Assert.True(scenario.Check().Passed);
    }

    /// <summary>What the claim hands on: the plan carries what the run needs, and every id it was decided by.</summary>
    [Fact]
    public void A_passing_pre_flight_hands_on_what_was_resolved()
    {
        var scenario = new Scenario
        {
            Operation = Add,
            Plugin = Scenario.Package(binding: Scenario.BindingSchema),
            Route = new Route(Scenario.PluginId, new JsonObject { ["workspace"] = "west" }, "sha256:west"),
            Contact = Scenario.Person(("email", "ada@example.test")),
            Arguments = Scenario.CompleteArguments,
        };

        var plan = scenario.Check().Plan;

        Assert.NotNull(plan);
        Assert.Equal(Scenario.PluginId, plan.Plugin.Manifest.Id);
        Assert.Equal(RouteScope.GlobalDefault, plan.Scope);
        Assert.Equal("sha256:west", plan.BindingIdentity);
        Assert.Equal("west", (string?)plan.Binding!["workspace"]);
        Assert.Equal(Add, plan.Contract.Id);
        Assert.Equal(scenario.PluginSnapshotId, plan.PluginSnapshotId);
        Assert.Equal(scenario.RoutingSnapshotId, plan.RoutingSnapshotId);
        Assert.Equal("ada@example.test", (string?)plan.Input["contacts"]![0]!["channels"]![0]!["value"]);
    }

    /// <summary>
    /// The claim answers exactly what <c>route.resolve</c> would, because it reads the same snapshot through the
    /// same pure function: the level the route won at travels with the plan and is recorded against the attempt.
    /// </summary>
    [Fact]
    public void The_scope_the_route_won_at_travels_with_the_plan()
    {
        var scenario = new Scenario
        {
            Operation = Get,
            Route = Scenario.ToTheInstalledPackage,
            CampaignRoute = new Route(Scenario.PluginId, null, null),
            CampaignPins = new Dictionary<string, string>(StringComparer.Ordinal) { ["campaign"] = "cmp-provider-7" },
        };

        Assert.Equal(RouteScope.CampaignOperation, scenario.Check().Plan!.Scope);
    }

    /// <summary>The code this state is refused with, having first said that the class travels with it.</summary>
    private static string Refused(Scenario scenario, FailureClass expected)
    {
        var verdict = scenario.Check();

        Assert.False(verdict.Passed);
        Assert.Null(verdict.Plan);
        Assert.NotNull(verdict.Message);
        Assert.Equal(expected, verdict.Class);
        return verdict.Code!;
    }

    /// <summary>A mutable little world: everything the pre-flight reads, repaired one thing at a time.</summary>
    private sealed class Scenario
    {
        public const string PluginId = "stand-in-provider";

        public static JsonObject BindingSchema => new()
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("workspace"),
            ["properties"] = new JsonObject { ["workspace"] = new JsonObject { ["type"] = "string" } },
        };

        /// <summary>The ordinary case: one global default, naming the package this scenario has installed.</summary>
        public static Route ToTheInstalledPackage => new(PluginId, null, null);

        public static JsonObject CompleteArguments => new()
        {
            ["list"] = new JsonObject { ["external_id"] = "L-1129" },
            ["channel"] = "email",
        };

        public string RoutingSnapshotId { get; } = PublicId.New(RouteSnapshot.IdPrefix);

        public string PluginSnapshotId { get; } = PublicId.New(PluginProtocol.SnapshotIdPrefix);

        public string Operation { get; set; } = Add;

        /// <summary>The global default route, and null means nothing is routed anywhere.</summary>
        public Route? Route { get; set; }

        public Route? CampaignRoute { get; set; }

        public LoadedPlugin Plugin { get; set; } = Package();

        public IReadOnlyList<LoadedPlugin> Others { get; set; } = [];

        public Contact? Contact { get; set; }

        public bool Suppressed { get; set; }

        public JsonObject? Arguments { get; set; }

        public IReadOnlyDictionary<string, string> ContactPins { get; set; } = PreflightFacts.NoPins;

        public IReadOnlyDictionary<string, string> CampaignPins { get; set; } = PreflightFacts.NoPins;

        public static LoadedPlugin Package(
            string id = PluginId,
            PluginKind kind = PluginKind.Provider,
            IReadOnlyList<string>? operations = null,
            IReadOnlyList<int>? contractVersions = null,
            JsonObject? binding = null,
            PluginStatus status = PluginStatus.Valid) =>
            TestPlugins.Loaded(id, operations ?? [Add, Enroll, Get], kind, contractVersions, binding, status);

        public static Contact Person(params (string Channel, string Value)[] channels)
        {
            var contact = new Contact { PublicId = PublicId.New("cnt"), FirstName = "Ada" };
            foreach (var (channel, value) in channels)
            {
                contact.Channels.Add(new ContactChannel { Channel = channel, Value = value, IsPrimary = true });
            }

            return contact;
        }

        public PreflightVerdict Check()
        {
            var campaign = WorkItemFactory.NewCampaign();
            var item = WorkItemFactory.NewProviderOp(campaign, Operation);
            item.Contact = Contact;
            if (Arguments is not null)
            {
                item.Context = new JsonObject { [WorkItemService.InputKey] = Arguments.DeepClone() };
            }

            var facts = new PreflightFacts(item, campaign, Contact, ContactPins, CampaignPins, Suppressed);
            var plugins = new PluginSnapshot(PluginSnapshotId, DateTime.UnixEpoch, SnapshotSource.Startup, [Plugin, .. Others]);

            var campaigns = new Dictionary<string, RouteSet>(StringComparer.Ordinal);
            if (CampaignRoute is not null)
            {
                campaigns[campaign.PublicId] = new RouteSet(
                    null,
                    new Dictionary<string, Route>(StringComparer.Ordinal) { [Operation] = CampaignRoute });
            }

            var routes = new RouteSnapshot(
                RoutingSnapshotId,
                DateTimeOffset.UnixEpoch,
                PluginSnapshotId,
                new RouteSet(Route, RouteSet.Empty.Operations),
                campaigns);

            return ProviderOpPreflight.Check(facts, plugins, routes);
        }
    }
}
