using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Plugins;
using Jason.Runtime.Configuration;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Integration;
using Jason.Runtime.Tests.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Tests.Routing;

/// <summary>
/// The global scope of <c>route.set</c> and <c>route.unset</c>: the route is written to the settings file by the
/// runtime's own writer, which keeps every byte it does not own, and frozen by the reload in the same act, so it is in
/// force when the request answers; a reload that refuses puts the file back, and the answer says whether it did. No value
/// of the file reaches a log line or an error message, since a plugin's secret sits beside the route.
/// </summary>
public class GlobalRouteWriteTests
{
    /// <summary>A secret beside the route, as a plugin's configuration holds one; it must never be said anywhere.</summary>
    private const string Secret = "SENTINEL-GLOBAL-ROUTE-SECRET";

    /// <summary>The settings file the runtime starts from: layout, a comment, and a secret the writer must keep as they are.</summary>
    private static readonly string Original =
        "{\n" +
        "    // The machine's own settings.\n" +
        "    \"Dispatcher\": { \"Enabled\": false },\n" +
        $"    \"Plugins\": {{ \"Config\": {{ \"{TestPlugins.FakeProviderId}\": {{ \"token\": \"{Secret}\" }} }} }}\n" +
        "}\n";

    /// <summary>What the writer makes of <see cref="Original"/> for the route these tests set: one member more, in the file's own layout.</summary>
    private static readonly string WithRoute =
        "{\n" +
        "    // The machine's own settings.\n" +
        "    \"Dispatcher\": { \"Enabled\": false },\n" +
        $"    \"Plugins\": {{ \"Config\": {{ \"{TestPlugins.FakeProviderId}\": {{ \"token\": \"{Secret}\" }} }} }},\n" +
        "    \"Routes\": {\n" +
        "        \"Operations\": {\n" +
        "            \"campaign.get\": {\n" +
        "                \"Plugin\": \"fake-provider\",\n" +
        "                \"Binding\": {\"workspace\":\"west\"}\n" +
        "            }\n" +
        "        }\n" +
        "    }\n" +
        "}\n";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_global_route_set_is_written_to_the_settings_file_and_in_force_when_it_answers()
    {
        await using var api = await StartAsync();
        var reloads = (await ReloadedAsync(api)).Items.Count;

        var answer = await api.PostOkAsync<RoutesDto>(Operations.RouteSet, Global("campaign.get", RouteActivationTests.Workspace()), Ct);

        var route = api.Resolve<RouteRegistry>().Snapshot.Global.Operations["campaign.get"];
        Assert.Equal(TestPlugins.FakeProviderId, route.PluginId);
        Assert.True(JsonNode.DeepEquals(RouteActivationTests.Workspace(), route.Binding));
        Assert.Equal(api.Resolve<RouteRegistry>().Snapshot.Id, answer.RoutingSnapshotId);
        Assert.Equal(TestPlugins.FakeProviderId, answer.Global.Operations["campaign.get"].PluginId);

        // Every byte the writer does not own is kept: the comment, the layout, the secret.
        Assert.Equal(WithRoute, File.ReadAllText(api.Paths.UserSettingsFile));

        Assert.Equal(reloads + 1, (await ReloadedAsync(api)).Items.Count);
        var line = Assert.Single((await LinesAsync(api)).Items);
        Assert.Equal("campaign.get", line.Key);
        Assert.Null(line.CampaignId);
        Assert.Null(line.Old);
        Assert.True(JsonNode.DeepEquals(
            new JsonObject { ["plugin"] = TestPlugins.FakeProviderId, ["binding"] = RouteActivationTests.Workspace() },
            line.New));
        Assert.Empty(TemporaryFiles(api.Paths));
        Assert.DoesNotContain(Secret, Logs(api.Paths), StringComparison.Ordinal);
    }

    /// <summary>A route the file holds already and the active routes hold too: nothing is written, activated or journaled.</summary>
    [Fact]
    public async Task Setting_the_route_in_force_again_writes_nothing_and_activates_nothing()
    {
        await using var api = await StartAsync();
        await api.PostOkAsync<RoutesDto>(Operations.RouteSet, Global("campaign.get", RouteActivationTests.Workspace()), Ct);
        var bytes = File.ReadAllBytes(api.Paths.UserSettingsFile);
        var snapshot = api.Resolve<RouteRegistry>().Snapshot.Id;

        var answer = await api.PostOkAsync<RoutesDto>(Operations.RouteSet, Global("campaign.get", RouteActivationTests.Workspace()), Ct);

        Assert.Equal(bytes, File.ReadAllBytes(api.Paths.UserSettingsFile));
        Assert.Equal(snapshot, answer.RoutingSnapshotId);
        Assert.Equal(snapshot, api.Resolve<RouteRegistry>().Snapshot.Id);
        Assert.Single((await LinesAsync(api)).Items);
    }

