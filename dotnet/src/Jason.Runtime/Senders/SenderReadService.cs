using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Contacts;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;

namespace Jason.Runtime.Senders;

/// <summary>
/// The provider's accounts a send can go from, read for a campaign when a person or an agent asks and answered in the
/// request. The read is routed by the campaign's route for <c>sender.list</c> itself, on the channel it names, as the
/// claim would route the same operation written as work; it is held to the route's half of the claim's checks; the input
/// is composed as a work item's would be, with the campaign and the routed plugin's own pins of it; and the answer is held
/// to the operation's document. It is information, not work: no work item, no attempt, no approval, no retry, nothing
/// journaled and nothing pinned from what comes back. The same operation stays work as well, so it is not refused there.
/// </summary>
public sealed class SenderReadService(
    JasonDbContext db,
    PluginRegistry plugins,
    RouteRegistry routes,
    ExternalIdStore identifiers,
    IProviderReadInvoker invoker,
    PatternTiming? timing = null)
{
    private const string Operation = "sender.list";

    /// <summary>The prefix of a sender read's invocation id, beside the other reads' own.</summary>
    private const string InvocationPrefix = "sdr";

    private const int MaxLimit = 100;
    private const int DefaultLimit = 20;
    private const int MaxOffset = 1_000_000;

    private static readonly string[] Owners = ["own", "team"];
    private static readonly string[] Channels = ["email", "linkedin"];

    private readonly PatternTiming _timing = timing ?? PatternTiming.Default;

    public async Task<SenderListResponse> ListAsync(SenderListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new ValidationErrors();
        var owners = request.Owners?.Trim();
        if (string.IsNullOrEmpty(owners))
        {
            errors.Add("owners", "required", "owners says whose accounts to list, own or team; a colleague's account is never a default.");
        }
        else if (!Owners.Contains(owners, StringComparer.Ordinal))
        {
            errors.Add("owners", "invalid", "owners is own or team.");
        }

        var channel = request.SenderChannel?.Trim();
        if (channel is not null && !Channels.Contains(channel, StringComparer.Ordinal))
        {
            errors.Add("sender_channel", "invalid", "sender_channel is email or linkedin, or absent for every channel.");
        }

        if (request.Limit is < 1 or > MaxLimit)
        {
            errors.Add("limit", "invalid", string.Create(CultureInfo.InvariantCulture, $"limit must be between 1 and {MaxLimit}."));
        }

        if (request.Offset is < 0 or > MaxOffset)
        {
            errors.Add("offset", "invalid", string.Create(CultureInfo.InvariantCulture, $"offset must be between 0 and {MaxOffset}."));
        }

        errors.ThrowIfAny();

        var campaign = await CampaignService.LoadAsync(db, request.CampaignId, cancellationToken).ConfigureAwait(false);
        var contract = OperationCatalog.Find(Operation)!;
        var pluginSnapshot = plugins.Snapshot;
        if (RouteResolver.Resolve(routes.Snapshot, campaign.PublicId, Operation, channel) is not { } resolution)
        {
            var on = channel is null ? string.Empty : $" on '{channel}'";
            throw new NotFoundException(
                AttemptErrors.NoRoute,
                $"No route sends '{Operation}'{on} anywhere for campaign '{campaign.PublicId}'. Write one with route.set, or a global one in the settings file.");
        }

        var route = resolution.Route;
        if (ProviderOpPreflight.Routable(pluginSnapshot, contract, route, _timing) is { Code: { } code } refused)
        {
            throw code == ProviderOpPreflight.TransientOutcome
                ? DomainErrors.ValidationTimedOut(refused.Details ?? [])
                : new ConflictException(code, refused.Message!);
        }

        var args = new JsonObject { ["owners"] = owners };
        if (channel is not null)
        {
            args["sender_channel"] = channel;
        }

        args["limit"] = request.Limit ?? DefaultLimit;
        args["offset"] = request.Offset ?? 0;

        var plugin = new PinnedPlugin(pluginSnapshot.Find(route.PluginId)!, pluginSnapshot.Id);
        var input = CanonicalInput.ComposeCampaignRead(contract, campaign, identifiers.PinsFor(campaign, route.PluginId), args, definition: null);
        var result = await ProviderReadCall.InvokeAsync(
            invoker,
            route.PluginId,
            contract,
            input,
            route.Binding,
            plugin,
            InvocationPrefix,
            cancellationToken).ConfigureAwait(false);

        return new SenderListResponse(
            route.PluginId,
            result["items"]!.DeepClone().AsArray(),
            result["has_more"]!.GetValue<bool>(),
            result["channels"]!.DeepClone().AsObject(),
            result["vendor"]!.DeepClone().AsObject());
    }
}
