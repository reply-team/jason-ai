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
    /// <summary>The verbs that change the installation or follow it without end, which no chat runs either.</summary>
    private static readonly string[] Installation =
        ["runtime", "plugin install", "plugin reload", "profile", "skills", "update", "uninstall", "ui", "session", "journal watch"];

    [Fact]
    public void The_deny_list_holds_every_verb_that_is_a_person_s_own_word()
    {
        var listed = DenyList();

        var missing = DefaultActorTests.TheEleven.Select(DefaultActorTests.VerbOf).Where(verb => !listed.Contains(verb)).ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void The_deny_list_holds_every_verb_that_changes_the_installation()
    {
        var listed = DenyList();

        var missing = Installation.Where(verb => !listed.Contains(verb)).ToList();

        Assert.Empty(missing);
    }

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
