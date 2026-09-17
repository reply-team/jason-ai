using System.Net;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Plugins;
using Jason.Runtime.Configuration;
using Jason.Runtime.Plugins.Registry;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Plugins;

/// <summary>
/// The registry through the API a person and an agent both use. What matters is the two halves of the contract:
/// a reload either activates the whole candidate set or changes nothing at all, and what a plugin may do is
/// always shown as requested against granted.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class PluginServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_installation_with_no_plugins_answers_an_empty_registry()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var registry = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginList, null, Ct);

        Assert.Equal(0, registry.Snapshot.PluginCount);
        Assert.Empty(registry.Plugins);
        Assert.StartsWith("snp_", registry.Snapshot.Id, StringComparison.Ordinal);
        Assert.Equal(SnapshotSource.Startup, registry.LastReload!.Source);
        Assert.True(registry.LastReload.Activated);
        Assert.True(registry.Activated);
        Assert.Empty(registry.LastReload.Candidates);
    }

    [Fact]
    public async Task An_installed_package_is_listed_with_its_digest_and_what_it_may_use()
    {
        using var programs = new TestPrograms();
        programs.AddProgram(FakeProviderCli.ExecutableName);
        var search = new TestSearchPath { Path = programs.Root };
        await using var api = await StartAsync(search, paths =>
        {
            TestPlugins.InstallFakeProvider(paths);
            TestPlugins.Grant(paths, TestPlugins.FakeProviderId, exec: ["*"], env: ["FAKE_TOKEN"]);
        });

        var reloaded = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginReload, new { Reason = "after installing" }, Ct);

        Assert.True(reloaded.Activated);
        Assert.Equal(SnapshotSource.Reload, reloaded.Snapshot.Source);
        var plugin = Assert.Single(reloaded.Plugins);
        Assert.Equal(TestPlugins.FakeProviderId, plugin.Id);
        Assert.Equal(PluginStatus.Valid, plugin.Status);
        Assert.Equal(PluginKind.Provider, plugin.Kind);
        Assert.StartsWith("sha256:", plugin.Digest, StringComparison.Ordinal);
        Assert.Contains("echo.run", plugin.Operations, StringComparer.Ordinal);
        Assert.Equal("main.js", plugin.Entry.Module);
        Assert.Empty(plugin.Problems);

        // Requested against granted, for every capability: the whole point of the view.
        Assert.Equal(FakeProviderCli.ExecutableName, Assert.Single(plugin.Capabilities.Exec!.Requested).Name);
        Assert.Equal([FakeProviderCli.ExecutableName], plugin.Capabilities.Exec.Granted);
        Assert.Equal(["localhost:5555", "127.0.0.1:5555"], plugin.Capabilities.Http!.Requested);
        Assert.Empty(plugin.Capabilities.Http.Granted);
        Assert.Equal(["FAKE_TOKEN", "FAKE_OTHER"], plugin.Capabilities.Env!.Requested);
        Assert.Equal(["FAKE_TOKEN"], plugin.Capabilities.Env.Granted);

        // What a route to this plugin has to carry, so an operator can see it before writing one.
        Assert.Equal(["workspace"], plugin.BindingSchema!["required"]!.AsArray().Select(name => name!.GetValue<string>()));

        var listed = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginList, null, Ct);
        Assert.Equal(reloaded.Snapshot.Id, listed.Snapshot.Id);
        Assert.Equal(reloaded.Plugins[0].Digest, Assert.Single(listed.Plugins).Digest);
    }

    [Theory]
    [InlineData(TestPlugins.BindingBoundTooLarge, "yaml_invalid")]
    [InlineData(TestPlugins.BindingTypeAsAList, "field_invalid")]
    public async Task A_manifest_the_reader_cannot_take_at_face_value_is_a_named_problem_rather_than_a_failure_of_the_runtime(
        string fragment,
        string expected)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        TestPlugins.Write(
            api.Paths,
            "unreadable",
            TestPlugins.Manifest("unreadable", extra: fragment),
            "export function invoke() { return { result: {} }; }");

        var error = await api.PostErrorAsync(Operations.PluginReload, null, HttpStatusCode.Conflict, Ct);

        // A deterministic fault in a stranger's package is theirs to fix, so it must never come back as ours to
        // retry: the answer names the file, the place inside it and the rule instead.
        Assert.Equal("plugin_reload_rejected", error.Code);
        Assert.False(error.Retryable);
        var detail = Assert.Single(error.Details!);
        Assert.Equal(expected, detail.Code);
        Assert.StartsWith("unreadable/plugin.yaml#", detail.Field, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_broken_package_is_refused_and_the_previous_snapshot_keeps_working()
    {
        using var programs = new TestPrograms();
        programs.AddProgram(FakeProviderCli.ExecutableName);
        var search = new TestSearchPath { Path = programs.Root };
        await using var api = await StartAsync(search, paths =>
        {
            TestPlugins.InstallFakeProvider(paths);
            TestPlugins.Grant(paths, TestPlugins.FakeProviderId, exec: ["*"]);
        });
        var before = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginList, null, Ct);
        var manifest = Path.Combine(api.Paths.PluginPackageDirectory(TestPlugins.FakeProviderId), "plugin.yaml");
        var original = await File.ReadAllTextAsync(manifest, Ct);
        await File.WriteAllTextAsync(manifest, original.Replace("kind: provider", "kind: bogus", StringComparison.Ordinal), Ct);

        var error = await api.PostErrorAsync(Operations.PluginReload, null, HttpStatusCode.Conflict, Ct);

        Assert.Equal("plugin_reload_rejected", error.Code);
        Assert.False(error.Retryable);
        var detail = Assert.Single(error.Details!);
        Assert.Equal("fake-provider/plugin.yaml#kind", detail.Field);
        Assert.Equal("kind_invalid", detail.Code);

        // Nothing changed: the same snapshot, the same plugin, and a report that says why it stayed.
        var after = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginList, null, Ct);
        Assert.Equal(before.Snapshot.Id, after.Snapshot.Id);
        Assert.Single(after.Plugins);
        Assert.False(after.Activated);
        Assert.False(after.LastReload!.Activated);
        var candidate = Assert.Single(after.LastReload.Candidates);
        Assert.Equal(CandidateStatus.Invalid, candidate.Status);
        Assert.Equal("kind_invalid", Assert.Single(candidate.Problems).Code);
        Assert.Equal("kind", Assert.Single(candidate.Problems).Path);

        await File.WriteAllTextAsync(manifest, original, Ct);
        var repaired = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginReload, null, Ct);
        Assert.NotEqual(before.Snapshot.Id, repaired.Snapshot.Id);
        Assert.Equal(PluginStatus.Valid, Assert.Single(repaired.Plugins).Status);
    }

    [Fact]
    public async Task A_plugin_the_machine_cannot_run_is_listed_without_holding_the_others_back()
    {
        using var programs = new TestPrograms();
        programs.AddProgram(FakeProviderCli.ExecutableName);
        var search = new TestSearchPath { Path = programs.Root };
        await using var api = await StartAsync(search, paths =>
        {
            TestPlugins.InstallFakeProvider(paths);
            TestPlugins.Grant(paths, TestPlugins.FakeProviderId, exec: ["*"]);
            TestPlugins.Write(
                paths,
                "needs-a-tool",
                TestPlugins.Manifest("needs-a-tool", extra: "capabilities:\n  exec:\n    executables:\n      - name: not-installed-anywhere\n"),
                "export function invoke() { return { result: {} }; }");
        });

        var reloaded = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginReload, null, Ct);

        Assert.True(reloaded.Activated);
        Assert.Equal(2, reloaded.Plugins.Count);
        Assert.Equal(PluginStatus.Valid, reloaded.Plugins.Single(p => p.Id == TestPlugins.FakeProviderId).Status);
        var held = reloaded.Plugins.Single(p => p.Id == "needs-a-tool");
        Assert.Equal(PluginStatus.Unavailable, held.Status);
        Assert.Equal("executable_missing", Assert.Single(held.Problems).Code);
        Assert.Null(Assert.Single(held.Capabilities.Exec!.Requested).Path);

        // Repairing the machine is a reload away: the search path is read again, not remembered.
        using var repaired = new TestPrograms();
        repaired.AddProgram(FakeProviderCli.ExecutableName);
        repaired.AddProgram("not-installed-anywhere");
        search.Path = repaired.Root;

        var again = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginReload, null, Ct);

        Assert.Equal(PluginStatus.Valid, again.Plugins.Single(p => p.Id == "needs-a-tool").Status);
        Assert.NotNull(Assert.Single(again.Plugins.Single(p => p.Id == "needs-a-tool").Capabilities.Exec!.Requested).Path);
    }

    [Fact]
    public async Task A_version_command_runs_only_for_a_program_the_user_granted()
    {
        var variable = "FAKE_CLI_RECORD_" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        var record = Path.Combine(Path.GetTempPath(), "jason-tests", variable + ".txt");
        Directory.CreateDirectory(Path.GetDirectoryName(record)!);
        Environment.SetEnvironmentVariable(variable, record);
        try
        {
            var search = new TestSearchPath { Path = TestPlugins.FakeCliDirectory };
            await using var api = await StartAsync(search, paths =>
            {
                Install(paths, variable);
                TestPlugins.Grant(paths, "records", exec: ["*"], env: ["*"]);
            });

            var granted = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginReload, null, Ct);

            Assert.Equal("1.2.3", Assert.Single(Assert.Single(granted.Plugins).Capabilities.Exec!.Requested).Version);
            var lines = await File.ReadAllLinesAsync(record, Ct);
            Assert.NotEmpty(lines);
            Assert.All(lines, line => Assert.StartsWith("argv=--version", line, StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
            File.Delete(record);
        }
    }

    [Fact]
    public async Task A_program_nobody_granted_is_resolved_but_never_started()
    {
        var variable = "FAKE_CLI_RECORD_" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        var record = Path.Combine(Path.GetTempPath(), "jason-tests", variable + ".txt");
        Directory.CreateDirectory(Path.GetDirectoryName(record)!);
        Environment.SetEnvironmentVariable(variable, record);
        try
        {
            var search = new TestSearchPath { Path = TestPlugins.FakeCliDirectory };
            await using var api = await StartAsync(search, paths =>
            {
                Install(paths, variable);

                // The variable is granted, so the program would record itself the moment it ran. It must not run.
                TestPlugins.Grant(paths, "records", env: ["*"]);
            });

            var ungranted = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginReload, null, Ct);

            var executable = Assert.Single(Assert.Single(ungranted.Plugins).Capabilities.Exec!.Requested);
            Assert.NotNull(executable.Path);
            Assert.Null(executable.Version);
            Assert.False(File.Exists(record), "a reload started a program the user had not granted");
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
            File.Delete(record);
        }
    }

    [Fact]
    public async Task A_notification_plugin_is_listed_with_its_kind_and_no_operations()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, prepare: paths =>
        {
            File.WriteAllText(paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
            TestPlugins.Write(
                paths,
                "notifier",
                """
                manifest_version: 1
                id: notifier
                version: 0.2.0
                kind: notification
                contracts:
                  protocol: [1]
                """,
                "export function invoke() { return { result: {} }; }");
        });

        var reloaded = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginReload, null, Ct);

        var plugin = Assert.Single(reloaded.Plugins);
        Assert.Equal(PluginKind.Notification, plugin.Kind);
        Assert.Equal(PluginStatus.Valid, plugin.Status);
        Assert.Empty(plugin.Operations);
        Assert.Empty(plugin.Contracts.Operations);
    }

    [Fact]
    public async Task A_grant_the_manifest_never_asked_for_is_shown_as_a_problem_of_the_settings()
    {
        using var programs = new TestPrograms();
        programs.AddProgram(FakeProviderCli.ExecutableName);
        await using var api = await StartAsync(new TestSearchPath { Path = programs.Root }, paths =>
        {
            TestPlugins.InstallFakeProvider(paths);
            TestPlugins.Grant(paths, TestPlugins.FakeProviderId, http: ["evil.test"]);
        });

        var reloaded = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginReload, null, Ct);

        var plugin = Assert.Single(reloaded.Plugins);
        Assert.Equal(PluginStatus.Valid, plugin.Status);
        Assert.Equal("grant_unrequested", Assert.Single(plugin.Problems).Code);
        Assert.Empty(plugin.Capabilities.Http!.Granted);
    }

    [Fact]
    public async Task A_reason_longer_than_the_limit_names_the_field()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(Operations.PluginReload, new { Reason = new string('x', 2001) }, HttpStatusCode.BadRequest, Ct);

        Assert.Equal("validation_failed", error.Code);
        Assert.Equal("reason", Assert.Single(error.Details!).Field);
    }

    [Fact]
    public async Task A_caller_cannot_reload_as_the_runtime_itself()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var error = await api.PostErrorAsync(
            Operations.PluginReload,
            new { Actor = new { Type = "system", Id = "runtime" } },
            HttpStatusCode.BadRequest,
            Ct);

        Assert.Equal("validation_failed", error.Code);
        Assert.Equal("actor.type", Assert.Single(error.Details!).Field);
    }

    /// <summary>
    /// A reload is an operator asking to make the file true now, so the seam's answer — work from the last value
    /// that validated — would be the wrong one here: it would activate a set of packages built from settings the
    /// file no longer holds. What it must not be either is an exception nobody translated, which is what a
    /// mistyped plugin ceiling used to produce. It is the same rejected reload a bad package gets, naming the
    /// setting that broke, with the previous snapshot still active and still listed.
    /// </summary>
    [Fact]
    public async Task A_reload_against_a_refused_plugins_section_is_rejected_with_the_setting_it_names()
    {
        using var programs = new TestPrograms();
        programs.AddProgram(FakeProviderCli.ExecutableName);
        var search = new TestSearchPath { Path = programs.Root };
        await using var api = await StartAsync(search, paths =>
        {
            TestPlugins.InstallFakeProvider(paths);
            TestPlugins.Grant(paths, TestPlugins.FakeProviderId, exec: ["*"]);
            File.WriteAllText(paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
        });

        var active = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginReload, null, Ct);
        Assert.Single(active.Plugins);

        // A memory ceiling below the floor the validator holds: the section, refused.
        File.WriteAllText(api.Paths.UserSettingsFile, InvalidPlugins);
        Assert.True(await TestOptions.RefusedOnceAsync(api.Resolve<LiveSettings<PluginsOptions>>(), Ct));

        var error = await api.PostErrorAsync(Operations.PluginReload, null, HttpStatusCode.Conflict, Ct);

        Assert.Equal("plugin_reload_rejected", error.Code);
        var detail = Assert.Single(error.Details!);
        Assert.Equal("plugins_settings_invalid", detail.Code);
        Assert.Equal("Plugins:Limits:MemoryMb", detail.Field);

        // Nothing moved: the package that was active still is, and the report says why nothing changed.
        var listed = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginList, null, Ct);
        Assert.Equal(active.Snapshot.Id, listed.Snapshot.Id);
        Assert.Single(listed.Plugins);
        Assert.False(listed.Activated);
    }

    /// <summary>A plugin memory ceiling below the floor the validator holds, which refuses the whole section.</summary>
    private const string InvalidPlugins = """{"Dispatcher":{"Enabled":false},"Plugins":{"Limits":{"MemoryMb":0}}}""";

    internal static void Install(JasonPaths paths, string variable) => TestPlugins.Write(
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

    internal static Task<RuntimeApiFixture> StartAsync(TestSearchPath search, Action<JasonPaths> prepare) =>
        RuntimeApiFixture.StartAsync(
            Ct,
            prepare: prepare,
            configureServices: services => services.AddSingleton<ISearchPath>(search));
}
