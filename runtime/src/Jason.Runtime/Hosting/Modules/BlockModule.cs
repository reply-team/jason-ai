using Jason.Contracts.Api;
using Jason.Runtime.Api;
using Jason.Runtime.Blocks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Hosting.Modules;

/// <summary>
/// The provider blocks as a person sees them: what is blocked, one block with where it is cleared, and a clear on their
/// word. Nothing inside the runtime calls these: the dispatcher opens and clears blocks as the provider answers.
/// </summary>
public static class BlockModule
{
    public static IServiceCollection AddBlockModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddScoped<BlockService>();
    }

    public static void MapBlockOperations(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapOperation<BlockService, BlockListRequest, Page<ProviderBlockDto>>(
            Operations.BlockList, (service, request, cancellationToken) => service.ListAsync(request, cancellationToken));
        app.MapOperation<BlockService, BlockGetRequest, ProviderBlockDto>(
            Operations.BlockGet, (service, request, cancellationToken) => service.GetAsync(request, cancellationToken));
        app.MapOperation<BlockService, BlockClearRequest, ProviderBlockDto>(
            Operations.BlockClear, (service, request, cancellationToken) => service.ClearAsync(request, cancellationToken));
    }
}
