using System.CommandLine;
using System.Reflection;
using Jason.Cli;
using System.Text.RegularExpressions;
using Jason.Contracts.Api;
using Jason.Contracts.Execution;
using Jason.Contracts.Harness;
using Jason.Contracts.Hosts;

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
    /// The verbs the chat refuses beyond the CLI's table of verbs that take no default actor: adding a role, whose entry
    /// command the dispatcher starts when work names the role, and linking a project's folder, which then becomes a
    /// working folder of every chat on the project's campaigns.
    /// </summary>
    private static readonly string[] TheChatsOwnWord = ["role add", "project link"];

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
    }

    /// <summary>
    /// The allow list's verbs that change nothing: a chat in Plan only runs these and no other jason command. Each verb of
    /// the allow list is declared here or in <see cref="ChangesTheAgentMakes"/>, never both, so a verb added to the CLI is
    /// red until someone says which it is.
    /// </summary>
    private static readonly string[] ReadsOfThePlan =
    [
        "approval get", "approval list", "approval list-standing", "audience availability", "audience filter-values",
        "audience preview", "block get", "block list", "campaign activity", "campaign activity-by-period", "campaign audience",
        "campaign get", "campaign list", "campaign list-contacts", "campaign summary", "contact get", "contact list",
        "contact thread", "contact threads", "decision get", "decision list", "events sources", "journal kinds", "journal list",
        "logs", "operation describe", "plugin list", "preference list", "project get", "project list", "reply count", "report get",
        "report list", "role list", "rolenote get", "rolenote list", "route list", "route resolve", "sender list", "status",
        "subscription list", "subscription runs", "suppression list", "workitem count", "workitem get", "workitem list",
        "workitem wait",
    ];

    /// <summary>The allow list's verbs that change something: a chat in Plan only never runs them.</summary>
    private static readonly string[] ChangesTheAgentMakes =
    [
        "campaign add-contacts", "campaign archive", "campaign create", "campaign pause", "campaign remove-contacts",
        "campaign start", "campaign update", "campaign update-context", "contact archive", "contact create", "contact update",
        "decision raise", "events backfill", "events signal", "journal append", "project create", "project update",
        "reply record", "rolenote set", "suppression add", "workitem cancel", "workitem complete", "workitem create",
        "workitem heartbeat", "workitem set-result", "workitem update",
    ];

    private const string PlanListHeader =
        "# The jason commands the desktop's chat runs in Plan only, one per line: the verbs of chat-allow-list.txt that change\n" +
        "# nothing. In Plan only every other jason command is refused.\n" +
        "#\n" +
        "# Written from chat-allow-list.txt by DesktopDenyListTests, where each verb of the allow list is declared a read or a\n" +
        "# change; a verb added to the CLI is red there until it is declared and JASON_UPDATE_FIXTURES=1 writes this file\n" +
        "# again. Do not edit it by hand.\n";

    /// <summary>
    /// Every verb of the allow list is declared a read or a change, exactly once, and every declared verb is on the allow
    /// list; the plan list is the reads, in the allow list's order.
    /// </summary>
    [Fact]
    public void The_plan_list_is_the_allow_list_s_verbs_declared_reads()
    {
        var allowed = AllowList();
        var reads = ReadsOfThePlan.ToHashSet(StringComparer.Ordinal);
        var changes = ChangesTheAgentMakes.ToHashSet(StringComparer.Ordinal);

        Assert.Empty(reads.Intersect(changes).ToList());
        Assert.Empty(allowed.Where(verb => !reads.Contains(verb) && !changes.Contains(verb)).ToList());
        Assert.Empty(reads.Concat(changes).Where(verb => !allowed.Contains(verb)).ToList());

        var text = PlanListHeader + string.Concat(allowed.Where(reads.Contains).Select(verb => verb + "\n"));
        var file = Path.Combine(RepositoryRoot(), "desktop", "chat-plan-list.txt");
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            File.WriteAllText(file, text);
            return;
        }

        Assert.True(File.Exists(file), $"desktop/chat-plan-list.txt is not there. Rerun this test with {UpdateVariable}=1 and commit the file.");
        Assert.Equal(text, File.ReadAllText(file).Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    private static List<string> AllowList() =>
        File.ReadAllLines(Path.Combine(RepositoryRoot(), "desktop", "chat-allow-list.txt"))
            .Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('#')).ToList();

    /// <summary>
    /// Each verb asked each time is a leaf of the CLI's command tree, as the allow list's walk reads it: one that became a
    /// group, or was renamed, would take nothing out of the allow list, and its verbs would run without asking.
    /// </summary>
    [Fact]
    public void Each_verb_asked_each_time_is_a_leaf_of_the_cli_s_command_tree()
    {
        using var paths = new TempPaths();
        var root = CliApp.BuildRootCommand(new CliEnvironment(TextWriter.Null, TextWriter.Null, paths.Paths));
        var verbs = Verbs(root, []).ToHashSet(StringComparer.Ordinal);

        Assert.Empty(AskedEachTime.Where(verb => !verbs.Contains(verb)).ToList());
    }

    /// <summary>
    /// Every leaf of the command tree but a hidden command and what is under it: a hidden verb is on no list, so it goes to
    /// the person's card, the safe side.
    /// </summary>
    private static IEnumerable<string> Verbs(Command command, string[] words)
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

    private const string DenyListHeader =
        "# The jason commands the desktop's chat never runs, one per line.\n" +
        "#\n" +
        "# The listed commands are refused by the host's tool fence, and a command that names a listed operation is refused by\n" +
        "# the desktop. A person runs them, in the page or in a terminal. An operation is told from a command by its dot.\n" +
        "#\n" +
        "# The command lines below are written from ConfinedVerbs.Table by DesktopDenyListTests, the table the CLI refuses under\n" +
        "# JASON_CONFINED; a line taken out or added here by hand is red there until JASON_UPDATE_FIXTURES=1 writes the file\n" +
        "# again. The operation lines are checked against the Runtime API and the verbs, and are edited by hand.\n";

    private static readonly Dictionary<ConfinedVerbs.Kind, string> DenyListGroups = new()
    {
        [ConfinedVerbs.Kind.PersonsWord] =
            "#\n# The verbs that are a person's own word: an approval approved or rejected, a standing approval, a decision answered, a\n" +
            "# preference, a campaign's send mode, a block cleared, work resumed, a send reviewed, a report and a subscription\n" +
            "# rewound. The CLI gives them no default actor, and gives none to four verbs further down either.\n",
        [ConfinedVerbs.Kind.Installation] =
            "#\n# The verbs that change the installation, follow it without end, or carry it off the machine:\n",
        [ConfinedVerbs.Kind.Acts] =
            "#\n# The verbs the CLI gives no default actor that change who is reached or what runs: taking someone off the\n" +
            "# do-not-contact register, changing which plugin performs an operation, and changing which profile a role runs on.\n",
        [ConfinedVerbs.Kind.ChatsOwn] =
            "#\n# The verbs the chat refuses beyond the CLI's own table: adding a role, whose entry command the dispatcher starts when\n" +
            "# work names the role, and linking a project's folder, which then becomes a working folder of every chat on the\n" +
            "# project's campaigns.\n",
    };

    /// <summary>
    /// The deny list's command half is the CLI's table of refused commands, written from it: the file holds exactly the
    /// table's commands in its order, and its operation lines are the ones the file already holds.
    /// </summary>
    [Fact]
    public void The_deny_list_s_command_half_is_written_from_the_confined_verbs()
    {
        var text = DenyListHeader;
        foreach (var group in ConfinedVerbs.Table.GroupBy(entry => entry.Kind))
        {
            text += DenyListGroups[group.Key] + string.Concat(group.Select(entry => entry.Command + "\n"));
        }

        text += "#\n# The Runtime API operations the verbs above call, so that a command naming one is refused by the desktop:\n" +
            string.Concat(DeniedOperations().Select(operation => operation + "\n"));
        var file = Path.Combine(RepositoryRoot(), "desktop", "chat-deny-list.txt");
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            File.WriteAllText(file, text);
            return;
        }

        Assert.Equal(text, File.ReadAllText(file).Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [Fact]
    public void The_allow_list_names_no_verb_that_takes_no_default_actor_and_not_doctor()
    {
        var file = Path.Combine(RepositoryRoot(), "desktop", "chat-allow-list.txt");
        var allowed = File.ReadAllLines(file).Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('#')).ToList();

        Assert.NotEmpty(allowed);
        Assert.DoesNotContain("doctor", allowed);
        Assert.Empty(allowed.Where(verb => DefaultActorTests.NoDefaultActor.Select(DefaultActorTests.VerbOf).Contains(verb)).ToList());
    }

    [Fact]
    public void The_deny_list_holds_every_verb_that_takes_no_default_actor()
    {
        var listed = DenyList();

        var missing = DefaultActorTests.NoDefaultActor.Select(DefaultActorTests.VerbOf).Where(verb => !listed.Contains(verb)).ToList();

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

    /// <summary>
    /// The desktop spells the confining variable once, in its permission model, and sets it in every host's environment
    /// by that declaration; it is the CLI's own name, so the jason the chat runs is the jason that reads it.
    /// </summary>
    [Fact]
    public void The_desktop_s_confining_variable_is_the_cli_s()
    {
        var model = File.ReadAllText(Path.Combine(RepositoryRoot(), "desktop", "src", "chat-permission-model.ts"));

        var declared = Regex.Matches(model, @"^export const confinedVariable = '(?<name>[^']*)';\r?$", RegexOptions.Multiline);

        Assert.Equal(Confinement.Variable, Assert.Single(declared).Groups["name"].Value);
    }

    /// <summary>
    /// The files a host in <see cref="AgentHosts.All"/> reads in the folder it runs in that the table of hosts has no
    /// column for: Cursor's rules file and Copilot's instruction file.
    /// </summary>
    private static readonly string[] HostFilesTheTableDoesNotName = [".cursorrules", ".github/copilot-instructions.md"];

    /// <summary>
    /// The files Claude Code reads the MCP servers it starts from: a project's <c>.mcp.json</c> at the folder it runs in,
    /// and the user's and local scopes in <c>.claude.json</c>. A write of either could add a server under a name the
    /// person chose.
    /// </summary>
    private static readonly string[] ServerFiles = [".mcp.json", ".claude.json"];

    /// <summary>
    /// The desktop's permission model guards, as one declaration, the names whose files act on what runs next: a write
    /// under one is a card each time. It holds the names the runtime and the hosts use, read here from their own tables:
    /// the folder a marker lives in, the person's memory file, every instruction file a harness folder holds beside it, the
    /// first folder of every place a host reads a work directory's skills from, the first folder of every host's own
    /// settings, the files the table has no column for, and the files Claude Code reads its MCP servers from. A host or a
    /// file added to those tables is red here until the model guards it too.
    /// </summary>
    [Fact]
    public void The_desktop_s_guarded_names_hold_the_runtime_s_and_the_hosts_own_names()
    {
        var model = File.ReadAllText(Path.Combine(RepositoryRoot(), "desktop", "src", "chat-permission-model.ts"));
        var declaration = Assert.Single(Regex.Matches(
            model,
            @"^export const guardedNames: [^=\r\n]+= \[\r?$(?<body>.*?)^\];\r?$",
            RegexOptions.Multiline | RegexOptions.Singleline));
        var guarded = Regex.Matches(declaration.Groups["body"].Value, @"name: '(?<name>[^']*)'")
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

        string[] expected =
        [
            HarnessMarker.Directory,
            AgentHosts.MemoryFile,
            .. AgentHosts.ImportFiles.Select(file => file.FileName),
            .. AgentHosts.All.Select(facts => facts.WorkSkills.Split('/')[0]),
            .. AgentHosts.All.Select(facts => facts.HomeMarker.Split('/')[0]),
            .. HostFilesTheTableDoesNotName,
            .. ServerFiles,
        ];
        var missing = expected.Distinct(StringComparer.Ordinal).Where(name => !guarded.Contains(name)).Order(StringComparer.Ordinal).ToList();

        Assert.True(guarded.Count > 0, "The model's guardedNames declaration names nothing.");
        Assert.True(missing.Count == 0, $"The desktop's guarded names lack: {string.Join(", ", missing)}");
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

        foreach (var args in DefaultActorTests.NoDefaultActor)
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

    private static List<string> DeniedOperations() =>
        [.. Lines().Where(line => line.Contains('.'))];

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
