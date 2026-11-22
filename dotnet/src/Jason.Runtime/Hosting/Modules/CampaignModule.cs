using Jason.Contracts.Api;
using Jason.Runtime.Api;
using Jason.Runtime.Audiences;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Configuration;
using Jason.Runtime.Journal;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Jason.Runtime.Hosting.Modules;

/// <summary>Campaigns, their context and the chronicle that records what happened to them.</summary>
public static class CampaignModule
{
    public static IServiceCollection AddCampaignModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // A campaign going live reads the deep review's interval from the manager's section, so the seam is stated here
        // as well as beside the loop: whichever module comes first registers it, and a composition without the dispatcher
        // still reads the installation's setting rather than the shipped default.
        services.TryAddSingleton(DispatcherModule.Seam<ManagerOptions>(ManagerOptions.Section));

        // A review asked for counts the manager's launches of the day as the claim counts them: the same limits and the same
        // day, read through the same seams the dispatcher module names; either may be composed first.
        services.TryAddSingleton(DispatcherModule.Seam<LaunchesOptions>(LaunchesOptions.Section));
        services.TryAddSingleton(DispatcherModule.Seam<RuntimeOptions>(RuntimeOptions.Section));
        services.TryAddSingleton<InstallationDay>();
        return services.AddScoped<CampaignService>().AddScoped<CampaignReviews>().AddScoped<CampaignNumbers>().AddScoped<CampaignAudienceView>().AddScoped<JournalService>();
    }

    public static void MapCampaignOperations(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapOperation<CampaignService, CampaignCreateRequest, CampaignDto>(
            Operations.CampaignCreate, (service, request, cancellationToken) => service.CreateAsync(request, cancellationToken));
        app.MapOperation<CampaignService, CampaignGetRequest, CampaignDto>(
            Operations.CampaignGet, (service, request, cancellationToken) => service.GetAsync(request, cancellationToken));
        app.MapOperation<CampaignService, CampaignListRequest, Page<CampaignSummaryDto>>(
            Operations.CampaignList, (service, request, cancellationToken) => service.ListAsync(request, cancellationToken));
        app.MapOperation<CampaignService, CampaignUpdateRequest, CampaignDto>(
            Operations.CampaignUpdate, (service, request, cancellationToken) => service.UpdateAsync(request, cancellationToken));
        app.MapOperation<CampaignService, CampaignTransitionRequest, CampaignDto>(
            Operations.CampaignStart, (service, request, cancellationToken) => service.StartAsync(request, cancellationToken));
        app.MapOperation<CampaignService, CampaignTransitionRequest, CampaignDto>(
            Operations.CampaignPause, (service, request, cancellationToken) => service.PauseAsync(request, cancellationToken));
        app.MapOperation<CampaignService, CampaignTransitionRequest, CampaignDto>(
            Operations.CampaignArchive, (service, request, cancellationToken) => service.ArchiveAsync(request, cancellationToken));
        app.MapOperation<CampaignReviews, CampaignReviewRequest, WorkItemDto>(
            Operations.CampaignReview, (service, request, cancellationToken) => service.ReviewAsync(request, cancellationToken));
        app.MapOperation<CampaignService, CampaignUpdateContextRequest, CampaignDto>(
            Operations.CampaignUpdateContext, (service, request, cancellationToken) => service.UpdateContextAsync(request, cancellationToken));
        app.MapOperation<CampaignService, CampaignSetSendModeRequest, CampaignDto>(
            Operations.CampaignSetSendMode, (service, request, cancellationToken) => service.SetSendModeAsync(request, cancellationToken));

        app.MapOperation<CampaignNumbers, CampaignSummaryRequest, CampaignSummaryResponse>(
            Operations.CampaignSummary, (service, request, cancellationToken) => service.SummaryAsync(request, cancellationToken));
        app.MapOperation<CampaignNumbers, CampaignActivityRequest, CampaignActivityResponse>(
            Operations.CampaignActivity, (service, request, cancellationToken) => service.ActivityAsync(request, cancellationToken));
        app.MapOperation<CampaignNumbers, CampaignActivityByPeriodRequest, CampaignActivityByPeriodResponse>(
            Operations.CampaignActivityByPeriod, (service, request, cancellationToken) => service.ActivityByPeriodAsync(request, cancellationToken));
        app.MapOperation<CampaignAudienceView, CampaignAudienceRequest, CampaignAudienceResponse>(
            Operations.CampaignAudience, (service, request, cancellationToken) => service.ReadAsync(request, cancellationToken));

        app.MapOperation<JournalService, JournalAppendRequest, JournalEntryDto>(
            Operations.JournalAppend, (service, request, cancellationToken) => service.AppendAsync(request, cancellationToken));
        app.MapOperation<JournalService, JournalListRequest, Page<JournalEntryDto>>(
            Operations.JournalList, (service, request, cancellationToken) => service.ListAsync(request, cancellationToken));
        app.MapOperation<JournalService, JournalKindsRequest, Page<JournalKindDto>>(
            Operations.JournalKinds, (_, request, _) => Task.FromResult(JournalService.Kinds(request)));
    }
}
