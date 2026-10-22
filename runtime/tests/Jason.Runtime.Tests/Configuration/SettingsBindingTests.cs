using System.Text;
using System.Text.Json.Nodes;
using Jason.Contracts.Discovery;
using Jason.Runtime.Configuration;
using Jason.Runtime.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Tests.Configuration;

/// <summary>
/// A value of the wrong type, read by the real binder. The binder's refusal repeats the value in its message, so
/// what a person is told is composed from that message instead: the setting's path and the type it has to be,
/// read out with one pattern, and nothing else of it. These tests run the real binder, so they pin its wording —
/// a .NET that words it differently turns them red, and the fallback sentence keeps the value out meanwhile.
/// </summary>
public class SettingsBindingTests
{
    private static readonly IReadOnlySet<string> NoPlugins = new HashSet<string>();

    [Fact]
    public void A_switch_written_as_text_is_refused_by_its_path_and_its_type()
    {
        Assert.Equal(
            ["Dispatcher:Enabled could not be read as System.Boolean."],
            Refusal<DispatcherOptions>("""{"Dispatcher":{"Enabled":"SENTINEL_BOOL"}}"""));
    }

    /// <summary>Text, a number too large for the setting, and a fraction: one sentence for all three.</summary>
    [Theory]
    [InlineData("\"SENTINEL_INT\"")]
    [InlineData("99999999999")]
    [InlineData("1.5")]
    public void A_number_the_setting_cannot_hold_is_refused_by_its_path_and_its_type(string value)
    {
        Assert.Equal(
            ["Plugins:Limits:TimeoutMs could not be read as System.Int32."],
            Refusal<PluginsOptions>(Settings("Plugins", "Limits", "TimeoutMs", JsonNode.Parse(value))));
    }

    /// <summary>The manager's section is read by hand rather than bound, and refused the same way.</summary>
    [Fact]
    public void The_section_read_by_hand_is_refused_the_same_way()
    {
        Assert.Equal(
            ["Manager:ReviewSeconds could not be read as System.Int32."],
            Refusal<ManagerOptions>("""{"Manager":{"ReviewSeconds":"SENTINEL_MANAGER"}}"""));
    }

    /// <summary>
    /// A path can carry a key a person chose — the id a notification plugin is named by, say — and that key is
    /// shown only when it names a plugin that is installed. The sentence is composed from the binder's own words, which
    /// is where a map's entry it could not read is said from too (the tests below).
    /// </summary>
    [Theory]
    [InlineData("fake-notifier", "Notifications:Plugins:fake-notifier:Enabled could not be read as System.Boolean.")]
    [InlineData("pasted-secret", "Notifications:Plugins:<key>:Enabled could not be read as System.Boolean.")]
    public void A_key_a_person_chose_is_shown_only_when_it_names_an_installed_plugin(string named, string expected)
    {
        var binder = new InvalidOperationException(
            $"Failed to convert configuration value 'SENTINEL' at 'Notifications:Plugins:{named}:Enabled' to type 'System.Boolean'.");

        var sentence = SettingsFailure.Unbindable(NotificationsOptions.Section, binder, new HashSet<string> { "fake-notifier" });

        Assert.Equal(expected, sentence);
    }

    /// <summary>
    /// An entry of a map holding a value of the wrong type. The binder leaves such an entry out of the map rather than
    /// refusing it, so the section used to validate without it and nothing was said: a notification channel whose
    /// switch was written as text was simply gone. It is refused like any other value of the wrong type, by the path
    /// the binder names when the entry is read on its own and the type that path has to be.
    /// </summary>
    [Fact]
    public void An_entry_of_a_map_the_binder_cannot_read_is_refused_by_its_path_and_its_type()
    {
        Assert.Equal(
            ["Notifications:Plugins:<key>:Enabled could not be read as System.Boolean."],
            Refusal<NotificationsOptions>(Settings("Notifications", "Plugins", "fake-notifier", "Enabled", "SENTINEL")));
    }

    /// <summary>
    /// The entry's key in that sentence is written the way every key a person chose is: as typed when it names a plugin
    /// installed here, and as a placeholder otherwise.
    /// </summary>
    [Fact]
    public void An_entry_of_a_map_the_binder_cannot_read_is_named_by_its_plugin_when_that_plugin_is_installed()
    {
        using var dir = new TempDataDir();
        var package = Path.Combine(dir.Paths.PluginsDirectory, "fake-notifier");
        Directory.CreateDirectory(package);
        File.WriteAllText(Path.Combine(package, "plugin.yaml"), "id: fake-notifier");
        using var provider = Provider(Settings("Notifications", "Plugins", "fake-notifier", "Enabled", "SENTINEL"), dir.Paths);

        Assert.Equal(["Notifications:Plugins:fake-notifier:Enabled could not be read as System.Boolean."], Refusal<NotificationsOptions>(provider));
    }

