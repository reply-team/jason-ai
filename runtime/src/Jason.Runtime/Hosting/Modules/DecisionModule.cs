using Jason.Contracts.Api;
using Jason.Runtime.Api;
using Jason.Runtime.Decisions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Hosting.Modules;

/// <summary>
/// The questions a running role could not answer for itself: raising one, reading what is waiting, and the
/// answer, which is a person's. Nothing inside the runtime calls the last of these.
/// </summary>
public static class DecisionModule
{
    public static IServiceCollection AddDecisionModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddScoped<DecisionService>();
    }

    public static void MapDecisionOperations(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapOperation<DecisionService, DecisionRaiseRequest, DecisionDto>(
            Operations.DecisionRaise, (service, request, cancellationToken) => service.RaiseAsync(request, cancellationToken));
        app.MapOperation<DecisionService, DecisionAnswerRequest, DecisionDto>(
            Operations.DecisionAnswer, (service, request, cancellationToken) => service.AnswerAsync(request, cancellationToken));
        app.MapOperation<DecisionService, DecisionGetRequest, DecisionDto>(
            Operations.DecisionGet, (service, request, cancellationToken) => service.GetAsync(request, cancellationToken));
        app.MapOperation<DecisionService, DecisionListRequest, Page<DecisionSummaryDto>>(
            Operations.DecisionList, (service, request, cancellationToken) => service.ListAsync(request, cancellationToken));
    }
}
