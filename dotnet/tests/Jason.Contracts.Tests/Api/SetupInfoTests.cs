using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;

namespace Jason.Contracts.Tests.Api;

/// <summary>
/// Where this Jason's parts are: the role skills, the plugins, the custom UI and the settings file, and whether that file
/// holds plugin secrets too. One record carries it in <c>system.info</c> and in the body of <c>jason status</c>, and it is
/// composed from <see cref="JasonPaths"/> alone, so the layout is spelled in one place.
/// </summary>
public class SetupInfoTests
{
    /// <summary>
    /// The section rides last on <c>system.info</c>, after every section that came before it, with its five members in
    /// this order; a runtime older than the section answers <c>null</c>.
    /// </summary>
    [Fact]
    public void System_info_says_where_this_jasons_parts_are_last_and_null_from_an_older_runtime()
    {
        var info = SystemInfo() with
        {
            Setup = new SetupInfo("/data/skills/roles", "/data/plugins", "/data/ui/custom", "/data/config/settings.json", true),
        };

        Assert.EndsWith(
            "\"released_plugins\":null,\"setup\":{\"role_skills_directory\":\"/data/skills/roles\",\"plugins_directory\":\"/data/plugins\","
                + "\"custom_ui_directory\":\"/data/ui/custom\",\"settings_file\":\"/data/config/settings.json\",\"settings_file_holds_secrets\":true}}",
            JsonSerializer.Serialize(info, JasonJson.Options),
            StringComparison.Ordinal);
        Assert.EndsWith("\"released_plugins\":null,\"setup\":null}", JsonSerializer.Serialize(info with { Setup = null }, JasonJson.Options), StringComparison.Ordinal);
    }

    /// <summary>
    /// Every member is the <see cref="JasonPaths"/> property of the same name, over any root: nothing here composes a path
    /// of its own, and the settings file holds plugin secrets in this layout.
    /// </summary>
    [Theory]
    [InlineData("jason-setup-a")]
    [InlineData("elsewhere/jason-setup-b")]
    public void Setup_is_composed_from_jason_paths_alone(string folder)
    {
        var paths = new JasonPaths(Path.Combine(Path.GetTempPath(), folder));

        var setup = SetupInfo.From(paths);

        Assert.Equal(paths.RoleSkillsDirectory, setup.RoleSkillsDirectory);
        Assert.Equal(paths.PluginsDirectory, setup.PluginsDirectory);
        Assert.Equal(paths.UiCustomDirectory, setup.CustomUiDirectory);
        Assert.Equal(paths.UserSettingsFile, setup.SettingsFile);
        Assert.Equal(paths.UserSettingsFileHoldsSecrets, setup.SettingsFileHoldsSecrets);
        Assert.True(setup.SettingsFileHoldsSecrets, "Today's layout keeps Plugins:Config in the one settings file.");
    }

    /// <summary>
    /// And the file that declares the record spells no part of the layout: no path is combined and no folder of the data
    /// directory is named there. A path that moves is <see cref="JasonPaths"/>'s to answer.
    /// </summary>
    [Fact]
    public void The_setup_record_spells_no_line_of_the_layout()
    {
        var text = File.ReadAllText(Path.Combine(SourceRoot(), "dotnet", "src", "Jason.Contracts", "Api", "SetupInfo.cs"));

        foreach (var spelled in new[] { "Path.", "Combine", "\"skills\"", "\"plugins\"", "\"ui\"", "\"custom\"", "\"config\"", "settings.json" })
        {
            Assert.False(text.Contains(spelled, StringComparison.Ordinal), $"SetupInfo.cs spells '{spelled}': the layout is JasonPaths' alone.");
        }
    }

    private static string SourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }

    private static SystemInfoResponse SystemInfo() =>
        new(
            "0.1.0", "v1", "rt_01J", 1234, DateTimeOffset.UnixEpoch, "/data",
            new DatabaseInfo([], [], null),
            new DispatcherInfo(DispatcherState.Running, 10, 4, 0, null, 0, 0),
            new PluginsInfo(1, "snp_01J", DateTimeOffset.UnixEpoch, true),
            new RoutesInfo("rts_01J", DateTimeOffset.UnixEpoch, null, 0, 0));
}
