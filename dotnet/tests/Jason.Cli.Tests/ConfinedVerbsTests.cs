using System.CommandLine;

namespace Jason.Cli.Tests;

/// <summary>
/// Under <c>JASON_CONFINED</c> the CLI refuses, locally and before any request, every command of
/// <see cref="ConfinedVerbs"/>: a person's word, or a change of this installation. Unset or empty, nothing changes. A role
/// attempt never carries the variable, so no entry stands down for an attempt id in the environment. A higher bar, not a boundary. The terminal <c>session open</c> starts carries no variable
/// (<c>TerminalPlanTests</c> holds that).
/// </summary>
public class ConfinedVerbsTests
{
    private const string Variable = "JASON_CONFINED";
    private const string Attempt = "att_01K7QN0000000000000000000R";

    /// <summary>One invocation that parses, for each entry of the table (a noun entry is given one of its verbs).</summary>
    internal static readonly Dictionary<string, string[]> Samples = new(StringComparer.Ordinal)
    {
        ["approval approve"] = ["approval", "approve", "apr_A"],
        ["approval reject"] = ["approval", "reject", "apr_A"],
        ["approval set-standing"] = ["approval", "set-standing", "cmp_A", "--category", "interested"],
        ["approval unset-standing"] = ["approval", "unset-standing", "cmp_A", "--category", "interested"],
        ["decision answer"] = ["decision", "answer", "dec_A", "--answer", "Yes, go ahead."],
        ["preference set"] = ["preference", "set", "outreach.tone", "--value", "{\"tone\":\"plain\"}"],
        ["preference unset"] = ["preference", "unset", "outreach.tone"],
        ["campaign set-send-mode"] = ["campaign", "set-send-mode", "cmp_A", "--cold", "automatic"],
        ["block clear"] = ["block", "clear", "blk_A"],
        ["workitem resume"] = ["workitem", "resume", "wi_A"],
        ["launches lift"] = ["launches", "lift"],
        ["workitem review"] = ["workitem", "review", "wi_A", "--review-hash", "sha256:00", "--result", "passed"],
        ["report submit"] = ["report", "submit", "--effect", "email_sent", "--tool", "t", "--summary", "s"],
        ["subscription rewind"] = ["subscription", "rewind", "--kind", "campaign_created", "--since", "2026-10-12T00:00:00Z"],
        ["runtime"] = ["runtime", "status"],
        ["plugin install"] = ["plugin", "install", "fake-provider"],
        ["plugin reload"] = ["plugin", "reload"],
        ["profile"] = ["profile", "list"],
        ["skills"] = ["skills", "update", "--dry-run"],
        ["update"] = ["update", "check"],
        ["uninstall"] = ["uninstall"],
        ["install"] = ["install"],
        ["ui"] = ["ui", "tokens"],
        ["session"] = ["session", "open", "--dry-run"],
        ["journal watch"] = ["journal", "watch"],
        ["export"] = ["export"],
        ["import"] = ["import", "archive.zip"],
        ["suppression remove"] = ["suppression", "remove", "--channel", "email", "--value", "someone@example.com", "--reason", "asked again"],
        ["route set"] = ["route", "set", "--campaign", "cmp_A", "--plugin", "fake-provider", "--operation", "campaign.get"],
        ["route unset"] = ["route", "unset", "--campaign", "cmp_A", "--operation", "campaign.get"],
        ["plugin grant"] = ["plugin", "grant", "fake-provider", "--http", "api.example.com"],
        ["plugin revoke"] = ["plugin", "revoke", "fake-provider", "--env", "FAKE_TOKEN"],
        ["role set-profile"] = ["role", "set-profile", "researcher", "--execution-profile", "local-claude"],
        ["notification enable"] = ["notification", "enable", "toast"],
        ["notification disable"] = ["notification", "disable", "toast"],
        ["role add"] = ["role", "add", "researcher", "--execution-profile", "local-claude"],
        ["project link"] = ["project", "link", "prj_A", "--path", "."],
        ["campaign start"] = ["campaign", "start", "cmp_A"],
        ["campaign archive"] = ["campaign", "archive", "cmp_A"],
    };

    public static TheoryData<string> Entries() => [.. ConfinedVerbs.Table.Select(entry => entry.Command)];

    /// <summary>The sample, with a person named for the verbs that take an actor and no default.</summary>
    private static string[] WithActor(string entry) =>
        DefaultActorTests.NoDefaultActor.Any(args => DefaultActorTests.VerbOf(args) == entry) || entry == "role add"
            ? ["--actor", "human:x", .. Samples[entry]]
            : Samples[entry];

