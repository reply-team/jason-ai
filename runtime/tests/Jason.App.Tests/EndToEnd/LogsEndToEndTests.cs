using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Jason.App.Tests.EndToEnd;

/// <summary>
/// <c>jason logs</c> against the log a real runtime wrote: the shipped program at both ends, the runtime on a temporary
/// data directory and the verb reading the file it left there.
/// </summary>
/// <remarks>
/// The unit tests hold the verb to files a test wrote the way the runtime writes them. Only a real runtime shows that
/// the two agree on the name of the file, on where it is, and on what a line looks like — and that the verb can read the
/// file while the runtime still holds it open for writing, which on Windows is the case a reader that did not share
/// writing would be turned away from.
/// </remarks>
public class LogsEndToEndTests
{
    private const string StartingTemplate = "Jason runtime {Version} starting as {InstanceId} (pid {Pid}) with data directory {DataDir}";

    private const string EndedAtClaimTemplate = "Attempt {AttemptId} of work item {WorkItemId} ended at the claim: {Code}";

    [Fact]
    public async Task The_line_a_runtime_writes_as_it_starts_is_found_by_jason_logs_while_it_runs_and_after_it_has_stopped()
    {
        using var it = GoldenPath.Create("logs");
        var descriptor = await GoldenPath.StartAsync(it);

        // While the runtime is up and holds its log file open for writing.
        var running = GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(it, "logs", "--limit", "10000")));
        Assert.Equal(it.Paths.LogsDirectory, (string?)running["directory"]);
        Assert.True((bool)running["exists"]!);
        var file = Assert.Single(running["files"]!.AsArray())!.AsObject();
        Assert.Matches(new Regex(@"^runtime-[0-9]{8}\.jsonl$", RegexOptions.CultureInvariant), (string?)file["name"]);
        Assert.Equal(0, (int)running["skipped_lines"]!);

        var starting = Starting(running);
        Assert.Equal(descriptor.RuntimeVersion, (string?)starting["Version"]);
        Assert.Equal(descriptor.InstanceId, (string?)starting["InstanceId"]);
        Assert.Equal(descriptor.Pid, (int?)starting["Pid"]);
        Assert.Equal(it.Paths.Root, (string?)starting["DataDir"]);

        // The same line, for a person.
        var human = await GoldenPath.Ok(GoldenPath.JasonAsync(it, "logs", "--human", "--limit", "10000"));
        Assert.Contains(
            $"INF  Jason runtime {descriptor.RuntimeVersion} starting as {descriptor.InstanceId} (pid {descriptor.Pid})",
            human.Output,
            StringComparison.Ordinal);

        // With the runtime gone there is nothing to ask and the answer does not change: that is what reading files is for.
        await GoldenPath.StopAsync(it);
        var stopped = GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(it, "logs", "--limit", "10000")));
        Assert.Equal((string?)starting["InstanceId"], (string?)Starting(stopped)["InstanceId"]);
        Assert.True(stopped["events"]!.AsArray().Count >= running["events"]!.AsArray().Count);
    }

    /// <summary>
    /// The most common reason work does not run is that the claim refuses it, before any handler or child exists, so
    /// the log is the one place a person can ask what became of it. A real runtime refuses a work item because nothing
    /// routes its operation, and <c>jason logs</c> finds that line by the work item and by its campaign.
    /// </summary>
    [Fact]
    public async Task Work_the_claim_refuses_is_found_by_jason_logs_under_its_work_item_and_its_campaign()
    {
        using var it = GoldenPath.Create("logs-claim");
        await GoldenPath.WriteSettingsAsync(it, new SettingsShape(Routed: false));
        await GoldenPath.StartAsync(it);

        var campaign = (string)GoldenPath.Json(await GoldenPath.Ok(
            GoldenPath.JasonAsync(it, "campaign", "create", "--name", "Nowhere to go")))["id"]!;
        GoldenPath.AssertSuccess(await GoldenPath.JasonAsync(it, "campaign", "start", campaign));
        var workItem = (string)GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(
            it,
            "workitem",
            "create",
            campaign,
            "--kind",
            "provider_op",
            "--operation",
            "campaign.get",
            "--input",
            new JsonObject { ["campaign"] = new JsonObject { ["external_id"] = "7" } }.ToJsonString())))["id"]!;

        var refused = await GoldenPath.PollAsync(it, workItem, item => GoldenPath.Finished((string?)item["status"]));
        var attempt = Assert.Single(refused["attempts"]!.AsArray())!;
        Assert.Equal("no_route", (string?)attempt["error"]!["code"]);
        var attemptId = (string)attempt["id"]!;

        // Asked by the work item, the line is the only one of its kind: the item had one attempt, and the claim ended it
        // once.
        var byWorkItem = await EndedAtClaimAsync(it, "--work-item", workItem);
        AssertRefusal(Assert.Single(byWorkItem));

        // Asked by the campaign, it is found once among whatever else the claim has ended in that campaign. The failure
        // this item ends with is one of the shipped manager triggers, so the next scan summons a manager check-in into
        // the same campaign; no role here can run it, and the claim ends that attempt too, with a line of its own under
        // the same campaign. Whether that line has been written by the time the log is read is a race with that scan,
        // which a slow machine loses often. So the line is picked out by its attempt, and what is held of the rest is that
        // no attempt the claim ended is said twice.
        var byCampaign = await EndedAtClaimAsync(it, "--campaign", campaign);
        Assert.Equal(byCampaign.Count, byCampaign.DistinctBy(evt => (string?)evt["AttemptId"]).Count());
        AssertRefusal(Assert.Single(byCampaign, evt => (string?)evt["AttemptId"] == attemptId));

        // And the refusal was final: the item was claimed once and has not been claimed again since.
        var after = GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(it, "workitem", "get", workItem)));
        Assert.Equal("failed", (string?)after["status"]);
        Assert.Equal(attemptId, (string?)Assert.Single(after["attempts"]!.AsArray())!["id"]);

        await GoldenPath.StopAsync(it);

        void AssertRefusal(JsonObject line)
        {
            Assert.Equal("Warning", (string?)line["@l"]);
            Assert.Equal("no_route", (string?)line["Code"]);
            Assert.Equal(attemptId, (string?)line["AttemptId"]);
            Assert.Equal(workItem, (string?)line["WorkItemId"]);
            Assert.Equal(campaign, (string?)line["CampaignId"]);
        }
    }

    /// <summary>The claim-end lines <c>jason logs</c> returns under one filter, oldest first.</summary>
    private static async Task<List<JsonObject>> EndedAtClaimAsync(Installation it, string filter, string id) =>
        GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(it, "logs", filter, id, "--limit", "10000")))["events"]!.AsArray()
            .Select(evt => evt!.AsObject())
            .Where(evt => (string?)evt["@mt"] == EndedAtClaimTemplate)
            .ToList();

    private static JsonObject Starting(JsonObject document) =>
        document["events"]!.AsArray()
            .Select(evt => evt!.AsObject())
            .Single(evt => (string?)evt["@mt"] == StartingTemplate);
}
