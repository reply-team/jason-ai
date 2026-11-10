using Jason.Cli;
using Jason.Contracts.Api;

namespace Jason.Cli.Tests.Commands;

public class AudienceCommandsTests
{
    [Fact]
    public async Task Preview_sends_the_campaign_and_the_definition_as_an_object_and_prints_the_estimate_and_the_sample()
    {
        const string preview = """
            {"plugin_id":"reply","count":1840,"companies":412,"sample":[{"external_id":null,"first_name":"Lucia","last_name":"Ferraro",
             "title":"Head of Sales","company":{"name":"Northwind","domain":"northwind.example","industry":null,"size":null},"location":"Lisbon","raw":null}]}
            """;
        using var cli = new CliRun(preview);

        var exit = await cli.RunAsync(
            "audience", "preview", "--campaign", "cmp_A", "--definition", "{\"industries\":[\"Software\"]}", "--people-per-company", "2", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.AudiencePreview, "{\"campaign_id\":\"cmp_A\",\"definition\":{\"industries\":[\"Software\"]},\"people_per_company\":2}");
        Assert.Contains("1840", cli.Text, StringComparison.Ordinal);
        Assert.Contains("Lucia Ferraro", cli.Text, StringComparison.Ordinal);
        Assert.Contains("Northwind", cli.Text, StringComparison.Ordinal);
    }

    /// <summary>An audience is named one way, by its definition or by its number, and a run that names it both ways or neither asks the runtime nothing.</summary>
    [Theory]
    [InlineData("--definition", "{\"industries\":[\"Software\"]}", "--audience", "3")]
    [InlineData("--people-per-company", "2", "--human", null)]
    public async Task A_preview_that_names_the_audience_both_ways_or_neither_is_a_usage_error(string first, string firstValue, string second, string? secondValue)
    {
        using var cli = new CliRun();
        string[] named = secondValue is null ? [first, firstValue, second] : [first, firstValue, second, secondValue];

        var exit = await cli.RunAsync(["audience", "preview", "--campaign", "cmp_A", .. named]);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Null(cli.Sent);
    }

    [Fact]
    public async Task Filter_values_sends_the_filter_the_text_and_the_count_and_prints_the_values()
    {
        using var cli = new CliRun("""{"plugin_id":"reply","values":["Software","Software Development"]}""");

        var exit = await cli.RunAsync("audience", "filter-values", "--campaign", "cmp_A", "--filter", "industries", "--query", "Soft", "--limit", "5", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.AudienceFilterValues, "{\"campaign_id\":\"cmp_A\",\"filter\":\"industries\",\"query\":\"Soft\",\"limit\":5}");
        Assert.Contains("Software Development", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Availability_sends_the_campaign_and_prints_why_the_account_may_not_search()
    {
        using var cli = new CliRun("""{"plugin_id":"reply","available":false,"reason":"no_credits","detail":null,"credits":null,"headroom":null}""");

        var exit = await cli.RunAsync("audience", "availability", "--campaign", "cmp_A", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.AudienceAvailability, "{\"campaign_id\":\"cmp_A\"}");
        Assert.Contains("no_credits", cli.Text, StringComparison.Ordinal);
    }
}