    [Fact]
    public void Every_entry_has_a_sample_and_every_sample_an_entry()
    {
        Assert.Equal(
            ConfinedVerbs.Table.Select(entry => entry.Command).Order(StringComparer.Ordinal),
            Samples.Keys.Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Entries))]
    public async Task Confined_the_command_is_refused_with_the_sentence_and_nothing_is_sent(string entry)
    {
        var run = await DefaultActorTests.RunEachAsync(WithActor(entry), new Dictionary<string, string> { [Variable] = "1" });

        Assert.Equal(ExitCodes.ApiError, run.Exit);
        Assert.Empty(run.Requests);
        Assert.Equal(ConfinedVerbs.Sentence + Environment.NewLine, run.Error);
    }

    /// <summary>Unset and empty are as before: the refusal is the variable's alone, not the command's.</summary>
    [Theory]
    [MemberData(nameof(Entries))]
    public async Task Unset_or_empty_the_command_is_not_refused(string entry)
    {
        foreach (var variables in new[] { new Dictionary<string, string>(), new Dictionary<string, string> { [Variable] = string.Empty } })
        {
            var run = await DefaultActorTests.RunEachAsync(WithActor(entry), variables);
            Assert.DoesNotContain(ConfinedVerbs.Sentence, run.Error, StringComparison.Ordinal);
        }
    }

    /// <summary>The operation verbs among them still reach the runtime when the variable is unset.</summary>
    [Theory]
    [InlineData("approval approve")]
    [InlineData("decision answer")]
    [InlineData("preference set")]
    [InlineData("block clear")]
    [InlineData("launches lift")]
    [InlineData("route set")]
    [InlineData("role add")]
    [InlineData("campaign start")]
    [InlineData("campaign archive")]
    public async Task Unset_the_verb_reaches_the_runtime(string entry)
    {
        var run = await DefaultActorTests.RunEachAsync(WithActor(entry), new Dictionary<string, string>());

        Assert.Equal(ExitCodes.Success, run.Exit);
        Assert.NotEmpty(run.Requests);
    }

    /// <summary>
    /// An attempt id in the environment is a claim and buys no exception: every entry, the two a role runs included, is
    /// refused with the variable set and an attempt named. A real role attempt never carries the variable
    /// (<c>ExecutorConfinementTests</c>), so this is only a chat turn whose agent wrote an id in front.
    /// </summary>
    [Theory]
    [MemberData(nameof(Entries))]
    public async Task With_an_attempt_named_every_entry_is_still_refused(string entry)
    {
        var run = await DefaultActorTests.RunEachAsync(
            WithActor(entry), new Dictionary<string, string> { [Variable] = "1", ["JASON_ATTEMPT_ID"] = Attempt });

        Assert.Equal(ExitCodes.ApiError, run.Exit);
        Assert.Empty(run.Requests);
        Assert.Equal(ConfinedVerbs.Sentence + Environment.NewLine, run.Error);
    }

    /// <summary>With the variable unset, a role attempt's own verbs run as they did: the request is sent as its attempt.</summary>
    [Theory]
    [InlineData("report submit")]
    [InlineData("workitem resume")]
    [InlineData("workitem review")]
    public async Task A_role_attempt_s_own_verbs_run_as_before_where_the_variable_is_unset(string entry)
    {
        var run = await DefaultActorTests.RunEachAsync(Samples[entry], new Dictionary<string, string> { ["JASON_ATTEMPT_ID"] = Attempt });

        Assert.Equal(ExitCodes.Success, run.Exit);
        Assert.NotEmpty(run.Requests);
    }

    [Fact]
    public async Task Approval_approve_confined_with_an_attempt_named_is_refused()
    {
        var run = await DefaultActorTests.RunEachAsync(
            ["--actor", "human:x", "approval", "approve", "apr_A"],
            new Dictionary<string, string> { [Variable] = "1", ["JASON_ATTEMPT_ID"] = Attempt });

        Assert.Equal(ExitCodes.ApiError, run.Exit);
        Assert.Empty(run.Requests);
    }

    /// <summary>
    /// The campaign's allowance lets searches the provider charges for go without asking, so its forms are a person's word
    /// like the verbs they ride on: approving with <c>--allow</c>, and setting or taking back a standing approval for
    /// <c>audience.search</c>, are refused with the variable set, and nothing is sent.
    /// </summary>
    [Theory]
    [InlineData("--actor", "human:x", "approval", "approve", "apr_A", "--allow", "100")]
    [InlineData("--actor", "human:x", "approval", "set-standing", "cmp_A", "--operation", "audience.search", "--audience", "2", "--max-people", "250")]
    [InlineData("--actor", "human:x", "approval", "unset-standing", "cmp_A", "--operation", "audience.search")]
    public async Task Confined_the_allowance_forms_are_refused_and_nothing_is_sent(params string[] args)
    {
        var run = await DefaultActorTests.RunEachAsync(args, new Dictionary<string, string> { [Variable] = "1" });

        Assert.Equal(ExitCodes.ApiError, run.Exit);
        Assert.Empty(run.Requests);
        Assert.Equal(ConfinedVerbs.Sentence + Environment.NewLine, run.Error);
    }

    /// <summary>
    /// A global route is refused as a campaign's is: the entry is the verb, so every scope of it is covered, and the
    /// desktop's chat, which sets the variable on every turn in every mode, never re-routes every campaign.
    /// </summary>
    [Theory]
    [InlineData("--actor", "human:x", "route", "set", "--global", "--plugin", "fake-provider")]
    [InlineData("--actor", "human:x", "route", "unset", "--global", "--operation", "campaign.get")]
    public async Task Confined_a_global_route_write_is_refused_and_nothing_is_sent(params string[] args)
    {
        var run = await DefaultActorTests.RunEachAsync(args, new Dictionary<string, string> { [Variable] = "1" });

        Assert.Equal(ExitCodes.ApiError, run.Exit);
        Assert.Empty(run.Requests);
        Assert.Equal(ConfinedVerbs.Sentence + Environment.NewLine, run.Error);
    }

    /// <summary>
    /// A grant changes what a plugin may reach and run, so it is refused as a route is, by every flag: the desktop's chat,
    /// which sets the variable on every turn in every mode, never grants a plugin anything or takes a grant back.
    /// </summary>
    [Theory]
    [InlineData("--actor", "human:x", "plugin", "grant", "fake-provider", "--http", "api.example.com")]
    [InlineData("--actor", "human:x", "plugin", "grant", "fake-provider", "--exec", "node")]
    [InlineData("--actor", "human:x", "plugin", "grant", "fake-provider", "--env", "*")]
    [InlineData("--actor", "human:x", "plugin", "grant", "fake-provider", "--api", "campaign")]
    [InlineData("--actor", "human:x", "plugin", "grant", "fake-provider", "--files", "/srv/lines")]
    [InlineData("--actor", "human:x", "plugin", "grant", "fake-provider", "--subscription", "workitem_failed")]
    [InlineData("--actor", "human:x", "plugin", "revoke", "fake-provider", "--http", "api.example.com")]
    public async Task Confined_a_plugin_grant_or_revoke_is_refused_and_nothing_is_sent(params string[] args)
    {
        var run = await DefaultActorTests.RunEachAsync(args, new Dictionary<string, string> { [Variable] = "1" });

        Assert.Equal(ExitCodes.ApiError, run.Exit);
        Assert.Empty(run.Requests);
        Assert.Equal(ConfinedVerbs.Sentence + Environment.NewLine, run.Error);
    }

    /// <summary>
    /// A verb of the table in another letter case is no verb of the CLI: it is a usage error, with the variable set or
    /// not, and nothing is sent. The desktop's chat refuses the mis-cased form itself and does not lean on this.
    /// </summary>
    [Theory]
    [InlineData("--actor", "human:x", "Campaign", "Set-Send-Mode", "cmp_A", "--cold", "automatic")]
    [InlineData("--actor", "human:x", "CAMPAIGN", "SET-SEND-MODE", "cmp_A", "--cold", "automatic")]
    [InlineData("--actor", "human:x", "PREFERENCE", "SET", "send_mode.cold", "--value", "{}")]
    [InlineData("--actor", "human:x", "Preference", "Unset", "send_mode.warm")]
    [InlineData("--actor", "human:x", "campaign", "Set-Send-Mode", "cmp_A", "--cold", "automatic")]
    [InlineData("--actor", "human:x", "Approval", "approve", "apr_A")]
    [InlineData("Campaign", "Start", "cmp_A")]
    [InlineData("campaign", "START", "cmp_A")]
    [InlineData("Campaign", "Archive", "cmp_A")]
    [InlineData("campaign", "ARCHIVE", "cmp_A")]
    public async Task A_table_verb_in_another_letter_case_is_a_usage_error_and_nothing_is_sent(params string[] args)
    {
        foreach (var variables in new[] { new Dictionary<string, string> { [Variable] = "1" }, new Dictionary<string, string>() })
        {
            var run = await DefaultActorTests.RunEachAsync(args, variables);

            Assert.Equal(ExitCodes.Usage, run.Exit);
            Assert.Empty(run.Requests);
        }
    }

    /// <summary>
    /// The bar reads the verb and never the key: a send mode's preferences are refused confined as any preference is, with
    /// an attempt named or not, and nothing is sent.
    /// </summary>
    [Theory]
    [InlineData("preference", "set", "send_mode.cold", "--value", "{\"mode\":\"automatic\"}")]
    [InlineData("preference", "set", "send_mode.warm", "--value", "{\"mode\":\"every_message\"}")]
    [InlineData("preference", "unset", "send_mode.cold")]
    [InlineData("preference", "unset", "send_mode.warm")]
    public async Task Confined_a_send_mode_preference_is_refused_whatever_its_key_and_nothing_is_sent(params string[] args)
    {
        foreach (var variables in new[] { new Dictionary<string, string> { [Variable] = "1" }, new Dictionary<string, string> { [Variable] = "1", ["JASON_ATTEMPT_ID"] = Attempt } })
        {
            var run = await DefaultActorTests.RunEachAsync(["--actor", "human:x", .. args], variables);

            Assert.Equal(ExitCodes.ApiError, run.Exit);
            Assert.Empty(run.Requests);
            Assert.Equal(ConfinedVerbs.Sentence + Environment.NewLine, run.Error);
        }
    }

    /// <summary>
    /// Starting a campaign, or resuming a paused one, is what lets its queued work run, so in the chat it is the person's
    /// own act: every form of the command is refused with the variable set, with a reason, an actor or an attempt named,
    /// and nothing is sent.
    /// </summary>
    [Theory]
    [InlineData("campaign", "start", "cmp_A")]
    [InlineData("campaign", "start", "cmp_A", "--reason", "The person said go.")]
    [InlineData("--actor", "human:x", "campaign", "start", "cmp_A")]
    [InlineData("--actor", "agent:chat", "campaign", "start", "cmp_A", "--reason", "Resuming after the holiday.")]
    [InlineData("campaign", "start", "cmp_A", "--actor", "human:x")]
    public async Task Confined_starting_a_campaign_is_refused_in_every_form_and_nothing_is_sent(params string[] args)
    {
        foreach (var variables in new[] { new Dictionary<string, string> { [Variable] = "1" }, new Dictionary<string, string> { [Variable] = "1", ["JASON_ATTEMPT_ID"] = Attempt } })
        {
            var run = await DefaultActorTests.RunEachAsync(args, variables);

            Assert.Equal(ExitCodes.ApiError, run.Exit);
            Assert.Empty(run.Requests);
            Assert.Equal(ConfinedVerbs.Sentence + Environment.NewLine, run.Error);
        }
    }

    /// <summary>
    /// Archiving a campaign is final: no verb undoes it, and it cancels the campaign's open work but a search's stop, with
    /// each item's approval whether still asked or already given, stops its running attempts once the archive is
    /// committed, and cancels its pending questions. In the chat it is the person's own act: every form of the command is
    /// refused with the variable set, with a reason, an actor or an attempt named, and nothing is sent.
    /// </summary>
    [Theory]
    [InlineData("campaign", "archive", "cmp_A")]
    [InlineData("campaign", "archive", "cmp_A", "--reason", "The person said it is over.")]
    [InlineData("--actor", "human:x", "campaign", "archive", "cmp_A")]
    [InlineData("--actor", "agent:chat", "campaign", "archive", "cmp_A", "--reason", "The offer has ended.")]
    [InlineData("campaign", "archive", "cmp_A", "--actor", "human:x")]
    public async Task Confined_archiving_a_campaign_is_refused_in_every_form_and_nothing_is_sent(params string[] args)
    {
        foreach (var variables in new[] { new Dictionary<string, string> { [Variable] = "1" }, new Dictionary<string, string> { [Variable] = "1", ["JASON_ATTEMPT_ID"] = Attempt } })
        {
            var run = await DefaultActorTests.RunEachAsync(args, variables);

            Assert.Equal(ExitCodes.ApiError, run.Exit);
            Assert.Empty(run.Requests);
            Assert.Equal(ConfinedVerbs.Sentence + Environment.NewLine, run.Error);
        }
    }

    /// <summary>
    /// The chat keeps the acts that stop reaching a person: pausing a campaign, adding to the do-not-contact register,
    /// cancelling work and recording a verdict on a reply run with the variable set.
    /// </summary>
    [Theory]
    [InlineData("campaign", "pause", "cmp_A")]
    [InlineData("suppression", "add", "--channel", "email", "--value", "someone@example.com")]
    [InlineData("workitem", "cancel", "wi_A")]
    [InlineData("reply", "record", "cmp_A", "--plugin", "mailbox", "--thread", "thr_A", "--message", "msg_A", "--message-at", "2026-10-12T00:00:00Z", "--category", "interested", "--confidence", "0.9", "--reaction", "draft")]
    public async Task Confined_the_acts_that_stop_reaching_a_person_run_as_before(params string[] args)
    {
        var run = await DefaultActorTests.RunEachAsync(args, new Dictionary<string, string> { [Variable] = "1" });

        Assert.Equal(ExitCodes.Success, run.Exit);
        Assert.NotEmpty(run.Requests);
    }

    /// <summary>The verbs a role attempt and a chat's agent keep are untouched with the variable set.</summary>
    [Theory]
    [InlineData("workitem", "get", "wi_A")]
    [InlineData("workitem", "complete", "wi_A", "--attempt", "att_A", "--status", "succeeded")]
    [InlineData("workitem", "create", "cmp_A", "--kind", "ai_role", "--role", "researcher")]
    [InlineData("decision", "raise", "wi_A", "--attempt", "att_A", "--question", "Which tone?")]
    [InlineData("decision", "list")]
    [InlineData("campaign", "list")]
    public async Task Confined_the_verbs_not_in_the_table_run_as_before(params string[] args)
    {
        var run = await DefaultActorTests.RunEachAsync(args, new Dictionary<string, string> { [Variable] = "1" });

        Assert.Equal(ExitCodes.Success, run.Exit);
        Assert.NotEmpty(run.Requests);
    }

    /// <summary>
    /// The notification noun is confined verb by verb, as the plugin noun is, not as a whole: so every verb the CLI
    /// registers under it is covered by an entry, and a verb added there later is a difference in this row before it is a
    /// switch the chat could run.
    /// </summary>
    [Fact]
    public void Every_verb_the_cli_registers_under_the_notification_noun_is_confined()
    {
        using var paths = new TempPaths();
        var root = CliApp.BuildRootCommand(new CliEnvironment(TextWriter.Null, TextWriter.Null, paths.Paths));
        var noun = Assert.Single(root.Subcommands, command => command.Name == "notification");

        Assert.NotEmpty(noun.Subcommands);
        var open = noun.Subcommands.Select(verb => $"notification {verb.Name}").Where(verb => ConfinedVerbs.Covering(verb) is null).ToList();
        Assert.True(open.Count == 0, $"Not confined: {string.Join(", ", open)}");
    }

    /// <summary>
    /// The noun alone, and its help, run no verb: confined, each sends nothing. The bare noun is a usage error, as it is
    /// unconfined; its help names the two verbs, whose own help states the rule.
    /// </summary>
    [Theory]
    [InlineData(ExitCodes.Usage, "notification")]
    [InlineData(ExitCodes.Success, "notification", "--help")]
    public async Task Confined_the_notification_noun_alone_sends_nothing(int exit, params string[] args)
    {
        var run = await DefaultActorTests.RunEachAsync(args, new Dictionary<string, string> { [Variable] = "1" });

        Assert.Equal(exit, run.Exit);
        Assert.Empty(run.Requests);
    }

    /// <summary>
    /// The person's own word, whose help says the act is the person's: the verbs that take no default actor and name the
    /// person. Three entries of the same kind are left out, as a role runs them itself: <c>workitem review</c>, which the
    /// critic sends from its own run, <c>report submit</c>, whose reporter may be <c>role:&lt;id&gt;</c>, and
    /// <c>workitem resume</c>, which ends a wait on the word of whoever is named, a person or not.
    /// </summary>
    private static readonly string[] PersonsOwnWord =
    [
        "approval approve", "approval reject", "approval set-standing", "approval unset-standing", "decision answer",
        "preference set", "preference unset", "campaign set-send-mode", "block clear", "subscription rewind", "launches lift",
    ];

    /// <summary>
    /// Help is not a run of the verb: a refused command still shows its help, and the help says what is true of it for every
    /// caller. A person's own word (<see cref="PersonsOwnWord"/>) says the act is the person's, run by an agent on their word
    /// and recorded as theirs; every entry says that the variable a Plan only turn sets refuses it.
    /// </summary>
    [Theory]
    [MemberData(nameof(Entries))]
    public async Task Confined_the_help_of_a_refused_command_is_shown_and_states_the_rule(string entry)
    {
        var output = new StringWriter();
        using var paths = new TempPaths();
        var env = new CliEnvironment(output, new StringWriter(), paths.Paths, Variables: new Dictionary<string, string> { [Variable] = "1" });

        var exit = await CliApp.RunAsync([.. entry.Split(' '), "--help"], env, TestContext.Current.CancellationToken);

        var text = string.Join(' ', output.ToString().Split((char[])[' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(ExitCodes.Success, exit);
        const string act = "The act is the person's: an agent runs it when the person said so, and it is recorded as theirs.";
        if (PersonsOwnWord.Contains(entry))
        {
            Assert.Contains(act, text, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain("The act is the person's", text, StringComparison.Ordinal);
        }

        Assert.Contains("Under JASON_CONFINED, which Jason Desktop sets on every chat turn that can only read and propose, it is refused before any request.", text, StringComparison.Ordinal);
        Assert.Contains("A higher bar, not a boundary", text, StringComparison.Ordinal);
        Assert.Contains("a claim, not a proof", text, StringComparison.Ordinal);
        Assert.DoesNotContain("not for an agent", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Jason's window", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The person's own word is exactly the table's entries of that kind that name the person with no default actor, less
    /// the three a role runs itself; a verb added to either side is red here until it is placed.
    /// </summary>
    [Fact]
    public void The_person_s_own_word_is_the_entries_that_name_the_person_less_those_a_role_runs()
    {
        using var paths = new TempPaths();
        var root = CliApp.BuildRootCommand(new CliEnvironment(new StringWriter(), new StringWriter(), paths.Paths));
        var namingThePerson = ConfinedVerbs.Table
            .Where(each => each.Kind == ConfinedVerbs.Kind.PersonsWord)
            .Select(each => each.Command)
            .Where(command => Find(root, command)!.Description!.Contains(ActorOption.NoDefault, StringComparison.Ordinal))
            .ToArray();

        Assert.Equal([.. namingThePerson.Except(["workitem review"]).Order(StringComparer.Ordinal)], PersonsOwnWord.Order(StringComparer.Ordinal));
        Assert.Contains("workitem review", namingThePerson);
    }

    /// <summary>
    /// No verb a role's skill names says in its help that the act is the person's: a role runs what its skill teaches from
    /// its own run, and a help that called that act the person's would teach the role it is not its own.
    /// </summary>
    [Fact]
    public void No_verb_a_role_s_skill_names_says_in_its_help_that_the_act_is_the_person_s()
    {
        using var paths = new TempPaths();
        var root = CliApp.BuildRootCommand(new CliEnvironment(new StringWriter(), new StringWriter(), paths.Paths));
        var roles = Path.Combine(RepositoryRoot(), "skills", "runtime", "roles");
        var files = Directory.GetFiles(roles, "*.md", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        var named = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(file), @"\bjason((?: [a-z][a-z-]*)+)"))
            {
                // The longest run of words that names a command of the tree.
                var words = match.Groups[1].Value.Trim().Split(' ');
                Command? command = root;
                var path = new List<string>();
                foreach (var word in words)
                {
                    var next = command.Subcommands.FirstOrDefault(sub => sub.Name == word);
                    if (next is null)
                    {
                        break;
                    }

                    command = next;
                    path.Add(word);
                }

                if (path.Count > 0 && command.Description?.Contains(ConfinedVerbs.PersonsWordHelp, StringComparison.Ordinal) == true)
                {
                    named.Add($"{Path.GetRelativePath(roles, file)}: jason {string.Join(' ', path)}");
                }
            }
        }

        Assert.Empty(named);
    }

    private static Command? Find(Command root, string verb)
    {
        Command? command = root;
        foreach (var word in verb.Split(' '))
        {
            command = command?.Subcommands.FirstOrDefault(sub => sub.Name == word);
        }

        return command;
    }

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
