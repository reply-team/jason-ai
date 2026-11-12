using Jason.Cli;
using Jason.Contracts.Api;

namespace Jason.Cli.Tests.Commands;

public class OperationCommandsTests
{
    [Fact]
    public async Task Describe_sends_the_campaign_and_the_operation_and_prints_both_schemas()
    {
        const string description = """
            {"operation":"audience.search","version":1,"plugin_id":"reply","plugin_version":"1.4.0",
             "arguments":{"type":"object","properties":{"count":{"type":"integer"}}},
             "definition":{"schema":{"type":"object","properties":{"industries":{"description":"Industries the company is in."}}},"keys":{}}}
            """;
        using var cli = new CliRun(description);

        var exit = await cli.RunAsync("operation", "describe", "audience.search", "--campaign", "cmp_A", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.OperationDescribe, "{\"campaign_id\":\"cmp_A\",\"operation\":\"audience.search\"}");
        Assert.Contains("\"count\"", cli.Text, StringComparison.Ordinal);
        Assert.Contains("Industries the company is in.", cli.Text, StringComparison.Ordinal);
    }
}
