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
/// does not satisfy its own schema is a validation failure — the one a planner can fix by editing the item. A
/// document a schema pattern could not finish measuring is neither: nothing was found wrong and nothing has run,
/// so it is transient, and the only outcome here the claim puts back in the queue.
/// </param>
/// <param name="Details">Where the input failed, by pointer, for the one check that can name more than one place.</param>
/// <param name="Plan">Everything the run needs, and null whenever something refused it.</param>
/// <param name="Resolution">
/// Where the work was routed, present whenever routing succeeded — including when a later check refused the
/// item. It is what lets a refused attempt still record the plugin it would have used: computing that answer a
/// second time would make two places that can disagree about one decision.
/// </param>
/// <param name="Parks">
/// True where nothing is wrong with the work and it still may not run: its operation needs a person's approval.
/// A park carries both a code and a plan — the code is what the item says about itself while it waits, and the
/// plan is what the subject of that decision is built from.
/// </param>
public sealed record PreflightVerdict(
    string? Code,
    string? Message,
    FailureClass? Class,
    IReadOnlyList<ErrorDetail>? Details,
    ProviderOpPlan? Plan,
    Resolution? Resolution,
    bool Parks = false)
{
    /// <summary>Nothing objected: the item may be claimed and run now.</summary>
    public bool Passed => Code is null;

    /// <summary>The work is over before it began: a refusal, and never a decision somebody still has to make.</summary>
    public bool Refused => Code is not null && !Parks;
}

