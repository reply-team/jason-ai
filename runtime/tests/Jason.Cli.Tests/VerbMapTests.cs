using Jason.Cli;

namespace Jason.Cli.Tests;

/// <summary>
/// Every top-level name is a noun, with one declared exception. The rule is in <c>CliApp</c>'s own doc comment
/// and this is the half that bites: a second exception has to be added here, in the open, rather than arriving
/// as one more verb somebody thought was obviously fine.
/// </summary>
/// <remarks>
/// <para>
/// Read from the program rather than from its internals. <c>BuildRootCommand</c> is internal and this assembly
/// is not its friend; the repository has exactly one friend declaration and it is for a test that cannot be
/// written any other way. This one can: the list comes from <c>--help</c>, which is the surface a person
/// reads, and each name is then confirmed through the argument router — a noun this program knows does not
/// come back "unrecognized".
/// </para>
/// </remarks>
public class VerbMapTests
{
    /// <summary>The one name that is not a noun. It cannot be one; <c>CliApp</c> says why.</summary>
    private const string DeclaredException = "status";

    /// <summary>
    /// Exact, and exact at every commit. A noun arrives here in the same commit that adds it to the program,
    /// which is a one-line diff beside the verb — and that is the whole point of a guard like this.
    /// </summary>
    private static readonly string[] Nouns =
    [
        "approval", "campaign", "contact", "decision", "journal", "plugin", "profile", "report",
        "role", "rolenote", "route", "runtime", "suppression", "update", "workitem",
    ];

    [Fact]
    public async Task Every_top_level_name_is_a_noun_except_the_one_declared_exception()
    {
        var printed = await NamesFromHelpAsync();

        Assert.Contains(DeclaredException, printed);
        Assert.Equal(Nouns, printed.Where(name => name != DeclaredException).Order(StringComparer.Ordinal));
    }

    /// <summary>And each of them really is a command, asked of the router rather than of the help text.</summary>
    [Theory]
    [MemberData(nameof(EveryName))]
    public async Task Each_name_is_a_command_this_program_knows(string name)
    {
        var (exit, error) = await RunAsync(name);

        Assert.True(
            !error.Contains($"Unrecognized command or argument '{name}'", StringComparison.Ordinal),
            $"'--help' prints '{name}' and the program does not know it: exit {exit}, saying:{Environment.NewLine}{error}");
    }

    /// <summary>
    /// And the exception has no subcommands. <c>jason status logs</c> and <c>jason status probe</c> are how a
    /// status verb becomes a diagnostics verb, which is a story of its own and deliberately not this one.
    /// </summary>
    [Fact]
    public async Task The_exception_has_no_subcommands()
    {
        var printed = await NamesFromHelpAsync(DeclaredException);

        Assert.True(
            printed.Count == 0,
            $"'jason {DeclaredException}' has grown subcommands: {string.Join(", ", printed)}. A check may report "
            + "a fact and the command that repairs it; a verb that grew a subcommand grew a diagnostics tool.");
    }

    public static TheoryData<string> EveryName() => [.. Nouns, DeclaredException];

    /// <summary>
    /// The command names a help page prints: the section the library titles "Commands:", first word of each
    /// indented line.
    /// </summary>
    /// <remarks>
    /// If the root page has no such section this fails saying so. A library rewording has to be a red test
    /// somebody looks at, never a silently empty list that agrees with whatever the expectation happens to be
    /// — which is the shape a guard fails in when nobody notices it stopped guarding.
    /// </remarks>
    private static async Task<IReadOnlyList<string>> NamesFromHelpAsync(params string[] path)
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        var exit = await CliApp.RunAsync(
            [.. path, "--help"],
            new CliEnvironment(output, new StringWriter(), dir.Paths),
            TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.Success, exit);
        var text = output.ToString();
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var start = Array.FindIndex(lines, line => line.Trim().Equals("Commands:", StringComparison.Ordinal));

        Assert.True(
            start >= 0 || path.Length > 0,
            "The root help page has no 'Commands:' section, so this guard can no longer read the verb map from "
            + $"it. Look, and fix the reading rather than the map:{Environment.NewLine}{text}");

        var names = new List<string>();
        for (var index = start + 1; start >= 0 && index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.Trim().Length == 0 || !char.IsWhiteSpace(line[0]))
            {
                break;
            }

            // "  campaign <id>  Manage campaigns" -> "campaign"; an argument or an alias follows the name.
            names.Add(line.Trim().Split([' ', ',', '\t'], StringSplitOptions.RemoveEmptyEntries)[0]);
        }

        return names;
    }

    private static async Task<(int Exit, string Error)> RunAsync(params string[] args)
    {
        using var dir = new TempPaths();
        var error = new StringWriter();
        var exit = await CliApp.RunAsync(
            args,
            new CliEnvironment(new StringWriter(), error, dir.Paths),
            TestContext.Current.CancellationToken);
        return (exit, error.ToString());
    }
}
