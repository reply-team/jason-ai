using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Execution;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Registry;

namespace Jason.Runtime.Routing;

/// <summary>
/// What the pre-flight decided: either the plan a run needs, or the one reason the item cannot run at all.
/// </summary>
/// <param name="Code">The attempt error code, or null when nothing refused the item.</param>
/// <param name="Message">What a person reading the work item is told.</param>
/// <param name="Class">
/// The kind of failure, which the attempt records: everything structural is permanent, and only a document that
/// does not satisfy its own schema is a validation failure — the one a planner can fix by editing the item.
/// </param>
/// <param name="Details">Where the input failed, by pointer, for the one check that can name more than one place.</param>
/// <param name="Plan">Everything the run needs, and null whenever something refused it.</param>
public sealed record PreflightVerdict(
    string? Code,
    string? Message,
    FailureClass? Class,
    IReadOnlyList<ErrorDetail>? Details,
    ProviderOpPlan? Plan)
{
    public bool Passed => Code is null;
}

/// <summary>
/// Everything that can stop a provider work item, decided before a child process exists. Twelve checks in one
/// order: what the runtime cannot do at all, then the gate it owes a person, then what the planner got wrong —
/// so a manager meets the reason the item can never run before the reasons that need their attention.
/// </summary>
/// <remarks>
/// <para>
/// A pure function over <see cref="PreflightFacts"/> and two immutable snapshots: no database, no clock, no
/// options. The claimer reads the facts inside the claim transaction and this decides on them, which is what
/// keeps the claim to a handful of indexed reads and lets the order above be a unit test with no fixture.
/// </para>
/// <para>
/// <b>Never a fallback to another plugin.</b> An operation the routed plugin cannot perform ends the item with
/// the reason. Handing the work to whichever other plugin happens to implement it would mean a campaign acting
/// against an account nobody named, which is precisely the surprise routing exists to prevent.
/// </para>
/// </remarks>
public static class ProviderOpPreflight
{
    /// <summary>The one approval a runtime may give itself: everything else is a person's to give.</summary>
    private const string Automatic = "auto";

    /// <summary>The twelve, in the order they are checked. The first that fails is the one the attempt records.</summary>
    public static IReadOnlyList<string> Codes { get; } =
    [
        AttemptErrors.OperationUnknown,
        AttemptErrors.NoRoute,
        AttemptErrors.PluginNotLoaded,
        AttemptErrors.PluginUnavailable,
        AttemptErrors.PluginOperationUnsupported,
        AttemptErrors.ContractIncompatible,
        AttemptErrors.BindingInvalid,
        AttemptErrors.ApprovalRequired,
        AttemptErrors.ContactRequired,
        AttemptErrors.NoChannelValue,
        AttemptErrors.Suppressed,
        AttemptErrors.InputInvalid,
    ];

    /// <summary>The whole decision, in the published order.</summary>
    public static PreflightVerdict Check(PreflightFacts facts, PluginSnapshot plugins, RouteSnapshot routes)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(plugins);
        ArgumentNullException.ThrowIfNull(routes);

        var operation = facts.Item.Operation ?? string.Empty;
        if (OperationCatalog.Find(operation) is not { } contract)
        {
            // Creation refuses an operation this build does not publish; a row written by a build that did is the
            // reason this is checked rather than assumed.
            return Refused(AttemptErrors.OperationUnknown, $"This runtime publishes no contract for operation '{operation}'.");
        }

        if (RouteResolver.Resolve(routes, facts.Campaign.PublicId, operation) is not { } resolution)
        {
            return Refused(AttemptErrors.NoRoute, $"No provider route exists yet for operation '{operation}'.");
        }

        var route = resolution.Route;
        if (Routable(plugins, contract, route) is { } unroutable)
        {
            return unroutable;
        }

        // Nothing objected, so the plugin the checks above found is there to be found again.
        var plugin = plugins.Find(route.PluginId)!;

        if (!string.Equals(contract.Approval.Value, Automatic, StringComparison.Ordinal))
        {
            return Refused(
                AttemptErrors.ApprovalRequired,
                $"Operation '{operation}' is approved '{contract.Approval.Value}', and a dispatcher may not stand in for the person who approves it.");
        }

        if (contract.Preflight.Contact == ContactRequirement.Required && facts.Contact is null)
        {
            return Refused(AttemptErrors.ContactRequired, $"Operation '{operation}' acts on a contact, and this work item names none.");
        }

        // A channel the arguments never named is not a person's problem: the composed document says so by
        // pointer below, which is the answer that tells a planner what to write.
        if (CanonicalInput.ConsumedChannel(contract, facts.Item) is { } channel && facts.Contact is { } contact)
        {
            if (CanonicalInput.Reachable(contact, channel) is null)
            {
                return Refused(
                    AttemptErrors.NoChannelValue,
                    $"The contact has no '{channel}' channel, and '{channel}' is the one '{operation}' consumes.");
            }

            if (facts.Suppressed)
            {
                return Refused(
                    AttemptErrors.Suppressed,
                    $"The contact's '{channel}' value is on the suppression list, so this campaign does not reach them there.");
            }
        }

        var input = CanonicalInput.Compose(contract, facts);
        if (Contradictions(contract, facts, input) is { Count: > 0 } contradicted)
        {
            return Refused(
                AttemptErrors.InputInvalid,
                $"The arguments of this work item contradict an identifier Jason has already recorded for '{operation}'.",
                contradicted,
                FailureClass.Validation);
        }

