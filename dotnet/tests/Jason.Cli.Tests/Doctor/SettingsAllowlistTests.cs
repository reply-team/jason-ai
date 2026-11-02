using System.Text.Json.Nodes;
using Jason.Cli.Doctor;
using Jason.Contracts.Configuration;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Doctor;

/// <summary>
/// The one list of settings whose values may leave the machine in a diagnostic bundle. The list is short on purpose
/// and every path on it is named here, so a path added by accident, or a path matched by something that merely
/// looks like it, fails a test rather than carrying a value nobody decided to carry.
/// </summary>
public sealed class SettingsAllowlistTests
{
    /// <summary>The settings that are a switch, or a word from a closed set: the values that say how the runtime is set up.</summary>
    internal static readonly string[] Switches =
    [
        "Dispatcher:Enabled", "Update:CheckEnabled", "Notifications:Enabled", "Notifications:Toast",
        "Notifications:Plugins:fake-provider:Enabled", "Inbound:Enabled", "Responder:OnReply",
    ];

    internal static readonly string[] Words = ["Logging:MinimumLevel", "Manager:Triggers[]"];

    /// <summary>The one number among them the runtime holds in 64 bits: the size a log file may reach. The others are 32.</summary>
    internal const string WideInteger = "Logging:FileSizeLimitBytes";

    /// <summary>The settings that are a number with a size, a count, a time or a port, each named one by one.</summary>
    internal static readonly string[] Integers =
    [
        "Runtime:Port",
        "Logging:RetainedFileCountLimit", "Logging:FileSizeLimitBytes",
        "Dispatcher:TickSeconds", "Dispatcher:MaxParallel", "Dispatcher:DrainSeconds", "Dispatcher:RetryDelaySeconds", "Dispatcher:ExitGraceSeconds",
        "Dispatcher:EventsCadenceSeconds",
        "Dispatcher:AiRole:TimeoutSeconds", "Dispatcher:AiRole:HeartbeatSeconds", "Dispatcher:AiRole:MaxAttempts",
        "Dispatcher:AiRole:MaxWaitSeconds", "Dispatcher:AiRole:MaxWaits",
        "Dispatcher:Blocks:TrialSeconds",
        "Dispatcher:ProviderOp:TimeoutSeconds", "Dispatcher:ProviderOp:HeartbeatSeconds", "Dispatcher:ProviderOp:MaxAttempts",
        "Manager:ReviewSeconds", "Manager:TimeoutSeconds", "Manager:MaxAttempts", "Manager:Priority", "Manager:MaxEntriesPerScan",
        "Roles:MaxStdoutBytes", "Roles:MaxSkillBytes",
        "Update:InitialDelayMinutes", "Update:IntervalHours",
        "Notifications:QuietSeconds", "Notifications:MaxWaitSeconds", "Notifications:MinIntervalMinutes",
        "Plugins:Limits:TimeoutMs", "Plugins:Limits:MaxTimeoutMs", "Plugins:Limits:MemoryMb", "Plugins:Limits:MaxMemoryMb",
        "Plugins:Limits:MaxStatements", "Plugins:Limits:MaxRecursion",
        "Plugins:Exec:OutputBytes", "Plugins:Exec:MaxCalls",
        "Plugins:Http:ResponseBytes", "Plugins:Http:RequestBytes", "Plugins:Http:MaxCalls", "Plugins:Http:TimeoutMs",
        "Plugins:Invoker:OutcomeBytes", "Plugins:Invoker:StderrBytes", "Plugins:Invoker:KillGraceMs",
        "Plugins:Invoker:VersionCheckTimeoutMs", "Plugins:Invoker:LogLineBytes",
        "Inbound:SyncSeconds", "Inbound:LookbackSeconds",
        "Responder:Priority", "Responder:TimeoutSeconds", "Responder:MaxAttempts", "Responder:MaxAgeSeconds",
    ];