    /// <summary>
    /// A route written into the file by hand and never reloaded is not in force; setting it writes nothing, since the file
    /// says it already, and puts it in force by the same request.
    /// </summary>
    [Fact]
    public async Task A_route_the_file_holds_and_no_reload_froze_is_put_in_force_by_the_same_set()
    {
        await using var api = await StartAsync();
        var monitor = api.Resolve<IOptionsMonitor<RoutesOptions>>();
        File.WriteAllText(api.Paths.UserSettingsFile, WithRoute);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!monitor.CurrentValue.Operations.ContainsKey("campaign.get") && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25, Ct);
        }

        var bytes = File.ReadAllBytes(api.Paths.UserSettingsFile);
        Assert.DoesNotContain("campaign.get", api.Resolve<RouteRegistry>().Snapshot.Global.Operations.Keys);

        await api.PostOkAsync<RoutesDto>(Operations.RouteSet, Global("campaign.get", RouteActivationTests.Workspace()), Ct);

        Assert.Equal(bytes, File.ReadAllBytes(api.Paths.UserSettingsFile));
        Assert.Equal(TestPlugins.FakeProviderId, api.Resolve<RouteRegistry>().Snapshot.Global.Operations["campaign.get"].PluginId);
        var line = Assert.Single((await LinesAsync(api)).Items);
        Assert.Null(line.Old);
    }

    /// <summary>
    /// The other way round: the route is in force and a hand edit took it out of the file without a reload. Setting it
    /// writes it back and reloads, and since the reload leaves the route in force as it was, no line says it changed.
    /// </summary>
    [Fact]
    public async Task A_write_that_leaves_the_route_in_force_as_it_was_journals_no_change()
    {
        await using var api = await StartAsync();
        await api.PostOkAsync<RoutesDto>(Operations.RouteSet, Global("campaign.get", RouteActivationTests.Workspace()), Ct);
        var reloads = (await ReloadedAsync(api)).Items.Count;
        var monitor = api.Resolve<IOptionsMonitor<RoutesOptions>>();
        File.WriteAllText(api.Paths.UserSettingsFile, Original);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (monitor.CurrentValue.Operations.ContainsKey("campaign.get") && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25, Ct);
        }

        await api.PostOkAsync<RoutesDto>(Operations.RouteSet, Global("campaign.get", RouteActivationTests.Workspace()), Ct);

        Assert.Equal(WithRoute, File.ReadAllText(api.Paths.UserSettingsFile));
        Assert.Equal(reloads + 1, (await ReloadedAsync(api)).Items.Count);
        Assert.Single((await LinesAsync(api)).Items);
    }

    /// <summary>
    /// The route was checked against the active plugins and held, so it was written; the reload over the packages as they
    /// are now refused, so the file is put back byte for byte, the routes in force stay, and nothing is journaled.
    /// </summary>
    [Fact]
    public async Task A_reload_that_refuses_after_the_write_puts_the_file_back_byte_for_byte()
    {
        await using var api = await StartAsync();
        var snapshot = api.Resolve<RouteRegistry>().Snapshot.Id;
        File.WriteAllText(Path.Combine(api.Paths.PluginPackageDirectory(TestPlugins.FakeProviderId), "plugin.yaml"), "manifest_version: [not, a, number]\n");

        var error = await api.PostErrorAsync(Operations.RouteSet, Global("campaign.get", RouteActivationTests.Workspace()), HttpStatusCode.Conflict, Ct);

        Assert.Equal("plugin_reload_rejected", error.Code);
        Assert.Matches(@"^The plugins were not loaded again: \d+ problem\(s\), so the route was not written: the settings file was put back as it was\.$", error.Message);
        var last = error.Details![^1];
        Assert.Equal("settings.json", last.Field);
        Assert.Equal("settings_file_put_back", last.Code);
        Assert.Equal("The settings file was put back as it was; the routes in force are unchanged.", last.Message);
        Assert.Equal(Original, File.ReadAllText(api.Paths.UserSettingsFile));
        Assert.Equal(snapshot, api.Resolve<RouteRegistry>().Snapshot.Id);
        Assert.Empty((await LinesAsync(api)).Items);
        Assert.Empty(TemporaryFiles(api.Paths));
        Assert.DoesNotContain(Secret, error.Message + string.Concat(error.Details.Select(detail => detail.Message)), StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, Logs(api.Paths), StringComparison.Ordinal);
    }

    /// <summary>A file the parser stops in is not edited, and the refusal says where, by line and byte, without an exception's name.</summary>
    [Fact]
    public async Task A_settings_file_that_does_not_parse_is_refused_with_its_line_and_byte_and_nothing_written()
    {
        await using var api = await StartAsync();
        var broken = "{\n  \"Dispatcher\": { \"Enabled\": false },\n  \"" + Secret + "\": nope\n}\n";
        File.WriteAllText(api.Paths.UserSettingsFile, broken);

        var error = await api.PostErrorAsync(Operations.RouteSet, Global("campaign.get", RouteActivationTests.Workspace()), HttpStatusCode.Conflict, Ct);

        Assert.Equal("settings_file_unreadable", error.Code);
        Assert.False(error.Retryable);
        Assert.Matches(@"^The settings file could not be read \(at line 3, byte \d+\), so nothing was written; fix it, then run the command again\.$", error.Message);
        Assert.DoesNotContain("Exception", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, error.Message, StringComparison.Ordinal);
        Assert.Equal(broken, File.ReadAllText(api.Paths.UserSettingsFile));
        Assert.Empty((await LinesAsync(api)).Items);
    }

    /// <summary>
    /// Another writer that changes the file between this writer's read and its commit is never overwritten: the act is made
    /// again from a fresh read, which keeps that writer's change, and after <see cref="SettingsWriter.Attempts"/> such
    /// changes the write is refused as one to send again, with the other writer's file left as it left it.
    /// </summary>
    [Theory]
    [InlineData(1, true)]
    [InlineData(SettingsWriter.Attempts, false)]
    public async Task A_change_by_another_writer_between_the_read_and_the_commit_is_kept(int changes, bool written)
    {
        await using var api = await StartAsync();
        var path = api.Paths.UserSettingsFile;
        var writer = new SettingsWriter(api.Resolve<SettingsFile>(), api.Paths, NullLogger<SettingsWriter>.Instance, attempt =>
        {
            if (attempt <= changes)
            {
                File.WriteAllText(path, Original.Replace("\"Enabled\": false", $"\"Enabled\": false, \"TickSeconds\": {attempt + 10}", StringComparison.Ordinal));
            }
        });

        Task<bool> Act() => writer.WriteAsync(
            [SettingsEdit.Set("Plugins:Grants:fake-provider:Http", new JsonArray("example.com"))],
            "Plugins",
            (wrote, _) => Task.FromResult(wrote),
            (refused, _) => refused,
            Ct);

        if (written)
        {
            Assert.True(await Act());
            var text = File.ReadAllText(path);
            Assert.Contains("\"TickSeconds\": 11", text, StringComparison.Ordinal);
            Assert.Contains("\"Grants\":", text, StringComparison.Ordinal);
        }
        else
        {
            var refused = await Assert.ThrowsAsync<DomainException>(Act);
            Assert.Equal("settings_file_changed", refused.Code);
            Assert.True(refused.Retryable);
            Assert.Contains($"\"TickSeconds\": {SettingsWriter.Attempts + 10}", File.ReadAllText(path), StringComparison.Ordinal);
            Assert.DoesNotContain("Grants", File.ReadAllText(path), StringComparison.Ordinal);
        }

        Assert.Empty(TemporaryFiles(api.Paths));
    }

    /// <summary>
    /// Another writer that changes the file after this one wrote it and before the put-back is not overwritten either: the
    /// file is left as that writer left it, and the refusal says so.
    /// </summary>
    [Fact]
    public async Task A_put_back_never_writes_over_a_change_another_writer_made_after_the_write()
    {
        await using var api = await StartAsync();
        var path = api.Paths.UserSettingsFile;
        var theirs = Original.Replace("\"Enabled\": false", "\"Enabled\": false, \"TickSeconds\": 12", StringComparison.Ordinal);
        SettingsPutBack? outcome = null;

        var refused = await Assert.ThrowsAsync<DomainException>(() => api.Resolve<SettingsWriter>().WriteAsync<bool>(
            [SettingsEdit.Set("Plugins:Grants:fake-provider:Http", new JsonArray("example.com"))],
            "Plugins",
            (_, _) =>
            {
                File.WriteAllText(path, theirs);
                throw new DomainException(409, "refused_for_the_test", "Refused for the test.", false);
            },
            (domain, putBack) =>
            {
                outcome = putBack;
                return domain;
            },
            Ct));

        Assert.Equal("refused_for_the_test", refused.Code);
        Assert.Equal(SettingsPutBack.ChangedSince, outcome);
        Assert.Equal(theirs, File.ReadAllText(path));
    }

    /// <summary>
    /// A file another program holds open when the put-back comes is left holding what this wrote, and the refusal says
    /// which: the file is not changed, and the caller is told it was held.
    /// </summary>
    [Fact]
    public async Task A_put_back_the_file_is_held_against_leaves_it_as_written_and_says_held()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses a read of a file another handle holds unshared.");
        await using var api = await StartAsync();
        var path = api.Paths.UserSettingsFile;
        SettingsPutBack? outcome = null;
        FileStream? held = null;
        try
        {
            await Assert.ThrowsAsync<DomainException>(() => api.Resolve<SettingsWriter>().WriteAsync<bool>(
                [SettingsEdit.Set("Plugins:Grants:fake-provider:Http", new JsonArray("example.com"))],
                "Plugins",
                (_, _) =>
                {
                    held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                    throw new DomainException(409, "refused_for_the_test", "Refused for the test.", false);
                },
                (domain, putBack) =>
                {
                    outcome = putBack;
                    return domain;
                },
                Ct));
        }
        finally
        {
            held?.Dispose();
        }

        Assert.Equal(SettingsPutBack.Held, outcome);
        Assert.Contains("\"Grants\":", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Empty(TemporaryFiles(api.Paths));
    }

    /// <summary>
    /// What a refusal says of the file after each put-back. Where the file was held, no command mends it while loads refuse
    /// — the same command again writes nothing, and the opposite one is put back — so it says the hand edit that does: a
    /// set's route taken out, an unset's route put back, or the reload that makes the file true.
    /// </summary>
    [Theory]
    [InlineData(SettingsPutBack.PutBack, false, "settings_file_put_back", "The settings file was put back as it was; the routes in force are unchanged.", "the settings file was put back as it was.")]
    [InlineData(SettingsPutBack.ChangedSince, false, "settings_file_not_put_back", "The settings file was changed by another writer after this one wrote it, so it was left as that writer left it; the routes in force are unchanged.", "the settings file was changed by another writer after this one wrote it, so it was left as that writer left it.")]
    [InlineData(SettingsPutBack.Held, false, "settings_file_not_put_back", "Another program held the settings file open, so it was not put back: it holds the route this wrote, which is not in force; the routes in force are unchanged. Take the route out of the file by hand, or fix what the load refused and run jason plugin reload.", "another program held the settings file open, so it was not put back: it holds the route this wrote, which is not in force; take the route out of the file by hand, or fix what the load refused and run jason plugin reload.")]
    [InlineData(SettingsPutBack.Held, true, "settings_file_not_put_back", "Another program held the settings file open, so it was not put back: the route this took away is gone from the file and still in force; the routes in force are unchanged. Put the route back into the file by hand, or fix what the load refused and run jason plugin reload.", "another program held the settings file open, so it was not put back: the route this took away is gone from the file and still in force; put the route back into the file by hand, or fix what the load refused and run jason plugin reload.")]
    public void A_refused_reload_says_what_became_of_the_file(SettingsPutBack putBack, bool takenAway, string code, string detail, string clause)
    {
        var rejected = new DomainException(409, "plugin_reload_rejected", "The plugins were not loaded again.", false, [new ErrorDetail("plugins/x", "manifest_invalid", "A package problem.")]);

        var said = RouteService.PutBackSaid(rejected, putBack, takenAway);

        Assert.Equal("plugin_reload_rejected", said.Code);
        Assert.Equal("The plugins were not loaded again: 1 problem(s), so the route was not written: " + clause, said.Message);
        Assert.Equal(2, said.Details!.Count);
        Assert.Equal(("settings.json", code, detail), (said.Details[^1].Field, said.Details[^1].Code, said.Details[^1].Message));
    }

    /// <summary>A setting an environment variable gives a value is not written to the file, where it would never be in force.</summary>
    [Fact]
    public void A_setting_an_environment_variable_holds_is_named_as_shadowed()
    {
        var prefix = "JASONTEST" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant() + "_";
        Environment.SetEnvironmentVariable(prefix + "Routes__Default__Plugin", "elsewhere");
        try
        {
            var configuration = new ConfigurationBuilder().AddEnvironmentVariables(prefix).Build();
            var file = new SettingsFile(configuration);

            Assert.True(file.Shadowed("Routes:Default:Plugin"));
            Assert.True(file.Shadowed("routes:default"));
            Assert.False(file.Shadowed("Routes:Operations:campaign.get:Plugin"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(prefix + "Routes__Default__Plugin", null);
        }
    }

    [Fact]
    public void Shadowed_names_the_variable_that_holds_the_setting()
    {
        var refused = DomainErrors.SettingShadowed("Routes:Default:Plugin");

        Assert.Equal("setting_shadowed", refused.Code);
        Assert.Contains("JASON_Routes__Default__Plugin", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A global route names no campaign, and a campaign's route still needs one: the scope is never guessed from what is
    /// missing.
    /// </summary>
    [Fact]
    public async Task The_scope_and_a_campaign_are_never_given_together_and_a_campaigns_route_still_needs_one()
    {
        await using var api = await StartAsync();

        var both = await api.PostErrorAsync(
            Operations.RouteSet,
            new { scope = "global", campaign_id = "cmp_01ARZ3NDEKTSV4RRFFQ69G5FAV", operation = "campaign.get", plugin = TestPlugins.FakeProviderId, binding = RouteActivationTests.Workspace() },
            HttpStatusCode.BadRequest,
            Ct);
        var detail = Assert.Single(both.Details!);
        Assert.Equal(("campaign_id", "not_allowed"), (detail.Field, detail.Code));
        Assert.Equal("campaign_id cannot be given with scope global: a global route is every campaign's; leave scope out to route one campaign.", detail.Message);

        var neither = await api.PostErrorAsync(
            Operations.RouteSet,
            new { operation = "campaign.get", plugin = TestPlugins.FakeProviderId, binding = RouteActivationTests.Workspace() },
            HttpStatusCode.BadRequest,
            Ct);
        Assert.Contains(neither.Details!, refusal => refusal is { Field: "campaign_id", Code: "required" });

        Assert.Equal(Original, File.ReadAllText(api.Paths.UserSettingsFile));
    }

    /// <summary>A binding the settings file would read back as something else is refused rather than frozen as what was not sent.</summary>
    [Theory]
    [InlineData("""{"workspace":"12345"}""")]
    [InlineData("""{"workspace":"true"}""")]
    [InlineData("""{"workspace":"west","extra":{}}""")]
    [InlineData("""{"workspace":"west","a:b":"c"}""")]
    public async Task A_binding_the_settings_file_cannot_keep_as_written_is_refused(string binding)
    {
        await using var api = await StartAsync();

        var error = await api.PostErrorAsync(Operations.RouteSet, Global("campaign.get", JsonNode.Parse(binding)!.AsObject()), HttpStatusCode.BadRequest, Ct);

        var detail = Assert.Single(error.Details!);
        Assert.Equal(("binding", "not_representable_globally"), (detail.Field, detail.Code));
        Assert.Equal(Original, File.ReadAllText(api.Paths.UserSettingsFile));
    }

    [Fact]
    public async Task A_global_route_unset_takes_it_away_and_leaves_the_rest_of_the_file()
    {
        await using var api = await StartAsync();
        await api.PostOkAsync<RoutesDto>(Operations.RouteSet, Global("campaign.get", RouteActivationTests.Workspace()), Ct);

        await api.PostOkAsync<RoutesDto>(Operations.RouteUnset, new { scope = "global", operation = "campaign.get" }, Ct);

        Assert.DoesNotContain("campaign.get", api.Resolve<RouteRegistry>().Snapshot.Global.Operations.Keys);
        var written = File.ReadAllText(api.Paths.UserSettingsFile);
        Assert.DoesNotContain("campaign.get", written, StringComparison.Ordinal);
        Assert.Contains(Secret, written, StringComparison.Ordinal);
        Assert.Contains("// The machine's own settings.", written, StringComparison.Ordinal);
        var lines = (await LinesAsync(api)).Items;
        Assert.Equal(2, lines.Count);
        Assert.Contains(lines, line => line.New is null && line.Old is not null);
    }

    /// <summary>
    /// Every refusal that finds nothing routed names the two ways to route it, a campaign's and every campaign's, as the
    /// operation that writes each: a global route is no longer something to write by hand.
    /// </summary>
    [Theory]
    [InlineData(Operations.RouteResolve, "campaign.get")]
    [InlineData(Operations.OperationDescribe, "campaign.get")]
    [InlineData(Operations.AudienceAvailability, null)]
    public async Task A_refusal_that_finds_nothing_routed_names_the_global_scope_of_route_set(string operation, string? routed)
    {
        await using var api = await StartAsync();
        var campaign = (await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "Routed nowhere" }, Ct)).Id;
        object body = routed is null ? new { campaign_id = campaign } : new { campaign_id = campaign, operation = routed };

        var error = await api.PostErrorAsync(operation, body, HttpStatusCode.NotFound, Ct);

        Assert.Equal("no_route", error.Code);
        Assert.EndsWith("Write one with route.set, or a global one with route.set and scope global.", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("settings file", error.Message, StringComparison.Ordinal);
    }

    /// <summary>A route that is in neither the file nor the routes in force is the state asked for.</summary>
    [Fact]
    public async Task Unsetting_a_global_route_there_is_not_writes_nothing()
    {
        await using var api = await StartAsync();
        var snapshot = api.Resolve<RouteRegistry>().Snapshot.Id;

        var answer = await api.PostOkAsync<RoutesDto>(Operations.RouteUnset, new { scope = "global", operation = "campaign.get" }, Ct);

        Assert.Equal(snapshot, answer.RoutingSnapshotId);
        Assert.Equal(Original, File.ReadAllText(api.Paths.UserSettingsFile));
        Assert.Empty((await LinesAsync(api)).Items);
    }

    /// <summary>
    /// The temporary file takes the settings file's own mode from its first byte, so a file only its owner may read is never
    /// readable by anyone else in between, and the file keeps that mode after the rename.
    /// </summary>
    [Fact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public async Task The_settings_file_keeps_its_owner_only_mode_through_a_write()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "A Unix file mode; on Windows the file takes its folder's permissions.");
        await using var api = await StartAsync();
        File.SetUnixFileMode(api.Paths.UserSettingsFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        await api.PostOkAsync<RoutesDto>(Operations.RouteSet, Global("campaign.get", RouteActivationTests.Workspace()), Ct);

        Assert.Contains("campaign.get", File.ReadAllText(api.Paths.UserSettingsFile), StringComparison.Ordinal);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(api.Paths.UserSettingsFile));
    }

    private static object Global(string operation, JsonObject binding) =>
        new { scope = "global", operation, plugin = TestPlugins.FakeProviderId, binding, reason = "routed for the test" };

    private static Task<Page<JournalEntryDto>> LinesAsync(RuntimeApiFixture api) =>
        api.PostOkAsync<Page<JournalEntryDto>>(Operations.JournalList, new { Kind = JournalKinds.GlobalRoutesUpdated }, Ct);

    private static Task<Page<JournalEntryDto>> ReloadedAsync(RuntimeApiFixture api) =>
        api.PostOkAsync<Page<JournalEntryDto>>(Operations.JournalList, new { Kind = JournalKinds.PluginsReloaded }, Ct);

    private static string[] TemporaryFiles(JasonPaths paths) => Directory.GetFiles(paths.ConfigDirectory, "*.tmp");

    private static string Logs(JasonPaths paths) =>
        Directory.Exists(paths.LogsDirectory)
            ? string.Concat(Directory.GetFiles(paths.LogsDirectory).Select(FakeHostRuntime.ReadShared))
            : string.Empty;

    private static Task<RuntimeApiFixture> StartAsync() =>
        RouteActivationTests.StartAsync(prepare: paths =>
        {
            TestPlugins.InstallFakeProvider(paths);
            File.WriteAllText(paths.UserSettingsFile, Original, new UTF8Encoding(false));
        });
}