        // The whole document, not only the arguments: a rule over the root — "the campaign is named somehow" —
        // is one only the composed input can answer, and creation never sees the composed input.
        if (SchemaValidator.Validate(input, contract.InputSchema) is { Count: > 0 } problems)
        {
            return Refused(
                AttemptErrors.InputInvalid,
                $"The input composed for '{operation}' does not satisfy the schema that operation publishes.",
                Details(problems),
                FailureClass.Validation);
        }

        return new PreflightVerdict(
            null,
            null,
            null,
            null,
            new ProviderOpPlan(
                plugin,
                plugins.Id,
                routes.Id,
                resolution.Scope,
                route.Binding,
                route.BindingIdentity,
                contract,
                input));
    }

    /// <summary>
    /// The half of the decision that needs nothing but the route and the plugin set behind it: whether the
    /// plugin a route names would be handed this operation at all. Null when it would be.
    /// <para>
    /// It is its own function because <c>route.resolve</c> answers exactly this, before any work item exists —
    /// and an operator asking "why will this campaign not run?" has to be told what the claim would decide,
    /// rather than something that resembles it because two places were kept in step by hand.
    /// </para>
    /// </summary>
    public static PreflightVerdict? Routable(PluginSnapshot plugins, OperationContract contract, Route route)
    {
        ArgumentNullException.ThrowIfNull(plugins);
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(route);

        if (plugins.Find(route.PluginId) is not { } plugin)
        {
            return Refused(
                AttemptErrors.PluginNotLoaded,
                $"The route for '{contract.Id}' names plugin '{route.PluginId}', which the active plugin set does not hold.");
        }

        if (plugin.Status == PluginStatus.Unavailable)
        {
            return Refused(
                AttemptErrors.PluginUnavailable,
                $"Plugin '{plugin.Manifest.Id}' is installed but unavailable on this machine; reload after repairing what it needs.");
        }

        // The kind is asked first: a plugin that only notifies lists no operation at all, so asking "does it
        // implement this one?" first would answer with what is missing rather than with what it is.
        if (plugin.Manifest.Kind != PluginKind.Provider)
        {
            return Refused(
                AttemptErrors.PluginOperationUnsupported,
                $"Plugin '{plugin.Manifest.Id}' is not a kind of plugin that performs canonical operations.");
        }

        if (!plugin.Supports(contract.Id))
        {
            return Refused(
                AttemptErrors.PluginOperationUnsupported,
                $"Plugin '{plugin.Manifest.Id}' does not perform '{contract.Id}', and work is never handed to a plugin the route did not name.");
        }

        if (!plugin.Manifest.Contracts.Operations.Contains(contract.Version))
        {
            return Refused(
                AttemptErrors.ContractIncompatible,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Plugin '{plugin.Manifest.Id}' speaks no version of the '{contract.Id}' contract that this runtime publishes, which is version {contract.Version}."));
        }

        // Activation already held this binding to the same schema. It is checked again because the plan a run is
        // given has to be true of the package in front of it, not of the one the route was written against. A
        // route that carries no binding at all is checked too: a plugin whose schema requires one is unusable
        // without it, and passing the item on to a child that cannot work would be the dishonest answer.
        if (plugin.Manifest.Binding is { } schema && SchemaValidator.Validate(route.Binding, schema) is { Count: > 0 } wrong)
        {
            return Refused(
                AttemptErrors.BindingInvalid,
                $"The binding of the route to plugin '{plugin.Manifest.Id}' does not satisfy the schema that plugin declares.",
                Details(wrong));
        }

        return null;
    }

    /// <summary>
    /// A6. An identifier in the arguments that disagrees with one Jason already recorded is refused rather than
    /// silently resolved either way: the rule is to work by the pin, so the message names the pin. Equal values
    /// pass — a planner repeating what Jason knows contradicts nothing.
    /// </summary>
    private static List<ErrorDetail> Contradictions(OperationContract contract, PreflightFacts facts, JsonObject input)
    {
        var contradicted = new List<ErrorDetail>();
        if (input["args"] is not JsonObject arguments)
        {
            return contradicted;
        }

        foreach (var (kind, declared) in contract.ExternalIds)
        {
            var pins = declared.Entity == PinnedEntity.Contact ? facts.ContactPins : facts.CampaignPins;
            if (!pins.TryGetValue(kind, out var pinned)
                || arguments[kind] is not JsonObject named
                || named["external_id"] is not JsonValue given
                || given.GetValueKind() != JsonValueKind.String
                || !given.TryGetValue(out string? argument)
                || string.Equals(argument, pinned, StringComparison.Ordinal))
            {
                continue;
            }

            contradicted.Add(new ErrorDetail(
                $"/args/{kind}/external_id",
                "conflict",
                $"This runtime has already recorded '{pinned}' as what this plugin calls the {kind}, and a pin is worked by rather than argued with."));
        }

        return contradicted;
    }

    private static IReadOnlyList<ErrorDetail> Details(IReadOnlyList<SchemaProblem> problems) =>
        [.. problems.Select(problem => new ErrorDetail(Pointer(problem.Pointer), problem.Reason, problem.Message))];

    /// <summary>The whole document is a place too, and it is named rather than left blank.</summary>
    private static string Pointer(string pointer) => pointer.Length == 0 ? "/" : pointer;

    private static PreflightVerdict Refused(
        string code,
        string message,
        IReadOnlyList<ErrorDetail>? details = null,
        FailureClass failureClass = FailureClass.Permanent) =>
        new(code, message, failureClass, details, null);
}
