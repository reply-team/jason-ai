using System.Net;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Plugins;
using Jason.Runtime.Configuration;
using Jason.Runtime.Discovery;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Hosting;
using Jason.Runtime.Journal;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Integration;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.Tests.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Tests.Configuration;

public class OptionsHotReloadTests
{

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Critical(Family.C1, "live settings")]
    public async Task A_hand_edit_of_the_user_settings_reaches_the_running_runtime()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths => File.WriteAllText(paths.UserSettingsFile, """{"Dispatcher":{"TickSeconds":7}}"""));

        var monitor = fixture.Runtime.Services.GetRequiredService<IOptionsMonitor<DispatcherOptions>>();
        Assert.Equal(7, monitor.CurrentValue.TickSeconds);

        File.WriteAllText(fixture.Paths.UserSettingsFile, """{"Dispatcher":{"TickSeconds":9}}""");

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (monitor.CurrentValue.TickSeconds != 9 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25, Ct);
        }

        Assert.Equal(9, monitor.CurrentValue.TickSeconds);
    }

    [Fact]
    public async Task An_edited_grant_is_seen_by_the_runtime_that_will_resolve_it_at_the_next_reload()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths => File.WriteAllText(paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false},"Plugins":{"Grants":{"fake":{"Env":["A"]}}}}"""));

        var monitor = fixture.Runtime.Services.GetRequiredService<IOptionsMonitor<PluginsOptions>>();
        Assert.Equal(["A"], monitor.CurrentValue.Grants["fake"].Env);

        File.WriteAllText(fixture.Paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false},"Plugins":{"Grants":{"fake":{"Env":["A","B"]}}}}""");

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (monitor.CurrentValue.Grants["fake"].Env.Count != 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25, Ct);
        }

        Assert.Equal(["A", "B"], monitor.CurrentValue.Grants["fake"].Env);
    }

    /// <summary>
    /// Routes are read rather than bound, so the change token is registered by hand; this is what says the hand
    /// did it. A route still only takes effect when a reload freezes it — what must reach a running runtime is
    /// the edited file, so that the reload has something new to freeze.
    /// </summary>
    [Fact]
    [Critical(Family.W11, "routes are read only at a reload")]
    public async Task An_edited_route_is_seen_by_the_runtime_that_will_freeze_it_at_the_next_reload()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths => File.WriteAllText(paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false},"Routes":{"Default":{"Plugin":"fake-provider","Binding":{"workspace":"west"}}}}"""));

        var monitor = fixture.Runtime.Services.GetRequiredService<IOptionsMonitor<RoutesOptions>>();
        Assert.Equal("fake-provider", monitor.CurrentValue.Default!.Plugin);
        Assert.Equal("west", (string?)Assert.IsType<JsonObject>(monitor.CurrentValue.Default.Binding)["workspace"]);

        File.WriteAllText(fixture.Paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false},"Routes":{"Default":{"Plugin":"other-provider"}}}""");

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (monitor.CurrentValue.Default!.Plugin != "other-provider" && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25, Ct);
        }

        Assert.Equal("other-provider", monitor.CurrentValue.Default!.Plugin);
        Assert.Null(monitor.CurrentValue.Default.Binding);
    }

    [Fact]
    public async Task A_setting_the_runtime_cannot_work_with_stops_the_start_and_leaves_the_lock_free()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Dispatcher":{"TickSeconds":0}}""");

        var failure = await Assert.ThrowsAsync<InvalidSettingsException>(() => RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet, Ct));

        Assert.Contains("Dispatcher:TickSeconds", failure.Message, StringComparison.Ordinal);

        // The failed start gave everything back: the next runtime acquires the instance lock as usual.
        using var free = InstanceLock.Acquire(dir.Paths);
        Assert.NotNull(free);
    }

    /// <summary>
    /// A value of the wrong type in a section the host validates as it starts. The binder's refusal used to
    /// escape as itself, and the host wrote it to the log as the reason it failed to start — with the binder's
    /// message, which repeats what was typed. It is the validator's refusal now: the start still fails, and what
    /// it says, where it is thrown and where it is logged, is the setting's path and the type it has to be.
    /// </summary>
    [Fact]
    [Critical(Family.C1, "a start refusal names the path, never the value")]
    public async Task A_value_of_the_wrong_type_stops_the_start_naming_the_setting_and_never_the_value()
    {
        const string sentinel = "SENTINEL_F";
        const string sentence = "Plugins:Limits:TimeoutMs could not be read as System.Int32.";
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false},"Plugins":{"Limits":{"TimeoutMs":"SENTINEL_F"}}}""");

        var failure = await Assert.ThrowsAsync<InvalidSettingsException>(() => RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet, Ct));

        Assert.Equal("Invalid Jason configuration: " + sentence, failure.Message);
        Assert.Null(failure.InnerException);
        Assert.DoesNotContain(sentinel, failure.ToString(), StringComparison.Ordinal);

        var logs = string.Concat(Directory.GetFiles(dir.Paths.LogsDirectory).Select(File.ReadAllText));
        Assert.DoesNotContain(sentinel, logs, StringComparison.Ordinal);

        // The runtime's own line says why it did not start, by the setting; and nothing says it kept settings it never had.
        var lines = logs.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonNode.Parse(line)!).ToList();
        var refused = Assert.Single(lines, line => (string?)line["@mt"] == "The runtime did not start: its settings were refused: {Failure}");
        Assert.Equal("Error", (string?)refused["@l"]);
        Assert.Equal(sentence, (string?)refused["Failure"]);
        Assert.DoesNotContain(lines, line => ((string?)line["@mt"] ?? string.Empty).Contains("keeps the last ones that validated", StringComparison.Ordinal));
    }

    /// <summary>
    /// A hand edit that leaves the file unparseable used to change nothing and say nothing: the runtime kept what
    /// it had, and the person who typed the comma in the wrong place was never told that it had. It is said once
    /// now, by the exception's type and the position the parser stopped at, and what the runtime keeps is what it
    /// had — not what a file it could not read would have given it, which is every setting at its default.
    /// </summary>
    [Fact]
    [Critical(Family.C1, "a bad live edit keeps the last good settings")]
    public async Task A_settings_file_that_is_not_valid_JSON_is_reported_by_position_while_the_runtime_keeps_what_it_had()
    {
        IConfigurationRoot? configuration = null;
        await using var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            configurationBuilt: built => configuration = built,
            prepare: paths => File.WriteAllText(paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false,"TickSeconds":7}}"""));

        var monitor = fixture.Runtime.Services.GetRequiredService<IOptionsMonitor<DispatcherOptions>>();
        Assert.Equal(7, monitor.CurrentValue.TickSeconds);

        // The second line, and a token that is not the start of a property name, so the position is not (0, 0). The
        // parser's message quotes one byte of such a token, so text in it could not reach the log through that message
        // anyway; the duplicate-key twin below, whose message names the key, is what holds that no message is written.
        File.WriteAllText(fixture.Paths.UserSettingsFile, "{\"Dispatcher\":{\"TickSeconds\":1,\n  unquoted");

        var line = Assert.Single(await SettingsFileLinesAsync(fixture.Paths));
        Assert.Equal("Warning", (string?)line["@l"]);
        Assert.Equal(KeptAtPosition, (string?)line["@mt"]);
        Assert.Equal("JsonException", (string?)line["ExceptionType"]);
        Assert.Equal(2, (int?)line["LineNumber"]);
        Assert.Equal(3, (int?)line["BytePositionInLine"]);
        Assert.Null(line["@x"]);

        // What the runtime works from is what it had.
        Assert.Equal(7, monitor.CurrentValue.TickSeconds);
        Assert.False(monitor.CurrentValue.Enabled);

        // And the raw key reads null: the options in force are the last good ones, not what the configuration now holds.
        Assert.True(
            configuration!["Dispatcher:TickSeconds"] is null,
            $"The raw key reads '{configuration["Dispatcher:TickSeconds"]}' after the failed load.");
        await AssertStillAnswersAsync(fixture);
    }

    /// <summary>
    /// A file that is valid JSON to the reader but holds an escape no text can be made of — half of a surrogate pair, as
    /// <c>"\ud800"</c> — is refused by the parser with an exception of another family than the one a syntax error
    /// throws. It is the same fact about the file: a live edit to it never stops the runtime, the last good settings
    /// stay in force, the person is told once by the failure's type, and a reload answers the settings problem rather
    /// than a server error.
    /// </summary>
    [Fact]
    public async Task A_live_edit_to_a_file_holding_an_undecodable_escape_never_stops_the_runtime_and_the_reload_answers_the_settings_problem()
    {
        const string sentinel = "SENTINEL_HALF_PAIR";
        await using var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths => File.WriteAllText(paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false,"TickSeconds":7}}"""));

        var monitor = fixture.Runtime.Services.GetRequiredService<IOptionsMonitor<DispatcherOptions>>();
        Assert.Equal(7, monitor.CurrentValue.TickSeconds);

        File.WriteAllText(fixture.Paths.UserSettingsFile, "{\"Dispatcher\":{\"Enabled\":false,\"TickSeconds\":1},\"" + sentinel + "\":\"\\ud800\"}");

        var line = Assert.Single(await SettingsFileLinesAsync(fixture.Paths));
        Assert.Equal(KeptWithoutPosition, (string?)line["@mt"]);
        Assert.Equal(nameof(InvalidOperationException), (string?)line["ExceptionType"]);
        Assert.Equal(7, monitor.CurrentValue.TickSeconds);
        await AssertStillAnswersAsync(fixture);

        var error = await fixture.PostErrorAsync(Operations.PluginReload, null, HttpStatusCode.Conflict, Ct);
        Assert.Equal("plugin_reload_rejected", error.Code);
        var detail = Assert.Single(error.Details!);
        Assert.Equal("settings_file_unreadable", detail.Code);
        Assert.DoesNotContain(sentinel, detail.Message, StringComparison.Ordinal);
        Assert.Equal(7, monitor.CurrentValue.TickSeconds);
        Assert.DoesNotContain(sentinel, RuntimeLogs(fixture.Paths), StringComparison.Ordinal);
        await AssertStillAnswersAsync(fixture);
    }

    /// <summary>
    /// The same unreadable edit met by a plugin reload sent straight after it, with the file's watch stopped so that the
    /// reload's own read is the only one. A reload is an operator asking to make the file true now, and a file that does
    /// not parse says nothing it could be made true from: the reload is rejected, naming the file and where the parser
    /// stopped and never what was typed, the previous snapshot stays active with the grant it resolved, and nothing is
    /// written to the journal. The log says it once, by position, with the settings in force kept.
    /// </summary>
    [Fact]
    public async Task A_settings_file_that_is_not_valid_JSON_at_a_plugin_reload_rejects_it_by_position_and_keeps_the_grants_in_force()
    {
        const string sentinel = "SENTINEL_UNPARSED";
        using var programs = new TestPrograms();
        programs.AddProgram(FakeProviderCli.ExecutableName);
        await using var fixture = await PluginServiceTests.StartWithTheWatchStoppedAsync(new TestSearchPath { Path = programs.Root }, paths =>
        {
            TestPlugins.InstallFakeProvider(paths);
            TestPlugins.Grant(paths, TestPlugins.FakeProviderId, exec: ["*"]);
        });
        var started = await fixture.PostOkAsync<PluginRegistryDto>(Operations.PluginList, null, Ct);

        File.WriteAllText(fixture.Paths.UserSettingsFile, "{\"Plugins\":{\"Grants\":{},\n  " + sentinel);
        var error = await fixture.PostErrorAsync(Operations.PluginReload, null, HttpStatusCode.Conflict, Ct);

        Assert.Equal("plugin_reload_rejected", error.Code);
        Assert.False(error.Retryable);
        var detail = Assert.Single(error.Details!);
        Assert.Equal("settings.json", detail.Field);
        Assert.Equal("settings_file_unreadable", detail.Code);
        Assert.Contains("JsonException at line 2, byte 3", detail.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(sentinel, detail.Message, StringComparison.Ordinal);

        var listed = await fixture.PostOkAsync<PluginRegistryDto>(Operations.PluginList, null, Ct);
        Assert.Equal(started.Snapshot.Id, listed.Snapshot.Id);
        Assert.False(listed.Activated);
        Assert.Equal([FakeProviderCli.ExecutableName], Assert.Single(listed.Plugins).Capabilities.Exec!.Granted);
        Assert.Single((await PluginsReloadedAsync(fixture)).Items);

        var line = Assert.Single(SettingsFileLines(fixture.Paths));
        Assert.Equal("Warning", (string?)line["@l"]);
        Assert.Equal(KeptAtPosition, (string?)line["@mt"]);
        Assert.Equal("JsonException", (string?)line["ExceptionType"]);
        Assert.Equal(2, (int?)line["LineNumber"]);
        Assert.Equal(3, (int?)line["BytePositionInLine"]);
        Assert.DoesNotContain(sentinel, RuntimeLogs(fixture.Paths), StringComparison.Ordinal);
    }

    /// <summary>
    /// A file another handle holds for itself alone — an editor in the middle of saving it — cannot be read at all: the
    /// reload is rejected the same way, the failure named by its type, the previous snapshot left active and nothing
    /// journaled. Once the handle is let go, the next reload reads the file and activates.
    /// </summary>
    [Fact]
    public async Task A_settings_file_held_by_another_handle_at_a_plugin_reload_rejects_it_by_type_and_keeps_the_grants_in_force()
    {
        using var programs = new TestPrograms();
        programs.AddProgram(FakeProviderCli.ExecutableName);
        await using var fixture = await PluginServiceTests.StartWithTheWatchStoppedAsync(new TestSearchPath { Path = programs.Root }, paths =>
        {
            TestPlugins.InstallFakeProvider(paths);
            TestPlugins.Grant(paths, TestPlugins.FakeProviderId, exec: ["*"]);
        });
        var started = await fixture.PostOkAsync<PluginRegistryDto>(Operations.PluginList, null, Ct);

        ErrorBody error;
        using (new FileStream(fixture.Paths.UserSettingsFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            error = await fixture.PostErrorAsync(Operations.PluginReload, null, HttpStatusCode.Conflict, Ct);
        }

        Assert.Equal("plugin_reload_rejected", error.Code);
        var detail = Assert.Single(error.Details!);
        Assert.Equal("settings.json", detail.Field);
        Assert.Equal("settings_file_unreadable", detail.Code);
        Assert.Contains("(IOException)", detail.Message, StringComparison.Ordinal);
        var listed = await fixture.PostOkAsync<PluginRegistryDto>(Operations.PluginList, null, Ct);
        Assert.Equal(started.Snapshot.Id, listed.Snapshot.Id);
        Assert.Equal([FakeProviderCli.ExecutableName], Assert.Single(listed.Plugins).Capabilities.Exec!.Granted);
        Assert.Single((await PluginsReloadedAsync(fixture)).Items);

        var line = Assert.Single(SettingsFileLines(fixture.Paths));
        Assert.Equal(KeptWithoutPosition, (string?)line["@mt"]);
        Assert.Equal(nameof(IOException), (string?)line["ExceptionType"]);

        var reloaded = await fixture.PostOkAsync<PluginRegistryDto>(Operations.PluginReload, null, Ct);
        Assert.True(reloaded.Activated);
        Assert.Equal(2, (await PluginsReloadedAsync(fixture)).Items.Count);
    }

    /// <summary>
    /// A file that is not there when a reload reads it — a save that removes the file and then renames a new one into
    /// place, met in between — is one the reload cannot read, not one that puts every setting on its default: the grants
    /// and the global route in force are frozen again, and the log says the failure by its type. The watch is stopped, so
    /// nothing but the reload's own read meets the file.
    /// </summary>
    [Fact]
    public async Task A_settings_file_gone_at_a_plugin_reload_keeps_the_grants_and_the_global_route_in_force_and_is_said_by_type()
    {
        using var programs = new TestPrograms();
        programs.AddProgram(FakeProviderCli.ExecutableName);
        await using var fixture = await PluginServiceTests.StartWithTheWatchStoppedAsync(new TestSearchPath { Path = programs.Root }, paths =>
        {
            TestPlugins.InstallFakeProvider(paths);
            TestPlugins.InstallOtherProvider(paths);
            TestPlugins.Grant(paths, TestPlugins.FakeProviderId, exec: ["*"]);
            TestRoutes.WriteGlobal(paths, TestRoutes.GlobalDefault(TestPlugins.FakeProviderId, RouteActivationTests.Workspace()));
        });
        var started = await fixture.PostOkAsync<PluginRegistryDto>(Operations.PluginList, null, Ct);
        Assert.Equal([FakeProviderCli.ExecutableName], started.Plugins.Single(p => p.Id == TestPlugins.FakeProviderId).Capabilities.Exec!.Granted);
        Assert.Equal(TestPlugins.FakeProviderId, fixture.Resolve<RouteRegistry>().Snapshot.Global.Default?.PluginId);

        File.Delete(fixture.Paths.UserSettingsFile);
        var reloaded = await fixture.PostOkAsync<PluginRegistryDto>(Operations.PluginReload, null, Ct);

        Assert.True(reloaded.Activated);
        Assert.Equal([FakeProviderCli.ExecutableName], reloaded.Plugins.Single(p => p.Id == TestPlugins.FakeProviderId).Capabilities.Exec!.Granted);
        var routes = fixture.Resolve<RouteRegistry>().Snapshot;
        Assert.Equal(reloaded.RoutingSnapshotId, routes.Id);
        Assert.Equal(TestPlugins.FakeProviderId, routes.Global.Default?.PluginId);
        Assert.Equal(2, (await PluginsReloadedAsync(fixture)).Items.Count);

        var line = Assert.Single(SettingsFileLines(fixture.Paths));
        Assert.Equal("Warning", (string?)line["@l"]);
        Assert.Equal(KeptWithoutPosition, (string?)line["@mt"]);
        Assert.Equal(nameof(FileNotFoundException), (string?)line["ExceptionType"]);
    }

    /// <summary>
    /// A fresh installation has no settings file and never had one. There is nothing of the file's to keep, so a reload
    /// activates on the defaults — the plugin's program requested and not granted — and the log says nothing of the file.
    /// </summary>
    [Fact]
    public async Task A_plugin_reload_where_no_settings_file_was_ever_written_activates_on_the_defaults_and_says_nothing_of_the_file()
    {
        using var programs = new TestPrograms();
        programs.AddProgram(FakeProviderCli.ExecutableName);
        var search = new TestSearchPath { Path = programs.Root };
        await using var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths => TestPlugins.InstallFakeProvider(paths),
            configureServices: services => services.AddSingleton<ISearchPath>(search));
        Assert.False(File.Exists(fixture.Paths.UserSettingsFile), $"a settings file was written at '{fixture.Paths.UserSettingsFile}'");

        var reloaded = await fixture.PostOkAsync<PluginRegistryDto>(Operations.PluginReload, null, Ct);

        Assert.True(reloaded.Activated);
        var exec = Assert.Single(reloaded.Plugins).Capabilities.Exec!;
        Assert.Equal(FakeProviderCli.ExecutableName, Assert.Single(exec.Requested).Name);
        Assert.Empty(exec.Granted);
        Assert.Empty(SettingsFileLines(fixture.Paths));
    }

    /// <summary>
    /// An edit that leaves the file unreadable while the runtime is still starting — after its configuration was built,
    /// before any section was read into the options — is not one it can keep out. The failed reload has emptied the
    /// file's keys by the time the sections are first read, so they are read on their defaults; the warning used to say
    /// the runtime kept the settings it had all the same. It says what happens now, and what to do about it.
    /// </summary>
    [Fact]
    public async Task A_settings_file_that_becomes_unreadable_while_the_runtime_starts_is_said_to_leave_it_on_defaults()
    {
        using var dir = new TempDataDir();

        // Composing the host comes after the configuration is built and before any section is read into the options.
        var (runtime, line) = await StartEditedWhileStartingAsync(
            dir,
            """{"Dispatcher":{"Enabled":false,"TickSeconds":7}}""",
            "{\"Dispatcher\":{\"TickSeconds\":1,\n  unquoted",
            asTheHostStarts: false);
        await using (runtime)
        {
            Assert.Equal(StartingAtPosition, (string?)line["@mt"]);
            Assert.Equal(2, (int?)line["LineNumber"]);
            Assert.Equal(3, (int?)line["BytePositionInLine"]);
            Assert.Null(line["@x"]);

            // And that is what happened: the tick the file asked for is not the one in force, the shipped one is.
            var monitor = runtime.Services.GetRequiredService<IOptionsMonitor<DispatcherOptions>>();
            Assert.Equal(new DispatcherOptions().TickSeconds, monitor.CurrentValue.TickSeconds);
        }
    }

    /// <summary>
    /// The same while the host makes its own services. Every section has been validated by then, but the handler pool
    /// sizes itself from the dispatcher's settings through a cache of its own, which is first filled here; an edit that
    /// lands now leaves the pool on the shipped size. So it is still said as the start leaving settings on their
    /// defaults, and not as the runtime keeping what it had — which is what it was said as while that cache was taken
    /// for read.
    /// </summary>
    [Fact]
    public async Task A_settings_file_that_becomes_unreadable_as_the_host_makes_its_services_is_said_to_leave_it_on_defaults()
    {
        using var dir = new TempDataDir();

        var (runtime, line) = await StartEditedWhileStartingAsync(
            dir,
            """{"Dispatcher":{"Enabled":false,"MaxParallel":8,"TickSeconds":7}}""",
            "{\"Dispatcher\":{\"MaxParallel\":8,\n  unquoted",
            asTheHostStarts: true);
        await using (runtime)
        {
            Assert.Equal(StartingAtPosition, (string?)line["@mt"]);
            Assert.Null(line["@x"]);

            // And that is what runs: the pool is the shipped size, not the eight the file asked for.
            Assert.Equal(new DispatcherOptions().MaxParallel, runtime.Services.GetRequiredService<HandlerPool>().MaxParallel);
        }
    }

    /// <summary>
    /// And the same for a file the loader refuses by its content rather than its syntax, which has no position to say,
    /// at both moments. This is the edit whose message would carry what was typed — the loader names a key written
    /// twice — so it is what shows that the message is not what is said, and that no exception is written under the line.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_settings_file_refused_for_its_content_while_the_runtime_starts_is_said_to_leave_it_on_defaults(bool asTheHostStarts)
    {
        const string sentinel = "SENTINEL_W_KEY";
        using var dir = new TempDataDir();

        var (runtime, line) = await StartEditedWhileStartingAsync(
            dir,
            """{"Dispatcher":{"Enabled":false,"TickSeconds":7}}""",
            "{\"Dispatcher\":{\"" + sentinel + "\":1,\"" + sentinel + "\":2}}",
            asTheHostStarts);
        await using (runtime)
        {
            Assert.Equal(StartingWithoutPosition, (string?)line["@mt"]);
            Assert.Equal("FormatException", (string?)line["ExceptionType"]);
            Assert.Null(line["@x"]);
            Assert.DoesNotContain(sentinel, RuntimeLogs(dir.Paths), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A start whose settings file becomes unreadable while it is still starting: while the host is composed, or as the
    /// host makes the first of its own services. The start goes on once the runtime has said something of the edit, and
    /// the runtime and that one line are handed back.
    /// </summary>
    private static async Task<(RunningRuntime Runtime, JsonNode Line)> StartEditedWhileStartingAsync(
        TempDataDir dir,
        string settings,
        string edit,
        bool asTheHostStarts)
    {
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, settings);

        void Edit()
        {
            File.WriteAllText(dir.Paths.UserSettingsFile, edit);
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (SettingsFileLines(dir.Paths).Count == 0 && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(25);
            }
        }

        var options = TestRuntimeOptions.Quiet with
        {
            ConfigureServices = services =>
            {
                TestRuntimeOptions.TurnTheOutsideOff(services);
                if (asTheHostStarts)
                {
                    // First among the host's own services, so it is made before any service that reads a section once.
                    services.Insert(0, ServiceDescriptor.Singleton<IHostedService>(_ =>
                    {
                        Edit();
                        return new NothingToStart();
                    }));
                }
                else
                {
                    Edit();
                }
            },
        };

        var runtime = await RuntimeHost.StartAsync(dir.Paths, options, Ct);
        return (runtime, Assert.Single(SettingsFileLines(dir.Paths)));
    }

    /// <summary>A hosted service that is only there to be made.</summary>
    private sealed class NothingToStart : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>What a running runtime says of an edit it cannot read: it keeps the settings it had.</summary>
    private const string KeptAtPosition =
        "The settings file could not be read ({ExceptionType} at line {LineNumber}, byte {BytePositionInLine}); the runtime keeps the settings it had";

    /// <summary>The same, for a failure with no position to say.</summary>
    private const string KeptWithoutPosition =
        "The settings file could not be read ({ExceptionType}); the runtime keeps the settings it had";

    /// <summary>The same, for a failure with no position to say.</summary>
    private const string StartingWithoutPosition =
        "The settings file could not be read ({ExceptionType}) while the runtime was starting, "
        + "so any setting not yet in force starts on its default; restart it once the file can be read";

    /// <summary>What a starting runtime says of one: any setting not yet in force starts on its default.</summary>
    private const string StartingAtPosition =
        "The settings file could not be read ({ExceptionType} at line {LineNumber}, byte {BytePositionInLine}) while the runtime was starting, "
        + "so any setting not yet in force starts on its default; restart it once the file can be read";

    /// <summary>
    /// The loader refuses a file for more than its syntax, and its message for the rest repeats what was typed: a
    /// key written twice is refused by naming the key. So the type is what is said when there is no position to
    /// say, and nothing of the message.
    /// </summary>
    [Fact]
    public async Task A_settings_file_the_loader_refuses_for_its_content_is_reported_by_type_alone_and_never_by_its_message()
    {
        const string sentinel = "SENTINEL_B_KEY";
        await using var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths => File.WriteAllText(paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false,"TickSeconds":7}}"""));

        var monitor = fixture.Runtime.Services.GetRequiredService<IOptionsMonitor<DispatcherOptions>>();
        Assert.Equal(7, monitor.CurrentValue.TickSeconds);

        File.WriteAllText(fixture.Paths.UserSettingsFile, "{\"Dispatcher\":{\"" + sentinel + "\":1,\"" + sentinel + "\":2}}");

        var line = Assert.Single(await SettingsFileLinesAsync(fixture.Paths));
        Assert.DoesNotContain(sentinel, RuntimeLogs(fixture.Paths), StringComparison.Ordinal);
        Assert.Null(line["@x"]);
        Assert.Equal("Warning", (string?)line["@l"]);
        Assert.Equal("FormatException", (string?)line["ExceptionType"]);
        Assert.Null(line["LineNumber"]);
        Assert.Null(line["BytePositionInLine"]);
        Assert.Equal(7, monitor.CurrentValue.TickSeconds);
        await AssertStillAnswersAsync(fixture);
    }

    /// <summary>
    /// At start there is nothing to keep, so a file that cannot be read stops the start; and since this runs
    /// before there is a log, the sentence it stops with is all there is to read. It names the file and the
    /// position, and carries nothing underneath it that could hold the text.
    /// </summary>
    [Fact]
    public async Task A_settings_file_that_is_not_valid_JSON_stops_the_start_naming_the_file_and_the_position_and_never_the_content()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, "{\"Dispatcher\":{\"TickSeconds\":1,\n  unquoted");

        var failure = await Assert.ThrowsAsync<InvalidSettingsException>(() => RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet, Ct));

        Assert.Equal(
            $"Invalid Jason configuration: the settings file at '{dir.Paths.UserSettingsFile}' could not be read (JsonException at line 2, byte 3).",
            failure.Message);
        Assert.Null(failure.InnerException);
    }

    /// <summary>
    /// The same refusal at start for a file the loader refuses by its content: the type, and the file. The loader's
    /// message here names the key, so this is the start's twin that would show it.
    /// </summary>
    [Fact]
    public async Task A_settings_file_the_loader_refuses_for_its_content_stops_the_start_naming_the_file_and_the_type_alone()
    {
        const string sentinel = "SENTINEL_B_KEY";
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, "{\"Dispatcher\":{\"" + sentinel + "\":1,\"" + sentinel + "\":2}}");

        var failure = await Assert.ThrowsAsync<InvalidSettingsException>(() => RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet, Ct));

        Assert.DoesNotContain(sentinel, failure.ToString(), StringComparison.Ordinal);
        Assert.Equal(
            $"Invalid Jason configuration: the settings file at '{dir.Paths.UserSettingsFile}' could not be read (FormatException).",
            failure.Message);
        Assert.Null(failure.InnerException);
    }

    /// <summary>
    /// And it is said where whoever started the runtime will hear it. A runtime started in the background has put its
    /// own standard error on the null device before it reads its settings, and this refusal comes before there is a
    /// log, so a sentence that went only to standard error reached nobody: <c>jason runtime start</c> read an exit code
    /// and sent the person to log files that said nothing about this start.
    /// </summary>
    [Fact]
    public async Task A_settings_file_that_is_not_valid_JSON_is_said_through_the_refusal_writer_and_never_its_content()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, "{\"Dispatcher\":{\"TickSeconds\":1,\n  unquoted");

        var said = await RefusedStartAsync(dir.Paths);

        Assert.Equal(
            $"Invalid Jason configuration: the settings file at '{dir.Paths.UserSettingsFile}' could not be read (JsonException at line 2, byte 3).",
            said);
    }

    /// <summary>The same for a value of the wrong type in a section the host is built from: the setting's path and its type.</summary>
    [Fact]
    public async Task A_value_of_the_wrong_type_the_host_is_built_from_is_said_through_the_refusal_writer_and_never_the_value()
    {
        const string sentinel = "SENTINEL_T_MISTYPED";
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, "{\"Dispatcher\":{\"TickSeconds\":\"" + sentinel + "\"}}");

        var said = await RefusedStartAsync(dir.Paths);

        Assert.Equal("Invalid Jason configuration: Dispatcher:TickSeconds could not be read as System.Int32.", said);
    }

    /// <summary>
    /// And so is a refusal from a section the host validates as it starts, long after the log began. Those were left to
    /// the log alone, and a person who started the runtime in the background was told only to go and read it.
    /// </summary>
    [Fact]
    public async Task A_value_of_the_wrong_type_in_a_section_checked_as_the_host_starts_is_said_through_the_refusal_writer()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false},"Plugins":{"Limits":{"TimeoutMs":"SENTINEL_L_LATE"}}}""");

        var said = await RefusedStartAsync(dir.Paths);

        Assert.Equal("Invalid Jason configuration: Plugins:Limits:TimeoutMs could not be read as System.Int32.", said);
    }

    /// <summary>
    /// A settings refusal can come after every section has been validated, too: from a service the host starts that
    /// reads settings of its own. The host refuses to start for it, and it is said the same way.
    /// </summary>
    [Fact]
    public async Task A_settings_refusal_raised_as_the_host_starts_its_services_is_said_through_the_refusal_writer()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
        var options = TestRuntimeOptions.Quiet with
        {
            ConfigureServices = services =>
            {
                TestRuntimeOptions.TurnTheOutsideOff(services);
                services.AddOptions<LateSettings>().Validate(_ => false, "Late:Setting must be something else.");
                services.AddHostedService<ReadsLateSettings>();
            },
        };

        var said = await RefusedStartAsync(dir.Paths, options);

        Assert.Equal("Invalid Jason configuration: Late:Setting must be something else.", said);
    }

    /// <summary>Settings no section declares, so nothing validates them until a service reads them.</summary>
    private sealed class LateSettings
    {
    }

    /// <summary>A service that reads its settings as it starts, and only then.</summary>
    private sealed class ReadsLateSettings(IOptions<LateSettings> settings) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _ = settings.Value;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>
    /// Several sections refused at once: the options system raises them together, and each is said, in one refusal.
    /// </summary>
    [Fact]
    public async Task Refusals_from_several_sections_at_start_are_said_together()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(
            dir.Paths.UserSettingsFile,
            """{"Dispatcher":{"Enabled":false},"Plugins":{"Limits":{"TimeoutMs":"SENTINEL_L_LATE"}},"Notifications":{"QuietSeconds":0}}""");

        var said = await RefusedStartAsync(dir.Paths);

        Assert.StartsWith("Invalid Jason configuration: ", said, StringComparison.Ordinal);
        Assert.Contains("Notifications:QuietSeconds must be", said, StringComparison.Ordinal);

        // Once, though the dispatcher's settings read the plugins' through their seam and refuse with the same refusal.
        Assert.Equal(1, Occurrences(said, "Plugins:Limits:TimeoutMs could not be read as System.Int32."));
    }

    /// <summary>
    /// The dispatcher's settings read the plugins' to hold one rule across the two; a start that cannot read the plugins'
    /// still says what is wrong with the dispatcher's own, beside the plugins' own refusal, instead of one hiding the
    /// other until the first is fixed.
    /// </summary>
    [Fact]
    public async Task A_start_refused_for_the_plugins_settings_still_says_the_dispatchers_own_refusal()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(
            dir.Paths.UserSettingsFile,
            """{"Dispatcher":{"Enabled":false,"TickSeconds":0},"Plugins":{"Limits":{"TimeoutMs":"SENTINEL_L_LATE"}}}""");

        var said = await RefusedStartAsync(dir.Paths);

        Assert.Contains("Plugins:Limits:TimeoutMs could not be read as System.Int32.", said, StringComparison.Ordinal);
        Assert.Contains("Dispatcher:TickSeconds must be", said, StringComparison.Ordinal);
    }

    /// <summary>
    /// Three settings refused in the same words — grants for three plugins none of which is installed, so each key is
    /// written as a placeholder — are three refusals, and each is said: only the copy of a section's refusal that a
    /// seam adds is left out.
    /// </summary>
    [Fact]
    public async Task Settings_refused_in_the_same_words_are_each_said()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(
            dir.Paths.UserSettingsFile,
            """{"Dispatcher":{"Enabled":false},"Plugins":{"Grants":{"first-one":{"Exec":[""]},"second-one":{"Exec":[""]},"third-one":{"Exec":[""]}}}}""");

        var refused = await StartRefusedAsync(dir.Paths);

        Assert.Equal(3, Occurrences(refused.Message, "Plugins:Grants:<key>:Exec[0] must be"));
    }

    private static int Occurrences(string text, string needle) => text.Split(needle).Length - 1;

    /// <summary>
    /// A list in a shape the binder passes over stops the start the same way, by the setting's path and the type it has
    /// to be: an object among the words of a command, and the command written as one piece of text.
    /// </summary>
    [Theory]
    [InlineData("""{"Dispatcher":{"Enabled":false},"Roles":{"DefaultEntryCommand":["agent-host",{"x":"SENTINEL_S_LIST"},"-p"]}}""", "Roles:DefaultEntryCommand:1 could not be read as System.String.")]
    [InlineData("""{"Dispatcher":{"Enabled":false},"Roles":{"DefaultEntryCommand":"SENTINEL_S_LIST -p"}}""", "Roles:DefaultEntryCommand could not be read as a list of System.String.")]
    public async Task A_list_the_binder_would_pass_over_stops_the_start_by_its_path_and_type(string settings, string failure)
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, settings);

        var refused = await StartRefusedAsync(dir.Paths);

        Assert.Equal("Invalid Jason configuration: " + failure, refused.Message);
        Assert.Null(refused.InnerException);
    }

    /// <summary>
    /// A number or a switch written as a list stops the start too, whether its section is read before the host exists
    /// or as it starts: the setting's path and its type, and nothing that was typed.
    /// </summary>
    [Theory]
    [InlineData("""{"Dispatcher":{"Enabled":false,"TickSeconds":["SENTINEL_S_SCALAR"]}}""", "Dispatcher:TickSeconds could not be read as System.Int32.")]
    [InlineData("""{"Dispatcher":{"Enabled":false},"Update":{"CheckEnabled":["SENTINEL_S_SCALAR"]}}""", "Update:CheckEnabled could not be read as System.Boolean.")]
    public async Task A_setting_read_from_text_and_written_as_a_list_stops_the_start_by_its_path_and_type(string settings, string failure)
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, settings);

        var refused = await StartRefusedAsync(dir.Paths);

        Assert.Equal("Invalid Jason configuration: " + failure, refused.Message);
    }

    /// <summary>
    /// The routes are read from their section by hand rather than bound, and text where the whole section, or its map of
    /// operations, belongs was passed over there too: the runtime started with no routes and said nothing. It is refused
    /// by the path and what has to be there, as in every bound section.
    /// </summary>
    [Theory]
    [InlineData("""{"Dispatcher":{"Enabled":false},"Routes":{"Operations":"SENTINEL_S_ROUTES"}}""", "Routes:Operations could not be read as a map of Jason.Runtime.Configuration.RouteEntry.")]
    [InlineData("""{"Dispatcher":{"Enabled":false},"Routes":"SENTINEL_S_ROUTES"}""", "Routes could not be read as Jason.Runtime.Configuration.RoutesOptions.")]
    public async Task Text_where_the_routes_or_their_operations_belong_stops_the_start_by_path_and_type(string settings, string failure)
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, settings);

        var refused = await StartRefusedAsync(dir.Paths);

        Assert.Equal("Invalid Jason configuration: " + failure, refused.Message);
    }

    /// <summary>A plugin's configuration is flat: a key with children stops a start by the path, and the words stay out.</summary>
    [Theory]
    [InlineData("""{"Dispatcher":{"Enabled":false},"Plugins":{"Config":{"some-plugin":{"url":{"host":"SENTINEL_C_NESTED"}}}}}""", "Plugins:Config:<key>:<key> could not be read as System.String.")]
    [InlineData("""{"Dispatcher":{"Enabled":false},"Plugins":{"Config":{"some-plugin":["SENTINEL_C_LIST"]}}}""", "Plugins:Config:<key>:<key> must be a name: a plugin's configuration is a flat map from a name to text, and a list is not one.")]
    public async Task Structure_in_a_plugins_configuration_stops_the_start_by_path(string settings, string failure)
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, settings);

        var refused = await StartRefusedAsync(dir.Paths);

        Assert.Equal("Invalid Jason configuration: " + failure, refused.Message);
    }

    /// <summary>
    /// The same structure typed into the file of a running runtime: it is refused, the configuration read before it is
    /// what the runtime keeps, and the words typed there reach no log line.
    /// </summary>
    [Fact]
    public async Task Structure_typed_into_a_plugins_configuration_live_keeps_the_last_good_settings()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths => File.WriteAllText(paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false},"Plugins":{"Config":{"some-plugin":{"url":"https://receiver.example.test/first"}}}}"""));
        var settings = fixture.Resolve<LiveSettings<PluginsOptions>>();

        File.WriteAllText(fixture.Paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false},"Plugins":{"Config":{"some-plugin":{"url":{"host":"SENTINEL_C_LIVE"}}}}}""");
        Assert.True(await TestOptions.RefusedAsync(settings, Ct));

        Assert.True(settings.TryCurrent(out var kept));
        Assert.Equal("https://receiver.example.test/first", kept.Config["some-plugin"]["url"]);

        await fixture.Runtime.StopAsync();
        var logs = string.Concat(Directory.GetFiles(fixture.Paths.LogsDirectory).Select(File.ReadAllText));
        Assert.Contains("Plugins:Config:<key>:<key>", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL_C_LIVE", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("receiver.example.test", logs, StringComparison.Ordinal);
    }

    /// <summary>
    /// A start that has to be refused, run so that a start that is not refused stops again at once rather than leaving a
    /// runtime behind it, and fails the test.
    /// </summary>
    private static async Task<InvalidSettingsException> StartRefusedAsync(JasonPaths paths)
    {
        try
        {
            var started = await RuntimeHost.StartAsync(paths, TestRuntimeOptions.Quiet, Ct);
            await started.DisposeAsync();
        }
        catch (InvalidSettingsException refused)
        {
            Assert.DoesNotContain("SENTINEL", refused.ToString(), StringComparison.Ordinal);
            return refused;
        }

        Assert.Fail("The runtime started with a settings file it should have refused.");
        return null!;
    }

    /// <summary>
    /// A start run the way the detached runtime runs it, with a refusal writer of its own: it must answer 1 rather than
    /// throw, and what it wrote there is handed back. No sentinel any test here writes may be in it.
    /// </summary>
    private static async Task<string> RefusedStartAsync(JasonPaths paths, RuntimeHostOptions? options = null)
    {
        using var refusals = new StringWriter();
        var exit = 0;

        var escaped = await Record.ExceptionAsync(async () => exit = await RuntimeHost.RunAsync(paths, (options ?? TestRuntimeOptions.Quiet) with { Refusals = refusals }, Ct));

        Assert.Null(escaped);
        Assert.Equal(1, exit);
        var said = refusals.ToString().Trim();
        Assert.DoesNotContain("SENTINEL", said, StringComparison.Ordinal);
        return said;
    }

    /// <summary>A refused edit is a fact about a file, not about the process: the runtime still takes a call.</summary>
    private static async Task AssertStillAnswersAsync(RuntimeApiFixture fixture)
    {
        var (status, body) = await fixture.PostAsync(Operations.SystemInfo, null, Ct);
        Assert.True(status == HttpStatusCode.OK, $"{Operations.SystemInfo} answered {(int)status} after the edit: {body}");
    }

    /// <summary>Everything the running runtime has logged so far; it holds its log files open, so they are read shared.</summary>
    private static string RuntimeLogs(JasonPaths paths) =>
        string.Concat(Directory.GetFiles(paths.LogsDirectory).Select(FakeHostRuntime.ReadShared));

    /// <summary>
    /// The runtime's own lines about the settings file, as the JSON the log holds them in, once the first has
    /// come — and once the edit's own reload and any second one the same write could raise are past, so that a
    /// test asking for one line is told of a second.
    /// </summary>
    private static async Task<List<JsonNode>> SettingsFileLinesAsync(JasonPaths paths)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (SettingsFileLines(paths).Count == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25, Ct);
        }

        await Task.Delay(750, Ct);
        return SettingsFileLines(paths);
    }

    /// <summary>The journal's <c>plugins_reloaded</c> lines: one for each load that activated, the start's included.</summary>
    private static Task<Page<JournalEntryDto>> PluginsReloadedAsync(RuntimeApiFixture fixture) =>
        fixture.PostOkAsync<Page<JournalEntryDto>>(Operations.JournalList, new { Kind = JournalKinds.PluginsReloaded }, Ct);

    private static List<JsonNode> SettingsFileLines(JasonPaths paths) =>
        RuntimeLogs(paths)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Contains("The settings file could not be read", StringComparison.Ordinal))
            .Select(line => JsonNode.Parse(line)!)
            .ToList();
}
