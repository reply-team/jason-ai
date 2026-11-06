using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Jason.Cli.Doctor;
using Jason.Contracts.Configuration;
using Jason.Contracts.Ids;
using Jason.Contracts.Operations;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Doctor;

/// <summary>
/// The settings file as a diagnostic bundle carries it: what it is made of — keys, types, lengths — and a value only
/// where one published list says the value may travel. Everything here is written for the worst file a person can
/// produce: a token pasted on the wrong line, a key that is a secret, a list where an object belongs.
/// </summary>
public sealed class ConfigShapeTests
{
    private const string Plugin = "fake-provider";

    private static readonly IReadOnlySet<string> Installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Plugin };

    private static JsonObject Describe(string text) => ConfigShape.Describe(text, SettingsAllowlist.Default, Installed);

    private static JsonNode Shape(string text) => Describe(text)["shape"]!;

    /// <summary>The description of a setting inside the shape, by the keys that lead to it.</summary>
    private static JsonNode At(JsonNode shape, params string[] keys)
    {
        var node = shape;
        foreach (var key in keys)
        {
            node = node["members"]![key]!;
        }

        return node;
    }

    /// <summary>The names an object's members are printed under, in the order the file wrote them.</summary>
    private static IEnumerable<string> Names(JsonNode node) => node["members"]!.AsObject().Select(member => member.Key);

    /// <summary>The description of one value written at an unlisted place: <c>Update:FeedUrl</c> is a string and never a value.</summary>
    private static string Feed(string json) => At(Shape("{\"Update\":{\"FeedUrl\":" + json + "}}"), "Update", "FeedUrl").ToJsonString();

    // A plugin's configuration: the section, whole, in every way a file can spell it.

    private const string Withheld = """{"withheld":"plugin_configuration"}""";

    /// <summary>Nothing of the section travels: no plugin, no key, no size, no type, no value and not how many there are.</summary>
    [Theory]
    [InlineData("""{"Plugins":{"Config":{"fake-provider":{"url":"https://SENTINEL.example.test","Port":"5000","token":"SENTINEL"}}}}""")]
    [InlineData("""{"PLUGINS":{"CONFIG":{"fake-provider":{"url":"SENTINEL"}}}}""")]
    [InlineData("""{"Plugins":{"Config":["SENTINEL"]}}""")]
    [InlineData("""{"Plugins":{"Config":"SENTINEL"}}""")]
    [InlineData("""{"Plugins:Config":{"fake-provider":{"url":"SENTINEL"}}}""")]
    public void The_section_of_a_plugins_configuration_is_withheld_whole(string text)
    {
        var described = Describe(text).ToJsonString();

        Assert.Contains(Withheld, described, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL", described, StringComparison.Ordinal);
        Assert.DoesNotContain("fake-provider", described, StringComparison.Ordinal);
        Assert.DoesNotContain("url", described, StringComparison.Ordinal);
        Assert.DoesNotContain("token", described, StringComparison.Ordinal);
    }

    /// <summary>A key that spells a path inside the section reaches the same settings, so it is the section, and its own name stays home too.</summary>
    [Theory]
    [InlineData("""{"Plugins:Config:fake-provider":{"url":"SENTINEL"}}""")]
    [InlineData("""{"Plugins:Config:fake-provider:url":"SENTINEL"}""")]
    [InlineData("""{"plugins:config:SENTINEL":{"url":"SENTINEL"}}""")]
    [InlineData("""{"Plugins":{"Config:fake-provider":{"url":"SENTINEL"}}}""")]
    [InlineData("""{"Plugins":{"Config:fake-provider:url":"SENTINEL"}}""")]
    public void A_key_that_spells_a_path_inside_the_section_is_the_section(string text)
    {
        var described = Describe(text).ToJsonString();

        Assert.Contains(Withheld, described, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL", described, StringComparison.Ordinal);
        Assert.DoesNotContain("fake-provider", described, StringComparison.Ordinal);
        Assert.DoesNotContain("url", described, StringComparison.Ordinal);
    }

    [Fact]
    public void What_stands_beside_the_section_is_described_as_ever()
    {
        var shape = Shape("""{"Plugins":{"Config":{"fake-provider":{"url":"x"}},"Exec":{"MaxCalls":7}}}""");

        Assert.Equal(7, At(shape, "Plugins", "Exec", "MaxCalls")["value"]!.GetValue<int>());
        Assert.Equal(Withheld, At(shape, "Plugins", "Config").ToJsonString());
    }

    // The file that is not there, the one that is not JSON, and what is said about each.

    [Fact]
    public void A_file_that_is_not_there_is_described_as_missing_and_nothing_more()
    {
        Assert.Equal("""{"exists":false,"bytes":0,"parses":false}""", ConfigShape.Describe(null, SettingsAllowlist.Default, Installed).ToJsonString());
    }

    /// <summary>Some editors save a settings file with a byte order mark. The runtime reads past it, so it is no part of the JSON, though it is part of the size.</summary>
    [Fact]
    public void A_byte_order_mark_at_the_start_is_counted_in_the_size_and_is_no_part_of_the_json()
    {
        const string text = "\uFEFF{\"Runtime\":{\"Port\":5000}}";

        var described = Describe(text);

        Assert.True(described["parses"]!.GetValue<bool>());
        Assert.Equal(Encoding.UTF8.GetByteCount(text), described["bytes"]!.GetValue<int>());
        Assert.Equal(5000, At(described["shape"]!, "Runtime", "Port")["value"]!.GetValue<int>());
    }

    [Fact]
    public void The_size_is_the_bytes_of_the_file_not_the_characters_of_the_text()
    {
        const string text = """{"Update":{"FeedUrl":"é€😀"}}""";

        var described = Describe(text);

        Assert.True(described["exists"]!.GetValue<bool>());
        Assert.True(described["parses"]!.GetValue<bool>());
        Assert.Equal(Encoding.UTF8.GetByteCount(text), described["bytes"]!.GetValue<int>());
        Assert.NotEqual(text.Length, described["bytes"]!.GetValue<int>());
    }

    /// <summary>
    /// A file that is not valid JSON is described by where the parser stopped and nothing it said: the parser's message
    /// quotes what it found there, and what was typed onto the wrong line is often a secret.
    /// </summary>
    [Fact]
    [Critical(Family.S4, "a broken file leaks no text")]
    public void A_file_that_is_not_json_is_described_by_the_line_and_column_of_the_parser_and_nothing_of_its_text()
    {
        const string text = "{\n  \"Update\": {\n    \"FeedUrl\": \"SENTINEL-FEED\",\n    \"IntervalHours\": ]\n  }\n}";

        var described = Describe(text);

        Assert.True(described["exists"]!.GetValue<bool>());
        Assert.False(described["parses"]!.GetValue<bool>());
        Assert.Null(described["shape"]);
        Assert.Equal("""{"line":4,"column":22}""", described["error"]!.ToJsonString());
        Assert.DoesNotContain("SENTINEL", described.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain("IntervalHours", described.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>The runtime reports the byte the parser stopped at, and so does this, so the two lines agree.</summary>
    [Fact]
    public void The_column_counts_bytes_as_the_runtime_reports_it()
    {
        Assert.Equal("""{"line":1,"column":8}""", Describe("""{"é": ]""")["error"]!.ToJsonString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n  ")]
    [InlineData("{")]
    [InlineData("""{"Dispatcher": {"MaxParallel": 2}""")]
    [InlineData("""{"Dispatcher": {"MaxParallel": 2}} trailing""")]
    [InlineData("{'Dispatcher': 1}")]
    [InlineData("""{"a": 01}""")]
    [InlineData("""{"a": "unterminated""")]
    [InlineData("{Dispatcher: 1}")]
    public void A_file_that_does_not_parse_says_so_and_never_throws(string text)
    {
        var described = Describe(text);

        Assert.False(described["parses"]!.GetValue<bool>());
        Assert.Null(described["shape"]);
        Assert.True(described["error"]!["line"]!.GetValue<long>() >= 1);
        Assert.True(described["error"]!["column"]!.GetValue<long>() >= 1);
    }

    /// <summary>
    /// Text a person can write and a parser can read, that no string type holds as it was written: an escape for half of a
    /// pair, a character that is nothing, a byte order mark. None of it is a reason for a bundle not to be written.
    /// </summary>
    [Theory]
    [InlineData("{\"Update\":{\"FeedUrl\":\"\\ud800\"}}")]
    [InlineData("{\"Update\":{\"FeedUrl\":\"a\\udc00b\"}}")]
    [InlineData("{\"Update\":{\"FeedUrl\":\"\\u0000\"}}")]
    [InlineData("{\"\\ud800\":1}")]
    [InlineData("{\"Update\":{\"FeedUrl\":[\"\\ud800\"]}}")]
    [InlineData("\ufeff{\"Update\":{}}")]
    [InlineData("{\"Update\":{\"FeedUrl\":\"\ud800\"}}")]
    public void Text_that_is_odd_to_hold_never_stops_the_description(string text)
    {
        var described = Describe(text);

        Assert.True(described["exists"]!.GetValue<bool>());
        Assert.NotNull(described["parses"]);
    }

    [Fact]
    public void A_file_nested_past_the_depth_the_parser_allows_does_not_parse_and_never_overflows_anything()
    {
        var text = new string('[', 5000) + new string(']', 5000);

        var described = Describe(text);

        Assert.False(described["parses"]!.GetValue<bool>());
        Assert.Null(described["shape"]);
    }

    /// <summary>
    /// The runtime's provider reads comments and a comma before a closing bracket, so a file that has them is a file
    /// that works, and describing it as one that does not parse would send somebody looking for the wrong thing.
    /// </summary>
    [Fact]
    public void Comments_and_trailing_commas_are_read_as_the_runtime_reads_them()
    {
        const string text = """
            {
              // where the runtime listens
              "Runtime": { "Port": 5000, },  /* a block comment */
              "Dispatcher": { "MaxParallel": 2, },
            }
            """;

        var described = Describe(text);

        Assert.True(described["parses"]!.GetValue<bool>());
        Assert.Equal(5000, At(described["shape"]!, "Runtime", "Port")["value"]!.GetValue<int>());
        Assert.Equal(2, At(described["shape"]!, "Dispatcher", "MaxParallel")["value"]!.GetValue<int>());
    }

    // A file that is valid JSON and still not a settings file.

    [Theory]
    [InlineData("[]", """{"type":"array","length":0}""")]
    [InlineData("\"SENTINEL\"", """{"type":"string","length":8}""")]
    [InlineData("42", """{"type":"integer"}""")]
    [InlineData("4.5", """{"type":"number"}""")]
    [InlineData("true", """{"type":"boolean"}""")]
    [InlineData("null", """{"type":"null"}""")]
    public void A_file_that_is_json_but_not_an_object_says_what_it_is_and_carries_no_value(string text, string expected)
    {
        var described = Describe(text);

        Assert.True(described["parses"]!.GetValue<bool>());
        Assert.Equal(expected, described["shape"]!.ToJsonString());
        Assert.DoesNotContain("SENTINEL", described.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_list_of_settings_objects_at_the_top_is_described_as_a_list_and_carries_no_value()
    {
        var described = Describe("""[{"Dispatcher":{"MaxParallel":424200001}},{"Runtime":{"Port":424200002}}]""");

        var shape = described["shape"]!;
        Assert.Equal("array", shape["type"]!.GetValue<string>());
        Assert.Equal(2, shape["length"]!.GetValue<int>());
        Assert.DoesNotContain("42420000", described.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The runtime refuses a file that sets one key twice; this describes it and says so, and describes the first
    /// of them only, so a value written second never travels.
    /// </summary>
    [Fact]
    public void A_key_written_twice_is_counted_and_only_the_first_is_described()
    {
        var described = Describe("""{"Update":{"IntervalHours":1,"IntervalHours":424200004,"FeedUrl":"a"}}""");

        var update = At(described["shape"]!, "Update");
        Assert.Equal(3, update["key_count"]!.GetValue<int>());
        Assert.Equal(1, update["duplicate_keys"]!.GetValue<int>());
        Assert.Equal(1, update["members"]!["IntervalHours"]!["value"]!.GetValue<int>());
        Assert.DoesNotContain("42420000", described.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>Keys are matched without regard to case, so two spellings of one key are one key written twice.</summary>
    [Fact]
    public void A_key_written_twice_in_two_cases_is_one_key_written_twice()
    {
        var described = Describe("""{"Dispatcher":{"MaxParallel":2},"dispatcher":{"MaxParallel":424200003}}""");

        var shape = described["shape"]!;
        Assert.Equal(2, shape["key_count"]!.GetValue<int>());
        Assert.Equal(1, shape["duplicate_keys"]!.GetValue<int>());
        Assert.Equal(["Dispatcher"], shape["members"]!.AsObject().Select(member => member.Key));
        Assert.Equal(2, At(shape, "Dispatcher", "MaxParallel")["value"]!.GetValue<int>());
        Assert.DoesNotContain("42420000", described.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_written_once_has_no_count_of_duplicates()
    {
        Assert.Null(Shape("""{"Runtime":{"Port":1}}""")["duplicate_keys"]);
    }

    // Keys: written as typed when the code already knows the name, as a placeholder when it does not.

    [Fact]
    public void A_setting_name_is_shown_as_typed_whatever_its_case()
    {
        var shape = Shape("""{"dispatcher":{"MAXPARALLEL":2},"Update":{}}""");

        Assert.Equal(["dispatcher", "Update"], Names(shape));
        Assert.Equal(["MAXPARALLEL"], Names(At(shape, "dispatcher")));
        Assert.Equal(2, shape["key_count"]!.GetValue<int>());
    }

    /// <summary>
    /// A key a person typed is text, and a token pasted where a name belonged is still a token: it is never printed. What
    /// is written under it is still described — by its own keys, which the code knows, and by type — but never by value.
    /// </summary>
    [Fact]
    [Critical(Family.S4, "an unknown key is a placeholder")]
    public void A_key_the_code_does_not_know_is_written_as_a_placeholder_and_what_is_under_it_is_still_described()
    {
        var described = Describe("""{"ghp_SENTINELTOKEN":{"Port":1},"Runtime":{"Port":2}}""");

        var shape = described["shape"]!;
        Assert.Equal(["<key>", "Runtime"], Names(shape));
        Assert.Equal(2, shape["key_count"]!.GetValue<int>());
        Assert.Equal("""{"type":"integer"}""", At(shape, "<key>", "Port").ToJsonString());
        Assert.Equal(2, At(shape, "Runtime", "Port")["value"]!.GetValue<int>());
        Assert.DoesNotContain("SENTINEL", described.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Several_unknown_keys_in_one_object_are_told_apart_by_number_and_none_of_their_text_appears()
    {
        var described = Describe("""{"Runtime":{"aaa-SENTINEL":1,"bbb-SENTINEL":"x","ccc-SENTINEL":[1]}}""");

        var runtime = At(described["shape"]!, "Runtime");
        Assert.Equal(["<key>", "<key 2>", "<key 3>"], Names(runtime));
        Assert.Equal(["integer", "string", "array"], Names(runtime).Select(name => At(runtime, name)["type"]!.GetValue<string>()));
        Assert.DoesNotContain("SENTINEL", described.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Sixty_unknown_keys_are_sixty_numbered_placeholders_and_sixty_one_are_only_counted()
    {
        string Object(int count) => "{\"Runtime\":{" + string.Join(",", Enumerable.Range(0, count).Select(index => $"\"SENTINEL-{index}\":{index}")) + "}}";

        var sixty = At(Shape(Object(60)), "Runtime");
        var sixtyOne = Describe(Object(61));

        Assert.Equal(60, Names(sixty).Count());
        Assert.Equal("<key 60>", Names(sixty).Last());
        Assert.Equal(60, Names(sixty).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            """{"type":"object","key_count":61,"members_not_shown":"more_than_60_keys"}""",
            At(sixtyOne["shape"]!, "Runtime").ToJsonString());
        Assert.DoesNotContain("SENTINEL", sixtyOne.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The placeholder is itself a word nobody has to keep to themselves, so a key typed to look like one is an unknown
    /// key like any other: it gets a placeholder of its own, in either order, and never takes another key's name.
    /// </summary>
    [Theory]
    [InlineData("<key>")]
    [InlineData("<key 2>")]
    [InlineData("<key 3>")]
    public void A_key_typed_as_a_placeholder_is_an_unknown_key_like_any_other(string typed)
    {
        var before = At(Shape("{\"Runtime\":{\"" + typed + "\":1,\"other\":\"x\"}}"), "Runtime");
        var after = At(Shape("{\"Runtime\":{\"other\":\"x\",\"" + typed + "\":1}}"), "Runtime");

        Assert.Equal(["<key>", "<key 2>"], Names(before));
        Assert.Equal(["integer", "string"], Names(before).Select(name => At(before, name)["type"]!.GetValue<string>()));
        Assert.Equal(["<key>", "<key 2>"], Names(after));
        Assert.Equal(["string", "integer"], Names(after).Select(name => At(after, name)["type"]!.GetValue<string>()));
    }

    [Fact]
    public void A_key_typed_as_a_placeholder_never_carries_a_value()
    {
        var described = Describe("""{"Notifications":{"Plugins":{"<key>":{"Enabled":true}}},"Dispatcher":{"<key>":2}}""");

        Assert.Equal("""{"type":"boolean"}""", At(described["shape"]!, "Notifications", "Plugins", "<key>", "Enabled").ToJsonString());
        Assert.DoesNotContain("\"value\"", described.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A key with colons in it is several keys to the configuration system, and one of them can be typed to look like a
    /// placeholder. It is hidden as a whole, like a key typed as one: printed as typed it would show a placeholder that
    /// stands for nothing hidden, and carry the value beneath it as if every key on the way had been shown.
    /// </summary>
    [Theory]
    [InlineData("""{"Notifications:Plugins:<key>:Enabled":true}""", "")]
    [InlineData("""{"Notifications":{"Plugins:<key 2>:Enabled":true}}""", "Notifications")]
    public void A_key_with_colons_and_a_placeholder_among_them_is_hidden_as_a_whole(string text, string parent)
    {
        var described = Describe(text);

        var holder = parent.Length == 0 ? described["shape"]! : At(described["shape"]!, parent);
        Assert.Equal(["<key>"], Names(holder));
        Assert.DoesNotContain("\"value\"", described.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>An index of five digits or more is a number somebody typed, not a place in a list, and is not printed.</summary>
    [Fact]
    public void A_key_of_five_digits_where_a_list_stands_is_not_printed()
    {
        var described = Describe("""{"Manager":{"Triggers":{"12345":"workitem_failed","1234":"workitem_failed"}}}""");

        Assert.Equal(["<key>", "1234"], Names(At(described["shape"]!, "Manager", "Triggers")));
        Assert.DoesNotContain("12345", described.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_plugin_is_shown_by_its_id_only_where_it_is_installed()
    {
        var described = Describe("""{"Plugins":{"Grants":{"fake-provider":{"Exec":["a"]},"SENTINEL-not-installed":{"Exec":["b","c"]}}}}""");

        var grants = At(described["shape"]!, "Plugins", "Grants");
        Assert.Equal(["fake-provider", "<key>"], Names(grants));
        Assert.Equal(1, At(grants, "fake-provider", "Exec")["length"]!.GetValue<int>());
        Assert.Equal(2, At(grants, "<key>", "Exec")["length"]!.GetValue<int>());
        Assert.DoesNotContain("SENTINEL", described.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A listed path with an id in it is a plugin that is installed: another id in the same place is a key that is not
    /// shown, and a value is carried only where every key on the way was.
    /// </summary>
    [Fact]
    public void An_id_that_is_not_installed_beside_one_that_is_shows_a_placeholder_and_only_the_installed_ones_value()
    {
        var described = Describe("""{"Notifications":{"Plugins":{"SENTINEL-other":{"Enabled":true},"fake-provider":{"Enabled":false}}}}""");

        var plugins = At(described["shape"]!, "Notifications", "Plugins");
        Assert.Equal(["<key>", "fake-provider"], Names(plugins));
        Assert.Equal("""{"type":"boolean"}""", At(plugins, "<key>", "Enabled").ToJsonString());
        Assert.Equal("""{"type":"boolean","value":false}""", At(plugins, "fake-provider", "Enabled").ToJsonString());
        Assert.DoesNotContain("SENTINEL", described.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_plugins_installed_now_are_the_ones_that_are_shown()
    {
        var text = """{"Notifications":{"Plugins":{"PLUGIN":{"Enabled":true}}}}""".Replace("PLUGIN", Plugin, StringComparison.Ordinal);

        var withPlugin = ConfigShape.Describe(text, SettingsAllowlist.Default, Installed);
        var without = ConfigShape.Describe(text, SettingsAllowlist.Default, new HashSet<string>());

        Assert.Equal("""{"type":"boolean","value":true}""", At(withPlugin["shape"]!, "Notifications", "Plugins", Plugin, "Enabled").ToJsonString());
        Assert.Equal(["<key>"], Names(At(without["shape"]!, "Notifications", "Plugins")));
        Assert.Equal("""{"type":"boolean"}""", At(without["shape"]!, "Notifications", "Plugins", "<key>", "Enabled").ToJsonString());
    }

    /// <summary>
    /// What stands under a key that is not shown is described, by its own known keys and by type, but a value is
    /// carried only where every key on the way was shown: the path is judged by what was printed, not by what was typed.
    /// </summary>
    [Fact]
    public void Nothing_beneath_a_key_that_is_not_shown_carries_a_value_though_the_path_below_it_is_listed()
    {
        var described = Describe("""
            {"ghp_SENTINEL":{"Dispatcher":{"MaxParallel":424200014,"Enabled":true},"Logging":{"MinimumLevel":"Warning"},"Manager":{"Triggers":["workitem_failed"]}}}
            """);

        var hidden = At(described["shape"]!, "<key>");
        Assert.Equal("""{"type":"integer"}""", At(hidden, "Dispatcher", "MaxParallel").ToJsonString());
        Assert.Equal("""{"type":"boolean"}""", At(hidden, "Dispatcher", "Enabled").ToJsonString());
        Assert.Null(At(hidden, "Logging", "MinimumLevel")["value"]);
        Assert.Null(At(hidden, "Manager", "Triggers")["values"]);
        Assert.DoesNotContain("\"value", described.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL", described.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain("42420001", described.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>An item of a list is marked <c>[]</c> in a listed path, and a key that ends that way is no marker.</summary>
    [Fact]
    public void A_key_that_ends_like_the_marker_of_a_list_item_is_not_shown_and_carries_nothing()
    {
        var described = Describe("""{"Manager":{"Triggers[]":"workitem_failed","Triggers":["workitem_failed"]}}""");

        var manager = At(described["shape"]!, "Manager");
        Assert.Equal(["<key>", "Triggers"], Names(manager));
        Assert.Equal("""{"type":"string","length":15}""", At(manager, "<key>").ToJsonString());
        Assert.Equal("""["workitem_failed"]""", At(manager, "Triggers")["values"]!.ToJsonString());
    }

    [Fact]
    public void A_published_operation_and_a_public_id_are_shown_as_keys()
    {
        var operation = OperationCatalog.All[0].Id;
        var campaign = PublicId.New("cmp");

        var shape = Shape("""{"Routes":{"Operations":{"OPERATION":{"Plugin":"x"}}},"Notifications":{"Plugins":{"CAMPAIGN":{}}}}""".Replace("OPERATION", operation, StringComparison.Ordinal).Replace("CAMPAIGN", campaign, StringComparison.Ordinal));

        Assert.Equal("string", At(shape, "Routes", "Operations", operation, "Plugin")["type"]!.GetValue<string>());
        Assert.Equal("object", At(shape, "Notifications", "Plugins", campaign)["type"]!.GetValue<string>());
    }

    /// <summary>A number is an index only where a list is: at the whole path of one, and a key somebody chose anywhere else.</summary>
    [Fact]
    public void A_number_is_shown_as_a_key_only_where_it_indexes_a_list()
    {
        var list = Shape("""{"Manager":{"Triggers":{"0":"workitem_failed"}}}""");

        Assert.Equal("workitem_failed", At(list, "Manager", "Triggers", "0")["value"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("""{"Dispatcher":{"4111111111":1}}""", "Dispatcher")]
    [InlineData("""{"Plugins":{"Http":{"4111111111":1}}}""", "Plugins:Http")]
    [InlineData("""{"Plugins":{"Exec":{"4111111111":1}}}""", "Plugins:Exec")]
    [InlineData("""{"Routes":{"Default":{"Binding":{"Env":{"4111111111":"x"}}}}}""", "Routes:Default:Binding:Env")]
    [InlineData("""{"Routes":{"Default":{"Binding":{"Triggers":{"4111111111":"x"}}}}}""", "Routes:Default:Binding:Triggers")]
    [InlineData("""{"Notifications":{"Plugins":{"fake-provider":{"Binding":{"Exec":{"4111111111":1}}}}}}""", "Notifications:Plugins:fake-provider:Binding:Exec")]
    [InlineData("""{"SENTINEL":{"Manager":{"Triggers":{"4111111111":"workitem_failed"}}}}""", "<key>:Manager:Triggers")]
    public void A_number_typed_as_a_key_is_never_printed_where_no_list_stands(string text, string at)
    {
        var described = Describe(text);

        Assert.Equal(["<key>"], Names(At(described["shape"]!, at.Split(':'))));
        Assert.DoesNotContain("4111111111", described.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain("\"value", described.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_object_of_sixty_known_keys_prints_them_and_one_of_sixty_one_does_not()
    {
        var names = SettingsVocabulary.Names.Order(StringComparer.Ordinal).ToList();
        Assert.True(names.Count > 61, "The vocabulary has to hold more than the cap for this test to mean anything.");

        string Object(int count) => "{" + string.Join(",", names.Take(count).Select(name => $"\"{name}\":1")) + "}";

        var sixty = Shape(Object(60));
        var sixtyOne = Shape(Object(61));

        Assert.Equal(60, sixty["members"]!.AsObject().Count);
        Assert.Equal("""{"type":"object","key_count":61,"members_not_shown":"more_than_60_keys"}""", sixtyOne.ToJsonString());
    }

    [Fact]
    public void An_empty_object_has_no_members_and_no_reason()
    {
        Assert.Equal("""{"type":"object","key_count":0,"members":{}}""", Shape("{}").ToJsonString());
    }

    // Values: described by type, length and kind of characters, never by what they say.

    [Fact]
    public void A_string_is_described_by_its_length_in_code_points()
    {
        Assert.Equal("""{"type":"string","length":3}""", Feed("\"abc\""));
        Assert.Equal("""{"type":"string","length":0}""", Feed("\"\""));

        // A character outside the basic plane is two UTF-16 units and one code point; and an escaped one is the same character.
        Assert.Equal("""{"type":"string","length":2,"non_ascii":1}""", Feed("\"a😀\""));
        Assert.Equal("""{"type":"string","length":2,"non_ascii":1}""", Feed("\"a\\ud83d\\ude00\""));
    }

    [Fact]
    public void A_string_says_whether_it_holds_markup_how_many_line_breaks_and_how_many_characters_outside_ascii()
    {
        Assert.Equal("""{"type":"string","length":8,"html":true}""", Feed("\"<b>bold<\""));
        Assert.Equal("""{"type":"string","length":7,"html":true}""", Feed("\"</p>end\""));
        Assert.Equal("""{"type":"string","length":5,"html":true}""", Feed("\"<!--x\""));
        Assert.Equal("""{"type":"string","length":5}""", Feed("\"a < b\""));
        Assert.Equal("""{"type":"string","length":2}""", Feed("\"<3\""));
        Assert.Equal("""{"type":"string","length":8,"line_breaks":3}""", Feed("\"a\\nb\\r\\nc\\rd\""));
        Assert.Equal("""{"type":"string","length":4,"non_ascii":3}""", Feed("\"aéüñ\""));
    }

    [Fact]
    [Critical(Family.S4, "a bundle carries a string's shape, never its text")]
    public void A_string_is_never_described_by_its_text_whatever_it_says()
    {
        var shape = Describe("""{"Update":{"FeedUrl":"sk-SENTINEL-1234 <script>"},"Roles":{"DefaultExecutionProfile":"SENTINEL-profile"}}""");

        Assert.DoesNotContain("SENTINEL", shape.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain("script", shape.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain("value", shape.ToJsonString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("2026-10-04T05:37:12.123Z", "dddd-dd-ddTdd:dd:dd.dddZ")]
    [InlineData("2026-10-04T05:37:12Z", "dddd-dd-ddTdd:dd:ddZ")]
    [InlineData("2026-10-04T05:37", "dddd-dd-ddTdd:dd")]
    [InlineData("2026-10-04T05:37:12+02:00", "dddd-dd-ddTdd:dd:dd+dd:dd")]
    [InlineData("2026-10-04T05:37:12-0530", "dddd-dd-ddTdd:dd:dd-dddd")]
    public void A_timestamp_is_described_by_its_pattern_alone(string timestamp, string pattern)
    {
        var described = Shape("{\"Update\":{\"FeedUrl\":\"" + timestamp + "\"}}")["members"]!["Update"]!["members"]!["FeedUrl"]!;

        // Compared as data: the encoder that writes the shape out may spell a plus sign as an escape.
        Assert.Equal(["type", "timestamp"], described.AsObject().Select(property => property.Key));
        Assert.Equal(pattern, described["timestamp"]!.GetValue<string>());
        Assert.DoesNotContain("2026", described.ToJsonString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("2026-10-04 05:37:12")]
    [InlineData("2026-10-04")]
    [InlineData("2026-10-04T05:37:12Z extra")]
    [InlineData("x2026-10-04T05:37:12Z")]
    [InlineData("2026-10-04T05:37:12Z\\n")]
    [InlineData("26-10-04T05:37:12Z")]
    public void Something_that_is_not_quite_a_timestamp_is_an_ordinary_string(string text)
    {
        var described = Feed($"\"{text}\"");

        Assert.Contains("\"length\"", described, StringComparison.Ordinal);
        Assert.DoesNotContain("timestamp", described, StringComparison.Ordinal);
    }

    [Fact]
    public void A_number_or_a_boolean_or_a_null_is_described_by_its_type()
    {
        Assert.Equal("""{"type":"integer"}""", Feed("5"));
        Assert.Equal("""{"type":"integer"}""", Feed("-5"));
        Assert.Equal("""{"type":"integer"}""", Feed("99999999999999999999999999"));
        Assert.Equal("""{"type":"number"}""", Feed("2.5"));
        Assert.Equal("""{"type":"number"}""", Feed("2.0"));
        Assert.Equal("""{"type":"number"}""", Feed("1e3"));
        Assert.Equal("""{"type":"boolean"}""", Feed("true"));
        Assert.Equal("""{"type":"boolean"}""", Feed("false"));
        Assert.Equal("""{"type":"null"}""", Feed("null"));
    }

    // Lists: their length, and one shape for all their items.

    [Fact]
    public void A_list_of_strings_has_its_length_and_the_range_of_its_items_and_never_an_item()
    {
        var described = Describe("""{"Roles":{"DefaultEntryCommand":["claude","--print","SENTINEL-TOKEN-0123"]}}""");

        var list = At(described["shape"]!, "Roles", "DefaultEntryCommand");
        Assert.Equal(
            """{"type":"array","length":3,"item_types":{"string":3},"strings":{"length_min":6,"length_max":19}}""",
            list.ToJsonString());
        Assert.DoesNotContain("SENTINEL", described.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain("claude", described.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_list_of_strings_says_how_many_of_its_items_hold_markup_line_breaks_or_characters_outside_ascii()
    {
        var list = At(
            Shape("""{"Roles":{"DefaultEntryCommand":["<b>","a\nb","é","plain","<i>é\n"]}}"""),
            "Roles",
            "DefaultEntryCommand");

        Assert.Equal(
            """{"length_min":1,"length_max":5,"with_html":2,"with_line_breaks":2,"with_non_ascii":2}""",
            list["strings"]!.ToJsonString());
    }

    [Fact]
    public void A_list_of_timestamps_is_the_patterns_that_occur()
    {
        var list = At(
            Shape("""{"Roles":{"DefaultEntryCommand":["2026-10-04T05:37:12Z","2027-01-02T01:02:03Z","2026-10-04T05:37"]}}"""),
            "Roles",
            "DefaultEntryCommand");

        Assert.Equal("""{"timestamps":["dddd-dd-ddTdd:dd","dddd-dd-ddTdd:dd:ddZ"]}""", list["strings"]!.ToJsonString());
    }

    [Fact]
    public void An_empty_list_has_a_length_and_nothing_else()
    {
        Assert.Equal("""{"type":"array","length":0}""", At(Shape("""{"Roles":{"DefaultEntryCommand":[]}}"""), "Roles", "DefaultEntryCommand").ToJsonString());
    }

    /// <summary>The union of a list's items: for each key, how many items have it, how many hold null there, and which types.</summary>
    [Fact]
    public void The_objects_in_a_list_are_described_as_one_shape_with_a_count_for_each_key()
    {
        var list = At(
            Shape("""{"Routes":{"Default":{"Binding":{"Port":[1,"a",null,{"Enabled":true,"Port":1},{"Enabled":null,"Toast":"hello","Port":[1,2]},[1,2,3]]}}}}"""),
            "Routes",
            "Default",
            "Binding",
            "Port");

        Assert.Equal(6, list["length"]!.GetValue<int>());
        Assert.Equal("""{"array":1,"integer":1,"null":1,"object":2,"string":1}""", list["item_types"]!.ToJsonString());
        Assert.Equal(
            """
            {"objects":2,"members":{"Enabled":{"present":2,"null":1,"types":["boolean"]},"Port":{"present":2,"null":0,"types":["array","integer"],"array_lengths":[2]},"Toast":{"present":1,"null":0,"types":["string"],"strings":{"length_min":5,"length_max":5}}}}
            """.Trim(),
            list["items"]!.ToJsonString());
        Assert.Equal("[3]", list["array_lengths"]!.ToJsonString());
        Assert.Null(list["values"]);
    }

    /// <summary>
    /// An unknown key in the objects of a list is a placeholder, and the same unknown key in several of them is one
    /// member: that reveals the items share a key, and never which.
    /// </summary>
    [Fact]
    public void Objects_in_a_list_that_share_an_unknown_key_are_one_member_with_a_count_and_the_key_is_not_printed()
    {
        var described = Describe("""{"Roles":{"DefaultEntryCommand":[{"SENTINEL-key":1,"Port":1},{"SENTINEL-key":"a"},{"Port":2,"other-SENTINEL":3}]}}""");

        var items = At(described["shape"]!, "Roles", "DefaultEntryCommand")["items"]!;
        Assert.Equal(["<key>", "Port", "<key 2>"], Names(items));
        Assert.Equal("""{"present":2,"null":0,"types":["integer","string"],"strings":{"length_min":1,"length_max":1}}""", items["members"]!["<key>"]!.ToJsonString());
        Assert.Equal(2, items["members"]!["Port"]!["present"]!.GetValue<int>());
        Assert.Equal(1, items["members"]!["<key 2>"]!["present"]!.GetValue<int>());
        Assert.DoesNotContain("SENTINEL", described.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Objects_nested_in_a_list_under_an_unknown_key_are_described_and_carry_nothing()
    {
        var described = Describe("""{"Roles":{"DefaultEntryCommand":[{"SENTINEL-key":{"Port":424200015}}]}}""");

        var nested = At(described["shape"]!, "Roles", "DefaultEntryCommand")["items"]!["members"]!["<key>"]!["objects"]!;
        Assert.Equal("""{"present":1,"null":0,"types":["integer"]}""", nested["members"]!["Port"]!.ToJsonString());
        Assert.DoesNotContain("42420001", described.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Objects_in_a_list_with_more_than_sixty_keys_between_them_are_counted_and_their_keys_are_not_printed()
    {
        var names = SettingsVocabulary.Names.Order(StringComparer.Ordinal).ToList();
        var first = "{" + string.Join(",", names.Take(40).Select(name => $"\"{name}\":1")) + "}";
        var second = "{" + string.Join(",", names.Skip(30).Take(40).Select(name => $"\"{name}\":1")) + "}";

        var items = At(Shape("{\"Roles\":{\"DefaultEntryCommand\":[" + first + "," + second + "]}}"), "Roles", "DefaultEntryCommand")["items"]!;

        Assert.Equal("""{"objects":2,"members_not_shown":"more_than_60_keys"}""", items.ToJsonString());
    }

    // The list of values that travel, and what keeps every other value off it.

    [Fact]
    public void A_listed_setting_carries_its_value_in_the_shape()
    {
        var shape = Shape("""{"Dispatcher":{"Enabled":false,"MaxParallel":2},"Logging":{"MinimumLevel":"Warning"},"Manager":{"Triggers":["workitem_failed","decision_raised"]}}""");

        Assert.Equal("""{"type":"boolean","value":false}""", At(shape, "Dispatcher", "Enabled").ToJsonString());
        Assert.Equal("""{"type":"integer","value":2}""", At(shape, "Dispatcher", "MaxParallel").ToJsonString());
        Assert.Equal("""{"type":"string","length":7,"value":"Warning"}""", At(shape, "Logging", "MinimumLevel").ToJsonString());
        Assert.Equal(
            """["decision_raised","workitem_failed"]""",
            At(shape, "Manager", "Triggers")["values"]!.ToJsonString());
    }

    /// <summary>The same setting under another spelling is the same setting.</summary>
    [Fact]
    public void A_listed_setting_is_found_whatever_case_its_keys_are_written_in()
    {
        var shape = Shape("""{"dispatcher":{"maxParallel":2},"PLUGINS":{"exec":{"maxcalls":7}}}""");

        Assert.Equal(2, At(shape, "dispatcher", "maxParallel")["value"]!.GetValue<int>());
        Assert.Equal(7, At(shape, "PLUGINS", "exec", "maxcalls")["value"]!.GetValue<int>());
    }

    /// <summary>
    /// Two settings share the last key <c>Exec</c> and a list a person writes under a plugin's grant is made of program
    /// names: a path is matched whole, so the number of calls is carried and nothing under a grant is.
    /// </summary>
    [Fact]
    public void A_setting_that_shares_its_last_key_with_a_listed_one_is_not_carried()
    {
        var described = Describe("""
            {"Plugins":{"Exec":{"MaxCalls":7},"Grants":{"fake-provider":{"Exec":{"MaxCalls":424200008},"Http":["424200009"]}}}}
            """);

        var shape = described["shape"]!;
        Assert.Equal(7, At(shape, "Plugins", "Exec", "MaxCalls")["value"]!.GetValue<int>());
        Assert.Null(At(shape, "Plugins", "Grants", Plugin, "Exec", "MaxCalls")["value"]);
        Assert.DoesNotContain("42420000", described.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_listed_integer_that_holds_anything_but_an_integer_is_shape_only()
    {
        var described = Describe("""
            {"Dispatcher":{"MaxParallel":"SENTINEL-TEXT","TickSeconds":2.5,"DrainSeconds":true,"RetryDelaySeconds":null,"ExitGraceSeconds":[1],"AiRole":{"TimeoutSeconds":{"Port":1}}}}
            """);

        var dispatcher = At(described["shape"]!, "Dispatcher");
        Assert.Equal("""{"type":"string","length":13}""", At(dispatcher, "MaxParallel").ToJsonString());
        Assert.Equal("""{"type":"number"}""", At(dispatcher, "TickSeconds").ToJsonString());
        Assert.Equal("""{"type":"boolean"}""", At(dispatcher, "DrainSeconds").ToJsonString());
        Assert.Equal("""{"type":"null"}""", At(dispatcher, "RetryDelaySeconds").ToJsonString());
        Assert.DoesNotContain("SENTINEL", described.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain("\"value\"", described.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A number the runtime's own binder would refuse for the setting is free text to it, and its message never says it
    /// back; so it is not said here either. Only the size limit of a log file is wider than 32 bits.
    /// </summary>
    [Fact]
    public void A_listed_integer_the_setting_cannot_hold_is_shape_only()
    {
        var described = Describe("""
            {"Runtime":{"Port":9999999999},"Logging":{"FileSizeLimitBytes":9999999999,"RetainedFileCountLimit":2147483648},"Manager":{"Priority":-2147483649}}
            """);

        var shape = described["shape"]!;
        Assert.Equal("""{"type":"integer"}""", At(shape, "Runtime", "Port").ToJsonString());
        Assert.Equal("""{"type":"integer"}""", At(shape, "Logging", "RetainedFileCountLimit").ToJsonString());
        Assert.Equal("""{"type":"integer"}""", At(shape, "Manager", "Priority").ToJsonString());
        Assert.Equal(9999999999, At(shape, "Logging", "FileSizeLimitBytes")["value"]!.GetValue<long>());
        Assert.DoesNotContain("-214748", described.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_listed_switch_that_holds_anything_but_a_boolean_is_shape_only()
    {
        var described = Describe("""{"Dispatcher":{"Enabled":"SENTINEL-TEXT"},"Update":{"CheckEnabled":1},"Notifications":{"Toast":"true"}}""");

        Assert.DoesNotContain("SENTINEL", described.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain("\"value\"", described.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A value the runtime would refuse is free text, and being at a listed path does not make it anything else: the
    /// path says what was meant to be there, not what is.
    /// </summary>
    [Fact]
    public void A_word_the_runtime_would_refuse_never_travels_even_at_a_path_that_lists_words()
    {
        var described = Describe("""
            {"Logging":{"MinimumLevel":"SENTINEL-LEVEL"},"Manager":{"Triggers":["workitem_failed","SENTINEL-TRIGGER","WORKITEM_FAILED",7,null,["workitem_failed"]]}}
            """);

        var triggers = At(described["shape"]!, "Manager", "Triggers");
        Assert.Equal("""["workitem_failed"]""", triggers["values"]!.ToJsonString());
        Assert.Null(At(described["shape"]!, "Logging", "MinimumLevel")["value"]);
        Assert.DoesNotContain("SENTINEL", described.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain("WORKITEM", described.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A listed setting does not name a list of records, so a value inside one is never carried however the record's
    /// keys are spelled.
    /// </summary>
    [Fact]
    public void A_listed_setting_written_as_a_list_of_records_carries_nothing()
    {
        var described = Describe("""
            {"Manager":{"Triggers":[{"Priority":424200010,"Enabled":true}]},"Dispatcher":{"MaxParallel":[{"MaxParallel":424200011}]}}
            """);

        Assert.DoesNotContain("42420001", described.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain("\"value\"", described.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_setting_named_as_never_carried_is_shape_only_even_when_it_holds_what_it_should()
    {
        var described = Describe("""
            {
              "Update":{"FeedUrl":"https://SENTINEL.example/feed.json"},
              "Roles":{"DefaultExecutionProfile":"sentinel-profile","DefaultEntryCommand":["sentinel-command"]},
              "Plugins":{"Grants":{"fake-provider":{"Exec":["sentinel-program"],"Http":["sentinel.example"],"Env":["SENTINEL_VARIABLE"]}}},
              "Routes":{"Default":{"Plugin":"sentinel-plugin","Binding":{"Port":424200012,"Enabled":true}}},
              "Notifications":{"Plugins":{"fake-provider":{"Enabled":true,"Binding":{"Port":424200013,"Toast":true}}}}
            }
            """);

        var text = described.ToJsonString();
        Assert.DoesNotContain("SENTINEL", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("42420001", text, StringComparison.Ordinal);

        // The one value on that page that is on the list is the one that says whether the channel is on.
        Assert.True(At(described["shape"]!, "Notifications", "Plugins", Plugin, "Enabled")["value"]!.GetValue<bool>());
    }

    // The generated document: a sentinel at every leaf of every setting, and only the listed ones are found.

    /// <summary>
    /// Every leaf a settings file has, by path, each with a value of its own that nobody would write by accident. A
    /// path listed as <c>fake-provider</c> stands for any one installed plugin. The shape of the list is the settings
    /// vocabulary; whether each leaf may travel is not decided here but by the paths this class names.
    /// </summary>
    private static IReadOnlyList<(string Path, bool IsInteger)> Leaves()
    {
        var operation = OperationCatalog.All[0].Id;

        var leaves = new List<(string, bool)>();
        foreach (var path in SettingsAllowlistTests.Integers)
        {
            leaves.Add((path, true));
        }

        // A switch and a word hold a switch and a word, so a number in either place is no value of theirs.
        foreach (var path in SettingsAllowlistTests.Switches.Concat(SettingsAllowlistTests.Words))
        {
            leaves.Add((path, false));
        }

        foreach (var path in new[]
                 {
                     // Strings and lists of strings, none of which is ever a value.
                     "Update:FeedUrl", "Roles:DefaultExecutionProfile", "Roles:DefaultEntryCommand[]",
                     "Routes:Default:Plugin", $"Routes:Operations:{operation}:Plugin",
                     "Plugins:Grants:fake-provider:Exec[]", "Plugins:Grants:fake-provider:Http[]", "Plugins:Grants:fake-provider:Env[]",

                     // A binding is free-form: whatever a person typed under a name the settings know, so these
                     // are the names that would collide with a listed one if a path were not matched whole.
                     "Routes:Default:Binding:Port", "Routes:Default:Binding:Enabled", "Routes:Default:Binding:MaxCalls",
                     "Routes:Default:Binding:MinimumLevel", "Routes:Default:Binding:Triggers[]",
                     $"Routes:Operations:{operation}:Binding:Port", $"Routes:Operations:{operation}:Binding:MaxCalls",
                     "Notifications:Plugins:fake-provider:Binding:Port", "Notifications:Plugins:fake-provider:Binding:Enabled",
                     "Notifications:Plugins:fake-provider:Binding:QuietSeconds",
                     "Plugins:Grants:fake-provider:Limits:TimeoutMs",
                 })
        {
            leaves.Add((path, false));
        }

        return leaves;
    }

    /// <summary>One document holding every leaf: <paramref name="value"/> says what is written at each.</summary>
    private static string Document(Func<int, string, bool, JsonNode> value)
    {
        var root = new JsonObject();
        var leaves = Leaves();
        for (var index = 0; index < leaves.Count; index++)
        {
            var (path, isInteger) = leaves[index];
            var isList = path.EndsWith("[]", StringComparison.Ordinal);
            var keys = (isList ? path[..^2] : path).Split(':');

            var parent = root;
            foreach (var key in keys[..^1])
            {
                parent = (JsonObject)(parent[key] ??= new JsonObject());
            }

            var leaf = value(index, path, isInteger);
            if (isList)
            {
                ((JsonArray)(parent[keys[^1]] ??= new JsonArray())).Add(leaf);
            }
            else
            {
                parent[keys[^1]] = leaf;
            }
        }

        return root.ToJsonString();
    }

    private static long Sentinel(int index) => 424_200_100 + index;

    /// <summary>
    /// Every leaf holds a value that is of the right kind for what the setting is — an integer where a number belongs
    /// — and distinct from every other. What shows up in the shape is exactly the integers at the paths the list names.
    /// </summary>
    [Fact]
    public void Of_a_sentinel_at_every_leaf_only_the_listed_integers_appear()
    {
        AssertOnlyTheListedIntegersAppear(Installed);
    }

    /// <summary>
    /// The same document described on a machine with no plugin installed. Every key a plugin's id stood for is now a
    /// placeholder, and the listed integers, which are under no plugin's key, still appear.
    /// </summary>
    [Fact]
    public void Of_a_sentinel_at_every_leaf_with_no_plugin_installed_the_listed_integers_still_appear_and_nothing_else()
    {
        AssertOnlyTheListedIntegersAppear(new HashSet<string>());
    }

    private static void AssertOnlyTheListedIntegersAppear(IReadOnlySet<string> installed)
    {
        var leaves = Leaves();
        var document = Document((index, _, _) => JsonValue.Create(Sentinel(index)));

        var shape = ConfigShape.Describe(document, SettingsAllowlist.Default, installed).ToJsonString();

        for (var index = 0; index < leaves.Count; index++)
        {
            var found = shape.Contains(Sentinel(index).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            Assert.True(found == leaves[index].IsInteger, $"{leaves[index].Path}: carried {found}, listed {leaves[index].IsInteger}");
        }
    }

    /// <summary>The same document with a string in every leaf, the listed ones included: none of them is the kind the list names.</summary>
    [Fact]
    [Critical(Family.S4, "the sentinel sweep over settings")]
    public void Of_a_sentinel_string_at_every_leaf_none_appears_not_even_where_the_path_is_listed()
    {
        var document = Document((index, _, _) => JsonValue.Create($"SENTINEL-{index}-TEXT"));

        var shape = ConfigShape.Describe(document, SettingsAllowlist.Default, Installed).ToJsonString();

        Assert.DoesNotContain("SENTINEL", shape, StringComparison.Ordinal);
        Assert.DoesNotContain("\"value\"", shape, StringComparison.Ordinal);
    }

    /// <summary>The same document with every leaf a list of sentinels or an object holding one: nothing is a value there.</summary>
    [Fact]
    public void Of_a_sentinel_inside_a_list_or_an_object_at_every_leaf_none_appears()
    {
        var asList = Document((index, _, _) => new JsonArray(JsonValue.Create(Sentinel(index)), JsonValue.Create($"SENTINEL-{index}")));
        var asObject = Document((index, _, _) => new JsonObject { ["Port"] = Sentinel(index), ["Enabled"] = $"SENTINEL-{index}" });

        foreach (var document in new[] { asList, asObject })
        {
            var shape = ConfigShape.Describe(document, SettingsAllowlist.Default, Installed).ToJsonString();

            Assert.DoesNotContain("SENTINEL", shape, StringComparison.Ordinal);
            Assert.DoesNotContain("424200", shape, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The whole document of integers, put where none of it may be carried: under a key nobody knows, under a key that
    /// looks like the placeholder or like the marker of a list item, and under a known name that makes every path of it
    /// a path that is on no list. Every listed leaf is a leaf the list names only at its own place.
    /// </summary>
    [Fact]
    public void Of_a_sentinel_at_every_leaf_none_appears_beneath_a_key_that_is_not_shown_or_not_where_the_list_has_it()
    {
        var document = Document((index, _, _) => JsonValue.Create(Sentinel(index)));

        foreach (var wrapper in new[] { "SENTINEL-wrapper", "<key>", "<key 2>", "Manager:Triggers[]", "Dispatcher", "Plugins:Exec" })
        {
            var wrapped = new JsonObject { [wrapper] = JsonNode.Parse(document) }.ToJsonString();

            var shape = Describe(wrapped).ToJsonString();

            Assert.DoesNotContain("424200", shape, StringComparison.Ordinal);
            Assert.DoesNotContain("SENTINEL", shape, StringComparison.Ordinal);
        }
    }

    /// <summary>The same document in an object of more than sixty keys: nothing of what is in it is described at all.</summary>
    [Fact]
    public void Of_a_sentinel_at_every_leaf_none_appears_in_an_object_of_more_than_sixty_keys()
    {
        var root = JsonNode.Parse(Document((index, _, _) => JsonValue.Create(Sentinel(index))))!.AsObject();
        foreach (var name in SettingsVocabulary.Names.Order(StringComparer.Ordinal).Where(name => !root.ContainsKey(name)).Take(70))
        {
            root[name] = 1;
        }

        Assert.True(root.Count > 60);

        var described = Describe(root.ToJsonString());

        Assert.Equal("more_than_60_keys", described["shape"]!["members_not_shown"]!.GetValue<string>());
        Assert.DoesNotContain("424200", described.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The paths whose values the description carries are said by the description itself, where each value is written:
    /// a setting's path as the file spells it, a list's items as the list's path and <c>[]</c>, and nothing it did not carry.
    /// </summary>
    [Fact]
    public void The_description_says_which_settings_it_carried_a_value_for()
    {
        ConfigShape.Describe(
            """{ "dispatcher": { "maxParallel": 2, "Enabled": "yes" }, "Manager": { "Triggers": ["workitem_failed", "SENTINEL"] }, "Update": { "FeedUrl": "x" }, "Notifications": { "Plugins": { "fake-provider": { "Enabled": true } } } }""",
            SettingsAllowlist.Default,
            Installed,
            out var carried);

        Assert.Equal(["dispatcher:maxParallel", "Manager:Triggers[]", "Notifications:Plugins:fake-provider:Enabled"], carried);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{ not json")]
    [InlineData("""{ "Update": { "FeedUrl": "x" } }""")]
    public void A_description_that_carries_nothing_says_so(string? text)
    {
        ConfigShape.Describe(text, SettingsAllowlist.Default, Installed, out var carried);

        Assert.Empty(carried);
    }
}
