using Jason.Contracts.Api;
using Jason.Runtime.Api;
using Jason.Runtime.Approvals;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Hosting.Modules;

/// <summary>
/// The decisions a person makes about work the runtime may not decide for itself: what is waiting, what it would
/// do, and the two answers; and the standing approvals a person sets in advance for a campaign's drafted replies of one
/// category. Nothing inside the runtime calls these.
/// </summary>
public static class ApprovalModule
{
    public static IServiceCollection AddApprovalModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddScoped<ApprovalService>().AddScoped<StandingApprovalService>();
    }

    public static void MapApprovalOperations(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapOperation<ApprovalService, ApprovalListRequest, Page<ApprovalSummaryDto>>(
            Operations.ApprovalList, (service, request, cancellationToken) => service.ListAsync(request, cancellationToken));
        app.MapOperation<ApprovalService, ApprovalGetRequest, ApprovalDto>(
            Operations.ApprovalGet, (service, request, cancellationToken) => service.GetAsync(request, cancellationToken));
        app.MapOperation<ApprovalService, ApprovalDecisionRequest, ApprovalDto>(
            Operations.ApprovalApprove, (service, request, cancellationToken) => service.ApproveAsync(request, cancellationToken));
        app.MapOperation<ApprovalService, ApprovalDecisionRequest, ApprovalDto>(
            Operations.ApprovalReject, (service, request, cancellationToken) => service.RejectAsync(request, cancellationToken));
        app.MapOperation<StandingApprovalService, ApprovalSetStandingRequest, StandingApprovalDto>(
            Operations.ApprovalSetStanding, (service, request, cancellationToken) => service.SetAsync(request, cancellationToken));
        app.MapOperation<StandingApprovalService, ApprovalUnsetStandingRequest, StandingApprovalDto>(
            Operations.ApprovalUnsetStanding, (service, request, cancellationToken) => service.UnsetAsync(request, cancellationToken));
        app.MapOperation<StandingApprovalService, ApprovalListStandingRequest, Page<StandingApprovalDto>>(
            Operations.ApprovalListStanding, (service, request, cancellationToken) => service.ListAsync(request, cancellationToken));
    }
}
