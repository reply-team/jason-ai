using System.Text.Json;
using Jason.Cli.Moving;

namespace Jason.Cli.Tests.Moving;

/// <summary>
/// The settings file of another machine made this machine's: each path under the other machine's home, data directory or
/// harness root moved to the same path here, the root named, and every other byte of the file left as it was written.
/// </summary>
public sealed class SettingsRewriteTests
{
    private static readonly string Sep = Path.DirectorySeparatorChar.ToString();

    private static readonly string HereHome = Path.Combine(Path.GetTempPath(), "made-up-home");

    private static readonly string HereData = Path.Combine(HereHome, ".jason");

    private static readonly string HereRoot = Path.Combine(HereHome, "Jason");

    private static readonly PlaceMoved[] FromLinux =
    [
        new("/home/pat", HereHome),
        new("/home/pat/.jason", HereData),
        new("/home/pat/Jason", HereRoot),
    ];

    private static readonly PlaceMoved[] FromWindows =
    [
        new(@"C:\Users\pat", HereHome),
        new(@"C:\Users\pat\.jason", HereData),
        new(@"C:\Users\pat\Jason", HereRoot),
    ];

    private static bool Nothing(string path) => false;

    [Fact]
    public void Only_the_moved_strings_change_and_comments_spacing_order_and_every_other_value_stay()
    {
        const string Text = "{\n  // kept\n  \"Plugins\": {\"Config\": {\"reply\": {\"token\": \"made-up-secret\",   \"exports\": \"/home/pat/exports\"}}},\n  /* kept too */ \"Dispatcher\": {\"MaxParallel\": 2, \"Enabled\": true,},\n  \"Name\": \"not a path\"\n}\n";

        var result = SettingsRewrite.Rewrite(Text, "linux", FromLinux, HereRoot, nameRoot: false, Nothing);

        var expected = Text.Replace("\"/home/pat/exports\"", JsonSerializer.Serialize(HereHome + Sep + "exports"), StringComparison.Ordinal);
        Assert.Equal(expected, result.Text);
        var rewrite = Assert.Single(result.Rewritten);
        Assert.Equal(new SettingRewritten("Plugins:Config:reply:exports", "/home/pat/exports", HereHome + Sep + "exports"), rewrite);
        Assert.Empty(result.NotHere);
    }

    [Fact]
    public void The_longest_place_a_path_is_under_decides_where_it_goes()
    {
        var result = SettingsRewrite.Rewrite("{\"A\": \"/home/pat/.jason/logs/copy\", \"B\": \"/home/pat/Jason\"}", "linux", FromLinux, HereRoot, nameRoot: false, Nothing);

        Assert.Equal(
            [new SettingRewritten("A", "/home/pat/.jason/logs/copy", HereData + Sep + "logs" + Sep + "copy"), new SettingRewritten("B", "/home/pat/Jason", HereRoot)],
            result.Rewritten);
    }

    [Fact]
    public void A_name_that_only_begins_like_a_place_is_not_under_it()
    {
        var result = SettingsRewrite.Rewrite("{\"A\": \"/home/patrick/notes\"}", "linux", FromLinux, HereRoot, nameRoot: false, Nothing);

        Assert.Empty(result.Rewritten);
        Assert.Equal([new SettingNotHere("A", "/home/patrick/notes")], result.NotHere);
    }

    [Fact]
    public void A_path_written_on_Windows_is_moved_whatever_its_case_and_separators()
    {
        var text = "{\"A\": " + JsonSerializer.Serialize(@"c:\users\PAT\exports\q4") + ", \"B\": \"C:/Users/pat/Jason/projects\"}";

        var result = SettingsRewrite.Rewrite(text, "windows", FromWindows, HereRoot, nameRoot: false, Nothing);

        Assert.Equal(
            [
                new SettingRewritten("A", @"c:\users\PAT\exports\q4", HereHome + Sep + "exports" + Sep + "q4"),
                new SettingRewritten("B", "C:/Users/pat/Jason/projects", HereRoot + Sep + "projects"),
            ],
            result.Rewritten);
    }

    [Fact]
    public void A_path_written_on_Linux_is_held_to_its_case()
    {
        var result = SettingsRewrite.Rewrite("{\"A\": \"/home/PAT/exports\"}", "linux", FromLinux, HereRoot, nameRoot: false, Nothing);

        Assert.Empty(result.Rewritten);
        Assert.Equal([new SettingNotHere("A", "/home/PAT/exports")], result.NotHere);
    }

    [Fact]
    public void A_path_outside_the_three_places_is_left_as_written_and_said_only_when_it_is_not_here()
    {
        const string Text = "{\"Gone\": \"/srv/gone\", \"There\": \"/srv/there\"}";

        var result = SettingsRewrite.Rewrite(Text, "linux", FromLinux, HereRoot, nameRoot: false, path => path == "/srv/there");

        Assert.Equal(Text, result.Text);
        Assert.Equal([new SettingNotHere("Gone", "/srv/gone")], result.NotHere);
    }

    [Fact]
    public void An_element_of_an_array_is_named_by_its_index()
    {
        var result = SettingsRewrite.Rewrite("{\"List\": [1, {\"x\": 2}, [3], \"/home/pat/a\", \"/srv/gone\"]}", "linux", FromLinux, HereRoot, nameRoot: false, Nothing);

        Assert.Equal("List:3", Assert.Single(result.Rewritten).Setting);
        Assert.Equal("List:4", Assert.Single(result.NotHere).Setting);
    }

