using System.CommandLine;
using Jason.Cli;

namespace Jason.Cli.Tests;

/// <summary>
/// The desktop's chat drives an agent host that may run <c>jason</c>, fenced by one file, <c>desktop/chat-deny-list.txt</c>:
/// the listed commands are refused by the host's tool fence, and the listed operations are not callable through the
/// allowed tools. The file is held here to the CLI's own tables, so a verb that becomes a person's own word, or a line
/// taken out of the file, is red in this class before an agent can run it.
/// </summary>
public class DesktopDenyListTests
{
    /// <summary>
    /// The verbs that ask the runtime nothing, or only to stop and start (<c>DefaultActorTests.NotRun</c>), which the chat's
    /// agent keeps: reads of this installation that change nothing.
    /// </summary>
    private static readonly string[] ReadsTheAgentKeeps =
        ["status", "logs", "doctor", "runtime status", "runtime autostart status", "update status", "update check"];

    /// <summary>
    /// The verbs the chat refuses beyond the CLI's own table: taking someone off the do-not-contact register, changing which
    /// plugin performs an operation, and changing which profile a role runs on.
    /// </summary>
    private static readonly string[] TheChatsOwnWord = ["suppression remove", "route set", "route unset", "role set-profile"];

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

    private static HashSet<string> DenyList()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        return File.ReadAllLines(Path.Combine(root.FullName, "desktop", "chat-deny-list.txt"))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToHashSet(StringComparer.Ordinal);
    }
}
