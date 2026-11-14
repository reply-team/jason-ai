using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Jason.App.Tests.Skills;

/// <summary>
/// The places where two skills describe one thing: the notes the responder writes and the manager reads, and the
/// categories every other skill names. A skill that renamed a key or quoted a category the responder does not know would
/// send a reader to a note or a verdict that is never there.
/// </summary>
public sealed partial class ReplyLoopSeamsTests
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"--note '(\{.*\})'")]
    private static partial Regex Note();

    [GeneratedRegex(@"--category (\S+)")]
    private static partial Regex CategoryOption();

    private static string Flat(string path) => Whitespace().Replace(File.ReadAllText(path), " ");

    private static string ResponderFile(string name) =>
        Path.Combine(SkillPack.Root(), "roles", "responder", name);

    private static JsonObject Categories() => (JsonObject)JsonNode.Parse(File.ReadAllText(ResponderFile("categories.json")))!;

    private static IReadOnlyList<string> CategoryIds() =>
        Categories()["categories"]!.AsArray().Select(row => (string)row!["id"]!).ToList();

    /// <summary>
    /// The responder prints the role note it writes; the manager names, in the same order, the fields of each entry of its
    /// two lists, so the text the manager is taught from and the note the responder leaves are one shape.
    /// </summary>
    [Fact]
    public void The_manager_names_the_role_note_keys_the_responder_writes()
    {
        var written = SkillPack.PrintedCommands(ResponderFile("SKILL.md"))
            .Select(line => Note().Match(line))
            .Where(match => match.Success)
            .Select(match => JsonNode.Parse(match.Groups[1].Value)!.AsObject())
            .Where(note => note.ContainsKey("returns") || note.ContainsKey("bounces"))
            .ToList();
        Assert.True(written.Count > 0, "The responder prints no role note with returns or bounces.");

        var manager = Flat(SkillPack.Find("manager").File);
        foreach (var note in written)
        {
            foreach (var list in new[] { "returns", "bounces" })
            {
                var entry = note[list]!.AsArray()[0]!.AsObject();
                var fields = string.Join(", ", entry.Select(property => property.Key));
                Assert.Contains($"`{{{fields}}}` to the `{list}` list", manager, StringComparison.Ordinal);
            }
        }

        // The category an entry of `returns` carries is one the responder knows, and not_now is among those it files there.
        var ids = CategoryIds();
        foreach (var entry in written.SelectMany(note => note["returns"]!.AsArray()))
        {
            Assert.Contains((string)entry!["category"]!, ids);
        }

        Assert.Contains("`not_now` carrying its own category", manager, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every category a skill of these two packs names is a line of the responder's file, whether in prose or as a
    /// printed <c>--category</c>, and each file still names the ones listed here, so the list cannot go stale.
    /// </summary>
    [Theory]
    [InlineData("runtime", "roles/manager", "bounce interested not_now opt_out out_of_office")]
    [InlineData("runtime", "roles/analyst", "bounce")]
    [InlineData("runtime", "approvals-and-questions", "interested meeting_requested")]
    [InlineData("business", "approval-boundaries", "bounce opt_out referral")]
    public void The_categories_a_skill_names_are_the_responders(string pack, string skill, string named)
    {
        var file = Path.Combine(pack == "runtime" ? SkillPack.Root() : SkillPack.BusinessRoot(), skill.Replace('/', Path.DirectorySeparatorChar), SkillPack.SkillFile);
        var text = Flat(file);
        var ids = CategoryIds();

        foreach (var id in named.Split(' '))
        {
            Assert.Contains(id, ids);
            Assert.Matches($"(?<![a-z_]){id}(?![a-z_])", text);
        }

        foreach (var line in SkillPack.PrintedCommands(file))
        {
            var option = CategoryOption().Match(line);
            if (option.Success)
            {
                Assert.Contains(option.Groups[1].Value, ids);
            }
        }
    }

    /// <summary>
    /// Every verdict the manager prints, and the one a person's session prints to lift an opt-out, names a category and
    /// the reaction word its row in the file gives, the word before the colon, so an example that said <c>draft</c> for a
    /// category that waits on nothing would teach a wrong act.
    /// </summary>
    [Fact]
    public void The_manager_example_gives_the_reaction_the_categorys_row_gives()
    {
        var lines = new[] { "manager", "approvals-and-questions" }
            .SelectMany(skill => SkillPack.PrintedCommands(SkillPack.Find(skill).File))
            .Where(command => command.StartsWith("jason reply record ", StringComparison.Ordinal))
            .ToList();
        Assert.True(lines.Count >= 3, "the manager's two verdicts and the person's own");
        foreach (var line in lines)
        {
            var category = Regex.Match(line, @"--category (\S+)").Groups[1].Value;
            var reaction = Regex.Match(line, @"--reaction (\S+)").Groups[1].Value;

            var row = Categories()["categories"]!.AsArray().Single(candidate => (string)candidate!["id"]! == category)!;
            var word = ((string)row["reaction"]!).Split(':')[0];
            Assert.Equal(word, reaction);
        }
    }
}
