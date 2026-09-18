using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli.Tests.Commands;
using Jason.Contracts.Api;
using Jason.Contracts.Json;

namespace Jason.Cli.Tests.Human;

/// <summary>
/// What a person sees of the identifiers a provider gave us. A pin is one line; a disagreement shows both values
/// side by side, because the whole point of keeping the divergence is that somebody can look at it.
/// </summary>
public class ExternalIdRenderingTests
{
    private static readonly DateTimeOffset Moment = new(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_contact_shows_the_identifiers_each_plugin_knows_it_by()
    {
        using var cli = new CliRun(Contact([
            new ExternalIdDto("fake-provider", "contact", "prov-1", Moment, "att_A"),
            new ExternalIdDto("other-provider", "contact", "oth-9", Moment, "att_A"),
        ]));

        var exit = await cli.RunAsync("contact", "get", "cnt_A", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("EXTERNAL IDS", cli.Text, StringComparison.Ordinal);
        Assert.Contains("fake-provider", cli.Text, StringComparison.Ordinal);
        Assert.Contains("prov-1", cli.Text, StringComparison.Ordinal);
        Assert.Contains("oth-9", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_diverged_identifier_shows_the_pin_and_what_the_provider_said_instead()
    {
        using var cli = new CliRun(Contact([
            new ExternalIdDto("fake-provider", "contact", "prov-1", Moment, "att_A", "prov-2", Moment, "att_B"),
        ]));

        var exit = await cli.RunAsync("contact", "get", "cnt_A", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("prov-1", cli.Text, StringComparison.Ordinal);
        Assert.Contains("prov-2", cli.Text, StringComparison.Ordinal);
        Assert.Contains("att_B", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_contact_nobody_has_named_yet_says_nothing_about_identifiers()
    {
        using var cli = new CliRun(Contact([]));

        var exit = await cli.RunAsync("contact", "get", "cnt_A", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.DoesNotContain("EXTERNAL IDS", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_campaign_shows_what_a_provider_calls_it()
    {
        var campaign = JsonSerializer.Serialize(
            new CampaignDto(
                "cmp_A",
                "LatAm",
                CampaignStatus.Active,
                new JsonObject { ["icp"] = "founders" },
                [new ExternalIdDto("fake-provider", "campaign", "seq-42", Moment, "att_A")],
                null,
                Moment,
                Moment,
                null),
            JasonJson.Options);
        using var cli = new CliRun(campaign);

        var exit = await cli.RunAsync("campaign", "get", "cmp_A", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("EXTERNAL IDS", cli.Text, StringComparison.Ordinal);
        Assert.Contains("seq-42", cli.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", cli.Text, StringComparison.Ordinal);
    }

    private static string Contact(IReadOnlyList<ExternalIdDto> externalIds) =>
        JsonSerializer.Serialize(
            new ContactDto(
                "cnt_A",
                "Ada",
                "Lovelace",
                null,
                null,
                null,
                [new ChannelDto("email", "ada@example.com", null, true, null)],
                [],
                externalIds,
                Moment,
                Moment,
                null),
            JasonJson.Options);
}
