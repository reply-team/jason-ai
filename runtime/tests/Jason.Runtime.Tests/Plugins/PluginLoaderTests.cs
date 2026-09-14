using Jason.Contracts.Discovery;
using Jason.Contracts.Plugins;
using Jason.Runtime.Configuration;
using Jason.Runtime.Plugins.Manifest;
using Jason.Runtime.Plugins.Registry;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jason.Runtime.Tests.Plugins;

/// <summary>
/// One load of the plugins directory: what becomes a snapshot, what holds the whole set out, and what holds back
/// only the plugin it belongs to. The distinction is the point — an autostarted runtime sees a different search
/// path than a shell does, and one uninstalled vendor tool must never empty the registry.
/// </summary>
public class PluginLoaderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Nothing_installed_is_an_empty_snapshot_that_still_activates()
    {
        using var dir = new TempDataDir();
        using var programs = new TestPrograms();

        var load = await NewLoader(dir.Paths, programs).LoadAsync(SnapshotSource.Startup, Ct);

        Assert.NotNull(load.Snapshot);
        Assert.Empty(load.Snapshot.Plugins);
        Assert.StartsWith("snp_", load.Snapshot.Id, StringComparison.Ordinal);
        Assert.Equal(SnapshotSource.Startup, load.Snapshot.Source);
        Assert.True(load.Report.Activated);
        Assert.Empty(load.Report.Candidates);
    }

    [Fact]
    public async Task The_checked_in_package_loads_with_its_digest_and_its_operations()
    {
        using var dir = new TempDataDir();
        using var programs = new TestPrograms();
        programs.AddProgram("dotnet");
        var root = TestPlugins.InstallFakeProvider(dir.Paths);

        var load = await NewLoader(dir.Paths, programs).LoadAsync(SnapshotSource.Reload, Ct);

        var plugin = Assert.Single(load.Snapshot!.Plugins);
        Assert.Equal(TestPlugins.FakeProviderId, plugin.Manifest.Id);
        Assert.Equal(PluginStatus.Valid, plugin.Status);
        Assert.Equal(root, plugin.Root);
        Assert.StartsWith("sha256:", plugin.Digest, StringComparison.Ordinal);
        Assert.Equal(PackageDigest.Compute(root).Digest, plugin.Digest);
        Assert.True(plugin.Supports("echo.run"));
        Assert.False(plugin.Supports("nothing.here"));
        Assert.Empty(plugin.Problems);

        // Declared and found, but never started: the user granted nothing, so no version was asked for.
        var executable = Assert.Single(plugin.Executables);
        Assert.Equal("dotnet", executable.Name);
        Assert.NotNull(executable.Path);
        Assert.Null(executable.Version);
        Assert.Empty(plugin.Grants.Exec);

        // The manifest's own limits, inside what this installation allows.
        Assert.Equal(new EffectivePluginLimits(20_000, 64), plugin.Limits);
        Assert.Equal(plugin, load.Snapshot.Find(TestPlugins.FakeProviderId));
        Assert.Null(load.Snapshot.Find("nobody"));
    }

    [Fact]
    public async Task A_package_with_no_limits_of_its_own_runs_under_the_installation_defaults()
    {
        using var dir = new TempDataDir();
        using var programs = new TestPrograms();
        TestPlugins.Write(dir.Paths, "plain", TestPlugins.Manifest("plain"), "export function invoke() { return { result: {} }; }");

        var load = await NewLoader(dir.Paths, programs, options => options.Limits.TimeoutMs = 45_000).LoadAsync(SnapshotSource.Reload, Ct);

        Assert.Equal(new EffectivePluginLimits(45_000, 64), Assert.Single(load.Snapshot!.Plugins).Limits);
    }

    [Fact]
    public async Task One_broken_package_keeps_the_whole_set_out()
    {
        using var dir = new TempDataDir();
        using var programs = new TestPrograms();
        programs.AddProgram("dotnet");
        TestPlugins.InstallFakeProvider(dir.Paths);
        TestPlugins.Write(dir.Paths, "broken", TestPlugins.Manifest("broken").Replace("kind: provider", "kind: bogus", StringComparison.Ordinal), "export function invoke() {}");

        var load = await NewLoader(dir.Paths, programs).LoadAsync(SnapshotSource.Reload, Ct);

        Assert.Null(load.Snapshot);
        Assert.False(load.Report.Activated);
        var broken = Assert.Single(load.Report.Candidates, candidate => candidate.Directory == "broken");
        Assert.Equal(CandidateStatus.Invalid, broken.Status);
        Assert.Equal(ProblemCodes.KindInvalid, Assert.Single(broken.Problems).Code);
        Assert.Equal("kind", Assert.Single(broken.Problems).Path);
        Assert.Equal(CandidateStatus.Valid, Assert.Single(load.Report.Candidates, candidate => candidate.Directory == TestPlugins.FakeProviderId).Status);
    }

    [Fact]
    public async Task A_plugin_whose_program_is_missing_holds_back_only_itself()
    {
        using var dir = new TempDataDir();
        using var programs = new TestPrograms();
        programs.AddProgram("dotnet");
        TestPlugins.InstallFakeProvider(dir.Paths);
        TestPlugins.Write(
            dir.Paths,
            "needs-a-tool",
            TestPlugins.Manifest("needs-a-tool", extra: "capabilities:\n  exec:\n    executables:\n      - name: not-installed-anywhere\n"),
            "export function invoke() { return { result: {} }; }");

        var load = await NewLoader(dir.Paths, programs).LoadAsync(SnapshotSource.Reload, Ct);

        Assert.True(load.Report.Activated);
        Assert.Equal(2, load.Snapshot!.Plugins.Count);
        Assert.Equal(PluginStatus.Valid, load.Snapshot.Find(TestPlugins.FakeProviderId)!.Status);

        var held = load.Snapshot.Find("needs-a-tool")!;
        Assert.Equal(PluginStatus.Unavailable, held.Status);
        Assert.Equal(ProblemCodes.ExecutableMissing, Assert.Single(held.Problems).Code);
        Assert.Equal(CandidateStatus.Unavailable, Assert.Single(load.Report.Candidates, c => c.Directory == "needs-a-tool").Status);
    }

    [Fact]
    public async Task A_repaired_search_path_makes_the_same_package_valid()
    {
        using var dir = new TempDataDir();
        using var programs = new TestPrograms();
        var search = new TestSearchPath { Path = programs.Root };
        TestPlugins.Write(
            dir.Paths,
            "needs-a-tool",
            TestPlugins.Manifest("needs-a-tool", extra: "capabilities:\n  exec:\n    executables:\n      - name: not-installed-anywhere\n"),
            "export function invoke() { return { result: {} }; }");
        var loader = NewLoader(dir.Paths, search);
        Assert.Equal(PluginStatus.Unavailable, Assert.Single((await loader.LoadAsync(SnapshotSource.Reload, Ct)).Snapshot!.Plugins).Status);

        programs.AddProgram("not-installed-anywhere");

        var load = await loader.LoadAsync(SnapshotSource.Reload, Ct);

        Assert.Equal(PluginStatus.Valid, Assert.Single(load.Snapshot!.Plugins).Status);
    }

    [Fact]
    public async Task A_directory_that_is_not_a_package_is_skipped_rather_than_blamed()
    {
        using var dir = new TempDataDir();
        using var programs = new TestPrograms();
        Directory.CreateDirectory(Path.Combine(dir.Paths.PluginsDirectory, "notes"));
        File.WriteAllText(Path.Combine(dir.Paths.PluginsDirectory, "notes", "README.md"), "a stray folder");
        Directory.CreateDirectory(Path.Combine(dir.Paths.PluginsDirectory, ".hidden"));
        File.WriteAllText(Path.Combine(dir.Paths.PluginsDirectory, ".hidden", "plugin.yaml"), "not even read");

        var load = await NewLoader(dir.Paths, programs).LoadAsync(SnapshotSource.Reload, Ct);

        Assert.True(load.Report.Activated);
        Assert.Empty(load.Snapshot!.Plugins);
        var skipped = Assert.Single(load.Report.Candidates);
        Assert.Equal("notes", skipped.Directory);
        Assert.Equal(CandidateStatus.Skipped, skipped.Status);
        Assert.Equal(ProblemCodes.ManifestMissing, Assert.Single(skipped.Problems).Code);
    }

    [Fact]
    public async Task A_grant_the_manifest_never_asked_for_is_a_warning_the_plugin_carries()
    {
        using var dir = new TempDataDir();
        using var programs = new TestPrograms();
        programs.AddProgram("dotnet");
        TestPlugins.InstallFakeProvider(dir.Paths);

        var load = await NewLoader(dir.Paths, programs, options =>
            options.Grants[TestPlugins.FakeProviderId] = new PluginGrant { Http = ["evil.test"] }).LoadAsync(SnapshotSource.Reload, Ct);

        var plugin = Assert.Single(load.Snapshot!.Plugins);
        Assert.Equal(PluginStatus.Valid, plugin.Status);
        Assert.Equal(ProblemCodes.GrantUnrequested, Assert.Single(plugin.Problems).Code);
        Assert.Empty(plugin.Grants.Http);
    }

    [Fact]
    public async Task A_version_command_runs_for_a_granted_program()
    {
        var (variable, record) = NewRecording();
        try
        {
            using var dir = new TempDataDir();
            InstallRecordingPackage(dir.Paths, variable);

            var load = await NewLoader(dir.Paths, new TestSearchPath { Path = TestPlugins.FakeCliDirectory }, options =>
                options.Grants["records"] = new PluginGrant { Exec = ["*"], Env = ["*"] }).LoadAsync(SnapshotSource.Reload, Ct);

            var plugin = Assert.Single(load.Snapshot!.Plugins);
            Assert.Equal(PluginStatus.Valid, plugin.Status);
            Assert.Equal("1.2.3", Assert.Single(plugin.Executables).Version);
            Assert.Single(await File.ReadAllLinesAsync(record, Ct));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
            File.Delete(record);
        }
    }

    [Fact]
    public async Task A_version_command_never_runs_for_a_program_the_user_did_not_grant()
    {
        var (variable, record) = NewRecording();
        try
        {
            using var dir = new TempDataDir();
            InstallRecordingPackage(dir.Paths, variable);

            // The environment is granted, so the program would record itself if it ran at all. It must not run:
            // a reload never starts a program the user has not consented to.
            var load = await NewLoader(dir.Paths, new TestSearchPath { Path = TestPlugins.FakeCliDirectory }, options =>
                options.Grants["records"] = new PluginGrant { Env = ["*"] }).LoadAsync(SnapshotSource.Reload, Ct);

            var plugin = Assert.Single(load.Snapshot!.Plugins);
            Assert.Equal(PluginStatus.Valid, plugin.Status);
            var executable = Assert.Single(plugin.Executables);
            Assert.NotNull(executable.Path);
            Assert.Null(executable.Version);
            Assert.False(File.Exists(record), "an ungranted executable was started at reload");
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
            File.Delete(record);
        }
    }

    private static (string Variable, string Record) NewRecording()
    {
        var variable = "FAKE_CLI_RECORD_" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        var record = Path.Combine(Path.GetTempPath(), "jason-tests", variable + ".txt");
        Directory.CreateDirectory(Path.GetDirectoryName(record)!);
        Environment.SetEnvironmentVariable(variable, record);
        return (variable, record);
    }

    private static void InstallRecordingPackage(JasonPaths paths, string variable) => TestPlugins.Write(
        paths,
        "records",
        TestPlugins.Manifest("records", extra: $"""
            capabilities:
              exec:
                executables:
                  - name: {FakeProviderCli.ExecutableName}
                    version_command: ["--version"]
              env:
                variables: [{variable}]
            """),
        "export function invoke() { return { result: {} }; }");

    private static PluginLoader NewLoader(JasonPaths paths, TestPrograms programs, Action<PluginsOptions>? configure = null) =>
        NewLoader(paths, new TestSearchPath { Path = programs.Root }, configure);

    private static PluginLoader NewLoader(JasonPaths paths, TestSearchPath search, Action<PluginsOptions>? configure = null)
    {
        var options = new PluginsOptions();
        configure?.Invoke(options);
        return new PluginLoader(
            paths,
            new ExecutableResolver(search),
            new TestOptionsMonitor<PluginsOptions>(options),
            TimeProvider.System,
            NullLogger<PluginLoader>.Instance);
    }
}
