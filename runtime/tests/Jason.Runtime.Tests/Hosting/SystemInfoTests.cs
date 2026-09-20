using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Contracts.Update;
using Jason.Runtime.Hosting;
using Jason.Runtime.Persistence;
using Jason.Runtime.Execution;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.Tests.Routing;
using Jason.Runtime.Update;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Hosting;

public class SystemInfoTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// A start that migrated says what it applied and where it put the backup. Both are things only that start
    /// knows, and the one caller that has to know them cannot open the database to find out.
    /// </summary>
    /// <remarks>
    /// The applier is that caller. After it swaps a binary and starts it, the question it must answer is "did
    /// this start migrate, and if it did, which file is the database as it was before?" — because the answer
    /// decides what a rollback has to put back. The migration report has carried both since it was written;
    /// only the answer left them out.
    /// </remarks>
    [Fact]
    public async Task A_start_that_migrated_says_what_it_applied_and_where_it_put_the_backup()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(Ct, prepare: AtTheMigrationBeforeLast);

        var info = await fixture.PostOkAsync<SystemInfoResponse>(Operations.SystemInfo, null, Ct);

        Assert.NotEmpty(info.Database.AppliedMigrations);
        var newly = Assert.Single(info.Database.NewlyApplied);
        Assert.Equal(info.Database.AppliedMigrations[^1], newly);

        Assert.NotNull(info.Database.BackupFile);
        Assert.True(
            File.Exists(info.Database.BackupFile),
            $"system.info named a backup that is not there: {info.Database.BackupFile}");
        Assert.StartsWith(fixture.Paths.BackupsDirectory, info.Database.BackupFile, StringComparison.Ordinal);
    }

    /// <summary>And a start that migrated nothing says so, rather than leaving a caller to guess from a null.</summary>
    /// <remarks>
    /// It takes two starts to be that start. A runtime over an empty data directory creates the database by
    /// running every migration there is, so its own report is the longest one it will ever write — the second
    /// start over the same directory is the first one that finds nothing to do. That is what the applier meets
    /// after a swap whose new binary carries no new migration, and the answer is what tells it that a rollback
    /// has no database to put back.
    /// </remarks>
    [Fact]
    public async Task A_start_that_migrated_nothing_says_that_too()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);

        IReadOnlyList<string> creating;
        await using (var first = await RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet, Ct))
        {
            // Named rather than assumed: this is the run the assertions below must not be satisfied by.
            creating = (await InfoAsync(first)).Database.NewlyApplied;
            Assert.NotEmpty(creating);
        }

        await using var second = await RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet, Ct);

        var info = await InfoAsync(second);

        // The same schema the first start left, and nothing of it applied here: there was nothing to back up
        // and nothing was replaced, which is exactly what a start of an up-to-date installation looks like.
        Assert.Equal(creating, info.Database.AppliedMigrations);
        Assert.Empty(info.Database.NewlyApplied);
        Assert.Null(info.Database.BackupFile);
    }

    /// <summary>What <c>system.info</c> answers a runtime that was started directly rather than through a fixture.</summary>
    private static async Task<SystemInfoResponse> InfoAsync(RunningRuntime runtime)
    {
        using var http = new HttpClient { BaseAddress = runtime.BaseUrl };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", runtime.Token);
        using var body = new StringContent("{}", Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(Operations.Route(Operations.SystemInfo), body, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonSerializer.Deserialize<SystemInfoResponse>(await response.Content.ReadAsStringAsync(Ct), JasonJson.Options)!;
    }

    /// <summary>
    /// A database at the migration before the last one, made by EF's own migrator rather than by a file checked
    /// in: the triggers and the indexes live in the migrations, so what comes out is a real older database.
    /// </summary>
    private static void AtTheMigrationBeforeLast(JasonPaths paths)
    {
        Directory.CreateDirectory(paths.StateDirectory);
        using var db = new JasonDbContext(JasonDbContext.CreateOptions(paths.DatabaseFile));
        var all = db.Database.GetMigrations().ToList();
        db.GetService<IMigrator>().Migrate(all[^2]);
    }

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
