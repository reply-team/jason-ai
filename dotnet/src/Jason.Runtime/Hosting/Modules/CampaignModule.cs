using Jason.Contracts.Api;
using Jason.Runtime.Api;
using Jason.Runtime.Audiences;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Journal;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Hosting.Modules;

/// <summary>Campaigns, their context and the chronicle that records what happened to them.</summary>
public static class CampaignModule
{
    public static IServiceCollection AddCampaignModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddScoped<CampaignService>().AddScoped<CampaignNumbers>().AddScoped<CampaignAudienceView>().AddScoped<JournalService>();
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
