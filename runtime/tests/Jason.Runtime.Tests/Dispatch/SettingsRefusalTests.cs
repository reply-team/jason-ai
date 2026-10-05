using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Notifications;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// What a broken settings file costs. The operator's path to a tick or a budget is an editor, and an editor is
/// where a typo happens; every part of the runtime that reads those settings has to answer the same way when it
/// meets one, or a mistyped number turns into a runtime that cannot be told what its own children did.
/// </summary>
public class SettingsRefusalTests
{
    /// <summary>A tick short enough that several of them pass inside a test, and a drain short enough to stop.</summary>
    private const string FastTick = """{"Dispatcher":{"TickSeconds":1,"DrainSeconds":1}}""";

    private const string Launchable = """
        {"Dispatcher":{"TickSeconds":1,"DrainSeconds":1,"RetryDelaySeconds":0},"Roles":{"DefaultEntryCommand":["agent-host"]}}
        """;

    /// <summary>A tick of zero: the validator's own sentence names the field, so the refusal is the file's.</summary>
    private const string Invalid = """{"Dispatcher":{"TickSeconds":0,"DrainSeconds":1}}""";

    /// <summary>The same mistyped tick in a file that still says how a role is launched: one edit, one mistake.</summary>
    private const string InvalidLaunchable = """
        {"Dispatcher":{"TickSeconds":0,"DrainSeconds":1},"Roles":{"DefaultEntryCommand":["agent-host"]}}
        """;

    /// <summary>
    /// A plugin memory limit below the floor the validator holds. It breaks a different section of the same
    /// file: the dispatcher's own settings are untouched, so a runtime that answered every request from the
    /// dispatcher's seam alone would still be broken by this one.
    /// </summary>
    private const string InvalidPlugins = """{"Dispatcher":{"Enabled":false},"Plugins":{"Limits":{"MemoryMb":0}}}""";

    /// <summary>Text where the dispatcher's switch wants true or false, standing in for anything a person might type.</summary>
    private const string WrongTypeSentinel = "SENTINEL_D_YES";

    /// <summary>
    /// The edit with a value of the wrong type in it. It leaves the drain unwritten, so a section read again from
    /// this file would carry the shipped ten seconds where the last settings that validated said one.
    /// </summary>
    private const string WrongType = """{"Dispatcher":{"TickSeconds":1,"Enabled":"SENTINEL_D_YES"}}""";

    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// While the settings file is invalid, the operations a running child depends on must still answer. A child
    /// that has finished cannot report it through a 500: it has no way to tell "the runtime is broken" from "my
    /// answer was rejected", so it retries into the same wall until its lease runs out and the item is called
    /// ambiguous — the one ending this runtime is built never to invent.
    /// </summary>
    [Fact]
    public async Task A_refused_settings_edit_does_not_turn_a_heartbeat_into_a_server_error()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(Ct, prepare: Settings(FastTick));
        var settings = fixture.Resolve<LiveSettings<DispatcherOptions>>();
        Assert.True(await DispatchHarness.FirstScanDoneAsync(fixture.Resolve<DispatcherStatus>(), Ct));
        var seeded = SeedRunningAttempt(fixture.Paths);

        File.WriteAllText(fixture.Paths.UserSettingsFile, Invalid);
        Assert.True(await DispatchHarness.EventuallyAsync(() => settings.Refusals > 0, Ct));

        var beat = await fixture.PostOkAsync<HeartbeatResponse>(
            Operations.WorkItemHeartbeat, new WorkItemHeartbeatRequest(seeded.ItemId, seeded.AttemptId), Ct);

