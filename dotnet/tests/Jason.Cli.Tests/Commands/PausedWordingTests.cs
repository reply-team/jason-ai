using System.CommandLine;
using System.Text.RegularExpressions;
using Jason.Cli;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Commands;

/// <summary>
/// A person's line that a reply stopped is <c>paused</c>, one word in the code and in what a person reads: the state the
/// summary, <c>list-contacts</c> and the item results name. "Held" is a send the provider holds for a later moment, a
/// different thing, so neither the CLI's help, nor a skill, nor a page that describes the line calls the paused state held: no held line, person,
/// member or contact, no people held in a campaign, and no line held, is held or stays held. The act keeps its own name, the
/// hold, and every other held thing keeps the word: an item held at a daily limit, a letter the provider holds for later,
/// <c>audience_search_held</c>, a search that is held.
/// </summary>
public partial class PausedWordingTests
{
    /// <summary>The wordings that call the paused state held.</summary>
    [GeneratedRegex(@"\bheld (?:line|lines|person|people|member|members|contact|contacts)\b|\bline (?:is |stays )?(?:only )?held\b|\bpeople held in\b|\bwho is not held\b", RegexOptions.IgnoreCase)]
    internal static partial Regex HeldForPaused();

    [Theory]
    [InlineData("A held contact to continue.", true)]
    [InlineData("Continue the line with people held in a campaign.", true)]
    [InlineData("leave the line held", true)]
    [InlineData("the line stays held", true)]
    [InlineData("A member who is not held is answered not_paused.", true)]
    [InlineData("a send the provider holds for later is a held send", false)]
    [InlineData("a paused member is enrolled again", false)]
    [InlineData("a held person", true)]
    [InlineData("a held member", true)]
    [InlineData("the line is held", true)]
    [InlineData("one whose line is only held, paused, still counts", true)]
    [InlineData("an item held at a daily limit of launches", false)]
    [InlineData("a letter the provider holds for later", false)]
    [InlineData("audience_search_held", false)]
    [InlineData("a search that is held waits for the person", false)]
    [InlineData("people held at a daily limit", false)]
    public void The_pattern_knows_the_state_from_a_held_send(string text, bool refused)
    {
        Assert.Equal(refused, HeldForPaused().IsMatch(text));
    }

    [Fact]
    public void No_help_string_calls_a_paused_line_held()
    {
        using var paths = new TempPaths();
        var root = CliApp.BuildRootCommand(new CliEnvironment(TextWriter.Null, TextWriter.Null, paths.Paths));
        var found = new List<string>();
        Walk(root, "jason", found);

        Assert.Empty(found);
    }

    /// <summary>
    /// The pages that describe a person's line, and every skill an agent reads, say it the same way: each file is read
    /// with its line breaks folded into spaces, so a wording wrapped over two lines is found too.
    /// </summary>
    [Fact]
    public void No_skill_and_no_page_of_the_line_calls_a_paused_line_held()
    {
        var root = RepositoryRoot();
        string[] pages =
        [
            "docs/work-execution.md", "docs/inbound-sync.md", "docs/runtime-api.md", "docs/routing.md", "docs/campaign-manager.md",
            "docs/contracts/README.md",
        ];
        var files = pages
            .Concat(Under(root, "skills", "*.md"))
            .Concat(Under(root, "docs/contracts/journal", "contacts_*.json"))
            .Concat(Under(root, "docs/contracts/fixtures/journal", "*.json").Where(file => file.Contains("/contacts_", StringComparison.Ordinal)))
            .ToList();
        Assert.Contains(files, file => file.StartsWith("skills/runtime/roles/manager/", StringComparison.Ordinal));

        var found = new List<string>();
        foreach (var file in files)
        {
            var text = Regex.Replace(File.ReadAllText(Path.Combine(root, file)), @"\s+", " ");
            found.AddRange(HeldForPaused().Matches(text).Select(match => $"{file}: \"{match.Value}\""));
        }

        Assert.Empty(found);
    }

    private static IEnumerable<string> Under(string root, string folder, string pattern) =>
        Directory.EnumerateFiles(Path.Combine(root, folder), pattern, SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal);

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

    private static void Walk(Command command, string path, List<string> found)
    {
        Check(path, command.Description, found);
        foreach (var option in command.Options)
        {
            Check($"{path} {option.Name}", option.Description, found);
        }

        foreach (var argument in command.Arguments)
        {
            Check($"{path} <{argument.Name}>", argument.Description, found);
        }

        foreach (var sub in command.Subcommands)
        {
            Walk(sub, $"{path} {sub.Name}", found);
        }
    }

    private static void Check(string where, string? text, List<string> found)
    {
        if (text is not null && HeldForPaused().Match(text) is { Success: true } match)
        {
            found.Add($"{where}: \"{match.Value}\"");
        }
    }
}
