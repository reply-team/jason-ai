using System.CommandLine;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Cli;
using Jason.Cli.Tests.Commands;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;

namespace Jason.Cli.Tests;

/// <summary>
/// A write given no <c>--actor</c> is made as <c>human:&lt;the account's name&gt;</c>, the name the paired browser writes
/// as, so one person carries one name in a timeline whichever of the two they used. The verbs that take no default actor
/// are refused, in one sentence that names the form, unless an actor is named or the environment names an attempt: a
/// person's own word, and the acts the CLI will not put the account's name on by itself.
/// </summary>
public class DefaultActorTests
{
    private const string Account = "quillon";

    [Fact]
    public async Task A_write_without_an_actor_is_made_as_the_account_s_person()
    {
        using var cli = new CliRun(accountName: Account);

        var exit = await cli.RunAsync("campaign", "create", "--name", "Spring outreach");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.CampaignCreate, "{\"name\":\"Spring outreach\",\"actor\":{\"type\":\"human\",\"id\":\"quillon\"}}");
    }

    [Theory]
    [InlineData("human:ada", "{\"type\":\"human\",\"id\":\"ada\"}")]
    [InlineData("human", "{\"type\":\"human\"}")]
    [InlineData("role:planner", "{\"type\":\"role\",\"id\":\"planner\"}")]
    public async Task An_actor_given_overrides_the_default(string given, string sent)
    {
        using var cli = new CliRun(accountName: Account);

        var exit = await cli.RunAsync("--actor", given, "journal", "append", "cmp_A", "--kind", "observation");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.JournalAppend, $"{{\"campaign_id\":\"cmp_A\",\"kind\":\"observation\",\"actor\":{sent}}}");
    }

    /// <summary>An actor the request's own file names is the one given, as it was before there was a default.</summary>
    [Fact]
    public async Task An_actor_the_file_names_is_kept()
    {
        using var paths = new TempPaths();
        Directory.CreateDirectory(paths.Paths.Root);
        var file = Path.Combine(paths.Paths.Root, "item.json");
        File.WriteAllText(file, "{\"kind\":\"ai_role\",\"role\":\"researcher\",\"actor\":{\"type\":\"role\",\"id\":\"planner\"}}");
        using var cli = new CliRun(accountName: Account);

        var exit = await cli.RunAsync("workitem", "create", "cmp_A", "--file", file);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("{\"type\":\"role\",\"id\":\"planner\"}", JsonNode.Parse(cli.Sent!)!["actor"]!.ToJsonString());
    }

    /// <summary>Where the account's name cannot be read, a write is what it was before: a person who gave no name.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Without_the_account_s_name_a_write_carries_no_actor(string? account)
    {
        using var cli = new CliRun(accountName: account);

        var exit = await cli.RunAsync("campaign", "create", "--name", "Spring outreach");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.CampaignCreate, "{\"name\":\"Spring outreach\"}");
    }

    /// <summary>A launched executor is its attempt whether or not it says so; the account's person is the default after that.</summary>
    [Fact]
    public void The_attempt_in_the_environment_comes_before_the_account_s_person()
    {
        Assert.Equal("{\"type\":\"attempt\",\"id\":\"att_01J\"}", ActorOption.Default(ActorOption.Parse(null, "att_01J"), Account)!.ToJsonString());
        Assert.Equal("{\"type\":\"human\",\"id\":\"quillon\"}", ActorOption.Default(ActorOption.Parse(null, null), Account)!.ToJsonString());
        Assert.Null(ActorOption.Default(ActorOption.Parse(null, null), null));
    }

    [Fact]
    public void A_name_the_platform_cannot_give_is_no_name()
    {
        Assert.Null(CliEnvironment.AccountNameOf(() => throw new InvalidOperationException("No account here.")));
        Assert.Null(CliEnvironment.AccountNameOf(() => "   "));
        Assert.Equal(Account, CliEnvironment.AccountNameOf(() => Account));
    }

    /// <summary>
    /// A verb that takes no default actor, with none named, is refused before anything is sent, in one sentence that names
    /// the form: a resume naming whoever ends the wait, a report its reporter, an act the CLI will not put the account's
    /// name on whoever does it.
    /// </summary>
    [Theory]
    [MemberData(nameof(TakingNoDefault))]
    public async Task A_verb_that_takes_no_default_actor_is_refused_without_a_named_actor_in_one_sentence(string[] verb)
    {
        using var cli = new CliRun(accountName: Account);

        var exit = await cli.RunAsync(verb);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Equal(0, cli.Requests);
        var (_, form) = Expected(verb);
        var line = Assert.Single(cli.Error.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries), text => !text.StartsWith("Run 'jason", StringComparison.Ordinal));
        Assert.EndsWith($"the CLI's default actor does not apply to it; {form}.", line, StringComparison.Ordinal);
        Assert.Single(line.Replace("e.g. ", "e.g.", StringComparison.Ordinal).Split(". ", StringSplitOptions.None));
    }

    [Theory]
    [MemberData(nameof(TakingNoDefault))]
    public async Task A_verb_that_takes_no_default_actor_is_sent_as_the_actor_named(string[] verb)
    {
        using var cli = new CliRun(accountName: Account);

        var exit = await cli.RunAsync(["--actor", "human:ada", .. verb]);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("{\"type\":\"human\",\"id\":\"ada\"}", JsonNode.Parse(cli.Sent!)!["actor"]!.ToJsonString());
    }

    /// <summary>
    /// Inside a run nothing changes: the attempt the environment names is the actor, as it is for a resume, and the
    /// request is sent. The runtime, not the CLI, decides what an attempt may do.
    /// </summary>
    [Theory]
    [MemberData(nameof(TakingNoDefault))]
    public async Task Inside_a_run_a_verb_that_takes_no_default_actor_is_sent_as_its_attempt(string[] verb)
    {
        var run = await RunEachAsync(verb, new Dictionary<string, string> { ["JASON_ATTEMPT_ID"] = "att_01K7QN0000000000000000000R" });

        Assert.True(run.Exit == ExitCodes.Success, $"'jason {VerbOf(verb)}' exited {run.Exit}, saying:{Environment.NewLine}{run.Error}");
        var (_, body) = Assert.Single(run.Requests);
        Assert.Equal("{\"type\":\"attempt\",\"id\":\"att_01K7QN0000000000000000000R\"}", JsonNode.Parse(body)!["actor"]!.ToJsonString());
    }

    /// <summary>The help of each of those verbs says the default does not apply to it, and how to name who acts.</summary>
    [Theory]
    [MemberData(nameof(TakingNoDefault))]
    public void A_verb_that_takes_no_default_actor_says_in_its_help_that_the_default_does_not_apply(string[] verb)
    {
        var command = Find(verb);
        var (help, _) = Expected(verb);

        Assert.Contains(help, command.Description, StringComparison.Ordinal);
    }

    /// <summary>
    /// The acts the runtime takes from any caller say what is true of them: the CLI will not put the account's name on them
    /// by itself. Their help and their refusal never say that the runtime refuses them, or that only a person can.
    /// </summary>
    [Theory]
    [MemberData(nameof(TheRuntimeTakesFromAnyone))]
    public async Task An_act_the_runtime_takes_from_anyone_never_says_the_runtime_refuses_it(string[] verb)
    {
        using var cli = new CliRun(accountName: Account);
        var exit = await cli.RunAsync(verb);

        Assert.Equal(ExitCodes.Usage, exit);
        var said = Find(verb).Description + Environment.NewLine + cli.Error;
        Assert.Contains("will not put this account's name on", said, StringComparison.Ordinal);
        foreach (var claim in new[] { "runtime refuses", "person's word", "person's own word", "only a person" })
        {
            Assert.DoesNotContain(claim, said, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>A report's author is never taken from the machine: a role may report, so the refusal names both forms.</summary>
    [Fact]
    public async Task A_report_names_its_reporter_as_a_person_or_a_role()
    {
        using var cli = new CliRun(accountName: Account);

        var exit = await cli.RunAsync("--actor", "role:researcher", "report", "submit", "--effect", "email_sent", "--tool", "t", "--summary", "s");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("{\"type\":\"role\",\"id\":\"researcher\"}", JsonNode.Parse(cli.Sent!)!["actor"]!.ToJsonString());
    }

    /// <summary>And the help of <c>--actor</c> says what the default is, and what it is where the name cannot be read.</summary>
    [Fact]
    public void The_help_of_actor_says_the_default()
    {
        var description = ActorOption.Create().Description;

        Assert.Contains("human:<this account's name>", description, StringComparison.Ordinal);
        Assert.Contains("the paired browser", description, StringComparison.Ordinal);
        Assert.Contains("where that name cannot be read, as a person who gave no name", description, StringComparison.Ordinal);
        Assert.Contains("rewinding a subscription", description, StringComparison.Ordinal);
        Assert.Contains("lifting a suppression, setting or unsetting a route, and setting a role's execution profile take no default.", description, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every verb that reaches the runtime, run with the account's name set against a runtime that answers <c>{}</c>: each
    /// request whose type takes an actor carries the account's person, and the verbs that take no default actor send
    /// nothing at all. Which types take one is read from the runtime's own registration of its operations and from the
    /// contracts, so a write that left the default out is a difference here however its body was built; and every
    /// operation that takes an actor is reached by some verb, so none is left out of the reading.
    /// </summary>
    [Fact]
    public async Task Every_write_a_verb_makes_carries_the_account_s_person_but_those_that_take_no_default()
    {
        var takesAnActor = OperationsTakingAnActor();
        var reached = new HashSet<string>(StringComparer.Ordinal);

        foreach (var args in Runs)
        {
            var run = await RunEachAsync(args);
            Assert.True(run.Requests.Count > 0, $"'jason {VerbOf(args)}' sent nothing: exit {run.Exit}, saying:{Environment.NewLine}{run.Error}");
            foreach (var (operation, body) in run.Requests.Where(request => takesAnActor.Contains(request.Operation)))
            {
                reached.Add(operation);
                Assert.True(
                    JsonNode.DeepEquals(JsonNode.Parse("{\"type\":\"human\",\"id\":\"quillon\"}"), JsonNode.Parse(body)!["actor"]),
                    $"'jason {VerbOf(args)}' sent {operation} as {body}");
            }
        }

        foreach (var args in NoDefaultActor)
        {
            var refused = await RunEachAsync(args);
            Assert.True(refused.Exit == ExitCodes.Usage && refused.Requests.Count == 0, $"'jason {VerbOf(args)}' was not refused: exit {refused.Exit}, {refused.Requests.Count} request(s)");

            var named = await RunEachAsync(["--actor", "human:ada", .. args]);
            reached.UnionWith(named.Requests.Select(request => request.Operation).Where(takesAnActor.Contains));
        }

        Assert.Equal(takesAnActor.Order(StringComparer.Ordinal), reached.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// A request whose type takes no actor — every read, and the calls a running executor makes — carries none with the
    /// account's name set: the default is a writer's name, never a reader's.
    /// </summary>
    [Fact]
    public async Task A_request_whose_type_takes_no_actor_is_sent_without_one()
    {
        var takesAnActor = OperationsTakingAnActor();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var args in Runs)
        {
            var run = await RunEachAsync(args);
            foreach (var (operation, body) in run.Requests.Where(request => !takesAnActor.Contains(request.Operation)))
            {
                seen.Add(operation);
                Assert.True(JsonNode.Parse(body) is not JsonObject sent || sent["actor"] is null, $"'jason {VerbOf(args)}' sent {operation} as {body}");
            }
        }

        Assert.Contains(Operations.CampaignList, seen);
        Assert.Contains(Operations.WorkItemComplete, seen);
    }

    /// <summary>
    /// The verbs above are every verb: each is run there, takes no default actor, or says here why it is not run, so a verb
    /// added later is a difference in this row before it is a write nobody ran.
    /// </summary>
    [Fact]
    public void Every_verb_is_run_or_says_why_not()
    {
        using var paths = new TempPaths();
        var leaves = new List<string>();
        Walk(CliApp.BuildRootCommand(new CliEnvironment(TextWriter.Null, TextWriter.Null, paths.Paths)), string.Empty);

        Assert.Equal(
            leaves.Order(StringComparer.Ordinal),
            Runs.Concat(NoDefaultActor).Select(VerbOf).Concat(NotRun.Keys).Order(StringComparer.Ordinal));

        void Walk(Command command, string path)
        {
            if (command.Subcommands.Count == 0)
            {
                leaves.Add(path);
            }

            foreach (var sub in command.Subcommands)
            {
                Walk(sub, $"{path} {sub.Name}".Trim());
            }
        }
    }

    public static TheoryData<string[]> TakingNoDefault() => [.. NoDefaultActor];

    public static TheoryData<string[]> TheRuntimeTakesFromAnyone() =>
        [.. NoDefaultActor.Where(args => RuntimeTakesFromAnyone.Contains(VerbOf(args)))];

    /// <summary>
    /// The verbs no default actor applies to: a person's own word, which the runtime refuses too when nobody is named, and
    /// the acts the runtime takes from any caller but the CLI will not put the account's name on by itself.
    /// </summary>
    internal static readonly string[][] NoDefaultActor =
    [
        ["approval", "approve", "apr_A"],
        ["approval", "reject", "apr_A"],
        ["approval", "set-standing", "cmp_A", "--category", "interested"],
        ["approval", "unset-standing", "cmp_A", "--category", "interested"],
        ["decision", "answer", "dec_A", "--answer", "Yes, go ahead."],
        ["preference", "set", "outreach.tone", "--value", "{\"tone\":\"plain\"}"],
        ["preference", "unset", "outreach.tone"],
        ["campaign", "set-send-mode", "cmp_A", "--cold", "automatic"],
        ["block", "clear", "blk_A"],
        ["workitem", "resume", "wi_A"],
        ["workitem", "review", "wi_A", "--review-hash", "sha256:00", "--result", "passed"],
        ["report", "submit", "--effect", "email_sent", "--tool", "t", "--summary", "s"],
        ["subscription", "rewind", "--kind", "campaign_created", "--since", "2026-10-12T00:00:00Z"],
        ["suppression", "remove", "--channel", "email", "--value", "someone@example.com", "--reason", "asked again"],
        ["route", "set", "--campaign", "cmp_A", "--plugin", "fake-provider", "--operation", "campaign.get"],
        ["route", "unset", "--campaign", "cmp_A", "--operation", "campaign.get"],
        ["role", "set-profile", "researcher", "--execution-profile", "local-claude"],
    ];

    /// <summary>
    /// Of those, the acts the runtime takes from any caller: lifting someone's opt-out, moving which account acts for a
    /// campaign, and changing which agent host runs a role. The CLI alone asks who does them.
    /// </summary>
    internal static readonly string[] RuntimeTakesFromAnyone = ["suppression remove", "route set", "route unset", "role set-profile"];

    /// <summary>One invocation of every other verb that reaches the runtime.</summary>
    internal static readonly string[][] Runs =
    [
        ["runtime", "status"],
        ["campaign", "create", "--name", "Spring outreach"],
        ["campaign", "get", "cmp_A"],
        ["campaign", "list"],
        ["campaign", "update", "cmp_A", "--name", "Autumn outreach"],
        ["campaign", "start", "cmp_A"],
        ["campaign", "pause", "cmp_A"],
        ["campaign", "archive", "cmp_A"],
        ["campaign", "update-context", "cmp_A", "--unset", "old_pitch"],
        ["campaign", "add-contacts", "cmp_A", "--file", "-"],
        ["campaign", "remove-contacts", "cmp_A", "--contact", "cnt_A"],
        ["campaign", "list-contacts", "cmp_A"],
        ["campaign", "summary", "cmp_A"],
        ["campaign", "activity", "cmp_A", "--since", "2026-10-12T00:00:00Z", "--until", "2026-10-13T00:00:00Z"],
        ["campaign", "activity-by-period", "cmp_A", "--count", "replied", "--period", "day", "--since", "2026-10-12T00:00:00Z", "--until", "2026-10-13T00:00:00Z"],
        ["campaign", "audience", "cmp_A"],
        ["project", "create", "--name", "Northwind"],
        ["project", "get", "prj_A"],
        ["project", "list"],
        ["project", "update", "prj_A", "--name", "Southwind"],
        ["project", "link", "prj_A", "--path", AppContext.BaseDirectory],
        ["preference", "list"],
        ["contact", "create", "--first-name", "Ada"],
        ["contact", "get", "cnt_A"],
        ["contact", "list"],
        ["contact", "update", "cnt_A", "--first-name", "Ada"],
        ["contact", "archive", "cnt_A"],
        ["contact", "threads", "cnt_A"],
        ["contact", "thread", "cnt_A", "thr_A"],
        ["workitem", "create", "cmp_A", "--kind", "ai_role", "--role", "researcher"],
        ["workitem", "get", "wi_A"],
        ["workitem", "list"],
        ["workitem", "count"],
        ["workitem", "update", "wi_A", "--priority", "5"],
        ["workitem", "cancel", "wi_A"],
        ["workitem", "heartbeat", "wi_A", "--attempt", "att_A"],
        ["workitem", "set-result", "wi_A", "--attempt", "att_A", "--result", "{}"],
        ["workitem", "complete", "wi_A", "--attempt", "att_A", "--status", "succeeded"],
        ["workitem", "wait", "wi_A", "--attempt", "att_A", "--until", "2026-10-13T00:00:00Z"],
        ["role", "list"],
        ["role", "add", "researcher", "--execution-profile", "local-claude"],
        ["rolenote", "get", "--role", "researcher", "--campaign", "cmp_A"],
        ["rolenote", "set", "--role", "researcher", "--campaign", "cmp_A", "--note", "{\"voice\":\"short sentences\"}"],
        ["rolenote", "list"],
        ["profile", "create", "local-codex", "--host", "codex", "--program", "codex"],
        ["profile", "update", "local-codex", "--description", "The laptop's agent."],
        ["profile", "get", "local-codex"],
        ["profile", "list"],
        ["profile", "disable", "local-codex"],
        ["profile", "enable", "local-codex"],
        ["approval", "list"],
        ["approval", "get", "apr_A"],
        ["approval", "list-standing", "cmp_A"],
        ["block", "list"],
        ["block", "get", "blk_A"],
        ["decision", "list"],
        ["decision", "get", "dec_A"],
        ["decision", "raise", "wi_A", "--attempt", "att_A", "--question", "Which tone?"],
        ["reply", "record", "cmp_A", "--plugin", "mailbox", "--thread", "thr_A", "--message", "msg_A", "--message-at", "2026-10-12T00:00:00Z", "--category", "interested", "--confidence", "0.9", "--reaction", "draft"],
        ["reply", "count", "cmp_A", "--since", "2026-10-12T00:00:00Z", "--until", "2026-10-13T00:00:00Z"],
        ["report", "get", "rpt_A"],
        ["report", "list"],
        ["journal", "append", "cmp_A", "--kind", "observation"],
        ["journal", "list"],
        ["journal", "kinds"],
        ["suppression", "add", "--channel", "email", "--value", "someone@example.com"],
        ["suppression", "list"],
        ["plugin", "list"],
        ["plugin", "reload"],
        ["route", "resolve", "--campaign", "cmp_A", "--operation", "campaign.get"],
        ["operation", "describe", "audience.search", "--campaign", "cmp_A"],
        ["audience", "preview", "--campaign", "cmp_A", "--definition", "{\"industries\":[\"Software\"]}"],
        ["audience", "filter-values", "--campaign", "cmp_A", "--filter", "industries"],
        ["audience", "availability", "--campaign", "cmp_A"],
        ["sender", "list", "--campaign", "cmp_A", "--owners", "own"],
        ["route", "list"],
        ["events", "sources"],
        ["events", "backfill", "--source", "evs_A", "--from", "2026-10-12T00:00:00Z", "--to", "2026-10-13T00:00:00Z"],
        ["events", "signal", "--source", "evs_A"],
        ["subscription", "list"],
        ["subscription", "runs", "--kind", "campaign_created"],
        ["ui", "open", "--no-open"],
        ["ui", "tokens"],
        ["ui", "revoke", "uit_A"],
    ];

    /// <summary>The verbs not run above, each with why.</summary>
    internal static readonly Dictionary<string, string> NotRun = new(StringComparer.Ordinal)
    {
        ["runtime start"] = "starts the runtime's process",
        ["runtime restart"] = "starts the runtime's process",
        ["runtime stop"] = "stops the runtime's process, by an operation that takes no actor",
        ["runtime autostart enable"] = "changes this machine's logon registration and asks the runtime nothing",
        ["runtime autostart disable"] = "changes this machine's logon registration and asks the runtime nothing",
        ["runtime autostart status"] = "reads this machine's logon registration and asks the runtime nothing",
        ["plugin install"] = "reads a release before it asks for a reload; PluginInstallCommandTests holds the reload's actor",
        ["ui install"] = "places a release's build and asks the runtime nothing",
        ["journal watch"] = "holds the journal's stream open until interrupted, only reads and sends no actor; JournalWatchTests holds what it prints",
        ["export"] = "writes this installation's files into an archive and asks the runtime only to stop and to start",
        ["import"] = "places an archive's files and asks the runtime to start and project.link per project folder, with the actor set as every verb sets it; ImportCommandTests holds the link's body",
        ["session open"] = "asks the runtime only reads and starts a terminal; SessionOpenerTests holds what it asks",
        ["update check"] = "reads a release and asks the runtime nothing",
        ["update apply"] = "replaces the executable",
        ["update rollback"] = "replaces the executable",
        ["update status"] = "reads this installation's files",
        ["skills install"] = "deploys skills and asks the runtime nothing",
        ["skills update"] = "deploys skills and asks the runtime nothing",
        ["status"] = "asks the runtime only reads",
        ["logs"] = "reads the log files and never asks the runtime",
        ["doctor"] = "asks the runtime only reads",
        ["uninstall"] = "takes this installation off the machine",
    };

    /// <summary>The one operation the runtime maps by hand, with no body to bind: it reads, and takes no actor.</summary>
    private static readonly string[] Unbound = [Operations.SystemInfo];

    private static readonly RuntimeDescriptor Live = new("v1", "0.1.0-dev", "rt_LIVE", 77, "http://127.0.0.1:5000", "the-token", DateTimeOffset.UnixEpoch);

    /// <summary>
    /// The operations whose request type has an <c>actor</c>: the type each one binds, read from the runtime's own
    /// <c>MapOperation&lt;TService, TRequest, TResponse&gt;(Operations.X, …)</c> lines, and the property read from the contracts.
    /// </summary>
    private static HashSet<string> OperationsTakingAnActor()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        var modules = Directory.EnumerateFiles(Path.Combine(root.FullName, "dotnet", "src", "Jason.Runtime", "Hosting", "Modules"), "*.cs")
            .Select(File.ReadAllText);
        var bindings = modules
            .SelectMany(text => Regex.Matches(text, @"MapOperation<\w+,\s*(?<request>\w+),.*?\(\s*Operations\.(?<operation>\w+)", RegexOptions.Singleline))
            .Select(match => (Request: match.Groups["request"].Value, Operation: (string)typeof(Operations).GetField(match.Groups["operation"].Value)!.GetRawConstantValue()!))
            .ToList();
        var contracts = typeof(Operations).Assembly.GetTypes().ToLookup(type => type.Name, StringComparer.Ordinal);

        // Every operation is bound once, so the reading stopping short would show as a smaller count, not as a pass.
        Assert.Equal(
            typeof(Operations).GetFields().Count(field => field.IsLiteral) - Unbound.Length,
            bindings.Select(binding => binding.Operation).Distinct(StringComparer.Ordinal).Count());

        return bindings
            .Where(binding => binding.Request != nameof(JsonObject))
            .Where(binding => Assert.Single(contracts[binding.Request]).GetProperty("Actor")?.PropertyType == typeof(ActorRef))
            .Select(binding => binding.Operation)
            .ToHashSet(StringComparer.Ordinal);
    }

    internal static async Task<(int Exit, string Error, List<(string Operation, string Body)> Requests)> RunEachAsync(string[] args, IReadOnlyDictionary<string, string>? variables = null)
    {
        using var paths = new TempPaths();
        paths.WriteDescriptor(Live);
        var requests = new List<(string Operation, string Body)>();
        var error = new StringWriter();
        var environment = new CliEnvironment(
            new StringWriter(),
            error,
            paths.Paths,
            new FakeHandler(request =>
            {
                requests.Add((request.RequestUri!.AbsolutePath["/v1/".Length..], request.Content?.ReadAsStringAsync(CancellationToken.None).GetAwaiter().GetResult() ?? "{}"));
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            }),
            new StringReader("[{\"first_name\":\"Ada\"}]"),
            Variables: variables ?? new Dictionary<string, string>(),
            AccountName: Account);

        var exit = await CliApp.RunAsync(args, environment, TestContext.Current.CancellationToken);
        return (exit, error.ToString(), requests);
    }

    /// <summary>The verb an invocation names: the words the command tree reads as commands, before its first argument or option.</summary>
    internal static string VerbOf(string[] args) => string.Join(' ', Walked(args).Words);

    private static Command Find(string[] verb) => Walked(verb).Command;

    private static (Command Command, List<string> Words) Walked(string[] args)
    {
        using var paths = new TempPaths();
        Command command = CliApp.BuildRootCommand(new CliEnvironment(TextWriter.Null, TextWriter.Null, paths.Paths));
        var words = new List<string>();
        foreach (var word in args)
        {
            if (command.Subcommands.FirstOrDefault(sub => sub.Name == word) is not { } next)
            {
                break;
            }

            command = next;
            words.Add(word);
        }

        return (command, words);
    }

    private static (string Help, string Form) Expected(string[] verb) => VerbOf(verb) switch
    {
        "workitem resume" => (ActorOption.NoDefaultForResume, "name who ends it, e.g. --actor human:<Name>"),
        "suppression remove" or "route set" or "route unset" or "role set-profile" => (ActorOption.NoDefaultForAnAct, "name who does it, e.g. --actor human:<Name>"),
        "report submit" => (ActorOption.NoDefaultForReport, "write --actor human:<Name> or --actor role:<id>"),
        _ => (ActorOption.NoDefault, "write --actor human:<Name>"),
    };
}
