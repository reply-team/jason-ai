using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;

namespace Jason.Cli.Tests.Commands;

public class CampaignCommandsTests
{
    private static readonly DateTimeOffset Moment = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Create_sends_the_name_and_the_context()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", "create", "--name", "LatAm", "--context", "{\"icp\":\"founders\"}");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.CampaignCreate, "{\"name\":\"LatAm\",\"context\":{\"icp\":\"founders\"}}");
    }

    [Fact]
    public async Task Create_lays_the_options_over_a_file_body()
    {
        using var file = new TempFile("{\"name\":\"from the file\",\"context\":{\"icp\":\"founders\"}}");
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", "create", "--file", file.Path, "--name", "LatAm");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.CampaignCreate, "{\"name\":\"LatAm\",\"context\":{\"icp\":\"founders\"}}");
    }

    [Fact]
    public async Task A_context_that_is_not_an_object_is_a_usage_error()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", "create", "--name", "LatAm", "--context", "[1]");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--context", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_context_that_is_not_json_is_a_usage_error()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", "create", "--context", "{");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--context", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_carries_the_positional_id_in_the_body()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", "get", "cmp_A");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.CampaignGet, "{\"campaign_id\":\"cmp_A\"}");
    }

    [Fact]
    public async Task List_passes_the_filter_and_the_page()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", "list", "--status", "active", "--limit", "5", "--cursor", "Y21wX0E");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.CampaignList, "{\"status\":\"active\",\"limit\":5,\"cursor\":\"Y21wX0E\"}");
    }

    [Fact]
    public async Task List_without_options_sends_an_empty_body()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", "list");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.CampaignList, "{}");
    }

    [Fact]
    public async Task Update_sends_the_new_name_and_the_reason()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", "update", "cmp_A", "--name", "LatAm Q4", "--reason", "renamed after the split");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.CampaignUpdate, "{\"campaign_id\":\"cmp_A\",\"name\":\"LatAm Q4\",\"reason\":\"renamed after the split\"}");
    }

    [Theory]
    [InlineData("start", Operations.CampaignStart)]
    [InlineData("pause", Operations.CampaignPause)]
    [InlineData("archive", Operations.CampaignArchive)]
    public async Task A_transition_verb_maps_to_its_operation(string verb, string operation)
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", verb, "cmp_A", "--reason", "the quarter ended");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(operation, "{\"campaign_id\":\"cmp_A\",\"reason\":\"the quarter ended\"}");
    }

    [Fact]
    public async Task Update_context_sets_unsets_and_claims_the_actor()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync(
            "campaign",
            "update-context",
            "cmp_A",
            "--set",
            "{\"icp\":\"founders\"}",
            "--unset",
            "old_pitch",
            "--unset",
            "older_pitch",
            "--reason",
            "the plan changed",
            "--actor",
            "role:planner");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.CampaignUpdateContext,
            "{\"campaign_id\":\"cmp_A\",\"set\":{\"icp\":\"founders\"},\"unset\":[\"old_pitch\",\"older_pitch\"],\"reason\":\"the plan changed\",\"actor\":{\"type\":\"role\",\"id\":\"planner\"}}");
    }

    [Fact]
    public async Task Add_contacts_wraps_a_file_array_under_contacts()
    {
        using var file = new TempFile("[{\"first_name\":\"Ada\"},{\"contact_id\":\"cnt_B\"}]");
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", "add-contacts", "cmp_A", "--file", file.Path, "--match-by", "email");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.CampaignAddContacts,
            "{\"campaign_id\":\"cmp_A\",\"match_by\":\"email\",\"contacts\":[{\"first_name\":\"Ada\"},{\"contact_id\":\"cnt_B\"}]}");
    }

    [Fact]
    public async Task Add_contacts_reads_standard_input()
    {
        using var cli = new CliRun(standardInput: "[{\"contact_id\":\"cnt_B\"}]");

        var exit = await cli.RunAsync("campaign", "add-contacts", "cmp_A", "--file", "-");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.CampaignAddContacts, "{\"campaign_id\":\"cmp_A\",\"contacts\":[{\"contact_id\":\"cnt_B\"}]}");
    }

    [Fact]
    public async Task Add_contacts_without_a_file_is_a_usage_error()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", "add-contacts", "cmp_A");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--file", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Remove_contacts_collects_every_contact_option()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", "remove-contacts", "cmp_A", "--contact", "cnt_A", "--contact", "cnt_B", "--reason", "they asked");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.CampaignRemoveContacts,
            "{\"campaign_id\":\"cmp_A\",\"contact_ids\":[\"cnt_A\",\"cnt_B\"],\"reason\":\"they asked\"}");
    }

    [Fact]
    public async Task List_contacts_passes_the_state_filter()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", "list-contacts", "cmp_A", "--state", "excluded", "--limit", "10");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.CampaignListContacts, "{\"campaign_id\":\"cmp_A\",\"state\":\"excluded\",\"limit\":10}");
    }

    [Fact]
    public async Task An_unknown_option_is_a_usage_error()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", "get", "cmp_A", "--bogus");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--bogus", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_reserved_actor_is_refused_on_a_read_only_verb()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", "get", "cmp_A", "--actor", "system");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--actor", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_renders_one_campaign()
    {
        var campaign = JsonSerializer.Serialize(
            new CampaignDto("cmp_A", "LatAm", CampaignStatus.Active, new JsonObject { ["icp"] = "founders", ["tone"] = "plain" }, [], Moment, Moment, null),
            JasonJson.Options);
        using var cli = new CliRun(campaign);

        var exit = await cli.RunAsync("campaign", "get", "cmp_A", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("cmp_A", cli.Text, StringComparison.Ordinal);
        Assert.Contains("LatAm", cli.Text, StringComparison.Ordinal);
        Assert.Contains("active", cli.Text, StringComparison.Ordinal);
        Assert.Contains("icp, tone", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_renders_a_campaign_listing_with_its_cursor()
    {
        var page = JsonSerializer.Serialize(
            new Page<CampaignSummaryDto>([new CampaignSummaryDto("cmp_A", "LatAm", CampaignStatus.Draft, Moment, Moment, null)], "Y21wX0E"),
            JasonJson.Options);
        using var cli = new CliRun(page);

        var exit = await cli.RunAsync("campaign", "list", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("NAME", cli.Text, StringComparison.Ordinal);
        Assert.Contains("cmp_A", cli.Text, StringComparison.Ordinal);
        Assert.Contains("draft", cli.Text, StringComparison.Ordinal);
        Assert.Contains("next cursor: Y21wX0E", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_renders_the_members_of_a_campaign()
    {
        var contact = new ContactDto(
            "cnt_A",
            "Ada",
            "Lovelace",
            "Analytical Engines",
            "Mathematician",
            "Europe/Kyiv",
            [new ChannelDto("email", "ada@example.com", null, true, null)],
            [],
            [],
            Moment,
            Moment,
            null);
        var page = JsonSerializer.Serialize(
            new Page<MembershipItemDto>([new MembershipItemDto(contact, MembershipState.Enrolled, Moment, Moment)], null),
            JasonJson.Options);
        using var cli = new CliRun(page);

        var exit = await cli.RunAsync("campaign", "list-contacts", "cmp_A", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("cnt_A", cli.Text, StringComparison.Ordinal);
        Assert.Contains("Ada Lovelace", cli.Text, StringComparison.Ordinal);
        Assert.Contains("enrolled", cli.Text, StringComparison.Ordinal);
        Assert.Contains("ada@example.com", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_summarizes_an_import_and_names_every_rejected_item()
    {
        var result = JsonSerializer.Serialize(
            new AddContactsResult(
                "cmp_A",
                new AddContactsSummary(2, 1, 1),
                [
                    new AddContactsItemResult(0, AddContactsItemStatus.Added, "cnt_A", true, MembershipState.Enrolled, null),
                    new AddContactsItemResult(1, AddContactsItemStatus.Rejected, null, false, null, new ErrorBody("ambiguous_match", "Two contacts share that address.", false)),
                ]),
            JasonJson.Options);
        using var file = new TempFile("[]");
        using var cli = new CliRun(result);

        var exit = await cli.RunAsync("campaign", "add-contacts", "cmp_A", "--file", file.Path, "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("added 2, already members 1, rejected 1", cli.Text, StringComparison.Ordinal);
        Assert.Contains("#1: ambiguous_match", cli.Text, StringComparison.Ordinal);
        Assert.Contains("Two contacts share that address.", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_summarizes_a_removal()
    {
        var result = JsonSerializer.Serialize(
            new RemoveContactsResult(
                "cmp_A",
                new RemoveContactsSummary(1, 1, 0, 0),
                [
                    new RemoveContactsItemResult(0, RemoveContactsItemStatus.Removed, "cnt_A", null),
                    new RemoveContactsItemResult(1, RemoveContactsItemStatus.NotMember, "cnt_B", null),
                ]),
            JasonJson.Options);
        using var cli = new CliRun(result);

        var exit = await cli.RunAsync("campaign", "remove-contacts", "cmp_A", "--contact", "cnt_A", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("removed 1", cli.Text, StringComparison.Ordinal);
        Assert.Contains("not members 1", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_falls_back_to_the_raw_body_when_the_shape_is_unknown()
    {
        using var cli = new CliRun("{\"unexpected\":true}");

        var exit = await cli.RunAsync("campaign", "get", "cmp_A", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("{\"unexpected\":true}", cli.Text, StringComparison.Ordinal);
    }
}

/// <summary>
/// One CLI invocation against a runtime that is not there: a descriptor pointing at a fake handler, which
/// captures the request the verb composed and answers with the body the test chose.
/// </summary>
internal sealed class CliRun : IDisposable
{
    private static readonly RuntimeDescriptor Live = new("v1", "0.1.0-dev", "rt_LIVE", 77, "http://127.0.0.1:5000", "the-token", DateTimeOffset.UnixEpoch);

    private readonly TempPaths _paths = new();
    private readonly CliEnvironment _environment;

    public CliRun(string response = "{}", string? standardInput = null, HttpStatusCode status = HttpStatusCode.OK, bool descriptor = true)
    {
        if (descriptor)
        {
            _paths.WriteDescriptor(Live);
        }

        _environment = new CliEnvironment(
            Output,
            Error,
            _paths.Paths,
            new FakeHandler(request =>
            {
                Url = request.RequestUri!.ToString();
                Sent = request.Content!.ReadAsStringAsync(CancellationToken.None).GetAwaiter().GetResult();
                return new HttpResponseMessage(status) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
            }),
            standardInput is null ? null : new StringReader(standardInput));
    }

    public StringWriter Output { get; } = new();

    public StringWriter Error { get; } = new();

    public string? Url { get; private set; }

    public string? Sent { get; private set; }

    public string Text => Output.ToString();

    public Task<int> RunAsync(params string[] args) => CliApp.RunAsync(args, _environment, TestContext.Current.CancellationToken);

    /// <summary>The verb reached the right operation with the right body; property order is not part of the contract.</summary>
    public void AssertPosted(string operation, string expectedBody)
    {
        Assert.Equal("http://127.0.0.1:5000/v1/" + operation, Url);
        Assert.NotNull(Sent);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expectedBody), JsonNode.Parse(Sent)), $"The body was {Sent}");
    }

    public void Dispose() => _paths.Dispose();
}
