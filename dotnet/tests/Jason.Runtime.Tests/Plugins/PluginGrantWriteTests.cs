using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Plugins;
using Jason.Runtime.Configuration;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Integration;
using Jason.Runtime.Tests.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Tests.Plugins;

/// <summary>
/// <c>plugin.grant</c> and <c>plugin.revoke</c>: one entry of one list under <c>Plugins:Grants:&lt;id&gt;</c>, written by
/// the runtime's own writer of the settings file, which keeps every byte it does not own, and frozen by the reload in the
/// same act. The verb is stricter than the file: what the manifest does not request is refused, where one written by hand
/// is a warning. No value of the file reaches an answer, a line or a log, since a plugin's secret sits beside its grants.
/// </summary>
public class PluginGrantWriteTests
{
    /// <summary>A secret beside the grants, as a plugin's configuration holds one; it must never be said anywhere.</summary>
    private const string Secret = "SENTINEL-PLUGIN-GRANT-SECRET";

    /// <summary>A package that asks for every capability a grant can give, a host by the word among them.</summary>
    private const string Asker = "grant-asker";

    private static readonly string AskerManifest = TestPlugins.Manifest(
        Asker,
        extra: "capabilities:\n  http:\n    hosts: [api.example.com]\n    any_granted_host: true\n  api: [read, journal.append]\n  files:\n    append: true\nsubscriptions: [workitem_failed, campaign_created]\n");

    /// <summary>The settings file the runtime starts from: layout, a comment, and a secret the writer must keep as they are.</summary>
    private static readonly string Original =
        "{\n" +
        "    // The machine's own settings.\n" +
        "    \"Dispatcher\": { \"Enabled\": false },\n" +
        $"    \"Plugins\": {{ \"Config\": {{ \"{TestPlugins.FakeProviderId}\": {{ \"token\": \"{Secret}\" }} }} }}\n" +
        "}\n";

    /// <summary>What the writer makes of <see cref="Original"/> for the first host these tests grant: one member more, in the file's own layout.</summary>
    private static readonly string WithHost =
        "{\n" +
        "    // The machine's own settings.\n" +
        "    \"Dispatcher\": { \"Enabled\": false },\n" +
        $"    \"Plugins\": {{ \"Config\": {{ \"{TestPlugins.FakeProviderId}\": {{ \"token\": \"{Secret}\" }} }}, \"Grants\": {{\"{TestPlugins.FakeProviderId}\": {{\"Http\": [\"localhost:5555\"]}}}} }}\n" +
        "}\n";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_requested_host_granted_is_written_in_force_at_once_and_every_other_byte_is_kept()
    {
        await using var api = await StartAsync();
        var reloads = (await ReloadedAsync(api)).Items.Count;

        var answer = await GrantAsync(api, TestPlugins.FakeProviderId, "http", "LOCALHOST:5555");

        Assert.Equal(["localhost:5555"], Plugin(answer, TestPlugins.FakeProviderId).Capabilities.Http!.Granted);
        Assert.Equal(["localhost:5555"], api.Resolve<PluginRegistry>().Snapshot.Find(TestPlugins.FakeProviderId)!.Grants.Http);
        Assert.Equal(WithHost, File.ReadAllText(api.Paths.UserSettingsFile));
        Assert.Equal(reloads + 1, (await ReloadedAsync(api)).Items.Count);
        var line = Assert.Single((await LinesAsync(api)).Items);
        Assert.Equal($"{TestPlugins.FakeProviderId}/http", line.Key);
        Assert.True(JsonNode.DeepEquals(Recorded(TestPlugins.FakeProviderId, "http"), line.Old));
        Assert.True(JsonNode.DeepEquals(Recorded(TestPlugins.FakeProviderId, "http", "localhost:5555"), line.New));
        Assert.DoesNotContain(Secret, Logs(api.Paths), StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, string.Concat((await LinesAsync(api)).Items.Select(item => item.New?.ToJsonString())), StringComparison.Ordinal);
    }

