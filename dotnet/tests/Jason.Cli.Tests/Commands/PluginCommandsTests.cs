using System.Net;
using System.Text.Json;
using Jason.Cli;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Contracts.Plugins;

namespace Jason.Cli.Tests.Commands;

public class PluginCommandsTests
{
    private static readonly DateTimeOffset Moment = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task List_sends_an_empty_body()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("plugin", "list");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.PluginList, "{}");
    }

    [Fact]
    public async Task Reload_sends_the_reason_and_the_actor()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("plugin", "reload", "--reason", "installed the fake provider", "--actor", "human:ada");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.PluginReload, "{\"reason\":\"installed the fake provider\",\"actor\":{\"type\":\"human\",\"id\":\"ada\"}}");
    }

    [Fact]
    public async Task Reload_without_options_sends_an_empty_body()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("plugin", "reload");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.PluginReload, "{}");
    }

    [Fact]
    public async Task A_reserved_actor_is_refused_on_the_read_only_verb()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("plugin", "list", "--actor", "system");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--actor", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_renders_the_registry_as_a_table()
    {
        using var cli = new CliRun(JsonSerializer.Serialize(TwoPlugins(), JasonJson.Options));

        var exit = await cli.RunAsync("plugin", "list", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(
            $"""
            ID             VERSION  KIND          STATUS       OPERATIONS              GRANTS                                             DIGEST
            -------------  -------  ------------  -----------  ----------------------  -------------------------------------------------  ------------
            fake-provider  1.0.0    provider      valid        echo.run, exec.run ...  exec 1/1 | http 0/2 | env 1/2 | subscriptions 1/2  3f2a9c1b4d5e
            notifier       0.2.0    notification  unavailable  -                       exec 1/1 | http - | env - | subscriptions -        9a8b7c6d5e4f

            fake-provider is loaded and granted only part of what it asks for. It asks to reach: localhost:5555, api.example.com. Grant them under Plugins:Grants:fake-provider:Http in {cli.Environment.Paths.UserSettingsFile}, then run `jason plugin reload`.
            notifier unavailable: executable_missing — No 'notify' was found.
            """.ReplaceLineEndings() + Environment.NewLine,
            cli.Text);
    }

    [Fact]
    public async Task Human_mode_says_which_plugins_read_their_accounts_events()
    {
        var registry = TwoPlugins();
        var reader = registry.Plugins[0] with { Contracts = new PluginContractsDto([1], [1], [1]), Operations = ["campaign.get"] };
        using var cli = new CliRun(JsonSerializer.Serialize(registry with { Plugins = [reader] }, JasonJson.Options));

        await cli.RunAsync("plugin", "list", "--human");

        Assert.Contains("campaign.get + events", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_counts_a_plugin_that_declares_only_subscriptions_among_the_granted()
    {
        var registry = TwoPlugins();
        var subscriber = registry.Plugins[1] with
        {
            Capabilities = new PluginCapabilitiesDto(null, null, null)
            {
                Subscriptions = new ListCapabilityDto(["workitem_failed"], ["workitem_failed"]),
            },
        };
        using var cli = new CliRun(JsonSerializer.Serialize(registry with { Plugins = [subscriber] }, JasonJson.Options));

        await cli.RunAsync("plugin", "list", "--human");

        Assert.Contains("exec - | http - | env - | subscriptions 1/1", cli.Text, StringComparison.Ordinal);
    }

    /// <summary>A grant beyond the declared hosts counts as every grant does, and the word that allows it stands beside the counts.</summary>
    [Fact]
    public async Task Human_mode_says_the_word_beside_the_capabilities()
    {
        var registry = TwoPlugins();
        var forwarder = registry.Plugins[1] with
        {
            Capabilities = new PluginCapabilitiesDto(null, new ListCapabilityDto(["127.0.0.1:9123"], ["receiver.example.test", "127.0.0.1:9123"]), null)
            {
                AnyGrantedHost = true,
            },
        };
        using var cli = new CliRun(JsonSerializer.Serialize(registry with { Plugins = [forwarder] }, JasonJson.Options));

        await cli.RunAsync("plugin", "list", "--human");

        Assert.Contains("exec - | http 2/1 | env - | subscriptions - | any_granted_host", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_package_without_the_word_keeps_the_answer_it_always_had()
    {
        var answer = JsonSerializer.Serialize(new PluginCapabilitiesDto(null, null, null), JasonJson.Options);

        Assert.DoesNotContain("any_granted_host", answer, StringComparison.Ordinal);
    }

    [Fact]
    public void A_package_that_asks_for_no_files_keeps_the_answer_it_always_had()
    {
        var answer = JsonSerializer.Serialize(new PluginCapabilitiesDto(null, null, null), JasonJson.Options);

        Assert.DoesNotContain("\"files\"", answer, StringComparison.Ordinal);
    }

    /// <summary>A manifest names no place, so there is nothing to count the directories against.</summary>
    [Fact]
    public async Task Human_mode_counts_the_directories_a_plugin_may_write_in()
    {
        var registry = TwoPlugins();
        var writer = registry.Plugins[1] with
        {
            Capabilities = new PluginCapabilitiesDto(null, null, null) { Files = new FilesCapabilityDto(true, ["/srv/lines", "/srv/other"]) },
        };
        using var cli = new CliRun(JsonSerializer.Serialize(registry with { Plugins = [writer] }, JasonJson.Options));

        await cli.RunAsync("plugin", "list", "--human");

        Assert.Contains("exec - | http - | env - | subscriptions - | files 2", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_says_nothing_of_files_for_a_plugin_that_asks_for_none()
    {
        using var cli = new CliRun(JsonSerializer.Serialize(TwoPlugins(), JasonJson.Options));

        await cli.RunAsync("plugin", "list", "--human");

        Assert.DoesNotContain("files", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_counts_the_api_families_granted_against_those_requested()
    {
        var registry = TwoPlugins();
        var handler = registry.Plugins[1] with
        {
            Capabilities = new PluginCapabilitiesDto(null, null, null, new ListCapabilityDto(["read", "journal.append"], ["read"])),
        };
        using var cli = new CliRun(JsonSerializer.Serialize(registry with { Plugins = [handler] }, JasonJson.Options));

        await cli.RunAsync("plugin", "list", "--human");

        Assert.Contains("exec - | http - | env - | api 1/2", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_names_the_problems_of_a_rejected_reload()
    {
        using var cli = new CliRun(JsonSerializer.Serialize(KeptAfterARejectedReload(), JasonJson.Options));

        var exit = await cli.RunAsync("plugin", "list", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(
            """
            ID             VERSION  KIND      STATUS  OPERATIONS  GRANTS  DIGEST
            -------------  -------  --------  ------  ----------  ------  ------------
            fake-provider  1.0.0    provider  valid   echo.run    -       3f2a9c1b4d5e

            last reload rejected at 2026-09-14 12:00:00 UTC:
            broken/plugin.yaml#3:1: yaml_invalid — mapping values are not allowed here
            broken/plugin.yaml#id: id_mismatch — the manifest says 'other', the directory says 'broken'
            Routes:Default: route_plugin_unknown — no plugin 'gone' is in the candidate set
            Subscriptions:Order:workitem_failed[0]: subscription_order_ungranted — no plugin with id 'gone' is in the candidate set
            """.ReplaceLineEndings() + Environment.NewLine,
            cli.Text);
    }

    [Fact]
    public async Task Human_mode_falls_back_to_the_raw_body_when_the_shape_is_unknown()
    {
        using var cli = new CliRun("{\"unexpected\":true}");

        var exit = await cli.RunAsync("plugin", "list", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("{\"unexpected\":true}", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_reports_what_the_reload_activated()
    {
        using var cli = new CliRun(JsonSerializer.Serialize(TwoPlugins(), JasonJson.Options));

        var exit = await cli.RunAsync("plugin", "reload", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(
            $"""
            activated snapshot snp_01J4 with 2 plugins
            fake-provider is loaded and granted only part of what it asks for. It asks to reach: localhost:5555, api.example.com. Grant them under Plugins:Grants:fake-provider:Http in {cli.Environment.Paths.UserSettingsFile}, then run `jason plugin reload`.
            notifier unavailable: executable_missing — No 'notify' was found.
            """.ReplaceLineEndings() + Environment.NewLine,
            cli.Text);
    }

    [Fact]
    public async Task Human_mode_counts_one_plugin_as_one()
    {
        using var cli = new CliRun(JsonSerializer.Serialize(KeptAfterARejectedReload() with { LastReload = null, Activated = true }, JasonJson.Options));

        var exit = await cli.RunAsync("plugin", "reload", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("activated snapshot snp_01J4 with 1 plugin" + Environment.NewLine, cli.Text);
    }

    [Fact]
    public async Task Human_mode_falls_back_to_the_raw_body_when_the_reload_was_not_activated()
    {
        using var cli = new CliRun(JsonSerializer.Serialize(KeptAfterARejectedReload(), JasonJson.Options));

        var exit = await cli.RunAsync("plugin", "reload", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("\"activated\":false", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refused_reload_prints_the_error_body_and_exits_1()
    {
        const string envelope =
            "{\"error\":{\"code\":\"plugin_reload_rejected\",\"message\":\"2 problems in 1 package.\",\"retryable\":false,"
            + "\"details\":[{\"field\":\"broken/plugin.yaml#3:1\",\"code\":\"yaml_invalid\",\"message\":\"mapping values are not allowed here\"}]}}";
        using var cli = new CliRun(envelope, status: HttpStatusCode.Conflict);

        var exit = await cli.RunAsync("plugin", "reload", "--human");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal(envelope + Environment.NewLine, cli.Text);
    }

    /// <summary>
    /// A plugin the runtime loaded that the settings grant nothing reads <c>valid</c>, and every call it makes would be
    /// refused: under the table one sentence names what it asks for, the key it is granted under, the settings file this CLI
    /// uses and the reload, in that order. The table keeps its counts.
    /// </summary>
    [Theory]
    [InlineData("list")]
    [InlineData("reload")]
    public async Task Human_mode_says_how_to_grant_a_plugin_granted_nothing(string verb)
    {
        using var cli = new CliRun(Serialize(OnePlugin(PluginStatus.Valid, new PluginCapabilitiesDto(Exec("reply", granted: false), null, null))));

        var exit = await cli.RunAsync("plugin", verb, "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(
            $"reply is loaded and granted nothing. It asks to run: reply. Grant it under Plugins:Grants:reply:Exec in {cli.Environment.Paths.UserSettingsFile}, then run `jason plugin reload`.",
            Lines(cli.Text)[^1]);
        if (verb == "list")
        {
            Assert.Contains("exec 0/1", cli.Text, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Granted something is not granted everything: each capability granted nothing of what it asks has its own sentence and
    /// its own key, in the manifest's order, and one granted part of what it asks (env 1/2 here) has none. A directory is no
    /// name a manifest can ask for, so files has words of its own.
    /// </summary>
    [Theory]
    [InlineData("list")]
    [InlineData("reload")]
    public async Task Human_mode_says_what_a_partly_granted_plugin_is_granted_nothing_of(string verb)
    {
        var capabilities = new PluginCapabilitiesDto(
            Exec("reply", granted: true),
            new ListCapabilityDto(["api.example.com"], []),
            new ListCapabilityDto(["FAKE_TOKEN", "OTHER_TOKEN"], ["FAKE_TOKEN"]),
            new ListCapabilityDto(["read", "journal.append"], []))
        {
            Subscriptions = new ListCapabilityDto(["workitem_failed"], []),
            Files = new FilesCapabilityDto(true, []),
        };
        using var cli = new CliRun(Serialize(OnePlugin(PluginStatus.Valid, capabilities)));

        await cli.RunAsync("plugin", verb, "--human");

        var file = cli.Environment.Paths.UserSettingsFile;
        Assert.Equal(
            "reply is loaded and granted only part of what it asks for."
            + $" It asks to reach: api.example.com. Grant it under Plugins:Grants:reply:Http in {file}."
            + $" It asks to call: read, journal.append. Grant them under Plugins:Grants:reply:Api in {file}."
            + $" It asks to be told of: workitem_failed. Grant it under Plugins:Grants:reply:Subscriptions in {file}."
            + $" It asks for directories to write to; grant them under Plugins:Grants:reply:Files in {file}, each an absolute path."
            + " Then run `jason plugin reload`.",
            Lines(cli.Text)[^1]);
    }

    /// <summary>An environment variable is asked for by name, and the last capability named carries the reload.</summary>
    [Fact]
    public async Task Human_mode_names_the_variables_a_plugin_asks_to_read()
    {
        var capabilities = new PluginCapabilitiesDto(null, null, new ListCapabilityDto(["FAKE_TOKEN"], []));
        using var cli = new CliRun(Serialize(OnePlugin(PluginStatus.Valid, capabilities)));

        await cli.RunAsync("plugin", "list", "--human");

        Assert.Equal(
            $"reply is loaded and granted nothing. It asks to read: FAKE_TOKEN. Grant it under Plugins:Grants:reply:Env in {cli.Environment.Paths.UserSettingsFile}, then run `jason plugin reload`.",
            Lines(cli.Text)[^1]);
    }

    /// <summary>A plugin granted all it asks for has nothing said under the table.</summary>
    [Fact]
    public async Task Human_mode_says_nothing_more_of_a_plugin_granted_what_it_asks()
    {
        using var cli = new CliRun(Serialize(OnePlugin(PluginStatus.Valid, new PluginCapabilitiesDto(Exec("reply", granted: true), null, null))));

        await cli.RunAsync("plugin", "reload", "--human");

        Assert.Equal("activated snapshot snp_01J4 with 1 plugin" + Environment.NewLine, cli.Text);
    }

    /// <summary>
    /// A plugin the machine cannot run says why in the runtime's own words: each problem's code with its message, never the
    /// code alone, since the message is where the repair is.
    /// </summary>
    [Theory]
    [InlineData("list")]
    [InlineData("reload")]
    public async Task Human_mode_names_each_problem_of_an_unavailable_plugin_with_its_message(string verb)
    {
        var registry = OnePlugin(PluginStatus.Unavailable, new PluginCapabilitiesDto(Exec("reply", granted: false), null, null));
        var unavailable = registry with
        {
            Plugins =
            [
                registry.Plugins[0] with
                {
                    Problems =
                    [
                        new PluginProblemDto("executable_missing", "capabilities.exec.executables[0].name", "'reply' was not found."),
                        new PluginProblemDto("executable_incompatible", "capabilities.exec.executables[1].name", "'other' is 0.1.0, older than 0.4.0."),
                    ],
                },
            ],
        };
        using var cli = new CliRun(Serialize(unavailable));

        await cli.RunAsync("plugin", verb, "--human");

        Assert.Equal(
            "reply unavailable: executable_missing — 'reply' was not found.; executable_incompatible — 'other' is 0.1.0, older than 0.4.0.",
            Lines(cli.Text)[^1]);
        Assert.DoesNotContain("granted nothing", cli.Text, StringComparison.Ordinal);
    }

    /// <summary>What a manifest asks for and what a runtime says of it are text nobody wrote here: shown inert.</summary>
    [Theory]
    [InlineData("list")]
    [InlineData("reload")]
    public async Task Human_mode_shows_what_a_manifest_asks_for_and_a_problem_s_message_inert(string verb)
    {
        const string Hostile = "x\u001b[2J‮y";
        var registry = OnePlugin(PluginStatus.Valid, new PluginCapabilitiesDto(null, new ListCapabilityDto([Hostile], []), null));
        var broken = registry.Plugins[0] with
        {
            Id = "other",
            Status = PluginStatus.Unavailable,
            Problems = [new PluginProblemDto("executable_missing", "capabilities.exec.executables[0].name", Hostile)],
        };
        using var cli = new CliRun(Serialize(registry with { Plugins = [registry.Plugins[0], broken] }));

        await cli.RunAsync("plugin", verb, "--human");

        Assert.DoesNotContain('\u001b', cli.Text);
        Assert.DoesNotContain('‮', cli.Text);
        Assert.Contains("It asks to reach: x�[2J�y.", cli.Text, StringComparison.Ordinal);
        Assert.Contains("executable_missing — x�[2J�y", cli.Text, StringComparison.Ordinal);
    }

    /// <summary>The help is the verb's contract, and says what it now prints for people under the table.</summary>
    [Theory]
    [InlineData("list")]
    [InlineData("reload")]
    public async Task The_help_says_what_human_mode_says_of_each_plugin(string verb)
    {
        using var cli = new CliRun();

        await cli.RunAsync("plugin", verb, "--help");

        Assert.Contains(
            "With --human, each unavailable plugin's problems are named with the runtime's words, and each capability a valid plugin asks for and is granted nothing of is named with the key that grants it in the settings file this CLI uses.",
            System.Text.RegularExpressions.Regex.Replace(cli.Text, @"\s+", " "),
            StringComparison.Ordinal);
    }

    private static string Serialize(PluginRegistryDto registry) => JsonSerializer.Serialize(registry, JasonJson.Options);

    private static string[] Lines(string text) => text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

    private static ExecCapabilityDto Exec(string name, bool granted) =>
        new([new ExecutableDto(name, "/usr/local/bin/" + name, null, null)], granted ? [name] : []);

    /// <summary>A registry of one plugin, <c>reply</c>, as an activated reload answers it.</summary>
    private static PluginRegistryDto OnePlugin(PluginStatus status, PluginCapabilitiesDto capabilities)
    {
        var registry = TwoPlugins();
        var reply = registry.Plugins[0] with
        {
            Id = "reply",
            Capabilities = capabilities,
            Status = status,
            Problems = [],
        };
        return registry with { Snapshot = registry.Snapshot with { PluginCount = 1 }, Plugins = [reply] };
    }

    /// <summary>A registry the way a machine with one working plugin and one it cannot run answers.</summary>
    private static PluginRegistryDto TwoPlugins() => new(
        new SnapshotDto("snp_01J4", Moment, SnapshotSource.Reload, 2),
        "rts_01J4",
        [
            new PluginDto(
                "fake-provider",
                "1.0.0",
                PluginKind.Provider,
                "Fake provider",
                "A stand-in for a vendor CLI.",
                null,
                "/home/u/.jason/plugins/fake-provider",
                "sha256:3f2a9c1b4d5e6f708192a3b4c5d6e7f8091a2b3c4d5e6f708192a3b4c5d6e7f8",
                new PluginContractsDto([1], [1], []),
                ["echo.run", "exec.run", "http.get"],
                new PluginEntry("main.js", "invoke"),
                new PluginCapabilitiesDto(
                    new ExecCapabilityDto([new ExecutableDto("dotnet", "/usr/bin/dotnet", "10.0.100", null)], ["dotnet"]),
                    new ListCapabilityDto(["localhost:5555", "api.example.com"], []),
                    new ListCapabilityDto(["FAKE_TOKEN", "OTHER_TOKEN"], ["FAKE_TOKEN"]))
                {
                    Subscriptions = new ListCapabilityDto(["workitem_failed", "campaign_paused"], ["workitem_failed"]),
                },
                new PluginLimitsDto(60000, 64),
                null,
                PluginStatus.Valid,
                []),
            new PluginDto(
                "notifier",
                "0.2.0",
                PluginKind.Notification,
                "Notifier",
                null,
                null,
                "/home/u/.jason/plugins/notifier",
                "sha256:9a8b7c6d5e4f3a2b1c0d9e8f7a6b5c4d3e2f1a0b9c8d7e6f5a4b3c2d1e0f9a8b",
                new PluginContractsDto([1], [], []),
                [],
                new PluginEntry("main.js", "invoke"),
                new PluginCapabilitiesDto(new ExecCapabilityDto([new ExecutableDto("notify", null, null, "0.4.0")], ["notify"]), null, null),
                new PluginLimitsDto(60000, 64),
                null,
                PluginStatus.Unavailable,
                [new PluginProblemDto("executable_missing", "capabilities.exec.executables[0]", "No 'notify' was found.")]),
        ],
        new ReloadReportDto(Moment, SnapshotSource.Reload, true, [], []),
        Activated: true);

    /// <summary>The snapshot that survived a refused reload, with the diagnostics that explain the refusal.</summary>
    private static PluginRegistryDto KeptAfterARejectedReload() => new(
        new SnapshotDto("snp_01J4", Moment, SnapshotSource.Startup, 1),
        "rts_01J4",
        [
            new PluginDto(
                "fake-provider",
                "1.0.0",
                PluginKind.Provider,
                "Fake provider",
                null,
                null,
                "/home/u/.jason/plugins/fake-provider",
                "sha256:3f2a9c1b4d5e6f708192a3b4c5d6e7f8091a2b3c4d5e6f708192a3b4c5d6e7f8",
                new PluginContractsDto([1], [1], []),
                ["echo.run"],
                new PluginEntry("main.js", "invoke"),
                new PluginCapabilitiesDto(null, null, null),
                new PluginLimitsDto(60000, 64),
                null,
                PluginStatus.Valid,
                []),
        ],
        new ReloadReportDto(
            Moment,
            SnapshotSource.Reload,
            Activated: false,
            [
                new CandidateDto("fake-provider", "fake-provider", CandidateStatus.Valid, []),
                new CandidateDto(
                    "broken",
                    null,
                    CandidateStatus.Invalid,
                    [
                        // A syntax error names its position in the file; a field problem names the field. The
                        // renderer spells both as one location without doubling the file name.
                        new PluginProblemDto("yaml_invalid", "plugin.yaml#3:1", "mapping values are not allowed here"),
                        new PluginProblemDto("id_mismatch", "id", "the manifest says 'other', the directory says 'broken'"),
                    ]),
            ],

            // A route is not a package, so it is not one of the candidates above; it is still a reason the
            // reload changed nothing, and the person reading this is the person who wrote the route.
            [new RouteProblemDto("Routes:Default", "route_plugin_unknown", "no plugin 'gone' is in the candidate set")])
        {
            // Nor is the order of a kind's subscribers: the setting is where the person goes to repair it.
            Subscriptions =
            [
                new SubscriptionProblemDto(
                    "Subscriptions:Order:workitem_failed[0]",
                    "subscription_order_ungranted",
                    "no plugin with id 'gone' is in the candidate set"),
            ],
        },
        Activated: false);
}