        Assert.Equal(seeded.AttemptId, beat.AttemptId);
        Assert.True(beat.LockUntil > DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// The same for the answer that matters most. Only the failure branch of a completion reads the settings —
    /// how many attempts this kind of work gets, and how long to wait before the next one — so a successful
    /// completion would prove nothing here.
    /// </summary>
    [Fact]
    public async Task A_refused_settings_edit_does_not_turn_a_failed_completion_into_a_server_error()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(Ct, prepare: Settings(FastTick));
        var settings = fixture.Resolve<LiveSettings<DispatcherOptions>>();
        Assert.True(await DispatchHarness.FirstScanDoneAsync(fixture.Resolve<DispatcherStatus>(), Ct));
        var seeded = SeedRunningAttempt(fixture.Paths);

        File.WriteAllText(fixture.Paths.UserSettingsFile, Invalid);
        Assert.True(await DispatchHarness.EventuallyAsync(() => settings.Refusals > 0, Ct));

        var dto = await fixture.PostOkAsync<WorkItemDto>(
            Operations.WorkItemComplete,
            new WorkItemCompleteRequest(
                seeded.ItemId,
                seeded.AttemptId,
                CompletionStatus.Failed,
                Result: null,
                new CompletionErrorDto("provider_unavailable", "The provider was not there.", Details: null),
                Reason: null),
            Ct);

        // The last settings that validated say this kind gets three attempts, so the item is released for another.
        Assert.Equal(WorkItemStatus.Created, dto.Status);
    }

    /// <summary>
    /// The complaint is about the edit, not about the reading of it. Every part of a scan now reads the same
    /// seam, so a single broken file is said once however many readers meet it — and the scan itself no longer
    /// falls over, which is the line this test counts to zero.
    /// </summary>
    [Fact]
    public async Task A_refused_settings_edit_is_said_once_and_not_once_a_tick()
    {
        var fixture = await RuntimeApiFixture.StartAsync(Ct, prepare: Settings(FastTick));
        await using (fixture)
        {
            var settings = fixture.Resolve<LiveSettings<DispatcherOptions>>();
            Assert.True(await DispatchHarness.FirstScanDoneAsync(fixture.Resolve<DispatcherStatus>(), Ct));

            File.WriteAllText(fixture.Paths.UserSettingsFile, Invalid);

            // Several ticks' worth of reads, so "said once" is a claim about the edit and not about one read.
            Assert.True(await DispatchHarness.EventuallyAsync(() => settings.Refusals >= 5, Ct, 20000));
            await fixture.Runtime.StopAsync();

            var logs = ReadLogs(fixture.Paths);
            Assert.Equal(1, Occurrences(logs, "settings were refused"));

            // The refusal is logged at Error, so this count pins the reader against a line that is really there.
            // Without it the zero below would pass on a reader that matched nothing, before the fix and after.
            Assert.Equal(1, ErrorLines(logs, "settings were refused"));
            Assert.Contains("\"Section\":\"Dispatcher\"", logs, StringComparison.Ordinal);
            Assert.Equal(0, ErrorLines(logs, "Dispatcher scan failed"));
        }
    }

