using System.Net;
using Jason.Runtime.Configuration;
using Jason.Runtime.Execution.Hosts;
using Jason.Runtime.Notifications;
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
    private static int refusals;

    /// <summary>What the transport says when it is reached, so a test can name it rather than guess at it.</summary>
    public const string Refusal = "No test in this repository reaches the network";

    /// <summary>
    /// How many times the transport has refused a request. A test runtime that reaches for the feed leaves a
    /// mark here — which is what lets one test assert that the transport is doing its job, rather than that it
    /// is merely declared.
    /// </summary>
    public static int Refusals => Volatile.Read(ref refusals);

    /// <summary>What every test's runtime is started with. Add to it with <c>with</c>; do not build another.</summary>
    public static RuntimeHostOptions Quiet { get; } = new(
        ShippedSettingsDirectory: null,
        ConsoleLogging: false,
        ConfigureServices: TurnTheOutsideOff)
    {
        FeedHandler = new NoNetworkInTests(),
        // Every test runtime on a random port: a suite of hundreds would otherwise climb from 52766 and collide
        // with a developer's own running Jason.
        Port = 0,
        // Every test runtime in a home of its own beside its data directory, so the harness root its first create makes
        // is a folder the test removes, never ~/Jason. A property rather than a line in the hook, for the reason the
        // feed's handler is one: a test that replaces the hook replaces it whole.
        Home = paths => TempDataDir.HomeOf(paths.Root),
    };

    /// <summary>
    /// Both things a runtime does on its own toward somebody outside the test: it asks the release feed, and it
    /// notifies a person. A test runtime does neither unless the test turns one back on. Nor does it read a folder the
    /// machine keeps for itself: the host that checks one is registered again with none to check.
    /// </summary>
    public static void TurnTheOutsideOff(IServiceCollection services)
    {
        TurnTheUpdateCheckOff(services);
        TurnNotificationsOff(services);
        KeepTheMachinesPolicyFolderOut(services);
    }

    /// <summary>
    /// Gemini CLI is refused a launch where the machine's own system policy folder holds a policy file, so a row that runs
    /// a role under it would depend on the machine it runs on. Registered after the runtime's own, and the last
    /// registration of a host is the one a claim resolves, so the runtime of a test checks no folder.
    /// </summary>
    public static void KeepTheMachinesPolicyFolderOut(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IAgentHost>(new GeminiCliHost(null));
    }

    /// <summary>
    /// After binding, so that it beats whatever a test's own settings file says. A test that means to exercise
    /// the notifier turns it on explicitly, with channels of its own: no test shows a toast or sends anything.
    /// </summary>
    public static void TurnNotificationsOff(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.PostConfigure<NotificationsOptions>(notifications => notifications.Enabled = false);
    }

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
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref refusals);
            throw new HttpRequestException(
                $"A test runtime tried to read {request?.RequestUri}. {Refusal}: "
                + "the update check is off in TestRuntimeOptions.Quiet, and a test that means to exercise it passes its own FeedHandler.",
                null,
                HttpStatusCode.ServiceUnavailable);
        }
    }
}
