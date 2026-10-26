using Jason.Cli.Status;
using Jason.Contracts.Plugins;

namespace Jason.Cli.Tests.Status;

/// <summary>
/// jason status names every plugin this machine holds back, with the codes <c>plugin.list</c> gives it, so the cause of
/// provider work failing at the claim is in the answer rather than one listing away. Optional, so it never decides
/// readiness: an installation with no plugin at all, or no route, is ready while no provider work can run, and work
/// routed to an unavailable plugin fails at the claim, a notification plugin delivers nothing, and nothing else stops.
/// </summary>
public class PluginAvailabilityCheckTests
{
    private const string Name = "plugin_availability";

    /// <summary>What an unavailable plugin stops, and that nothing else stops: said once, in the words the fact ends with.</summary>
    private const string WhatStops = "Work routed to such a plugin fails at the claim, and a notification plugin delivers nothing; nothing else stops, so this check never decides readiness.";

    /// <summary>A package root as the fake's listing gives it, which the fact never quotes.</summary>
    private const string PackageRoot = "/home/ada/.jason";

    /// <summary>The path a problem names in its manifest, which the fact never quotes.</summary>
    private const string ProblemPath = "capabilities.exec.executables[0].name";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_unavailable_plugin_is_named_with_its_code_and_the_installation_stays_ready()
    {
        using var dir = new TempPaths();
        StatusCommandTests.Running(dir, registry: Registry(Plugin("reply", PluginStatus.Unavailable, "executable_missing")));
        var output = new StringWriter();

        var exit = await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), Ct);

        var report = StatusCommandTests.Read(output);
        var check = Assert.Single(report.Checks, c => c.Name == Name);
        Assert.Equal(CheckState.Failed, check.State);
        Assert.False(check.Required);
        Assert.StartsWith("reply is unavailable; its problems: executable_missing.", check.Fact, StringComparison.Ordinal);
        Assert.Contains("`jason plugin list` says what each problem is", check.Fact, StringComparison.Ordinal);
        Assert.Contains("`jason plugin reload`", check.Fact, StringComparison.Ordinal);
        Assert.Null(check.Fix);

        // Optional whatever it says: an unavailable plugin leaves the installation ready, and the exit code with it.
        Assert.True(report.Ready);
        Assert.Equal(ExitCodes.Success, exit);
    }

    [Fact]
    public async Task The_human_line_names_the_plugin_and_its_code()
    {
        using var dir = new TempPaths();
        StatusCommandTests.Running(dir, registry: Registry(Plugin("reply", PluginStatus.Unavailable, "executable_missing")));
        var output = new StringWriter();

        await CliApp.RunAsync(["status", "--human"], StatusCommandTests.Machine(dir, output), Ct);

        var line = Assert.Single(output.ToString().Split(Environment.NewLine), each => each.StartsWith(Name, StringComparison.Ordinal));
        Assert.Contains("optional", line, StringComparison.Ordinal);
        Assert.Contains("failed", line, StringComparison.Ordinal);
        Assert.Contains("reply is unavailable; its problems: executable_missing.", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_problems_on_one_plugin_are_both_named_and_a_code_twice_is_named_once()
    {
        var check = await CheckAsync(Registry(Plugin("reply", PluginStatus.Unavailable, "executable_missing", "executable_incompatible", "executable_missing")));

        Assert.Equal(CheckState.Failed, check.State);
        Assert.StartsWith("reply is unavailable; its problems: executable_missing, executable_incompatible.", check.Fact, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Each_unavailable_plugin_is_named_and_an_available_one_is_not()
    {
        var check = await CheckAsync(Registry(
            Plugin("acme-sdr", PluginStatus.Valid),
            Plugin("reply", PluginStatus.Unavailable, "executable_missing"),
            Plugin("notifier", PluginStatus.Unavailable, "executable_version_check_failed")));

        Assert.Equal(CheckState.Failed, check.State);
        Assert.StartsWith("reply is unavailable; its problems: executable_missing. notifier is unavailable; its problems: executable_version_check_failed.", check.Fact, StringComparison.Ordinal);
        Assert.DoesNotContain("acme-sdr", check.Fact, StringComparison.Ordinal);
    }

    /// <summary>A notification plugin that cannot run is named like any other, and the fact says its deliveries stop.</summary>
    [Fact]
    public async Task An_unavailable_notification_plugin_is_named_and_the_fact_says_it_delivers_nothing()
    {
        var check = await CheckAsync(Registry(Plugin("notifier", PluginStatus.Unavailable, PluginKind.Notification, "probe_timed_out")));

        Assert.Equal(CheckState.Failed, check.State);
        Assert.StartsWith("notifier is unavailable; its problems: probe_timed_out. ", check.Fact, StringComparison.Ordinal);
        Assert.Contains(WhatStops, check.Fact, StringComparison.Ordinal);
    }

    /// <summary>The fact is held to a line a table can show: ten plugins are named, and the rest are counted.</summary>
    [Fact]
    public async Task More_unavailable_plugins_than_ten_are_named_to_ten_and_the_rest_counted()
    {
        var check = await CheckAsync(Registry([.. Enumerable.Range(0, 12).Select(n => Plugin($"plugin-{n:D2}", PluginStatus.Unavailable, "executable_missing"))]));

        var named = string.Join(" ", Enumerable.Range(0, 10).Select(n => $"plugin-{n:D2} is unavailable; its problems: executable_missing."));
        Assert.StartsWith(named + " 2 more plugins are unavailable. ", check.Fact, StringComparison.Ordinal);
        Assert.DoesNotContain("plugin-10", check.Fact, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Exactly_ten_unavailable_plugins_are_all_named_and_none_is_counted()
    {
        var check = await CheckAsync(Registry([.. Enumerable.Range(0, 10).Select(n => Plugin($"plugin-{n:D2}", PluginStatus.Unavailable, "executable_missing"))]));

        var named = string.Join(" ", Enumerable.Range(0, 10).Select(n => $"plugin-{n:D2} is unavailable; its problems: executable_missing."));
        Assert.StartsWith(named + " This machine holds an unavailable plugin back", check.Fact, StringComparison.Ordinal);
        Assert.DoesNotContain(" more plugin", check.Fact, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Eleven_unavailable_plugins_are_named_to_ten_and_one_is_counted_in_the_singular()
    {
        var check = await CheckAsync(Registry([.. Enumerable.Range(0, 11).Select(n => Plugin($"plugin-{n:D2}", PluginStatus.Unavailable, "executable_missing"))]));

        var named = string.Join(" ", Enumerable.Range(0, 10).Select(n => $"plugin-{n:D2} is unavailable; its problems: executable_missing."));
        Assert.StartsWith(named + " 1 more plugin is unavailable. This machine holds an unavailable plugin back", check.Fact, StringComparison.Ordinal);
        Assert.DoesNotContain("plugin-10", check.Fact, StringComparison.Ordinal);
    }

    [Fact]
    public async Task More_codes_than_ten_on_one_plugin_are_named_to_ten_and_the_rest_counted()
    {
        var check = await CheckAsync(Registry(Plugin("reply", PluginStatus.Unavailable, [.. Enumerable.Range(0, 12).Select(n => $"problem_{n:D2}")])));

        var named = string.Join(", ", Enumerable.Range(0, 10).Select(n => $"problem_{n:D2}"));
        Assert.StartsWith($"reply is unavailable; its problems: {named}, and 2 more. ", check.Fact, StringComparison.Ordinal);
        Assert.DoesNotContain("problem_10", check.Fact, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Exactly_ten_codes_on_one_plugin_are_all_named_and_none_is_counted()
    {
        var check = await CheckAsync(Registry(Plugin("reply", PluginStatus.Unavailable, [.. Enumerable.Range(0, 10).Select(n => $"problem_{n:D2}")])));

        var named = string.Join(", ", Enumerable.Range(0, 10).Select(n => $"problem_{n:D2}"));
        Assert.StartsWith($"reply is unavailable; its problems: {named}. This machine holds an unavailable plugin back", check.Fact, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Eleven_codes_on_one_plugin_are_named_to_ten_and_one_is_counted()
    {
        var check = await CheckAsync(Registry(Plugin("reply", PluginStatus.Unavailable, [.. Enumerable.Range(0, 11).Select(n => $"problem_{n:D2}")])));

        var named = string.Join(", ", Enumerable.Range(0, 10).Select(n => $"problem_{n:D2}"));
        Assert.StartsWith($"reply is unavailable; its problems: {named}, and 1 more. This machine holds an unavailable plugin back", check.Fact, StringComparison.Ordinal);
        Assert.DoesNotContain("problem_10", check.Fact, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_plugin_available_is_ok_and_names_none()
    {
        var check = await CheckAsync(Registry(Plugin("acme-sdr", PluginStatus.Valid), Plugin("reply", PluginStatus.Valid)));

        Assert.Equal(CheckState.Ok, check.State);
        Assert.Equal("Every plugin installed is available (2).", check.Fact);
        Assert.Null(check.Fix);
    }

    /// <summary>A warning on a plugin that can run is not a reason to name it: only the status says whether it is held back.</summary>
    [Fact]
    public async Task A_plugin_that_can_run_is_not_named_for_a_warning()
    {
        var check = await CheckAsync(Registry(Plugin("reply", PluginStatus.Valid, "grant_unrequested")));

        Assert.Equal(CheckState.Ok, check.State);
        Assert.DoesNotContain("reply", check.Fact, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_plugin_is_absent()
    {
        var check = await CheckAsync(Registry());

        Assert.Equal(CheckState.Absent, check.State);
        Assert.Equal("No plugin is installed, so none is unavailable.", check.Fact);
    }

    /// <summary>
    /// The fact quotes the runtime's words and nothing else: an id no package can have, and a code that is not a word, are
    /// not quoted, and a problem's message — a sentence about this machine — never is. A bundle carries this fact as it
    /// is, and its own plugin list keeps exactly these words.
    /// </summary>
    [Fact]
    public async Task Only_a_well_formed_id_and_worded_codes_are_quoted_and_never_a_message()
    {
        var check = await CheckAsync(Registry(
            Plugin("Not An Id", PluginStatus.Unavailable, "executable_missing"),
            Plugin("reply", PluginStatus.Unavailable, "SENTINEL code", "executable_not_runnable")));

        Assert.Equal(CheckState.Failed, check.State);
        Assert.StartsWith("A plugin whose id no package can have is unavailable; its problems: executable_missing. reply is unavailable; its problems: executable_not_runnable.", check.Fact, StringComparison.Ordinal);
        Assert.DoesNotContain("Not An Id", check.Fact, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL", check.Fact, StringComparison.Ordinal);
        Assert.DoesNotContain(Message, check.Fact, StringComparison.Ordinal);
        Assert.DoesNotContain(PackageRoot, check.Fact, StringComparison.Ordinal);
        Assert.DoesNotContain("capabilities.", check.Fact, StringComparison.Ordinal);
    }

    /// <summary>A code is a word of at most sixty-four characters: the longest that is one is quoted, and one more is not.</summary>
    [Fact]
    public async Task A_code_of_sixty_five_characters_is_not_quoted_and_one_of_sixty_four_is()
    {
        var longest = new string('a', 64);
        var check = await CheckAsync(Registry(Plugin("reply", PluginStatus.Unavailable, new string('a', 65), longest, "executable_missing")));

        Assert.StartsWith($"reply is unavailable; its problems: {longest}, executable_missing. ", check.Fact, StringComparison.Ordinal);
    }

    /// <summary>A code starts with a lowercase letter: a digit or an underscore first is not a word the runtime spells.</summary>
    [Fact]
    public async Task A_code_that_starts_with_a_digit_or_an_underscore_is_not_quoted()
    {
        var check = await CheckAsync(Registry(Plugin("reply", PluginStatus.Unavailable, "9_lives", "_hidden", "executable_missing")));

        Assert.StartsWith("reply is unavailable; its problems: executable_missing. ", check.Fact, StringComparison.Ordinal);
    }

    /// <summary>
    /// plugin.list failing is unknown, and the exit code is the one the other checks make: this check never decides it.
    /// </summary>
    [Fact]
    public async Task A_plugin_list_that_does_not_answer_is_unknown_and_keeps_the_exit_code()
    {
        using var dir = new TempPaths();
        StatusCommandTests.Running(dir);
        var output = new StringWriter();

        var exit = await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), Ct);

        var report = StatusCommandTests.Read(output);
        var check = Assert.Single(report.Checks, c => c.Name == Name);
        Assert.Equal(CheckState.Unknown, check.State);
        Assert.Equal("plugin.list did not answer, so whether each plugin is available is not known.", check.Fact);
        Assert.Null(check.Fix);
        Assert.True(report.Ready);
        Assert.Equal(ExitCodes.Success, exit);
    }

    [Fact]
    public async Task A_runtime_that_did_not_answer_was_not_asked_and_status_never_exits_three()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();

        var exit = await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), Ct);

        var check = Assert.Single(StatusCommandTests.Read(output).Checks, c => c.Name == Name);
        Assert.Equal(CheckState.Unknown, check.State);
        Assert.Equal("The runtime did not answer, so whether its plugins are available was not asked.", check.Fact);
        Assert.False(check.Required);
        Assert.Equal(ExitCodes.ApiError, exit);
    }

    [Fact]
    public async Task The_check_follows_the_plugin_package_check()
    {
        using var dir = new TempPaths();
        StatusCommandTests.Running(dir);
        var output = new StringWriter();
        await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), Ct);

        var names = StatusCommandTests.Read(output).Checks.Select(c => c.Name).ToList();
        Assert.Equal(names.IndexOf("plugin_package") + 1, names.IndexOf(Name));
    }

    [Fact]
    public async Task Help_publishes_the_check_as_optional_and_what_it_names()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();

        await CliApp.RunAsync(["status", "--help"], new CliEnvironment(output, new StringWriter(), dir.Paths), Ct);

        var text = string.Join(' ', output.ToString().Split((char[])[' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        var optional = text.IndexOf("Optional — absent never fails:", StringComparison.Ordinal);
        Assert.True(optional >= 0, text);
        Assert.True(text.IndexOf("every plugin installed is available", StringComparison.Ordinal) > optional, text);
        Assert.Contains("The availability check names each plugin 'jason plugin list' reports unavailable, with its problem codes", text, StringComparison.Ordinal);
    }

    /// <summary>The problem's own sentence, which the runtime writes about this machine and the fact never quotes.</summary>
    private const string Message = "No program of that name is on this runtime's search path at /home/ada/bin.";

    private static async Task<StatusCheck> CheckAsync(PluginRegistryDto registry)
    {
        using var dir = new TempPaths();
        StatusCommandTests.Running(dir, registry: registry);
        var output = new StringWriter();
        await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), Ct);

        return Assert.Single(StatusCommandTests.Read(output).Checks, c => c.Name == Name);
    }

    private static PluginRegistryDto Registry(params PluginDto[] plugins) => new(
        new SnapshotDto("snp_01J4", DateTimeOffset.UnixEpoch, SnapshotSource.Startup, plugins.Length),
        "rts_01J4",
        plugins,
        null,
        Activated: true);

    private static PluginDto Plugin(string id, PluginStatus status, params string[] codes) => Plugin(id, status, PluginKind.Provider, codes);

    private static PluginDto Plugin(string id, PluginStatus status, PluginKind kind, params string[] codes) => new(
        id,
        "1.0.0",
        kind,
        "SENTINEL manifest name",
        "SENTINEL manifest description",
        null,
        $"{PackageRoot}/plugins/{id}",
        "sha256:3f2a",
        new PluginContractsDto([1], [1], []),
        [],
        new PluginEntry("main.js", "invoke"),
        new PluginCapabilitiesDto(new ExecCapabilityDto([new ExecutableDto("acme", null, null, null)], ["acme"]), null, null),
        new PluginLimitsDto(60000, 64),
        null,
        status,
        [.. codes.Select(code => new PluginProblemDto(code, ProblemPath, Message))]);
}
