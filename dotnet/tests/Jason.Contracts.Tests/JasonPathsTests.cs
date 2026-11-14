using Jason.Contracts.Api;
using Jason.Contracts.Discovery;

namespace Jason.Contracts.Tests;

public class JasonPathsTests
{
    private static readonly string Scratch = Path.Combine(Path.GetTempPath(), "jason-paths-test");

    [Fact]
    public void Layout_is_derived_from_the_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "jason-paths-test");
        var paths = new JasonPaths(root, 1);
        Assert.Equal(Path.GetFullPath(root), paths.Root);
        Assert.Equal(Path.Combine(paths.Root, "state", "jason.db"), paths.DatabaseFile);
        Assert.Equal(Path.Combine(paths.Root, "state", "backups"), paths.BackupsDirectory);
        Assert.Equal(Path.Combine(paths.Root, "config", "settings.json"), paths.UserSettingsFile);
        Assert.Equal(Path.Combine(paths.Root, "run", "runtime.json"), paths.DescriptorFile);
        Assert.Equal(Path.Combine(paths.Root, "run", "runtime.lock"), paths.LockFile);
        Assert.Equal(Path.Combine(paths.Root, "plugins"), paths.PluginsDirectory);
        Assert.Equal(Path.Combine(paths.Root, "logs"), paths.LogsDirectory);
        Assert.Equal(6, paths.StartDirectories.Count());
    }

    [Fact]
    public void The_work_directory_holds_one_directory_per_attempt()
    {
        var paths = new JasonPaths(Scratch, 1);

        Assert.Equal(Path.Combine(paths.Root, "work"), paths.WorkDirectory);
        Assert.Equal(Path.Combine(paths.Root, "work", "wi_a", "att_b"), paths.AttemptWorkDirectory("wi_a", "att_b"));
        Assert.Contains(paths.WorkDirectory, paths.StartDirectories);
    }

    [Fact]
    public void A_plugin_package_and_an_invocation_have_their_own_directories()
    {
        var paths = new JasonPaths(Scratch, 1);

        Assert.Equal(Path.Combine(paths.Root, "plugins", "fake-provider"), paths.PluginPackageDirectory("fake-provider"));
        Assert.Equal(Path.Combine(paths.Root, "work", "plugins"), paths.PluginWorkDirectory);
        Assert.Equal(Path.Combine(paths.Root, "work", "plugins", "pin_a"), paths.PluginInvocationDirectory("pin_a"));
    }

    /// <summary>The root alone, resolved without reading anything: a test never reads the stamp of a real home.</summary>
    [Fact]
    public void Default_root_is_dot_jason_under_the_user_profile()
    {
        var previous = Environment.GetEnvironmentVariable(JasonPaths.DataDirectoryVariable);
        try
        {
            Environment.SetEnvironmentVariable(JasonPaths.DataDirectoryVariable, null);
            var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".jason");
            Assert.Equal(expected, JasonPaths.RootFromEnvironment());

            Environment.SetEnvironmentVariable(JasonPaths.DataDirectoryVariable, Path.Combine(Path.GetTempPath(), "jason-override"));
            Assert.Equal(Path.Combine(Path.GetTempPath(), "jason-override"), JasonPaths.RootFromEnvironment());
        }
        finally
        {
            Environment.SetEnvironmentVariable(JasonPaths.DataDirectoryVariable, previous);
        }
    }

    // ---- by layout -------------------------------------------------------------------------------------------------

    /// <summary>Every path of the data directory in layout 1, as every Jason before the layout stamp composed it.</summary>
    private static readonly (string Member, string Relative)[] LayoutOne =
    [
        (nameof(JasonPaths.LayoutFile), "layout.json"),
        (nameof(JasonPaths.StateDirectory), "state"),
        (nameof(JasonPaths.BackupsDirectory), "state/backups"),
        (nameof(JasonPaths.DatabaseFile), "state/jason.db"),
        (nameof(JasonPaths.ConfigDirectory), "config"),
        (nameof(JasonPaths.UserSettingsFile), "config/settings.json"),
        (nameof(JasonPaths.SecretsFile), "config/settings.json"),
        (nameof(JasonPaths.PluginsDirectory), "plugins"),
        (nameof(JasonPaths.UiDirectory), "ui"),
        (nameof(JasonPaths.UiDefaultDirectory), "ui/default"),
        (nameof(JasonPaths.UiCustomDirectory), "ui/custom"),
        (nameof(JasonPaths.RunDirectory), "run"),
        (nameof(JasonPaths.DescriptorFile), "run/runtime.json"),
        (nameof(JasonPaths.LockFile), "run/runtime.lock"),
        (nameof(JasonPaths.LogsDirectory), "logs"),
        (nameof(JasonPaths.WorkDirectory), "work"),
        (nameof(JasonPaths.RoleSkillsDirectory), "skills/roles"),
        (nameof(JasonPaths.SkillsStagingDirectory), "skills/.staging"),
        (nameof(JasonPaths.SkillsStagedSourcesDirectory), "skills/staged"),
        (nameof(JasonPaths.PluginWorkDirectory), "work/plugins"),
    ];

    /// <summary>What layout 2 places differently: the parts a person sets up, in <c>setup/</c>, and the secrets beside the rest of <c>config/</c>.</summary>
    private static readonly Dictionary<string, string> LayoutTwoMoves = new(StringComparer.Ordinal)
    {
        [nameof(JasonPaths.UserSettingsFile)] = "setup/settings.json",
        [nameof(JasonPaths.SecretsFile)] = "config/secrets.json",
        [nameof(JasonPaths.PluginsDirectory)] = "setup/plugins",
        [nameof(JasonPaths.UiCustomDirectory)] = "setup/ui",
        [nameof(JasonPaths.RoleSkillsDirectory)] = "setup/roles",
        [nameof(JasonPaths.SkillsStagingDirectory)] = "setup/.staging",
    };

    private static readonly string[] StartOne = ["state", "config", "plugins", "run", "logs", "work"];

    /// <summary>
    /// A data directory with no stamp answers every path as every Jason before the stamp did, read from the directory as
    /// <c>jason</c> reads it; and a member added to the paths is added to the list above, or this is red.
    /// </summary>
    [Fact]
    public void A_missing_stamp_answers_every_path_as_today()
    {
        var root = Path.Combine(Path.GetTempPath(), "jason-paths-no-stamp-" + Guid.NewGuid().ToString("N"));
        var read = new JasonPaths(root);

        Assert.Equal(1, read.Layout);
        Assert.Equal(LayoutStampState.Missing, read.Stamp!.State);
        Assert.True(read.UserSettingsFileHoldsSecrets);
        Assert.Equal(Members(read), LayoutOne.Select(pair => (pair.Member, Rooted(read, pair.Relative))).OrderBy(pair => pair.Member, StringComparer.Ordinal));
        Assert.Equal(StartOne.Select(relative => Rooted(read, relative)), read.StartDirectories);
        Assert.Equal(Members(new JasonPaths(root, 1)), Members(read));
    }

    /// <summary>Layout 2 places the six moving parts in <c>setup/</c> and the secrets file in <c>config/</c>; nothing else moves.</summary>
    [Fact]
    public void Layout_two_places_the_moving_parts_under_setup()
    {
        var paths = new JasonPaths(Scratch, 2);

        Assert.Equal(2, paths.Layout);
        Assert.Null(paths.Stamp);
        Assert.False(paths.UserSettingsFileHoldsSecrets);
        var expected = LayoutOne.Select(pair => (pair.Member, Rooted(paths, LayoutTwoMoves.GetValueOrDefault(pair.Member, pair.Relative))));
        Assert.Equal(expected.OrderBy(pair => pair.Member, StringComparer.Ordinal), Members(paths));
        Assert.Equal(Path.Combine(paths.Root, "setup", "plugins", "a.plugin"), paths.PluginPackageDirectory("a.plugin"));
        Assert.Contains(Path.Combine(paths.Root, "setup", "plugins"), paths.StartDirectories);
    }

    [Fact]
    public void In_layout_one_the_secrets_file_is_the_settings_file()
    {
        var paths = new JasonPaths(Scratch, 1);

        Assert.Equal(paths.UserSettingsFile, paths.SecretsFile);
        Assert.True(paths.UserSettingsFileHoldsSecrets);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3)]
    public void A_given_layout_this_file_has_no_table_for_is_refused(int layout)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new JasonPaths(Scratch, layout));
    }

    /// <summary>
    /// Every path of either layout, and every one the setup read reports, is inside the data directory: a part that left it
    /// would be one the doctor's rewrite of the data directory does not cover.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Every_member_of_either_layout_and_every_setup_path_is_under_the_data_directory(int layout)
    {
        var paths = new JasonPaths(Scratch, layout);
        var setup = SetupInfo.From(paths);
        var under = paths.Root + Path.DirectorySeparatorChar;

        foreach (var path in Members(paths).Select(pair => pair.Path)
            .Concat(paths.StartDirectories)
            .Concat([setup.RoleSkillsDirectory, setup.PluginsDirectory, setup.CustomUiDirectory, setup.SettingsFile]))
        {
            Assert.StartsWith(under, path, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Production code outside the contracts makes its paths from a root alone, so that they read the stamp: a layout given
    /// skips it, and with it every refusal, so it is a test's or the code that moves the parts, which lives beside
    /// <see cref="JasonPaths"/>. Read from the sources; a target-typed <c>new(...)</c> is beyond what this reads.
    /// </summary>
    [Fact]
    public void Production_code_outside_the_contracts_never_gives_the_paths_a_layout()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        var sources = Path.Combine(root.FullName, "dotnet", "src");
        var found = new List<string>();
        var read = 0;
        foreach (var file in Directory.EnumerateFiles(sources, "*.cs", SearchOption.AllDirectories)
            .Where(file => !Path.GetRelativePath(sources, file).StartsWith("Jason.Contracts" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
        {
            read++;
            var text = File.ReadAllText(file);
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(text, @"new\s+(?:[\w.]+\.)?JasonPaths\s*\("))
            {
                var depth = 1;
                for (var at = match.Index + match.Length; at < text.Length && depth > 0; at++)
                {
                    depth += text[at] switch { '(' => 1, ')' => -1, _ => 0 };
                    if (depth == 1 && text[at] == ',')
                    {
                        found.Add(Path.GetRelativePath(sources, file));
                        break;
                    }
                }
            }
        }

        Assert.True(read > 100, "the sources were not found, so this asserts nothing.");
        Assert.Empty(found);
    }

    /// <summary>Every string member but the root, by name, as the paths answer it.</summary>
    internal static IEnumerable<(string Member, string Path)> Members(JasonPaths paths) =>
        typeof(JasonPaths).GetProperties()
            .Where(property => property.PropertyType == typeof(string) && property.Name != nameof(JasonPaths.Root))
            .Select(property => (property.Name, (string)property.GetValue(paths)!))
            .OrderBy(pair => pair.Name, StringComparer.Ordinal);

    private static string Rooted(JasonPaths paths, string relative) => Path.Combine([paths.Root, .. relative.Split('/')]);
}
