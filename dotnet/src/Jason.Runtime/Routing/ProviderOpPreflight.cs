using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Execution;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.WorkItems;

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
/// True where nothing is wrong with the work and it still may not run: another search of its campaign is in the way, its operation needs a person's approval, or
/// a provider block holds it. A park carries both a code and a plan — the code is what the item says about itself
/// while it waits, and the plan is what the subject of that decision is built from.
/// </param>
/// <param name="Block">
/// The open provider block that holds this work, where one does: on the twenty-sixth check's own park, and beside an
/// approval's, since the work a person releases is still held by the block until it clears or a trial gets past it.
/// </param>
public sealed record PreflightVerdict(
    string? Code,
    string? Message,
    FailureClass? Class,
    IReadOnlyList<ErrorDetail>? Details,
    ProviderOpPlan? Plan,
    Resolution? Resolution,
    bool Parks = false,
    ProviderBlock? Block = null)
{
    /// <summary>Nothing objected: the item may be claimed and run now.</summary>
    public bool Passed => Code is null;

    /// <summary>The work is over before it began: a refusal, and never a decision somebody still has to make.</summary>
    public bool Refused => Code is not null && !Parks;
}

/// <summary>
/// Everything that can stop a provider work item, decided before a child process exists. Twenty-six checks in one
/// order: what the runtime cannot do at all, then what the planner got wrong, then whether another search of the campaign
/// is in the way, then the approval it owes a person, and last whether the provider is refusing such work for now — so a manager meets the reasons an item can never run
/// before the reasons that need their attention, nobody is asked to approve work that would have been refused
/// anyway, and a block the provider holds is the one answer that says nothing about the work itself.
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

    /// <summary>The twenty-six, in the order they are checked. The first that fails is the one the attempt records.</summary>
    /// <remarks>
    /// Checks 7, 19 and 20 have one answer more, <see cref="TransientOutcome"/>, which is not in this list because it is not
    /// one of the reasons an item cannot run: it is what each answers when a pattern of the schema it measures against
    /// could not finish, twice — nothing found wrong, nothing run, and the item back in the queue.
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
        AttemptErrors.SendUnknown,
        AttemptErrors.SendReferenceAbsent,
        AttemptErrors.SendRouteMismatch,
        AttemptErrors.SearchUnknown,
        AttemptErrors.SearchReferenceAbsent,
        AttemptErrors.SearchRouteMismatch,
        AttemptErrors.AudienceUnknown,
        AttemptErrors.InputInvalid,
        AttemptErrors.DefinitionInvalid,
        AttemptErrors.ArgumentUnsupported,
        AttemptErrors.TemplateNotOffered,
        AttemptErrors.ProviderAnswerLost,
        AttemptErrors.SearchUnfinished,
        AttemptErrors.ApprovalRequired,
        AttemptErrors.ProviderBlocked,
    ];

    /// <summary>
    /// What checks 7, 19 and 20 answer when a pattern of the schema they measure against — the plugin's binding schema, the
    /// operation's input schema, the plugin's definition schema — did not finish matching, twice: transient, and the only
    /// answer here the claim puts back in the queue.
    /// </summary>
    public const string TransientOutcome = AttemptErrors.ValidationTimedOut;

    /// <summary>The whole decision, in the published order, with patterns matched the runtime's one way.</summary>
    public static PreflightVerdict Check(PreflightFacts facts, PluginSnapshot plugins, RouteSnapshot routes) =>
        Check(facts, plugins, routes, PatternTiming.Default);

    /// <summary>
    /// What a refusal of a send that may have reached the person says beside its own reason: read alone, the reason would
    /// have a planner create the send again before anyone found out whether the earlier attempt acted.
    /// </summary>
    private const string MayHaveActed =
        "An earlier attempt of this work item may have reached the person, so the work stays as unsettled as that attempt left it: find out at the provider whether it acted before creating the work again.";

    /// <summary>The same for a metered operation whose earlier attempt may have acted, and be spending, at the provider.</summary>
    private const string MayBeSpending =
        "An earlier attempt of this work item may have acted at the provider, and be spending, so the work stays as unsettled as that attempt left it: find out at the provider whether it acted before creating the work again.";

    /// <summary>
    /// The refusals from <c>no_route</c> through <c>template_not_offered</c>, named by the last of them and counted by
    /// its place, so a check added among them is among them: they say nothing of whether an earlier attempt acted. While the
    /// work item carries one that may have reached the person, each keeps its code and is recorded ambiguous, with
    /// <see cref="MayHaveActed"/>.
    /// </summary>
    private static readonly HashSet<string> SilentOnTheEarlierAttempt =
        new(Codes.Take(Codes.ToList().IndexOf(AttemptErrors.TemplateNotOffered) + 1).Skip(1), StringComparer.Ordinal);

    /// <summary>
    /// The whole decision, in the published order, with every schema pattern matched under <paramref name="timing"/>.
    /// While the work item carries an earlier attempt that may have reached the person, of an operation whose repeat
    /// rests on the key, every refusal from the route to the arguments keeps its code and is recorded ambiguous, saying
    /// so: that attempt left the work so, and nothing the claim refuses for settles it.
    /// </summary>
    public static PreflightVerdict Check(PreflightFacts facts, PluginSnapshot plugins, RouteSnapshot routes, PatternTiming timing)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(plugins);
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(timing);

        var verdict = InOrder(facts, plugins, routes, timing);
        return verdict.Code is { } code
            && SilentOnTheEarlierAttempt.Contains(code)
            && facts.Unsettled is not null
            && OperationCatalog.Find(facts.Item.Operation ?? string.Empty) is { } contract
            && OutcomeContract.HeldToTheUnsettledAttempt(contract)
            ? verdict with { Class = FailureClass.Ambiguous, Message = $"{verdict.Message} {(OutcomeContract.SettlesByKey(contract) ? MayHaveActed : MayBeSpending)}" }
            : verdict;
    }

    /// <summary>The twenty-six checks, in the published order: the first that fails is the verdict.</summary>
    private static PreflightVerdict InOrder(PreflightFacts facts, PluginSnapshot plugins, RouteSnapshot routes, PatternTiming timing)
    {
        var operation = facts.Item.Operation ?? string.Empty;
        if (OperationCatalog.Find(operation) is not { } contract)
        {
            // Creation refuses an operation this build does not publish; a row written by a build that did is the
            // reason this is checked rather than assumed.
            return Refused(null, AttemptErrors.OperationUnknown, $"This runtime publishes no contract for operation '{operation}'.");
        }

        if (Resolve(contract, facts, routes) is not { } resolution)
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

        // Twelfth to fourteenth: a read or a take-back of a send acts by the reference the send's own attempt returned,
        // so the send is one this campaign made, that went through with a reference, under the plugin and binding this
        // work is routed to: the reference is that provider account's. The package's digest is not compared, since a
        // reference outlives a version. It is asked before the input is composed, because the input carries that
        // reference.
        if (CanonicalInput.NamesASend(contract)
            && Unnamed(facts.Send, CanonicalInput.NamedSend(facts.Item), Sends, route, operation) is { } unnamed)
        {
            return Refused(resolution, unnamed.Code, unnamed.Message);
        }

        // Fifteenth to seventeenth: a status read or a stop of a search, the same three questions of the search it names,
        // by the reference the search's own attempt returned. A search that answered `finished` inside its call kept no
        // reference, so there is nothing to read or to stop.
        if (CanonicalInput.NamesASearch(contract)
            && Unnamed(facts.Search, CanonicalInput.NamedSearch(facts.Item), Searches, route, operation) is { } unsearched)
        {
            return Refused(resolution, unsearched.Code, unsearched.Message);
        }

        // Eighteenth: an audience named by its number is one the campaign holds, whose definition the input then carries.
        // Asked before the input is composed, because the input carries that definition.
        if (CanonicalInput.CarriesADefinition(contract)
            && CanonicalInput.NamedAudience(facts.Item) is { } number
            && facts.Audience is null)
        {
            return Refused(
                resolution,
                AttemptErrors.AudienceUnknown,
                string.Create(CultureInfo.InvariantCulture, $"This campaign holds no audience under {number} for '{operation}': a campaign numbers its audiences from 1, in the order their first searches were claimed, so name one it holds, or the audience by its definition."));
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
        // is one only the composed input can answer, and creation never sees the composed input. The provider's slot in
        // the arguments is set aside as it was at creation, and travels in the plan as written.
        var problems = contract.CheckInput(input, timing);
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

        // Twentieth: the definition is the provider's, so it is held to the schema the routed plugin declares for it, once
        // the envelope around it satisfies the operation's own, and before a person is asked to approve a search the
        // provider would refuse. A refusal points into the definition, as the envelope's do into the input.
        if (CanonicalInput.CarriesADefinition(contract) && Undefined(plugin, input, route, operation, timing) is { } undefined)
        {
            return undefined with { Resolution = resolution };
        }

        // Twenty-first: an argument the document names per channel goes only where the routed plugin's manifest lists it
        // for the channel the arguments name. It is asked once the input satisfies its schema, so a moment that is no
        // moment is the planner's to fix first, and before a person is asked to approve work that could not go out as
        // written. An argument the work does not carry asks nothing of the manifest.
        if (UntakenArgument(contract, plugin, input, CanonicalInput.ConsumedChannel(contract, facts.Item)) is { } untaken)
        {
            return Refused(
                resolution,
                AttemptErrors.ArgumentUnsupported,
                $"'{route.PluginId}' takes no '{untaken.Argument}' for '{operation}' on the '{untaken.Channel}' channel: its manifest does not list it there. Leave the argument out and set not_before to the moment, or route the operation to a plugin that takes it.");
        }

        // Twenty-second: a message written as a template goes only to a plugin whose manifest lists the operation's channel
        // under `templates`; an absent key means none, so a package that never said it writes a template is handed none,
        // and nothing it would send as written or turn down reaches it. It is asked beside the twenty-first and for the same
        // reason: the planner can write the message as final text, or route it elsewhere, before a person approves it.
        if (Untemplated(contract, plugin, input, CanonicalInput.ConsumedChannel(contract, facts.Item)) is { } untemplated)
        {
            return Refused(
                resolution,
                AttemptErrors.TemplateNotOffered,
                $"'{route.PluginId}' writes no template of '{operation}' on the '{untemplated}' channel: its manifest does not list the channel under templates. Write the message as final text, or route the operation to a plugin that writes a template there.");
        }

        // Twenty-third: a repeat of a send whose lost answer only the work item's key settles is made only under a plugin
        // whose manifest says it settles the send so, and only under the package, by its id and digest, and the binding
        // the attempt that lost it ran under: the key settles a send only at the provider and account that took it, and
        // another version of the package may keep its keys otherwise. A metered operation whose repeat waits on a
        // recovery read is held the same way but for the digest: its recovery read finds what the earlier attempt did at
        // the account, whichever version of the package made it, so an update in between does not refuse it for good.
        // And only with the input that attempt was composed with: a provider holding the key answers a repeat with the
        // send the first request made, so an item edited since, or a person whose address changed since, would be
        // answered for a message the person never got. Its arguments are
        // compared with the snapshot, and the whole document with the digest its claim recorded where it recorded one; the
        // campaign's status is not in the digest, and a pin that attempt or a later attempt of the item learned from its
        // answer is not a change of the request. That attempt may have reached the person, and a repeat anywhere else could reach them again; the work is
        // as unsettled as that attempt left it, so the refusal keeps its class, and nothing has run, so it is asked before a
        // person approves.
        if (facts.Unsettled is { } earlier
            && OutcomeContract.HeldToTheUnsettledAttempt(contract)
            && Unsettled(earlier, facts.Learned, plugin, route, operation, input, OutcomeContract.SettlesByKey(contract)) is { } unsettled)
        {
            return Refused(resolution, AttemptErrors.ProviderAnswerLost, unsettled, failureClass: FailureClass.Ambiguous);
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

        // The block that holds this work, if one does: only an operation whose document says
        // `dispatch.under_provider_block: waits` is held, only by a block of the plugin and binding it is routed to, and
        // a sender's block only where the input names that sender.
        var identity = ProviderBlocks.Identity(route.BindingIdentity);
        var block = OperationCatalog.HeldByProviderBlock.Contains(contract.Id) && facts.Blocks is { Count: > 0 } open
            ? ProviderBlocks.Covering(
                open.Where(candidate => string.Equals(candidate.PluginId, route.PluginId, StringComparison.Ordinal)
                    && string.Equals(candidate.BindingIdentity, identity, StringComparison.Ordinal)),
                input)
            : null;

        // Twenty-fourth: a campaign runs one search at a time, and a second one would spend beside the first — Reply's own
        // refusal covers only a search of the same filters. Asked before the approval, so nobody is asked to approve a spend
        // that cannot start now; not a refusal, since nothing is wrong with the work: it waits, and the claim asks again.
        if (CanonicalInput.MakesASearch(contract.Id) && facts.SearchInTheWay is { } inTheWay)
        {
            return new PreflightVerdict(
                AttemptErrors.SearchUnfinished,
                $"This campaign's search '{inTheWay}' has not ended, or may have started at the provider although its answer was lost, and a campaign runs one search at a time, so this one waits for it and is claimed again a search cadence later, or the held cadence where the provider holds it. Let that search end, or stop it with audience.search_cancel; one whose answer was lost settles at its own next attempt.",
                Class: null,
                Details: null,
                plan,
                resolution,
                Parks: true);
        }

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
                Parks: true,
                block);
        }

        // Twenty-sixth, after the approval: a block says nothing about the work, so a person is asked about it while the
        // provider refuses such work, and the work they release still waits for the block. Nothing has run, so no
        // attempt is made for it unless it is the block's trial, which the claim decides.
        if (block is not null)
        {
            return new PreflightVerdict(
                AttemptErrors.ProviderBlocked,
                ProviderBlocks.HeldMessage(operation, block),
                Class: null,
                Details: null,
                plan,
                resolution,
                Parks: true,
                block);
        }

        return new PreflightVerdict(null, null, null, null, plan, resolution);
    }

    /// <summary>
    /// Where the work is routed: by its campaign's route for its operation and the channel its call names, except a read
    /// or a stop of a search the campaign made with a reference, which goes where that search ran
    /// (<see cref="RouteResolver.ForSearch"/>), whatever its own operation resolves to now. The claim reads its facts by
    /// the same answer.
    /// <para>
    /// The channel is the value of the argument the operation's document names for its route
    /// (<see cref="OperationContract.RouteChannel"/>), read from <c>context.input</c> as the item holds it before a child
    /// exists — not the channel the operation consumes, which a filter such as <c>sender.list</c>'s is not. A call that
    /// names none, or an operation whose document names no such argument, resolves without a channel, by its operation.
    /// </para>
    /// </summary>
    public static Resolution? Resolve(OperationContract contract, PreflightFacts facts, RouteSnapshot routes)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(routes);
        return CanonicalInput.NamesASearch(contract)
            && facts.Search is { Status: WorkItemStatus.Succeeded, Reference: not null, PluginId: { } ranAt } search
            && CanonicalInput.MakesASearch(search.Operation)
            ? RouteResolver.ForSearch(routes, facts.Campaign.PublicId, contract.Id, search.Operation!, ranAt, search.BindingIdentity)
            : RouteResolver.Resolve(routes, facts.Campaign.PublicId, contract.Id, contract.RouteChannel(facts.Item.Context[WorkItemService.InputKey]));
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

        foreach (var (kind, declared) in contract.ExternalIds.Where(entry => entry.Value.Entity != PinnedEntity.WorkItem))
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
    /// The channel the input's message is a template on when the routed plugin's manifest does not list it under
    /// <c>templates</c> for the operation; null when the message is final, or the operation's message is never a template.
    /// </summary>
    private static string? Untemplated(OperationContract contract, LoadedPlugin plugin, JsonObject input, string? channel) =>
        contract.ChannelTemplates is not null
        && channel is not null
        && input["args"]?["message"]?["compose"] is JsonValue compose
        && compose.TryGetValue(out string? written)
        && written == "template"
        && !plugin.Manifest.Templates.Writes(contract.Id, channel)
            ? channel
            : null;

    /// <summary>
    /// The first argument the document names per channel that the input carries and the plugin does not take on the
    /// channel the arguments name, in the document's order; null when there is none.
    /// </summary>
    private static (string Argument, string Channel)? UntakenArgument(
        OperationContract contract,
        LoadedPlugin plugin,
        JsonObject input,
        string? channel)
    {
        if (contract.ChannelArguments is not { } names || channel is null || input["args"] is not JsonObject args)
        {
            return null;
        }

        foreach (var name in names)
        {
            if (args.ContainsKey(name) && !plugin.Manifest.Arguments.Takes(contract.Id, name, channel))
            {
                return (name, channel);
            }
        }

        return null;
    }

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
    /// What a later operation acts on by a provider's reference, and the three codes it is refused with: the noun the
    /// arguments name it by, which operations make one, and what a refusal for a missing reference adds.
    /// </summary>
    private sealed record ReferenceRule(string Noun, Func<string?, bool> Makes, string Unknown, string Absent, string Mismatch, string AbsentNote);

    private static readonly ReferenceRule Sends = new(
        "send",
        CanonicalInput.MakesASend,
        AttemptErrors.SendUnknown,
        AttemptErrors.SendReferenceAbsent,
        AttemptErrors.SendRouteMismatch,
        string.Empty);

    private static readonly ReferenceRule Searches = new(
        "search",
        CanonicalInput.MakesASearch,
        AttemptErrors.SearchUnknown,
        AttemptErrors.SearchReferenceAbsent,
        AttemptErrors.SearchRouteMismatch,
        " A search that answered `finished` inside its own call kept nothing to read or to stop.");

    /// <summary>
    /// Why the work <paramref name="named"/> under the arguments cannot be acted on by its reference, or null where it can:
    /// the campaign holds it, it makes what <paramref name="rule"/> names, it succeeded with a reference, and it ran under
    /// the plugin and binding this work is routed to.
    /// </summary>
    private static (string Code, string Message)? Unnamed(NamedReference? found, string? named, ReferenceRule rule, Route route, string operation)
    {
        if (found is null || !rule.Makes(found.Operation))
        {
            return (rule.Unknown, $"This campaign holds no {rule.Noun} under '{named}' for '{operation}' to act on: no work item of it by that id makes one.");
        }

        if (found.Status != WorkItemStatus.Succeeded || found.Reference is null)
        {
            return (rule.Absent, $"The {rule.Noun} '{found.WorkItem}' has not succeeded with a reference, so the provider kept nothing for '{operation}' to find it by.{rule.AbsentNote}");
        }

        return string.Equals(found.PluginId, route.PluginId, StringComparison.Ordinal)
            && string.Equals(found.BindingIdentity, route.BindingIdentity, StringComparison.Ordinal)
            ? null
            : (rule.Mismatch, $"The {rule.Noun} '{found.WorkItem}' was made under another plugin or account than '{operation}' is routed to, and its reference is that account's, so it is acted on there or not at all.");
    }

    /// <summary>
    /// Why the composed definition is not one the routed plugin takes, or null where it is: it satisfies the schema the
    /// plugin's manifest declares under <c>audience_definition</c>. Every problem points into the input, under
    /// <c>/definition</c>. A plugin that performs an operation carrying a definition declares that schema, or it does not
    /// load; one that somehow did not is refused here rather than handed a definition nothing checked.
    /// </summary>
    internal static PreflightVerdict? Undefined(LoadedPlugin plugin, JsonObject input, Route route, string operation, PatternTiming timing)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(timing);

        if (plugin.Manifest.AudienceDefinition is not { } declared)
        {
            return Refused(
                null,
                AttemptErrors.DefinitionInvalid,
                $"'{route.PluginId}' declares no audience_definition, so nothing says what a definition for '{operation}' carries there.",
                failureClass: FailureClass.Validation);
        }

        var problems = SchemaValidator.Validate(input["definition"], declared.Schema, timing)
            .Select(problem => problem with { Pointer = DefinitionPointer + problem.Pointer })
            .ToList();
        if (SchemaValidator.Verdicts(problems) is { Count: > 0 } refused)
        {
            return Refused(
                null,
                AttemptErrors.DefinitionInvalid,
                $"The definition composed for '{operation}' does not satisfy the schema '{route.PluginId}' declares for an audience's definition.",
                Details(refused),
                FailureClass.Validation);
        }

        return SchemaValidator.Unmeasured(problems)
            ? Unmeasured(null, $"A pattern in the definition schema '{route.PluginId}' declares did not finish matching the composed definition in time, twice, so nothing was decided about it; the item is tried again.", problems)
            : null;
    }

    /// <summary>Where the composed definition is in the input, as a pointer.</summary>
    private const string DefinitionPointer = "/definition";

    /// <summary>
    /// Why a repeat after an attempt that lost its answer cannot be settled under this route, or null where it can. For an
    /// operation the work item's key settles, the routed plugin lists it under <c>settles_by_key</c> and its digest is the
    /// one the attempt's provenance names, since another version of the package may keep its keys otherwise. For every
    /// operation held here, the plugin's id and the binding are the ones that provenance names — a metered operation's
    /// recovery read finds the earlier attempt's act at the account, whichever version of the package made it — the
    /// arguments of <paramref name="input"/> are the ones its snapshot kept, in any order of their keys, and the whole of
    /// <paramref name="input"/> has the name its claim recorded once the pins that attempt <paramref name="learned"/>
    /// itself are left out. An attempt whose provenance does not name the package leaves nothing to compare. One claimed
    /// before attempts named their input names none, and is held to its arguments alone, as it was when it lost its answer.
    /// </summary>
    private static string? Unsettled(Attempt earlier, PinsLearned? learned, LoadedPlugin plugin, Route route, string operation, JsonObject input, bool byKey)
    {
        const string Ask = "so a repeat could make the effect twice; nothing was run. Find out from the provider whether the first attempt acted.";
        var settles = byKey
            ? "the work item's key settles a send only at the provider and the account that took it"
            : "its recovery read finds what that attempt may have started only at the provider and the account that took it";
        var request = byKey ? "the work item's key settles only the request it went with" : "its recovery read finds only the request that attempt went with";
        if (byKey && !plugin.Manifest.SettlesByKey(operation))
        {
            return $"An earlier attempt of this '{operation}' lost its answer, and '{route.PluginId}' does not list '{operation}' under settles_by_key in its manifest, {Ask}";
        }

        if (earlier.Provenance is not { PluginId: { } ranUnder } provenance)
        {
            return $"An earlier attempt of this '{operation}' lost its answer, and its provenance does not say which plugin and account it ran under, {Ask}";
        }

        if (!string.Equals(ranUnder, route.PluginId, StringComparison.Ordinal)
            || (byKey && !string.Equals(provenance.PluginDigest, plugin.Digest, StringComparison.Ordinal))
            || !string.Equals(provenance.BindingIdentity, route.BindingIdentity, StringComparison.Ordinal))
        {
            var where = byKey ? "another plugin, package version or account" : "another plugin or account";
            return $"An earlier attempt of this '{operation}' lost its answer under {where} than '{route.PluginId}' is routed to now, and {settles}, {Ask}";
        }

        if (!JsonNode.DeepEquals(earlier.ContextSnapshot[WorkItemService.InputKey], input["args"]))
        {
            return $"An earlier attempt of this '{operation}' lost its answer with other arguments than the work item carries now, and {request}, {Ask}";
        }

        return provenance.InputDigest is not { } composed || string.Equals(composed, CanonicalInput.Digest(learned is null ? input : CanonicalInput.Without(input, learned)), StringComparison.Ordinal)
            ? null
            : $"An earlier attempt of this '{operation}' lost its answer with another input than the one composed for it now: what the composed input carries of the person or the campaign changed since, and {request}, {Ask}";
    }

    /// <summary>
    /// A check that could not be answered, because a schema pattern did not finish matching, twice. It is not a
    /// refusal of the work — nothing about the item or the route was found wrong — and nothing has run, so it is the
    /// one outcome of these checks that is transient: the item goes back to the queue under the ordinary retry rule.
    /// </summary>
    private static PreflightVerdict Unmeasured(Resolution? resolution, string message, IReadOnlyList<SchemaProblem> problems) =>
        Refused(resolution, TransientOutcome, message, Details(problems), FailureClass.Transient);
}
