using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Contracts.Notifications;
using Jason.Runtime.Configuration;
using Jason.Runtime.Journal;
using Jason.Runtime.Notifications;
using Jason.Runtime.Tests.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Notifications;

/// <summary>
/// <c>notification.enable</c> and <c>notification.disable</c>: one notification channel — the toast, or a notification
/// plugin by its id — turned on or off through the runtime rather than by a hand edit of the settings file. Each writes the
/// file through the runtime's one writer of it, which keeps every byte it does not own, and is in force when the request
/// answers: the section is read live, so nothing is reloaded. Off writes <c>false</c>; on takes the <c>false</c> away,
/// since a channel is on unless written off. There is no switch for every channel at once.
/// </summary>
public class NotificationSwitchTests
{
    /// <summary>A plugin that speaks the delivery contract and takes any binding: ready wherever it is named.</summary>
    private const string Ready = "pager";

    /// <summary>A plugin that speaks the delivery contract and needs a binding naming a chat.</summary>
    private const string Chat = "chat-room";

    /// <summary>A provider plugin installed whose executable is missing here, so it is not loaded as valid: no channel yet.</summary>
    private const string Provider = "fake-provider";

    /// <summary>A provider plugin installed and loaded: never a channel, since it declares no delivery contract.</summary>
    private const string Other = "crm";

    /// <summary>The settings file the runtime starts from: layout, a comment and a setting of the same section the writer must keep.</summary>
    private const string Original =
        "{\n" +
        "    // The machine's own settings.\n" +
        "    \"Dispatcher\": { \"Enabled\": false },\n" +
        "    \"Notifications\": { \"QuietSeconds\": 30 }\n" +
        "}\n";

    /// <summary>What the writer makes of <see cref="Original"/> when the toast is turned off: one member more, in the file's own layout.</summary>
    private const string ToastOff =
        "{\n" +
        "    // The machine's own settings.\n" +
        "    \"Dispatcher\": { \"Enabled\": false },\n" +
        "    \"Notifications\": { \"QuietSeconds\": 30, \"Toast\": false }\n" +
        "}\n";

    private const string ToastOffAlone =
        "{\n" +
        "    \"Dispatcher\": { \"Enabled\": false },\n" +
        "    \"Notifications\": { \"Toast\": false }\n" +
        "}\n";

    private const string WithPlugins =
        "{\n" +
        "    \"Dispatcher\": { \"Enabled\": false },\n" +
        "    \"Notifications\": { \"Plugins\": { \"pager\": { \"Binding\": { \"topic\": \"jason\" } }, \"chat-room\": {} } }\n" +
        "}\n";

    /// <summary>Every notification held off as a whole, and nothing else said of them.</summary>
    private const string AllOffAlone = "{\n    \"Dispatcher\": { \"Enabled\": false },\n    \"Notifications\": { \"Enabled\": false }\n}\n";

    private static readonly object Ada = new { type = "human", id = "ada" };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Turning_the_toast_off_writes_false_keeps_every_other_byte_and_is_in_force_at_once()
    {
        await using var api = await StartAsync();

        var answer = await api.PostOkAsync<NotificationsInfo>(Operations.NotificationDisable, new { channel = "toast", actor = Ada, reason = "too many banners" }, Ct);

        Assert.False(Toast(answer).On);
        Assert.Equal(ToastOff, File.ReadAllText(api.Paths.UserSettingsFile));
        Assert.False(InForce(api).Toast);
        Assert.Equal(30, InForce(api).QuietSeconds);
        var line = Assert.Single((await LinesAsync(api)).Items);
        Assert.Equal("Notifications:Toast", line.Key);
        Assert.True(line.Old!.GetValue<bool>());
        Assert.False(line.New!.GetValue<bool>());
        Assert.Equal("too many banners", line.Reason);
        Assert.Equal(("human", "ada"), (line.Actor.Type.ToString().ToLowerInvariant(), line.Actor.Id));
        Assert.Null(line.CampaignId);
    }

