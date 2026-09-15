using Jason.Contracts.Api;
using Jason.Contracts.Plugins;
using Jason.Runtime.Journal;

namespace Jason.Runtime.Tests.Plugins;

/// <summary>
/// The load that happens because the runtime started. It is the same load a reload performs — and it never gets
/// in the way of the rest of the runtime: a candidate set that cannot be activated leaves the registry empty and
/// the diagnostics on record, not the process down.
/// </summary>
public class PluginStartupTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_package_installed_before_the_runtime_started_is_already_active()
    {
        using var programs = new TestPrograms();
        programs.AddProgram("dotnet");
        await using var api = await PluginServiceTests.StartAsync(new TestSearchPath { Path = programs.Root }, paths =>
        {
            TestPlugins.InstallFakeProvider(paths);
            TestPlugins.Grant(paths, TestPlugins.FakeProviderId, exec: ["*"]);
        });

        var registry = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginList, null, Ct);

        Assert.Equal(SnapshotSource.Startup, registry.Snapshot.Source);
        Assert.Equal(1, registry.Snapshot.PluginCount);
        Assert.Equal(PluginStatus.Valid, Assert.Single(registry.Plugins).Status);
        Assert.True(registry.LastReload!.Activated);

        var entries = await api.PostOkAsync<Page<JournalEntryDto>>(Operations.JournalList, new { Kind = JournalKinds.PluginsReloaded }, Ct);
        var entry = Assert.Single(entries.Items);
        Assert.Equal(new ActorRef(ActorType.System, "runtime"), entry.Actor);
        Assert.Equal(registry.Snapshot.Id, entry.Key);
        Assert.Equal("startup", (string?)entry.New!["source"]);

        var info = await api.PostOkAsync<SystemInfoResponse>(Operations.SystemInfo, null, Ct);
        Assert.Equal(1, info.Plugins.ActiveCount);
        Assert.Equal(registry.Snapshot.Id, info.Plugins.SnapshotId);
        Assert.True(info.Plugins.LastReloadActivated);
    }

    [Fact]
    public async Task A_package_the_manifest_reader_cannot_read_is_named_rather_than_disabling_every_plugin_in_silence()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, prepare: paths =>
        {
            File.WriteAllText(paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
            TestPlugins.Write(
                paths,
                "overflowing",
                TestPlugins.Manifest("overflowing", extra: PluginServiceTests.OverflowingBinding),
                "export function invoke() { return { result: {} }; }");
        });

        // The load itself used to end in an exception, which left the log saying only that the runtime carries on
        // without plugins. Whoever has to fix it needs the package's name and the rule it broke.
        var registry = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginList, null, Ct);

        Assert.False(registry.LastReload!.Activated);
        var candidate = Assert.Single(registry.LastReload.Candidates);
        Assert.Equal("overflowing", candidate.Directory);
        Assert.Equal(CandidateStatus.Invalid, candidate.Status);
        Assert.Equal("yaml_invalid", Assert.Single(candidate.Problems).Code);
    }

    [Fact]
    public async Task A_package_that_cannot_be_loaded_leaves_the_runtime_up_with_an_empty_registry()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, prepare: paths =>
        {
            File.WriteAllText(paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
            TestPlugins.Write(
                paths,
                "broken",
                TestPlugins.Manifest("broken").Replace("operations: [echo.run]", "operations: [Echo.Run]", StringComparison.Ordinal),
                "export function invoke() {}");
        });

        // The runtime answers, which is the whole point: a broken plugin must not stand between a person and
        // their campaigns.
        var registry = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginList, null, Ct);

        Assert.Equal(0, registry.Snapshot.PluginCount);
        Assert.Empty(registry.Plugins);
        Assert.False(registry.Activated);
        Assert.False(registry.LastReload!.Activated);
        Assert.Equal(SnapshotSource.Startup, registry.LastReload.Source);
        var candidate = Assert.Single(registry.LastReload.Candidates);
        Assert.Equal("broken", candidate.Directory);
        Assert.Equal(CandidateStatus.Invalid, candidate.Status);
        Assert.Equal("operation_invalid", Assert.Single(candidate.Problems).Code);
        Assert.Equal("operations[0]", Assert.Single(candidate.Problems).Path);

        var info = await api.PostOkAsync<SystemInfoResponse>(Operations.SystemInfo, null, Ct);
        Assert.Equal(0, info.Plugins.ActiveCount);
        Assert.False(info.Plugins.LastReloadActivated);

        // Nothing was activated, so nothing was written down either.
        var entries = await api.PostOkAsync<Page<JournalEntryDto>>(Operations.JournalList, new { Kind = JournalKinds.PluginsReloaded }, Ct);
        Assert.Empty(entries.Items);
    }
}