    /// <summary>
    /// An entry that is not an object at all — text, or a switch, where a channel or a grant belongs — is left out the
    /// same way, and the binder has no words for it even read on its own: it is said by the entry's path and the type
    /// the entry has to be. Writing <c>false</c> for a channel is one way to mean "off", and it is refused rather than
    /// taken to mean anything.
    /// </summary>
    [Theory]
    [InlineData("Notifications", "Plugins", "\"SENTINEL\"", "Notifications:Plugins:<key> could not be read as Jason.Runtime.Notifications.NotificationPluginEntry.")]
    [InlineData("Notifications", "Plugins", "false", "Notifications:Plugins:<key> could not be read as Jason.Runtime.Notifications.NotificationPluginEntry.")]
    [InlineData("Plugins", "Grants", "\"SENTINEL\"", "Plugins:Grants:<key> could not be read as Jason.Runtime.Configuration.PluginGrant.")]
    public void An_entry_that_is_not_an_object_is_refused_by_its_path_and_the_type_it_has_to_be(string section, string map, string entry, string expected)
    {
        var refusal = section == NotificationsOptions.Section
            ? Refusal<NotificationsOptions>(Settings(section, map, "pasted-secret", JsonNode.Parse(entry)))
            : Refusal<PluginsOptions>(Settings(section, map, "pasted-secret", JsonNode.Parse(entry)));

        Assert.Equal([expected], refusal);
    }

    /// <summary>
    /// Every entry left out is said, and an entry the binder could make is not: one sentence for each, in the order the
    /// file holds them.
    /// </summary>
    [Fact]
    public void Each_entry_left_out_is_said_and_no_other()
    {
        Assert.Equal(
            [
                "Notifications:Plugins:<key>:Enabled could not be read as System.Boolean.",
                "Notifications:Plugins:<key> could not be read as Jason.Runtime.Notifications.NotificationPluginEntry.",
            ],
            Refusal<NotificationsOptions>("""{"Notifications":{"Plugins":{"first-secret":{"Enabled":"SENTINEL"},"good-one":{"Enabled":false},"second-secret":"SENTINEL"}}}"""));
    }

    /// <summary>
    /// And an entry with nothing in it is not one the binder left out: it makes it, with every setting of the entry at
    /// its default, so naming a channel with an empty object still turns it on.
    /// </summary>
    [Fact]
    public void An_empty_entry_is_made_rather_than_refused()
    {
        using var provider = Provider("""{"Notifications":{"Plugins":{"fake-notifier":{}}}}""", TestOptions.NothingInstalled());

        var notifications = provider.GetRequiredService<IOptionsMonitor<NotificationsOptions>>().CurrentValue;

        Assert.True(Assert.Single(notifications.Plugins).Value.Enabled);
    }

    /// <summary>
    /// An item of a list that the binder cannot make — an object or a list where text belongs — is left out of the list
    /// the way a map's entry is, and the items after it move up into its place: a command line loses a word without a
    /// word said. It is refused by the item's path, the index shown because it stands under the whole path of a list,
    /// and the type the item has to be. Text where the whole list belongs is passed over too, and is refused the same way.
    /// </summary>
    [Theory]
    [InlineData("""{"Roles":{"DefaultEntryCommand":["agent-host",{"x":"SENTINEL"},"-p"]}}""", "Roles:DefaultEntryCommand:1 could not be read as System.String.")]
    [InlineData("""{"Roles":{"DefaultEntryCommand":["agent-host",["SENTINEL"],"-p"]}}""", "Roles:DefaultEntryCommand:1 could not be read as System.String.")]
    [InlineData("""{"Roles":{"DefaultEntryCommand":"SENTINEL -p"}}""", "Roles:DefaultEntryCommand could not be read as a list of System.String.")]
    public void A_list_item_the_binder_cannot_make_and_text_where_a_list_belongs_are_refused_by_path_and_type(string settings, string expected)
    {
        Assert.Equal([expected], Refusal<RolesOptions>(settings));
    }

    /// <summary>The same inside a map's entry, where the entry's key is written as every key a person chose is.</summary>
    [Theory]
    [InlineData("""{"Plugins":{"Grants":{"pasted-secret":{"Env":["A",{"x":"SENTINEL"}]}}}}""", "Plugins:Grants:<key>:Env:1 could not be read as System.String.")]
    [InlineData("""{"Plugins":{"Grants":{"pasted-secret":{"Exec":"SENTINEL"}}}}""", "Plugins:Grants:<key>:Exec could not be read as a list of System.String.")]
    public void A_list_inside_a_map_entry_is_refused_the_same_way(string settings, string expected)
    {
        Assert.Equal([expected], Refusal<PluginsOptions>(settings));
    }

