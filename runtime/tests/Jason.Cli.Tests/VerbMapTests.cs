using Jason.Cli;

namespace Jason.Cli.Tests;

/// <summary>
/// Every top-level name is a noun, with the declared exceptions. The rule is in <c>CliApp</c>'s own doc
/// comment and this is the half that bites: a further exception has to be added here, in the open, rather
/// than arriving as one more verb somebody thought was obviously fine.
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
    /// <summary>
    /// The names that are not nouns at all. Neither can be one; <c>CliApp</c> says why. <c>status</c> answers
    /// about one installation rather than about anything the API owns, and <c>uninstall</c> takes this
    /// installation off the machine, which is an act with no noun behind it and no operation either.
    /// </summary>
    private static readonly string[] DeclaredExceptions = ["status", "uninstall"];

    /// <summary>
    /// The nouns with no API operation behind them. Both change this installation rather than asking the
    /// runtime about anything, so nothing in <c>Operations</c> stands behind either — which is why the map's
    /// own doc comment no longer claims one-to-one with a single exception.
    /// </summary>
    private static readonly string[] WithoutAnOperation = ["skills", "update"];

    /// <summary>
    /// Exact, and exact at every commit. A noun arrives here in the same commit that adds it to the program,
    /// which is a one-line diff beside the verb — and that is the whole point of a guard like this.
    /// </summary>
    private static readonly string[] Nouns =
    [
        "approval", "campaign", "contact", "decision", "journal", "plugin", "profile", "report",
        "role", "rolenote", "route", "runtime", "skills", "suppression", "update", "workitem",
    ];

    [Fact]
    public async Task Every_top_level_name_is_a_noun_except_the_declared_exceptions()
    {
        var printed = await NamesFromHelpAsync();

        foreach (var exception in DeclaredExceptions)
        {
            Assert.Contains(exception, printed);
        }

        Assert.Equal(
            Nouns,
            printed.Where(name => !DeclaredExceptions.Contains(name, StringComparer.Ordinal)).Order(StringComparer.Ordinal));
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
    /// And neither exception has subcommands. <c>jason status logs</c> and <c>jason status probe</c> are how a
    /// status verb becomes a diagnostics verb, which is a story of its own and deliberately not this one; and
    /// <c>jason uninstall everything</c> is how a destructive verb grows a second, less careful spelling.
    /// </summary>
    [Theory]
    [MemberData(nameof(Exceptions))]
    public async Task An_exception_has_no_subcommands(string name)
    {
        var printed = await NamesFromHelpAsync(name);

        Assert.True(
            printed.Count == 0,
            $"'jason {name}' has grown subcommands: {string.Join(", ", printed)}. A check may report "
            + "a fact and the command that repairs it; a verb that grew a subcommand grew a diagnostics tool.");
    }

    public static TheoryData<string> EveryName() => [.. Nouns, .. DeclaredExceptions];

    /// <summary>And the exceptions are exactly these two, so a third arrives in this diff or not at all.</summary>
    public static TheoryData<string> Exceptions() => [.. DeclaredExceptions];

    /// <summary>
    /// And the names that stand for no operation are exactly the ones declared. A noun that quietly grew
    /// without one is the map drifting from the rule its own doc comment states.
    /// </summary>
    [Fact]
    public void Exactly_the_declared_names_have_no_api_operation_behind_them()
    {
        var operations = typeof(Contracts.Api.Operations)
            .GetFields()
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!)
            .Select(name => name.Split('.')[0])
            .ToHashSet(StringComparer.Ordinal);

        var strangers = Nouns.Where(noun => !operations.Contains(Spoken(noun))).Order(StringComparer.Ordinal);

        Assert.Equal(WithoutAnOperation.Order(StringComparer.Ordinal), strangers);

        // And the two verbs stand for none either. Each says it never will, and nothing held that: an
        // `uninstall.*` or `status.*` operation arriving in the contracts would have kept this test green while
        // the four declared exceptions quietly became three.
        Assert.All(DeclaredExceptions, exception => Assert.DoesNotContain(exception, operations));
    }

    /// <summary>
    /// What an operation calls the noun a command spells differently. Two of them differ on purpose, and this
    /// is the whole of that list rather than a rule with exceptions nobody wrote down.
    /// </summary>
    private static string Spoken(string noun) => noun switch
    {
        // The one noun the API spells differently. `jason runtime status` is `system.info` and
        // `jason runtime stop` is `system.shutdown`: the operations exist, under the name the runtime uses
        // for itself rather than the one a person types.
        "runtime" => "system",
        _ => noun,
    };

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

        // A row is a line indented to the name column. Everything indented further is the wrapped
        // continuation of the description beside it, and a blank line can be part of one: a command whose
        // help carries paragraphs prints them right here, under its own row.
        //
        // This used to break at the first blank line, which read the list as far as the first such command --
        // and passed only because that command happened to be the last one in the map. Adding a verb after it
        // is what showed it up. A guard that stops early looks exactly like a guard that did not.
        const int NameColumn = 2;
        var names = new List<string>();
        for (var index = start + 1; start >= 0 && index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.Trim().Length == 0)
            {
                continue;
            }

            // The next section's heading, which is the one thing at column zero.
            if (!char.IsWhiteSpace(line[0]))
            {
                break;
            }

            if (line.Length - line.TrimStart().Length != NameColumn)
            {
                continue;
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
