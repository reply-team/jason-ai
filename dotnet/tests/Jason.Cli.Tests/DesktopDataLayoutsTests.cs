using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Discovery;

namespace Jason.Cli.Tests;

/// <summary>
/// <c>desktop/data-layouts.json</c> is the data directory's table as <see cref="JasonPaths"/> composes it — every path of
/// every layout it has a table for, relative to the data directory, the names a purge removes, the stamp's name and the
/// layout this build runs — written from it, so that the desktop's own paths are held to the same table and the two
/// cannot part.
/// </summary>
public sealed class DesktopDataLayoutsTests
{
    private const string UpdateVariable = "JASON_UPDATE_FIXTURES";

    [Fact]
    public void The_desktop_s_table_of_layouts_is_the_one_jason_paths_composes()
    {
        var text = Compose();
        var file = Path.Combine(RepositoryRoot(), "desktop", "data-layouts.json");
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            File.WriteAllText(file, text);
            return;
        }

        Assert.True(File.Exists(file), $"desktop/data-layouts.json is not there. Rerun this test with {UpdateVariable}=1 and commit the file.");
        Assert.Equal(text, File.ReadAllText(file).Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    /// <summary>The file as JasonPaths says it: two-space indents, one member a line, a newline at the end.</summary>
    internal static string Compose()
    {
        var root = Path.Combine(Path.GetTempPath(), "jason-data-layouts");
        var layouts = new JsonObject();
        for (var layout = 1; layout <= JasonPaths.NewestTable; layout++)
        {
            var paths = new JasonPaths(root, layout);
            var members = new JsonObject();
            foreach (var property in typeof(JasonPaths).GetProperties()
                .Where(property => property.PropertyType == typeof(string) && property.Name != nameof(JasonPaths.Root))
                .OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(paths.Root, (string)property.GetValue(paths)!).Replace(Path.DirectorySeparatorChar, '/');
                members[JsonNamingPolicy.SnakeCaseLower.ConvertName(property.Name)] = relative;
            }

            members["user_settings_file_holds_secrets"] = paths.UserSettingsFileHoldsSecrets;
            layouts[layout.ToString(System.Globalization.CultureInfo.InvariantCulture)] = members;
        }

        var document = new JsonObject
        {
            ["written_from"] = "JasonPaths, by DesktopDataLayoutsTests in dotnet/tests/Jason.Cli.Tests; change the C# and rerun that test with JASON_UPDATE_FIXTURES=1, never this file by hand",
            ["stamp"] = LayoutStamp.FileName,
            ["this_build_runs"] = LayoutStamp.ThisBuildRuns,
            ["own_entries"] = new JsonArray([.. JasonPaths.OwnEntries.Select(entry => (JsonNode)entry)]),
            ["layouts"] = layouts,
        };

        return document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    private static string RepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        return root.FullName;
    }
}
