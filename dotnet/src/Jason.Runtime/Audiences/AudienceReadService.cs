using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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
using Microsoft.AspNetCore.Http;

namespace Jason.Runtime.Audiences;

/// <summary>
/// A campaign's audience reads, made when a person or an agent asks and answered in the request: how big an audience is
/// and who is in it, the values a provider's filter takes, and whether the account may search now. Each is routed by the
/// campaign's route for its operation, resolved as the claim resolves it and held to the route's half of the claim's
/// checks; the input is composed as a work item's would be, with no person; and the answer is held to the operation's
/// document. It is information, not work: no work item, no attempt, no approval, no retry, nothing journaled and nothing
/// pinned from what comes back.
/// </summary>
public sealed partial class AudienceReadService(
    JasonDbContext db,
    PluginRegistry plugins,
    RouteRegistry routes,
    ExternalIdStore identifiers,
    IProviderReadInvoker invoker,
    PatternTiming? timing = null)
{
    private const string PreviewOperation = "audience.preview";
    private const string FilterValuesOperation = "audience.filter_values";
    private const string AvailabilityOperation = "audience.availability";

    /// <summary>The prefix of a campaign read's invocation id, beside the contact reads' own.</summary>
    private const string InvocationPrefix = "cpr";

    private const int MaxPeoplePerCompany = 100;
    private const int MaxQueryLength = 200;
    private const int MaxValues = 100;
    private const int DefaultValues = 20;

    private readonly PatternTiming _timing = timing ?? PatternTiming.Default;

    public async Task<AudiencePreviewResponse> PreviewAsync(AudiencePreviewRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new ValidationErrors();
        if (request.Audience is not null && request.Definition is not null)
        {
            errors.Add("definition", "conflict", "Name the audience by its number or by its definition, not both.");
        }
        else if (request.Audience is null && request.Definition is null)
        {
            errors.Add("definition", "required", "Name the audience by its definition, or an audience the campaign holds by its number.");
        }

        if (request.Audience is < 1)
        {
            errors.Add("audience", "invalid", "audience is an audience's number, 1 or more.");
        }

        if (request.PeoplePerCompany is < 1 or > MaxPeoplePerCompany)
        {
            errors.Add("people_per_company", "invalid", string.Create(CultureInfo.InvariantCulture, $"people_per_company must be between 1 and {MaxPeoplePerCompany}."));
        }

        errors.ThrowIfAny();
        var target = await TargetAsync(request.CampaignId, PreviewOperation, cancellationToken).ConfigureAwait(false);

        // As the claim answers a top-up by a number this build holds no audience under.
        if (request.Audience is { } number)
        {
            throw new NotFoundException(
                AttemptErrors.AudienceUnknown,
                string.Create(CultureInfo.InvariantCulture, $"Campaign '{target.Campaign.PublicId}' holds no audience under {number}: this build numbers no audience yet, so name the audience by its definition."));
        }

        var args = new JsonObject { ["definition"] = request.Definition!.DeepClone() };
        if (request.PeoplePerCompany is { } perCompany)
        {
            args["people_per_company"] = perCompany;
        }

        var input = CanonicalInput.ComposeCampaignRead(target.Contract, target.Campaign, target.Pins, args, request.Definition.DeepClone().AsObject());

        // The definition is the provider's, so the routed plugin's declared schema refuses it before anything runs, under
        // the claim's own code and with the claim's pointers.
        if (ProviderOpPreflight.Undefined(target.Plugin.Plugin, input, target.Route, PreviewOperation, _timing) is { Code: { } code } refused)
        {
            throw code == ProviderOpPreflight.TransientOutcome
                ? DomainErrors.ValidationTimedOut(refused.Details ?? [])
                : new DomainException(StatusCodes.Status400BadRequest, code, refused.Message!, details: refused.Details);
        }

        var result = await ReadAsync(target, input, cancellationToken).ConfigureAwait(false);
        return new AudiencePreviewResponse(
            target.PluginId,
            (long?)result["count"],
            (long?)result["companies"],
            result["sample"]!.DeepClone().AsArray());
    }

    public async Task<AudienceFilterValuesResponse> FilterValuesAsync(AudienceFilterValuesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new ValidationErrors();
        var filter = request.Filter?.Trim();
        if (string.IsNullOrEmpty(filter))
        {
            errors.Add("filter", "required", "filter names one of the provider's filters, such as 'industries'.");
        }
        else if (!FilterName().IsMatch(filter))
        {
            errors.Add("filter", "invalid", "filter is a lowercase name of one of the provider's filters, such as 'industries'.");
        }

        if (request.Query is { Length: > MaxQueryLength })
        {
            errors.Add("query", "too_long", string.Create(CultureInfo.InvariantCulture, $"query must be at most {MaxQueryLength} characters."));
        }

        if (request.Limit is < 1 or > MaxValues)
        {
            errors.Add("limit", "invalid", string.Create(CultureInfo.InvariantCulture, $"limit must be between 1 and {MaxValues}."));
        }

        errors.ThrowIfAny();
        var target = await TargetAsync(request.CampaignId, FilterValuesOperation, cancellationToken).ConfigureAwait(false);
        var args = new JsonObject
        {
            ["filter"] = filter,
            ["query"] = request.Query ?? string.Empty,
            ["limit"] = request.Limit ?? DefaultValues,
        };

        var result = await ReadAsync(target, CanonicalInput.ComposeCampaignRead(target.Contract, target.Campaign, target.Pins, args, definition: null), cancellationToken).ConfigureAwait(false);
        return new AudienceFilterValuesResponse(target.PluginId, result["values"]!.DeepClone().AsArray());
    }

    public async Task<AudienceAvailabilityResponse> AvailabilityAsync(AudienceAvailabilityRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var target = await TargetAsync(request.CampaignId, AvailabilityOperation, cancellationToken).ConfigureAwait(false);
        var result = await ReadAsync(target, CanonicalInput.ComposeCampaignRead(target.Contract, target.Campaign, target.Pins, new JsonObject(), definition: null), cancellationToken).ConfigureAwait(false);
        return new AudienceAvailabilityResponse(
            target.PluginId,
            result["available"]!.GetValue<bool>(),
            (string?)result["reason"],
            (string?)result["detail"],
            result["credits"]?.DeepClone().AsObject(),
            (long?)result["headroom"]);
    }

    /// <summary>
    /// The plugin the campaign's route for <paramref name="operation"/> names, resolved against the snapshots active now
    /// and held to the half of the claim a route answers: nothing resolving is <c>no_route</c>; a route that cannot run is
    /// refused with the claim's own code; a binding no pattern could finish measuring is a read to ask again.
    /// </summary>
    private async Task<Target> TargetAsync(string? campaignId, string operation, CancellationToken ct)
    {
        var campaign = await CampaignService.LoadAsync(db, campaignId, ct).ConfigureAwait(false);
        var contract = OperationCatalog.Find(operation)!;
        var pluginSnapshot = plugins.Snapshot;
        if (RouteResolver.Resolve(routes.Snapshot, campaign.PublicId, operation) is not { } resolution)
        {
            throw new NotFoundException(
                AttemptErrors.NoRoute,
                $"No route sends '{operation}' anywhere for campaign '{campaign.PublicId}'. Write one with route.set, or a global one in the settings file.");
        }

        var route = resolution.Route;
        if (ProviderOpPreflight.Routable(pluginSnapshot, contract, route, _timing) is { Code: { } code } refused)
        {
            throw code == ProviderOpPreflight.TransientOutcome
                ? DomainErrors.ValidationTimedOut(refused.Details ?? [])
                : new ConflictException(code, refused.Message!);
        }

        var plugin = pluginSnapshot.Find(route.PluginId)!;
        return new Target(
            campaign,
            contract,
            route,
            new PinnedPlugin(plugin, pluginSnapshot.Id),
            identifiers.PinsFor(campaign, route.PluginId));
    }

    private Task<JsonObject> ReadAsync(Target target, JsonObject input, CancellationToken ct) =>
        ProviderReadCall.InvokeAsync(
            invoker,
            target.PluginId,
            target.Contract,
            input,
            target.Route.Binding,
            target.Plugin,
            InvocationPrefix,
            ct);

    [GeneratedRegex(@"^[a-z][a-z0-9_-]{0,63}\z")]
    private static partial Regex FilterName();

    private sealed record Target(
        Campaign Campaign,
        OperationContract Contract,
        Route Route,
        PinnedPlugin Plugin,
        IReadOnlyDictionary<string, string> Pins)
    {
        public string PluginId => Route.PluginId;
    }
}