    /// <summary>Settings that are named so nobody adds one by accident: a feed, a profile, a command, a grant, a route, a binding.</summary>
    internal static readonly string[] NeverAValue =
    [
        "Update:FeedUrl", "Roles:DefaultExecutionProfile", "Roles:DefaultEntryCommand[]", "Roles:DefaultEntryCommand:0",
        "Plugins:Grants:fake-provider:Exec[]", "Plugins:Grants:fake-provider:Http[]", "Plugins:Grants:fake-provider:Env[]",
        "Plugins:Grants:fake-provider:Exec:0", "Plugins:Grants:fake-provider:Enabled",
        "Routes:Default:Plugin", "Routes:Default:Binding:Port", "Routes:Default:Binding:Enabled",
        "Routes:Operations:campaign.list:Plugin", "Routes:Operations:campaign.list:Binding:MaxCalls",
        "Notifications:Plugins:fake-provider:Binding", "Notifications:Plugins:fake-provider:Binding:Enabled",
        "Notifications:Plugins:fake-provider:Binding:Port",
    ];

    /// <summary>What the parser makes of a piece of JSON, which for the literal <c>null</c> is no node at all.</summary>
    private static JsonNode? Parsed(string json) => JsonNode.Parse(json);

    private static JsonNode Text(string text) => JsonValue.Create(text)!;

    private static readonly string[] SampleSwitchValues = ["true", "false"];

