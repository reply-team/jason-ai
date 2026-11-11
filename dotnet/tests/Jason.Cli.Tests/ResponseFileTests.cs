using System.Text.Json.Nodes;
using Jason.Cli.Tests.Commands;
using Jason.Contracts.Api;

namespace Jason.Cli.Tests;

/// <summary>
/// The words a command runs are the words on its command line. A response file, <c>jason @file</c>, would let a program
/// that is shown or fenced by those words run others it read from a file, so the CLI reads none: <c>@file</c> is a word
/// like any other, and no verb takes it. Options may stand between a noun and its verb, so a fence that reads the words
/// reads them in order, with options between.
/// </summary>
public class ResponseFileTests
{
    [Fact]
    public async Task A_response_file_is_not_read_and_is_a_usage_error()
    {
        using var dir = new TempPaths();
        var file = Path.Combine(dir.Paths.Root, "words.rsp");
        File.WriteAllText(file, "campaign list\n");
        using var cli = new CliRun();

        var exit = await cli.RunAsync("@" + file);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Equal(0, cli.Requests);
    }

    [Fact]
    public async Task An_option_value_beginning_with_an_at_sign_reaches_the_request_as_typed()
    {
        using var dir = new TempPaths();
        var file = Path.Combine(dir.Paths.Root, "name.txt");
        File.WriteAllText(file, "read from a file\n");
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", "update", "cmp_A", "--name", "@" + file, "--reason", "@team");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.CampaignUpdate,
            new JsonObject { ["campaign_id"] = "cmp_A", ["name"] = "@" + file, ["reason"] = "@team" }.ToJsonString());
    }

    [Fact]
    public async Task An_option_between_the_noun_and_the_verb_still_reaches_the_verb()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("approval", "--actor", "human:x", "approve", "apr_A");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("http://127.0.0.1:5000/v1/approval.approve", cli.Url);
    }
}
