using Jason.Cli;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests;

/// <summary>
/// Every top-level name is a noun, with the declared exceptions. The rule is in <c>CliApp</c>'s own doc
/// comment and this is the half that bites: a further exception has to be added here, in the open, rather
/// than arriving as one more verb somebody thought was obviously fine.
/// </summary>
/// <remarks>
/// <para>
/// Read from the program rather than from its internals. <c>BuildRootCommand</c> is internal and this assembly
/// is not its friend; the repository has exactly one friend declaration and it is for tests that cannot be
/// written any other way. This one can: the list comes from <c>--help</c>, which is the surface a person
/// reads, and each name is then confirmed through the argument router — a noun this program knows does not
/// come back "unrecognized".
/// </para>
/// </remarks>
public class VerbMapTests
{
    /// <summary>
    /// The names that are not nouns at all. None can be one; <c>CliApp</c> says why. <c>status</c> answers
    /// about one installation rather than about anything the API owns, <c>uninstall</c> takes this
    /// installation off the machine, which is an act with no noun behind it and no operation either,
    /// <c>logs</c> reads the files the runtime writes, which the runtime is never asked about, and
    /// <c>doctor</c> collects one installation into one file, half of which is not the runtime's to know, and
    /// <c>export</c> writes this installation's files into one archive while the runtime is stopped, which the runtime
    /// cannot do of itself while it runs.
    /// </summary>
    private static readonly string[] DeclaredExceptions = ["status", "uninstall", "logs", "doctor", "export", "import"];

    /// <summary>
    /// The nouns with no API operation behind them. Two change this installation rather than asking the
    /// runtime about anything, and the third starts the person's own agent host in a terminal, which the runtime
    /// never starts or knows about, so nothing in <c>Operations</c> stands behind any of them — which is why the
    /// map's own doc comment no longer claims one-to-one with a single exception.
    /// </summary>
    private static readonly string[] WithoutAnOperation = ["session", "skills", "update"];

    /// <summary>
    /// Exact, and exact at every commit. A noun arrives here in the same commit that adds it to the program,
    /// which is a one-line diff beside the verb — and that is the whole point of a guard like this.
    /// </summary>
    private static readonly string[] Nouns =
    [
        "approval", "audience", "block", "campaign", "contact", "decision", "events", "journal", "operation", "plugin", "preference", "profile", "project",
        "reply", "report", "role", "rolenote", "route", "runtime", "session", "skills", "subscription", "suppression", "ui", "update", "workitem",
    ];

    [Fact]
    [Critical(Family.R2, "the verb map is one-to-one")]
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
    /// And no exception has subcommands. <c>jason status probe</c> is how a status verb becomes a diagnostics
    /// tool, and diagnostics arrive here as names of their own rather than as <c>status</c>'s subcommands;
    /// <c>jason logs tail</c> is how a reader of files grows a follower that never ends;
    /// <c>jason doctor send</c> is how a file a person reads before sharing it grows a way to leave unread; and
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

    /// <summary>And the exceptions are exactly these, so another arrives in this diff or not at all.</summary>
    public static TheoryData<string> Exceptions() => [.. DeclaredExceptions];

    /// <summary>
    /// And the names that stand for no operation are exactly the ones declared. A noun that quietly grew
    /// without one is the map drifting from the rule its own doc comment states.
    /// </summary>
    [Fact]
    [Critical(Family.R2, "the eleven declared exceptions")]
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