    /// <summary>
    /// The same edit with a value of the wrong type in it. The binder refuses text where a switch belongs before
    /// any validator sees the section, and its refusal used to escape every reader that was ready for the
    /// validator's: the loop's own wait threw, the host stopped the runtime with it, and the text — whatever was
    /// typed, a token pasted onto the wrong line included — went into the log with every exception that carried
    /// it. It is a refusal like any other now: said once, by the setting's path and the type it has to be, with
    /// the last settings that validated still in force and the runtime still answering.
    /// </summary>
    [Fact]
    public async Task A_setting_of_the_wrong_type_is_refused_like_any_other_and_stops_nothing()
    {
        var fixture = await RuntimeApiFixture.StartAsync(Ct, prepare: Settings(FastTick));
        await using (fixture)
        {
            var settings = fixture.Resolve<LiveSettings<DispatcherOptions>>();
            var status = fixture.Resolve<DispatcherStatus>();
            var lifetime = fixture.Resolve<IHostApplicationLifetime>();
            Assert.True(await DispatchHarness.FirstScanDoneAsync(status, Ct));

            File.WriteAllText(fixture.Paths.UserSettingsFile, WrongType);

            // Whichever comes first: the seam refusing the edit, or the runtime stopping because nothing caught it.
            Assert.True(await DispatchHarness.EventuallyAsync(
                () => settings.Refusals > 0 || lifetime.ApplicationStopping.IsCancellationRequested, Ct, 10000));
            Assert.False(lifetime.ApplicationStopping.IsCancellationRequested, "The runtime stopped over a value of the wrong type in its settings file.");

            // Three ticks on, the loop is still scanning and the runtime still answers.
            var scans = status.Scans;
            Assert.True(await DispatchHarness.EventuallyAsync(() => status.Scans >= scans + 3, Ct, 20000));
            Assert.Equal(DispatcherState.Running, status.State);
            await fixture.PostOkAsync<SystemInfoResponse>(Operations.SystemInfo, null, Ct);

            var current = settings.Current;
            Assert.True(current.Enabled);
            Assert.Equal(1, current.TickSeconds);
            Assert.Equal(1, current.DrainSeconds);
            Assert.False(lifetime.ApplicationStopping.IsCancellationRequested);

            await fixture.Runtime.StopAsync();
            var logs = ReadLogs(fixture.Paths);
            Assert.Equal(1, ErrorLines(logs, "settings were refused"));
            Assert.Equal(1, ErrorLines(logs, "Dispatcher:Enabled could not be read as System.Boolean."));

            // Nowhere: not in a message, not in a property, not in an exception written beside one.
            Assert.DoesNotContain(WrongTypeSentinel, logs, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Three sections are guarded now, and an operator told only that "settings were refused" would have the
    /// whole file to search. The complaint names the section it came from and the setting inside it, and it is
    /// still said once however many reads meet the same edit.
    /// </summary>
    [Fact]
    public async Task A_refused_edit_names_the_section_it_came_from_and_is_said_once()
    {
        var fixture = await RuntimeApiFixture.StartAsync(Ct, prepare: Settings(RuntimeApiFixture.DispatcherOff));
        await using (fixture)
        {
            var plugins = fixture.Resolve<LiveSettings<PluginsOptions>>();

            File.WriteAllText(fixture.Paths.UserSettingsFile, InvalidPlugins);
            Assert.True(await TestOptions.RefusedAsync(plugins, Ct));

            // Four more reads of the same broken file: "said once" is a claim about the edit, not about one read.
            for (var read = 0; read < 4; read++)
            {
                plugins.TryCurrent(out _);
            }

            Assert.Equal(5, plugins.Refusals);
            await fixture.Runtime.StopAsync();

            var logs = ReadLogs(fixture.Paths);
            Assert.Equal(1, ErrorLines(logs, "settings were refused"));
            Assert.Contains("\"Section\":\"Plugins\"", logs, StringComparison.Ordinal);
            Assert.Equal(1, ErrorLines(logs, "Plugins:Limits:MemoryMb"));
        }
    }

    /// <summary>
    /// A map's entry of the wrong type: a notification channel whose switch is written as text. The binder leaves such
    /// an entry out rather than refusing it, so the edit used to take the channel away with nothing said. It is refused
    /// like any other value of the wrong type: the last settings that validated stay in force, and the log says which
    /// setting and what type it has to be, never what was typed.
    /// </summary>
    [Fact]
    public async Task A_map_entry_of_the_wrong_type_is_refused_and_the_last_good_settings_are_kept()
    {
        const string sentinel = "SENTINEL_D_MAP";
        var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: Settings("""{"Dispatcher":{"Enabled":false},"Notifications":{"Plugins":{"fake-notifier":{"Enabled":true}}}}"""));
        await using (fixture)
        {
            var notifications = fixture.Resolve<LiveSettings<NotificationsOptions>>();
            Assert.True(notifications.Current.Plugins["fake-notifier"].Enabled);

            File.WriteAllText(
                fixture.Paths.UserSettingsFile,
                """{"Dispatcher":{"Enabled":false},"Notifications":{"Plugins":{"fake-notifier":{"Enabled":"SENTINEL_D_MAP"}}}}""");
            Assert.True(await TestOptions.RefusedAsync(notifications, Ct));

            Assert.True(notifications.Current.Plugins["fake-notifier"].Enabled);
            await fixture.Runtime.StopAsync();

            var logs = ReadLogs(fixture.Paths);
            var refused = logs.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => JsonNode.Parse(line)!)
                .Where(line => (string?)line["@l"] == "Error" && (string?)line["Section"] == NotificationsOptions.Section)
                .Select(line => (string?)line["Failure"])
                .ToList();
            Assert.Equal(["Notifications:Plugins:<key>:Enabled could not be read as System.Boolean."], refused);
            Assert.DoesNotContain(sentinel, logs, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A number edited into an object or a list. The binder kept the tick's default for it and said nothing, so a tick
    /// of one second went to ten. It is refused: the last settings that validated stay in force, and the log names the
    /// setting and its type, never what was typed.
    /// </summary>
    [Theory]
    [InlineData("""["SENTINEL_D_SCALAR"]""")]
    [InlineData("""{"x":"SENTINEL_D_SCALAR"}""")]
    public async Task A_number_written_as_an_object_or_a_list_is_refused_and_the_last_good_settings_are_kept(string tick)
    {
        var fixture = await RuntimeApiFixture.StartAsync(Ct, prepare: Settings(FastTick));
        await using (fixture)
        {
            var settings = fixture.Resolve<LiveSettings<DispatcherOptions>>();
            Assert.Equal(1, settings.Current.TickSeconds);

            File.WriteAllText(fixture.Paths.UserSettingsFile, """{"Dispatcher":{"DrainSeconds":1,"TickSeconds":""" + tick + "}}");
            Assert.True(await TestOptions.RefusedAsync(settings, Ct));

            Assert.Equal(1, settings.Current.TickSeconds);
            await fixture.Runtime.StopAsync();

            var logs = ReadLogs(fixture.Paths);
            var refused = logs.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => JsonNode.Parse(line)!)
                .Where(line => (string?)line["@l"] == "Error" && (string?)line["Section"] == DispatcherOptions.Section)
                .Select(line => (string?)line["Failure"])
                .ToList();
            Assert.Equal(["Dispatcher:TickSeconds could not be read as System.Int32."], refused);
            Assert.DoesNotContain("SENTINEL", logs, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A list edited into a shape the binder passes over — an object among the words of the command a role is launched
    /// with, or the whole command written as one piece of text — used to change the command without a word: a word
    /// gone, or the command gone. It is refused like any other value of the wrong type: the last settings that validated
    /// stay in force, and the log names the setting and the type it has to be, never what was typed.
    /// </summary>
    [Theory]
    [InlineData("""["agent-host",{"x":"SENTINEL_D_LIST"},"-p"]""", "Roles:DefaultEntryCommand:1 could not be read as System.String.")]
    [InlineData("""      "SENTINEL_D_LIST -p"      """, "Roles:DefaultEntryCommand could not be read as a list of System.String.")]
    public async Task A_list_the_binder_would_pass_over_is_refused_and_the_last_good_settings_are_kept(string command, string failure)
    {
        var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: Settings("""{"Dispatcher":{"Enabled":false},"Roles":{"DefaultEntryCommand":["agent-host"]}}"""));
        await using (fixture)
        {
            var roles = fixture.Resolve<LiveSettings<RolesOptions>>();
            Assert.Equal(["agent-host"], roles.Current.DefaultEntryCommand);

            File.WriteAllText(fixture.Paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false},"Roles":{"DefaultEntryCommand":""" + command + "}}");
            Assert.True(await TestOptions.RefusedAsync(roles, Ct));

            Assert.Equal(["agent-host"], roles.Current.DefaultEntryCommand);
            await fixture.Runtime.StopAsync();

            var logs = ReadLogs(fixture.Paths);
            var refused = logs.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => JsonNode.Parse(line)!)
                .Where(line => (string?)line["@l"] == "Error" && (string?)line["Section"] == RolesOptions.Section)
                .Select(line => (string?)line["Failure"])
                .ToList();
            Assert.Equal([failure], refused);
            Assert.DoesNotContain("SENTINEL", logs, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Every seam holds the settings the start validated before any edit can reach it. A seam keeps the last good
    /// settings only once it has read some, and with no dispatch loop nothing read the roles' or the manager's at start:
    /// a refused edit that reached one first found nothing to keep. Each section is edited into a value of the wrong
    /// type before anything of the test reads it through its seam, and each seam still answers with what it had.
    /// </summary>
    [Theory]
    [InlineData("Dispatcher", """{"Dispatcher":{"Enabled":false,"TickSeconds":"SENTINEL_D_SEAM"}}""")]
    [InlineData("Roles", """{"Dispatcher":{"Enabled":false},"Roles":{"MaxSkillBytes":"SENTINEL_D_SEAM"}}""")]
    [InlineData("Manager", """{"Dispatcher":{"Enabled":false},"Manager":{"ReviewSeconds":"SENTINEL_D_SEAM"}}""")]
    [InlineData("Plugins", """{"Dispatcher":{"Enabled":false},"Plugins":{"Limits":{"TimeoutMs":"SENTINEL_D_SEAM"}}}""")]
    [InlineData("Notifications", """{"Dispatcher":{"Enabled":false},"Notifications":{"QuietSeconds":"SENTINEL_D_SEAM"}}""")]
    [InlineData("Update", """{"Dispatcher":{"Enabled":false},"Update":{"IntervalHours":"SENTINEL_D_SEAM"}}""")]
    public async Task Every_seam_holds_the_settings_the_start_validated_before_an_edit_reaches_it(string section, string edit)
    {
        var fixture = await RuntimeApiFixture.StartAsync(Ct, prepare: Settings(RuntimeApiFixture.DispatcherOff));
        await using (fixture)
        {
            var keeps = section switch
            {
                "Dispatcher" => KeepsAfterTheEdit<DispatcherOptions>(fixture),
                "Roles" => KeepsAfterTheEdit<RolesOptions>(fixture),
                "Manager" => KeepsAfterTheEdit<ManagerOptions>(fixture),
                "Plugins" => KeepsAfterTheEdit<PluginsOptions>(fixture),
                "Notifications" => KeepsAfterTheEdit<NotificationsOptions>(fixture),
                _ => KeepsAfterTheEdit<UpdateOptions>(fixture),
            };

            File.WriteAllText(fixture.Paths.UserSettingsFile, edit);

            Assert.True(await keeps(), $"The {section} seam had nothing to keep when a refused edit reached it.");
        }
    }

    /// <summary>
    /// The roles' seam, asked through the runtime's own answer about itself: with no dispatch loop it is the first thing
    /// that reads the roles, and a refused edit that reached it first made <c>system.info</c> a server error — which a
    /// caller reads as a runtime that is not there. It answers, with the roles that validated.
    /// </summary>
    [Fact]
    public async Task A_refused_roles_edit_before_anything_read_the_roles_leaves_the_runtime_answering_with_what_it_had()
    {
        var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: Settings("""{"Dispatcher":{"Enabled":false},"Roles":{"MaxSkillBytes":8192}}"""));
        await using (fixture)
        {
            var monitor = fixture.Resolve<IOptionsMonitor<RolesOptions>>();
            File.WriteAllText(fixture.Paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false},"Roles":{"MaxSkillBytes":"SENTINEL_D_ROLES"}}""");
            Assert.True(await DispatchHarness.EventuallyAsync(() => Refuses(monitor), Ct, 10000), "The edit never reached the runtime.");

            var (status, body) = await fixture.PostAsync(Operations.SystemInfo, null, Ct);

            Assert.True(status == System.Net.HttpStatusCode.OK, $"{Operations.SystemInfo} answered {(int)status}: {body}");
            Assert.True(fixture.Resolve<LiveSettings<RolesOptions>>().TryCurrent(out var roles));
            Assert.Equal(8192, roles.MaxSkillBytes);

            await fixture.Runtime.StopAsync();
            var logs = ReadLogs(fixture.Paths);
            Assert.Equal(1, ErrorLines(logs, "Roles:MaxSkillBytes could not be read as System.Int32."));
            Assert.DoesNotContain("SENTINEL", logs, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Waits, after the edit the caller then writes, until the section's settings are refused, without reading them
    /// through the seam; then answers whether the seam still has settings to work from.
    /// </summary>
    private static Func<Task<bool>> KeepsAfterTheEdit<TOptions>(RuntimeApiFixture fixture)
        where TOptions : class
    {
        var monitor = fixture.Resolve<IOptionsMonitor<TOptions>>();
        var seam = fixture.Resolve<LiveSettings<TOptions>>();
        return async () =>
        {
            Assert.True(await DispatchHarness.EventuallyAsync(() => Refuses(monitor), Ct, 10000), "The edit never reached the runtime.");
            return seam.TryCurrent(out _);
        };
    }

    /// <summary>Whether the section's settings are refused now, asked of the options system rather than of a seam.</summary>
    private static bool Refuses<TOptions>(IOptionsMonitor<TOptions> monitor)
        where TOptions : class
    {
        try
        {
            _ = monitor.CurrentValue;
            return false;
        }
        catch (OptionsValidationException)
        {
            return true;
        }
    }

    /// <summary>
    /// A channel written as <c>null</c>, live. Configuration cannot tell <c>null</c> from an empty object, and the binder
    /// makes an entry for either with every setting of it at its default, so the edit names the channel the way
    /// <c>{}</c> does and the runtime goes on answering. No entry from the file reaches the validator as nothing; what
    /// the validator says of one is for an entry made some other way.
    /// </summary>
    [Fact]
    public async Task A_channel_written_as_null_stops_nothing()
    {
        var fixture = await RuntimeApiFixture.StartAsync(Ct, prepare: Settings(RuntimeApiFixture.DispatcherOff));
        await using (fixture)
        {
            var notifications = fixture.Resolve<LiveSettings<NotificationsOptions>>();
            var lifetime = fixture.Resolve<IHostApplicationLifetime>();
            Assert.Empty(notifications.Current.Plugins);

            File.WriteAllText(fixture.Paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false},"Notifications":{"Plugins":{"fake-notifier":null}}}""");

            // Whichever comes first: the edit in force, or the runtime stopping because something threw.
            Assert.True(await DispatchHarness.EventuallyAsync(
                () => (notifications.TryCurrent(out var current) && current.Plugins.ContainsKey("fake-notifier")) || lifetime.ApplicationStopping.IsCancellationRequested,
                Ct,
                10000));
            Assert.False(lifetime.ApplicationStopping.IsCancellationRequested, "The runtime stopped over a channel written as null.");
            Assert.Equal(0, notifications.Refusals);
            await fixture.PostOkAsync<SystemInfoResponse>(Operations.SystemInfo, null, Ct);
        }
    }

    /// <summary>
    /// And the work keeps moving meanwhile. One piece of work reads the settings four times over — the lease
    /// sweep, the claim, the handler that starts what was claimed, and the routine that ends it — so this is the
    /// test that says all of them fall back together, rather than one of them throwing the whole scan away.
    /// </summary>
    [Fact]
    public async Task Repairing_the_file_lets_the_work_through_again()
    {
        // A launch that fails is still a whole piece of work: claimed, started, and ended through the one
        // routine that decides whether an attempt is worth repeating — which is the reader this asserts on.
        var command = new FakeCommand(WorkItemKind.AiRole, _ => throw new InvalidOperationException("the host is not there"));
        var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: Settings(Launchable),
            configureServices: services => services.AddSingleton<ICommand>(command));
        await using (fixture)
        {
            var settings = fixture.Resolve<LiveSettings<DispatcherOptions>>();
            Assert.True(await DispatchHarness.FirstScanDoneAsync(fixture.Resolve<DispatcherStatus>(), Ct));

            File.WriteAllText(fixture.Paths.UserSettingsFile, InvalidLaunchable);
            Assert.True(await DispatchHarness.EventuallyAsync(() => settings.Refusals > 0, Ct));

            var item = SeedClaimable(fixture.Paths);
            Assert.True(await DispatchHarness.EventuallyAsync(
                () => StatusOf(fixture.Paths, item) == WorkItemStatus.Failed, Ct, 20000));

            // And the file is the authority again the moment it is put right.
            File.WriteAllText(fixture.Paths.UserSettingsFile, """{"Dispatcher":{"TickSeconds":2,"DrainSeconds":1}}""");
            Assert.True(await DispatchHarness.EventuallyAsync(() => settings.Current.TickSeconds == 2, Ct));
        }
    }

    /// <summary>
    /// A runtime that has never read settings the validator accepted has nothing to fall back to. A loop can
    /// live with that — it tries again next tick — but an endpoint has to answer, so the seam says plainly that
    /// it cannot, instead of throwing something the API surface has no word for.
    /// </summary>
    [Fact]
    public void A_runtime_that_never_read_a_good_value_answers_that_it_cannot_rather_than_throwing()
    {
        var settings = TestOptions.NothingValidated();

        Assert.False(settings.TryCurrent(out _));
        Assert.Equal(1, settings.Refusals);
    }

    [Fact]
    public async Task A_heartbeat_with_nothing_to_fall_back_to_is_refused_with_a_code_and_not_a_crash()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database);
        await using var ctx = database.Open();

        var error = await Assert.ThrowsAsync<DomainException>(
            () => NothingValidatedService(ctx).HeartbeatAsync(new WorkItemHeartbeatRequest(seeded.ItemId, seeded.AttemptId), Ct));

        Assert.Equal("settings_unreadable", error.Code);
        Assert.Equal(503, error.StatusCode);
        Assert.True(error.Retryable);
    }

    [Fact]
    public async Task A_failed_completion_with_nothing_to_fall_back_to_is_refused_with_a_code_and_not_a_crash()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database);
        await using var ctx = database.Open();
        var request = new WorkItemCompleteRequest(
            seeded.ItemId,
            seeded.AttemptId,
            CompletionStatus.Failed,
            Result: null,
            new CompletionErrorDto("provider_unavailable", "The provider was not there.", Details: null),
            Reason: null);

        var error = await Assert.ThrowsAsync<DomainException>(() => NothingValidatedService(ctx).CompleteAsync(request, Ct));

        Assert.Equal("settings_unreadable", error.Code);
        Assert.Equal(503, error.StatusCode);
        Assert.True(error.Retryable);
    }

    /// <summary>
    /// And a child that succeeded is never held up by a file it has nothing to do with: a successful completion
    /// reads no settings, so the same runtime that refuses the failure above takes this one.
    /// </summary>
    [Fact]
    public async Task A_successful_completion_needs_no_settings_and_is_taken_anyway()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database);
        await using var ctx = database.Open();
        var request = new WorkItemCompleteRequest(
            seeded.ItemId, seeded.AttemptId, CompletionStatus.Succeeded, Result: null, Error: null, Reason: null);

        var dto = await NothingValidatedService(ctx).CompleteAsync(request, Ct);

        Assert.Equal(WorkItemStatus.Succeeded, dto.Status);
    }

    /// <summary>
    /// The section an operator edits to raise a plugin's memory, or to give a child longer to stop, is read on
    /// the way into every work item — it bounds the lease a provider operation may be given. A typo there used
    /// to answer a creation with a 500, which tells a planner that the runtime broke rather than the file, and
    /// there is nothing an agent can do with that sentence. The work item is written from the last settings that
    /// validated, exactly as a tick is.
    /// </summary>
    [Fact]
    public async Task A_refused_plugins_edit_does_not_turn_a_work_item_into_a_server_error()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(Ct, prepare: Settings(RuntimeApiFixture.DispatcherOff));
        var plugins = fixture.Resolve<LiveSettings<PluginsOptions>>();
        var campaign = await fixture.PostOkAsync<CampaignDto>(
            Operations.CampaignCreate, new CampaignCreateRequest("outreach", null, null, null), Ct);

        File.WriteAllText(fixture.Paths.UserSettingsFile, InvalidPlugins);
        Assert.True(await TestOptions.RefusedAsync(plugins, Ct));

        var item = await fixture.PostOkAsync<WorkItemDto>(
            Operations.WorkItemCreate,
            new WorkItemCreateRequest(
                campaign.Id, WorkItemKind.AiRole, "researcher", null, null, null, null, null, null, null, null, null, null, null, null, null),
            Ct);

        Assert.Equal(WorkItemStatus.Created, item.Status);
    }

    private static ExecutorService NothingValidatedService(JasonDbContext db)
    {
        var clock = new FixedClock(Noon);
        var settings = TestOptions.NothingValidated();
        return new ExecutorService(db, clock, new AttemptOutcomes(new JournalWriter(clock), clock, settings), settings);
    }

    private static async Task<(string ItemId, string AttemptId)> SeedAsync(TestDatabase database)
    {
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var item = WorkItemFactory.NewAiRole(campaign, now: Noon, configure: w => w.Status = WorkItemStatus.Processing);
        var attempt = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Running, Noon);
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(item);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(Ct);
        return (item.PublicId, attempt.PublicId);
    }

    /// <summary>An item already being worked on, leased from now, so a running loop leaves it alone.</summary>
    private static (string ItemId, string AttemptId) SeedRunningAttempt(JasonPaths paths)
    {
        var now = DateTime.UtcNow;
        using var db = new JasonDbContext(JasonDbContext.CreateOptions(paths.DatabaseFile));
        var campaign = WorkItemFactory.NewCampaign(now: now);
        var item = WorkItemFactory.NewAiRole(campaign, now: now, configure: w => w.Status = WorkItemStatus.Processing);
        var attempt = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Running, now);
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(item);
        db.Attempts.Add(attempt);
        db.SaveChanges();
        return (item.PublicId, attempt.PublicId);
    }

    private static string SeedClaimable(JasonPaths paths)
    {
        var now = DateTime.UtcNow;
        using var db = new JasonDbContext(JasonDbContext.CreateOptions(paths.DatabaseFile));
        var campaign = WorkItemFactory.NewCampaign(now: now);
        var item = WorkItemFactory.NewAiRole(campaign, now: now);
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(item);
        db.SaveChanges();
        return item.PublicId;
    }

    private static WorkItemStatus StatusOf(JasonPaths paths, string publicId)
    {
        using var db = new JasonDbContext(JasonDbContext.CreateOptions(paths.DatabaseFile));
        return db.WorkItems.AsNoTracking().Where(w => w.PublicId == publicId).Select(w => w.Status).Single();
    }

    /// <summary>
    /// Error lines mentioning the needle. Serilog's compact format spells the level "@l" and leaves it out at
    /// Information, so a reader looking for "level" matches nothing and would make every count here vacuous.
    /// </summary>
    private static int ErrorLines(string logs, string needle) =>
        logs.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Count(line => line.Contains("\"@l\":\"Error\"", StringComparison.Ordinal)
                && line.Contains(needle, StringComparison.Ordinal));

    private static Action<JasonPaths> Settings(string json) => paths => File.WriteAllText(paths.UserSettingsFile, json);

    private static int Occurrences(string text, string needle)
    {
        var count = 0;
        for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static string ReadLogs(JasonPaths paths) =>
        string.Concat(Directory.GetFiles(paths.LogsDirectory).Select(File.ReadAllText));
}
