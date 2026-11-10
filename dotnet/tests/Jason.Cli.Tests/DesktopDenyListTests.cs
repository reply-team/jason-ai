using System.CommandLine;
using System.Reflection;
using Jason.Cli;
using Jason.Contracts.Api;

namespace Jason.Cli.Tests;

/// <summary>
/// The desktop's chat drives an agent host that may run <c>jason</c>, fenced by two files. In
/// <c>desktop/chat-deny-list.txt</c>, the listed commands are refused by the host's tool fence and by the desktop, and a
/// command naming a listed operation is refused by the desktop. <c>desktop/chat-allow-list.txt</c> names the verbs the
/// host runs without asking, and any other jason command goes to the person's card. The deny list is held here to the
/// CLI's own tables and to the Runtime API's names, and the allow list is written here from the CLI's command tree, so a
/// verb that becomes a person's own word, a new verb, or a line taken out of either file is red in this class before an
/// agent can run it.
/// </summary>
public class DesktopDenyListTests
{
    /// <summary>
    /// The verbs that ask the runtime nothing, or only to stop and start (<c>DefaultActorTests.NotRun</c>), which the chat's
    /// agent keeps: reads of this installation that change nothing. The reads under <c>runtime</c> and <c>update</c> are
    /// not among them: the deny list takes those nouns away whole.
    /// </summary>
    private static readonly string[] ReadsTheAgentKeeps = ["status", "logs", "doctor"];

    /// <summary>
    /// The verbs the chat refuses beyond the CLI's own table: taking someone off the do-not-contact register, changing which
    /// plugin performs an operation, changing which profile a role runs on, and adding a role, whose entry command the
    /// dispatcher starts when work names the role.
    /// </summary>
    private static readonly string[] TheChatsOwnWord = ["suppression remove", "route set", "route unset", "role set-profile", "role add"];

    /// <summary>
    /// The verbs the deny list leaves to the agent that still go to a person's card at every use: <c>doctor</c> writes its
    /// bundle to any path it is given.
    /// </summary>
    private static readonly string[] AskedEachTime = ["doctor"];

    private const string UpdateVariable = "JASON_UPDATE_FIXTURES";

    private const string AllowListHeader =
        "# The jason commands the desktop's chat runs without asking, one per line: every verb of the CLI that the deny list\n" +
        "# does not cover, but doctor, which writes its bundle to any path and so goes to the person's card at every use.\n" +
        "#\n" +
        "# Written from the CLI's command tree and chat-deny-list.txt by DesktopDenyListTests; a verb added to the CLI is red\n" +
        "# there until JASON_UPDATE_FIXTURES=1 writes this file again. Do not edit it by hand.\n";

    /// <summary>
    /// The allow list is the CLI's verbs, every leaf of its command tree, less what the deny list covers and less the verbs
    /// asked each time: the host lets the agent run these and only these, so a new verb is red here until the file is
    /// written again, and the deny list's word decides whether it is on it.
    /// </summary>
    [Fact]
    public void The_allow_list_is_every_verb_of_the_cli_the_deny_list_does_not_cover()
    {
        using var paths = new TempPaths();
        var root = CliApp.BuildRootCommand(new CliEnvironment(TextWriter.Null, TextWriter.Null, paths.Paths));
        var denied = DenyList();

        var allowed = Verbs(root, [])
            .Where(verb => !denied.Any(entry => Covers(entry, verb)) && !AskedEachTime.Contains(verb))
            .Order(StringComparer.Ordinal);
        var text = AllowListHeader + string.Concat(allowed.Select(verb => verb + "\n"));

        var file = Path.Combine(RepositoryRoot(), "desktop", "chat-allow-list.txt");
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            File.WriteAllText(file, text);
            return;
        }

        Assert.True(File.Exists(file), $"desktop/chat-allow-list.txt is not there. Rerun this test with {UpdateVariable}=1 and commit the file.");
        Assert.Equal(text, File.ReadAllText(file).Replace("\r\n", "\n", StringComparison.Ordinal));

