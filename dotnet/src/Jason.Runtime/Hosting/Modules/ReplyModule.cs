using Jason.Contracts.Api;
using Jason.Runtime.Api;
using Jason.Runtime.Replies;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Hosting.Modules;

/// <summary>
/// A role's verdict on one reply: recorded as the reserved line <c>reply_classified</c>, read back through the
/// chronicle like any other line.
/// </summary>
public static class ReplyModule
{
    public static IServiceCollection AddReplyModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddScoped<ReplyService>();
    }

    public static void MapReplyOperations(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapOperation<ReplyService, ReplyRecordRequest, JournalEntryDto>(
            Operations.ReplyRecord, (service, request, cancellationToken) => service.RecordAsync(request, cancellationToken));
    }
}
