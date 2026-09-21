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

        Assert.False(
            string.IsNullOrWhiteSpace(root.GetProperty("name").GetString()),
            "The marketplace has no name, and a person registers one by that name.");
        Assert.False(
            string.IsNullOrWhiteSpace(root.GetProperty("owner").GetProperty("name").GetString()),
            "The marketplace names no owner, which is what a person sees before they trust it.");

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
            Assert.False(
                string.IsNullOrWhiteSpace(plugin.GetProperty("description").GetString()),
                $"'{plugin.GetProperty("name")}' offers no description, which is the whole of what a person "
                + "reads before installing it.");

            // Nothing in this repository bumps a version string, and the business pack is refreshed on a
            // cadence that has nothing to do with runtime releases. A pinned entry would hand a person who
            // added this marketplace from git one copy of the pack for ever: pinning means updates arrive
            // only when the string changes. Unpinned, the version comes from the source, which is the thing
            // that actually moves.
            Assert.False(
                plugin.TryGetProperty("version", out _),
                $"'{plugin.GetProperty("name")}' pins a version. A pinned plugin from a git source receives "
                + "no update until that string changes, and nothing in this repository changes it — so the "
                + "pack a person installs would be frozen at the day this was written. Either drop the pin, "
                + "or land the discipline that bumps it and a guard that fails when content moves and the "
                + "string does not.");
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

            var discovered = new HashSet<string>(StringComparer.Ordinal);
            var manifest = Path.Combine(source, ".claude-plugin", "plugin.json");
            Assert.True(
                File.Exists(manifest),
                $"'{name}' is offered from '{source}' and declares nothing about where its skills are. A host "
                + "scans <source>/skills/<name>/SKILL.md and does not recurse, and this pack does not keep "
                + "its skills there — so without a manifest naming the path it installs and delivers nothing.");

            using var declared = JsonDocument.Parse(File.ReadAllText(manifest));
            Assert.False(
                string.IsNullOrWhiteSpace(declared.RootElement.GetProperty("name").GetString()),
                $"The manifest in '{source}' has no name, which is the one field a plugin manifest must carry.");

            var paths = declared.RootElement.GetProperty("skills").EnumerateArray()
                .Select(path => path.GetString() ?? string.Empty)
                .ToArray();
            Assert.True(paths.Length > 0, $"'{name}' declares an empty list of skill paths.");

            foreach (var path in paths)
            {
                Assert.True(
                    path.StartsWith("./", StringComparison.Ordinal),
                    $"'{name}' declares the skill path '{path}'. Paths are relative to the pack and start "
                    + "with './'.");

                var directory = Path.GetFullPath(Path.Combine(source, path));
                foreach (var child in Directory.EnumerateDirectories(directory))
                {
                    if (File.Exists(Path.Combine(child, SkillPack.SkillFile)))
                    {
                        discovered.Add(Path.GetFileName(child));
                    }
                }
            }

            // The count, not "more than none". A path that had lost eight of nine skills would satisfy a
            // test that only asked whether anything was there, and losing skills silently is the failure
            // this whole fact exists to catch.
            var expected = Expected[name!];
            Assert.True(
                discovered.SetEquals(expected),
                $"'{name}' offers [{string.Join(", ", discovered.Order())}] and this repository ships "
                + $"[{string.Join(", ", expected.Order())}]. A host looks for <name>/SKILL.md one level down "
                + "and does not recurse, so a pack whose skills moved deeper is offered and delivers nothing.");
        }
    }

    /// <summary>
    /// What each pack offers a harness, named here rather than counted from the tree — a guard that derived
    /// the answer from the same directories it is checking would agree with any mistake made in them.
    /// </summary>
    /// <remarks>
    /// The nine role skills are deliberately absent from the runtime pack's list. They live one level deeper,
    /// under <c>roles/</c>, where discovery does not reach; a launched role is taught by the runtime out of
    /// its own data directory, not by something a person installed into their editor. That exclusion is a
    /// decision, so it is held here rather than left to the shape of a directory.
    /// </remarks>
    private static readonly Dictionary<string, HashSet<string>> Expected = new(StringComparer.Ordinal)
    {
        ["jason-runtime-skills"] = new(StringComparer.Ordinal)
        {
            "approvals-and-questions", "managed-campaign-work", "operating-the-installation",
            "reporting-outside-effects", "troubleshooting-jason",
        },
        ["jason-business-skills"] = new(StringComparer.Ordinal)
        {
            "approval-boundaries", "audience-building", "campaign-launch", "campaign-planning",
            "inbox-triage", "linkedin-guardrails", "performance-analysis", "sdr-operations",
            "sending-guardrails",
        },
    };

    /// <summary>
    /// A manifest is a public document like any other: it points at this repository rather than the one the
    /// business knowledge came from, and carries no link an outside reader cannot open.
    /// </summary>
    [Fact]
    public void The_manifest_points_at_this_repository()
    {
        var text = File.ReadAllText(Manifest());
        Assert.True(
            !text.Contains("reply-skills", StringComparison.OrdinalIgnoreCase),
            "The marketplace still points at the repository the business knowledge came from.");
        Assert.True(
            !text.Contains("atlassian", StringComparison.OrdinalIgnoreCase),
            "The marketplace carries a link into a system an outside reader cannot open.");
    }
}
