using Jason.Contracts.Api;
using Jason.Runtime.Api;
using Jason.Runtime.Contacts;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Hosting.Modules;

/// <summary>Contacts, their channels, campaign membership and the global suppression list.</summary>
public static class ContactModule
{
    public static IServiceCollection AddContactModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ContactService>();
        return services;
    }

    public static void MapContactOperations(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapOperation<ContactService, ContactCreateRequest, ContactDto>(Operations.ContactCreate, (service, request, ct) => service.CreateAsync(request, ct));
        app.MapOperation<ContactService, ContactGetRequest, ContactDto>(Operations.ContactGet, (service, request, ct) => service.GetAsync(request, ct));
        app.MapOperation<ContactService, ContactListRequest, Page<ContactDto>>(Operations.ContactList, (service, request, ct) => service.ListAsync(request, ct));
        app.MapOperation<ContactService, ContactUpdateRequest, ContactDto>(Operations.ContactUpdate, (service, request, ct) => service.UpdateAsync(request, ct));
        app.MapOperation<ContactService, ContactArchiveRequest, ContactDto>(Operations.ContactArchive, (service, request, ct) => service.ArchiveAsync(request, ct));
    }
}
