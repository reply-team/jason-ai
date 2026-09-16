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
    TimeProvider clock)
{
    public const int MaxReasonLength = 2000;

    /// <summary>
    /// What would perform this operation for this campaign, and — when the route cannot be run — why not.
    /// Nothing resolving at all is the business error <c>no_route</c>: that a campaign has no route is a
    /// configuration answer, while a route that resolves and cannot run is a diagnosis, and the two must not
    /// arrive looking alike.
    /// </summary>
    public async Task<RouteResolutionDto> ResolveAsync(RouteResolveRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var contract = RequireContract(request.Operation);
        var campaign = await CampaignService.LoadAsync(db, request.CampaignId, cancellationToken).ConfigureAwait(false);

        var routes = registry.Snapshot;
        var snapshot = plugins.Snapshot;
        if (RouteResolver.Resolve(routes, campaign.PublicId, contract.Id) is not { } resolution)
        {
            throw new NotFoundException(
                AttemptErrors.NoRoute,
                $"No route sends '{contract.Id}' anywhere for campaign '{campaign.PublicId}'. Write one with route.set, or a global one in the settings file.");
        }

        var route = resolution.Route;
        var plugin = snapshot.Find(route.PluginId);
        var refused = ProviderOpPreflight.Routable(snapshot, contract, route);

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
    /// Sends one campaign's work — or one of its operations — to a plugin. The route is checked before the row
    /// is written, so a request that could never be activated is refused as the request it is rather than stored
    /// and then refused by its own activation.
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

        errors.ThrowIfAny();

        var operation = OperationOrDefault(request.Operation);
        var campaign = await LiveCampaignAsync(request.CampaignId, cancellationToken).ConfigureAwait(false);
        var plugin = request.Plugin!.Trim();
        var binding = request.Binding?.DeepClone().AsObject();

        // Before the row, never after it. The plugin set this is checked against is the set the write will be
        // activated over, because both happen under the reload gate.
        if (RouteActivator.Check(plugins.Snapshot, operation, plugin, binding) is { } problem)
        {
            throw new ValidationException(
                [new ErrorDetail(RouteActivator.CampaignField(campaign.PublicId, operation), problem.Code, problem.Message)]);
        }

        return await ActivateAsync(
            campaign,
            operation,
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

                return (previous, new JsonObject { ["plugin"] = plugin, ["binding"] = binding?.DeepClone() });
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Takes one campaign route away, and with it whatever it was overriding. Removing a route that is not there
    /// is not an error — it is the state the caller asked for — and it activates nothing, because nothing changed.
    /// </summary>
    public async Task<RoutesDto> UnsetAsync(RouteUnsetRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = Actors.Resolve(request.Actor);

        var errors = new ValidationErrors();
        var reason = Reason(request.Reason, errors);
        errors.ThrowIfAny();

        var operation = OperationOrDefault(request.Operation);
        var campaign = await LiveCampaignAsync(request.CampaignId, cancellationToken).ConfigureAwait(false);

        return await ActivateAsync(
            campaign,
            operation,
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
                    .FirstOrDefaultAsync(row => row.CampaignId == campaign.Id && row.Operation == operation, cancellationToken)
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
                    // set no longer holds. Nothing is committed and nothing is swapped, and the answer names
                    // every route that refused — exactly as a rejected reload does.
                    throw DomainErrors.PluginReloadRejected(candidate.Problems);
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
        route is null ? null : new JsonObject { ["plugin"] = route.PluginId, ["binding"] = route.Binding?.DeepClone() };

    private static OperationContract RequireContract(string? operation)
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
        if (refused is null)
        {
            return [];
        }

        var field = scope switch
        {
            RouteScope.CampaignOperation => RouteActivator.CampaignField(campaignId, operation),
            RouteScope.CampaignDefault => RouteActivator.CampaignField(campaignId, null),
            RouteScope.GlobalOperation => RouteActivator.GlobalOperationField(operation),
            _ => RouteActivator.GlobalDefaultField,
        };

        return [new ErrorDetail(field, refused.Code!, refused.Message!), .. refused.Details ?? []];
    }

    private static RoutesDto ToDto(RouteSnapshot snapshot, string? only)
    {
        var campaigns = snapshot.Campaigns
            .Where(entry => only is null || string.Equals(entry.Key, only, StringComparison.Ordinal))
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new CampaignRoutesDto(entry.Key, ToDto(entry.Value.Default), Overrides(entry.Value)))
            .ToList();

        return new RoutesDto(
            snapshot.Id,
            snapshot.PluginSnapshotId,
            new RouteSetDto(ToDto(snapshot.Global.Default), Overrides(snapshot.Global)),
            campaigns);
    }

    private static IReadOnlyDictionary<string, RouteDto> Overrides(RouteSet set) =>
        set.Operations
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .ToDictionary(entry => entry.Key, entry => ToDto(entry.Value)!, StringComparer.Ordinal);

    private static RouteDto? ToDto(Route? route) =>
        route is null ? null : new RouteDto(route.PluginId, route.Binding, route.BindingIdentity);
}
