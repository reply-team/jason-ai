using Jason.Contracts.Update;
using Jason.Runtime.Configuration;
using Jason.Runtime.Update;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Hosting.Modules;

/// <summary>
/// The unattended update check. It has no operations of its own: what it learns is read through
/// <c>system.info</c>, and what governs it is the <c>Update</c> section of the settings.
/// </summary>
public static class UpdateModule
{
    private const string FeedClient = "update-feed";

    /// <summary>
    /// A process that lives for weeks must not keep the address it resolved on its first day: connections are
    /// retired after this long, so the next check resolves the feed's name again.
    /// </summary>
    private static readonly TimeSpan ConnectionLifetime = TimeSpan.FromMinutes(5);

    /// <summary>A manifest is a few hundred bytes; a feed that has not answered in this long is not going to.</summary>
    private static readonly TimeSpan FeedTimeout = TimeSpan.FromSeconds(30);

    /// <param name="feedHandler">
    /// Where the feed's requests go when a test says; null is the network. The seam is the transport rather
    /// than the address, so the URL a test's runtime reads is exactly the one a real runtime would.
    /// </param>
    public static IServiceCollection AddUpdateModule(this IServiceCollection services, HttpMessageHandler? feedHandler)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<UpdateAdvertisement>();

        // The same guarded seam the dispatcher reads its section through: the last value that validated stays in
        // force, and the complaint about an edit that did not is said once.
        services.AddSingleton(DispatcherModule.Seam<UpdateOptions>(UpdateOptions.Section));

        // Keyed, so nothing else that may one day register an HttpClient collides with this one; made by the
        // container, so the container disposes it with the host.
        services.AddKeyedSingleton(FeedClient, (_, _) => Client(feedHandler));
        services.AddSingleton(provider => new UpdateFeed(provider.GetRequiredKeyedService<HttpClient>(FeedClient)));

        return services.AddHostedService<UpdateChecker>();
    }

    private static HttpClient Client(HttpMessageHandler? handler) =>
        new(handler ?? new SocketsHttpHandler { PooledConnectionLifetime = ConnectionLifetime }, disposeHandler: handler is null)
        {
            Timeout = FeedTimeout,
        };
}
