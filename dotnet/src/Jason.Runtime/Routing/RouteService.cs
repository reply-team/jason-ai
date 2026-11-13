using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Registry;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Routing;

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
        var campaign = await CampaignService.LoadAsync(db, request.CampaignId, cancellationToken).ConfigureAwait(false);

        var routes = registry.Snapshot;
        var snapshot = plugins.Snapshot;
        if (RouteResolver.Resolve(routes, campaign.PublicId, contract.Id, channel: null) is not { } resolution)
        {
            throw new NotFoundException(
                AttemptErrors.NoRoute,
                $"No route sends '{contract.Id}' anywhere for campaign '{campaign.PublicId}'. Write one with route.set, or a global one in the settings file.");
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
                    Field(campaign.PublicId, contract.Id, channel: null, resolution.Scope),
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
            Problems(campaign.PublicId, contract.Id, resolution.Scope, refused));
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
    public async Task<RoutesDto> SetAsync(RouteSetRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
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
            existing =>
            {
                var previous = Recorded(existing);
                if (existing is null)
                {
                    db.CampaignRoutes.Add(new CampaignRoute
                    {
                        CampaignId = campaign.Id,
                        Operation = operation,
                        Channel = channel,
                        PluginId = plugin,
                        Binding = binding,
                        UpdatedAt = clock.GetUtcNow().UtcDateTime,
                    });
                }
                else
                {
                    existing.PluginId = plugin;
                    existing.Binding = binding;
                    existing.UpdatedAt = clock.GetUtcNow().UtcDateTime;
                }

                return (previous, Recorded(plugin, binding, channel));
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
    public async Task<RoutesDto> UnsetAsync(RouteUnsetRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
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
            existing =>
            {
                if (existing is null)
                {
                    return null;
                }

                db.CampaignRoutes.Remove(existing);
                return (Recorded(existing), null);
            },
            cancellationToken).ConfigureAwait(false);
    }

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
        Func<CampaignRoute?, (JsonNode? Old, JsonNode? New)?> change,
        CancellationToken cancellationToken)
    {
        await gate.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                var existing = await db.CampaignRoutes
                    .FirstOrDefaultAsync(row => row.CampaignId == campaign.Id && row.Operation == operation && row.Channel == channel, cancellationToken)
                    .ConfigureAwait(false);

                if (change(existing) is not { } changed)
                {
                    return ToDto(registry.Snapshot, only: null);
                }

                journal.Append(
                    db,
                    actor,
                    JournalKinds.RoutesUpdated,
                    campaign,
                    key: operation ?? RouteActivator.CampaignDefaultName,
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
                    // set no longer holds, or the set would leave a search still followed with no route to the
                    // account it ran under, refused under a code of its own. Nothing is committed and nothing is
                    // swapped, and the answer names every route or search that refused — exactly as a rejected reload
                    // does — and only those: a binding that merely went unmeasured is no refusal, and alone it makes
                    // this a write to send again.
                    throw candidate.Unmeasured
                        ? DomainErrors.ValidationTimedOut(candidate.Problems)
                        : candidate.Refusals.All(problem => problem.Code == RouteProblemCodes.OrphansSearch)
                            ? DomainErrors.RouteOrphansSearch(candidate.Refusals)
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
    private static JsonNode? Recorded(CampaignRoute? route) =>
        route is null ? null : Recorded(route.PluginId, route.Binding, route.Channel);

    /// <summary>
    /// A route as <c>routes_updated</c> records it: its plugin and binding, and its channel where it names one. The key
    /// stays the operation, so a reader that keys on it reads a channel route as a route of that operation; the channel
    /// is said beside the route rather than folded into the key.
    /// </summary>
    private static JsonObject Recorded(string plugin, JsonObject? binding, string? channel)
    {
        var recorded = new JsonObject { ["plugin"] = plugin, ["binding"] = binding?.DeepClone() };
        if (channel is not null)
        {
            recorded["channel"] = channel;
        }

        return recorded;
    }

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
    private static IReadOnlyList<ErrorDetail> Problems(string campaignId, string operation, RouteScope scope, PreflightVerdict? refused)
    {
        return refused is null
            ? []
            : [new ErrorDetail(Field(campaignId, operation, channel: null, scope), refused.Code!, refused.Message!), .. refused.Details ?? []];
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