    /// <summary>On is the default, so turning a channel on takes its <c>false</c> away, and a section it leaves empty with it.</summary>
    [Theory]
    [InlineData(ToastOff, Original)]
    [InlineData(ToastOffAlone, "{\n    \"Dispatcher\": { \"Enabled\": false }\n}\n")]
    public async Task Turning_the_toast_on_takes_its_false_away(string settings, string after)
    {
        await using var api = await StartAsync(settings);

        var answer = await api.PostOkAsync<NotificationsInfo>(Operations.NotificationEnable, new { channel = "toast" }, Ct);

        Assert.True(Toast(answer).On);
        Assert.Equal(after, File.ReadAllText(api.Paths.UserSettingsFile));
        Assert.True(InForce(api).Toast);
        var line = Assert.Single((await LinesAsync(api)).Items);
        Assert.False(line.Old!.GetValue<bool>());
        Assert.True(line.New!.GetValue<bool>());
    }

    /// <summary>The state already in force is the state asked for: nothing is written and the chronicle says nothing.</summary>
    [Theory]
    [InlineData(Operations.NotificationEnable, Original)]
    [InlineData(Operations.NotificationDisable, ToastOff)]
    public async Task A_channel_already_as_asked_writes_nothing_and_journals_nothing(string operation, string settings)
    {
        await using var api = await StartAsync(settings);
        var bytes = File.ReadAllBytes(api.Paths.UserSettingsFile);

        await api.PostOkAsync<NotificationsInfo>(operation, new { channel = "toast" }, Ct);

        Assert.Equal(bytes, File.ReadAllBytes(api.Paths.UserSettingsFile));
        Assert.Empty((await LinesAsync(api)).Items);
    }

    [Fact]
    public async Task A_named_plugin_channel_is_turned_off_and_on_by_its_own_enabled_and_its_binding_is_kept()
    {
        await using var api = await StartAsync(WithPlugins);

        var off = await api.PostOkAsync<NotificationsInfo>(Operations.NotificationDisable, new { channel = Ready }, Ct);

        Assert.False(Channel(off, Ready).On);
        Assert.True(Channel(off, "toast").On);
        var written = JsonNode.Parse(File.ReadAllText(api.Paths.UserSettingsFile))!["Notifications"]!["Plugins"]![Ready]!;
        Assert.False(written["Enabled"]!.GetValue<bool>());
        Assert.Equal("jason", written["Binding"]!["topic"]!.GetValue<string>());
        Assert.False(InForce(api).Plugins[Ready].Enabled);

        var on = await api.PostOkAsync<NotificationsInfo>(Operations.NotificationEnable, new { channel = Ready }, Ct);

        Assert.True(Channel(on, Ready).On);
        Assert.Equal(WithPlugins, File.ReadAllText(api.Paths.UserSettingsFile));
        Assert.True(InForce(api).Plugins[Ready].Enabled);
        var lines = (await LinesAsync(api)).Items;
        Assert.Equal(2, lines.Count);
        Assert.All(lines, line => Assert.Equal($"Notifications:Plugins:{Ready}:Enabled", line.Key));
    }

    /// <summary>
    /// An installed notification plugin that is not named under <c>Notifications:Plugins</c> is no channel: it has no
    /// binding, and the switch cannot make one. Both acts refuse it and say what is missing; nothing is written.
    /// </summary>
    [Theory]
    [InlineData(Operations.NotificationEnable)]
    [InlineData(Operations.NotificationDisable)]
    public async Task An_installed_plugin_nobody_named_is_not_configured_and_nothing_is_written(string operation)
    {
        await using var api = await StartAsync();
        var bytes = File.ReadAllBytes(api.Paths.UserSettingsFile);

        var error = await api.PostErrorAsync(operation, new { channel = Ready }, HttpStatusCode.Conflict, Ct);

        Assert.Equal("notification_channel_not_configured", error.Code);
        Assert.False(error.Retryable);
        Assert.Equal(
            $"The plugin {Ready} is a notification plugin, but nothing names it under Notifications:Plugins:{Ready} in the settings file with the binding that says where it delivers, and a switch does not write one. Name it there with its binding, then try again.",
            error.Message);
        var detail = Assert.Single(error.Details!);
        Assert.Equal(("channel", "not_named"), (detail.Field, detail.Code));
        Assert.Equal(bytes, File.ReadAllBytes(api.Paths.UserSettingsFile));
        Assert.Empty((await LinesAsync(api)).Items);
    }