    /// <summary>And in the section read by hand, whose list of triggers is bound the same way.</summary>
    [Fact]
    public void A_list_item_in_the_section_read_by_hand_is_refused_the_same_way()
    {
        Assert.Equal(
            ["Manager:Triggers:1 could not be read as System.String."],
            Refusal<ManagerOptions>("""{"Manager":{"Triggers":["decision_answered",{"x":"SENTINEL"}]}}"""));
    }

    /// <summary>
    /// Text where a map or an object of the settings belongs is passed over the same way, leaving every entry or every
    /// setting under it at its default; it is refused by the path and what has to be there.
    /// </summary>
    [Theory]
    [InlineData("""{"Plugins":{"Grants":"SENTINEL"}}""", "Plugins:Grants could not be read as a map of Jason.Runtime.Configuration.PluginGrant.")]
    [InlineData("""{"Plugins":{"Limits":"SENTINEL"}}""", "Plugins:Limits could not be read as Jason.Runtime.Configuration.PluginLimitsOptions.")]
    public void Text_where_a_map_or_an_object_belongs_is_refused_by_path_and_type(string settings, string expected)
    {
        Assert.Equal([expected], Refusal<PluginsOptions>(settings));
    }

    /// <summary>
    /// An item written as nothing — <c>null</c>, <c>{}</c> or <c>[]</c> — is not one the binder leaves out: it keeps its
    /// place in the list, empty, and the section's own validator says what is wrong with an empty word.
    /// </summary>
    [Theory]
    [InlineData("""{"Roles":{"DefaultEntryCommand":["agent-host",null]}}""")]
    [InlineData("""{"Roles":{"DefaultEntryCommand":["agent-host",{}]}}""")]
    [InlineData("""{"Roles":{"DefaultEntryCommand":["agent-host",[]]}}""")]
    public void An_item_written_as_nothing_keeps_its_place_and_is_left_to_the_validator(string settings)
    {
        Assert.Equal(["Roles:DefaultEntryCommand[1] must not be blank."], Refusal<RolesOptions>(settings));
    }

    /// <summary>And an empty list, or an empty object, is not text where one belongs.</summary>
    [Fact]
    public void An_empty_list_and_an_empty_object_are_read_as_empty()
    {
        using var provider = Provider("""{"Roles":{"DefaultEntryCommand":[]},"Plugins":{"Limits":{}}}""", TestOptions.NothingInstalled());

        Assert.Empty(provider.GetRequiredService<IOptionsMonitor<RolesOptions>>().CurrentValue.DefaultEntryCommand);
        Assert.Equal(new PluginLimitsOptions().TimeoutMs, provider.GetRequiredService<IOptionsMonitor<PluginsOptions>>().CurrentValue.Limits.TimeoutMs);
    }

    /// <summary>
    /// A switch, a number or a word written as an object or a list. The binder keeps the setting's default for it and
    /// says nothing — a tick written <c>[5]</c> ran at ten seconds, an update check written <c>[false]</c> stayed on — or,
    /// for a word that may be absent, refuses in words that name neither the setting nor its type. It is refused like
    /// any other value of the wrong type: the setting's path, and the type it has to be.
    /// </summary>
    [Theory]
    [InlineData("""{"Dispatcher":{"TickSeconds":["SENTINEL"]}}""", "Dispatcher:TickSeconds could not be read as System.Int32.")]
    [InlineData("""{"Dispatcher":{"TickSeconds":{"x":"SENTINEL"}}}""", "Dispatcher:TickSeconds could not be read as System.Int32.")]
    [InlineData("""{"Dispatcher":{"AiRole":{"MaxAttempts":["SENTINEL"]}}}""", "Dispatcher:AiRole:MaxAttempts could not be read as System.Int32.")]
    [InlineData("""{"Update":{"CheckEnabled":["SENTINEL"]}}""", "Update:CheckEnabled could not be read as System.Boolean.")]
    [InlineData("""{"Update":{"FeedUrl":{"x":"SENTINEL"}}}""", "Update:FeedUrl could not be read as System.String.")]
    [InlineData("""{"Roles":{"DefaultExecutionProfile":{"x":"SENTINEL"}}}""", "Roles:DefaultExecutionProfile could not be read as System.String.")]
    [InlineData("""{"Runtime":{"Port":["SENTINEL"]}}""", "Runtime:Port could not be read as System.Int32.")]
    [InlineData("""{"Logging":{"MinimumLevel":{"x":"SENTINEL"}}}""", "Logging:MinimumLevel could not be read as System.String.")]
    [InlineData("""{"Notifications":{"Plugins":{"pasted-secret":{"Enabled":{"v":"SENTINEL"}}}}}""", "Notifications:Plugins:<key>:Enabled could not be read as System.Boolean.")]
    public void A_setting_read_from_text_and_written_as_an_object_or_a_list_is_refused_by_path_and_type(string settings, string expected)
    {
        var refusal = JsonNode.Parse(settings)!.AsObject().Single().Key switch
        {
            "Dispatcher" => Refusal<DispatcherOptions>(settings),
            "Update" => Refusal<UpdateOptions>(settings),
            "Roles" => Refusal<RolesOptions>(settings),
            "Runtime" => Refusal<RuntimeOptions>(settings),
            "Logging" => Refusal<LoggingOptions>(settings),
            _ => Refusal<NotificationsOptions>(settings),
        };

        Assert.Equal([expected], refusal);
    }

