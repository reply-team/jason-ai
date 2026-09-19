using System.Net;
using Jason.Runtime.Configuration;
using Jason.Runtime.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests;

/// <summary>
/// The options every test that composes a runtime starts it with. One value rather than one per test class,
/// because what it carries is a guarantee about the whole suite and a guarantee kept in ten places is a
/// guarantee until somebody adds an eleventh.
/// </summary>
/// <remarks>
/// <para>
/// The guarantee is that no test ever opens a socket to the release feed. It is kept twice over, on purpose.
/// <see cref="RuntimeHostOptions.ConfigureServices"/> turns the check off after the settings have been bound,
/// whatever a test wrote in its own settings file — but a test may replace that hook to register a service of
/// its own, and `with` replaces a property whole. So the handler is a **separate** property: a test that
/// replaces the hook still cannot reach the network, because the transport itself refuses.
/// </para>
/// <para>
/// Refusing rather than answering: a test runtime that reaches its first check has found something worth
/// knowing — five minutes is longer than any test's runtime lives — so the failure is loud in the log rather
/// than quietly satisfied by a stub that hands back a manifest nobody asked for.
/// </para>
/// </remarks>
public static class TestRuntimeOptions
{
    /// <summary>What every test's runtime is started with. Add to it with <c>with</c>; do not build another.</summary>
    public static RuntimeHostOptions Quiet { get; } = new(
        ShippedSettingsDirectory: null,
        ConsoleLogging: false,
        ConfigureServices: TurnTheUpdateCheckOff)
    {
        FeedHandler = new NoNetworkInTests(),
    };

    /// <summary>
    /// After binding, so that it beats whatever a test's own settings file says. A test that means to exercise
    /// the checker asks for it explicitly, with a handler of its own.
    /// </summary>
    public static void TurnTheUpdateCheckOff(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.PostConfigure<UpdateOptions>(update => update.CheckEnabled = false);
    }

    /// <summary>
    /// The transport a test runtime is given for the release feed. It answers nothing and refuses everything:
    /// reaching it means a runtime under test was about to ask a web page a question, which is the one thing
    /// this suite promises never to do.
    /// </summary>
    private sealed class NoNetworkInTests : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException(
                $"A test runtime tried to read {request?.RequestUri}. No test in this repository reaches the network: "
                + "the update check is off in TestRuntimeOptions.Quiet, and a test that means to exercise it passes its own FeedHandler.",
                null,
                HttpStatusCode.ServiceUnavailable);
    }
}