    [Fact]
    [Critical(Family.S4, "the allowlist is the published one")]
    public void The_published_list_names_every_path_it_carries_and_no_other()
    {
        var published = SettingsAllowlist.Default.Published.Select(PathOf).Order(StringComparer.Ordinal).ToList();
        var named = Switches.Concat(Words).Concat(Integers)
            .Select(path => path.Replace("fake-provider", "<id>", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(named, published);
        Assert.Equal(published.Count, published.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>
    /// A person reads this list in <c>--help</c> and decides whether to share a bundle, so each line says what kind of
    /// value it carries — which is also the only value of that kind that will ever be carried there.
    /// </summary>
    [Fact]
    public void Each_published_line_says_what_kind_of_value_travels_there()
    {
        var lines = SettingsAllowlist.Default.Published;

        Assert.All(
            Switches.Select(path => path.Replace("fake-provider", "<id>", StringComparison.Ordinal)),
            path => Assert.Contains($"{path} (true or false)", lines));
        Assert.Contains("Manager:Triggers[] (a kind the runtime writes into its own chronicle)", lines);
        Assert.Contains(
            "Logging:MinimumLevel (one of verbose, debug, information, warning, error, fatal, in any case)",
            lines);
        Assert.All(
            Integers,
            path => Assert.Contains($"{path} ({(path == WideInteger ? "a 64-bit integer" : "a 32-bit integer")})", lines));
    }

    [Theory]
    [MemberData(nameof(SwitchPaths))]
    public void A_switch_carries_true_and_false_and_nothing_else(string path)
    {
        var list = SettingsAllowlist.Default;

        Assert.All(SampleSwitchValues, value => Assert.True(list.Carries(path, Parsed(value)), value));
        Assert.All(
            new[] { "\"true\"", "\"SENTINEL\"", "1", "0", "null", "{}", "[true]", "{\"a\":true}" },
            value => Assert.False(list.Carries(path, Parsed(value)), value));
    }

    [Theory]
    [InlineData("Verbose")]
    [InlineData("debug")]
    [InlineData("INFORMATION")]
    [InlineData("Warning")]
    [InlineData("error")]
    [InlineData("Fatal")]
    public void A_log_level_carries_when_it_is_one_of_the_six_the_runtime_reads(string level)
    {
        Assert.True(SettingsAllowlist.Default.Carries("Logging:MinimumLevel", Text(level)));
    }

    /// <summary>
    /// A value the runtime refuses is free text, and free text is what a secret typed onto the wrong line looks like.
    /// Being at a listed path does not make it travel.
    /// </summary>
    [Theory]
    [InlineData("\"warn\"")]
    [InlineData("\"Trace\"")]
    [InlineData("\"SENTINEL-LEVEL\"")]
    [InlineData("\"\"")]
    [InlineData("\" Warning\"")]
    [InlineData("\"Warning \"")]
    [InlineData("3")]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("[\"Warning\"]")]
    public void A_refused_log_level_never_travels_even_at_its_own_path(string json)
    {
        Assert.False(SettingsAllowlist.Default.Carries("Logging:MinimumLevel", Parsed(json)));
    }

    [Fact]
    public void A_trigger_carries_when_it_is_a_kind_the_runtime_writes_and_not_when_it_is_any_other_word()
    {
        var list = SettingsAllowlist.Default;

        Assert.All(ManagerTriggerKinds.All, kind => Assert.True(list.Carries("Manager:Triggers[]", Text(kind)), kind));
        Assert.All(
            new[] { "SENTINEL-TRIGGER", "", "WORKITEM_FAILED", "workitem failed", "workitem_failed ", "plan_revision", "observation" },
            word => Assert.False(list.Carries("Manager:Triggers[]", Text(word)), word));
        Assert.False(list.Carries("Manager:Triggers[]", Parsed("5")));
        Assert.False(list.Carries("Manager:Triggers[]", Parsed("true")));
    }

    [Fact]
    public void The_list_itself_carries_nothing_only_its_items_do()
    {
        var list = SettingsAllowlist.Default;

        Assert.True(list.Carries("Manager:Triggers[]", Text("workitem_failed")));
        Assert.True(list.Carries("Manager:Triggers:0", Text("workitem_failed")));
        Assert.True(list.Carries("manager:triggers:12", Text("workitem_failed")));
        Assert.False(list.Carries("Manager:Triggers", Parsed("[\"workitem_failed\"]")));
        Assert.False(list.Carries("Manager:Triggers", Text("workitem_failed")));
        Assert.False(list.Carries("Manager:Triggers:first", Text("workitem_failed")));
        Assert.False(list.Carries("Manager:Triggers[]:x", Text("workitem_failed")));
    }

    [Theory]
    [MemberData(nameof(CriticalIntegerPaths))]
    public void A_bounded_number_carries_only_as_a_json_integer(string path)
    {
        var list = SettingsAllowlist.Default;

        Assert.All(new[] { "0", "2", "-1", "65535", "2147483647", "-2147483648" }, value => Assert.True(list.Carries(path, Parsed(value)), value));
        Assert.All(
            new[] { "\"2\"", "\"SENTINEL\"", "2.5", "2.0", "1e3", "true", "null", "{}", "[2]", "{\"a\":2}", "9223372036854775808", "-9223372036854775809" },
            value => Assert.False(list.Carries(path, Parsed(value)), value));
    }

    /// <summary>
    /// A number is carried only if it fits the type the setting is bound to. The runtime refuses a larger one and never
    /// says it back, so a card number typed at a port is not carried here although it is an integer.
    /// </summary>
    [Theory]
    [MemberData(nameof(IntegerPaths))]
    public void A_bounded_number_carries_only_when_it_fits_the_type_of_the_setting(string path)
    {
        var list = SettingsAllowlist.Default;
        var beyond32Bits = new[] { "2147483648", "-2147483649", "4111111111111111", "9007199254740993", "9223372036854775807", "-9223372036854775808" };

        Assert.All(beyond32Bits, value => Assert.Equal(path == WideInteger, list.Carries(path, Parsed(value))));
    }

    /// <summary>
    /// Settings are keyed without regard to case, so <c>"dispatcher": {"maxParallel": 2}</c> is the same setting as
    /// <c>Dispatcher:MaxParallel</c>; and a path is matched whole, from its first key to its last, because two settings
    /// share a last key and a map a person names can hold any key at all.
    /// </summary>
    [Theory]
    [InlineData("dispatcher:maxparallel", true)]
    [InlineData("DISPATCHER:MAXPARALLEL", true)]
    [InlineData("Dispatcher:MaxParallel", true)]
    [InlineData("Plugins:Exec:MaxCalls", true)]
    [InlineData("plugins:http:maxcalls", true)]
    [InlineData("Dispatcher:MaxParallel:Extra", false)]
    [InlineData("Extra:Dispatcher:MaxParallel", false)]
    [InlineData("Dispatcher", false)]
    [InlineData("MaxParallel", false)]
    [InlineData("Dispatcher:MaxParallel[]", false)]
    [InlineData("Plugins:Grants:x:Exec:MaxCalls", false)]
    [InlineData("Plugins:Grants:fake-provider:Exec:MaxCalls", false)]
    [InlineData("Plugins:Grants:x:Http:MaxCalls", false)]
    [InlineData("Plugins:Grants:x:Exec", false)]
    [InlineData("Plugins:MaxCalls", false)]
    [InlineData("Plugins:Exec", false)]
    [InlineData("Plugins:Exec:MaxCalls:Extra", false)]
    [InlineData("Routes:Default:Binding:MaxCalls", false)]
    [InlineData("Routes:Operations:campaign.list:Binding:Port", false)]
    [InlineData("Binding:Port", false)]
    [InlineData("", false)]
    [InlineData(":", false)]
    [InlineData("[]", false)]
    public void A_path_is_matched_whole_and_without_regard_to_case(string path, bool carried)
    {
        Assert.Equal(carried, SettingsAllowlist.Default.Carries(path, Parsed("2")));
    }

    [Theory]
    [InlineData("Notifications:Plugins:fake-provider:Enabled", true)]
    [InlineData("notifications:plugins:Another-Id:enabled", true)]
    [InlineData("Notifications:Plugins:Enabled", false)]
    [InlineData("Notifications:Plugins:fake-provider:Binding:Enabled", false)]
    [InlineData("Notifications:Plugins:a:b:Enabled", false)]
    [InlineData("Notifications:Plugins:[]:Enabled", false)]
    [InlineData("Notifications:Plugins:<key>:Enabled", false)]
    [InlineData("Notifications:Plugins:<key 2>:Enabled", false)]
    [InlineData("Notifications:Plugins::Enabled", false)]
    [InlineData("Notifications:Plugins:fake-provider:Binding", false)]
    [InlineData("Notifications:Plugins:fake-provider", false)]
    public void A_named_plugin_stands_for_one_key_and_only_the_one_between(string path, bool carried)
    {
        Assert.Equal(carried, SettingsAllowlist.Default.Carries(path, Parsed("true")));
    }

    [Theory]
    [MemberData(nameof(CriticalNeverAValuePaths))]
    public void A_setting_that_is_named_as_never_carried_carries_nothing_whatever_it_holds(string path)
    {
        var list = SettingsAllowlist.Default;

        Assert.All(
            new[] { "true", "false", "2", "0", "\"SENTINEL\"", "\"Warning\"", "\"workitem_failed\"", "\"https://example.com/feed.json\"", "null", "{}", "[]" },
            value => Assert.False(list.Carries(path, Parsed(value)), value));
    }

    [Fact]
    public void A_value_that_is_not_a_scalar_never_carries_wherever_it_stands()
    {
        var list = SettingsAllowlist.Default;

        Assert.All(
            Switches.Concat(Words).Concat(Integers),
            path => Assert.All(new[] { "{}", "[]", "{\"a\":1}", "[1,2]" }, value => Assert.False(list.Carries(path, Parsed(value)), $"{path} {value}")));
    }

    public static TheoryData<string> SwitchPaths() => [.. Switches];

    public static TheoryData<string> IntegerPaths() => [.. Integers];

    // Critical: an allowed number travels only as a number.
    public static TheoryData<string> CriticalIntegerPaths() =>
        CriticalRows.Mark(IntegerPaths(), Family.S4, data => data is ["Dispatcher:MaxParallel"]);

    public static TheoryData<string> NeverAValuePaths() => [.. NeverAValue];

    // Critical: the feed URL and the grants never travel.
    public static TheoryData<string> CriticalNeverAValuePaths() =>
        CriticalRows.Mark(NeverAValuePaths(), Family.S4, data => data is ["Update:FeedUrl"] or ["Plugins:Grants:fake-provider:Exec[]"]);

    /// <summary>The path at the front of a published line, with the description in brackets taken off.</summary>
    private static string PathOf(string line) => line[..line.IndexOf(" (", StringComparison.Ordinal)];
}
