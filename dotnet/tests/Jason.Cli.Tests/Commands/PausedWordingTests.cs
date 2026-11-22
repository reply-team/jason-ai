using System.CommandLine;
using System.Text.RegularExpressions;
using Jason.Cli;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Commands;

/// <summary>
/// A person's line that a reply stopped is <c>paused</c>, one word in the code and in what a person reads: the state the
/// summary, <c>list-contacts</c> and the item results name. "Held" is a send the provider holds for a later moment, a
/// different thing, so the help a person and an agent read never calls the paused state held: no held line, person,
/// member or contact, no people held in a campaign, and no line held, is held or stays held. The act keeps its own name, the
/// hold, and every other held thing keeps the word: an item held at a daily limit, a letter the provider holds for later,
/// <c>audience_search_held</c>, a search that is held.
/// </summary>
public partial class PausedWordingTests
{
    /// <summary>The wordings that call the paused state held.</summary>
    [GeneratedRegex(@"\bheld (?:line|lines|person|people|member|members|contact|contacts)\b|\bline (?:is |stays )?held\b|\bpeople held in\b|\bwho is not held\b", RegexOptions.IgnoreCase)]
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