    [Fact]
    public void The_root_the_file_names_is_the_root_placed_and_is_said_first()
    {
        var result = SettingsRewrite.Rewrite(
            "{\"Logs\": \"/home/pat/.jason/logs\", \"Harness\": {\"Root\": \"/mnt/elsewhere/Jason\"}}", "linux", FromLinux, HereRoot, nameRoot: false, Nothing);

        Assert.Equal("Harness:Root", result.Rewritten[0].Setting);
        Assert.Equal("/mnt/elsewhere/Jason", result.Rewritten[0].From);
        Assert.Equal(HereRoot, result.Rewritten[0].To);
        Assert.Equal(HereRoot, InstallationPlaces.SettingsRoot(result.Text));
        Assert.Empty(result.NotHere);
    }

    [Theory]
    [InlineData("{\"Harness\": {\"Root\": null}}", "null")]
    [InlineData("{\"Harness\": {\"Root\": 42}}", "42")]
    [InlineData("{\"Harness\": {\"Root\": true}, \"Dispatcher\": {}}", "true")]
    [InlineData("{\"Harness:Root\": false}", "false")]
    public void A_root_setting_whose_value_is_a_scalar_and_no_string_is_replaced_like_a_string(string text, string was)
    {
        var result = SettingsRewrite.Rewrite(text, "linux", FromLinux, HereRoot, nameRoot: true, Nothing);

        Assert.Equal(HereRoot, InstallationPlaces.SettingsRoot(result.Text));
        Assert.Equal(new SettingRewritten("Harness:Root", was, HereRoot), Assert.Single(result.Rewritten));
        Assert.Equal(text.Replace(was, JsonSerializer.Serialize(HereRoot), StringComparison.Ordinal), result.Text);
    }

    [Theory]
    [InlineData("{\"Harness\": {\"Root\": {\"Path\": \"/home/pat/Jason\"}}}")]
    [InlineData("{\"Harness\": {\"Root\": [\"/home/pat/Jason\"]}}")]
    [InlineData("{\"harness:root\": {}}")]
    public void A_root_setting_whose_value_is_an_object_or_an_array_is_refused_naming_the_setting(string text)
    {
        var refused = Assert.Throws<SettingsRefused>(() => SettingsRewrite.Rewrite(text, "linux", FromLinux, HereRoot, nameRoot: true, Nothing));

        Assert.Contains("Harness:Root", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_root_the_file_must_name_and_does_not_is_put_first_and_a_Harness_section_is_left_alone()
    {
        var result = SettingsRewrite.Rewrite("{\"Harness\": {\"Other\": 1}}", "linux", FromLinux, HereRoot, nameRoot: true, Nothing);

        Assert.StartsWith("{\n  \"Harness:Root\": " + JsonSerializer.Serialize(HereRoot) + ",", result.Text, StringComparison.Ordinal);
        Assert.EndsWith("\"Harness\": {\"Other\": 1}}", result.Text, StringComparison.Ordinal);
        Assert.Equal(HereRoot, InstallationPlaces.SettingsRoot(result.Text));
        Assert.Equal(new SettingRewritten("Harness:Root", string.Empty, HereRoot), Assert.Single(result.Rewritten));
    }

    [Fact]
    public void A_root_put_into_an_empty_object_reads_back()
    {
        var result = SettingsRewrite.Rewrite("{}", "linux", FromLinux, HereRoot, nameRoot: true, Nothing);

        Assert.Equal(HereRoot, InstallationPlaces.SettingsRoot(result.Text));
    }

    [Fact]
    public void A_root_the_file_need_not_name_is_not_put_in()
    {
        var result = SettingsRewrite.Rewrite("{\"Dispatcher\": {}}", "linux", FromLinux, HereRoot, nameRoot: false, Nothing);

        Assert.Equal("{\"Dispatcher\": {}}", result.Text);
        Assert.Empty(result.Rewritten);
    }

    [Fact]
    public void No_file_and_a_root_to_name_is_a_file_naming_it()
    {
        var result = SettingsRewrite.Rewrite(null, "linux", FromLinux, HereRoot, nameRoot: true, Nothing);

        Assert.Equal(HereRoot, InstallationPlaces.SettingsRoot(result.Text));
        Assert.Equal(new SettingRewritten("Harness:Root", string.Empty, HereRoot), Assert.Single(result.Rewritten));
    }

    [Fact]
    public void No_file_and_no_root_to_name_is_no_file()
    {
        var result = SettingsRewrite.Rewrite(null, "linux", FromLinux, HereRoot, nameRoot: false, Nothing);

        Assert.Equal(string.Empty, result.Text);
        Assert.Empty(result.Rewritten);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"a string\"")]
    [InlineData("{ not json")]
    public void A_file_that_is_not_a_JSON_object_is_refused(string text)
    {
        Assert.ThrowsAny<JsonException>(() => SettingsRewrite.Rewrite(text, "linux", FromLinux, HereRoot, nameRoot: false, Nothing));
    }

    [Theory]
    [InlineData(@"C:\Users", true, true)]
    [InlineData("C:/Users", true, true)]
    [InlineData(@"\\share\folder", true, true)]
    [InlineData("C:", true, false)]
    [InlineData("relative", true, false)]
    [InlineData("/home/pat", false, true)]
    [InlineData("home/pat", false, false)]
    [InlineData(@"C:\Users", false, false)]
    public void An_absolute_path_is_one_of_the_system_it_was_written_on(string value, bool windows, bool absolute)
    {
        Assert.Equal(absolute, SettingsRewrite.Absolute(value, windows));
    }
}
