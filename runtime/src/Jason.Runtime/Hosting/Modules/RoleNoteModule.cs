using Jason.Contracts.Api;
using Jason.Runtime.Api;
using Jason.Runtime.Notes;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Hosting.Modules;

/// <summary>
/// What a role remembers about a campaign. Three verbs, no patch and no delete of anything but the content
/// itself — a note is replaced whole, and clearing one is writing an empty document.
/// </summary>
public static class RoleNoteModule
{
    public static IServiceCollection AddRoleNoteModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddScoped<RoleNoteService>();
    }

    public static void MapRoleNoteOperations(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapOperation<RoleNoteService, RoleNoteGetRequest, RoleNoteDto>(
            Operations.RoleNoteGet, (service, request, cancellationToken) => service.GetAsync(request, cancellationToken));
        app.MapOperation<RoleNoteService, RoleNoteSetRequest, RoleNoteDto>(
            Operations.RoleNoteSet, (service, request, cancellationToken) => service.SetAsync(request, cancellationToken));
        app.MapOperation<RoleNoteService, RoleNoteListRequest, Page<RoleNoteSummaryDto>>(
            Operations.RoleNoteList, (service, request, cancellationToken) => service.ListAsync(request, cancellationToken));
    }
}