        // And the verbs stand for none either. Each says it never will, and nothing held that: an
        // `uninstall.*`, `status.*`, `logs.*`, `doctor.*`, `export.*` or `import.*` operation arriving in the contracts would have kept
        // this test green while the eleven declared exceptions quietly became ten.
        Assert.All(DeclaredExceptions, exception => Assert.DoesNotContain(exception, operations));
    }

    /// <summary>
    /// The <c>ui</c> group is its three operations and one verb that is none: <c>install</c>, which places the release's
    /// build on this machine and asks the runtime nothing, the way <c>skills install</c> does. A second verb like it is a
    /// decision, and it is made here.
    /// </summary>
    [Fact]
    public void The_ui_verb_group_is_its_three_operations_and_install()
    {
        using var paths = new TempPaths();
        var group = Jason.Cli.Commands.UiCommands.Build(new CliEnvironment(TextWriter.Null, TextWriter.Null, paths.Paths), new System.CommandLine.Option<string?>("--actor"));
        var verbs = group.Subcommands.Select(command => command.Name).Order(StringComparer.Ordinal).ToArray();
        var operations = typeof(Contracts.Api.Operations)
            .GetFields()
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!)
            .Where(name => name.StartsWith("ui.", StringComparison.Ordinal))
            .Select(name => name["ui.".Length..]);

        Assert.Equal(["install", "open", "revoke", "tokens"], verbs);
        Assert.Equal(["install"], verbs.Except(operations, StringComparer.Ordinal));
    }

    /// <summary>
    /// The verbs that map one to one to a stream rather than to an operation: a stream is a route of the API, named in
    /// <see cref="Contracts.Api.Streams"/>, that a client upgrades to a WebSocket and reads, and its verb is no exception
    /// to the map. A second stream arrives here with its verb, in the open.
    /// </summary>
    private static readonly Dictionary<string, string> StreamVerbs = new(StringComparer.Ordinal)
    {
        ["journal watch"] = Contracts.Api.Streams.Journal,
    };

    /// <summary>
    /// The <c>journal</c> group is its three operations and <c>watch</c>, which is the journal's stream, one to one. The
    /// stream is a route of the API beside the operations and none of them, so the verb is not a declared exception.
    /// </summary>
    [Fact]
    [Critical(Family.R2, "a stream's verb maps to its route")]
    public void The_journal_verb_group_is_its_three_operations_and_the_verb_of_its_stream()
    {
        using var paths = new TempPaths();
        var group = Jason.Cli.Commands.JournalCommands.Build(new CliEnvironment(TextWriter.Null, TextWriter.Null, paths.Paths), new System.CommandLine.Option<string?>("--actor"));
        var verbs = group.Subcommands.Select(command => command.Name).Order(StringComparer.Ordinal).ToArray();
        var operations = typeof(Contracts.Api.Operations)
            .GetFields()
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();
        var journalOperations = operations
            .Where(name => name.StartsWith("journal.", StringComparison.Ordinal))
            .Select(name => name["journal.".Length..]);

        Assert.Equal(["append", "kinds", "list", "watch"], verbs);
        Assert.Equal(["watch"], verbs.Except(journalOperations, StringComparer.Ordinal));

        // Every stream has its verb and every stream verb its stream, under the stream's own noun; a stream is a route of
        // the version every operation is under, and never an operation itself.
        Assert.Equal(Contracts.Api.Streams.All.Order(StringComparer.Ordinal), StreamVerbs.Values.Order(StringComparer.Ordinal));
        Assert.All(StreamVerbs, pair => Assert.Equal(pair.Key.Split(' ')[0], pair.Value.Split('.')[0]));
        Assert.Equal(Contracts.Api.Streams.JournalRoute, Contracts.Api.Operations.Route(Contracts.Api.Streams.Journal));
        Assert.All(Contracts.Api.Streams.All, stream => Assert.DoesNotContain(stream, operations));
    }

    /// <summary>
    /// The <c>subscription</c> group is its three operations and nothing beside them: where each subscribed kind stands,
    /// the records of its chains, and a person's rewind of its cursor. Running a chain is the runtime's own.
    /// </summary>
    [Fact]
    public void The_subscription_verb_group_is_its_three_operations()
    {
        using var paths = new TempPaths();
        var group = Jason.Cli.Commands.SubscriptionCommands.Build(new CliEnvironment(TextWriter.Null, TextWriter.Null, paths.Paths), new System.CommandLine.Option<string?>("--actor"));
        var verbs = group.Subcommands.Select(command => command.Name).Order(StringComparer.Ordinal).ToArray();
        var operations = typeof(Contracts.Api.Operations)
            .GetFields()
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!)
            .Where(name => name.StartsWith("subscription.", StringComparison.Ordinal))
            .Select(name => name["subscription.".Length..])
            .Order(StringComparer.Ordinal);

        Assert.Equal(["list", "rewind", "runs"], verbs);
        Assert.Equal(operations, verbs);
    }

    /// <summary>
    /// The <c>plugin</c> group is its two operations and one verb that is none: <c>install</c>, which places the package
    /// the release names in the plugins directory, as <c>ui install</c> places the release's UI, and asks the runtime only
    /// the reload <c>plugin reload</c> asks. The eighth exception, made here in the open.
    /// </summary>
    [Fact]
    public void The_plugin_verb_group_is_its_two_operations_and_install()
    {
        using var paths = new TempPaths();
        var group = Jason.Cli.Commands.PluginCommands.Build(new CliEnvironment(TextWriter.Null, TextWriter.Null, paths.Paths), new System.CommandLine.Option<string?>("--actor"));
        var verbs = group.Subcommands.Select(command => command.Name).Order(StringComparer.Ordinal).ToArray();
        var operations = typeof(Contracts.Api.Operations)
            .GetFields()
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!)
            .Where(name => name.StartsWith("plugin.", StringComparison.Ordinal))
            .Select(name => name["plugin.".Length..]);

        Assert.Equal(["install", "list", "reload"], verbs);
        Assert.Equal(["install"], verbs.Except(operations, StringComparer.Ordinal));
        Assert.DoesNotContain("install", operations);
    }

    /// <summary>
    /// The <c>session</c> noun is one verb, <c>open</c>, and no operation: it starts the person's own agent host in a
    /// terminal of this machine, which the runtime never starts or knows about. The ninth exception, made here in the
    /// open; a second verb under it is a decision of its own, and so is a third form of <c>open</c>.
    /// </summary>
    [Fact]
    public void The_session_noun_is_open_alone()
    {
        using var paths = new TempPaths();
        var group = Jason.Cli.Session.SessionCommands.Build(new CliEnvironment(TextWriter.Null, TextWriter.Null, paths.Paths), new System.CommandLine.Option<string?>("--actor"));

        Assert.Equal(["open"], group.Subcommands.Select(command => command.Name));

        // Its three forms are these options and no others: an item's (--approval, --question), a folder's (--campaign,
        // --project, --here, --host, --shell, --resume) and Jason's own (--setup, with --host), with --dry-run and --human
        // for all three. A further option is a change of the declared exception and is made in the open, with its text in
        // CliApp.
        Assert.Equal(
            ["--approval", "--question", "--campaign", "--project", "--here", "--host", "--shell", "--resume", "--setup", "--dry-run", "--human"],
            group.Subcommands.Single().Options.Select(option => option.Name).Where(name => name.StartsWith("--", StringComparison.Ordinal)));
        Assert.DoesNotContain(
            typeof(Contracts.Api.Operations).GetFields().Where(field => field.IsLiteral).Select(field => (string)field.GetRawConstantValue()!),
            name => name.StartsWith("session.", StringComparison.Ordinal));
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
