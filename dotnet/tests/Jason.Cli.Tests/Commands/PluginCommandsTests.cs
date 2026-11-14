using System.Net;
using System.Text.Json;
using Jason.Cli;
using Jason.Cli.Commands;
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

            fake-provider is loaded and granted only part of what it asks for. It asks to reach: localhost:5555, api.example.com. Grant them with `jason plugin grant fake-provider --http <host> --actor human:<Name>`, in force at once.
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
            fake-provider is loaded and granted only part of what it asks for. It asks to reach: localhost:5555, api.example.com. Grant them with `jason plugin grant fake-provider --http <host> --actor human:<Name>`, in force at once.
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
            "reply is loaded and granted nothing. It asks to run: reply. Grant it with `jason plugin grant reply --exec <program> --actor human:<Name>`, in force at once.",
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

        Assert.Equal(
            "reply is loaded and granted only part of what it asks for."
            + " It asks to reach: api.example.com. Grant it with `jason plugin grant reply --http <host> --actor human:<Name>`, in force at once."
            + " It asks to call: read, journal.append. Grant them with `jason plugin grant reply --api <family> --actor human:<Name>`, in force at once."
            + " It asks to be told of: workitem_failed. Grant it with `jason plugin grant reply --subscription <kind> --actor human:<Name>`, in force at once."
            + " It asks for directories to write to; grant one with `jason plugin grant reply --files <absolute path> --actor human:<Name>`.",
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
            "reply is loaded and granted nothing. It asks to read: FAKE_TOKEN. Grant it with `jason plugin grant reply --env <name> --actor human:<Name>`, in force at once.",
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
        using var cli = new CliRun(Serialize(Unavailable(granted: true)));

        await cli.RunAsync("plugin", verb, "--human");

        Assert.Equal(
            "reply unavailable: executable_missing — 'reply' was not found. executable_incompatible — 'other' is 0.1.0, older than 0.4.0.",
            Lines(cli.Text)[^1]);
        Assert.DoesNotContain("granted nothing", cli.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A plugin the machine cannot run is granted what the settings say all the same, so one granted nothing says both,
    /// its problems and then the grant, as <c>jason plugin install</c> does: the three verbs read one registry the same
    /// way, and the view install points to says the grant too.
    /// </summary>
    [Theory]
    [InlineData("list")]
    [InlineData("reload")]
    public async Task Human_mode_says_an_unavailable_plugin_s_problems_and_then_how_to_grant_it(string verb)
    {
        using var cli = new CliRun(Serialize(Unavailable(granted: false)));

        await cli.RunAsync("plugin", verb, "--human");

        Assert.Equal(
            new[]
            {
                "reply unavailable: executable_missing — 'reply' was not found. executable_incompatible — 'other' is 0.1.0, older than 0.4.0.",
                "reply is loaded and granted nothing. It asks to run: reply. Grant it with `jason plugin grant reply --exec <program> --actor human:<Name>`, in force at once.",
            },
            Lines(cli.Text)[^2..]);
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
            "With --human, each unavailable plugin's problems are named with the runtime's words, and each capability a plugin asks for and is granted nothing of is named with the jason plugin grant command that grants it, in force at once.",
            System.Text.RegularExpressions.Regex.Replace(cli.Text, @"\s+", " "),
            StringComparison.Ordinal);
    }

    private static string Serialize(PluginRegistryDto registry) => JsonSerializer.Serialize(registry, JasonJson.Options);

    private static string[] Lines(string text) => text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

    private static ExecCapabilityDto Exec(string name, bool granted) =>
        new([new ExecutableDto(name, "/usr/local/bin/" + name, null, null)], granted ? [name] : []);

    /// <summary>A registry of one plugin, <c>reply</c>, as an activated reload answers it.</summary>
    /// <summary><c>reply</c> unavailable with two problems, its one program granted or not.</summary>
    private static PluginRegistryDto Unavailable(bool granted)
    {
        var registry = OnePlugin(PluginStatus.Unavailable, new PluginCapabilitiesDto(Exec("reply", granted), null, null));
        return registry with
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
    }

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

    [Theory]
    [InlineData("--http", "http", "api.example.com")]
    [InlineData("--exec", "exec", "dotnet")]
    [InlineData("--env", "env", "FAKE_TOKEN")]
    [InlineData("--api", "api", "journal.append")]
    [InlineData("--files", "files", "/home/u/Jason/exports")]
    [InlineData("--subscription", "subscriptions", "workitem_failed")]
    public async Task Grant_sends_the_capability_its_flag_names_and_the_entry(string flag, string capability, string entry)
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("plugin", "grant", "fake-provider", flag, entry, "--reason", "it posts to the CRM", "--actor", "human:ada");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.PluginGrant,
            $"{{\"plugin_id\":\"fake-provider\",\"capability\":\"{capability}\",\"entry\":\"{entry}\",\"reason\":\"it posts to the CRM\",\"actor\":{{\"type\":\"human\",\"id\":\"ada\"}}}}");
    }

    [Fact]
    public async Task Revoke_sends_the_same_body_to_its_own_operation()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("plugin", "revoke", "fake-provider", "--http", "*", "--actor", "human:ada");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.PluginRevoke, "{\"plugin_id\":\"fake-provider\",\"capability\":\"http\",\"entry\":\"*\",\"actor\":{\"type\":\"human\",\"id\":\"ada\"}}");
    }

    [Theory]
    [InlineData("grant")]
    [InlineData("revoke")]
    public async Task No_grant_named_two_named_or_one_named_twice_is_a_usage_error_and_nothing_is_sent(string verb)
    {
        foreach (var (flags, said) in new (string[], string)[]
        {
            ([], PluginCommands.OneGrant),
            (["--http", "api.example.com", "--env", "FAKE_TOKEN"], PluginCommands.OneGrant),
            (["--env", " "], PluginCommands.OneGrant),
            (["--http", "api.example.com", "--http", "localhost:5555"], "--http"),
        })
        {
            using var cli = new CliRun();

            var exit = await cli.RunAsync(["plugin", verb, "fake-provider", .. flags, "--actor", "human:ada"]);

            Assert.Equal(ExitCodes.Usage, exit);
            Assert.Null(cli.Url);
            Assert.Contains(said, cli.Error.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task An_id_that_is_no_plugin_s_is_a_usage_error_and_nothing_is_sent()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("plugin", "grant", "Fake_Provider", "--http", "api.example.com", "--actor", "human:ada");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Null(cli.Url);
    }

    /// <summary>
    /// The first line is said from the body the runtime answered, the plugin's granted list, and never from what was asked
    /// alone: where the two disagree it says so. Hosts compare without regard to case, directories after a separator at
    /// the end is trimmed; a revoke of <c>*</c> says what a star stands for.
    /// </summary>
    [Theory]
    [InlineData("grant", "--http", "api.example.com", "api.example.com", "fake-provider is granted the host api.example.com, in force now.")]
    [InlineData("grant", "--http", "API.example.com", "api.example.com", "fake-provider is granted the host API.example.com, in force now.")]
    [InlineData("grant", "--http", "api.example.com", null, "fake-provider is still not granted the host api.example.com: the grants in force do not hold it.")]
    [InlineData("revoke", "--http", "api.example.com", null, "fake-provider is not granted the host api.example.com now; work already running keeps what it was started with.")]
    [InlineData("revoke", "--http", "API.example.com", "api.example.com", "fake-provider is still granted the host API.example.com: nothing in the file matched it.")]
    [InlineData("revoke", "--files", "/srv/lines/", "/srv/lines", "fake-provider is still granted the directory /srv/lines/: nothing in the file matched it.")]
    [InlineData("revoke", "--http", "*", "localhost:5555", "fake-provider's grant of every host its manifest names is taken back; what is granted by name stays. Work already running keeps what it was started with.")]
    public async Task Human_mode_says_the_grant_from_the_body_the_runtime_answered(string verb, string flag, string entry, string? granted, string said)
    {
        using var cli = new CliRun(JsonSerializer.Serialize(Answering(flag, granted, activated: true), JasonJson.Options));

        var exit = await cli.RunAsync("plugin", verb, "fake-provider", flag, entry, "--actor", "human:ada", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.StartsWith(said + Environment.NewLine + "activated snapshot snp_01J4 with 2 plugins", cli.Text, StringComparison.Ordinal);
    }

    /// <summary>A grant already held, and a revoke of what is not granted, activate nothing, so no snapshot line follows.</summary>
    [Theory]
    [InlineData("grant", "fake-provider is granted the host api.example.com, in force now.", "api.example.com")]
    [InlineData("revoke", "fake-provider is not granted the host api.example.com now; work already running keeps what it was started with.", null)]
    public async Task Human_mode_prints_no_activated_snapshot_for_a_request_that_activated_nothing(string verb, string said, string? granted)
    {
        using var cli = new CliRun(JsonSerializer.Serialize(Answering("--http", granted, activated: false), JasonJson.Options));

        var exit = await cli.RunAsync("plugin", verb, "fake-provider", "--http", "api.example.com", "--actor", "human:ada", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(said + Environment.NewLine, cli.Text);
    }

    /// <summary><see cref="TwoPlugins"/> with fake-provider granted one entry, or nothing, of the flag's capability.</summary>
    private static PluginRegistryDto Answering(string flag, string? granted, bool activated)
    {
        var registry = TwoPlugins();
        var plugin = registry.Plugins[0];
        IReadOnlyList<string> list = granted is null ? [] : [granted];
        var capabilities = flag switch
        {
            "--http" => plugin.Capabilities with { Http = new ListCapabilityDto(["localhost:5555", "api.example.com"], list) },
            "--files" => plugin.Capabilities with { Files = new FilesCapabilityDto(true, list) },
            _ => throw new ArgumentOutOfRangeException(nameof(flag), flag, "a flag these rows do not use."),
        };
        return registry with { Plugins = [plugin with { Capabilities = capabilities }, registry.Plugins[1]], Activated = activated };
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