    /// <summary>
    /// Every section the runtime reads has a validator that refuses what its binding could not read, and each refuses
    /// it by that section's path. A section whose validator went missing would start on the setting's default and
    /// say nothing, so each is given a value of the wrong type here, read the way the runtime reads it.
    /// </summary>
    [Theory]
    [MemberData(nameof(CriticalMistypedInEverySection))]
    public void A_mistyped_value_in_any_section_is_refused_by_that_sections_path(string section, string settings, string expected)
    {
        var refusal = section switch
        {
            RuntimeOptions.Section => Refusal<RuntimeOptions>(settings),
            LoggingOptions.Section => Refusal<LoggingOptions>(settings),
            DispatcherOptions.Section => Refusal<DispatcherOptions>(settings),
            ManagerOptions.Section => Refusal<ManagerOptions>(settings),
            RolesOptions.Section => Refusal<RolesOptions>(settings),
            PluginsOptions.Section => Refusal<PluginsOptions>(settings),
            UpdateOptions.Section => Refusal<UpdateOptions>(settings),
            NotificationsOptions.Section => Refusal<NotificationsOptions>(settings),
            RoutesOptions.Section => Refusal<RoutesOptions>(settings),
            InboundOptions.Section => Refusal<InboundOptions>(settings),
            _ => throw new ArgumentOutOfRangeException(nameof(section), section, "a section this test does not know"),
        };

        Assert.Equal([expected], refusal);
    }

