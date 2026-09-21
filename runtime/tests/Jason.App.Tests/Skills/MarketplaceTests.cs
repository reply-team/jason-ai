using System.Text.Json;

namespace Jason.App.Tests.Skills;

/// <summary>
/// The manifest that offers this repository's two skill packs to an agent harness. Every source is a relative
/// path inside this clone, so somebody who has cloned the repository can install from it with no network and
/// no second repository.
/// </summary>
/// <remarks>
/// <para>
/// This is the <em>skills</em> marketplace, and it is a different thing from the plugin marketplace under
/// <c>plugins/</c>: that one holds JavaScript packages that execute provider operations in the plugin host.
/// Two things in one repository are called a marketplace, so each is named in full wherever both appear.
/// </para>
/// <para>
/// The second fact below is the one worth having. A manifest that parses is not a manifest that installs: a
/// host discovers skills in <c>&lt;source&gt;/skills/&lt;name&gt;/SKILL.md</c>, or in the paths a plugin
/// manifest names, and <em>it does not recurse</em>. This repository keeps its packs at
/// <c>skills/runtime</c> and <c>skills/business</c>, so a bare source would be offered, install cleanly and
/// deliver nothing at all. Each source therefore carries a manifest that names where its skills are, and this
/// asserts the paths resolve to skills rather than trusting that the directory exists.
/// </para>
/// </remarks>
public class MarketplaceTests
{
    private static string Root() => SkillPack.RepositoryRoot();

    private static string Manifest() => Path.Combine(Root(), ".claude-plugin", "marketplace.json");

    [Fact]
    public void The_manifest_offers_both_packs_by_a_relative_path_inside_this_repository()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Manifest()));
        var root = document.RootElement;

        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("name").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("owner").GetProperty("name").GetString()));

        var plugins = root.GetProperty("plugins").EnumerateArray().ToArray();
        var names = plugins.Select(plugin => plugin.GetProperty("name").GetString()).Order().ToArray();
        Assert.True(
            names is ["jason-business-skills", "jason-runtime-skills"],
            $"The manifest offers [{string.Join(", ", names)}]. It offers the two packs this repository ships, "
            + "and a pack listed but absent is a broken install for whoever tries it.");

        foreach (var plugin in plugins)
        {
            var source = plugin.GetProperty("source").GetString() ?? string.Empty;
            Assert.True(
                source.StartsWith("./", StringComparison.Ordinal),
                $"'{plugin.GetProperty("name")}' is sourced from '{source}'. Every source here is a relative "
                + "path inside this clone, so a person who has cloned the repository can install with no "
                + "network.");
            Assert.True(
                Directory.Exists(Path.Combine(Root(), source[2..])),
                $"The manifest sources '{source}' and there is no such directory in this repository.");
            Assert.False(string.IsNullOrWhiteSpace(plugin.GetProperty("description").GetString()));
        }
    }

    [Fact]
    public void Every_offered_pack_says_where_its_skills_are_and_the_paths_hold_skills()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Manifest()));

        foreach (var plugin in document.RootElement.GetProperty("plugins").EnumerateArray())
        {
            var name = plugin.GetProperty("name").GetString();
            var source = Path.Combine(Root(), plugin.GetProperty("source").GetString()![2..]);

            var manifest = Path.Combine(source, ".claude-plugin", "plugin.json");
            Assert.True(
                File.Exists(manifest),
                $"'{name}' is offered from '{source}' and declares nothing about where its skills are. A host "
                + "scans <source>/skills/<name>/SKILL.md and does not recurse, and this pack does not keep "
                + "its skills there — so without a manifest naming the path it installs and delivers nothing.");

            using var declared = JsonDocument.Parse(File.ReadAllText(manifest));
            Assert.False(string.IsNullOrWhiteSpace(declared.RootElement.GetProperty("name").GetString()));

            var paths = declared.RootElement.GetProperty("skills").EnumerateArray()
                .Select(path => path.GetString() ?? string.Empty)
                .ToArray();
            Assert.NotEmpty(paths);

            foreach (var path in paths)
            {
                Assert.True(
                    path.StartsWith("./", StringComparison.Ordinal),
                    $"'{name}' declares the skill path '{path}'. Paths are relative to the pack and start "
                    + "with './'.");

                var directory = Path.GetFullPath(Path.Combine(source, path));
                var found = Directory
                    .EnumerateDirectories(directory)
                    .Count(child => File.Exists(Path.Combine(child, SkillPack.SkillFile)));

                Assert.True(
                    found > 0,
                    $"'{name}' declares the skill path '{path}' and nothing under it is a skill: a host looks "
                    + "for <name>/SKILL.md one level down and does not recurse.");
            }
        }
    }

    /// <summary>
    /// A manifest is a public document like any other: it points at this repository rather than the one the
    /// business knowledge came from, and carries no link an outside reader cannot open.
    /// </summary>
    [Fact]
    public void The_manifest_points_at_this_repository()
    {
        var text = File.ReadAllText(Manifest());
        Assert.DoesNotContain("reply-skills", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("atlassian", text, StringComparison.OrdinalIgnoreCase);
    }
}
