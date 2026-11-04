using Jason.Contracts.Discovery;
using Jason.Contracts.Plugins;
using Jason.Runtime.Configuration;
using Jason.Runtime.Plugins.Manifest;
using Jason.Runtime.Plugins.Registry;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jason.Runtime.Tests.Plugins;

/// <summary>
/// The grant of the journal kinds a plugin declared. Declaring a kind asks for it and the grant decides, by the rule every
/// capability follows: <c>*</c> admits every declared kind and nothing more, a named kind the manifest does not declare
/// admits nothing and is a warning, and a declared kind nobody granted is simply not admitted.
/// </summary>
public sealed class SubscriptionGrantTests
{
    private static readonly PluginManifest Declares = Manifest(["workitem_failed", "campaign_created"]);

    [Fact]
    public void A_plugin_nobody_decided_about_is_admitted_to_no_kind()
    {
        var grants = GrantResolver.Resolve(Declares, null);

        Assert.Empty(grants.Subscriptions);
        Assert.Empty(grants.Warnings);
    }

    [Fact]
    public void A_star_admits_every_declared_kind_and_nothing_more()
    {
        var grants = GrantResolver.Resolve(Declares, new PluginGrant { Subscriptions = ["*"] });

        Assert.Equal(["workitem_failed", "campaign_created"], grants.Subscriptions);
        Assert.Empty(grants.Warnings);
    }

    [Fact]
    public void A_subset_admits_exactly_the_kinds_it_names()
    {
        var grants = GrantResolver.Resolve(Declares, new PluginGrant { Subscriptions = ["campaign_created", "campaign_created"] });

        Assert.Equal(["campaign_created"], grants.Subscriptions);
        Assert.Empty(grants.Warnings);
    }

    [Fact]
    [Critical(Family.R5, "declaration then grant")]
    public void A_grant_naming_a_kind_the_manifest_does_not_declare_admits_nothing_and_says_so()
    {
        var grants = GrantResolver.Resolve(Declares, new PluginGrant { Subscriptions = ["contacts_added", "workitem_failed", "Workitem_Failed"] });

        Assert.Equal(["workitem_failed"], grants.Subscriptions);
        Assert.Equal(["grants.subscriptions[0]", "grants.subscriptions[2]"], grants.Warnings.Select(warning => warning.Path));
        Assert.All(grants.Warnings, warning => Assert.Equal(ProblemCodes.GrantUnrequested, warning.Code));
        Assert.All(grants.Warnings, warning => Assert.False(warning.Environmental));
    }

    [Fact]
    public void A_declared_kind_nobody_granted_is_not_admitted_and_is_no_problem()
    {
        var grants = GrantResolver.Resolve(Declares, new PluginGrant { Exec = ["*"] });

        Assert.Empty(grants.Subscriptions);
        Assert.Empty(grants.Warnings);
    }

    [Fact]
    public void A_star_over_a_manifest_that_declares_no_subscriptions_admits_nothing_and_warns_of_nothing()
    {
        var grants = GrantResolver.Resolve(Manifest([]), new PluginGrant { Subscriptions = ["*"] });

        Assert.Empty(grants.Subscriptions);
        Assert.Empty(grants.Warnings);
    }

    [Fact]
    public void A_grant_of_subscriptions_is_held_to_the_shape_of_a_journal_kind()
    {
        var result = new PluginsOptionsValidator().Validate(null, new PluginsOptions
        {
            Grants = new Dictionary<string, PluginGrant>(StringComparer.OrdinalIgnoreCase)
            {
                ["fake"] = new PluginGrant { Subscriptions = ["*", "workitem_failed", "Not A Kind"] },
            },
        });

        var failure = Assert.Single(result.Failures!);
        Assert.StartsWith("Plugins:Grants:<key>:Subscriptions[2] must be \"*\" or a journal kind the manifest declares; ", failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_grant_is_read_by_the_load_and_held_by_its_snapshot_until_the_next()
    {
        using var dir = new TempDataDir();
        TestPlugins.Write(
            dir.Paths,
            "watcher",
            TestPlugins.Manifest("watcher", extra: "subscriptions: [workitem_failed]\n"),
            "export function invoke() { return { result: {} }; }");
        var options = new PluginsOptions();
        options.Grants["watcher"] = new PluginGrant { Subscriptions = ["*"] };
        var loader = new PluginLoader(
            dir.Paths,
            new ExecutableResolver(TestPlugins.SearchPath),
            new TestOptionsMonitor<PluginsOptions>(options),
            TimeProvider.System,
            NullLogger<PluginLoader>.Instance);

        var first = await loader.LoadAsync(SnapshotSource.Reload, Ct);
        options.Grants["watcher"] = new PluginGrant();
        var second = await loader.LoadAsync(SnapshotSource.Reload, Ct);

        var before = Assert.Single(first.Snapshot!.Plugins);
        Assert.Equal(PluginStatus.Valid, before.Status);
        Assert.Equal(["workitem_failed"], before.Manifest.Subscriptions);
        Assert.Equal(["workitem_failed"], before.Grants.Subscriptions);
        Assert.Empty(before.Problems);
        Assert.Empty(Assert.Single(second.Snapshot!.Plugins).Grants.Subscriptions);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PluginManifest Manifest(IReadOnlyList<string> subscriptions) => new(
        "fake",
        "1.0.0",
        PluginKind.Subscriber,
        null,
        null,
        null,
        new ManifestContracts([1], []),
        [],
        new PluginEntry("main.js", "invoke"),
        new CapabilityRequests(null, null, null),
        new ManifestLimits(null, null),
        Binding: null)
    {
        Subscriptions = subscriptions,
    };
}
