using System.Text.Json;
using Jason.Cli;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Commands;

/// <summary>
/// The <c>rolenote</c> noun from the side it is typed on. The CLI composes a body and nothing more: what a note
/// may hold is the runtime's business, and a second copy of that rule here would be a second place to get it
/// wrong. What is checked here is the shape a launched agent has to get right from a skill page.
/// </summary>
public class RoleNoteCommandsTests
{
    private static readonly DateTimeOffset Moment = new(2026, 9, 18, 10, 0, 0, TimeSpan.Zero);

    private static string Squeezed(string row) => string.Join(' ', row.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>
    /// The role is named by <c>--role</c> and the level by an option: a campaign's, a project's, or — naming neither — the
    /// client's, which is the role's general craft. The CLI sends the level it was given and decides nothing about it.
    /// </summary>
    [Theory]
    [InlineData("rolenote get --role researcher", """{"role":"researcher"}""")]
    [InlineData("rolenote get --role researcher --campaign cmp_A", """{"campaign_id":"cmp_A","role":"researcher"}""")]
    [InlineData("rolenote get --project prj_A --role researcher", """{"project_id":"prj_A","role":"researcher"}""")]
    [InlineData("rolenote list", """{}""")]
    [InlineData("rolenote list --project prj_A --limit 5", """{"project_id":"prj_A","limit":5}""")]
    [InlineData("rolenote list --campaign cmp_A", """{"campaign_id":"cmp_A"}""")]
    public async Task A_verb_names_the_level_by_its_option_and_the_client_s_by_neither(string line, string body)
    {
        using var cli = new CliRun();
        var args = line.Split(' ');

        var exit = await cli.RunAsync(args);

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(args[1] == "get" ? Operations.RoleNoteGet : Operations.RoleNoteList, body);
    }

    [Theory]
    [InlineData("--campaign", "cmp_A", "campaign_id")]
    [InlineData("--project", "prj_A", "project_id")]
    public async Task Set_writes_the_note_at_the_level_its_option_names(string option, string id, string field)
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("rolenote", "set", "--role", "copywriter", option, id, "--note", """{"voice":"short sentences"}""");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.RoleNoteSet, $$$"""{"{{{field}}}":"{{{id}}}","role":"copywriter","note":{"voice":"short sentences"}}""");
    }

    [Fact]
    public async Task Set_with_neither_option_writes_the_client_s_note()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("--actor", "role:researcher", "rolenote", "set", "--role", "researcher", "--note", """{"craft":"ask before assuming"}""");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.RoleNoteSet, """{"role":"researcher","note":{"craft":"ask before assuming"},"actor":{"type":"role","id":"researcher"}}""");
    }

    /// <summary>
    /// The older form — the campaign, then the role — is what skills already written print, so it is accepted still, as
    /// that campaign's note, when it is whole: exactly those two arguments.
    /// </summary>
    [Fact]
    public async Task Get_names_the_campaign_and_the_role()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("rolenote", "get", "cmp_A", "researcher");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.RoleNoteGet, """{"campaign_id":"cmp_A","role":"researcher"}""");
    }

    /// <summary>
    /// A campaign and a project together are sent as they were given: whether they name one level is the runtime's to
    /// answer, and it refuses them by name.
    /// </summary>
    [Fact]
    public async Task A_campaign_and_a_project_are_sent_for_the_runtime_to_refuse()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("rolenote", "get", "--role", "researcher", "--campaign", "cmp_A", "--project", "prj_A");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.RoleNoteGet, """{"campaign_id":"cmp_A","project_id":"prj_A","role":"researcher"}""");
    }

    /// <summary>
    /// What a script that meant a campaign becomes when the variable holding the campaign is empty — one argument, or an
    /// option left with no value — never reaches the client's note: each is a usage error and nothing is sent. So is the
    /// older form mixed with the options, the option form without its role, and an argument to the listing.
    /// </summary>
    [Theory]
    // Critical: the older form shortened by an empty variable is refused rather than read as the client's level.
    [InlineData("rolenote set researcher --note {}", Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.X2)])]
    [InlineData("rolenote get researcher")]
    [InlineData("rolenote set --campaign --role researcher --note {}")]
    [InlineData("rolenote get --project --role researcher")]
    [InlineData("rolenote get cmp_A researcher --campaign cmp_B")]
    [InlineData("rolenote set cmp_A researcher --role researcher --note {}")]
    [InlineData("rolenote get cmp_A researcher --project prj_A")]
    [InlineData("rolenote get --campaign cmp_A")]
    [InlineData("rolenote set --note {}")]
    [InlineData("rolenote list cmp_A")]
    public async Task A_form_that_could_reach_the_wrong_level_is_a_usage_error(string line)
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync(line.Split(' '));

        Assert.True(exit == ExitCodes.Usage && cli.Sent is null, $"'{line}' exited {exit} and sent {cli.Sent ?? "nothing"}");
    }

    /// <summary>
    /// Help publishes the verb's contract: the form to use — the role and the level by their options — and, in one line,
    /// the older form it still accepts, since a form the help never names is a trap for its reader.
    /// </summary>
    [Theory]
    [InlineData("get")]
    [InlineData("set")]
    public async Task Help_shows_the_option_form_and_names_the_older_one(string verb)
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("rolenote", verb, "--help");

        Assert.Equal(ExitCodes.Success, exit);
        foreach (var shown in (string[])["--role", "--campaign", "--project", "the client's", $"The older form, rolenote {verb} <campaign-id> <role>, is still accepted", $" rolenote {verb} [options]"])
        {
            Assert.True(cli.Text.Contains(shown, StringComparison.Ordinal), $"The help of rolenote {verb} does not say '{shown}':{Environment.NewLine}{cli.Text}");
        }
    }

    [Fact]
    public async Task List_help_names_the_level_options_and_takes_no_argument()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("rolenote", "list", "--help");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.True(
            cli.Text.Contains("--campaign", StringComparison.Ordinal) && cli.Text.Contains("--project", StringComparison.Ordinal)
                && cli.Text.Contains(" rolenote list [options]", StringComparison.Ordinal),
            $"The listing's help read:{Environment.NewLine}{cli.Text}");
    }

    [Fact]
    public async Task Set_sends_the_note_it_was_given_on_the_command_line()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync(
            "--actor", "role:researcher",
            "rolenote", "set", "cmp_A", "researcher",
            "--note", """{"pass":2,"open_questions":["who signs"]}""",
            "--reason", "What the second pass found.");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.RoleNoteSet,
            """
            {"campaign_id":"cmp_A","role":"researcher","note":{"pass":2,"open_questions":["who signs"]},
             "reason":"What the second pass found.","actor":{"type":"role","id":"researcher"}}
            """);
    }

    /// <summary>
    /// The form a launched agent uses: it writes the document to a file and names the file. The whole file is
    /// the note — there is no envelope around it to get wrong — and the command stays the plain form that the
    /// allow rule grants, with no redirect in it.
    /// </summary>
    [Fact]
    public async Task Set_takes_the_whole_note_from_a_file()
    {
        using var file = new TempFile("""{"pass":3,"still_open":["who signs"]}""");
        using var cli = new CliRun();

        var exit = await cli.RunAsync("rolenote", "set", "cmp_A", "researcher", "--note-file", file.Path);

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.RoleNoteSet,
            """{"campaign_id":"cmp_A","role":"researcher","note":{"pass":3,"still_open":["who signs"]}}""");
    }

    [Fact]
    public async Task Set_takes_the_note_from_standard_input()
    {
        using var cli = new CliRun(standardInput: """{"pass":4}""");

        var exit = await cli.RunAsync("rolenote", "set", "cmp_A", "researcher", "--note-file", "-");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.RoleNoteSet, """{"campaign_id":"cmp_A","role":"researcher","note":{"pass":4}}""");
    }

    /// <summary>
    /// Two sources for one document is a caller who has lost track of what they are writing, and a note is
    /// replaced whole: choosing one for them would throw the other away without saying so.
    /// </summary>
    [Fact]
    public async Task Set_refuses_both_sources_of_the_note_at_once()
    {
        using var file = new TempFile("""{"from":"the file"}""");
        using var cli = new CliRun();

        var exit = await cli.RunAsync("rolenote", "set", "cmp_A", "researcher", "--note", """{"from":"the line"}""", "--note-file", file.Path);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Null(cli.Sent);
    }

    [Fact]
    public async Task Set_without_a_note_is_a_usage_error_rather_than_an_empty_one()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("rolenote", "set", "cmp_A", "researcher");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Null(cli.Sent);
    }

    /// <summary>A note that is not an object is caught before anything is sent: the verb writes documents.</summary>
    [Fact]
    public async Task Set_refuses_a_note_that_is_not_an_object()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("rolenote", "set", "cmp_A", "researcher", "--note", """["a list of things"]""");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Null(cli.Sent);
    }

    /// <summary>Clearing a note is writing an empty one, which is why there is no delete verb to find.</summary>
    [Fact]
    public async Task An_empty_object_is_how_a_note_is_cleared()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("rolenote", "set", "cmp_A", "researcher", "--note", "{}");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.RoleNoteSet, """{"campaign_id":"cmp_A","role":"researcher","note":{}}""");
    }

    [Fact]
    public async Task List_passes_the_campaign_and_the_page()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("rolenote", "list", "--campaign", "cmp_A", "--limit", "5", "--cursor", "cmVzZWFyY2hlcg");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.RoleNoteList, """{"campaign_id":"cmp_A","limit":5,"cursor":"cmVzZWFyY2hlcg"}""");
    }

    /// <summary>
    /// A person reading a note is shown what it says and told whose it is. The heading is the part that has to
    /// survive: somebody glancing at this must not take a role's working memory for what the runtime knows.
    /// </summary>
    [Fact]
    public async Task Rolenote_get_human_shows_the_note_and_says_whose_memory_it_is()
    {
        var note = JsonSerializer.Serialize(
            new RoleNoteDto(
                "cmp_A",
                null,
                "researcher",
                new System.Text.Json.Nodes.JsonObject { ["pass"] = 2 },
                "sha256:1111111111111111111111111111111111111111111111111111111111111111",
                12,
                Moment,
                Moment,
                new ActorRef(ActorType.Role, "researcher")),
            JasonJson.Options);
        using var cli = new CliRun(note);

        var exit = await cli.RunAsync("rolenote", "get", "cmp_A", "researcher", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains(Jason.Cli.Human.RoleNoteRenderers.Heading, cli.Text, StringComparison.Ordinal);
        var rows = cli.Text.Split('\n').Select(Squeezed).ToList();
        Assert.Contains("Role: researcher", rows);
        Assert.Contains("By: role:researcher", rows);
        Assert.Contains("Size: 12 bytes", rows);
        Assert.Contains("\"pass\": 2", cli.Text, StringComparison.Ordinal);
    }

    /// <summary>A person reading a note is told its level: a project's by the project, the client's in words.</summary>
    [Theory]
    [InlineData(null, "Level: the client's — the role's general craft")]
    [InlineData("prj_A", "Project: prj_A")]
    public async Task Rolenote_get_human_says_which_level_the_note_is_at(string? project, string row)
    {
        var note = JsonSerializer.Serialize(
            new RoleNoteDto(null, project, "researcher", new System.Text.Json.Nodes.JsonObject { ["craft"] = "ask first" }, null, 0, null, null, null),
            JasonJson.Options);
        using var cli = new CliRun(note);

        var exit = await cli.RunAsync(project is null ? ["rolenote", "get", "--role", "researcher", "--human"] : ["rolenote", "get", "--role", "researcher", "--project", project, "--human"]);

        Assert.Equal(ExitCodes.Success, exit);
        var rows = cli.Text.Split('\n').Select(Squeezed).ToList();
        Assert.True(rows.Contains(row) && !rows.Any(r => r.StartsWith("Campaign:", StringComparison.Ordinal)), $"The note rendered as:{Environment.NewLine}{cli.Text}");
    }

    /// <summary>A note that has never been written says so, rather than showing a blank where a date goes.</summary>
    [Fact]
    public async Task Rolenote_get_human_says_never_for_a_note_nobody_has_written()
    {
        var note = JsonSerializer.Serialize(
            new RoleNoteDto("cmp_A", null, "researcher", [], null, 0, null, null, null), JasonJson.Options);
        using var cli = new CliRun(note);

        var exit = await cli.RunAsync("rolenote", "get", "cmp_A", "researcher", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("Written: never", cli.Text.Split('\n').Select(Squeezed).ToList());
    }

    /// <summary>The listing carries no note, so the table cannot show one: it shows who wrote and how much.</summary>
    [Fact]
    public async Task Rolenote_list_human_shows_the_roles_and_never_a_note()
    {
        var page = JsonSerializer.Serialize(
            new Page<RoleNoteSummaryDto>(
                [
                    new RoleNoteSummaryDto("cmp_A", null, "planner", "sha256:2222222222222222222222222222222222222222222222222222222222222222", 40, Moment, Moment, new ActorRef(ActorType.Role, "planner")),
                    new RoleNoteSummaryDto("cmp_A", null, "researcher", "sha256:3333333333333333333333333333333333333333333333333333333333333333", 120, Moment, Moment, new ActorRef(ActorType.Attempt, "att_A")),
                ],
                "cmVzZWFyY2hlcg"),
            JasonJson.Options);
        using var cli = new CliRun(page);

        var exit = await cli.RunAsync("rolenote", "list", "--campaign", "cmp_A", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        var rows = cli.Text.Split('\n').Select(Squeezed).ToList();
        Assert.Contains("ROLE BYTES WRITTEN BY HASH", rows);
        Assert.Contains(rows, row => row.StartsWith("planner 40 ", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.StartsWith("researcher 120 ", StringComparison.Ordinal));
        Assert.Contains("next cursor: cmVzZWFyY2hlcg", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", cli.Text, StringComparison.Ordinal);
    }
}