    /// <summary>The theory above has a row for every section there is, so a section added later cannot be left out of it.</summary>
    [Fact]
    [Critical(Family.C1, "every section is covered by the type check")]
    public void The_mistyped_value_theory_has_a_row_for_every_section()
    {
        var sections = typeof(RuntimeOptions).Assembly.GetTypes()
            .Select(type => type.GetField("Section", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            .Where(field => field is { IsLiteral: true } && field.FieldType == typeof(string))
            .Select(field => (string)field!.GetRawConstantValue()!);

        Assert.Equal(sections.Order(StringComparer.Ordinal), MistypedRows.Select(row => row.Section).Order(StringComparer.Ordinal));
    }

    public static TheoryData<string, string, string> MistypedInEverySection
    {
        get
        {
            var data = new TheoryData<string, string, string>();
            foreach (var (section, settings, expected) in MistypedRows)
            {
                data.Add(section, settings, expected);
            }

            return data;
        }
    }

    // Critical: a mistyped value is refused by path.
    public static TheoryData<string, string, string> CriticalMistypedInEverySection() =>
        CriticalRows.Mark(MistypedInEverySection, Family.C1, data => data is ["Runtime", _, _]);

    private static readonly (string Section, string Settings, string Expected)[] MistypedRows =
    [
        (RuntimeOptions.Section, """{"Runtime":{"Port":"SENTINEL"}}""", "Runtime:Port could not be read as System.Int32."),
        (LoggingOptions.Section, """{"Logging":{"RetainedFileCountLimit":"SENTINEL"}}""", "Logging:RetainedFileCountLimit could not be read as System.Int32."),
        (DispatcherOptions.Section, """{"Dispatcher":{"TickSeconds":"SENTINEL"}}""", "Dispatcher:TickSeconds could not be read as System.Int32."),
        (ManagerOptions.Section, """{"Manager":{"MaxAttempts":"SENTINEL"}}""", "Manager:MaxAttempts could not be read as System.Int32."),
        (RolesOptions.Section, """{"Roles":{"MaxStdoutBytes":"SENTINEL"}}""", "Roles:MaxStdoutBytes could not be read as System.Int32."),
        (PluginsOptions.Section, """{"Plugins":{"Limits":{"TimeoutMs":"SENTINEL"}}}""", "Plugins:Limits:TimeoutMs could not be read as System.Int32."),
        (UpdateOptions.Section, """{"Update":{"CheckEnabled":"SENTINEL"}}""", "Update:CheckEnabled could not be read as System.Boolean."),
        (NotificationsOptions.Section, """{"Notifications":{"QuietSeconds":"SENTINEL"}}""", "Notifications:QuietSeconds could not be read as System.Int32."),
        (RoutesOptions.Section, """{"Routes":{"Operations":"SENTINEL"}}""", "Routes:Operations could not be read as a map of Jason.Runtime.Configuration.RouteEntry."),
        (InboundOptions.Section, """{"Inbound":{"SyncSeconds":"SENTINEL"}}""", "Inbound:SyncSeconds could not be read as System.Int32."),
    ];

    /// <summary>
    /// The binder writes the value before the path, so a value can imitate the very words the path and the type
    /// are read from. Only the binder's own last words are read, so the imitation is never taken for them.
    /// </summary>
    [Fact]
    public void A_value_written_to_look_like_the_binders_own_words_is_not_read_as_them()
    {
        var refusal = Refusal<DispatcherOptions>("""{"Dispatcher":{"Enabled":"x' at 'ghp_token123' to type 'ghp_secret456"}}""");

        Assert.Equal(["Dispatcher:Enabled could not be read as System.Boolean."], refusal);
    }

    /// <summary>
    /// The sentence against the binder's own exception, not one written for the test: the message really does
    /// repeat the value, which is the whole reason a sentence is composed rather than the message passed on.
    /// </summary>
    [Fact]
    public void The_binders_message_is_read_for_the_path_and_the_type_and_nothing_else()
    {
        var configuration = Configuration("""{"Plugins":{"Limits":{"TimeoutMs":"SENTINEL_INT"}}}""");
        var binder = Assert.Throws<InvalidOperationException>(
            () => configuration.GetSection(PluginsOptions.Section).Bind(new PluginsOptions()));
        Assert.Contains("SENTINEL_INT", binder.Message, StringComparison.Ordinal);

        var sentence = SettingsFailure.Unbindable(PluginsOptions.Section, binder, NoPlugins);

        Assert.Equal("Plugins:Limits:TimeoutMs could not be read as System.Int32.", sentence);
    }

    [Fact]
    public void A_message_in_words_the_pattern_does_not_know_names_only_the_section()
    {
        var unknown = new InvalidOperationException("The value 'SENTINEL' was not what was wanted.");

        Assert.Equal(
            "Dispatcher holds a value that could not be read.",
            SettingsFailure.Unbindable(DispatcherOptions.Section, unknown, NoPlugins));
    }

    private static IReadOnlyList<string> Refusal<TOptions>(string settings)
        where TOptions : class
    {
        using var provider = Provider(settings, TestOptions.NothingInstalled());
        return Refusal<TOptions>(provider);
    }

    /// <summary>The validator's refusal of the section, and nothing underneath it that could carry the value.</summary>
    private static IReadOnlyList<string> Refusal<TOptions>(ServiceProvider provider)
        where TOptions : class
    {
        var refused = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptionsMonitor<TOptions>>().CurrentValue);
        Assert.Null(refused.InnerException);
        return [.. refused.Failures];
    }

    private static ServiceProvider Provider(string settings, JasonPaths paths)
    {
        var services = new ServiceCollection();
        services.AddJasonOptions(Configuration(settings), paths);

        // The dispatcher's section has a second validator that reads the plugin section through its seam.
        services.AddSingleton(provider => new LiveSettings<PluginsOptions>(
            provider.GetRequiredService<IOptionsMonitor<PluginsOptions>>(),
            NullLogger<LiveSettings<PluginsOptions>>.Instance,
            PluginsOptions.Section));
        return services.BuildServiceProvider();
    }

    /// <summary>A settings file holding one value at the end of the path: every key but the last, then the value.</summary>
    private static string Settings(params object?[] pathThenValue)
    {
        JsonNode? node = pathThenValue[^1] switch
        {
            JsonNode json => json,
            var text => JsonValue.Create((string?)text),
        };
        for (var index = pathThenValue.Length - 2; index >= 0; index--)
        {
            node = new JsonObject { [(string)pathThenValue[index]!] = node };
        }

        return node!.ToJsonString();
    }

    private static IConfigurationRoot Configuration(string settings) =>
        new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(settings))).Build();
}
