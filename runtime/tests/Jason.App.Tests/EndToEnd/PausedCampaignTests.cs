using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Json;

namespace Jason.App.Tests.EndToEnd;

/// <summary>
/// Pausing a campaign holds its work, except the work whose not running is the harm. Out of process, through the
/// shipped executable, the Reply package and the stand-in vendor CLI: Jason's campaign is paused first, and the
/// provider's own pause and the record that somebody said stop are still claimed, run and answered, with nobody asked
/// — while a read created before them in the same campaign never runs at all.
/// </summary>
public class PausedCampaignTests
{
    private const int Sequence = 7;
    private const int Person = 3001;
    private const string Address = "ada@example.test";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task In_a_paused_campaign_the_provider_pause_and_the_record_of_a_stop_run_and_a_read_waits()
    {
        using var it = GoldenPath.Create("paused");
        it.Account.WithSequence(Sequence, "Q3 LatAm founders", "active", false, 11)
            .WithContact(Person, Address, "Ada").WithEnrollment(Sequence, Person);

        await GoldenPath.WriteSettingsAsync(it, new SettingsShape());
        await GoldenPath.StartAsync(it);
        GoldenPath.InstallReplyPackage(it);
        await GoldenPath.ReloadAsync(it, "installed the reply plugin", routed: true);

        var campaign = (string)GoldenPath.Json(await GoldenPath.Ok(
            GoldenPath.JasonAsync(it, "campaign", "create", "--name", "Q3 LatAm founders")))["id"]!;
        var contactsFile = Path.Combine(it.Root, "contacts.json");
        await File.WriteAllTextAsync(
            contactsFile,
            new JsonArray(new JsonObject
            {
                ["first_name"] = "Ada",
                ["channels"] = new JsonArray(new JsonObject { ["channel"] = "email", ["value"] = Address }),
            }).ToJsonString(JasonJson.Options),
            Ct);
        GoldenPath.AssertSuccess(await GoldenPath.JasonAsync(it, "campaign", "add-contacts", campaign, "--file", contactsFile, "--match-by", "email"));
        var contact = (string)Assert.Single(GoldenPath.Json(await GoldenPath.Ok(
            GoldenPath.JasonAsync(it, "campaign", "list-contacts", campaign)))["items"]!.AsArray())!["contact"]!["id"]!;

        // 1. Jason's campaign is started, then paused first, as an incident response may do it.
        GoldenPath.AssertSuccess(await GoldenPath.JasonAsync(it, "campaign", "start", campaign));
        GoldenPath.AssertSuccess(await GoldenPath.JasonAsync(it, "campaign", "pause", campaign));

        // 2. A read is created first: were the pause ignored, it would be the first thing claimed.
        var read = await CreateAsync(it, campaign, "campaign.get", null, new JsonObject { ["campaign"] = Linked() });

        // 3. The provider's own pause runs in the paused campaign, and the provider's sequence stops.
        var pause = await CreateAsync(it, campaign, "campaign.pause", null, new JsonObject
        {
            ["campaign"] = Linked(),
            ["reason"] = new JsonObject { ["code"] = "incident" },
        });
        var paused = await GoldenPath.PollAsync(it, pause, item => GoldenPath.Finished((string?)item["status"]));
        GoldenPath.AssertSucceeded(paused);
        Assert.Equal("paused", (string?)paused["result"]!["items"]![0]!["status"]);
        Assert.Equal("paused", (string?)it.Account.Sequences.Single()!["status"]);

        // 4. So does the record that somebody said stop, and the provider holds them as opted out.
        var optout = await CreateAsync(it, campaign, "optout.record", contact, new JsonObject
        {
            ["channel"] = "email",
            ["statement"] = "please take me off your list",
            ["arrived_via"] = "reply",
            ["said_at"] = "2026-10-03T08:14:00Z",
            ["learned_at"] = "2026-10-03T08:20:00Z",
        });
        var recorded = await GoldenPath.PollAsync(it, optout, item => GoldenPath.Finished((string?)item["status"]));
        GoldenPath.AssertSucceeded(recorded);
        Assert.Equal("recorded", (string?)recorded["result"]!["items"]![0]!["status"]);
        Assert.Contains(it.Account.OptOuts, entry => entry!.GetValue<int>() == Person);

        // 5. Nobody was asked about either, and the read is still waiting with its campaign, never attempted.
        Assert.Empty(GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(it, "approval", "list")))["items"]!.AsArray());
        var waiting = GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(it, "workitem", "get", read)));
        Assert.Equal("created", (string?)waiting["status"]);
        Assert.Empty(waiting["attempts"]!.AsArray());

        await GoldenPath.StopAsync(it);
    }

    private static JsonObject Linked() => new() { ["external_id"] = Sequence.ToString(CultureInfo.InvariantCulture) };

    private static async Task<string> CreateAsync(Installation it, string campaign, string operation, string? contact, JsonObject input)
    {
        string[] contactArgs = contact is null ? [] : ["--contact", contact];
        return (string)GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(
            it,
            [
                "workitem", "create", campaign, "--kind", "provider_op", "--operation", operation, .. contactArgs,
                "--input", input.ToJsonString(JasonJson.Options),
            ])))["id"]!;
    }
}
