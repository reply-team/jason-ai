using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Contracts.Execution;

namespace Jason.App.Tests.Skills;

/// <summary>
/// A role's entries as files: one <c>entries/&lt;name&gt;.md</c> beside the head for each name the runtime may put on a
/// work item, and nothing else there. The runtime reads the file by the name it wrote, so a file it never names is text
/// nobody is handed, and a name with no file is a launch that runs on the head alone. Each file is a text read beside the
/// head, never a skill a host could load by itself.
/// </summary>
public sealed partial class ManagerEntryFilesTests
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"--note '(\{.*\})'")]
    private static partial Regex Note();

    private static PackEntry Entry(string name) => Assert.Single(SkillPack.Entries("manager"), entry => entry.Name == name);

    /// <summary>The entry's body, flattened, so a pinned sentence may span a line break.</summary>
    internal static string Flat(string name) => Whitespace().Replace(SkillPack.Body(Entry(name).File), " ");

    [Fact]
    public void The_entry_files_and_the_entry_names_match_one_to_one()
    {
        foreach (var role in Directory.EnumerateDirectories(Path.Combine(SkillPack.Root(), "roles")).Select(Path.GetFileName))
        {
            var files = SkillPack.Entries(role!).Select(entry => entry.Name).Order(StringComparer.Ordinal).ToList();
            var names = RoleEntries.Of(role!).Order(StringComparer.Ordinal).ToList();
            Assert.True(
                files.SequenceEqual(names, StringComparer.Ordinal),
                $"The role '{role}' has entry files [{string.Join(", ", files)}] and the runtime names its entries "
                + $"[{string.Join(", ", names)}]: each name the runtime may write has its file, and no file is one it never names.");

            var folder = Path.Combine(SkillPack.Root(), "roles", role!, RoleEntries.Folder);
            if (Directory.Exists(folder))
            {
                Assert.DoesNotContain(Directory.EnumerateFileSystemEntries(folder), path => !path.EndsWith(".md", StringComparison.Ordinal));
            }
        }

        Assert.Equal(["deep_review", "inbound_reply", "quick_look", "resume", "start"], SkillPack.Entries("manager").Select(entry => entry.Name));
    }

    [Fact]
    public void Each_entry_s_front_matter_names_its_entry_and_its_status_and_nothing_else()
    {
        foreach (var entry in SkillPack.AllEntries())
        {
            var front = SkillFrontMatter.Read(entry.File);
            Assert.True(front.Problems.Count == 0, $"'{entry.File}': {string.Join("; ", front.Problems)}");
            Assert.Equal(["entry", "metadata"], front.Keys);
            Assert.Equal(entry.Name, front.Top["entry"]);
            Assert.Equal(["status"], front.Metadata.Keys);
            Assert.Contains(front.Metadata["status"], new[] { "draft", "verified" });
        }
    }

    /// <summary>
    /// An entry is read beside its head and never in its place: the launch contract is the head's, once, and a file with
    /// a <c>name</c> is a file a host could take for a skill of its own and load with no head at all.
    /// </summary>
    [Fact]
    public void No_entry_carries_the_launch_contract_or_a_name()
    {
        foreach (var entry in SkillPack.AllEntries())
        {
            var text = File.ReadAllText(entry.File);
            Assert.Null(LaunchContract.Extract(entry.File));
            Assert.DoesNotContain(LaunchContract.Begin, text, StringComparison.Ordinal);
            Assert.DoesNotContain(LaunchContract.End, text, StringComparison.Ordinal);
            Assert.DoesNotContain("## How this runtime launched you", text, StringComparison.Ordinal);

            var front = SkillFrontMatter.Read(entry.File);
            Assert.False(front.Top.ContainsKey("name"), $"'{entry.File}' names itself, so a host could load it as a skill of its own.");
            Assert.False(front.Top.ContainsKey("description"), $"'{entry.File}' describes itself, so a host could choose it as a skill of its own.");
        }
    }

    /// <summary>
    /// One manager, from one review to the next: every entry opens by reading its own note and says what it leaves there,
    /// so whichever review comes next starts where this one stopped. The section is the first of the entry, and it prints
    /// the read and the write.
    /// </summary>
    [Fact]
    public void Every_entry_opens_by_reading_the_note_and_says_what_it_leaves_there()
    {
        foreach (var entry in SkillPack.Entries("manager"))
        {
            var body = SkillPack.Body(entry.File);
            var first = body.Split('\n').FirstOrDefault(line => line.StartsWith("## ", StringComparison.Ordinal));
            Assert.True(first == "## Your note first", $"'{entry.Row}' opens with '{first}', and its first section is '## Your note first'.");

            var section = Section(body, "## Your note first");
            var flat = Whitespace().Replace(section, " ");
            Assert.Contains("**Read first.**", flat, StringComparison.Ordinal);
            Assert.Contains("**Leave there.**", flat, StringComparison.Ordinal);

            var printed = SkillPack.PrintedCommands(section.Split('\n'));
            Assert.Contains("jason rolenote get --role manager --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD", printed);
            var set = Assert.Single(printed, line => line.StartsWith("jason rolenote set --role manager --campaign ", StringComparison.Ordinal));
            var note = JsonNode.Parse(Note().Match(set).Groups[1].Value)!.AsObject();
            Assert.True(note.ContainsKey("last_review"), $"'{entry.Row}' leaves a note with no moment of the review: {set}");
        }
    }

    /// <summary>What each kind of review leaves, by the keys its printed note carries.</summary>
    [Theory]
    [InlineData("quick_look", "last_quick_look off")]
    [InlineData("deep_review", "last_deep_review last_quick_look conclusion")]
    [InlineData("start", "batch")]
    [InlineData("resume", "resumed_at")]
    [InlineData("inbound_reply", "replies")]
    public void Each_entry_leaves_its_own_marks_in_the_note(string name, string keys)
    {
        var set = SkillPack.PrintedCommands(Section(SkillPack.Body(Entry(name).File), "## Your note first").Split('\n'))
            .Single(line => line.StartsWith("jason rolenote set ", StringComparison.Ordinal));
        var note = JsonNode.Parse(Note().Match(set).Groups[1].Value)!.AsObject();

        foreach (var key in keys.Split(' '))
        {
            Assert.True(note.ContainsKey(key), $"'{name}' leaves no '{key}' in its note: {set}");
        }
    }

    /// <summary>
    /// The deep review leaves a short conclusion — what it saw, what it decided, what it watches, and when it looks next —
    /// and the moments of the last quick look and the last deep review, so the next review of any kind knows where it is.
    /// </summary>
    [Fact]
    public void The_deep_review_leaves_its_conclusion_and_the_two_moments()
    {
        var set = SkillPack.PrintedCommands(Section(SkillPack.Body(Entry("deep_review").File), "## Your note first").Split('\n'))
            .Single(line => line.StartsWith("jason rolenote set ", StringComparison.Ordinal));
        var note = JsonNode.Parse(Note().Match(set).Groups[1].Value)!.AsObject();
        var conclusion = note["conclusion"]!.AsObject();
        Assert.Equal(["saw", "decided", "watching", "next"], conclusion.Select(property => property.Key));
        Assert.NotNull(note["last_deep_review"]);
        Assert.NotNull(note["last_quick_look"]);

        var flat = Flat("deep_review");
        Assert.Contains("leave a short conclusion: what you saw, what you decided, what you watch, and when you look next", flat, StringComparison.Ordinal);
        Assert.Contains("the moments of the last quick look and of this deep review", flat, StringComparison.Ordinal);
    }

    /// <summary>
    /// A review that finds a recent one in its note covers only what changed since, and says so: a deep review at hour 24
    /// that knows one ran four hours ago is short.
    /// </summary>
    [Theory]
    [InlineData("quick_look")]
    [InlineData("deep_review")]
    [InlineData("resume")]
    [InlineData("inbound_reply")]
    public void A_review_that_finds_a_recent_one_covers_only_what_changed_since(string name)
    {
        var flat = Flat(name);
        Assert.Contains("covers only what changed since", flat, StringComparison.Ordinal);
        Assert.Contains("and says so", flat, StringComparison.Ordinal);
    }

    /// <summary>The entries name only what this build has: the verbs of a line, the counts and a row's touches.</summary>
    [Fact]
    public void The_entries_name_only_the_verbs_of_a_line_this_build_has()
    {
        var all = string.Join(' ', SkillPack.Entries("manager").Select(entry => Flat(entry.Name)));
        foreach (var retired in new[] { "line_ended", "end-line", "`first_batch` a reserved", "`line` key", "[NEEDS CODE", "[PROPOSAL", "[DEPENDS", "[SHOULD BE CODE" })
        {
            Assert.DoesNotContain(retired, all, StringComparison.Ordinal);
        }

        var printed = SkillPack.Entries("manager").SelectMany(entry => SkillPack.PrintedCommands(entry.File)).ToList();
        Assert.Contains(printed, line => line.StartsWith("jason campaign finish-contacts ", StringComparison.Ordinal));
        Assert.Contains(printed, line => line.StartsWith("jason campaign continue-contacts ", StringComparison.Ordinal));
        Assert.DoesNotContain(printed, line => line.StartsWith("jason launches lift", StringComparison.Ordinal));
        Assert.DoesNotContain(printed, line => line.StartsWith("jason campaign review", StringComparison.Ordinal));
        Assert.DoesNotContain(printed, line => line.StartsWith("jason campaign reopen-contacts", StringComparison.Ordinal) && !line.Contains("--decision dec_", StringComparison.Ordinal));
        Assert.DoesNotContain(printed, line => line.Contains("--actor", StringComparison.Ordinal));
    }

    /// <summary>The text from a heading to the next heading of the same level, or to the end.</summary>
    internal static string Section(string body, string heading)
    {
        var lines = body.Split('\n');
        var start = Array.IndexOf(lines, heading);
        Assert.True(start >= 0, $"No section '{heading}'.");
        var end = Array.FindIndex(lines, start + 1, line => line.StartsWith("## ", StringComparison.Ordinal));
        return string.Join('\n', lines[start..(end < 0 ? lines.Length : end)]);
    }
}
