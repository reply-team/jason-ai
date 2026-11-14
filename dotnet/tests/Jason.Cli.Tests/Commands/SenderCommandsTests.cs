using Jason.Cli;
using Jason.Cli.Http;
using Jason.Contracts.Api;
using Jason.Contracts.Operations;

namespace Jason.Cli.Tests.Commands;

public class SenderCommandsTests
{
    private const string Page = """
        {"plugin_id":"reply","items":[
          {"external_id":"e_1","channel":"email","name":"Sales desk","address":"sales@vendor.example","status":"active","owner":"own","vendor":{"status":"connected"}},
          {"external_id":"l_7","channel":"linkedin","name":"Lucia Ferraro","address":"https://www.linkedin.example/in/lucia","status":"disconnected","owner":"teammate","vendor":{"status":null}}],
         "has_more":true,"channels":{"email":"listed","linkedin":"listed"},"vendor":{"ordering":"email_then_linkedin"}}
        """;

    /// <summary>
    /// The verb asks the runtime one read: the campaign, whose accounts, the channel as <c>sender_channel</c> and the page;
    /// <c>--human</c> prints the plugin that answered and one account a row, and says when the provider holds more.
    /// </summary>
    [Fact]
    public async Task List_sends_the_campaign_the_owners_the_channel_and_the_page_and_prints_the_accounts()
    {
        using var cli = new CliRun(Page);

        var exit = await cli.RunAsync(
            "sender", "list", "--campaign", "cmp_A", "--owners", "own", "--channel", "email", "--limit", "100", "--offset", "0", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.SenderList, "{\"campaign_id\":\"cmp_A\",\"owners\":\"own\",\"sender_channel\":\"email\",\"limit\":100,\"offset\":0}");
        Assert.Contains("reply", cli.Text, StringComparison.Ordinal);
        Assert.Contains("sales@vendor.example", cli.Text, StringComparison.Ordinal);
        Assert.Contains("disconnected", cli.Text, StringComparison.Ordinal);
        Assert.Contains("teammate", cli.Text, StringComparison.Ordinal);
        Assert.Contains("More:", cli.Text, StringComparison.Ordinal);
    }

    /// <summary>Without a channel the read names none, and the runtime routes it by the operation alone.</summary>
    [Fact]
    public async Task List_without_a_channel_names_none()
    {
        using var cli = new CliRun(Page);

        var exit = await cli.RunAsync("sender", "list", "--campaign", "cmp_A", "--owners", "team");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.SenderList, "{\"campaign_id\":\"cmp_A\",\"owners\":\"team\"}");
    }

    /// <summary>Whose accounts and which campaign are never defaults: a run that leaves either out asks the runtime nothing.</summary>
    [Theory]
    [InlineData("--campaign", "cmp_A")]
    [InlineData("--owners", "own")]
    public async Task List_without_the_owners_or_the_campaign_is_a_usage_error(string option, string value)
    {
        using var cli = new CliRun(Page);

        var exit = await cli.RunAsync("sender", "list", option, value);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Null(cli.Sent);
    }

    /// <summary>The read is a catalog operation, so the CLI waits its own budget and a margin, not an ordinary request's.</summary>
    [Fact]
    public void The_read_is_waited_for_its_own_budget()
    {
        Assert.Equal(
            TimeSpan.FromMilliseconds(OperationCatalog.Find(Operations.SenderList)!.TimeoutMs) + RuntimeClient.Margin,
            RuntimeClient.Budget(Operations.SenderList));
    }
}
