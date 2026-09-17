using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Api;
using Jason.Runtime.Reports;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Hosting.Modules;

/// <summary>
/// What somebody did outside Jason and told Jason about afterwards. Nothing inside the runtime calls these, and
/// there is deliberately no verb that changes or removes an admitted report.
/// </summary>
public static class ReportModule
{
    public static IServiceCollection AddReportModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddScoped<ReportService>();
    }

    public static void MapReportOperations(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Bound as the document it arrived as, unlike every other verb: what is stored has to be what was sent,
        // and a typed request would drop the keys it does not know instead of refusing them.
        app.MapOperation<ReportService, JsonObject, ReportDto>(
            Operations.ReportSubmit, (service, request, cancellationToken) => service.SubmitAsync(request, cancellationToken));

        // Reading one back is an ordinary typed verb: what has to survive intact is the assertion inside the
        // report, not the request that asks for it.
        app.MapOperation<ReportService, ReportGetRequest, ReportDto>(
            Operations.ReportGet, (service, request, cancellationToken) => service.GetAsync(request, cancellationToken));
        app.MapOperation<ReportService, ReportListRequest, Page<ReportSummaryDto>>(
            Operations.ReportList, (service, request, cancellationToken) => service.ListAsync(request, cancellationToken));
    }
}
