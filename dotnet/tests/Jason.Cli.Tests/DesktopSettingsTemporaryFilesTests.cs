using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Files;

namespace Jason.Cli.Tests;

/// <summary>
/// <c>desktop/settings-temporary-files.json</c> is the rule of <see cref="SettingsTemporaryFiles"/> as data — each writer's
/// shape of the temporary file it writes beside the settings file, by the digits of its id and how many — written from
/// it, so that the desktop's writers of the settings file are held to the same shapes the runtime's start sweeps and
/// <c>jason export</c> leaves out, and the two cannot part.
/// </summary>
public sealed class DesktopSettingsTemporaryFilesTests
{
    private const string UpdateVariable = "JASON_UPDATE_FIXTURES";

    [Fact]
    public void The_desktop_s_shapes_of_a_settings_write_s_temporary_file_are_the_rule_s()
    {
        var text = Compose();
        var file = Path.Combine(RepositoryRoot(), "desktop", "settings-temporary-files.json");
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            File.WriteAllText(file, text);
            return;
        }

        Assert.True(File.Exists(file), $"desktop/settings-temporary-files.json is not there. Rerun this test with {UpdateVariable}=1 and commit the file.");
        Assert.Equal(text, File.ReadAllText(file).Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    /// <summary>The file as the rule says it: two-space indents, one member a line, a newline at the end.</summary>
    internal static string Compose()
    {
        var document = new JsonObject
        {
            ["written_from"] = "SettingsTemporaryFiles, by DesktopSettingsTemporaryFilesTests in dotnet/tests/Jason.Cli.Tests; change the C# and rerun that test with JASON_UPDATE_FIXTURES=1, never this file by hand",
            ["name"] = "the name of the file written, a dot, the id, the suffix",
            ["suffix"] = SettingsTemporaryFiles.Suffix,
            ["shapes"] = new JsonArray([.. SettingsTemporaryFiles.Shapes.Select(shape => (JsonNode)new JsonObject
            {
                ["writer"] = shape.Writer,
                ["digits"] = shape.Digits,
                ["fewest_digits"] = shape.FewestDigits,
                ["most_digits"] = shape.MostDigits,
            })]),
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
