using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Runtime.Audiences;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Registry;

namespace Jason.Runtime.Routing;

/// <summary>
/// What one canonical operation takes for one campaign: its own argument schema, and the schema of the half of the input
/// that is the provider's, as the plugin the campaign's route names declares it — for an operation carrying an audience's
/// definition, that plugin's <c>audience_definition</c>. The route is resolved now, against the active snapshots, and held
/// to the half of the claim a route answers, so what is described is what a work item created now would be held to.
/// </summary>
public sealed class OperationDescribeService(JasonDbContext db, RouteRegistry routes, PluginRegistry plugins, PatternTiming? timing = null)
{
    private readonly PatternTiming _timing = timing ?? PatternTiming.Default;

    public async Task<OperationDescriptionDto> DescribeAsync(OperationDescribeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var contract = RouteService.RequireContract(request.Operation);
        var channel = RouteService.ChannelAsked(request.Channel, contract.Id);
        var campaign = await CampaignService.LoadAsync(db, request.CampaignId, cancellationToken).ConfigureAwait(false);
        var snapshot = plugins.Snapshot;
        // What is described is the plugin the work would reach: a campaign's read on request goes where its searches go.
        var routedAs = AudienceReadService.RoutedAs(contract.Id);
        if (RouteResolver.Resolve(routes.Snapshot, campaign.PublicId, routedAs, channel) is not { } resolution)
        {
            throw new NotFoundException(
                AttemptErrors.NoRoute,
                $"No route sends '{routedAs}'{(channel is null ? string.Empty : $" on '{channel}'")} anywhere for campaign '{campaign.PublicId}'. Write one with route.set, or a global one with route.set and scope global.");
        }

        var route = resolution.Route;
        if (ProviderOpPreflight.Routable(snapshot, contract, route, _timing) is { Code: { } code } refused)
        {
            throw code == ProviderOpPreflight.TransientOutcome
                ? DomainErrors.ValidationTimedOut(refused.Details ?? [])
                : new ConflictException(code, refused.Message!);
        }

        var manifest = snapshot.Find(route.PluginId)!.Manifest;
        return new OperationDescriptionDto(
            contract.Id,
            contract.Version,
            route.PluginId,
            manifest.Version,
            contract.ArgumentSchema.DeepClone().AsObject(),
            CanonicalInput.CarriesADefinition(contract) ? manifest.AudienceDefinition?.ToJson() : null,
            channel);
    }
}
