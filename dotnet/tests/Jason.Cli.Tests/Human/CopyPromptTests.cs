using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Cli.Human;
using Jason.Contracts.Discovery;
using Jason.Contracts.Hosts;

namespace Jason.Cli.Tests.Human;

/// <summary>
/// The text that briefs an agent session on an item that waits: composed in one place, printed by <c>--prompt</c>,
/// holding identifiers and a folder and nothing of the item.
/// </summary>
public sealed partial class CopyPromptTests
{
    private static readonly RuntimeDescriptor Descriptor = new("v1", "0.1.0-dev", "rt_LIVE", 77, "http://127.0.0.1:5000", "the-token", DateTimeOffset.UnixEpoch);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void An_approval_prompt_names_what_waits_where_to_stand_the_skill_the_reading_and_that_the_session_decides_nothing()
    {
        var text = CopyPrompt.Compose(PromptKind.Approval, "apr_9", "cmp_8", "wi_7", new PromptFolder(PromptFolderLevel.Campaign, "/work/acme/q4"));

        Assert.StartsWith("Jason is waiting for a person's decision on an approval: apr_9, in campaign cmp_8.\n", text, StringComparison.Ordinal);
        Assert.Contains("Campaign folder: /work/acme/q4\n", text, StringComparison.Ordinal);
        Assert.Contains("Read its memory file, AGENTS.md: it holds what the person keeps about this campaign. If you can, work from that folder.", text, StringComparison.Ordinal);
        Assert.Contains("Use the skill approvals-and-questions.", text, StringComparison.Ordinal);
        Assert.Contains("jason skills install --host <host>", text, StringComparison.Ordinal);
        Assert.Contains("jason approval get apr_9\n", text, StringComparison.Ordinal);
        Assert.Contains("jason campaign get cmp_8 and jason journal list --campaign cmp_8\n", text, StringComparison.Ordinal);
        Assert.Contains("jason workitem get wi_7; it names its role. Then that role's note, jason rolenote get --role <role> --campaign cmp_8, and the other roles' as you need them (jason rolenote list --campaign cmp_8).", text, StringComparison.Ordinal);
        Assert.Contains("a proposed decision", text, StringComparison.Ordinal);
        Assert.Contains("You decide nothing. Approving, rejecting and answering stay with the person, and so does a standing approval: setting one is the person's own act.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_question_prompt_reads_the_question_through_decision_get_and_proposes_an_answer()
    {
        var text = CopyPrompt.Compose(PromptKind.Question, "dec_9", "cmp_8", "wi_7", null);

        Assert.StartsWith("Jason is waiting for a person's answer to a question: dec_9, in campaign cmp_8.\n", text, StringComparison.Ordinal);
        Assert.Contains("1. Read the question: jason decision get dec_9\n", text, StringComparison.Ordinal);
        Assert.Contains("a proposed answer", text, StringComparison.Ordinal);
        Assert.DoesNotContain("jason approval get", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_host_list_is_written_out_whole_from_the_hosts_the_build_knows()
    {
        var text = CopyPrompt.Compose(PromptKind.Approval, "apr_9", "cmp_8", "wi_7", null);

        Assert.Contains("is one of: " + AgentHosts.Vocabulary + ".", text, StringComparison.Ordinal);
        Assert.DoesNotContain("...", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_folder_the_folder_lines_and_the_memory_file_sentence_are_absent()
    {
        var text = CopyPrompt.Compose(PromptKind.Approval, "apr_9", "cmp_8", "wi_7", null);

        Assert.DoesNotContain("folder", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AGENTS.md", text, StringComparison.Ordinal);
        Assert.Contains("Use the skill approvals-and-questions.", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PromptFolderLevel.Project, "Project folder (the campaign has none recorded): /p\n", "about this project")]
    [InlineData(PromptFolderLevel.HarnessRoot, "Harness root (no folder is recorded for the campaign or its project): /p\n", "about this client")]
    public void A_folder_above_the_campaign_says_which_level_it_is(PromptFolderLevel level, string line, string whose)
    {
        var text = CopyPrompt.Compose(PromptKind.Approval, "apr_9", "cmp_8", "wi_7", new PromptFolder(level, "/p"));

        Assert.Contains(line, text, StringComparison.Ordinal);
        Assert.Contains(whose, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_verb_names_the_campaign_s_own_folder_and_asks_nothing_further()
    {
        var run = await Run(["approval", "get", "apr_1", "--prompt"], Fixture("approval.get", "pending-with-preview"), campaign: Fixture("campaign.get", "in-project-with-folder"));

        Assert.Equal(ExitCodes.Success, run.Exit);
        Assert.Contains("Campaign folder: ~/Jason/projects/acme-corp/campaigns/q3-leads", run.Output, StringComparison.Ordinal);
        Assert.Equal(["approval.get", "campaign.get"], run.Asked);
    }

    [Fact]
    public async Task A_campaign_with_no_folder_falls_back_to_its_project_s()
    {
        var run = await Run(["approval", "get", "apr_1", "--prompt"], Fixture("approval.get", "pending-with-preview"), campaign: Fixture("campaign.get", "one"), project: Fixture("project.get", "by-id"));

        Assert.Equal(ExitCodes.Success, run.Exit);
        Assert.Contains("Project folder (the campaign has none recorded): ~/Jason/projects/acme-corp", run.Output, StringComparison.Ordinal);
        Assert.Equal(["approval.get", "campaign.get", "project.get"], run.Asked);
    }

    [Fact]
    public async Task A_project_with_no_folder_falls_back_to_the_harness_root()
    {
        var run = await Run(["decision", "get", "dec_1", "--prompt"], Fixture("decision.get", "pending-with-options"), campaign: Fixture("campaign.get", "one"), project: Fixture("project.get", "by-slug"), info: Fixture("system.info", "with-ui"));

        Assert.Equal(ExitCodes.Success, run.Exit);
        Assert.Contains("Harness root (no folder is recorded for the campaign or its project): ~/Jason", run.Output, StringComparison.Ordinal);
        Assert.Equal(["decision.get", "campaign.get", "project.get", "system.info"], run.Asked);
    }

    [Fact]
    public async Task When_no_folder_can_be_found_the_verb_still_prints_the_prompt_without_folder_lines()
    {
        // Every further call is refused: the runtime is older than the harness section, or the campaign is gone.
        var run = await Run(["approval", "get", "apr_1", "--prompt"], Fixture("approval.get", "pending-with-preview"));

        Assert.Equal(ExitCodes.Success, run.Exit);
        Assert.StartsWith("Jason is waiting for a person's decision on an approval: apr_1, in campaign cmp_1.", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("folder", run.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, run.Error);
    }

    [Fact]
    public async Task Nothing_of_the_item_is_in_the_prompt_for_an_approval_or_a_question()
    {
        const string Marker = "ZEBRA-DISTINCTIVE-TEXT";
        var approval = JsonNode.Parse(Fixture("approval.get", "pending-with-preview"))!.AsObject();
        approval["campaign_name"] = Marker + " campaign";
        approval["contact_name"] = Marker + " contact";
        approval["decision_reason"] = Marker + " reason";
        approval["preview"]!["intent"] = Marker + " intent";
        approval["preview"]!["summary"] = Marker + " summary";
        approval["subject"]!["note"] = Marker + " subject";
        var question = JsonNode.Parse(Fixture("decision.get", "pending-with-options"))!.AsObject();
        question["question"] = Marker + " question";
        question["options"]![0]!["label"] = Marker + " label";
        question["options"]![0]!["detail"] = Marker + " detail";
        question["campaign_name"] = Marker + " campaign";

        var approvalRun = await Run(["approval", "get", "apr_1", "--prompt"], approval.ToJsonString());
        var questionRun = await Run(["decision", "get", "dec_1", "--prompt"], question.ToJsonString());

        Assert.Contains("apr_1", approvalRun.Output, StringComparison.Ordinal);
        Assert.Contains("dec_1", questionRun.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(Marker, approvalRun.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(Marker, questionRun.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("campaign.enroll", approvalRun.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_two_renderings_are_not_asked_for_together()
    {
        var run = await Run(["approval", "get", "apr_1", "--human", "--prompt"], Fixture("approval.get", "pending-with-preview"));

        Assert.Equal(ExitCodes.Usage, run.Exit);
        Assert.Contains("--human and --prompt", run.Error, StringComparison.Ordinal);
        Assert.Equal(string.Empty, run.Output);
    }

    [Fact]
    public async Task An_answer_that_is_not_an_item_prints_as_it_came()
    {
        var run = await Run(["approval", "get", "apr_1", "--prompt"], "{\"something\":\"else\"}");

        Assert.Equal(ExitCodes.Success, run.Exit);
        Assert.Equal("{\"something\":\"else\"}", run.Output.Trim());
    }

    [Fact]
    public async Task A_refused_read_prints_the_refusal_and_no_prompt()
    {
        var run = await Run(["approval", "get", "apr_1", "--prompt"], "{\"error\":{\"code\":\"approval_not_found\",\"message\":\"x\",\"retryable\":false}}", itemStatus: HttpStatusCode.NotFound);

        Assert.Equal(ExitCodes.ApiError, run.Exit);
        Assert.Contains("approval_not_found", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Jason is waiting", run.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every command line the prompt quotes is a verb this build has, and a line that carries arguments parses through
    /// the program's own parser: a verb renamed or an option dropped turns this red, and with it the recorded text.
    /// </summary>
    [Theory]
    [InlineData(PromptKind.Approval)]
    [InlineData(PromptKind.Question)]
    public void Every_command_the_prompt_quotes_is_one_the_parser_knows(PromptKind kind)
    {
        using var paths = new TempPaths();
        var root = CliApp.BuildRootCommand(new CliEnvironment(TextWriter.Null, TextWriter.Null, paths.Paths));
        var text = CopyPrompt.Compose(kind, kind == PromptKind.Approval ? "apr_9" : "dec_9", "cmp_8", "wi_7", null);

        var quoted = QuotedCommand().Matches(text).Select(match => match.Value).ToList();

        // The quoted lines, so the guard cannot pass by finding nothing.
        Assert.True(quoted.Count >= 8, string.Join(" | ", quoted));
        foreach (var line in quoted)
        {
            var words = line.Split(' ');
            var result = root.Parse(words.Skip(1).ToArray());
            Assert.Equal(words[2], result.CommandResult.Command.Name);
            if (words.Length > 3)
            {
                Assert.True(result.Errors.Count == 0, $"'{line}' does not parse: {string.Join("; ", result.Errors.Select(e => e.Message))}");
            }
        }

        Assert.Contains("jason approval approve", quoted);
        Assert.Contains("jason decision answer", quoted);
        Assert.Contains("jason approval set-standing", quoted);
        Assert.Contains("jason skills install --host <host>", quoted);
    }

    // A command the text quotes: the program's word, a noun and a verb, then flags, ids (prefix_...) and placeholders (<x>).
    [GeneratedRegex(@"jason [a-z]+ [a-z-]+(?: (?:--[a-z-]+|[a-z]+_\w+|<[a-z_-]+>))*")]
    private static partial Regex QuotedCommand();

    internal static string Fixture(string operation, string name)
    {
        var path = Path.Combine(RepositoryRoot(), "docs", "api", "fixtures", operation, name + ".json");
        return JsonNode.Parse(File.ReadAllText(path))!["response"]!.ToJsonString();
    }

    internal static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }

    internal sealed record PromptRun(int Exit, string Output, string Error, List<string> Asked);

    /// <summary>Runs the verb against a runtime that answers the item read and, when given them, the three reads a folder takes; any other read is refused.</summary>
    internal static async Task<PromptRun> Run(string[] args, string item, string? campaign = null, string? project = null, string? info = null, HttpStatusCode itemStatus = HttpStatusCode.OK)
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        var asked = new List<string>();
        var handler = new FakeHandler(request =>
        {
            var operation = request.RequestUri!.AbsolutePath.Split('/')[^1];
            asked.Add(operation);
            string? body = operation switch
            {
                "approval.get" or "decision.get" => item,
                "campaign.get" => campaign,
                "project.get" => project,
                "system.info" => info,
                _ => null,
            };
            return body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{\"error\":{\"code\":\"not_found\",\"message\":\"x\",\"retryable\":false}}") }
                : new HttpResponseMessage(operation is "approval.get" or "decision.get" ? itemStatus : HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
        });
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = await CliApp.RunAsync(args, new CliEnvironment(output, error, dir.Paths, handler), Ct);
        return new PromptRun(exit, output.ToString(), error.ToString(), asked);
    }
}
