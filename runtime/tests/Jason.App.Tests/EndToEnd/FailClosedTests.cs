using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Contracts.Json;

namespace Jason.App.Tests.EndToEnd;

/// <summary>
/// What happens when the thing that says where work goes is taken away. Nothing is guessed and nothing falls
/// back: the work stops with the code that says why, no child process is started, the provider is never asked,
/// and an operator can read the reason from the work item and from the route in the same words.
/// </summary>
/// <remarks>
/// The two halves run on one installation because they are one story — first the route itself is gone, then an
/// edit arrives that the plugin's own binding schema refuses — and because a proof of blocking should not need
/// more runtimes than the thing it is proving. The work used here is <c>campaign.get</c>: a read, so what is
/// being proven is the block rather than anything the block prevented.
/// </remarks>
public class FailClosedTests
{
    private const int Sequence = 7;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Work_whose_route_or_binding_was_taken_away_stops_where_an_operator_can_see_it()
    {
        using var it = GoldenPath.Create("fail-closed");
        it.Account.WithSequence(Sequence, "Q3 LatAm founders", "active", false, 11);

        // The route is taken away before anything runs, which is the same state as an operator removing it: the
        // package is installed and loaded, and nothing says where its work should go.
        await GoldenPath.WriteSettingsAsync(it, new SettingsShape(Routed: false));
        await GoldenPath.StartAsync(it);
        GoldenPath.InstallReplyPackage(it);
        var loaded = await GoldenPath.ReloadAsync(it, "installed the reply plugin", routed: false);
        GoldenPath.AssertTheStandInAnswered(Assert.Single(loaded["plugins"]!.AsArray())!.AsObject());
        var unrouted = (string)loaded["routing_snapshot_id"]!;

        var campaign = (string)GoldenPath.Json(await GoldenPath.Ok(
            GoldenPath.JasonAsync(it, "campaign", "create", "--name", "Q3 LatAm founders")))["id"]!;
        GoldenPath.AssertSuccess(await GoldenPath.JasonAsync(it, "campaign", "start", campaign));

        // 1. The work cannot be performed, and says so in one word an operator can look up.
        var stranded = await ReadAsync(it, campaign);
        var blocked = await GoldenPath.PollAsync(it, stranded, item => GoldenPath.Finished((string?)item["status"]));
        Assert.Equal("failed", (string?)blocked["status"]);
        var refusal = Assert.Single(blocked["attempts"]!.AsArray())!;
        Assert.Equal("no_route", (string?)refusal["error"]!["code"]);
        Assert.False((bool)refusal["error"]!["retriable"]!);

        // The decision was made before any child existed: the attempt is kept, it names no plugin, and the
        // provider was never asked anything at all.
        Assert.Null((string?)refusal["provenance"]!["plugin_id"]);
        Assert.Equal("campaign.get", (string?)refusal["provenance"]!["operation"]);
        Assert.Equal(unrouted, (string?)refusal["provenance"]!["routing_snapshot_id"]);
        Assert.Null(refusal["launch"]);
        Assert.Empty(it.Account.Calls);

        // 2. Nothing fell back. The package is loaded, valid and perfectly able to perform this operation — it
        //    simply was not asked, because being the only plugin installed is not the same as being the route.
        var listed = GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(it, "plugin", "list")));
        var plugin = Assert.Single(listed["plugins"]!.AsArray())!;
        Assert.Equal("valid", (string?)plugin["status"]);
        Assert.Contains("campaign.get", plugin["operations"]!.AsArray().Select(operation => (string?)operation));

        // And the same answer from the other side, in the same word, before any work is created: asking where
        // this operation would go is refused with the code the attempt carries, and the message says where a
        // route is written rather than leaving an operator to guess.
        var asked = await GoldenPath.JasonAsync(
            it, "route", "resolve", "--campaign", campaign, "--operation", "campaign.get");
        Assert.Equal(ExitCodes.ApiError, asked.ExitCode);
        var problem = GoldenPath.Json(asked)["error"]!;
        Assert.Equal("no_route", (string?)problem["code"]);
        Assert.False((bool)problem["retryable"]!);
        Assert.Contains("route.set", (string?)problem["message"], StringComparison.Ordinal);

        // 3. Put the route back and the same work runs, which is what makes the block above a block rather than
        //    a broken installation.
        await GoldenPath.WriteSettingsAsync(it, new SettingsShape(Routed: true));
        var routed = await GoldenPath.ReloadAsync(it, "routed provider work to the reply plugin", routed: true);
        var working = (string)routed["routing_snapshot_id"]!;
        Assert.NotEqual(unrouted, working);
        GoldenPath.AssertSucceeded(await GoldenPath.PollAsync(
            it, await ReadAsync(it, campaign), item => GoldenPath.Finished((string?)item["status"])));

        // 4. Now the other half: a binding the plugin's own schema refuses. The package declares what a route to
        //    it must carry, and this one carries a property that declaration does not allow.
        await File.WriteAllTextAsync(it.Paths.UserSettingsFile, BadBinding(it), Ct);
        var (refused, live) = await GoldenPath.ReloadUntilRefusedAsync(it, "edited the binding");

        // The whole edit is refused rather than half-applied, and the refusal names the route an operator has
        // to go and fix, in the words they wrote it in.
        Assert.Equal(ExitCodes.ApiError, refused.ExitCode);
        var error = GoldenPath.Json(refused)["error"]!;
        Assert.Equal("plugin_reload_rejected", (string?)error["code"]);
        var detail = Assert.Single(error["details"]!.AsArray())!;
        Assert.Equal("Routes:Default", (string?)detail["field"]);
        Assert.Equal("route_binding_invalid", (string?)detail["code"]);

        // And the runtime keeps what it had: a bad line in a settings file is not a reason to stop performing
        // the work that was already routed, and the registry still says which snapshot is the live one.
        var kept = GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(it, "plugin", "list")));
        Assert.Equal(live, (string?)kept["routing_snapshot_id"]);
        Assert.False((bool)kept["activated"]!);
        var rejected = Assert.Single(kept["last_reload"]!["routes"]!.AsArray())!;
        Assert.Equal("Routes:Default", (string?)rejected["route"]);
        Assert.Equal("route_binding_invalid", (string?)rejected["code"]);

        var mark = it.Account.Mark();
        GoldenPath.AssertSucceeded(await GoldenPath.PollAsync(
            it, await ReadAsync(it, campaign), item => GoldenPath.Finished((string?)item["status"])));
        Assert.NotEmpty(it.Account.CallsSince(mark));

        await GoldenPath.StopAsync(it);
    }

    /// <summary>One read of the provider's own campaign: work that needs no person's word and writes nothing.</summary>
    private static async Task<string> ReadAsync(Installation it, string campaign) =>
        (string)GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(
            it,
            "workitem",
            "create",
            campaign,
            "--kind",
            "provider_op",
            "--operation",
            "campaign.get",
            "--input",
            new JsonObject
            {
                ["campaign"] = new JsonObject { ["external_id"] = Sequence.ToString(CultureInfo.InvariantCulture) },
            }.ToJsonString(JasonJson.Options))))["id"]!;

    /// <summary>
    /// The same settings file with one thing wrong in it: a binding property the package's own schema does not
    /// allow. The package says a route to it carries a profile and a team, and nothing else.
    /// </summary>
    private static string BadBinding(Installation it)
    {
        var settings = JsonNode.Parse(GoldenPath.Settings(it, new SettingsShape(Routed: true)))!.AsObject();
        settings["Routes"]!["Default"]!["Binding"]!["nonsense"] = true;
        return settings.ToJsonString(JasonJson.Options);
    }
}