    /// <summary>
    /// An installed plugin nobody named that is not a notification plugin is told so rather than advised to be named: one
    /// of another kind would only be refused again as declaring no delivery contract, and one that is not loaded cannot be
    /// told apart until it is, which no setting of a channel changes.
    /// </summary>
    [Theory]
    [InlineData(Operations.NotificationEnable, Other, NotAttemptedReasons.NotDeclared, "The plugin crm is not a notification plugin (a provider plugin declaring no version 1 of the delivery contract), so it is no notification channel, and naming it in the settings file would not make it one.")]
    [InlineData(Operations.NotificationDisable, Other, NotAttemptedReasons.NotDeclared, "The plugin crm is not a notification plugin (a provider plugin declaring no version 1 of the delivery contract), so it is no notification channel, and naming it in the settings file would not make it one.")]
    [InlineData(Operations.NotificationEnable, Provider, NotAttemptedReasons.Unavailable, "The plugin fake-provider is installed but is not loaded, or cannot run on this machine (executable_missing), so it is no notification channel yet; the list of plugins says why. Once it is loaded, a notification plugin becomes a channel when Notifications:Plugins:fake-provider in the settings file names it with its binding.")]
    public async Task An_installed_plugin_that_is_no_notification_plugin_is_told_why_not_how_to_name_it(string operation, string channel, string reason, string message)
    {
        await using var api = await StartAsync();

        var error = await api.PostErrorAsync(operation, new { channel }, HttpStatusCode.Conflict, Ct);

        Assert.Equal("notification_channel_not_configured", error.Code);
        Assert.Equal(message, error.Message);
        var detail = Assert.Single(error.Details!);
        Assert.Equal(("channel", reason), (detail.Field, detail.Code));
    }

