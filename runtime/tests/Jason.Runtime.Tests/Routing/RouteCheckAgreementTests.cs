using System.Text.Json.Nodes;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Execution;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;

namespace Jason.Runtime.Tests.Routing;

/// <summary>
/// Activation and the claim ask the same questions of a route, and answer in two vocabularies on purpose: an
/// operator reading a rejected reload and a manager reading a failed attempt are asking different things. The
/// sentences are meant to differ; the verdicts are not. This theory is what says so — it runs both gates against
/// one built plugin set and holds their answers together through a single table.
/// </summary>
/// <remarks>
/// Two questions belong to one gate only, and they are written down in their own test rather than smuggled into
/// the table: a plugin's availability is the claim's alone, because a package that is merely unusable on this
/// machine is not a reason to refuse an installation's whole configuration, and a credential-shaped binding is
/// activation's alone, because that is where a binding is first written down.
/// </remarks>
public class RouteCheckAgreementTests
{
    private const string Operation = "campaign.get";
    private const string Routed = "shared-provider";

    private static readonly OperationContract Contract = OperationCatalog.Find(Operation)!;

    /// <summary>The binding schema a plugin may declare, so that "the route carries what it asked for" is a case.</summary>
    private static readonly JsonObject Schema = new()
    {
        ["type"] = "object",
        ["required"] = new JsonArray("workspace"),
        ["properties"] = new JsonObject { ["workspace"] = new JsonObject { ["type"] = "string" } },
    };

    /// <summary>
    /// The one bridge between the two vocabularies: every verdict activation can reach about a route, and the
    /// attempt error the claim answers the same fact with. Nothing else may translate between them.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> SameVerdict = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [RouteProblemCodes.PluginUnknown] = AttemptErrors.PluginNotLoaded,
        [RouteProblemCodes.PluginKindNotInvocable] = AttemptErrors.PluginOperationUnsupported,
        [RouteProblemCodes.OperationUnsupported] = AttemptErrors.PluginOperationUnsupported,
        [RouteProblemCodes.ContractIncompatible] = AttemptErrors.ContractIncompatible,
        [RouteProblemCodes.BindingInvalid] = AttemptErrors.BindingInvalid,
    };

    private static readonly IReadOnlyDictionary<string, SharedQuestion> Shared = new Dictionary<string, SharedQuestion>(StringComparer.Ordinal)
    {
        ["nothing stands in the route's way"] =
            new([Provider()], Routed, null, null),

        ["the route names a plugin the set does not hold"] =
            new([Provider()], "ghost-provider", null, RouteProblemCodes.PluginUnknown),

        ["the routed plugin only notifies"] =
            new([Provider(kind: PluginKind.Notification)], Routed, null, RouteProblemCodes.PluginKindNotInvocable),

        ["the routed plugin never claimed the operation"] =
            new([Provider(operations: [])], Routed, null, RouteProblemCodes.OperationUnsupported),

        ["the routed plugin speaks another operation contract"] =
            new([Provider(contractVersions: [2])], Routed, null, RouteProblemCodes.ContractIncompatible),

        ["the binding is not what the plugin's schema asks for"] =
            new([Provider(binding: Schema)], Routed, new JsonObject { ["workspace"] = 5 }, RouteProblemCodes.BindingInvalid),

        ["the route carries no binding where the plugin requires one"] =
            new([Provider(binding: Schema)], Routed, null, RouteProblemCodes.BindingInvalid),

        ["the binding is exactly what the plugin's schema asks for"] =
            new([Provider(binding: Schema)], Routed, new JsonObject { ["workspace"] = "west" }, null),
    };

    public static TheoryData<string> EveryQuestionBothGatesAsk() => [.. Shared.Keys];

    [Theory]
    [MemberData(nameof(EveryQuestionBothGatesAsk))]
    public void Activation_and_the_claim_reach_the_same_verdict_about_one_route(string question)
    {
        var scenario = Shared[question];
        var plugins = Set(scenario.Installed);
        var route = new Route(scenario.RoutedTo, scenario.Binding, BindingIdentity.Of(scenario.Binding));

        var activation = RouteActivator.Check(plugins, Operation, route.PluginId, route.Binding);
        var claim = ProviderOpPreflight.Routable(plugins, Contract, route);

        // The case says what activation answers, so that a pair agreeing for the wrong reason cannot pass.
        Assert.Equal(scenario.Activation, activation?.Code);
        Assert.Equal(Translated(activation?.Code), claim?.Code);
    }

    /// <summary>
    /// The two questions that are one gate's alone, stated rather than left as a gap in the table above. A plugin
    /// that is installed but unusable on this machine is an environment's problem, and refusing every route to it
    /// would turn a missing program into a rejected reload; a binding that reads like a credential is refused
    /// where a binding is written, because by the time an item is claimed it has already been journaled.
    /// </summary>
    [Fact]
    public void Availability_is_the_claim_s_question_and_a_credential_shaped_binding_is_activation_s()
    {
        var held = Set([Provider(status: PluginStatus.Unavailable)]);
        var plain = new Route(Routed, null, null);

        Assert.Null(RouteActivator.Check(held, Operation, Routed, binding: null));
        Assert.Equal(AttemptErrors.PluginUnavailable, ProviderOpPreflight.Routable(held, Contract, plain)!.Code);

        var loaded = Set([Provider()]);
        var credential = new JsonObject { ["api_key"] = "not a value anybody should paste here" };

        Assert.Equal(RouteProblemCodes.BindingSecretLike, RouteActivator.Check(loaded, Operation, Routed, credential)!.Code);
        Assert.Null(ProviderOpPreflight.Routable(loaded, Contract, new Route(Routed, credential, null)));
    }

    /// <summary>The claim-side verdict for an activation verdict, and a failure when the table has none.</summary>
    private static string? Translated(string? activation)
    {
        if (activation is null)
        {
            return null;
        }

        Assert.True(
            SameVerdict.ContainsKey(activation),
            $"activation answered '{activation}', which nothing in the table translates into what the claim would say.");
        return SameVerdict[activation];
    }

    private static PluginSnapshot Set(IReadOnlyList<LoadedPlugin> installed) =>
        new("snp_shared", DateTime.UnixEpoch, SnapshotSource.Reload, installed);

    private static LoadedPlugin Provider(
        PluginKind kind = PluginKind.Provider,
        IReadOnlyList<string>? operations = null,
        IReadOnlyList<int>? contractVersions = null,
        JsonObject? binding = null,
        PluginStatus status = PluginStatus.Valid) =>
        TestPlugins.Loaded(Routed, operations ?? [Operation], kind, contractVersions, binding, status);

    /// <summary>One route, one plugin set, and what activation is expected to make of it.</summary>
    private sealed record SharedQuestion(
        IReadOnlyList<LoadedPlugin> Installed,
        string RoutedTo,
        JsonObject? Binding,
        string? Activation);
}
