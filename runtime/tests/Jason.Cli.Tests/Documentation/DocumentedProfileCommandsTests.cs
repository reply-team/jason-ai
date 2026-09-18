using System.Text.RegularExpressions;
using Jason.Cli;

namespace Jason.Cli.Tests.Documentation;

/// <summary>
/// The commands the published pages tell somebody to type, typed. A page is often the first thing a new operator
/// runs, and an example that answers "unrecognized option" teaches them the tool is broken before it teaches
/// them anything else.
/// <para>
/// Every <c>jason profile …</c> line in the pages is run against the real command line, pointed at a data
/// directory with no runtime in it. What is asserted is that it is not a **usage** error: exit 2 is the CLI
/// saying it does not understand what was typed, which is the way a documented command stops working when an
/// option is renamed. Exit 3 — no runtime listening — is the right answer here and is what a passing line gives.
/// </para>
/// </summary>
public partial class DocumentedProfileCommandsTests
{
    private const int UsageError = 2;

    public static TheoryData<string, string> DocumentedCommands()
    {
        var data = new TheoryData<string, string>();
        foreach (var page in new[] { "README.md", Path.Combine("docs", "execution-profiles.md") })
        {
            foreach (var line in Commands(page))
            {
                data.Add(page, line);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(DocumentedCommands))]
    public async Task Every_documented_profile_command_is_understood(string page, string command)
    {
        using var dir = new TempPaths();
        var error = new StringWriter();

        var exit = await CliApp.RunAsync(
            Tokens(command),
            new CliEnvironment(new StringWriter(), error, dir.Paths),
            TestContext.Current.CancellationToken);

        Assert.True(exit != UsageError, $"{page} documents `{command}`, and the CLI answers: {error}");
    }

    /// <summary>At least one line, or the theory above asserts nothing at all.</summary>
    [Fact]
    public void The_pages_carry_profile_commands_to_check() => Assert.NotEmpty(DocumentedCommands());

    private static IEnumerable<string> Commands(string page)
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), page));

        // A shell continuation is one command spread over two lines; the reader types it as one.
        var joined = text.Replace("\\\r\n", " ", StringComparison.Ordinal).Replace("\\\n", " ", StringComparison.Ordinal);
        foreach (var line in joined.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("jason profile ", StringComparison.Ordinal))
            {
                yield return trimmed;
            }
        }
    }

    /// <summary>The tokens a shell would hand the program: quoted runs held together, the program name dropped.</summary>
    private static string[] Tokens(string command) =>
        [.. Quoted().Matches(command).Select(match => match.Value.Trim('"')).Skip(1)];

    [GeneratedRegex("\"[^\"]*\"|\\S+")]
    private static partial Regex Quoted();

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
