using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Configuration;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Registry;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Routing;

/// <summary>What a global route write did to the settings file at its path, as a refusal that could not put the file back says it.</summary>
public enum GlobalRouteWrite
{
    /// <summary>The file held no route there; it holds the one written.</summary>
    Added,

    /// <summary>The file held a route there; it holds the one written in its place.</summary>
    Replaced,

    /// <summary>The file held a route there; it holds none.</summary>
    TakenAway,
}

/// <summary>
/// The words a refusal that could not put the settings file back uses for what the write was about: the thing written,
/// in the singular and the plural, how the write took one away, and the clause a rejected reload's sentence ends its
/// cause with.
/// </summary>
/// <param name="One">The thing in the singular: "route".</param>
/// <param name="Many">The thing in the plural, as it is in force: "routes".</param>
/// <param name="TookAway">How a write that took one out of the file did it: "took away".</param>
/// <param name="NotDone">What became of the write when the reload refused: "the route was not written".</param>
public sealed record PutBackNouns(string One, string Many, string TookAway, string NotDone)
{
    /// <summary>A global route, as <c>route set --global</c> and <c>route unset --global</c> write it.</summary>
    public static PutBackNouns Route { get; } = new("route", "routes", "took away", "the route was not written");
}

/// <summary>
/// The four <c>route.*</c> operations: where one campaign's work would go, where every campaign's work goes, and
/// the two acts that change one campaign's mind.
/// <para>
/// Reading is answered from the active snapshot alone — the same snapshot the claim reads — so
/// <c>route.resolve</c> answers what the claim would decide rather than something that resembles it. Writing is
/// a small activation of its own: the row is written and a new route snapshot is built over the <b>frozen</b>
/// global set, so changing one campaign's route never quietly activates an edit to the settings file that
/// nobody asked to activate.
/// </para>
/// </summary>
public sealed class RouteService(
    JasonDbContext db,
    JournalWriter journal,
    RouteRegistry registry,
    PluginRegistry plugins,
    RouteActivator activator,
    ReloadGate gate,
    TimeProvider clock,
    PluginService reload,
    SettingsWriter writer,
    SettingsFile settingsFile,
    PatternTiming? timing = null)
{
    /// <summary>How a binding is matched against its plugin's patterns: the runtime's one default, which a test replaces.</summary>
    private readonly PatternTiming _timing = timing ?? PatternTiming.Default;

    public const int MaxReasonLength = 2000;

    /// <summary>
    /// What would perform this operation for this campaign, and — when the route cannot be run — why not.
    /// Nothing resolving at all is the business error <c>no_route</c>: that a campaign has no route is a
    /// configuration answer, while a route that resolves and cannot run is a diagnosis, and the two must not
    /// arrive looking alike. A route whose binding no pattern could finish measuring is neither: whether it can run
    /// is not known, so it is answered as a read to ask again.
    /// </summary>
    public async Task<RouteResolutionDto> ResolveAsync(RouteResolveRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var contract = RequireContract(request.Operation);
        var channel = ChannelAsked(request.Channel, contract.Id);
        var campaign = await CampaignService.LoadAsync(db, request.CampaignId, cancellationToken).ConfigureAwait(false);

        var routes = registry.Snapshot;
        var snapshot = plugins.Snapshot;
        if (RouteResolver.Resolve(routes, campaign.PublicId, contract.Id, channel) is not { } resolution)
        {
            throw new NotFoundException(
                AttemptErrors.NoRoute,
                $"No route sends '{contract.Id}'{(channel is null ? string.Empty : $" on '{channel}'")} anywhere for campaign '{campaign.PublicId}'. Write one with route.set, or a global one with route.set and scope global.");
        }

        var route = resolution.Route;
        var plugin = snapshot.Find(route.PluginId);
        var refused = ProviderOpPreflight.Routable(snapshot, contract, route, _timing);

        // Resolve predicts the claim, and the claim would end such an item transient rather than refuse it: a binding
        // no pattern could finish measuring is not "cannot run", so `usable: false` would be a verdict nobody reached.
        // The read is answered as one to ask again, as `route.set` answers the same binding.
        if (refused is { Code: ProviderOpPreflight.TransientOutcome })
        {
            throw DomainErrors.ValidationTimedOut(
            [
                new ErrorDetail(
                    Field(campaign.PublicId, contract.Id, channel, resolution.Scope),
                    SchemaValidator.PatternTimedOut,
                    $"the binding could not be measured against what '{route.PluginId}' declares, so whether this route can run is not known yet."),
            ]);
        }

        return new RouteResolutionDto(
            contract.Id,
            campaign.PublicId,
            route.PluginId,
            plugin?.Manifest.Version,
            plugin?.Digest,
            plugin?.Status,
            resolution.Scope,
            route.Binding,
            route.BindingIdentity,
            routes.Id,
            snapshot.Id,
            contract.Version,
            refused is null,
            Problems(campaign.PublicId, contract.Id, channel, resolution.Scope, refused),
            channel);
    }

    /// <summary>
    /// Every route, or the global set plus one campaign's. The global set is answered either way: it is what a
    /// campaign that names nothing falls back to, so leaving it out would leave out half the answer.
    /// </summary>
    public async Task<RoutesDto> ListAsync(RouteListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // A campaign nobody can name is a typo, and "this campaign has no routes" is an answer somebody acts on.
        var only = request.CampaignId is null
            ? null
            : (await CampaignService.LoadAsync(db, request.CampaignId, cancellationToken).ConfigureAwait(false)).PublicId;

        return ToDto(registry.Snapshot, only);
    }

    /// <summary>
    /// Sends one campaign's work — or one of its operations, or one channel of an operation — to a plugin. The route
    /// is checked before the row is written, so a request that could never be activated is refused as the request it
    /// is rather than stored and then refused by its own activation. A channel route is a row of its own beside the
    /// operation's: setting one never changes the operation's own route, nor the other way round.
    /// </summary>
    /// <param name="insideRun">The attempt a request from inside a run names (<see cref="Api.InsideARun.AttemptOf"/>); null for one from outside.</param>
    public async Task<RoutesDto> SetAsync(RouteSetRequest request, string? insideRun, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Scope == RouteWriteScope.Global)
        {
            RefuseInsideARun(insideRun);
            return await SetGlobalAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var actor = Actors.Resolve(request.Actor);

        var errors = new ValidationErrors();
        var reason = Reason(request.Reason, errors);
        if (string.IsNullOrWhiteSpace(request.Plugin))
        {
            errors.Add("plugin", "required", "plugin is required; a route names the plugin that performs the work.");
        }

        var channel = ChannelOrNone(request.Channel, request.Operation, errors);
        errors.ThrowIfAny();

        var operation = OperationOrDefault(request.Operation);
        var campaign = await LiveCampaignAsync(request.CampaignId, cancellationToken).ConfigureAwait(false);
        var plugin = request.Plugin!.Trim();
        var binding = request.Binding?.DeepClone().AsObject();

        // Before the row, never after it. The plugin set this is checked against is the set the write will be
        // activated over, because both happen under the reload gate.
        if (RouteActivator.Check(plugins.Snapshot, operation, channel, plugin, binding, _timing) is { } problem)
        {
            var detail = new ErrorDetail(RouteActivator.CampaignField(campaign.PublicId, operation, channel), problem.Code, problem.Message);

            // A binding no pattern could finish measuring is not refused: nothing was found wrong with it and nothing
            // has been written, so the request is answered as one to send again.
            throw problem.Code == SchemaValidator.PatternTimedOut
                ? DomainErrors.ValidationTimedOut([detail])
                : new ValidationException([detail]);
        }

        return await ActivateAsync(
            campaign,
            operation,
            channel,
            actor,
            reason,
            async () =>
            {
                var now = clock.GetUtcNow().UtcDateTime;
                if (channel is null)
                {
                    var existing = await OperationRowAsync(campaign, operation, cancellationToken).ConfigureAwait(false);
                    var previous = Recorded(existing?.PluginId, existing?.Binding);
                    if (existing is null)
                    {
                        db.CampaignRoutes.Add(new CampaignRoute
                        {
                            CampaignId = campaign.Id,
                            Operation = operation,
                            PluginId = plugin,
                            Binding = binding,
                            UpdatedAt = now,
                        });
                    }
                    else
                    {
                        existing.PluginId = plugin;
                        existing.Binding = binding;
                        existing.UpdatedAt = now;
                    }

                    return (previous, Recorded(plugin, binding));
                }
                else
                {
                    var existing = await ChannelRowAsync(campaign, operation!, channel, cancellationToken).ConfigureAwait(false);
                    var previous = Recorded(existing?.PluginId, existing?.Binding);
                    if (existing is null)
                    {
                        db.CampaignChannelRoutes.Add(new CampaignChannelRoute
                        {
                            CampaignId = campaign.Id,
                            Operation = operation!,
                            Channel = channel,
                            PluginId = plugin,
                            Binding = binding,
                            UpdatedAt = now,
                        });
                    }
                    else
                    {
                        existing.PluginId = plugin;
                        existing.Binding = binding;
                        existing.UpdatedAt = now;
                    }

                    return (previous, Recorded(plugin, binding));
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Takes one campaign route away, and with it whatever it was overriding. Removing a route that is not there
    /// is not an error — it is the state the caller asked for — and it activates nothing, because nothing changed.
    /// A channel route and its operation's own route are taken away each on its own: the one not named stays. A channel
    /// the operation's document would never let a route name is refused as <c>route.set</c> refuses it, before "not
    /// there" is considered: answering "nothing to take away" for it would leave the route the caller meant in place.
    /// </summary>
    /// <param name="insideRun">The attempt a request from inside a run names (<see cref="Api.InsideARun.AttemptOf"/>); null for one from outside.</param>
    public async Task<RoutesDto> UnsetAsync(RouteUnsetRequest request, string? insideRun, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Scope == RouteWriteScope.Global)
        {
            RefuseInsideARun(insideRun);
            return await UnsetGlobalAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var actor = Actors.Resolve(request.Actor);

        var errors = new ValidationErrors();
        var reason = Reason(request.Reason, errors);
        var channel = ChannelOrNone(request.Channel, request.Operation, errors);
        errors.ThrowIfAny();

        var operation = OperationOrDefault(request.Operation);
        var campaign = await LiveCampaignAsync(request.CampaignId, cancellationToken).ConfigureAwait(false);
        if (operation is not null && channel is not null && RouteActivator.ChannelProblem(operation, channel) is { } problem)
        {
            throw new ValidationException([new ErrorDetail(RouteActivator.CampaignField(campaign.PublicId, operation, channel), problem.Code, problem.Message)]);
        }

        return await ActivateAsync(
            campaign,
            operation,
            channel,
            actor,
            reason,
            async () =>
            {
                if (channel is null)
                {
                    var existing = await OperationRowAsync(campaign, operation, cancellationToken).ConfigureAwait(false);
                    if (existing is null)
                    {
                        return null;
                    }

                    db.CampaignRoutes.Remove(existing);
                    return (Recorded(existing.PluginId, existing.Binding), null);
                }
                else
                {
                    var existing = await ChannelRowAsync(campaign, operation!, channel, cancellationToken).ConfigureAwait(false);
                    if (existing is null)
                    {
                        return null;
                    }

                    db.CampaignChannelRoutes.Remove(existing);
                    return (Recorded(existing.PluginId, existing.Binding), null);
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends one operation's work, one channel of it, or all provider work to a plugin for every campaign with no route of
    /// its own: the route is written to the settings file by the settings writer and frozen by the plugin reload in the
    /// same act, so it is in force once this answers, and a reload that refuses puts the file back. The route is checked
    /// against the active plugins before anything is written, as a campaign's is; the reload over the packages as they
    /// are now is what decides.
    /// </summary>
    private async Task<RoutesDto> SetGlobalAsync(RouteSetRequest request, CancellationToken cancellationToken)
    {
        var actor = Actors.Resolve(request.Actor);

        var errors = new ValidationErrors();
        var reason = Reason(request.Reason, errors);
        if (string.IsNullOrWhiteSpace(request.Plugin))
        {
            errors.Add("plugin", "required", "plugin is required; a route names the plugin that performs the work.");
        }

        NoCampaign(request.CampaignId, errors);
        var channel = ChannelOrNone(request.Channel, request.Operation, errors);
        var binding = request.Binding?.DeepClone().AsObject();
        if (binding is not null && !RoutesOptions.Carries(binding))
        {
            errors.Add(
                "binding",
                "not_representable_globally",
                "binding holds a value the settings file cannot keep as written — a string spelled like a number or a boolean, an empty object or list, keys that differ only in case, or a key with a colon; set it for each campaign with --campaign, which keeps JSON as it is.");
        }

        errors.ThrowIfAny();

        var operation = OperationOrDefault(request.Operation);
        var plugin = request.Plugin!.Trim();
        var path = GlobalPath(operation, channel);
        if (RouteActivator.Check(plugins.Snapshot, operation, channel, plugin, binding, _timing) is { } problem)
        {
            var detail = new ErrorDetail(path, problem.Code, problem.Message);
            throw problem.Code == SchemaValidator.PatternTimedOut
                ? DomainErrors.ValidationTimedOut([detail])
                : new ValidationException([detail]);
        }

        SettingsEdit[] edits =
        [
            SettingsEdit.Set($"{path}:{nameof(RouteEntry.Plugin)}", JsonValue.Create(plugin)),
            binding is null
                ? SettingsEdit.Remove($"{path}:{nameof(RouteEntry.Binding)}")
                : SettingsEdit.Set($"{path}:{nameof(RouteEntry.Binding)}", binding),
        ];
        return await WriteGlobalAsync(operation, channel, edits, new Route(plugin, binding, null), actor, reason, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Takes one global route away, as <see cref="SetGlobalAsync"/> writes one. A route the file does not hold and the
    /// active routes do not either is the state asked for: nothing is written and nothing is activated. The objects the
    /// route leaves empty go with it, and nothing beside it: an operation's own route and its channel routes stay apart.
    /// </summary>
    private async Task<RoutesDto> UnsetGlobalAsync(RouteUnsetRequest request, CancellationToken cancellationToken)
    {
        var actor = Actors.Resolve(request.Actor);

        var errors = new ValidationErrors();
        var reason = Reason(request.Reason, errors);
        NoCampaign(request.CampaignId, errors);
        var channel = ChannelOrNone(request.Channel, request.Operation, errors);
        errors.ThrowIfAny();

        var operation = OperationOrDefault(request.Operation);
        var path = GlobalPath(operation, channel);
        if (operation is not null && channel is not null && RouteActivator.ChannelProblem(operation, channel) is { } problem)
        {
            throw new ValidationException([new ErrorDetail(path, problem.Code, problem.Message)]);
        }

        List<SettingsEdit> edits =
        [
            SettingsEdit.Remove($"{path}:{nameof(RouteEntry.Plugin)}"),
            SettingsEdit.Remove($"{path}:{nameof(RouteEntry.Binding)}"),
            SettingsEdit.RemoveIfEmpty(path),
        ];
        if (channel is not null)
        {
            edits.Add(SettingsEdit.RemoveIfEmpty($"{RouteActivator.GlobalOperationField(operation!)}:{nameof(OperationRouteEntry.Channels)}"));
            edits.Add(SettingsEdit.RemoveIfEmpty(RouteActivator.GlobalOperationField(operation!)));
        }

        return await WriteGlobalAsync(operation, channel, edits, wanted: null, actor, reason, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The global half's one shape: under the reload gate, the settings writer edits the file, and the reload freezes it
    /// with a <c>global_routes_updated</c> line in the same save as its <c>plugins_reloaded</c>. A file the edit left as
    /// it was is reloaded only where the routes in force are not already what was asked, so a route written by hand and
    /// never reloaded is put in force by the same request. A refused reload puts the file back
    /// (<see cref="SettingsWriter"/>), and the answer says whether it did.
    /// </summary>
    private async Task<RoutesDto> WriteGlobalAsync(
        string? operation,
        string? channel,
        IReadOnlyList<SettingsEdit> edits,
        Route? wanted,
        ActorRef actor,
        string? reason,
        CancellationToken cancellationToken)
    {
        await gate.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var before = registry.Snapshot;
            return await writer.WriteAsync(
                edits,
                RoutesOptions.Section,
                async (wrote, token) =>
                {
                    if (!wrote && Same(At(before.Global, operation, channel), wanted))
                    {
                        return ToDto(before, only: null);
                    }

                    // A write has just loaded the file under the settings lock; a file left as it was is read now, as a
                    // reload reads it, since its watch may not yet have told the runtime of a hand edit.
                    var (_, routes) = await reload.ReloadHeldAsync(
                        actor,
                        reason,
                        wrote ? null : settingsFile.Read(),
                        recordRefusal: !wrote,
                        routeRefusal: RouteRefusal,
                        besides: (_, after) =>
                        {
                            var old = Recorded(At(before.Global, operation, channel));
                            var now = Recorded(At(after.Global, operation, channel));
                            if (!JsonNode.DeepEquals(old, now))
                            {
                                journal.Append(db, actor, JournalKinds.GlobalRoutesUpdated, campaign: null, key: JournalKey(operation, channel), old: old, updated: now, reason: reason);
                            }
                        },
                        token).ConfigureAwait(false);
                    return ToDto(routes, only: null);
                },
                (refused, putBack, held) => PutBackSaid(refused, putBack, Written(wanted, held, operation, channel)),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Semaphore.Release();
        }
    }

    /// <summary>A refused route set, answered as a campaign route write answers it where every refusal is a search's or a send's.</summary>
    private static DomainException? RouteRefusal(RouteCandidate candidate) =>
        candidate.Refusals.All(problem => problem.Code == RouteProblemCodes.OrphansSearch)
            ? DomainErrors.RouteOrphansSearch(candidate.Refusals)
            : candidate.Refusals.All(problem => problem.Code == RouteProblemCodes.OrphansSend)
                ? DomainErrors.RouteOrphansSend(candidate.Refusals, candidate.StrandedSends)
                : null;

    /// <summary>
    /// The refusal of a write the reload refused after it, with what became of the file said in one more detail, under the
    /// settings file's own field, and for a rejected reload in its first sentence too.
    /// </summary>
    /// <remarks>
    /// A file that could not be put back holds what the write left, out of force, and no command mends it while loads
    /// refuse: the same command again finds the file as it asks and writes nothing, so its reload refuses with nothing to
    /// put back, and the opposite command is put back as this one was meant to be. So the sentence says which hand edit
    /// mends it — a new route taken out, a replaced route or an unset's route put back — or the reload that makes the file
    /// true. Which of those the write was is read from the file as it was before the write, not from the routes in force,
    /// since the file is what the hand edit mends.
    /// </remarks>
    internal static DomainException PutBackSaid(DomainException refused, SettingsPutBack putBack, GlobalRouteWrite write, PutBackNouns? nouns = null)
    {
        nouns ??= PutBackNouns.Route;
        var (detail, clause) = putBack switch
        {
            SettingsPutBack.PutBack => (
                $"The settings file was put back as it was; the {nouns.Many} in force are unchanged.",
                "the settings file was put back as it was."),
            SettingsPutBack.ChangedSince => (
                $"The settings file was changed by another writer after this one wrote it, so it was left as that writer left it; the {nouns.Many} in force are unchanged.",
                "the settings file was changed by another writer after this one wrote it, so it was left as that writer left it."),
            _ => NotPutBack(putBack, write, nouns),
        };
        var details = refused.Details ?? [];
        var message = refused.Code == "plugin_reload_rejected"
            ? string.Create(CultureInfo.InvariantCulture, $"The plugins were not loaded again: {details.Count} problem(s), so {nouns.NotDone}: {clause}")
            : refused.Message;
        return new DomainException(
            refused.StatusCode,
            refused.Code,
            message,
            refused.Retryable,
            [.. details, new ErrorDetail(PluginLoader.SettingsFileField, SettingsWriter.Code(putBack), detail)]);
    }

    /// <summary>
    /// A file that still holds what the write left: why it could not be put back, what it holds, and the hand edit that
    /// mends it, as a detail's sentences and as a clause of the rejected reload's.
    /// </summary>
    private static (string Detail, string Clause) NotPutBack(SettingsPutBack putBack, GlobalRouteWrite write, PutBackNouns nouns)
    {
        var one = nouns.One;
        var cause = putBack == SettingsPutBack.Held
            ? "another program held the settings file open"
            : "the settings file could not be written again";
        var (state, mend) = write switch
        {
            GlobalRouteWrite.TakenAway => ($"the {one} this {nouns.TookAway} is gone from the file and still in force", $"put the {one} back into the file by hand"),
            GlobalRouteWrite.Replaced => ($"it holds the {one} this wrote, which is not in force, where it held the {one} this replaced", $"put the {one} this replaced back into the file by hand"),
            _ => ($"it holds the {one} this wrote, which is not in force", $"take the {one} out of the file by hand"),
        };
        mend += ", or fix what the load refused and run jason plugin reload";
        return (
            $"{Capital(cause)}, so it was not put back: {state}; the {nouns.Many} in force are unchanged. {Capital(mend)}.",
            $"{cause}, so it was not put back: {state}; {mend}.");

        static string Capital(string text) => string.Concat(char.ToUpperInvariant(text[0]).ToString(), text.AsSpan(1));
    }

    /// <summary>What a global write did to the file at its path: by the file's settings before the write, where it held a plugin there.</summary>
    internal static GlobalRouteWrite Written(Route? wanted, IReadOnlyDictionary<string, string?> held, string? operation, string? channel) =>
        wanted is null ? GlobalRouteWrite.TakenAway
        : held.TryGetValue($"{GlobalPath(operation, channel)}:{nameof(RouteEntry.Plugin)}", out var plugin) && plugin is not null ? GlobalRouteWrite.Replaced
        : GlobalRouteWrite.Added;

    /// <summary>
    /// Nothing inside a dispatched run changes Jason itself, and a global route is Jason's own configuration: it is
    /// refused to a request from inside a run as the gate refuses a plugin's grant, by the same header and with the
    /// same code and a sentence of its own, before anything is read or written. The gate refuses by operation and cannot see the scope, which is
    /// in the body, so the refusal stands here. A campaign's own route is a run's to change, as it always was.
    /// </summary>
    private static void RefuseInsideARun(string? insideRun)
    {
        if (insideRun is not null)
        {
            throw DomainErrors.GlobalRouteInsideARun(insideRun);
        }
    }

    /// <summary>A global route names no campaign: one that does is a caller that meant a campaign's route, or meant both.</summary>
    private static void NoCampaign(string? campaignId, ValidationErrors errors)
    {
        if (!string.IsNullOrWhiteSpace(campaignId))
        {
            errors.Add("campaign_id", "not_allowed", "campaign_id cannot be given with scope global: a global route is every campaign's; leave scope out to route one campaign.");
        }
    }

    /// <summary>The global route's path in the settings file, as a rejected reload names it: <c>Routes:Default</c>, an operation's, or a channel's under it.</summary>
    private static string GlobalPath(string? operation, string? channel) =>
        operation is null ? RouteActivator.GlobalDefaultField
        : channel is null ? RouteActivator.GlobalOperationField(operation)
        : RouteActivator.GlobalChannelField(operation, channel);

    /// <summary>The route a set holds for the default, an operation, or one channel of it; null where it holds none.</summary>
    private static Route? At(RouteSet set, string? operation, string? channel) =>
        operation is null ? set.Default
        : channel is null ? set.Operations.GetValueOrDefault(operation)
        : set.ChannelRoute(operation, channel);

    /// <summary>Whether a route in force is the one asked for: the same plugin and the same binding, or none for none.</summary>
    private static bool Same(Route? held, Route? wanted) =>
        held is null || wanted is null
            ? held is null && wanted is null
            : string.Equals(held.PluginId, wanted.PluginId, StringComparison.Ordinal) && JsonNode.DeepEquals(held.Binding, wanted.Binding);

    private static JsonObject? Recorded(Route? route) => route is null ? null : Recorded(route.PluginId, route.Binding);

    /// <summary>The campaign's default route, or one operation's own route: a row of <c>campaign_routes</c>.</summary>
    private Task<CampaignRoute?> OperationRowAsync(Campaign campaign, string? operation, CancellationToken cancellationToken) =>
        db.CampaignRoutes.FirstOrDefaultAsync(row => row.CampaignId == campaign.Id && row.Operation == operation, cancellationToken);

    /// <summary>One channel's route under an operation: a row of <c>campaign_channel_routes</c>.</summary>
    private Task<CampaignChannelRoute?> ChannelRowAsync(Campaign campaign, string operation, string channel, CancellationToken cancellationToken) =>
        db.CampaignChannelRoutes.FirstOrDefaultAsync(
            row => row.CampaignId == campaign.Id && row.Operation == operation && row.Channel == channel,
            cancellationToken);

    /// <summary>
    /// The key a <c>routes_updated</c> line is filed under: <c>default</c>, the operation's id for its own route, and
    /// <c>&lt;operation&gt;/&lt;channel&gt;</c> for a channel's route, so that two routes are never one key to a reader
    /// that keeps the latest line per key. An operation's id never holds a slash (<see cref="OperationCatalog"/>), so the
    /// first slash is where the channel starts.
    /// </summary>
    internal static string JournalKey(string? operation, string? channel) =>
        operation is null ? RouteActivator.CampaignDefaultName
        : channel is null ? operation
        : $"{operation}/{channel}";

    /// <summary>
    /// The shape both writers share: under the reload gate, change the row, write down what changed, and build a
    /// new route snapshot over the frozen global set. The transaction makes the pair one act — a candidate that
    /// refused itself leaves no row behind, and nothing is swapped until the write is committed.
    /// </summary>
    /// <param name="change">
    /// Changes the row and answers with the old and the new value for the journal, or null when there was
    /// nothing to change at all.
    /// </param>
    private async Task<RoutesDto> ActivateAsync(
        Campaign campaign,
        string? operation,
        string? channel,
        ActorRef actor,
        string? reason,
        Func<Task<(JsonNode? Old, JsonNode? New)?>> change,
        CancellationToken cancellationToken)
    {
        await gate.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                if (await change().ConfigureAwait(false) is not { } changed)
                {
                    return ToDto(registry.Snapshot, only: null);
                }

                journal.Append(
                    db,
                    actor,
                    JournalKinds.RoutesUpdated,
                    campaign,
                    key: JournalKey(operation, channel),
                    old: changed.Old,
                    updated: changed.New,
                    reason: reason);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                // A5. The global half is copied from the snapshot that is already active and never read again
                // from the settings file: changing one campaign's route is not the act that activates a global edit.
                var candidate = await activator.BuildAsync(plugins.Snapshot, GlobalRouteSource.FromActiveSnapshot, cancellationToken).ConfigureAwait(false);
                if (candidate.Snapshot is null)
                {
                    // This route was checked before it was written, so reaching here means another route in the
                    // set no longer holds, or the set would leave a search still followed, or a send still held for a
                    // later moment, with no route to the account it ran under, each refused under a code of its own. Nothing is committed and nothing is
                    // swapped, and the answer names every route or search that refused — exactly as a rejected reload
                    // does — and only those: a binding that merely went unmeasured is no refusal, and alone it makes
                    // this a write to send again.
                    throw candidate.Unmeasured
                        ? DomainErrors.ValidationTimedOut(candidate.Problems)
                        : candidate.Refusals.All(problem => problem.Code == RouteProblemCodes.OrphansSearch)
                            ? DomainErrors.RouteOrphansSearch(candidate.Refusals)
                            : candidate.Refusals.All(problem => problem.Code == RouteProblemCodes.OrphansSend)
                                ? DomainErrors.RouteOrphansSend(candidate.Refusals, candidate.StrandedSends)
                                : DomainErrors.PluginReloadRejected(candidate.Refusals);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                activator.Activate(candidate.Snapshot);
                return ToDto(candidate.Snapshot, only: null);
            }
        }
        finally
        {
            gate.Semaphore.Release();
        }
    }

    /// <summary>The campaign a route may be written for: archived work never dispatches, so it is never re-routed.</summary>
    private async Task<Campaign> LiveCampaignAsync(string? campaignId, CancellationToken cancellationToken)
    {
        var campaign = await CampaignService.LoadAsync(db, campaignId, cancellationToken).ConfigureAwait(false);
        return campaign.Status == CampaignStatus.Archived
            ? throw DomainErrors.CampaignArchived(campaign.PublicId)
            : campaign;
    }

    /// <summary>What the journal records a route as, and nothing where there was no route.</summary>
    /// <remarks>A channel route is recorded exactly as an operation's own route is; which route it is, the key says.</remarks>
    private static JsonObject? Recorded(string? plugin, JsonObject? binding) =>
        plugin is null ? null : new JsonObject { ["plugin"] = plugin, ["binding"] = binding?.DeepClone() };

    internal static OperationContract RequireContract(string? operation)
    {
        if (string.IsNullOrWhiteSpace(operation))
        {
            throw DomainErrors.Required("operation");
        }

        return OperationCatalog.Find(operation.Trim())
            ?? throw new ValidationException(
            [
                new ErrorDetail(
                    "operation",
                    "unknown",
                    "operation must name an operation this runtime publishes a contract for: "
                        + string.Join(", ", OperationCatalog.All.Select(published => published.Id)) + "."),
            ]);
    }

    /// <summary>An absent operation is the campaign's default route; a blank one is that same absence, typed out.</summary>
    private static string? OperationOrDefault(string? operation) =>
        string.IsNullOrWhiteSpace(operation) ? null : RequireContract(operation).Id;

    /// <summary>
    /// The channel a request names, or none where it names none — left out or null. A channel that is there and blank is
    /// refused rather than read as none: that would address the operation's own route, and a set would replace it and an
    /// unset take it away, when the caller named a channel's route — most likely one an empty variable or field left
    /// out. A channel is a channel of an operation, so one named without an operation — under the default, which has
    /// none — is refused as the operation missing. Whether the operation routes by channel, and by this one, is the
    /// route check's to say.
    /// </summary>
    private static string? ChannelOrNone(string? channel, string? operation, ValidationErrors errors)
    {
        if (channel is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(channel))
        {
            errors.Add("channel", "blank", "channel is blank. Leave it out to name the operation's own route, or name the channel whose route this is.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(operation))
        {
            errors.Add("operation", "required", "operation is required with a channel; a channel route is a route of one operation, and the default names none.");
        }

        return channel.Trim();
    }

    /// <summary>
    /// The channel a read names for the operation it asks about, or none where it names none. A read holds the channel to
    /// what a write would: blank is refused rather than read as none, and a channel the operation's document would never
    /// let a route name is refused, since answering for it would describe a call no claim can make.
    /// </summary>
    internal static string? ChannelAsked(string? channel, string operation)
    {
        if (channel is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(channel))
        {
            throw new ValidationException(
                [new ErrorDetail("channel", "blank", "channel is blank. Leave it out to ask about a call that names none, or name the channel the call names.")]);
        }

        var named = channel.Trim();
        return RouteActivator.ChannelProblem(operation, named) is { } problem
            ? throw new ValidationException([new ErrorDetail("channel", problem.Code, problem.Message)])
            : named;
    }

    private static string? Reason(string? reason, ValidationErrors errors)
    {
        if (reason is not null && reason.Trim().Length > MaxReasonLength)
        {
            errors.Add("reason", "too_long", string.Create(CultureInfo.InvariantCulture, $"reason must be at most {MaxReasonLength} characters."));
        }

        return string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    /// <summary>
    /// Where the problem is, spelled the way the operator wrote the route — the same name a rejected reload uses
    /// for it, so two answers about one route read as one vocabulary.
    /// </summary>
    private static IReadOnlyList<ErrorDetail> Problems(string campaignId, string operation, string? channel, RouteScope scope, PreflightVerdict? refused)
    {
        return refused is null
            ? []
            : [new ErrorDetail(Field(campaignId, operation, channel, scope), refused.Code!, refused.Message!), .. refused.Details ?? []];
    }

    /// <summary>
    /// The route that won, named the way the operator wrote it: each of the six scopes by its own name, a channel route
    /// under its operation and channel. A scope this does not name is a scope added without its name, so it throws
    /// rather than answer with another route's name.
    /// </summary>
    internal static string Field(string campaignId, string operation, string? channel, RouteScope scope) => scope switch
    {
        RouteScope.CampaignChannel => RouteActivator.CampaignField(campaignId, operation, Named(channel, scope)),
        RouteScope.CampaignOperation => RouteActivator.CampaignField(campaignId, operation),
        RouteScope.CampaignDefault => RouteActivator.CampaignField(campaignId, null),
        RouteScope.GlobalChannel => RouteActivator.GlobalChannelField(operation, Named(channel, scope)),
        RouteScope.GlobalOperation => RouteActivator.GlobalOperationField(operation),
        RouteScope.GlobalDefault => RouteActivator.GlobalDefaultField,
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "a route scope with no name in a problem."),
    };

    /// <summary>The channel a channel scope won by: such a scope is reached only by a call that named one.</summary>
    private static string Named(string? channel, RouteScope scope) =>
        channel ?? throw new ArgumentNullException(nameof(channel), $"a route that won at {scope} won by a channel the call named.");

    private static RoutesDto ToDto(RouteSnapshot snapshot, string? only)
    {
        var campaigns = snapshot.Campaigns
            .Where(entry => only is null || string.Equals(entry.Key, only, StringComparison.Ordinal))
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new CampaignRoutesDto(entry.Key, ToDto(entry.Value.Default), Overrides(entry.Value), Channels(entry.Value)))
            .ToList();

        return new RoutesDto(
            snapshot.Id,
            snapshot.PluginSnapshotId,
            new RouteSetDto(ToDto(snapshot.Global.Default), Overrides(snapshot.Global), Channels(snapshot.Global)),
            campaigns);
    }

    private static IReadOnlyDictionary<string, RouteDto> Overrides(RouteSet set) =>
        set.Operations
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .ToDictionary(entry => entry.Key, entry => ToDto(entry.Value)!, StringComparer.Ordinal);

    /// <summary>A set's channel routes, operation then channel, each ordinally; empty, never absent, where it has none.</summary>
    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, RouteDto>> Channels(RouteSet set) =>
        set.ChannelRoutes
            .GroupBy(entry => entry.Operation, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyDictionary<string, RouteDto>)group.ToDictionary(entry => entry.Channel, entry => ToDto(entry.Route)!, StringComparer.Ordinal),
                StringComparer.Ordinal);

    private static RouteDto? ToDto(Route? route) =>
        route is null ? null : new RouteDto(route.PluginId, route.Binding, route.BindingIdentity);
}
