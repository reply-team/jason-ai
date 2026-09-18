using System.Text.Json;
using Jason.Cli;
using Jason.Contracts.Api;
using Jason.Contracts.Json;

namespace Jason.Cli.Tests.Commands;

/// <summary>
/// The <c>profile</c> noun from the side it is typed on: what goes on the wire, and what a person reading the
/// answer is shown. The CLI composes a body and nothing more — every rule about what a profile may say is the
/// runtime's, and a second copy of one here would be a second place to get it wrong.
/// </summary>
public class ProfileCommandsTests
{
    private static readonly DateTimeOffset Moment = new(2026, 9, 18, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Profile_list_human_shows_the_host_the_program_and_the_current_revision()
    {
        var page = JsonSerializer.Serialize(
            new Page<ExecutionProfileDto>([Profile("local-claude", revision: 3), Profile("retired", revision: 1, disabled: true)], "cHJmX0E"),
            JasonJson.Options);
        using var cli = new CliRun(page);

        var exit = await cli.RunAsync("profile", "list", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("NAME", cli.Text, StringComparison.Ordinal);
        Assert.Contains("local-claude", cli.Text, StringComparison.Ordinal);
        Assert.Contains("claude_code", cli.Text, StringComparison.Ordinal);
        Assert.Contains("claude", cli.Text, StringComparison.Ordinal);
        Assert.Contains("3", cli.Text, StringComparison.Ordinal);
        Assert.Contains("retired", cli.Text, StringComparison.Ordinal);
        Assert.Contains("next cursor: cHJmX0E", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_sends_every_field_it_was_given()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync(
            "--actor", "human:person-1",
            "profile", "create", "local-claude",
            "--description", "The host installed on this machine.",
            "--host", "claude_code",
            "--program", "claude",
            "--arg", "--permission-mode",
            "--arg", "dontAsk",
            "--deny", "Write",
            "--deny", "WebFetch",
            "--cli-command", "jason",
            "--host-version", "2.1.275",
            "--reason", "Registering the host that is installed.");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.ProfileCreate,
            "{\"name\":\"local-claude\",\"description\":\"The host installed on this machine.\",\"host\":\"claude_code\","
            + "\"program\":\"claude\",\"args\":[\"--permission-mode\",\"dontAsk\"],\"deny\":[\"Write\",\"WebFetch\"],"
            + "\"cli_command\":\"jason\",\"host_version_verified\":\"2.1.275\","
            + "\"reason\":\"Registering the host that is installed.\",\"actor\":{\"type\":\"human\",\"id\":\"person-1\"}}");
    }

    [Fact]
    public async Task Create_lays_the_options_over_a_file_body()
    {
        using var file = new TempFile("{\"host\":\"claude_code\",\"program\":\"from the file\",\"deny\":[\"Write\"]}");
        using var cli = new CliRun();

        var exit = await cli.RunAsync("profile", "create", "local-claude", "--file", file.Path, "--program", "claude");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.ProfileCreate,
            "{\"name\":\"local-claude\",\"host\":\"claude_code\",\"program\":\"claude\",\"deny\":[\"Write\"]}");
    }

    /// <summary>
    /// What makes a patch a patch: an option nobody typed is absent from the body, so the runtime copies that
    /// field forward into the revision it appends rather than being told to clear it.
    /// </summary>
    [Fact]
    public async Task Update_sends_only_the_fields_it_was_given()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("profile", "update", "local-claude", "--deny", "Write", "--deny", "WebFetch");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.ProfileUpdate, "{\"name\":\"local-claude\",\"deny\":[\"Write\",\"WebFetch\"]}");
    }

    /// <summary>An empty list is a thing a repeatable option cannot say, and <c>--file</c> is how it is said.</summary>
    [Fact]
    public async Task Update_can_clear_a_list_through_a_file()
    {
        using var file = new TempFile("{\"args\":[]}");
        using var cli = new CliRun();

        var exit = await cli.RunAsync("profile", "update", "local-claude", "--file", file.Path);

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.ProfileUpdate, "{\"name\":\"local-claude\",\"args\":[]}");
    }

    [Fact]
    public async Task Get_carries_the_name_and_the_revision()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("profile", "get", "local-claude", "--revision", "1");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.ProfileGet, "{\"name\":\"local-claude\",\"revision\":1}");
    }

    [Fact]
    public async Task List_passes_the_page_and_the_disabled_flag()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("profile", "list", "--include-disabled", "--limit", "5", "--cursor", "cHJmX0E");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.ProfileList, "{\"include_disabled\":true,\"limit\":5,\"cursor\":\"cHJmX0E\"}");
    }

    [Fact]
    public async Task List_without_options_sends_an_empty_body()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("profile", "list");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.ProfileList, "{}");
    }

    [Theory]
    [InlineData("disable", Operations.ProfileDisable)]
    [InlineData("enable", Operations.ProfileEnable)]
    public async Task A_toggle_verb_maps_to_its_operation(string verb, string operation)
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("profile", verb, "retired", "--reason", "The host was uninstalled.", "--actor", "human:person-1");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            operation,
            "{\"name\":\"retired\",\"reason\":\"The host was uninstalled.\",\"actor\":{\"type\":\"human\",\"id\":\"person-1\"}}");
    }

    /// <summary>
    /// The CLI composes a body and nothing more. Which hosts exist is the runtime's vocabulary, and a copy of it
    /// here would be a second place to update when a host is added — and the place that got it wrong meanwhile.
    /// </summary>
    [Fact]
    public async Task A_host_the_cli_has_never_heard_of_still_goes_on_the_wire()
    {
        using var cli = new CliRun(
            "{\"error\":{\"code\":\"validation_failed\",\"message\":\"Validation failed: host: invalid.\",\"retryable\":false}}",
            status: System.Net.HttpStatusCode.BadRequest);

        var exit = await cli.RunAsync("profile", "create", "someday", "--host", "codex", "--program", "codex");

        Assert.Equal(ExitCodes.ApiError, exit);
        cli.AssertPosted(Operations.ProfileCreate, "{\"name\":\"someday\",\"host\":\"codex\",\"program\":\"codex\"}");

        // The API's own answer, verbatim on stdout: stderr is diagnostics, and the envelope is the answer.
        Assert.Contains("host: invalid", cli.Text, StringComparison.Ordinal);
    }

    /// <summary>The CLI is a client, not an editor: what the runtime answered is what is printed.</summary>
    [Fact]
    public async Task Get_prints_the_api_response_exactly_as_it_came()
    {
        var body = JsonSerializer.Serialize(Profile("local-claude", revision: 3), JasonJson.Options);
        using var cli = new CliRun(body);

        var exit = await cli.RunAsync("profile", "get", "local-claude");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(body, cli.Text.TrimEnd());
    }

    [Fact]
    public async Task Human_mode_renders_one_profile()
    {
        using var cli = new CliRun(JsonSerializer.Serialize(Profile("local-claude", revision: 3), JasonJson.Options));

        var exit = await cli.RunAsync("profile", "get", "local-claude", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("local-claude", cli.Text, StringComparison.Ordinal);
        Assert.Contains("claude_code", cli.Text, StringComparison.Ordinal);
        Assert.Contains("--permission-mode dontAsk", cli.Text, StringComparison.Ordinal);
        Assert.Contains("Deny:", cli.Text, StringComparison.Ordinal);
        Assert.Contains("Write", cli.Text, StringComparison.Ordinal);
        Assert.Contains("Revision:", cli.Text, StringComparison.Ordinal);
        Assert.Contains("2.1.275", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", cli.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reading an old revision and taking it for the live one is the one mistake this page can cause, so the
    /// page says which is which rather than printing a number that looks like the answer.
    /// </summary>
    [Fact]
    public async Task Human_mode_says_when_the_revision_read_is_not_the_one_in_force()
    {
        var profile = Profile("local-claude", revision: 3) with
        {
            Revision = new ExecutionProfileRevisionDto(1, AgentHostKind.ClaudeCode, "claude", [], [], "jason", null, Moment),
        };
        using var cli = new CliRun(JsonSerializer.Serialize(profile, JasonJson.Options));

        var exit = await cli.RunAsync("profile", "get", "local-claude", "--revision", "1", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("1 (in force: 3)", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_falls_back_to_the_raw_body_when_the_shape_is_unknown()
    {
        using var cli = new CliRun("{\"unexpected\":true}");

        var exit = await cli.RunAsync("profile", "get", "local-claude", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("{\"unexpected\":true}", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_option_is_a_usage_error()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("profile", "get", "local-claude", "--bogus");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--bogus", cli.Error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>There is no delete in the API, so there is none to type.</summary>
    [Theory]
    [InlineData("delete")]
    [InlineData("remove")]
    [InlineData("rename")]
    public async Task There_is_no_verb_that_removes_a_profile(string verb)
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("profile", verb, "local-claude");

        Assert.Equal(ExitCodes.Usage, exit);
    }

    private static ExecutionProfileDto Profile(string name, int revision, bool disabled = false) =>
        new(
            "prf_" + name,
            name,
            "The host installed on this machine.",
            disabled,
            revision,
            new ExecutionProfileRevisionDto(revision, AgentHostKind.ClaudeCode, "claude", ["--permission-mode", "dontAsk"], ["Write"], "jason", "2.1.275", Moment),
            Moment,
            Moment);
}
