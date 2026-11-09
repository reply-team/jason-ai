using Jason.Cli.Status;
using Jason.Contracts.Plugins;

namespace Jason.Cli.Tests.Status;

/// <summary>
/// jason status names each loaded plugin that asks for a capability and is granted nothing of it, by its id and the
/// capability's key under Plugins:Grants, from the same plugin.list answer the availability check reads. Optional, and
/// never deciding readiness: such a plugin runs without what it is not granted, and nothing else stops.
/// </summary>
public class PluginGrantsCheckTests
{
    private const string Name = "plugin_grants";

    private const string Tail =
        " Such a plugin runs without what it is not granted, and nothing else stops, so this check never decides readiness."
        + " `jason plugin list --human` says what each asks for, the key that grants it and the settings file, and"
        + " `jason plugin reload` reads the grant.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_plugin_granted_nothing_of_what_it_asks_is_named_by_its_keys_and_the_installation_stays_ready()
    {
        using var dir = new TempPaths();
        StatusCommandTests.Running(dir, registry: Registry(Plugin("reply", exec: ["reply"], http: ["api.example.com"])));
        var output = new StringWriter();

        var exit = await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), Ct);

        var report = StatusCommandTests.Read(output);
        var check = Assert.Single(report.Checks, c => c.Name == Name);
        Assert.Equal(CheckState.Failed, check.State);
        Assert.False(check.Required);
        Assert.Equal("reply is granted nothing it asks for under Exec, Http." + Tail, check.Fact);
        Assert.Null(check.Fix);
        Assert.True(report.Ready);
        Assert.Equal(ExitCodes.Success, exit);
    }

    /// <summary>
    /// The fact carries ids and keys and nothing a manifest names: neither the program a plugin runs nor the host it
    /// reaches, which <c>jason plugin list --human</c> says beside the key.
    /// </summary>
    [Fact]
    public async Task The_fact_names_no_program_and_no_host_the_manifest_asks_for()
    {
        var check = await CheckAsync(Registry(Plugin("reply", exec: ["SENTINEL_PROGRAM"], http: ["sentinel.example.com"])));

        Assert.DoesNotContain("SENTINEL_PROGRAM", check.Fact, StringComparison.Ordinal);
        Assert.DoesNotContain("sentinel.example.com", check.Fact, StringComparison.Ordinal);
    }

    /// <summary>A capability granted part of what it asks for is granted something, so the plugin is not named for it.</summary>
    [Fact]
    public async Task A_capability_granted_part_of_what_it_asks_is_not_named()
    {
        var check = await CheckAsync(Registry(
            Plugin("reply", exec: ["reply", "node"], execGranted: ["reply"], http: ["api.example.com"]),
            Plugin("relay", exec: ["relay"], execGranted: ["relay"])));

        Assert.Equal("reply is granted nothing it asks for under Http." + Tail, check.Fact);
    }

    /// <summary>
    /// A plugin the machine holds back is the availability check's to name, with its problems; only a loaded plugin is
    /// named here.
    /// </summary>
    [Fact]
    public async Task An_unavailable_plugin_is_left_to_the_availability_check()
    {
        var check = await CheckAsync(Registry(Plugin("reply", exec: ["reply"], status: PluginStatus.Unavailable)));

        Assert.Equal(CheckState.Ok, check.State);
        Assert.Equal("Every plugin loaded is granted something of each capability it asks for.", check.Fact);
    }

    [Fact]
    public async Task Plugins_granted_something_of_each_capability_are_ok()
    {
        var check = await CheckAsync(Registry(Plugin("reply", exec: ["reply"], execGranted: ["reply"]), Plugin("quiet")));

        Assert.Equal(CheckState.Ok, check.State);
        Assert.Equal("Every plugin loaded is granted something of each capability it asks for.", check.Fact);
    }

    [Fact]
    public async Task No_plugin_at_all_is_absent()
    {
        var check = await CheckAsync(Registry());

        Assert.Equal(CheckState.Absent, check.State);
        Assert.Equal("No plugin is installed, so none waits for a grant.", check.Fact);
    }

    [Fact]
    public async Task More_plugins_than_ten_are_named_to_ten_and_the_rest_counted()
    {
        var check = await CheckAsync(Registry([.. Enumerable.Range(0, 12).Select(n => Plugin($"plugin-{n:D2}", exec: ["vendor"]))]));

        var named = string.Join(" ", Enumerable.Range(0, 10).Select(n => $"plugin-{n:D2} is granted nothing it asks for under Exec."));
        Assert.Equal(named + " 2 more plugins are granted nothing they ask for." + Tail, check.Fact);
    }

    [Fact]
    public async Task An_id_no_package_can_have_is_not_quoted()
    {
        var check = await CheckAsync(Registry(Plugin("Not An Id", exec: ["vendor"])));

        Assert.StartsWith("A plugin whose id no package can have is granted nothing it asks for under Exec.", check.Fact, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_plugin_list_that_does_not_answer_is_unknown()
    {
        using var dir = new TempPaths();
        StatusCommandTests.Running(dir);
        var output = new StringWriter();

        var exit = await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), Ct);

        var report = StatusCommandTests.Read(output);
        var check = Assert.Single(report.Checks, c => c.Name == Name);
        Assert.Equal(CheckState.Unknown, check.State);
        Assert.Equal("plugin.list did not answer, so what each plugin is granted is not known.", check.Fact);
        Assert.True(report.Ready);
        Assert.Equal(ExitCodes.Success, exit);
    }

    [Fact]
    public async Task A_runtime_that_did_not_answer_was_not_asked()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();

        await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), Ct);

        var check = Assert.Single(StatusCommandTests.Read(output).Checks, c => c.Name == Name);
        Assert.Equal(CheckState.Unknown, check.State);
        Assert.Equal("The runtime did not answer, so what its plugins are granted was not asked.", check.Fact);
        Assert.False(check.Required);
    }

    [Fact]
    public async Task The_check_follows_the_subscriptions_check()
    {
        using var dir = new TempPaths();
        StatusCommandTests.Running(dir);
        var output = new StringWriter();
        await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), Ct);

        var names = StatusCommandTests.Read(output).Checks.Select(c => c.Name).ToList();
        Assert.Equal(names.IndexOf("plugin_subscriptions") + 1, names.IndexOf(Name));
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
        Assert.True(text.IndexOf("every plugin loaded is granted something of each capability it asks for", StringComparison.Ordinal) > optional, text);
        Assert.Contains(
            "The grants check names each loaded plugin that asks for a capability and is granted nothing of it, by its id and the capability's key under Plugins:Grants",
            text,
            StringComparison.Ordinal);
    }

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

    private static PluginDto Plugin(
        string id,
        string[]? exec = null,
        string[]? execGranted = null,
        string[]? http = null,
        PluginStatus status = PluginStatus.Valid) => new(
        id,
        "1.0.0",
        PluginKind.Provider,
        null,
        null,
        null,
        $"/home/u/.jason/plugins/{id}",
        "sha256:" + new string('0', 64),
        new PluginContractsDto([1], [1], []),
        ["contact.get"],
        new PluginEntry("main.js", "invoke"),
        new PluginCapabilitiesDto(
            exec is null ? null : new ExecCapabilityDto([.. exec.Select(name => new ExecutableDto(name, null, null, null))], execGranted ?? []),
            http is null ? null : new ListCapabilityDto(http, []),
            null),
        new PluginLimitsDto(60000, 64),
        null,
        status,
        []);
}
