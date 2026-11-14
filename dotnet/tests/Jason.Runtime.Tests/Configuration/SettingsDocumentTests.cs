using System.Text;
using System.Text.Json.Nodes;
using Jason.Runtime.Configuration;

namespace Jason.Runtime.Tests.Configuration;

/// <summary>
/// The settings writer's edit of the file's bytes, with no file and no runtime: it changes the members a caller owns and
/// keeps every other byte, refuses what it cannot change that way, and holds its own result to the runtime's parser.
/// </summary>
public class SettingsDocumentTests
{
    private const string Secret = "SENTINEL-SECRET-TOKEN";

    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    /// <summary>
    /// A file as a person keeps one: a byte order mark, Windows line endings, tabs, both kinds of comment, trailing commas,
    /// the section and its keys in cases of their own, and a plugin's secret beside the route.
    /// </summary>
    private static readonly string Kept =
        "{\r\n" +
        "\t// the person's own words, with a /* that opens nothing\r\n" +
        "\t\"routes\": {\r\n" +
        "\t\t/* default first */ \"DEFAULT\": { \"Plugin\": \"fake\" },\r\n" +
        "\t\t\"OPERATIONS\": {\r\n" +
        "\t\t\t\"contact.find\": { \"PLUGIN\": \"fake\", \"Binding\": {\"seat\": 1} }, // trailing, too\r\n" +
        "\t\t},\r\n" +
        "\t},\r\n" +
        "\t\"Plugins\": { \"Config\": { \"fake\": { \"token\": \"" + Secret + "\" } } }\r\n" +
        "}\r\n";

