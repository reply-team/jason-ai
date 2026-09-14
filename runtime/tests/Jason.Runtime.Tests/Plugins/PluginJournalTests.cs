using System.Net;
using Jason.Contracts.Api;
using Jason.Contracts.Plugins;
using Jason.Runtime.Journal;

namespace Jason.Runtime.Tests.Plugins;

/// <summary>
/// Which packages were active, at which digests, from when. The chronicle carries it because wave after wave the
/// question "what was running when this happened" is the one nobody can answer afterwards otherwise.
/// </summary>
public class PluginJournalTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_activated_reload_is_written_down_once()
    {
        using var programs = new TestPrograms();
        programs.AddProgram("dotnet");
        await using var api = await PluginServiceTests.StartAsync(new TestSearchPath { Path = programs.Root }, paths =>
        {
            TestPlugins.InstallFakeProvider(paths);
            TestPlugins.Grant(paths, TestPlugins.FakeProviderId, exec: ["*"]);
        });
        var atStartup = await EntriesAsync(api);
        var startup = Assert.Single(atStartup.Items);
        Assert.Equal(ActorType.System, startup.Actor.Type);

        var reloaded = await api.PostOkAsync<PluginRegistryDto>(
            Operations.PluginReload,
            new { Actor = new { Type = "human" }, Reason = "after installing the package" },
            Ct);

        var entries = await EntriesAsync(api);
        Assert.Equal(2, entries.Items.Count);
        var entry = entries.Items[0];
        Assert.Equal(JournalKinds.PluginsReloaded, entry.Kind);
        Assert.Equal(reloaded.Snapshot.Id, entry.Key);
        Assert.Null(entry.CampaignId);
        Assert.Equal(ActorType.Human, entry.Actor.Type);
        Assert.Equal("after installing the package", entry.Reason);
        Assert.Equal(reloaded.Snapshot.Id, (string?)entry.New!["snapshot_id"]);
        Assert.Equal("reload", (string?)entry.New["source"]);
        var plugin = entry.New["plugins"]!.AsArray()[0]!;
        Assert.Equal(TestPlugins.FakeProviderId, (string?)plugin["id"]);
        Assert.Equal("1.0.0", (string?)plugin["version"]);
        Assert.Equal(reloaded.Plugins[0].Digest, (string?)plugin["digest"]);
        Assert.Equal("valid", (string?)plugin["status"]);
    }

    [Fact]
    public async Task A_rejected_reload_writes_nothing()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, prepare: paths =>
        {
            File.WriteAllText(paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
            TestPlugins.Write(paths, "broken", TestPlugins.Manifest("broken").Replace("kind: provider", "kind: bogus", StringComparison.Ordinal), "export function invoke() {}");
        });
        var before = await EntriesAsync(api);

        var error = await api.PostErrorAsync(Operations.PluginReload, null, HttpStatusCode.Conflict, Ct);

        Assert.Equal("plugin_reload_rejected", error.Code);
        var after = await EntriesAsync(api);
        Assert.Equal(before.Items.Count, after.Items.Count);
    }

    [Fact]
    public async Task The_kind_belongs_to_the_runtime_and_nobody_else()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            Operations.JournalAppend,
            new { Kind = JournalKinds.PluginsReloaded },
            HttpStatusCode.BadRequest,
            Ct);

        Assert.Equal("reserved_kind", error.Code);
        Assert.Contains(JournalKinds.PluginsReloaded, JournalKinds.Reserved);
    }

    private static Task<Page<JournalEntryDto>> EntriesAsync(RuntimeApiFixture api) =>
        api.PostOkAsync<Page<JournalEntryDto>>(Operations.JournalList, new { Kind = JournalKinds.PluginsReloaded }, Ct);
}
