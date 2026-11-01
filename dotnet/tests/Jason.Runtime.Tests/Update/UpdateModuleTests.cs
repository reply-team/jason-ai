using Jason.Contracts.Update;
using Jason.Runtime.Hosting.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Update;

/// <summary>
/// The runtime's unattended check reads the feed through the client the update module makes. With no handler of a
/// test's own that is the network's client, and the default feed is on the product's own address, which answers with
/// a redirect to where the release is stored: so that client follows it.
/// </summary>
public class UpdateModuleTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_runtimes_feed_client_follows_the_address_to_the_release_it_redirects_to()
    {
        using var tree = new TempTree();
        var store = tree.NewDirectory("store");
        File.WriteAllText(
            Path.Combine(store, ReleaseAssets.Manifest),
            """
            {"schema":1,"version":"9.9.9","published_at":"2026-09-19T08:00:00Z",
             "artifacts":{"win-x64":{"asset":"jason-win-x64.zip","sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","size":1}},
             "release_notes_url":"https://example.test/notes"}
            """);
        using var address = new LoopbackReleases(store);
        await using var services = new ServiceCollection().AddUpdateModule(feedHandler: null).BuildServiceProvider();

        var manifest = await services.GetRequiredService<UpdateFeed>()
            .ReadAsync(new Uri($"{address.Releases}/latest/download/{ReleaseAssets.Manifest}"), Ct);

        Assert.Equal(SemanticVersion.Parse("9.9.9"), manifest.Version);
        Assert.Equal(
            ["GET /releases/latest/download/manifest.json", "GET /store/releases/latest/download/manifest.json"],
            address.Asked);
    }
}