    private static byte[] Bytes(string text, bool bom = false) => bom ? [.. Bom, .. Encoding.UTF8.GetBytes(text)] : Encoding.UTF8.GetBytes(text);

    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes.AsSpan().StartsWith(Bom) ? bytes[Bom.Length..] : bytes);

    private static SettingsEditResult Edit(string? text, params SettingsEdit[] edits) =>
        SettingsDocument.Edit(text is null ? null : Bytes(text), edits);

    [Fact]
    public void An_edit_keeps_every_byte_it_does_not_own()
    {
        var original = Bytes(Kept, bom: true);

        var moved = SettingsDocument.Edit(
            original,
            [
                SettingsEdit.Set("Routes:Operations:contact.find:Plugin", JsonValue.Create("other")),
                SettingsEdit.Set("Routes:Operations:contact.find:Binding", new JsonObject { ["seat"] = 2 }),
            ]);

        // Exactly the two values, each where it was written, under the key spelled as the person spelled it; the byte order
        // mark, the comments, the tabs, the trailing commas and the secret are where they were.
        Assert.True(moved.Changed);
        Assert.True(moved.Bytes.AsSpan().StartsWith(Bom));
        Assert.Equal(
            Kept.Replace("{ \"PLUGIN\": \"fake\", \"Binding\": {\"seat\": 1} }", "{ \"PLUGIN\": \"other\", \"Binding\": {\"seat\":2} }", StringComparison.Ordinal),
            Text(moved.Bytes));

        // A member inserted into an object written one member to a line goes on a line of its own after the last one's
        // line, past the comment ending it, at its siblings' indentation, in the file's own newline and indentation unit.
        var added = SettingsDocument.Edit(moved.Bytes, [SettingsEdit.Set("Routes:Operations:campaign.get:Plugin", JsonValue.Create("third"))]);
        Assert.Equal(
            Text(moved.Bytes).Replace(
                "// trailing, too\r\n",
                "// trailing, too\r\n\t\t\t\"campaign.get\": {\r\n\t\t\t\t\"Plugin\": \"third\"\r\n\t\t\t},\r\n",
                StringComparison.Ordinal),
            Text(added.Bytes));

        // Taking both away gives back the file as it was, byte for byte.
        var back = SettingsDocument.Edit(
            added.Bytes,
            [
                SettingsEdit.Remove("Routes:Operations:campaign.get:Plugin"),
                SettingsEdit.RemoveIfEmpty("Routes:Operations:campaign.get"),
                SettingsEdit.Set("Routes:Operations:contact.find:Plugin", JsonValue.Create("fake")),
                SettingsEdit.Set("Routes:Operations:contact.find:Binding", new JsonObject { ["seat"] = 1 }),
            ]);
        Assert.Equal(
            Kept.Replace("{\"seat\": 1}", "{\"seat\":1}", StringComparison.Ordinal),
            Text(back.Bytes));
        Assert.True(back.Bytes.AsSpan().StartsWith(Bom));
    }

    [Fact]
    public void A_set_that_holds_already_changes_no_byte()
    {
        var result = Edit(Kept, SettingsEdit.Set("Routes:Operations:contact.find:Binding", new JsonObject { ["seat"] = 1 }));

        Assert.False(result.Changed);
        Assert.Equal(Kept, Text(result.Bytes));
    }

    [Fact]
    public void A_set_of_an_operations_own_route_keeps_its_channel_routes_and_the_other_way_round()
    {
        const string file = """
            {
              "Routes": {
                "Operations": {
                  "message.send": {
                    "Plugin": "mailer",
                    "Channels": { "linkedin": { "Plugin": "social" } }
                  }
                }
              }
            }
            """;

        var own = Edit(file, SettingsEdit.Remove("Routes:Operations:message.send:Plugin"), SettingsEdit.Remove("Routes:Operations:message.send:Binding"), SettingsEdit.RemoveIfEmpty("Routes:Operations:message.send"));
        var keys = SettingsDocument.Flatten(own.Bytes);
        Assert.Equal("social", keys["Routes:Operations:message.send:Channels:linkedin:Plugin"]);
        Assert.False(keys.ContainsKey("Routes:Operations:message.send:Plugin"));

        var channel = Edit(
            file,
            SettingsEdit.Remove("Routes:Operations:message.send:Channels:linkedin:Plugin"),
            SettingsEdit.Remove("Routes:Operations:message.send:Channels:linkedin:Binding"),
            SettingsEdit.RemoveIfEmpty("Routes:Operations:message.send:Channels:linkedin"));
        keys = SettingsDocument.Flatten(channel.Bytes);
        Assert.Equal("mailer", keys["Routes:Operations:message.send:Plugin"]);
        Assert.DoesNotContain(keys.Keys, key => key.Contains("linkedin", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// An object with no member is a key of its own to the configuration system, and an operation's key with nothing under
    /// it reads as an operation routed to no plugin: an unset that left one behind would make the next reload refuse.
    /// </summary>
    [Fact]
    public void An_unset_that_empties_a_route_object_removes_it_and_nothing_above_it()
    {
        const string file = "{\n  \"Routes\": {\n    \"Operations\": {\n      \"campaign.get\": { \"Plugin\": \"p\" }\n    }\n  }\n}\n";

        var result = Edit(file, SettingsEdit.Remove("Routes:Operations:campaign.get:Plugin"), SettingsEdit.Remove("Routes:Operations:campaign.get:Binding"), SettingsEdit.RemoveIfEmpty("Routes:Operations:campaign.get"));

        Assert.Equal("{\n  \"Routes\": {\n    \"Operations\": {\n    }\n  }\n}\n", Text(result.Bytes));
        Assert.False(SettingsDocument.Flatten(result.Bytes).ContainsKey("Routes:Operations:campaign.get"));
    }

    [Fact]
    public void Deleting_a_member_takes_its_separating_comma_and_leaves_no_empty_line()
    {
        const string file = "{\n  \"A\": 1,\n  \"B\": 2,\n  \"C\": { \"x\": 1, \"y\": 2 }\n}\n";

        Assert.Equal("{\n  \"B\": 2,\n  \"C\": { \"x\": 1, \"y\": 2 }\n}\n", Text(Edit(file, SettingsEdit.Remove("A")).Bytes));
        Assert.Equal("{\n  \"A\": 1,\n  \"B\": 2\n}\n", Text(Edit(file, SettingsEdit.Remove("C")).Bytes));
        Assert.Equal("{\n  \"A\": 1,\n  \"B\": 2,\n  \"C\": { \"y\": 2 }\n}\n", Text(Edit(file, SettingsEdit.Remove("C:x")).Bytes));
        Assert.Equal("{\n  \"A\": 1,\n  \"B\": 2,\n  \"C\": { \"x\": 1 }\n}\n", Text(Edit(file, SettingsEdit.Remove("C:y")).Bytes));
        Assert.False(Edit(file, SettingsEdit.Remove("D")).Changed);
    }

    [Theory]
    [InlineData(null, "{\n  \"Routes\": {\n    \"Default\": {\n      \"Plugin\": \"p\"\n    }\n  }\n}\n")]
    [InlineData("{}", "{\n  \"Routes\": {\n    \"Default\": {\n      \"Plugin\": \"p\"\n    }\n  }\n}")]
    [InlineData("{\r\n}\r\n", "{\r\n  \"Routes\": {\r\n    \"Default\": {\r\n      \"Plugin\": \"p\"\r\n    }\r\n  }\r\n}\r\n")]
    [InlineData("{\"Dispatcher\":{\"TickSeconds\":7}}", "{\"Dispatcher\":{\"TickSeconds\":7}, \"Routes\": {\"Default\": {\"Plugin\": \"p\"}}}")]
    [InlineData("{\"Dispatcher\":{\"TickSeconds\":7},}", "{\"Dispatcher\":{\"TickSeconds\":7}, \"Routes\": {\"Default\": {\"Plugin\": \"p\"}},}")]
    [InlineData("{\n    \"Dispatcher\": {\n        \"TickSeconds\": 7\n    }\n}\n", "{\n    \"Dispatcher\": {\n        \"TickSeconds\": 7\n    },\n    \"Routes\": {\n        \"Default\": {\n            \"Plugin\": \"p\"\n        }\n    }\n}\n")]
    public void Missing_objects_are_composed_in_the_files_own_layout(string? file, string expected)
    {
        var result = Edit(file, SettingsEdit.Set("Routes:Default:Plugin", JsonValue.Create("p")));

        Assert.True(result.Changed);
        Assert.Equal(expected, Text(result.Bytes));
    }

    [Theory]
    [InlineData("{\"Routes\":{\"Default\":{\"Plugin\":\"a\"}},\"ROUTES\":{\"Operations\":{}}}", SettingsRefusal.Twice, "Routes")]
    [InlineData("{\"Routes\":{\"Operations\":{\"a\":{\"Plugin\":\"x\"}},\"operations\":{\"b\":{\"Plugin\":\"y\"}}}}", SettingsRefusal.Twice, "Routes:Operations")]
    [InlineData("{\"Routes\":{\"Operations\":{\"contact.find\":{\"Plugin\":\"a\",\"plugin\":\"b\"}}}}", SettingsRefusal.Unreadable, null)]
    [InlineData("{\"Routes:Operations\":{}}", SettingsRefusal.ColonKey, "Routes")]
    [InlineData("{\"Routes\":{\"Operations:contact.find\":{\"Plugin\":\"a\"}}}", SettingsRefusal.ColonKey, "Routes:Operations")]
    [InlineData("{\"Routes\":{\"Operations\":{\"contact.find\":{\"Plugin:x\":\"a\"}}}}", SettingsRefusal.ColonKey, "Routes:Operations:contact.find:Plugin")]
    [InlineData("{\"Routes\":\"x\"}", SettingsRefusal.NotAnObject, "Routes")]
    [InlineData("{\"Routes\":{\"Operations\":[]}}", SettingsRefusal.NotAnObject, "Routes:Operations")]
    [InlineData("{\"Routes\":{\"Operations\":null}}", SettingsRefusal.NotAnObject, "Routes:Operations")]
    public void A_setting_written_twice_or_through_a_value_that_is_not_an_object_is_not_edited(string file, SettingsRefusal refusal, string? setting)
    {
        var refused = Assert.Throws<SettingsFileRefusedException>(() => Edit(file, SettingsEdit.Set("Routes:Operations:contact.find:Plugin", JsonValue.Create("p"))));

        Assert.Equal(refusal, refused.Refusal);
        Assert.Equal(setting, refused.Setting);
    }

    [Fact]
    public void A_file_in_another_encoding_is_not_edited()
    {
        byte[] utf16 = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("{\"Routes\":{}}")];

        var refused = Assert.Throws<SettingsFileRefusedException>(() => SettingsDocument.Edit(utf16, [SettingsEdit.Set("Routes:Default:Plugin", JsonValue.Create("p"))]));

        Assert.Equal(SettingsRefusal.NotUtf8, refused.Refusal);
    }

    /// <summary>The parser's own place, counted from one as an editor counts, and nothing of the file in the refusal.</summary>
    [Fact]
    public void A_malformed_file_is_refused_with_its_line_and_byte()
    {
        var file = "{\n  \"Routes\": {\n    \"Default\": { \"Plugin\": \"" + Secret + "\" ]\n}";

        var refused = Assert.Throws<SettingsFileRefusedException>(() => Edit(file, SettingsEdit.Set("Routes:Default:Plugin", JsonValue.Create("p"))));

        Assert.Equal(SettingsRefusal.Unreadable, refused.Refusal);
        Assert.Equal(3, refused.Failure!.LineNumber);
        Assert.Equal(31 + Secret.Length, refused.Failure.BytePositionInLine);
        Assert.Equal($"at line 3, byte {31 + Secret.Length}", refused.Failure.Place());
        Assert.DoesNotContain(Secret, refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{\"a\":\"\\ud800\"}")]
    public void A_file_the_parser_does_not_read_is_refused_whole(string file)
    {
        var refused = Assert.Throws<SettingsFileRefusedException>(() => Edit(file, SettingsEdit.Set("Routes:Default:Plugin", JsonValue.Create("p"))));

        Assert.Equal(SettingsRefusal.Unreadable, refused.Refusal);
        Assert.Null(refused.Setting);
    }

    /// <summary>
    /// The post-condition, given an output the span logic did not make: one that changed the secret beside the route as
    /// well as the route. It refuses, and says nothing of either value.
    /// </summary>
    [Fact]
    public void The_post_condition_refuses_an_edit_that_changed_a_setting_it_does_not_own()
    {
        var before = SettingsDocument.Flatten(Bytes(Kept));
        var edits = new[] { SettingsEdit.Set("Routes:Operations:contact.find:Plugin", JsonValue.Create("other")) };
        var good = Kept.Replace("\"PLUGIN\": \"fake\"", "\"PLUGIN\": \"other\"", StringComparison.Ordinal);

        SettingsDocument.Verify(before, Bytes(good), edits);

        var leaked = good.Replace(Secret, "CHANGED", StringComparison.Ordinal);
        var refused = Assert.Throws<SettingsFileRefusedException>(() => SettingsDocument.Verify(before, Bytes(leaked), edits));
        Assert.Equal(SettingsRefusal.CheckRefused, refused.Refusal);
        Assert.DoesNotContain(Secret, refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("CHANGED", refused.Message, StringComparison.Ordinal);

        // A key that came from nowhere is refused as well, and so is one that went.
        var grown = good.Replace("\"Plugins\": {", "\"Extra\": 1, \"Plugins\": {", StringComparison.Ordinal);
        Assert.Throws<SettingsFileRefusedException>(() => SettingsDocument.Verify(before, Bytes(grown), edits));
        var shrunk = good.Replace("/* default first */ \"DEFAULT\": { \"Plugin\": \"fake\" },", string.Empty, StringComparison.Ordinal);
        Assert.Throws<SettingsFileRefusedException>(() => SettingsDocument.Verify(before, Bytes(shrunk), edits));

        // An empty object or a null where the edit wrote a value is no value: the parser reads both as a key without
        // one, at the edited path itself, and that is excused only above an edited path.
        foreach (var hollow in new[] { "{}", "null" })
        {
            var emptied = Kept.Replace("\"PLUGIN\": \"fake\"", "\"PLUGIN\": " + hollow, StringComparison.Ordinal);
            Assert.Throws<SettingsFileRefusedException>(() => SettingsDocument.Verify(before, Bytes(emptied), edits));
        }

        // A removal that left an empty object where the member was has not removed it.
        var removal = new[] { SettingsEdit.Remove("Routes:Operations:contact.find:Binding") };
        var removed = Kept.Replace(", \"Binding\": {\"seat\": 1}", string.Empty, StringComparison.Ordinal);
        SettingsDocument.Verify(before, Bytes(removed), removal);
        var hollowed = Kept.Replace("\"Binding\": {\"seat\": 1}", "\"Binding\": {}", StringComparison.Ordinal);
        Assert.Throws<SettingsFileRefusedException>(() => SettingsDocument.Verify(before, Bytes(hollowed), removal));
    }

    /// <summary>
    /// The edit owns whatever paths its caller names, and nothing in it is a route's: a plugin's grant list is set and taken
    /// away the same way, with the routes and the secret beside it kept byte for byte.
    /// </summary>
    [Fact]
    public void A_path_outside_the_routes_is_owned_and_edited_the_same_way()
    {
        var granted = Edit(Kept, SettingsEdit.Set("Plugins:Grants:fake:Http", new JsonArray("api.example.com", "files.example.com")));

        Assert.Equal(
            Kept.Replace(
                "\"Plugins\": { \"Config\": { \"fake\": { \"token\": \"" + Secret + "\" } } }",
                "\"Plugins\": { \"Config\": { \"fake\": { \"token\": \"" + Secret + "\" } }, \"Grants\": {\"fake\": {\"Http\": [\"api.example.com\",\"files.example.com\"]}} }",
                StringComparison.Ordinal),
            Text(granted.Bytes));
        var keys = SettingsDocument.Flatten(granted.Bytes);
        Assert.Equal("files.example.com", keys["Plugins:Grants:fake:Http:1"]);

        var revoked = SettingsDocument.Edit(granted.Bytes, [SettingsEdit.Remove("Plugins:Grants:fake:Http"), SettingsEdit.RemoveIfEmpty("Plugins:Grants:fake"), SettingsEdit.RemoveIfEmpty("Plugins:Grants")]);
        Assert.Equal(Kept, Text(revoked.Bytes));
    }
}
