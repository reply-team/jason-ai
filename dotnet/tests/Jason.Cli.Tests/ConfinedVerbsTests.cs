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
        ["block clear"] = ["block", "clear", "blk_A"],
        ["workitem resume"] = ["workitem", "resume", "wi_A"],
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
        ["ui"] = ["ui", "tokens"],
        ["session"] = ["session", "open", "--dry-run"],
        ["journal watch"] = ["journal", "watch"],
        ["export"] = ["export"],
        ["import"] = ["import", "archive.zip"],
        ["suppression remove"] = ["suppression", "remove", "--channel", "email", "--value", "someone@example.com", "--reason", "asked again"],
        ["route set"] = ["route", "set", "--campaign", "cmp_A", "--plugin", "fake-provider", "--operation", "campaign.get"],
        ["route unset"] = ["route", "unset", "--campaign", "cmp_A", "--operation", "campaign.get"],
        ["role set-profile"] = ["role", "set-profile", "researcher", "--execution-profile", "local-claude"],
        ["role add"] = ["role", "add", "researcher", "--execution-profile", "local-claude"],
        ["project link"] = ["project", "link", "prj_A", "--path", "."],
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
    [InlineData("route set")]
    [InlineData("role add")]
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

    /// <summary>Help is not a run of the verb: a refused command still shows its help, and the help states the rule.</summary>
    [Theory]
    [MemberData(nameof(Entries))]
    public async Task Confined_the_help_of_a_refused_command_is_shown_and_states_the_rule(string entry)
    {
        var output = new StringWriter();
        using var paths = new TempPaths();
        var env = new CliEnvironment(output, new StringWriter(), paths.Paths, Variables: new Dictionary<string, string> { [Variable] = "1" });

        var exit = await CliApp.RunAsync([.. entry.Split(' '), "--help"], env, TestContext.Current.CancellationToken);

        var text = output.ToString();
        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("A higher bar, not a boundary", text, StringComparison.Ordinal);
        Assert.Contains("JASON_CONFINED", text, StringComparison.Ordinal);
        Assert.Contains("a claim, not a proof", text, StringComparison.Ordinal);
    }
}