        static IEnumerable<string> Verbs(Command command, string[] words)
        {
            foreach (var sub in command.Subcommands.Where(sub => !sub.Hidden))
            {
                string[] path = [.. words, sub.Name];
                if (sub.Subcommands.Count == 0)
                {
                    yield return string.Join(' ', path);
                }
                else
                {
                    foreach (var verb in Verbs(sub, path))
                    {
                        yield return verb;
                    }
                }
            }
        }
    }

    [Fact]
    public void The_allow_list_names_no_verb_that_is_a_person_s_own_word_and_not_doctor()
    {
        var file = Path.Combine(RepositoryRoot(), "desktop", "chat-allow-list.txt");
        var allowed = File.ReadAllLines(file).Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('#')).ToList();

        Assert.NotEmpty(allowed);
        Assert.DoesNotContain("doctor", allowed);
        Assert.Empty(allowed.Where(verb => DefaultActorTests.TheEleven.Select(DefaultActorTests.VerbOf).Contains(verb)).ToList());
    }

    [Fact]
    public void The_deny_list_holds_every_verb_that_is_a_person_s_own_word()
    {
        var listed = DenyList();

        var missing = DefaultActorTests.TheEleven.Select(DefaultActorTests.VerbOf).Where(verb => !listed.Contains(verb)).ToList();

        Assert.Empty(missing);
    }

    /// <summary>
    /// Every verb that is not an operation the runtime is asked for is in the list, or is one of the reads the agent keeps:
    /// a verb that changes the installation, added later, is red here before it ships.
    /// </summary>
    [Fact]
    public void The_deny_list_holds_every_verb_that_is_not_an_operation_but_the_reads_the_agent_keeps()
    {
        var listed = DenyList();

        var missing = DefaultActorTests.NotRun.Keys
            .Where(verb => !ReadsTheAgentKeeps.Contains(verb) && !listed.Any(entry => Covers(entry, verb)))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(missing);
    }

    /// <summary>The reads the agent keeps are exactly that: none of them is one a deny-list entry takes away.</summary>
    [Fact]
    public void No_read_the_agent_keeps_is_covered_by_the_deny_list()
    {
        var listed = DenyList();

        var covered = ReadsTheAgentKeeps.Where(verb => listed.Any(entry => Covers(entry, verb))).ToList();

        Assert.Empty(covered);
    }

    [Fact]
    public void The_deny_list_holds_the_verbs_the_chat_refuses_beyond_the_cli_s_table()
    {
        var listed = DenyList();

        var missing = TheChatsOwnWord.Where(verb => !listed.Contains(verb)).ToList();

        Assert.Empty(missing);
    }

    /// <summary>An entry covers a verb when the entry's words begin the verb's: <c>runtime</c> covers <c>runtime stop</c>.</summary>
    private static bool Covers(string entry, string verb) =>
        verb == entry || verb.StartsWith(entry + " ", StringComparison.Ordinal);

    /// <summary>Every line names a command the CLI has, so a misspelt line is never a fence around nothing.</summary>
    [Fact]
    public void Every_line_of_the_deny_list_names_a_command_of_the_cli()
    {
        using var paths = new TempPaths();
        var root = CliApp.BuildRootCommand(new CliEnvironment(TextWriter.Null, TextWriter.Null, paths.Paths));

        var unknown = DenyList().Where(entry => !Names(root, entry.Split(' '))).ToList();

        Assert.Empty(unknown);

        static bool Names(Command command, string[] words)
        {
            foreach (var word in words)
            {
                if (command.Subcommands.FirstOrDefault(sub => sub.Name == word) is not { } next)
                {
                    return false;
                }

                command = next;
            }

            return true;
        }
    }

    /// <summary>Every operation line names an operation the Runtime API has, or its journal stream.</summary>
    [Fact]
    public void Every_operation_line_of_the_deny_list_names_an_operation_of_the_runtime_api()
    {
        var known = typeof(Operations).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Append(Streams.Journal)
            .ToHashSet(StringComparer.Ordinal);

        var unknown = DeniedOperations().Where(operation => !known.Contains(operation)).ToList();

        Assert.Empty(unknown);
    }

    /// <summary>
    /// The operations section names, by construction, every operation a denied verb posts that no allowed verb posts: each
    /// verb that reaches the runtime is run against a recording handler, a person's own word with an actor named, and what
    /// the denied ones post, less what the others post, is in the file, with the journal's stream and the runtime's stop,
    /// drain and resume, which the verbs that are not run call, so that a command naming one is refused by the desktop.
    /// </summary>
    [Fact]
    public async Task The_deny_list_names_every_operation_a_denied_verb_posts_and_no_allowed_verb_posts()
    {
        var denied = DenyList();
        var fromDenied = new HashSet<string>(StringComparer.Ordinal);
        var fromAllowed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var args in DefaultActorTests.Runs)
        {
            var run = await DefaultActorTests.RunEachAsync(args);
            Assert.True(run.Requests.Count > 0, $"'jason {DefaultActorTests.VerbOf(args)}' sent nothing: exit {run.Exit}, saying:{Environment.NewLine}{run.Error}");
            var verb = DefaultActorTests.VerbOf(args);
            (denied.Any(entry => Covers(entry, verb)) ? fromDenied : fromAllowed).UnionWith(run.Requests.Select(request => request.Operation));
        }

        foreach (var args in DefaultActorTests.TheEleven)
        {
            var run = await DefaultActorTests.RunEachAsync(["--actor", "human:ada", .. args]);
            Assert.True(run.Requests.Count > 0, $"'jason {DefaultActorTests.VerbOf(args)}' sent nothing: exit {run.Exit}, saying:{Environment.NewLine}{run.Error}");
            fromDenied.UnionWith(run.Requests.Select(request => request.Operation));
        }

        // status and doctor, which the agent keeps and which are not run here, ask the runtime's own report too.
        fromAllowed.Add(Operations.SystemInfo);
        var expected = fromDenied.Except(fromAllowed)
            .Concat([Streams.Journal, Operations.SystemShutdown, Operations.SystemDrain, Operations.SystemResume]);

        var missing = expected.Where(operation => !DeniedOperations().Contains(operation)).Distinct().Order(StringComparer.Ordinal).ToList();

        Assert.True(missing.Count == 0, $"The deny list's operations lack: {string.Join(", ", missing)}");
    }

    /// <summary>The file's command lines: an operation line is told by its dot.</summary>
    private static HashSet<string> DenyList() =>
        Lines().Where(line => !line.Contains('.')).ToHashSet(StringComparer.Ordinal);

    private static HashSet<string> DeniedOperations() =>
        Lines().Where(line => line.Contains('.')).ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<string> Lines() =>
        File.ReadAllLines(Path.Combine(RepositoryRoot(), "desktop", "chat-deny-list.txt"))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'));

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
}
