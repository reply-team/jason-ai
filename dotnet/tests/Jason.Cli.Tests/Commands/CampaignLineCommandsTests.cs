using System.Text.Json;
using Jason.Cli;
using Jason.Contracts.Api;
using Jason.Contracts.Json;

namespace Jason.Cli.Tests.Commands;

/// <summary>
/// The three verbs of a person's line in a campaign, one operation each: <c>finish-contacts</c> ends it with a reason,
/// <c>continue-contacts</c> enrols a paused person again, and <c>reopen-contacts</c> enrols a finished one again on the
/// person's decision, which names who decided and, from a run, the question they answered.
/// </summary>
public class CampaignLineCommandsTests
{
    [Fact]
    public async Task Finish_contacts_sends_every_contact_and_the_reason()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", "finish-contacts", "cmp_A", "--contact", "cnt_A", "--contact", "cnt_B", "--reason", "no answer after four touches");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.CampaignFinishContacts,
            "{\"campaign_id\":\"cmp_A\",\"contact_ids\":[\"cnt_A\",\"cnt_B\"],\"reason\":\"no answer after four touches\"}");
    }

    [Fact]
    public async Task Continue_contacts_sends_every_contact()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", "continue-contacts", "cmp_A", "--contact", "cnt_A", "--reason", "the reply was answered");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.CampaignContinueContacts,
            "{\"campaign_id\":\"cmp_A\",\"contact_ids\":[\"cnt_A\"],\"reason\":\"the reply was answered\"}");
    }

    [Fact]
    public async Task Reopen_contacts_sends_the_person_who_decided()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("--actor", "human:Marta", "campaign", "reopen-contacts", "cmp_A", "--contact", "cnt_A", "--reason", "she wrote again");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.CampaignReopenContacts,
            "{\"campaign_id\":\"cmp_A\",\"contact_ids\":[\"cnt_A\"],\"reason\":\"she wrote again\",\"actor\":{\"type\":\"human\",\"id\":\"Marta\"}}");
    }

    [Fact]
    public async Task Reopen_contacts_from_a_run_names_the_answered_question()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("--actor", "attempt:att_A", "campaign", "reopen-contacts", "cmp_A", "--contact", "cnt_A", "--decision", "dec_A");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.CampaignReopenContacts,
            "{\"campaign_id\":\"cmp_A\",\"contact_ids\":[\"cnt_A\"],\"decision_id\":\"dec_A\",\"actor\":{\"type\":\"attempt\",\"id\":\"att_A\"}}");
    }

    /// <summary>Each verb's help says what it does to the line, and reopening says whose decision it is and how a run carries it.</summary>
    [Theory]
    [InlineData("finish-contacts", "End the line")]
    [InlineData("finish-contacts", "--reason is required")]
    [InlineData("continue-contacts", "whose line in a campaign is paused")]
    [InlineData("reopen-contacts", "the person's decision")]
    [InlineData("reopen-contacts", "--decision")]
    [InlineData("reopen-contacts", ActorOption.NoDefault)]
    public async Task Each_verb_says_in_its_help_what_it_does_to_the_line(string verb, string said)
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", verb, "--help");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains(said, string.Join(' ', cli.Text.Split((char[])['\r', '\n', ' '], StringSplitOptions.RemoveEmptyEntries)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_counts_a_finish_the_way_the_api_counts_it()
    {
        var result = JsonSerializer.Serialize(
            new FinishContactsResult(
                "cmp_A",
                new FinishContactsSummary(1, 1, 0, 0, 1),
                [
                    new FinishContactsItemResult(0, FinishContactsItemStatus.Finished, "cnt_A", null),
                    new FinishContactsItemResult(1, FinishContactsItemStatus.AlreadyFinished, "cnt_B", null),
                    new FinishContactsItemResult(2, FinishContactsItemStatus.Rejected, "cnt_C", new ErrorBody("contact_not_found", "No contact 'cnt_C'.", false)),
                ]),
            JasonJson.Options);
        using var cli = new CliRun(result);

        var exit = await cli.RunAsync("campaign", "finish-contacts", "cmp_A", "--contact", "cnt_A", "--reason", "done", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("finished 1, already finished 1, not members 0, excluded 0, rejected 1", cli.Text, StringComparison.Ordinal);
        Assert.Contains("#2: contact_not_found", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_counts_a_continue_and_a_reopen_the_way_the_api_counts_them()
    {
        var continued = JsonSerializer.Serialize(
            new ContinueContactsResult("cmp_A", new ContinueContactsSummary(1, 1, 0, 0, 0), []),
            JasonJson.Options);
        using (var cli = new CliRun(continued))
        {
            Assert.Equal(ExitCodes.Success, await cli.RunAsync("campaign", "continue-contacts", "cmp_A", "--contact", "cnt_A", "--human"));
            Assert.Contains("continued 1, not paused 1, not members 0, excluded 0, rejected 0", cli.Text, StringComparison.Ordinal);
        }

        var reopened = JsonSerializer.Serialize(
            new ReopenContactsResult("cmp_A", new ReopenContactsSummary(1, 0, 0, 0, 1, 0), []),
            JasonJson.Options);
        using (var cli = new CliRun(reopened))
        {
            Assert.Equal(ExitCodes.Success, await cli.RunAsync("--actor", "human:Marta", "campaign", "reopen-contacts", "cmp_A", "--contact", "cnt_A", "--human"));
            Assert.Contains("reopened 1, not finished 0, not members 0, excluded 0, not in the decision 1, rejected 0", cli.Text, StringComparison.Ordinal);
        }
    }
}
