using Jason.Contracts.Api;
using Jason.Runtime.Api;
using Jason.Runtime.Profiles;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Hosting.Modules;

/// <summary>
/// The registry of execution profiles: which agent hosts this installation can launch, and how. Six verbs, and
/// no delete — an attempt names the revision it ran under for ever, so a profile is taken out of service rather
/// than taken away.
/// </summary>
public static class ProfileModule
{
    public static IServiceCollection AddProfileModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddScoped<ProfileService>();
    }

    public static void MapProfileOperations(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapOperation<ProfileService, ProfileCreateRequest, ExecutionProfileDto>(
            Operations.ProfileCreate, (service, request, cancellationToken) => service.CreateAsync(request, cancellationToken));
        app.MapOperation<ProfileService, ProfileUpdateRequest, ExecutionProfileDto>(
            Operations.ProfileUpdate, (service, request, cancellationToken) => service.UpdateAsync(request, cancellationToken));
        app.MapOperation<ProfileService, ProfileGetRequest, ExecutionProfileDto>(
            Operations.ProfileGet, (service, request, cancellationToken) => service.GetAsync(request, cancellationToken));
        app.MapOperation<ProfileService, ProfileListRequest, Page<ExecutionProfileDto>>(
            Operations.ProfileList, (service, request, cancellationToken) => service.ListAsync(request, cancellationToken));
        app.MapOperation<ProfileService, ProfileToggleRequest, ExecutionProfileDto>(
            Operations.ProfileDisable, (service, request, cancellationToken) => service.DisableAsync(request, cancellationToken));
        app.MapOperation<ProfileService, ProfileToggleRequest, ExecutionProfileDto>(
            Operations.ProfileEnable, (service, request, cancellationToken) => service.EnableAsync(request, cancellationToken));
    }
}
