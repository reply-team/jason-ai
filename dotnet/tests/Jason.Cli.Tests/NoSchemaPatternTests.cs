using Jason.Cli;
using Jason.Contracts.Notifications;
using Jason.Contracts.Operations;

namespace Jason.Cli.Tests;

/// <summary>
/// The command-line client never matches a value against a schema, so it compiles no pattern. A warm-up of the published
/// patterns belongs to the process that matches values — the runtime — and this holds that nothing the client can
/// load compiles one on its own: not the client's own paths, and not the contracts assembly's catalogs, which a warm-up
/// placed in a static initialiser would make pay on every cold start the release smoke test times.
/// </summary>
public sealed class NoSchemaPatternTests
{
    /// <remarks>
    /// The count is the whole process's rather than this call's, and that is the stronger claim on purpose: nothing in
    /// this test project validates a schema, so any compiled pattern here came from the client, or from the contracts
    /// assembly as it loaded — which a count taken before and after the call would not see. Every catalog is touched
    /// first, so a warm-up hidden in either one's initialiser is caught whatever order the tests run in.
    /// </remarks>
    [Fact]
    public async Task Help_an_ordinary_verb_and_the_catalogs_compile_no_schema_pattern()
    {
        using var dir = new TempPaths();

        Assert.NotEmpty(OperationCatalog.All);
        Assert.NotEmpty(DeliveryCatalog.All);
        Assert.NotEmpty(Jason.Contracts.Events.EventsCatalog.All);
        Assert.NotEmpty(Jason.Contracts.Subscriptions.SubscriptionCatalog.All);
        Assert.NotEmpty(Jason.Contracts.Preferences.PreferenceCatalog.All);
        Assert.NotEmpty(Jason.Contracts.Context.ContextKeyCatalog.All);
        var help = await CliApp.RunAsync(["--help"], new CliEnvironment(new StringWriter(), new StringWriter(), dir.Paths), TestContext.Current.CancellationToken);
        var list = await CliApp.RunAsync(["campaign", "list"], new CliEnvironment(new StringWriter(), new StringWriter(), dir.Paths), TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.Success, help);
        Assert.Equal(ExitCodes.RuntimeUnavailable, list);
        Assert.Equal(0, PatternTiming.PatternsCompiled);
    }
}
