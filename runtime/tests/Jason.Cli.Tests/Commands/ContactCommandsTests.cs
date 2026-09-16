using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Contracts.Api;
using Jason.Contracts.Json;

namespace Jason.Cli.Tests.Commands;

public class ContactCommandsTests
{
    private static readonly DateTimeOffset Moment = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Create_sends_every_scalar_field()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync(
            "contact",
            "create",
            "--first-name",
            "Ada",
            "--last-name",
            "Lovelace",
            "--company",
            "Analytical Engines",
            "--title",
            "Mathematician",
            "--time-zone",
            "Europe/Kyiv");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.ContactCreate,
            "{\"first_name\":\"Ada\",\"last_name\":\"Lovelace\",\"company\":\"Analytical Engines\",\"title\":\"Mathematician\",\"time_zone\":\"Europe/Kyiv\"}");
    }

    [Fact]
    public async Task Create_turns_emails_and_channels_into_entries()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync(
            "contact",
            "create",
            "--email",
            "ada@example.com",
            "--email",
            "ada@work.example.com",
            "--channel",
            "linkedin=https://www.linkedin.com/in/ada",
            "--custom",
            "{\"source\":\"a conference\"}");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.ContactCreate,
            "{\"channels\":["
                + "{\"channel\":\"email\",\"value\":\"ada@example.com\",\"primary\":false},"
                + "{\"channel\":\"email\",\"value\":\"ada@work.example.com\",\"primary\":false},"
                + "{\"channel\":\"linkedin\",\"value\":\"https://www.linkedin.com/in/ada\",\"primary\":false}],"
                + "\"custom\":{\"source\":\"a conference\"}}");
    }

    [Fact]
    public async Task A_channel_without_a_value_is_a_usage_error()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("contact", "create", "--channel", "linkedin");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--channel", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_channel_value_may_hold_further_equals_signs()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("contact", "create", "--channel", "portal=https://example.com/?id=7");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.ContactCreate,
            "{\"channels\":[{\"channel\":\"portal\",\"value\":\"https://example.com/?id=7\",\"primary\":false}]}");
    }

    [Fact]
    public async Task A_custom_value_that_is_not_an_object_is_a_usage_error()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("contact", "create", "--custom", "[1]");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--custom", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_lays_the_options_over_a_file_body()
    {
        using var file = new TempFile("{\"first_name\":\"from the file\",\"company\":\"Analytical Engines\"}");
        using var cli = new CliRun();

        var exit = await cli.RunAsync("contact", "create", "--file", file.Path, "--first-name", "Ada");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.ContactCreate, "{\"first_name\":\"Ada\",\"company\":\"Analytical Engines\"}");
    }

    [Fact]
    public async Task Get_carries_the_positional_id_in_the_body()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("contact", "get", "cnt_A");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.ContactGet, "{\"contact_id\":\"cnt_A\"}");
    }

    [Fact]
    public async Task Archive_carries_the_reason_and_the_actor()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("contact", "archive", "cnt_A", "--reason", "they asked to be forgotten", "--actor", "human:ada");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.ContactArchive,
            "{\"contact_id\":\"cnt_A\",\"reason\":\"they asked to be forgotten\",\"actor\":{\"type\":\"human\",\"id\":\"ada\"}}");
    }

    [Fact]
    public async Task List_passes_the_filters()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("contact", "list", "--channel", "email", "--value", "ada@example.com", "--include-archived", "--limit", "5", "--cursor", "Y250X0E");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.ContactList,
            "{\"channel\":\"email\",\"value\":\"ada@example.com\",\"include_archived\":true,\"limit\":5,\"cursor\":\"Y250X0E\"}");
    }

    [Fact]
    public async Task List_leaves_out_the_archive_flag_when_it_was_not_asked_for()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("contact", "list", "--channel", "email");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.ContactList, "{\"channel\":\"email\"}");
    }

    [Fact]
    public async Task Update_patches_the_named_fields_only()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("contact", "update", "cnt_A", "--company", "Analytical Engines");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.ContactUpdate, "{\"contact_id\":\"cnt_A\",\"company\":\"Analytical Engines\"}");
        Assert.DoesNotContain("first_name", cli.Sent!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Update_clears_the_fields_it_is_told_to_clear()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("contact", "update", "cnt_A", "--clear", "title", "--clear", "time_zone");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.ContactUpdate, "{\"contact_id\":\"cnt_A\",\"title\":null,\"time_zone\":null}");
    }

    [Fact]
    public async Task Clearing_a_field_that_is_not_patchable_is_a_usage_error()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("contact", "update", "cnt_A", "--clear", "channels");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--clear", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Update_replaces_the_whole_channel_list()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("contact", "update", "cnt_A", "--email", "ada@example.com", "--custom", "{\"source\":\"a conference\"}");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.ContactUpdate,
            "{\"contact_id\":\"cnt_A\",\"channels\":[{\"channel\":\"email\",\"value\":\"ada@example.com\",\"primary\":false}],\"custom\":{\"source\":\"a conference\"}}");
    }

    [Fact]
    public async Task Human_mode_renders_one_contact_with_its_channels()
    {
        var contact = JsonSerializer.Serialize(
            new ContactDto(
                "cnt_A",
                "Ada",
                "Lovelace",
                "Analytical Engines",
                "Mathematician",
                "Europe/Kyiv",
                [new ChannelDto("email", "ada@example.com", "work", true, null)],
                new JsonObject { ["source"] = "a conference" },
                [],
                Moment,
                Moment,
                null),
            JasonJson.Options);
        using var cli = new CliRun(contact);

        var exit = await cli.RunAsync("contact", "get", "cnt_A", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("cnt_A", cli.Text, StringComparison.Ordinal);
        Assert.Contains("Ada Lovelace", cli.Text, StringComparison.Ordinal);
        Assert.Contains("Analytical Engines", cli.Text, StringComparison.Ordinal);
        Assert.Contains("Europe/Kyiv", cli.Text, StringComparison.Ordinal);
        Assert.Contains("CHANNEL", cli.Text, StringComparison.Ordinal);
        Assert.Contains("ada@example.com", cli.Text, StringComparison.Ordinal);
        Assert.Contains("source", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_renders_a_contact_listing()
    {
        var page = JsonSerializer.Serialize(
            new Page<ContactDto>(
                [
                    new ContactDto(
                        "cnt_A",
                        "Ada",
                        "Lovelace",
                        "Analytical Engines",
                        null,
                        "Europe/Kyiv",
                        [new ChannelDto("email", "ada@example.com", null, false, null)],
                        [],
                        [],
                        Moment,
                        Moment,
                        null),
                ],
                "Y250X0E"),
            JasonJson.Options);
        using var cli = new CliRun(page);

        var exit = await cli.RunAsync("contact", "list", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("COMPANY", cli.Text, StringComparison.Ordinal);
        Assert.Contains("Ada Lovelace", cli.Text, StringComparison.Ordinal);
        Assert.Contains("ada@example.com", cli.Text, StringComparison.Ordinal);
        Assert.Contains("next cursor: Y250X0E", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", cli.Text, StringComparison.Ordinal);
    }
}
