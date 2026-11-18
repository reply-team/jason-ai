using System.CommandLine.Parsing;
using System.Text.RegularExpressions;
using Jason.Cli;

namespace Jason.Cli.Tests.Documentation;

/// <summary>
/// The commands the CLI's own sentences name, handed to the argument router. A refusal, a problem line or an answer that
/// tells a person or an agent to run <c>jason …</c> is the way on at the moment it is needed; one the CLI does not
/// understand teaches that the tool is broken. The pages' commands and the skills' are typed by their own guards
/// (<see cref="DocumentedPageCommandsTests"/>, <c>DocumentedSkillCommandsTests</c>), and the repairs <c>jason status</c>
/// prints by <c>StatusDisciplineTests</c>; this one reads the sentences of the update, the machine setup and the
/// elevation and lock refusals, and the rows of the release page's two tables of how a carried update ends and what a
/// kill leaves, which name a command in the middle of a sentence where the page guard, which reads lines, does not look.
/// </summary>
/// <remarks>
/// Each command is parsed, never run: an update, an install or a rollback typed for real would act. A command with a
/// placeholder in it - an interpolated <c>{…}</c> or a <c>&lt;…&gt;</c> - is not a command as written and is left.
/// </remarks>
public partial class CliSentenceCommandsTests
{
    /// <summary>
    /// The source folders and files whose sentences are read, under <c>dotnet/src/Jason.Cli</c>; a name ending in <c>*</c>
    /// is every file of the folder whose name starts with it.
    /// </summary>
    private static readonly string[] Sources =
    [
        "Update",
        "Installation",
        "Status",
        "Uninstall",
        Path.Combine("Commands", "UpdateCommands.cs"),
        Path.Combine("Commands", "Runtime*"),
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

    /// <summary>
    /// A sentence that names a verb rather than a line to type - "the `jason plugin grant` command that grants it" - is
    /// taken where the router's only complaint is an argument or a required option it was not given.
    /// </summary>
    [Theory]
    [InlineData("jason plugin grant")]
    [InlineData("jason route set --global")]
    public void A_verb_named_without_its_arguments_is_taken(string command) => Assert.Null(Refusal(command));

    /// <summary>What the router says of a command, parsed and never run; null where it takes it.</summary>
    /// <remarks>
    /// An error that stands on the command itself, or on an argument or option given no token, is a value the sentence did
    /// not give, a verb named rather than a line to type; a word or an option the router does not know is a token left
    /// unmatched or an unknown option, and is refused.
    /// </remarks>
    private static string? Refusal(string command)
    {
        using var dir = new TempPaths();
        var parsed = CliApp.BuildRootCommand(DocumentedPageCommandsTests.Machine(dir, new StringWriter())).Parse([.. ShellWords.Split(command)]);
        var unknown = CliApp.UnknownOptions(parsed);
        var refused = parsed.Errors.Where(error => error.SymbolResult switch
        {
            CommandResult => parsed.UnmatchedTokens.Count > 0,
            ArgumentResult argument => argument.Tokens.Count > 0,
            OptionResult option => option.Tokens.Count > 0,
            _ => true,
        }).ToList();
        return refused.Count == 0 && unknown.Count == 0 && parsed.UnmatchedTokens.Count == 0
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
        foreach (var folder in new[] { "Status", "Uninstall", "Commands" })
        {
            var under = Path.Combine("dotnet", "src", "Jason.Cli", folder) + Path.DirectorySeparatorChar;
            Assert.Contains(Commands(), named => named.Where.StartsWith(under, StringComparison.Ordinal));
        }

        Assert.Contains(Commands(), named => named.Where.EndsWith("RuntimeStartCommand.cs", StringComparison.Ordinal));
    }

    /// <summary>
    /// Each way a sentence names a command is read: backticked, single-quoted, a step line's unquoted <c>run: jason …</c>, and
    /// a string literal that is a command whole, as the constants are; a command with a placeholder in it is left.
    /// </summary>
    [Fact]
    public void Each_way_a_sentence_names_a_command_is_read()
    {
        const string Source = """
            Say("x", "`jason update apply` finishes it");
            Say("y", "Stop the runtime with 'jason runtime stop' and run this again.");
            Say("z", $"not updated — {why}; run: jason skills update");
            public const string Start = "jason runtime start";
            Say("p", $"{id} not replaced; run: jason plugin install {id} --replace");
            Say("q", "'jason plugin <id>' and `jason update …`");
            """;

        Assert.Equal(
            ["jason update apply", "jason runtime stop", "jason skills update", "jason runtime start"],
            Named(Source).Order(Comparer<string>.Create((a, b) => Source.IndexOf(a, StringComparison.Ordinal).CompareTo(Source.IndexOf(b, StringComparison.Ordinal)))));
    }

    private static IEnumerable<(string Where, string Command)> Commands()
    {
        var root = RepositoryRoot();
        var cli = Path.Combine(root, "dotnet", "src", "Jason.Cli");
        var files = Sources
            .Select(source => Path.Combine(cli, source))
            .SelectMany(path => path.EndsWith('*')
                ? Directory.EnumerateFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".cs")
                : Directory.Exists(path) ? Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories) : [path])
            .Order(StringComparer.Ordinal);
        foreach (var file in files)
        {
            foreach (var command in Named(Sentences(File.ReadAllText(file))))
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
    }

    private static IEnumerable<string> Backticked(string text) => Kept(Command().Matches(text));

    /// <summary>A source file without its comment lines, which no person or agent is ever shown.</summary>
    private static string Sentences(string source) =>
        string.Join('\n', source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    /// <summary>
    /// Every command a source file names: backticked; single-quoted, as the uninstall's and the runtime verbs' sentences
    /// quote one; after <c>run: </c> at the end of a step line, unquoted; and a string literal that is a command whole, a
    /// constant a sentence is built from.
    /// </summary>
    private static IEnumerable<string> Named(string text) =>
        Kept(Command().Matches(text)
            .Concat(Quoted().Matches(text))
            .Concat(StepLine().Matches(text))
            .Concat(Literal().Matches(text)));

    private static IEnumerable<string> Kept(IEnumerable<Match> matches) =>
        matches
            .Select(match => match.Groups[1].Value.Trim())
            .Where(command => command.IndexOfAny(['{', '<', '…']) < 0)
            .Distinct(StringComparer.Ordinal);

    [GeneratedRegex("`(jason [a-z][^`\\n]*)`")]
    private static partial Regex Command();

    [GeneratedRegex("'(jason [a-z][^'\\n]*)'")]
    private static partial Regex Quoted();

    /// <summary>A step line's way on, unquoted to the end of its string: <c>…; run: jason skills update"</c>.</summary>
    [GeneratedRegex("run: (jason [a-z][^\"\\n]*)\"")]
    private static partial Regex StepLine();

    /// <summary>A string literal that is a command and nothing else: <c>public const string Start = "jason runtime start";</c>.</summary>
    [GeneratedRegex("(?<![$@\\w])\"(jason [a-z][^\"\\\\\\n]*)\"")]
    private static partial Regex Literal();

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
