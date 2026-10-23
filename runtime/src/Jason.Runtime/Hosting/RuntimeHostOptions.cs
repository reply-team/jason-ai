using Jason.Contracts.Discovery;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Hosting;

/// <param name="ShippedSettingsDirectory">Directory holding the shipped <c>appsettings.json</c> (the install directory); null = none.</param>
/// <param name="ConsoleLogging">Mirror logs to the console (foreground runs).</param>
/// <param name="Clock">The runtime's clock; null means the system clock. A test moves time with its own.</param>
/// <param name="FeedHandler">
/// Where the update feed's requests go; null means the network. A test installs its own here, exactly where a
/// real runtime would send, so what it proves about the check is proved against the composed runtime.
/// </param>
/// <param name="ConfigureServices">Runs last while composing, so a test can register a service after the real one.</param>
/// <param name="Refusals">
/// Where a runtime that will not start says why; null means standard error. A detached runtime has pointed its
/// standard error at the null device before it gets that far, so it hands in the one it was started with — the
/// only way <c>jason runtime start</c> hears the reason. <see cref="RuntimeHost.RunAsync"/> closes it once the
/// runtime has started or refused, so nothing of the process that started it is held for longer than that.
/// </param>
/// <param name="ConsoleAtStart">
/// What <see cref="ProcessDetacher.Detach"/> found attached to this process; null for a runtime nobody detached,
/// and off Windows. The runtime writes it into its log at start, so which Windows honoured the executable's
/// <c>consoleAllocationPolicy</c> can be read afterwards rather than watched for.
/// </param>
public sealed record RuntimeHostOptions(
    string? ShippedSettingsDirectory = null,
    bool ConsoleLogging = true,
    TimeProvider? Clock = null,
    HttpMessageHandler? FeedHandler = null,
    Action<IServiceCollection>? ConfigureServices = null,
    TextWriter? Refusals = null,
    ConsoleAtStart? ConsoleAtStart = null)
{
    /// <summary>
    /// How long publishing the endpoint descriptor waits for a reader to let go; null means
    /// <see cref="Discovery.DescriptorPublisher.PublishRule"/>. A test holds it short, so a start refused by a held
    /// descriptor is refused in a moment rather than in ten seconds.
    /// </summary>
    internal Jason.Contracts.Files.HeldFileRule? DescriptorPublishing { get; init; }

    /// <summary>The port to listen on, overriding the settings when set. The test suite sets 0 so every test runtime is on a random port.</summary>
    public int? Port { get; init; }

    /// <summary>
    /// The home directory for the data directory this runtime starts on: where the harness root's default is made and the
    /// one folder the root may never be. Null means the account's own. The test suite gives every test a home of its own
    /// beside its data directory, so no test makes a folder in a real one; kept apart from <see cref="ConfigureServices"/>
    /// for the reason <see cref="FeedHandler"/> is, since a test that replaces that hook replaces it whole.
    /// </summary>
    public Func<JasonPaths, string>? Home { get; init; }

    /// <summary>
    /// Handed the configuration as soon as it is built, before anything can refuse the start. A refused start returns no
    /// runtime to ask, so this is how a test sees that it gave the configuration, and the settings file's watch, back.
    /// </summary>
    internal Action<Microsoft.Extensions.Configuration.IConfigurationRoot>? ConfigurationBuilt { get; init; }
}
