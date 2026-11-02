using System.Text.Json.Nodes;
using Jason.Contracts.Operations;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Hosting;
using Jason.Runtime.Plugins.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Jason.Runtime.Tests.Hosting;

/// <summary>
/// The build's own patterns are compiled and matched once when the runtime starts, outside any request, so the cold
/// engine's cost is never measured by a request's timeout. It is the runtime host's act, and it comes first.
/// </summary>
public sealed class PatternWarmUpTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Hosted services start one after another in the order they were registered, so the order is the guarantee: the
    /// warm-up has finished before a manifest is read and before the dispatcher scans.
    /// </summary>
    [Fact]
    public async Task The_runtime_host_warms_the_published_patterns_before_the_plugin_load_and_the_dispatcher()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var started = api.Resolve<IEnumerable<IHostedService>>().Select(service => service.GetType()).ToList();
        var warmUp = started.IndexOf(typeof(PatternWarmUp));

        Assert.True(warmUp >= 0, "the runtime host registers no warm-up of the published patterns");
        Assert.True(warmUp < started.IndexOf(typeof(PluginStartupLoader)), "the plugin load starts before the warm-up");
        Assert.True(warmUp < started.IndexOf(typeof(DispatcherService)), "the dispatcher starts before the warm-up");
    }

    /// <summary>
    /// Through the host's own timing — the one every validating site takes — so what is seen is the runtime starting,
    /// not a warm-up called by hand.
    /// </summary>
    [Fact]
    public async Task Starting_the_runtime_matches_every_pattern_this_build_publishes()
    {
        var timing = TimingOut.Never();
        await using var api = await RuntimeApiFixture.StartAsync(Ct, configureServices: services => services.AddSingleton<PatternTiming>(timing));

        // Found as the documents write them, and compared as the engine runs them, which is how a timing knows a pattern.
        var published = PatternWarmUp.PublishedSchemas().SelectMany(Patterns).Select(TimingOut.AsCompiled).ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(published);
        Assert.Subset(timing.Matched.ToHashSet(StringComparer.Ordinal), published);
    }

    [Fact]
    public void The_published_schemas_are_every_operation_s_the_delivery_contract_s_the_events_contract_s_every_preference_s_and_every_reserved_context_key_s()
    {
        var schemas = PatternWarmUp.PublishedSchemas().ToList();

        Assert.Equal(
            ((OperationCatalog.All.Count + Jason.Contracts.Notifications.DeliveryCatalog.All.Count + Jason.Contracts.Events.EventsCatalog.All.Count) * 2)
            + Jason.Contracts.Preferences.PreferenceCatalog.All.Count
            + Jason.Contracts.Context.ContextKeyCatalog.All.Count,
            schemas.Count);
        Assert.Contains(OperationCatalog.Find("list_membership.add")!.InputSchema, schemas);
        Assert.Contains(Jason.Contracts.Events.EventsCatalog.Find("events.read")!.OutputSchema, schemas);
        Assert.Contains(Jason.Contracts.Preferences.PreferenceCatalog.Find("sender.identity")!.ValueSchema, schemas);
        Assert.Contains(Jason.Contracts.Context.ContextKeyCatalog.Find("goals")!.ValueSchema, schemas);
    }

    /// <summary>Every <c>pattern</c> written anywhere in a schema, read as plain JSON rather than through the validator.</summary>
    private static IEnumerable<string> Patterns(JsonNode? node) => node switch
    {
        JsonObject map => map.SelectMany(pair =>
            pair.Key == "pattern" && pair.Value is JsonValue value && value.TryGetValue(out string? pattern)
                ? [pattern]
                : Patterns(pair.Value)),
        JsonArray list => list.SelectMany(Patterns),
        _ => [],
    };
}
