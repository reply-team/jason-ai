using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Json;

namespace Jason.App.Tests.EndToEnd;

/// <summary>
/// The spine, end to end and out of process: an objective becomes a campaign, the campaign's work becomes an
/// enrolment nobody may perform without a person's word, the word is given, the plugin performs it against the
/// provider through the vendor's own CLI, and what happened is still there — in full — after the runtime has
/// been stopped and started again.
/// </summary>
/// <remarks>
/// Every leg of this has its own test somewhere in this repository. What is proven here is that they are one
/// system: the same work item crosses the claim, the approval, a child process, a provider account and a
/// restart without anything having to be re-entered or re-decided. The account at the far end is the stand-in
/// vendor CLI beside these tests, never a real Reply account.
/// </remarks>
public class GoldenPathTests
{
    /// <summary>A sequence identifier of the shape the provider actually issues, and its first step.</summary>
    private const int Sequence = 7;

    private const int Person = 1001;

    private const string Address = "marta@example.test";

    private const string FirstName = "Marta";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_objective_becomes_approved_provider_work_and_survives_the_runtime_restarting()
    {
        using var it = GoldenPath.Create("golden");
        it.Account.WithSequence(Sequence, "Q3 LatAm founders", "active", false, 11)
            .WithContact(Person, Address, FirstName);

        await GoldenPath.WriteSettingsAsync(it, new SettingsShape());
        await GoldenPath.StartAsync(it);
        GoldenPath.InstallReplyPackage(it);
        var loaded = GoldenPath.Json(await GoldenPath.Ok(
            GoldenPath.JasonAsync(it, "plugin", "reload", "--reason", "installed the reply plugin")));
        GoldenPath.AssertTheStandInAnswered(Assert.Single(loaded["plugins"]!.AsArray())!.AsObject());
        var digest = (string)Assert.Single(loaded["plugins"]!.AsArray())!["digest"]!;
        var pluginSnapshot = (string)loaded["snapshot"]!["id"]!;
        var routingSnapshot = (string)loaded["routing_snapshot_id"]!;

        // 1. The objective, as the CLI takes it: a campaign, the person it is about, and one unit of work that
        //    names a canonical operation rather than describing an intention in prose.
        var campaign = (string)GoldenPath.Json(await GoldenPath.Ok(
            GoldenPath.JasonAsync(it, "campaign", "create", "--name", "Q3 LatAm founders")))["id"]!;

        var contactsFile = Path.Combine(it.Root, "contacts.json");
        await File.WriteAllTextAsync(
            contactsFile,
            new JsonArray(new JsonObject
            {
                ["first_name"] = FirstName,
                ["channels"] = new JsonArray(new JsonObject { ["channel"] = "email", ["value"] = Address }),
            }).ToJsonString(JasonJson.Options),
            Ct);
        var added = GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(
            it, "campaign", "add-contacts", campaign, "--file", contactsFile, "--match-by", "email")));
        Assert.Equal(1, (int)added["summary"]!["added"]!);
        var contact = (string)Assert.Single(GoldenPath.Json(await GoldenPath.Ok(
            GoldenPath.JasonAsync(it, "campaign", "list-contacts", campaign)))["items"]!.AsArray())!["contact"]!["id"]!;

        var item = (string)GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(
            it,
            "workitem",
            "create",
            campaign,
            "--kind",
            "provider_op",
            "--operation",
            "campaign.enroll",
            "--contact",
            contact,
            "--input",
            Enrolment().ToJsonString(JasonJson.Options))))["id"]!;
        GoldenPath.AssertSuccess(await GoldenPath.JasonAsync(it, "campaign", "start", campaign));

        // 2. The dispatcher will not perform this one. It parks at the claim, and a park is not an attempt:
        //    nothing was run, nobody was asked, and the item's budget is untouched.
        var parked = await GoldenPath.PollAsync(it, item, read => (string?)read["status"] == "awaiting_approval");
        Assert.Empty(parked["attempts"]!.AsArray());
        Assert.Equal(0, (int)parked["attempt_count"]!);
        Assert.Empty(it.Account.Calls);

        // 3. What a person is shown before they decide: what it would do, to whom, through which account — and
        //    the account by identity, never a credential and never the store it lives in.
        var waiting = Assert.Single(GoldenPath.Json(await GoldenPath.Ok(
            GoldenPath.JasonAsync(it, "approval", "list")))["items"]!.AsArray())!;
        var approvalId = (string)waiting["id"]!;
        Assert.StartsWith("apr_", approvalId, StringComparison.Ordinal);
        Assert.Equal("approval_required", (string?)waiting["reason"]);

        var question = GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(it, "approval", "get", approvalId)));
        Assert.Equal(item, (string?)question["work_item_id"]);
        Assert.Equal("campaign.enroll", (string?)question["operation"]);
        Assert.StartsWith("sha256:", (string?)question["subject_hash"], StringComparison.Ordinal);
        var preview = question["preview"]!;
        Assert.False(string.IsNullOrWhiteSpace((string?)preview["intent"]), "The preview said nothing about what it would do.");
        Assert.NotNull((string?)preview["reach"]!["value"]);
        Assert.NotNull((string?)preview["reversibility"]!["value"]);
        Assert.NotNull((string?)preview["cost"]!["value"]);
        Assert.Equal(campaign, (string?)preview["campaign"]!["id"]);
        Assert.Equal(contact, (string?)preview["contact"]!["id"]);
        Assert.Equal(Address, (string?)preview["contact"]!["value"]);
        Assert.Equal(GoldenPath.PluginId, (string?)question["plugin_id"]);
        Assert.NotNull((string?)question["binding_identity"]);
        Assert.DoesNotContain(
            it.Account.Marker,
            question.ToJsonString(JasonJson.Options),
            StringComparison.OrdinalIgnoreCase);

        // 4. The decision is a person's, and the runtime records which person and why.
        var decided = GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(
            it, "approval", "approve", approvalId, "--actor", "human:ada", "--reason", "checked the list")));
        Assert.Equal("approved", (string?)decided["status"]);
        Assert.Equal("human", (string?)decided["decided_by"]!["type"]);
        Assert.Equal("ada", (string?)decided["decided_by"]!["id"]);

        // 5. And then the whole path runs: claim, plugin host, the vendor CLI, the provider's own account.
        var ran = await GoldenPath.PollAsync(it, item, read => GoldenPath.Finished((string?)read["status"]));
        GoldenPath.AssertSucceeded(ran);
        var outcome = ran["result"]!["items"]!.AsArray()[0]!;
        Assert.Equal("enrolled", (string?)outcome["status"]);
        Assert.True((bool)ran["result"]!["campaign_live"]!);
        Assert.Equal([Person], it.Account.EnrolledIn(Sequence));

        // What ran, pinned at the claim and completed at the end: the package, its digest, both snapshots, the
        // approval that released it, and the account by identity.
        var attempt = Assert.Single(ran["attempts"]!.AsArray())!;
        var provenance = attempt["provenance"]!;
        Assert.Equal(GoldenPath.PluginId, (string?)provenance["plugin_id"]);
        Assert.Equal(digest, (string?)provenance["plugin_digest"]);
        Assert.Equal(pluginSnapshot, (string?)provenance["plugin_snapshot_id"]);
        Assert.Equal(routingSnapshot, (string?)provenance["routing_snapshot_id"]);
        Assert.Equal("global_default", (string?)provenance["route_scope"]);
        Assert.Equal(approvalId, (string?)provenance["approval_id"]);
        Assert.NotNull((string?)provenance["binding_identity"]);

        // 6. Everything a person would read afterwards, read now and kept.
        var before = GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(it, "workitem", "get", item)));
        var approvalBefore = GoldenPath.Json(await GoldenPath.Ok(
            GoldenPath.JasonAsync(it, "approval", "get", approvalId)));
        var chronicleBefore = await KindsAsync(it, item);

        // 7. The initiating session goes away entirely: the process that held all of this is stopped and a new
        //    one is started in its place.
        var first = it.Pid;
        await GoldenPath.StopAsync(it);
        var second = await GoldenPath.StartAsync(it);
        Assert.NotEqual(first, second.Pid);

        // 8. And the answer is the same answer. Per item, not per journal: a restart writes lines of its own —
        //    the reload it performs, what recovery found — so what must be identical is this item's own record.
        var after = GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(it, "workitem", "get", item)));
        Assert.Equal(before.ToJsonString(JasonJson.Options), after.ToJsonString(JasonJson.Options));

        var approvalAfter = GoldenPath.Json(await GoldenPath.Ok(
            GoldenPath.JasonAsync(it, "approval", "get", approvalId)));
        Assert.Equal(approvalBefore.ToJsonString(JasonJson.Options), approvalAfter.ToJsonString(JasonJson.Options));
        Assert.Equal("human", (string?)approvalAfter["decided_by"]!["type"]);
        Assert.Equal("ada", (string?)approvalAfter["decided_by"]!["id"]);
        Assert.Equal("checked the list", (string?)approvalAfter["decision_reason"]);

        Assert.Equal(chronicleBefore, await KindsAsync(it, item));

        // Nothing was asked of the provider a second time: a restart reads state, it does not repeat work.
        Assert.Equal([Person], it.Account.EnrolledIn(Sequence));
        Assert.Single(it.Account.Calls, call => call.Path.EndsWith("/bulk", StringComparison.Ordinal));

        await GoldenPath.StopAsync(it);
    }

    /// <summary>This item's own chronicle, oldest first: which kinds were written, and in what order.</summary>
    private static async Task<IReadOnlyList<string>> KindsAsync(Installation it, string workItemId)
    {
        var entries = GoldenPath.Json(await GoldenPath.Ok(
            GoldenPath.JasonAsync(it, "journal", "list", "--work-item", workItemId)))["items"]!.AsArray();

        // The chronicle is answered newest first, and the claim about it is about order.
        return [.. entries.Select(entry => (string)entry!["kind"]!).Reverse()];
    }

    /// <summary>The arguments the operation's own contract publishes, under the one context key that carries them.</summary>
    private static JsonObject Enrolment() => new()
    {
        ["campaign"] = new JsonObject { ["external_id"] = Sequence.ToString(CultureInfo.InvariantCulture) },
        ["channel"] = "email",
        ["collision"] = "skip",
        ["start"] = new JsonObject { ["position"] = "first_step" },
        ["first_touch"] = "authored_delay",
    };
}