    /// <summary>Each capability goes to the list of its own name, and is in force when the request answers.</summary>
    [Theory]
    [InlineData("exec", "Exec", "Jason.FakeProviderCli")]
    [InlineData("env", "Env", "FAKE_TOKEN")]
    [InlineData("api", "Api", "journal.append")]
    [InlineData("subscriptions", "Subscriptions", "workitem_failed")]
    [InlineData("http", "Http", "receiver.example.org")]
    public async Task Each_capability_is_written_under_its_own_key(string capability, string key, string entry)
    {
        await using var api = await StartAsync();
        var id = capability is "exec" or "env" ? TestPlugins.FakeProviderId : Asker;

        await GrantAsync(api, id, capability, entry);

        using var settings = JsonDocument.Parse(File.ReadAllText(api.Paths.UserSettingsFile), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        Assert.Equal(entry, settings.RootElement.GetProperty("Plugins").GetProperty("Grants").GetProperty(id).GetProperty(key)[0].GetString());
        var line = Assert.Single((await LinesAsync(api)).Items);
        Assert.Equal($"{id}/{capability}", line.Key);
        Assert.True(JsonNode.DeepEquals(Recorded(id, capability, entry), line.New));
    }

    [Fact]
    public async Task A_directory_is_granted_as_written_to_a_package_that_asks_for_files()
    {
        await using var api = await StartAsync();
        var directory = Path.Combine(Path.GetTempPath(), "jason-grant-" + Guid.NewGuid().ToString("N"));

        var answer = await GrantAsync(api, Asker, "files", directory);

        Assert.Equal([directory], Plugin(answer, Asker).Capabilities.Files!.Granted);
    }

    /// <summary>
    /// What the manifest does not request is refused and nothing is written: the file would keep it as a warning that grants
    /// nothing, and a verb that answered success for it would mislead whoever ran it.
    /// </summary>
    [Theory]
    [InlineData(TestPlugins.FakeProviderId, "exec", "node", "fake-provider's manifest does not request the program 'node', so it was not granted and nothing was written; it requests: Jason.FakeProviderCli.")]
    [InlineData(TestPlugins.FakeProviderId, "http", "api.example.com", "fake-provider's manifest does not request the host 'api.example.com', so it was not granted and nothing was written; it requests: localhost:5555, 127.0.0.1:5555.")]
    [InlineData(TestPlugins.FakeProviderId, "env", "HOME", "fake-provider's manifest does not request the variable 'HOME', so it was not granted and nothing was written; it requests: FAKE_TOKEN, FAKE_OTHER.")]
    [InlineData(TestPlugins.FakeProviderId, "api", "read", "fake-provider's manifest requests no API families, so 'read' was not granted and nothing was written.")]
    [InlineData(TestPlugins.FakeProviderId, "subscriptions", "*", "fake-provider's manifest requests no journal kinds, so '*' was not granted and nothing was written.")]
    [InlineData(TestPlugins.FakeProviderId, "files", "/srv/lines", "fake-provider's manifest does not ask to write files, so no directory was granted and nothing was written.")]
    [InlineData(Asker, "api", "campaign", "grant-asker's manifest does not request the API family 'campaign', so it was not granted and nothing was written; it requests: read, journal.append.")]
    [InlineData(Asker, "http", "not a host", "'not a host' is not a host name, so it was not granted and nothing was written.")]
    public async Task An_entry_the_manifest_does_not_request_is_refused_and_nothing_is_written(string id, string capability, string entry, string message)
    {
        await using var api = await StartAsync();

        var error = await api.PostErrorAsync(Operations.PluginGrant, Body(id, capability, entry), HttpStatusCode.Conflict, Ct);

        Assert.Equal("grant_unrequested", error.Code);
        Assert.False(error.Retryable);
        Assert.Equal(message, error.Message);
        Assert.Equal(Original, File.ReadAllText(api.Paths.UserSettingsFile));
        Assert.Empty((await LinesAsync(api)).Items);
    }

    /// <summary>The word lets a person grant a host the manifest does not name, and a star still means the declared hosts alone.</summary>
    [Fact]
    public async Task Any_granted_host_admits_an_undeclared_host_and_a_star_still_means_the_declared_hosts()
    {
        await using var api = await StartAsync();

        await GrantAsync(api, Asker, "http", "*");
        var star = api.Resolve<PluginRegistry>().Snapshot.Find(Asker)!.Grants.Http;
        Assert.Equal(["api.example.com"], star);

        // A star does not cover a host only the word admits, so the host is written beside it.
        var answer = await GrantAsync(api, Asker, "http", "Receiver.Example.org");
        Assert.Equal(["api.example.com", "receiver.example.org"], Plugin(answer, Asker).Capabilities.Http!.Granted);
        Assert.Contains("\"Http\": [\"*\",\"receiver.example.org\"]", File.ReadAllText(api.Paths.UserSettingsFile), StringComparison.Ordinal);
    }

    /// <summary>A grant the file holds and the load froze is the state asked for: nothing is written, loaded or journaled.</summary>
    [Fact]
    public async Task A_grant_already_held_writes_nothing_and_journals_nothing()
    {
        await using var api = await StartAsync();
        await GrantAsync(api, TestPlugins.FakeProviderId, "http", "localhost:5555");
        var bytes = File.ReadAllBytes(api.Paths.UserSettingsFile);
        var snapshot = api.Resolve<PluginRegistry>().Snapshot.Id;

        await GrantAsync(api, TestPlugins.FakeProviderId, "http", "localhost:5555");
        await GrantAsync(api, TestPlugins.FakeProviderId, "env", "*");
        var covered = File.ReadAllBytes(api.Paths.UserSettingsFile);
        var afterStar = api.Resolve<PluginRegistry>().Snapshot.Id;
        await GrantAsync(api, TestPlugins.FakeProviderId, "env", "FAKE_OTHER");

        Assert.NotEqual(bytes, covered);
        Assert.Equal(covered, File.ReadAllBytes(api.Paths.UserSettingsFile));
        Assert.NotEqual(snapshot, afterStar);
        Assert.Equal(afterStar, api.Resolve<PluginRegistry>().Snapshot.Id);
        Assert.Equal(2, (await LinesAsync(api)).Items.Count);
    }

    /// <summary>
    /// A grant written into the file by hand and never reloaded is not in force; granting it writes nothing, since the
    /// file says it already, and puts it in force by the same request.
    /// </summary>
    [Fact]
    public async Task A_grant_written_by_hand_and_not_reloaded_is_put_in_force_without_a_write()
    {
        await using var api = await StartAsync();
        var monitor = api.Resolve<IOptionsMonitor<PluginsOptions>>();
        File.WriteAllText(api.Paths.UserSettingsFile, WithHost);
        await UntilAsync(() => monitor.CurrentValue.Grants.ContainsKey(TestPlugins.FakeProviderId));
        Assert.Empty(api.Resolve<PluginRegistry>().Snapshot.Find(TestPlugins.FakeProviderId)!.Grants.Http);

        await GrantAsync(api, TestPlugins.FakeProviderId, "http", "localhost:5555");

        Assert.Equal(WithHost, File.ReadAllText(api.Paths.UserSettingsFile));
        Assert.Equal(["localhost:5555"], api.Resolve<PluginRegistry>().Snapshot.Find(TestPlugins.FakeProviderId)!.Grants.Http);
        Assert.Single((await LinesAsync(api)).Items);
    }

    /// <summary>Taking the last entry back takes the list away, and the objects it leaves empty, giving back the bytes it started from.</summary>
    [Fact]
    public async Task Revoking_takes_the_entry_out_and_the_objects_it_leaves_empty_go_with_it()
    {
        await using var api = await StartAsync();
        await GrantAsync(api, TestPlugins.FakeProviderId, "http", "localhost:5555");
        await GrantAsync(api, TestPlugins.FakeProviderId, "http", "127.0.0.1:5555");

        await RevokeAsync(api, TestPlugins.FakeProviderId, "http", "127.0.0.1:5555");
        Assert.Equal(WithHost, File.ReadAllText(api.Paths.UserSettingsFile));
        var answer = await RevokeAsync(api, TestPlugins.FakeProviderId, "http", "LocalHost:5555");

        Assert.Empty(Plugin(answer, TestPlugins.FakeProviderId).Capabilities.Http!.Granted);
        Assert.Equal(Original, File.ReadAllText(api.Paths.UserSettingsFile));
        var last = (await LinesAsync(api)).Items[0];
        Assert.True(JsonNode.DeepEquals(Recorded(TestPlugins.FakeProviderId, "http", "localhost:5555"), last.Old));
        Assert.True(JsonNode.DeepEquals(Recorded(TestPlugins.FakeProviderId, "http"), last.New));
    }

    /// <summary>What is not granted is the state a revoke asks for: nothing is written, and the answer is a success.</summary>
    [Fact]
    public async Task Revoking_what_is_not_granted_writes_nothing_and_answers_success()
    {
        await using var api = await StartAsync();
        var snapshot = api.Resolve<PluginRegistry>().Snapshot.Id;

        await RevokeAsync(api, TestPlugins.FakeProviderId, "http", "localhost:5555");
        await RevokeAsync(api, "no-such-plugin", "env", "TOKEN");

        Assert.Equal(Original, File.ReadAllText(api.Paths.UserSettingsFile));
        Assert.Equal(snapshot, api.Resolve<PluginRegistry>().Snapshot.Id);
        Assert.Empty((await LinesAsync(api)).Items);
    }

    /// <summary>
    /// An entry a star grants is not taken back by rewriting the star into the names it stands for today; the star itself
    /// is taken back alone, leaving what is named beside it.
    /// </summary>
    [Fact]
    public async Task Revoking_an_entry_a_star_covers_is_refused_and_the_star_is_taken_back_alone()
    {
        await using var api = await StartAsync();
        await GrantAsync(api, Asker, "http", "*");
        await GrantAsync(api, Asker, "http", "receiver.example.org");
        var bytes = File.ReadAllBytes(api.Paths.UserSettingsFile);

        var error = await api.PostErrorAsync(Operations.PluginRevoke, Body(Asker, "http", "api.example.com"), HttpStatusCode.Conflict, Ct);

        Assert.Equal("grant_held_by_star", error.Code);
        Assert.Equal(
            "Plugins:Grants:grant-asker:Http grants '*', which covers 'api.example.com', so nothing was taken back: take back '*' with jason plugin revoke grant-asker --http '*', then grant what it should keep.",
            error.Message);
        Assert.Equal(bytes, File.ReadAllBytes(api.Paths.UserSettingsFile));

        // A host only the word admits is no star's, so it goes; then the star goes alone.
        await RevokeAsync(api, Asker, "http", "receiver.example.org");
        await GrantAsync(api, Asker, "http", "receiver.example.org");
        var answer = await RevokeAsync(api, Asker, "http", "*");
        Assert.Equal(["receiver.example.org"], Plugin(answer, Asker).Capabilities.Http!.Granted);
    }

    /// <summary>
    /// The revoke was written; the reload refused it, because an order names the plugin for the kind it took back. The
    /// file is put back byte for byte, the grant in force stays, and the answer says so in the grant's words.
    /// </summary>
    [Fact]
    public async Task A_reload_the_revoke_makes_refuse_puts_the_file_back_and_says_so()
    {
        await using var api = await StartAsync();
        await GrantAsync(api, Asker, "subscriptions", "workitem_failed");
        File.WriteAllText(
            api.Paths.UserSettingsFile,
            File.ReadAllText(api.Paths.UserSettingsFile).Replace(
                "\"Dispatcher\": { \"Enabled\": false },",
                $"\"Dispatcher\": {{ \"Enabled\": false }},\n    \"Subscriptions\": {{ \"Order\": {{ \"workitem_failed\": [\"{Asker}\"] }} }},",
                StringComparison.Ordinal));
        var bytes = File.ReadAllText(api.Paths.UserSettingsFile);
        var snapshot = api.Resolve<PluginRegistry>().Snapshot.Id;

        var error = await api.PostErrorAsync(Operations.PluginRevoke, Body(Asker, "subscriptions", "workitem_failed"), HttpStatusCode.Conflict, Ct);

        Assert.Equal("plugin_reload_rejected", error.Code);
        Assert.Matches(@"^The plugins were not loaded again: \d+ problem\(s\), so the grant was not changed: the settings file was put back as it was\.$", error.Message);
        Assert.Contains(error.Details!, detail => detail.Code == "subscription_order_ungranted");
        Assert.Equal(("settings.json", "settings_file_put_back", "The settings file was put back as it was; the grants in force are unchanged."), (error.Details![^1].Field, error.Details[^1].Code, error.Details[^1].Message));
        Assert.Equal(bytes, File.ReadAllText(api.Paths.UserSettingsFile));
        Assert.Equal(snapshot, api.Resolve<PluginRegistry>().Snapshot.Id);
        Assert.Single((await LinesAsync(api)).Items);
    }

    /// <summary>A directory the load would refuse is refused before anything is written, with the load's own sentence.</summary>
    [Fact]
    public async Task A_directory_the_load_would_refuse_is_refused_before_anything_is_written()
    {
        await using var api = await StartAsync();

        var error = await api.PostErrorAsync(Operations.PluginGrant, Body(Asker, "files", "relative/lines"), HttpStatusCode.BadRequest, Ct);
        var star = await api.PostErrorAsync(Operations.PluginGrant, Body(Asker, "files", "*"), HttpStatusCode.BadRequest, Ct);

        Assert.Equal(("entry", "grant_path_refused"), (error.Details![0].Field, error.Details[0].Code));
        Assert.Equal(("entry", "not_allowed"), (star.Details![0].Field, star.Details[0].Code));
        Assert.Equal(Original, File.ReadAllText(api.Paths.UserSettingsFile));
    }

    [Fact]
    public async Task A_grant_to_a_plugin_that_is_not_loaded_is_refused_and_a_revoke_for_one_takes_its_entry_out()
    {
        await using var api = await StartAsync(Original.Replace("\"Config\"", "\"Grants\": { \"gone\": { \"Env\": [\"TOKEN\"] } }, \"Config\"", StringComparison.Ordinal));

        var error = await api.PostErrorAsync(Operations.PluginGrant, Body("gone", "env", "TOKEN"), HttpStatusCode.NotFound, Ct);
        Assert.Equal("plugin_not_found", error.Code);
        Assert.Equal("No plugin 'gone' is loaded, so nothing was granted; jason plugin list names the ones that are, and a package copied in loads at jason plugin reload.", error.Message);

        await RevokeAsync(api, "gone", "env", "TOKEN");
        Assert.Equal(Original, File.ReadAllText(api.Paths.UserSettingsFile));
    }

    /// <summary>
    /// A grant's reload refused after the write says what became of the file in the route writer's forms, with a grant's
    /// words: the grants in force, the grant this wrote or took back, and the hand edit that mends a file not put back.
    /// </summary>
    [Theory]
    [InlineData(SettingsPutBack.PutBack, false, "settings_file_put_back", "The settings file was put back as it was; the grants in force are unchanged.", "the settings file was put back as it was.")]
    [InlineData(SettingsPutBack.ChangedSince, true, "settings_file_not_put_back", "The settings file was changed by another writer after this one wrote it, so it was left as that writer left it; the grants in force are unchanged.", "the settings file was changed by another writer after this one wrote it, so it was left as that writer left it.")]
    [InlineData(SettingsPutBack.Held, false, "settings_file_not_put_back", "Another program held the settings file open, so it was not put back: it holds the grant this wrote, which is not in force; the grants in force are unchanged. Take the grant out of the file by hand, or fix what the load refused and run jason plugin reload.", "another program held the settings file open, so it was not put back: it holds the grant this wrote, which is not in force; take the grant out of the file by hand, or fix what the load refused and run jason plugin reload.")]
    [InlineData(SettingsPutBack.NotWritable, true, "settings_file_not_put_back", "The settings file could not be written again, so it was not put back: the grant this took back is gone from the file and still in force; the grants in force are unchanged. Put the grant back into the file by hand, or fix what the load refused and run jason plugin reload.", "the settings file could not be written again, so it was not put back: the grant this took back is gone from the file and still in force; put the grant back into the file by hand, or fix what the load refused and run jason plugin reload.")]
    public void A_refused_reload_after_a_grant_says_what_became_of_the_file(SettingsPutBack putBack, bool takenBack, string code, string detail, string clause)
    {
        var rejected = new DomainException(409, "plugin_reload_rejected", "The plugins were not loaded again.", false, [new ErrorDetail("plugins/x", "manifest_invalid", "A package problem.")]);

        var said = RouteService.PutBackSaid(rejected, putBack, takenBack ? GlobalRouteWrite.TakenAway : GlobalRouteWrite.Added, PluginGrantService.GrantNouns);

        Assert.Equal($"The plugins were not loaded again: 1 problem(s), so the grant was not changed: {clause}", said.Message);
        var last = said.Details![^1];
        Assert.Equal((PluginLoader.SettingsFileField, code, detail), (last.Field, last.Code, last.Message));
    }

    /// <summary>A list a <c>JASON_</c> variable sets one element of is the variable's: the file's list is not the one in force.</summary>
    [Fact]
    public void A_list_an_environment_variable_sets_one_element_of_is_shadowed()
    {
        var prefix = "JASONTEST" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant() + "_";
        Environment.SetEnvironmentVariable(prefix + "Plugins__Grants__fake-provider__Http__0", "localhost:5555");
        try
        {
            var file = new SettingsFile(new ConfigurationBuilder().AddEnvironmentVariables(prefix).Build(), prefix);

            Assert.Equal(prefix + "Plugins__Grants__fake-provider__Http__0", file.Shadowing("Plugins:Grants:fake-provider:Http"));
            Assert.Null(file.Shadowing("Plugins:Grants:fake-provider:Env"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(prefix + "Plugins__Grants__fake-provider__Http__0", null);
        }
    }

    private static object Body(string id, string capability, string entry) =>
        new { plugin_id = id, capability, entry, reason = "granted for the test" };

    private static Task<PluginRegistryDto> GrantAsync(RuntimeApiFixture api, string id, string capability, string entry) =>
        api.PostOkAsync<PluginRegistryDto>(Operations.PluginGrant, Body(id, capability, entry), Ct);

    private static Task<PluginRegistryDto> RevokeAsync(RuntimeApiFixture api, string id, string capability, string entry) =>
        api.PostOkAsync<PluginRegistryDto>(Operations.PluginRevoke, Body(id, capability, entry), Ct);

    private static PluginDto Plugin(PluginRegistryDto registry, string id) => registry.Plugins.Single(plugin => plugin.Id == id);

    private static JsonObject Recorded(string id, string capability, params string[] granted) => new()
    {
        ["plugin"] = id,
        ["capability"] = capability,
        ["granted"] = new JsonArray([.. granted.Select(name => (JsonNode)JsonValue.Create(name))]),
    };

    private static Task<Page<JournalEntryDto>> LinesAsync(RuntimeApiFixture api) =>
        api.PostOkAsync<Page<JournalEntryDto>>(Operations.JournalList, new { Kind = JournalKinds.PluginGrantsUpdated }, Ct);

    private static Task<Page<JournalEntryDto>> ReloadedAsync(RuntimeApiFixture api) =>
        api.PostOkAsync<Page<JournalEntryDto>>(Operations.JournalList, new { Kind = JournalKinds.PluginsReloaded }, Ct);

    private static string Logs(JasonPaths paths) =>
        Directory.Exists(paths.LogsDirectory)
            ? string.Concat(Directory.GetFiles(paths.LogsDirectory).Select(FakeHostRuntime.ReadShared))
            : string.Empty;

    private static async Task UntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25, Ct);
        }
    }

    private static Task<RuntimeApiFixture> StartAsync(string? settings = null) =>
        RouteActivationTests.StartAsync(prepare: paths =>
        {
            TestPlugins.InstallFakeProvider(paths);
            TestPlugins.Write(paths, Asker, AskerManifest, "export function invoke() {}");
            File.WriteAllText(paths.UserSettingsFile, settings ?? Original, new UTF8Encoding(false));
        });
}