/// <summary>
/// Everything that can stop a provider work item, decided before a child process exists. Thirteen checks in one
/// order: what the runtime cannot do at all, then what the planner got wrong, and last the gate it owes a
/// person — so a manager meets the reasons an item can never run before the reasons that need their attention,
/// and nobody is asked to approve work that would have been refused anyway.
/// </summary>
/// <remarks>
/// <para>
/// A pure function over <see cref="PreflightFacts"/> and two immutable snapshots, matching schema patterns under
/// the timing every validating site is handed: no database, no clock, no options. The claimer reads the facts
/// inside the claim transaction and this decides on them, which is what keeps the claim to a handful of indexed
/// reads and lets the order above be a unit test with no fixture.
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

    /// <summary>The kind of the pin by which a provider knows a person.</summary>
    private const string ContactPin = "contact";

    /// <summary>The thirteen, in the order they are checked. The first that fails is the one the attempt records.</summary>
    /// <remarks>
    /// Checks 7 and 12 have one answer more, <see cref="TransientOutcome"/>, which is not in this list because it is not
    /// one of the reasons an item cannot run: it is what either check answers when a pattern of the schema it measures
    /// against could not finish, twice — nothing found wrong, nothing run, and the item back in the queue.
    /// </remarks>
    public static IReadOnlyList<string> Codes { get; } =
    [
        AttemptErrors.OperationUnknown,
        AttemptErrors.NoRoute,
        AttemptErrors.PluginNotLoaded,
        AttemptErrors.PluginUnavailable,
        AttemptErrors.PluginOperationUnsupported,
        AttemptErrors.ContractIncompatible,
        AttemptErrors.BindingInvalid,
        AttemptErrors.ContactRequired,
        AttemptErrors.NoChannelValue,
        AttemptErrors.Suppressed,
        AttemptErrors.ContactNotLinked,
        AttemptErrors.InputInvalid,
        AttemptErrors.ApprovalRequired,
    ];

    /// <summary>
    /// What checks 7 and 12 answer when a pattern of the schema they measure against — the plugin's binding schema, the
    /// operation's input schema — did not finish matching, twice: transient, and the only answer here the claim puts
    /// back in the queue.
    /// </summary>
    public const string TransientOutcome = AttemptErrors.ValidationTimedOut;

    /// <summary>The whole decision, in the published order, with patterns matched the runtime's one way.</summary>
    public static PreflightVerdict Check(PreflightFacts facts, PluginSnapshot plugins, RouteSnapshot routes) =>
        Check(facts, plugins, routes, PatternTiming.Default);

    /// <summary>The whole decision, in the published order, with every schema pattern matched under <paramref name="timing"/>.</summary>
    public static PreflightVerdict Check(PreflightFacts facts, PluginSnapshot plugins, RouteSnapshot routes, PatternTiming timing)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(plugins);
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(timing);

        var operation = facts.Item.Operation ?? string.Empty;
        if (OperationCatalog.Find(operation) is not { } contract)
        {
            // Creation refuses an operation this build does not publish; a row written by a build that did is the
            // reason this is checked rather than assumed.
            return Refused(null, AttemptErrors.OperationUnknown, $"This runtime publishes no contract for operation '{operation}'.");
        }

        if (RouteResolver.Resolve(routes, facts.Campaign.PublicId, operation) is not { } resolution)
        {
            return Refused(null, AttemptErrors.NoRoute, $"No provider route exists yet for operation '{operation}'.");
        }

        var route = resolution.Route;
        if (Routable(plugins, contract, route, timing) is { } unroutable)
        {
            // Task 5 made these five checks shared with route.resolve, which predicts this decision; the
            // resolution is attached here rather than inside them, because resolve has no attempt to record it on.
            return unroutable with { Resolution = resolution };
        }

        // Nothing objected, so the plugin the checks above found is there to be found again.
        var plugin = plugins.Find(route.PluginId)!;

        if (contract.Preflight.Contact == ContactRequirement.Required && facts.Contact is null)
        {
            return Refused(resolution, AttemptErrors.ContactRequired, $"Operation '{operation}' acts on a contact, and this work item names none.");
        }

        // A channel the arguments never named is not a person's problem: the composed document says so by
        // pointer below, which is the answer that tells a planner what to write.
        if (CanonicalInput.ConsumedChannel(contract, facts.Item) is { } channel && facts.Contact is { } contact)
        {
            if (CanonicalInput.Reachable(contact, channel) is null)
            {
                return Refused(
                    resolution,
                    AttemptErrors.NoChannelValue,
                    $"The contact has no '{channel}' channel, and '{channel}' is the one '{operation}' consumes.");
            }

            // The register refuses what would reach the person. An operation that only takes them out declares
            // that it does not apply, and a suppressed person is exactly who that operation is for.
            if (facts.Suppressed && contract.Preflight.Suppression == SuppressionCheck.Checked)
            {
                return Refused(
                    resolution,
                    AttemptErrors.Suppressed,
                    $"The contact's '{channel}' value is on the suppression list, so this campaign does not reach them there.");
            }
        }

        // An operation that works by the routed plugin's pin alone has nobody to act on when that plugin has pinned
        // nobody for the person, and it looks nobody up. A pin the plugin holds at another value still names a
        // person there, so a divergence is no reason here; a pin held only by another plugin names nobody here.
        if (contract.Preflight.ProviderContact == ProviderContact.PinnedOnly
            && facts.Contact is not null
            && !facts.ContactPins.ContainsKey(ContactPin))
        {
            return Refused(
                resolution,
                AttemptErrors.ContactNotLinked,
                $"Operation '{operation}' acts only on the person '{route.PluginId}' has pinned, and '{route.PluginId}' has pinned nobody for this contact.");
        }

        var input = CanonicalInput.Compose(contract, facts);
        if (Contradictions(contract, facts, input) is { Count: > 0 } contradicted)
        {
            return Refused(
                resolution,
                AttemptErrors.InputInvalid,
                $"The arguments of this work item contradict an identifier Jason has already recorded for '{operation}'.",
                contradicted,
                FailureClass.Validation);
        }

        // The whole document, not only the arguments: a rule over the root — "the campaign is named somehow" —
        // is one only the composed input can answer, and creation never sees the composed input.
        var problems = SchemaValidator.Validate(input, contract.InputSchema, timing);
        if (SchemaValidator.Verdicts(problems) is { Count: > 0 } refused)
        {
            return Refused(
                resolution,
                AttemptErrors.InputInvalid,
                $"The input composed for '{operation}' does not satisfy the schema that operation publishes.",
                Details(refused),
                FailureClass.Validation);
        }

        // A pattern that did not finish matching, twice, answers nothing about the input, so this is not
        // `input_invalid`: nobody has anything to edit. Nothing has happened either — no child exists — so the
        // attempt is transient, and the next scan asks the same question of a warm engine.
        if (SchemaValidator.Unmeasured(problems))
        {
            return Unmeasured(resolution, $"A pattern in the schema '{operation}' publishes did not finish matching the composed input in time, twice, so nothing was decided about it; the item is tried again.", problems);
        }

        var plan = new ProviderOpPlan(
            plugin,
            plugins.Id,
            routes.Id,
            resolution.Scope,
            route.Binding,
            route.BindingIdentity,
            contract,
            input);

        // Asked last, and last on purpose. What a person is asked to approve is the composed input, so it has to
        // be composed and to satisfy the operation's own schema before anybody can be shown it — and nobody is
        // ever asked to approve reaching somebody the suppression register already protects.
        //
        // The published value is read and the condition beside it is not. A conditional block always states the
        // dangerous reading in `value`; campaign.enroll's condition only decides whether a preview is mandatory,
        // and a preview is built every time, so there is nothing here for a condition to soften. Both words a person
        // gives park the work — `confirm_once` and `confirm_each` — and the loader refuses any other: a work item is
        // one person, so the decision this parks for is one per item, bound to the composed input, which is what
        // `confirm_each` asks of every send.
        if (!string.Equals(contract.Approval.Value, Automatic, StringComparison.Ordinal))
        {
            return new PreflightVerdict(
                AttemptErrors.ApprovalRequired,
                $"Operation '{operation}' is approved '{contract.Approval.Value}', and a dispatcher may not stand in for the person who approves it.",
                Class: null,
                Details: null,
                plan,
                resolution,
                Parks: true);
        }

        return new PreflightVerdict(null, null, null, null, plan, resolution);
    }

    /// <summary>
    /// The half of the decision that needs nothing but the route and the plugin set behind it: whether the
    /// plugin a route names would be handed this operation at all. Null when it would be.
    /// <para>
    /// It is its own function because <c>route.resolve</c> answers exactly this, before any work item exists —
    /// and an operator asking "why will this campaign not run?" has to be told what the claim would decide,
    /// rather than something that resembles it because two places were kept in step by hand.
    /// </para>
    /// <para>
    /// The questions are <see cref="RouteChecks"/>, which activation asked of the same route when it froze it;
    /// what belongs here is the order they are asked in and the words a manager is answered with.
    /// </para>
    /// </summary>
    public static PreflightVerdict? Routable(PluginSnapshot plugins, OperationContract contract, Route route) =>
        Routable(plugins, contract, route, PatternTiming.Default);

    /// <summary>The same half of the decision, with the binding's patterns matched under <paramref name="timing"/>.</summary>
    public static PreflightVerdict? Routable(PluginSnapshot plugins, OperationContract contract, Route route, PatternTiming timing)
    {
        ArgumentNullException.ThrowIfNull(plugins);
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(timing);

        if (RouteChecks.Loaded(plugins, route.PluginId) is not { } plugin)
        {
            return Refused(
                null,
                AttemptErrors.PluginNotLoaded,
                $"The route for '{contract.Id}' names plugin '{route.PluginId}', which the active plugin set does not hold.");
        }

        // Refused here and nowhere in activation: a package that cannot run on this machine is an environment's
        // problem, and refusing every route to it would answer a missing program with a rejected reload. Activation
        // reads one environmental code, `probe_timed_out`, only to leave such a plugin's bindings unmeasured; it refuses
        // nothing for it, and this check, before the binding is measured below, is what every claim on its routes meets.
        if (RouteChecks.Unavailable(plugin))
        {
            return Refused(
                null,
                AttemptErrors.PluginUnavailable,
                $"Plugin '{plugin.Manifest.Id}' is installed but unavailable on this machine; reload after repairing what it needs.");
        }

        // The kind is asked first: a plugin that only notifies lists no operation at all, so asking "does it
        // implement this one?" first would answer with what is missing rather than with what it is.
        if (RouteChecks.KindNotInvocable(plugin))
        {
            return Refused(
                null,
                AttemptErrors.PluginOperationUnsupported,
                $"Plugin '{plugin.Manifest.Id}' is not a kind of plugin that performs canonical operations.");
        }

        if (RouteChecks.OperationUnsupported(plugin, contract.Id))
        {
            return Refused(
                null,
                AttemptErrors.PluginOperationUnsupported,
                $"Plugin '{plugin.Manifest.Id}' does not perform '{contract.Id}', and work is never handed to a plugin the route did not name.");
        }

        if (RouteChecks.ContractIncompatible(plugin, contract))
        {
            return Refused(
                null,
                AttemptErrors.ContractIncompatible,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Plugin '{plugin.Manifest.Id}' speaks no version of the '{contract.Id}' contract that this runtime publishes, which is version {contract.Version}."));
        }

        // Activation already held this binding to the same schema. It is checked again because the plan a run is
        // given has to be true of the package in front of it, not of the one the route was written against. A
        // route that carries no binding at all is checked too, as the empty object: a plugin whose schema refuses
        // that requires a binding and is unusable without one, and passing the item on to a child that cannot work
        // would be the dishonest answer.
        var bindingProblems = RouteChecks.BindingProblems(plugin, route.Binding, timing);
        if (SchemaValidator.Verdicts(bindingProblems) is { Count: > 0 } wrong)
        {
            return Refused(
                null,
                AttemptErrors.BindingInvalid,
                route.Binding is null
                    ? $"The route to plugin '{plugin.Manifest.Id}' carries no binding, and the schema that plugin declares requires one."
                    : $"The binding of the route to plugin '{plugin.Manifest.Id}' does not satisfy the schema that plugin declares.",
                Details(wrong));
        }

        // The same rule as the composed input's: a pattern that did not finish is no verdict on the binding.
        return SchemaValidator.Unmeasured(bindingProblems)
            ? Unmeasured(null, $"A pattern in the binding schema plugin '{plugin.Manifest.Id}' declares did not finish matching the route's binding in time, twice, so nothing was decided about it; the item is tried again.", bindingProblems)
            : null;
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

    private static IReadOnlyList<ErrorDetail> Details(IReadOnlyList<SchemaProblem> problems) => ErrorDetail.From(problems);

    /// <summary>
    /// One refusal, carrying the resolution the decision had reached when it refused: the attempt records what
    /// was chosen even where the work never ran, which is the half of a failure a manager can act on.
    /// </summary>
    private static PreflightVerdict Refused(
        Resolution? resolution,
        string code,
        string message,
        IReadOnlyList<ErrorDetail>? details = null,
        FailureClass failureClass = FailureClass.Permanent) =>
        new(code, message, failureClass, details, null, resolution);

    /// <summary>
    /// A check that could not be answered, because a schema pattern did not finish matching, twice. It is not a
    /// refusal of the work — nothing about the item or the route was found wrong — and nothing has run, so it is the
    /// one outcome of these checks that is transient: the item goes back to the queue under the ordinary retry rule.
    /// </summary>
    private static PreflightVerdict Unmeasured(Resolution? resolution, string message, IReadOnlyList<SchemaProblem> problems) =>
        Refused(resolution, TransientOutcome, message, Details(problems), FailureClass.Transient);
}
