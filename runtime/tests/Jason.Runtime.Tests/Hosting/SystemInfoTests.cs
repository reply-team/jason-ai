using Jason.Contracts.Api;
using Jason.Contracts.Update;
using Jason.Runtime.Execution;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.Tests.Routing;
using Jason.Runtime.Update;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Hosting;

public class SystemInfoTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_fixture_the_test_wrote_no_settings_for_runs_without_a_dispatcher()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(Ct);

        var info = await fixture.PostOkAsync<SystemInfoResponse>(Operations.SystemInfo, null, Ct);

        // Nothing scans behind the test's back: work seeded here stays where the test put it.
        Assert.Equal(DispatcherState.Disabled, info.Dispatcher.State);
        Assert.Equal(0, info.Dispatcher.Scans);
    }

    [Fact]
    public async Task System_info_reports_the_plugin_registry_from_the_active_snapshot()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(Ct);

        var info = await fixture.PostOkAsync<SystemInfoResponse>(Operations.SystemInfo, null, Ct);

        Assert.StartsWith("snp_", info.Plugins.SnapshotId, StringComparison.Ordinal);
        Assert.Equal(0, info.Plugins.ActiveCount);
        Assert.True(info.Plugins.LastReloadActivated);
        Assert.Equal(TimeSpan.Zero, info.Plugins.LoadedAt.Offset);
    }

    /// <summary>
    /// The cheapest way to see where work is being sent, beside which packages are active — the two questions
    /// are one question, and a runtime that answers only half of it sends an operator looking for the other.
    /// </summary>
    [Fact]
    public async Task System_info_reports_where_work_is_being_sent()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths =>
            {
                File.WriteAllText(paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
                TestPlugins.InstallFakeProvider(paths);
                TestRoutes.WriteGlobal(paths, TestRoutes.GlobalDefault(TestPlugins.FakeProviderId, RouteActivationTests.Workspace()));
            },
            configureServices: services => services.AddSingleton(TestPlugins.SearchPath));
        var campaign = (await fixture.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "Routed" }, Ct)).Id;
        await TestRoutes.SetCampaignAsync(fixture, campaign, "campaign.get", TestPlugins.FakeProviderId, RouteActivationTests.Workspace("east"), Ct);

        var info = await fixture.PostOkAsync<SystemInfoResponse>(Operations.SystemInfo, null, Ct);

        Assert.Equal(fixture.Resolve<RouteRegistry>().Snapshot.Id, info.Routes.SnapshotId);
        Assert.Equal(TestPlugins.FakeProviderId, info.Routes.GlobalDefaultPlugin);
        Assert.Equal(0, info.Routes.GlobalOverrideCount);
        Assert.Equal(1, info.Routes.CampaignRouteCount);
        Assert.Equal(TimeSpan.Zero, info.Routes.ActivatedAt.Offset);
    }

    [Fact]
    public async Task System_info_reports_a_runtime_that_routes_nothing_anywhere()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(Ct);

        var info = await fixture.PostOkAsync<SystemInfoResponse>(Operations.SystemInfo, null, Ct);

        Assert.StartsWith("rts_", info.Routes.SnapshotId, StringComparison.Ordinal);
        Assert.Null(info.Routes.GlobalDefaultPlugin);
        Assert.Equal(0, info.Routes.GlobalOverrideCount);
        Assert.Equal(0, info.Routes.CampaignRouteCount);
    }

    [Fact]
    public async Task System_info_reports_the_dispatcher_even_when_it_is_turned_off()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths => File.WriteAllText(paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false}}"""));

        var info = await fixture.PostOkAsync<SystemInfoResponse>(Operations.SystemInfo, null, Ct);

        Assert.Equal(DispatcherState.Disabled, info.Dispatcher.State);
        Assert.Equal(10, info.Dispatcher.TickSeconds);
        Assert.Equal(4, info.Dispatcher.MaxParallel);
        Assert.Equal(0, info.Dispatcher.RunningAttempts);
        Assert.Null(info.Dispatcher.LastScanAt);
        Assert.Equal(0, info.Dispatcher.Scans);
    }

    [Fact]
    public async Task The_dispatcher_section_is_read_live_from_the_status_and_the_settings()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths => File.WriteAllText(paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false,"TickSeconds":3}}"""));

        var status = fixture.Resolve<DispatcherStatus>();
        status.State = DispatcherState.Running;
        status.MaxParallel = 7;
        status.Scans = 11;
        status.LastScanAt = DateTimeOffset.UnixEpoch;
        using var attempt = fixture.Resolve<RunningAttemptRegistry>().Register("att_A", () => { });

        var (_, body) = await fixture.PostAsync(Operations.SystemInfo, null, Ct);

        Assert.Contains("\"state\":\"running\"", body, StringComparison.Ordinal);
        Assert.Contains("\"tick_seconds\":3", body, StringComparison.Ordinal);
        Assert.Contains("\"max_parallel\":7", body, StringComparison.Ordinal);
        Assert.Contains("\"running_attempts\":1", body, StringComparison.Ordinal);
        Assert.Contains("\"scans\":11", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// What the last update check learned, and null until there has been one: a runtime that has not looked
    /// says so, rather than "up to date", and a status line has to be able to tell the two apart.
    /// </summary>
    [Fact]
    public async Task System_info_says_what_the_last_update_check_learned_and_null_before_one()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(Ct);

        var (_, before) = await fixture.PostAsync(Operations.SystemInfo, null, Ct);
        Assert.Contains("\"update\":null", before, StringComparison.Ordinal);

        // Recorded the way the checker records it, so what system.info answers is what the checker learned.
        var manifest = UpdateManifest.Read(
            """{"schema":1,"version":"0.2.0","published_at":"2026-09-19T08:00:00Z","artifacts":{"win-x64":{"asset":"jason-win-x64.zip","sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","size":1}},"release_notes_url":"https://example.test/notes"}""");
        fixture.Resolve<UpdateAdvertisement>().Record(manifest, SemanticVersion.Parse("0.1.0"), new DateTimeOffset(2026, 9, 19, 8, 0, 0, TimeSpan.Zero));

        var info = await fixture.PostOkAsync<SystemInfoResponse>(Operations.SystemInfo, null, Ct);

        Assert.NotNull(info.Update);
        Assert.True(info.Update.Available);
        Assert.Equal("0.2.0", info.Update.Version);
        Assert.Equal(new DateTimeOffset(2026, 9, 19, 8, 0, 0, TimeSpan.Zero), info.Update.CheckedAt);
        Assert.Equal("https://example.test/notes", info.Update.ReleaseNotesUrl);
    }
}