    /// <summary>
    /// A named channel that cannot be turned on says what would change that, by its reason: a plugin that is not loaded is
    /// no setting's to mend, a plugin of another kind no setting makes a channel, and a binding is mended where it is written.
    /// Each ends without naming how it was asked, since the page asks too.
    /// </summary>
    [Theory]
    [InlineData("gone", "The notification channel gone cannot be turned on: its plugin is not loaded, or cannot run on this machine (not loaded). No setting of the channel changes that: the list of plugins says why the plugin is not loaded; once it is, try again.")]
    [InlineData(Other, "The notification channel crm cannot be turned on: its plugin is not a notification plugin (a provider plugin declaring no version 1 of the delivery contract). No setting makes it one: Notifications:Plugins:crm in the settings file names a plugin that cannot deliver a notification.")]
    public async Task A_named_channel_that_cannot_be_turned_on_says_what_would_change_that(string channel, string message)
    {
        var settings = $"{{\n    \"Dispatcher\": {{ \"Enabled\": false }},\n    \"Notifications\": {{ \"Plugins\": {{ \"{channel}\": {{ \"Enabled\": false }} }} }}\n}}\n";
        await using var api = await StartAsync(settings);

        var error = await api.PostErrorAsync(Operations.NotificationEnable, new { channel }, HttpStatusCode.Conflict, Ct);

        Assert.Equal(message, error.Message);
        Assert.DoesNotContain("command", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A channel named whose binding its plugin's own schema refuses cannot reach anybody: it is not turned on, and the
    /// refusal says what is missing, as a notification it was given would. Turning it off needs nothing of it.
    /// </summary>
    [Fact]
    public async Task A_named_channel_that_could_not_deliver_is_not_turned_on_and_says_what_is_missing()
    {
        const string ChatOff = "{\n    \"Dispatcher\": { \"Enabled\": false },\n    \"Notifications\": { \"Plugins\": { \"chat-room\": { \"Enabled\": false } } }\n}\n";
        await using var api = await StartAsync(ChatOff);
        var bytes = File.ReadAllBytes(api.Paths.UserSettingsFile);

        var error = await api.PostErrorAsync(Operations.NotificationEnable, new { channel = Chat }, HttpStatusCode.Conflict, Ct);

        Assert.Equal("notification_channel_not_configured", error.Code);
        Assert.StartsWith($"The notification channel {Chat} cannot be turned on: its binding does not satisfy the plugin's own schema (", error.Message, StringComparison.Ordinal);
        Assert.Contains("/chat", error.Message, StringComparison.Ordinal);
        Assert.EndsWith($"Mend Notifications:Plugins:{Chat}:Binding in the settings file, then try again.", error.Message, StringComparison.Ordinal);
        var detail = Assert.Single(error.Details!);
        Assert.Equal(("channel", NotAttemptedReasons.BindingInvalid), (detail.Field, detail.Code));
        Assert.Equal(bytes, File.ReadAllBytes(api.Paths.UserSettingsFile));
        Assert.Empty((await LinesAsync(api)).Items);
    }

    [Fact]
    public async Task A_named_channel_that_could_not_deliver_may_still_be_turned_off()
    {
        await using var api = await StartAsync(WithPlugins);

        var answer = await api.PostOkAsync<NotificationsInfo>(Operations.NotificationDisable, new { channel = Chat }, Ct);

        Assert.False(Channel(answer, Chat).On);
        Assert.False(InForce(api).Plugins[Chat].Enabled);
    }

    /// <summary>A name that is neither the toast nor a plugin this installation holds is no channel there could be.</summary>
    [Theory]
    [InlineData(Operations.NotificationEnable)]
    [InlineData(Operations.NotificationDisable)]
    public async Task A_name_that_is_no_channel_is_not_found(string operation)
    {
        await using var api = await StartAsync();

        var error = await api.PostErrorAsync(operation, new { channel = "nobody-installed-this" }, HttpStatusCode.NotFound, Ct);

        Assert.Equal("notification_channel_not_found", error.Code);
        Assert.Equal(
            "No notification channel is called nobody-installed-this: a channel is the toast, or a notification plugin installed in this data directory, by its id. jason runtime status --human says each on its Notify: line.",
            error.Message);
    }

    /// <summary>
    /// Every notification is held off by one setting, which no channel's switch overrides: turning a channel on is refused
    /// and the refusal names the line to change; turning one off is still written.
    /// </summary>
    [Fact]
    public async Task While_notifications_are_off_as_a_whole_a_channel_is_not_turned_on()
    {
        const string AllOff = "{\n    \"Dispatcher\": { \"Enabled\": false },\n    \"Notifications\": { \"Enabled\": false, \"Toast\": false }\n}\n";
        await using var api = await StartAsync(AllOff, allOn: false);
        var bytes = File.ReadAllBytes(api.Paths.UserSettingsFile);

        var error = await api.PostErrorAsync(Operations.NotificationEnable, new { channel = "toast" }, HttpStatusCode.Conflict, Ct);

        Assert.Equal("notifications_off", error.Code);
        Assert.Equal(
            "Every notification is off: Notifications:Enabled is false in the settings file, and no channel is turned on while it is. Take that line out of the settings file to turn notifications back on as a whole.",
            error.Message);
        Assert.Equal(bytes, File.ReadAllBytes(api.Paths.UserSettingsFile));

        await using var other = await StartAsync(AllOffAlone, allOn: false);
        var answer = await other.PostOkAsync<NotificationsInfo>(Operations.NotificationDisable, new { channel = "toast" }, Ct);
        Assert.False(answer.Enabled);
        Assert.False(InForce(other).Toast);
    }

    [Theory]
    [InlineData(null, "required")]
    [InlineData("  ", "required")]
    [InlineData("Not A Channel", "invalid")]
    public async Task A_missing_or_misshapen_channel_is_refused_on_the_field(string? channel, string code)
    {
        await using var api = await StartAsync();

        var error = await api.PostErrorAsync(Operations.NotificationEnable, new { channel }, HttpStatusCode.BadRequest, Ct);

        Assert.Equal("validation_failed", error.Code);
        var detail = Assert.Single(error.Details!);
        Assert.Equal(("channel", code), (detail.Field, detail.Code));
    }

    [Fact]
    public async Task A_reason_too_long_is_refused_on_the_field()
    {
        await using var api = await StartAsync();

        var error = await api.PostErrorAsync(Operations.NotificationDisable, new { channel = "toast", reason = new string('x', 2001) }, HttpStatusCode.BadRequest, Ct);

        Assert.Equal(("reason", "too_long"), (error.Details![0].Field, error.Details[0].Code));
    }

    /// <summary>Which channels tell the person is Jason's own setting: from inside a run it is refused by the header, before the body is read.</summary>
    [Theory]
    [InlineData(Operations.NotificationEnable)]
    [InlineData(Operations.NotificationDisable)]
    public async Task From_inside_a_run_it_is_refused_and_writes_nothing(string operation)
    {
        await using var api = await StartAsync(ToastOff);
        var bytes = File.ReadAllBytes(api.Paths.UserSettingsFile);
        var inside = new Dictionary<string, string> { ["Jason-Attempt"] = "att_01K7QN0000000000000000000R" };

        var (status, text) = await api.PostAsync(operation, new { channel = "toast", actor = Ada }, inside, Ct);

        Assert.True(status == HttpStatusCode.Forbidden, $"{operation} answered {(int)status}: {text}");
        var error = JsonSerializer.Deserialize<ErrorResponse>(text, JasonJson.Options)!.Error;
        Assert.Equal("decided_inside_a_run", error.Code);
        Assert.Equal(
            "This request comes from inside attempt `att_01K7QN0000000000000000000R`. Nothing inside a run changes Jason itself, and which channels tell the person that something waits for them is part of it: leave it to the person, or raise it as a decision.",
            error.Message);
        Assert.Equal(bytes, File.ReadAllBytes(api.Paths.UserSettingsFile));
    }

    [Fact]
    public async Task A_channel_an_environment_variable_sets_is_refused_as_shadowed()
    {
        await using var api = await StartAsync(
            configurationBuilt: configuration => configuration.Providers.OfType<EnvironmentVariablesConfigurationProvider>().Single()
                .Set("Notifications:Toast", "true"));
        var bytes = File.ReadAllBytes(api.Paths.UserSettingsFile);

        var error = await api.PostErrorAsync(Operations.NotificationDisable, new { channel = "toast" }, HttpStatusCode.Conflict, Ct);

        Assert.Equal("setting_shadowed", error.Code);
        Assert.Equal(bytes, File.ReadAllBytes(api.Paths.UserSettingsFile));
        Assert.Empty((await LinesAsync(api)).Items);
    }

    /// <summary>
    /// A Notifications section a person broke by hand keeps the runtime on the last settings that validated, so a switch
    /// written beside it would not be in force: the act is refused, the file is put back as the person left it, and the
    /// answer says so.
    /// </summary>
    [Fact]
    public async Task A_switch_the_runtime_would_not_put_in_force_is_refused_and_the_file_is_put_back()
    {
        await using var api = await StartAsync();
        const string Broken = "{\n    \"Dispatcher\": { \"Enabled\": false },\n    \"Notifications\": { \"QuietSeconds\": 0 }\n}\n";
        File.WriteAllText(api.Paths.UserSettingsFile, Broken, new UTF8Encoding(false));
        Assert.Null(api.Resolve<SettingsFile>().Read());
        Assert.Equal(30, InForce(api).QuietSeconds);

        var error = await api.PostErrorAsync(Operations.NotificationDisable, new { channel = "toast" }, HttpStatusCode.Conflict, Ct);

        Assert.Equal("setting_not_in_force", error.Code);
        Assert.Equal(
            "Notifications:Toast cannot be put in force: the Notifications settings in the file are refused, so the runtime keeps the last ones that validated; its log names the setting it refused. Mend the Notifications section, then try again.",
            error.Message);
        var last = error.Details![^1];
        Assert.Equal(("settings.json", "settings_file_put_back"), (last.Field, last.Code));
        Assert.Equal(Broken, File.ReadAllText(api.Paths.UserSettingsFile));
        Assert.True(InForce(api).Toast);
        Assert.Empty((await LinesAsync(api)).Items);
    }

    /// <summary>
    /// The runtime's own report says, for each plugin channel, whether it could deliver now — the checks a notification it
    /// was given would meet before anything is launched — so a page can say what is missing before anybody presses a switch.
    /// The toast has no such reading: whether there is a desktop is its presence.
    /// </summary>
    [Fact]
    public async Task The_report_says_whether_each_plugin_channel_could_deliver_and_what_is_missing()
    {
        await using var api = await StartAsync(WithPlugins);

        var info = await api.PostOkAsync<SystemInfoResponse>(Operations.SystemInfo, new { }, Ct);

        var notifications = info.Notifications!;
        Assert.Null(Channel(notifications, "toast").Readiness);
        Assert.Equal(ChannelReadiness.Ready, Channel(notifications, Ready).Readiness);
        Assert.Null(Channel(notifications, Ready).ReadinessDetail);
        Assert.Equal(NotAttemptedReasons.BindingInvalid, Channel(notifications, Chat).Readiness);
        Assert.Contains("/chat", Channel(notifications, Chat).ReadinessDetail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_exchanges_are_recorded()
    {
        await using var api = await StartAsync(WithPlugins);

        await api.RecordAsync<NotificationsInfo>(Operations.NotificationDisable, "toast", new { channel = "toast", reason = "too many banners" }, Ct);
        await api.RecordAsync<NotificationsInfo>(Operations.NotificationEnable, "toast", new { channel = "toast" }, Ct);
        await api.RecordAsync<NotificationsInfo>(Operations.NotificationDisable, "plugin", new { channel = Ready }, Ct);
        await api.RecordErrorAsync(Operations.NotificationEnable, "not-configured", new { channel = Chat }, HttpStatusCode.Conflict, Ct);
        await api.RecordErrorAsync(Operations.NotificationEnable, "unknown-channel", new { channel = "nobody-installed-this" }, HttpStatusCode.NotFound, Ct);

        await using var unnamed = await StartAsync();
        await unnamed.RecordErrorAsync(Operations.NotificationEnable, "not-named", new { channel = Ready }, HttpStatusCode.Conflict, Ct);

        await using var off = await StartAsync(AllOffAlone, allOn: false);
        await off.RecordErrorAsync(Operations.NotificationEnable, "notifications-off", new { channel = "toast" }, HttpStatusCode.Conflict, Ct);
    }

    private static NotificationChannelInfo Toast(NotificationsInfo info) => Channel(info, "toast");

    private static NotificationChannelInfo Channel(NotificationsInfo info, string name) => info.Channels.Single(channel => channel.Channel == name);

    private static NotificationsOptions InForce(RuntimeApiFixture api) => api.Resolve<LiveSettings<NotificationsOptions>>().Current;

    private static Task<Page<JournalEntryDto>> LinesAsync(RuntimeApiFixture api) =>
        api.PostOkAsync<Page<JournalEntryDto>>(Operations.JournalList, new { Kind = JournalKinds.NotificationChannelUpdated }, Ct);

    /// <summary>
    /// A runtime with the two notification plugins and a provider installed, and <paramref name="settings"/> as its file.
    /// A test runtime holds every notification off whatever its file says; this one holds them as <paramref name="allOn"/>
    /// says, which is what its file says of <c>Notifications:Enabled</c>, since the switch answers to it. Nothing is sent:
    /// the dispatcher is off, so no scan runs and the notifier never decides anything.
    /// </summary>
    private static Task<RuntimeApiFixture> StartAsync(string settings = Original, Action<IConfigurationRoot>? configurationBuilt = null, bool allOn = true) =>
        RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths =>
            {
                File.WriteAllText(paths.UserSettingsFile, settings, new UTF8Encoding(false));
                TestPlugins.InstallFakeProvider(paths);
                TestPlugins.Write(paths, Other, TestPlugins.Manifest(Other), Delivers);
                TestPlugins.Write(paths, Ready, Notifier(Ready, string.Empty), Delivers);
                TestPlugins.Write(
                    paths,
                    Chat,
                    Notifier(Chat, "binding:\n  type: object\n  properties:\n    chat:\n      type: string\n  required: [chat]\n  additionalProperties: false\n"),
                    Delivers);
            },
            configureServices: services => services.PostConfigure<NotificationsOptions>(notifications => notifications.Enabled = allOn),
            configurationBuilt: configurationBuilt);

    private const string Delivers = "export function invoke(operation, input, context) { return { result: { receipt: 'seen' } }; }";

    private static string Notifier(string id, string extra) =>
        $"""
        manifest_version: 1
        id: {id}
        version: 1.0.0
        kind: notification
        contracts:
          protocol: [1]
          operations: [1]
        {extra}
        """;
}
