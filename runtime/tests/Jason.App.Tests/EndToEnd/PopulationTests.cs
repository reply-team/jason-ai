using System.Text.Json.Nodes;
using Jason.Contracts.Json;

namespace Jason.App.Tests.EndToEnd;

/// <summary>
/// The population a person approves is the population that is started, or nothing is. Out of process, through the
/// shipped executable, the Reply package and the stand-in vendor CLI: an activation is parked naming how many people
/// it will reach, somebody is enrolled before anyone decides, the approved work starts nothing and says both numbers,
/// and the new decision is a new work item, parked again with the new number.
/// </summary>
public class PopulationTests
{
    private const int Sequence = 7;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_population_that_moved_after_the_approval_starts_nothing_and_a_new_item_names_the_new_number()
    {
        using var it = GoldenPath.Create("population");
        it.Account.WithSequence(Sequence, "Q3 LatAm founders", "new", false, 11)
            .WithContact(2001, "first@example.test", "First").WithEnrollment(Sequence, 2001)
            .WithContact(2002, "second@example.test", "Second").WithEnrollment(Sequence, 2002);

        await GoldenPath.WriteSettingsAsync(it, new SettingsShape());
        await GoldenPath.StartAsync(it);
        GoldenPath.InstallReplyPackage(it);
        await GoldenPath.ReloadAsync(it, "installed the reply plugin", routed: true);

        var campaign = (string)GoldenPath.Json(await GoldenPath.Ok(
            GoldenPath.JasonAsync(it, "campaign", "create", "--name", "Q3 LatAm founders")))["id"]!;
        GoldenPath.AssertSuccess(await GoldenPath.JasonAsync(it, "campaign", "start", campaign));

        // 1. The planner read two people and asks to start the campaign for them. A person is shown that number.
        var first = await ActivateAsync(it, campaign, enrolled: 2);
        await GoldenPath.PollAsync(it, first, read => (string?)read["status"] == "awaiting_approval");
        var approval = Assert.Single(GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(it, "approval", "list")))["items"]!.AsArray())!;
        var shown = GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(it, "approval", "get", (string)approval["id"]!)));
        Assert.Contains("People enrolled there: 2.", (string?)shown["preview"]!["summary"], StringComparison.Ordinal);

        // 2. Somebody else is enrolled at the provider before anybody decides.
        it.Account.WithContact(2003, "third@example.test", "Third").WithEnrollment(Sequence, 2003);

        // 3. The person approves the population they were shown, and the plugin holds that approval to it.
        GoldenPath.AssertSuccess(await GoldenPath.JasonAsync(
            it, "approval", "approve", (string)approval["id"]!, "--actor", "human:ada", "--reason", "two people, as planned"));
        var ended = await GoldenPath.PollAsync(it, first, read => GoldenPath.Finished((string?)read["status"]));
        Assert.Equal("failed", (string?)ended["status"]);
        var error = ended["attempts"]!.AsArray()[^1]!["error"]!;
        Assert.Equal("population_changed", (string?)error["code"]);
        // The plugin's own details travel as the attempt's trace, where a person reads both numbers.
        var details = JsonNode.Parse((string)error["trace"]!)!;
        Assert.Equal(2, (int)details["expected"]!);
        Assert.Equal(3, (int)details["actual"]!);
        Assert.Equal("new", (string?)it.Account.Sequences.Single()!["status"]);
        Assert.DoesNotContain(it.Account.Calls, call => call.Path.EndsWith("/start", StringComparison.Ordinal));

        // 4. The new population is a new decision: a new work item, parked again, with a summary that names it.
        var second = await ActivateAsync(it, campaign, enrolled: 3);
        await GoldenPath.PollAsync(it, second, read => (string?)read["status"] == "awaiting_approval");
        var waiting = GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(it, "approval", "list")))["items"]!.AsArray()
            .Single(entry => (string?)entry!["work_item_id"] == second)!;
        Assert.Equal("approval_required", (string?)waiting["reason"]);
        var renamed = GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(it, "approval", "get", (string)waiting["id"]!)));
        Assert.Contains("People enrolled there: 3.", (string?)renamed["preview"]!["summary"], StringComparison.Ordinal);
        Assert.Contains("is not 3 at execution", (string?)renamed["preview"]!["summary"], StringComparison.Ordinal);

        await GoldenPath.StopAsync(it);
    }

    private static async Task<string> ActivateAsync(Installation it, string campaign, int enrolled)
    {
        var input = new JsonObject
        {
            ["campaign"] = new JsonObject { ["external_id"] = Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            ["expected"] = new JsonObject { ["enrolled"] = enrolled },
        };

        return (string)GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(
            it,
            "workitem",
            "create",
            campaign,
            "--kind",
            "provider_op",
            "--operation",
            "campaign.activate",
            "--input",
            input.ToJsonString(JasonJson.Options))))["id"]!;
    }
}
