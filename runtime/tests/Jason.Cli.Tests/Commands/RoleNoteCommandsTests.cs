using System.Text.Json;
using Jason.Cli;
using Jason.Contracts.Api;
using Jason.Contracts.Json;

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

    [Fact]
    public async Task Get_names_the_campaign_and_the_role()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("rolenote", "get", "cmp_A", "researcher");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.RoleNoteGet, """{"campaign_id":"cmp_A","role":"researcher"}""");
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

        var exit = await cli.RunAsync("rolenote", "list", "cmp_A", "--limit", "5", "--cursor", "cmVzZWFyY2hlcg");

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

    /// <summary>A note that has never been written says so, rather than showing a blank where a date goes.</summary>
    [Fact]
    public async Task Rolenote_get_human_says_never_for_a_note_nobody_has_written()
    {
        var note = JsonSerializer.Serialize(
            new RoleNoteDto("cmp_A", "researcher", [], null, 0, null, null, null), JasonJson.Options);
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
                    new RoleNoteSummaryDto("cmp_A", "planner", "sha256:2222222222222222222222222222222222222222222222222222222222222222", 40, Moment, Moment, new ActorRef(ActorType.Role, "planner")),
                    new RoleNoteSummaryDto("cmp_A", "researcher", "sha256:3333333333333333333333333333333333333333333333333333333333333333", 120, Moment, Moment, new ActorRef(ActorType.Attempt, "att_A")),
                ],
                "cmVzZWFyY2hlcg"),
            JasonJson.Options);
        using var cli = new CliRun(page);

        var exit = await cli.RunAsync("rolenote", "list", "cmp_A", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        var rows = cli.Text.Split('\n').Select(Squeezed).ToList();
        Assert.Contains("ROLE BYTES WRITTEN BY HASH", rows);
        Assert.Contains(rows, row => row.StartsWith("planner 40 ", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.StartsWith("researcher 120 ", StringComparison.Ordinal));
        Assert.Contains("next cursor: cmVzZWFyY2hlcg", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", cli.Text, StringComparison.Ordinal);
    }
}
