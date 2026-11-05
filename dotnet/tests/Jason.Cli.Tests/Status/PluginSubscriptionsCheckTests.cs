using Jason.Cli.Status;
using Jason.Contracts.Plugins;

namespace Jason.Cli.Tests.Status;

/// <summary>
/// jason status names each plugin the settings grant journal kinds to, with those kinds, from the same plugin.list answer
/// the availability check reads. Optional, and never deciding readiness: a kind no plugin handles is the ordinary state of
/// an installation.
/// </summary>
public class PluginSubscriptionsCheckTests
{
    private const string Name = "plugin_subscriptions";

    private const string Tail = " `jason plugin list` says which kinds each plugin declares and which it is granted.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_plugin_granted_kinds_is_named_with_them_and_the_installation_stays_ready()
    {
        using var dir = new TempPaths();
        StatusCommandTests.Running(dir, registry: Registry(Plugin("forwarder", ["workitem_failed", "campaign_created"], ["workitem_failed", "campaign_created"])));
        var output = new StringWriter();

        var exit = await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), Ct);

        var report = StatusCommandTests.Read(output);
        var check = Assert.Single(report.Checks, c => c.Name == Name);
        Assert.Equal(CheckState.Ok, check.State);
        Assert.False(check.Required);
        Assert.Equal("forwarder handles workitem_failed, campaign_created." + Tail, check.Fact);
        Assert.Null(check.Fix);
        Assert.True(report.Ready);
        Assert.Equal(ExitCodes.Success, exit);
    }

    /// <summary>A kind declared and not granted is not handled, so it is not named; the listing shows the difference.</summary>
    [Fact]
    public async Task Only_granted_kinds_are_named_and_a_plugin_granted_none_is_not()
    {
        var check = await CheckAsync(Registry(
            Plugin("acme-sdr", ["workitem_failed"], []),
            Plugin("forwarder", ["workitem_failed", "campaign_created"], ["workitem_failed"]),
            Plugin("reply", null, null)));

        Assert.Equal(CheckState.Ok, check.State);
        Assert.Equal("forwarder handles workitem_failed." + Tail, check.Fact);
    }

    [Fact]
    public async Task No_plugin_granted_a_kind_is_absent()
    {
        var check = await CheckAsync(Registry(Plugin("reply", null, null), Plugin("acme-sdr", ["workitem_failed"], [])));

        Assert.Equal(CheckState.Absent, check.State);
        Assert.Equal("No plugin is granted a journal kind, so none handles one.", check.Fact);
    }

    [Fact]
    public async Task No_plugin_at_all_is_absent()
    {
        var check = await CheckAsync(Registry());

        Assert.Equal(CheckState.Absent, check.State);
    }

    [Fact]
    public async Task More_subscribers_than_ten_are_named_to_ten_and_the_rest_counted()
    {
        var check = await CheckAsync(Registry([.. Enumerable.Range(0, 12).Select(n => Plugin($"plugin-{n:D2}", ["workitem_failed"], ["workitem_failed"]))]));

        var named = string.Join(" ", Enumerable.Range(0, 10).Select(n => $"plugin-{n:D2} handles workitem_failed."));
        Assert.Equal(named + " 2 more plugins handle journal kinds." + Tail, check.Fact);
    }

    [Fact]
    public async Task Eleven_subscribers_are_named_to_ten_and_one_is_counted_in_the_singular()
    {
        var check = await CheckAsync(Registry([.. Enumerable.Range(0, 11).Select(n => Plugin($"plugin-{n:D2}", ["workitem_failed"], ["workitem_failed"]))]));

        Assert.EndsWith("plugin-09 handles workitem_failed. 1 more plugin handles journal kinds." + Tail, check.Fact, StringComparison.Ordinal);
        Assert.DoesNotContain("plugin-10", check.Fact, StringComparison.Ordinal);
    }

    [Fact]
    public async Task More_kinds_than_ten_on_one_plugin_are_named_to_ten_and_the_rest_counted()
    {
        string[] kinds = [.. Enumerable.Range(0, 12).Select(n => $"kind_{n:D2}")];
        var check = await CheckAsync(Registry(Plugin("forwarder", kinds, kinds)));

        Assert.Equal($"forwarder handles {string.Join(", ", kinds.Take(10))}, and 2 more." + Tail, check.Fact);
    }

    [Fact]
    public async Task Exactly_ten_kinds_are_all_named_and_none_is_counted()
    {
        string[] kinds = [.. Enumerable.Range(0, 10).Select(n => $"kind_{n:D2}")];
        var check = await CheckAsync(Registry(Plugin("forwarder", kinds, kinds)));

        Assert.Equal($"forwarder handles {string.Join(", ", kinds)}." + Tail, check.Fact);
    }

    /// <summary>
    /// The fact quotes the runtime's words and nothing else: an id no package can have and a kind that is not a word are
    /// not quoted, and a kind left unquoted is still counted, so the count says what the names do not.
    /// </summary>
    [Fact]
    public async Task Only_a_well_formed_id_and_worded_kinds_are_quoted_and_the_rest_counted()
    {
        var check = await CheckAsync(Registry(
            Plugin("Not An Id", ["workitem_failed"], ["workitem_failed"]),
            Plugin("forwarder", ["SENTINEL kind", "workitem_failed"], ["SENTINEL kind", "workitem_failed"]),
            Plugin("relay", ["SENTINEL other"], ["SENTINEL other"])));

        Assert.Equal(
            "A plugin whose id no package can have handles workitem_failed. forwarder handles workitem_failed, and 1 more. relay handles 1 kind." + Tail,
            check.Fact);
        Assert.DoesNotContain("SENTINEL", check.Fact, StringComparison.Ordinal);
        Assert.DoesNotContain("Not An Id", check.Fact, StringComparison.Ordinal);
    }

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
        Assert.Equal("plugin.list did not answer, so which plugins handle journal kinds is not known.", check.Fact);
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
        Assert.Equal("The runtime did not answer, so which plugins handle journal kinds was not asked.", check.Fact);
        Assert.False(check.Required);
    }

    [Fact]
    public async Task The_check_follows_the_availability_check()
    {
        using var dir = new TempPaths();
        StatusCommandTests.Running(dir);
        var output = new StringWriter();
        await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), Ct);

        var names = StatusCommandTests.Read(output).Checks.Select(c => c.Name).ToList();
        Assert.Equal(names.IndexOf("plugin_availability") + 1, names.IndexOf(Name));
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
        Assert.True(text.IndexOf("a plugin is granted a journal kind", StringComparison.Ordinal) > optional, text);
        Assert.Contains("The subscriptions check names each plugin the settings grant journal kinds to", text, StringComparison.Ordinal);
        Assert.Contains("so the check never decides readiness", text, StringComparison.Ordinal);
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

    private static PluginDto Plugin(string id, string[]? requested, string[]? granted) => new(
        id,
        "1.0.0",
        PluginKind.Notification,
        "SENTINEL manifest name",
        null,
        null,
        $"/home/ada/.jason/plugins/{id}",
        "sha256:3f2a",
        new PluginContractsDto([], [], []),
        [],
        new PluginEntry("main.js", "invoke"),
        new PluginCapabilitiesDto(null, null, null)
        {
            Subscriptions = requested is null ? null : new ListCapabilityDto(requested, granted ?? []),
        },
        new PluginLimitsDto(60000, 64),
        null,
        PluginStatus.Valid,
        []);
}
