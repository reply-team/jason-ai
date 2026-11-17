using System.Text.RegularExpressions;
using Jason.Cli;

namespace Jason.Cli.Tests.Documentation;

/// <summary>
/// The commands the CLI's own sentences name, handed to the argument router. A refusal, a problem line or an answer that
/// tells a person or an agent to run <c>jason …</c> is the way on at the moment it is needed; one the CLI does not
/// understand teaches that the tool is broken. The pages' commands and the skills' are typed by their own guards
/// (<see cref="DocumentedPageCommandsTests"/>, <c>DocumentedSkillCommandsTests</c>), and the repairs <c>jason status</c>
/// prints by <c>StatusDisciplineTests</c>; this one reads the sentences of the update, the machine setup and the
/// elevation and lock refusals, the rows of the release page's two tables of how a carried update ends and what a
/// kill leaves, and the page's sections on the move into Jason Desktop and going back from 0.49, which name a command
/// in the middle of a sentence where the page guard, which reads lines, does not look.
/// </summary>
/// <remarks>
/// Each command is parsed, never run: an update, an install or a rollback typed for real would act. A command with a
/// placeholder in it - an interpolated <c>{…}</c> or a <c>&lt;…&gt;</c> - is not a command as written and is left.
/// </remarks>
public partial class CliSentenceCommandsTests
{
    /// <summary>The source folders and files whose sentences are read, under <c>dotnet/src/Jason.Cli</c>.</summary>
    private static readonly string[] Sources =
    [
        "Update",
        "Installation",
        Path.Combine("Commands", "UpdateCommands.cs"),
    ];

    public static TheoryData<string, string> Named()
    {
        var data = new TheoryData<string, string>();
        foreach (var (where, command) in Commands())
        {
            data.Add(where, command);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Named))]
    public void Every_command_a_sentence_of_the_clis_own_names_is_one_its_router_takes(string where, string command) =>
        Assert.True(Refusal(command) is null, $"{where} names `{command}`, and the CLI answers: {Refusal(command)}");

    /// <summary>And the router refuses what it does not take: a verb it lacks, and an option a verb lacks.</summary>
    [Theory]
    [InlineData("jason update resume")]
    [InlineData("jason update apply --no-such-option")]
    [InlineData("jason system resume")]
    public void The_router_refuses_a_command_it_does_not_take(string command) => Assert.NotNull(Refusal(command));

    /// <summary>What the router says of a command, parsed and never run; null where it takes it.</summary>
    private static string? Refusal(string command)
    {
        using var dir = new TempPaths();
        var parsed = CliApp.BuildRootCommand(DocumentedPageCommandsTests.Machine(dir, new StringWriter())).Parse([.. ShellWords.Split(command)]);
        var unknown = CliApp.UnknownOptions(parsed);
        return parsed.Errors.Count == 0 && unknown.Count == 0
            ? null
            : string.Join(" ", parsed.Errors.Select(error => error.Message).Concat(unknown));
    }

    /// <summary>The reading still finds what these files name: the update's own command, the setup's, the kill table's.</summary>
    [Fact]
    public void The_sentences_name_commands_to_check()
    {
        var commands = Commands().Select(named => named.Command).ToHashSet(StringComparer.Ordinal);
        Assert.True(commands.Count >= 10, $"only {commands.Count} commands were read: {string.Join(", ", commands)}");
        foreach (var expected in new[] { "jason update apply", "jason update rollback", "jason install", "jason runtime start", "jason runtime resume", "jason runtime restart" })
        {
            Assert.Contains(expected, commands);
        }

        Assert.Contains(Commands(), named => named.Where.StartsWith("docs", StringComparison.Ordinal));
    }

    private static IEnumerable<(string Where, string Command)> Commands()
    {
        var root = RepositoryRoot();
        var cli = Path.Combine(root, "dotnet", "src", "Jason.Cli");
        var files = Sources
            .Select(source => Path.Combine(cli, source))
            .SelectMany(path => Directory.Exists(path) ? Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories) : [path])
            .Order(StringComparer.Ordinal);
        foreach (var file in files)
        {
            foreach (var command in Backticked(File.ReadAllText(file)))
            {
                yield return (Path.GetRelativePath(root, file), command);
            }
        }

        // The release page's two tables of a carried update's ends: how it ends, and what a kill leaves.
        var page = Path.Combine("docs", "release-and-update.md");
        var lines = File.ReadAllText(Path.Combine(root, page)).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var tables = 0;
        for (var at = 0; at < lines.Length; at++)
        {
            if (lines[at] is not ("| How it ends | What is at the carried path when the verb answers |" or "| The verb killed | What is left | The way on |"))
            {
                continue;
            }

            tables++;
            for (at += 2; at < lines.Length && lines[at].StartsWith('|'); at++)
            {
                foreach (var command in Backticked(lines[at]))
                {
                    yield return (page, command);
                }
            }
        }

        Assert.Equal(2, tables);

        // And the move's two sections whole, prose and tables: what the move answers and what status names send a person to
        // these commands, from the move into Jason Desktop to the end of going back from 0.49.
        var from = Array.IndexOf(lines, "### Moving a command-line install into Jason Desktop");
        var to = Array.FindIndex(lines, from + 1, line => line.StartsWith("## ", StringComparison.Ordinal));
        Assert.True(from >= 0 && to > from && lines[from..to].Contains("### Going back from 0.49"), "the move's sections were not found");
        foreach (var command in lines[from..to].SelectMany(Backticked).Distinct(StringComparer.Ordinal))
        {
            yield return ($"{page} (the move)", command);
        }
    }

    private static IEnumerable<string> Backticked(string text) =>
        Command().Matches(text)
            .Select(match => match.Groups[1].Value.Trim())
            .Where(command => command.IndexOfAny(['{', '<', '…']) < 0)
            .Distinct(StringComparer.Ordinal);

    [GeneratedRegex("`(jason [a-z][^`\\n]*)`")]
    private static partial Regex Command();

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }
}
