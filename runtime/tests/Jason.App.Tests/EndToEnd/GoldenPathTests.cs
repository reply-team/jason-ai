using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Contracts.Json;
using Microsoft.Data.Sqlite;

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
        var loaded = await GoldenPath.ReloadAsync(it, "installed the reply plugin", routed: true);
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

    /// <summary>
    /// What a restart finds, from outside the process that crashed. Two cases, two codes, and the second is the
    /// one that matters: a provider call that was in flight when the runtime died is not a failure, it is an
    /// unanswered question — the provider may well have acted — so the operation's own contract decides what
    /// happens next, and <c>campaign.enroll</c> declares a recovery read.
    /// </summary>
    /// <remarks>
    /// The killed runtime is killed alone, never with its tree: a crash that also took the child with it would
    /// be a tidier state than any real one, and would prove nothing about the state a real crash leaves. The
    /// child is then let go deliberately and its work observed at the provider before the runtime is started
    /// again, so what the restart meets is the hard case — the effect happened, and nobody recorded it.
    /// </remarks>
    [Fact]
    public async Task A_killed_runtime_retries_the_ambiguous_enrolment_and_gives_back_what_never_launched()
    {
        using var it = GoldenPath.Create("recovery");
        it.Account.WithSequence(Sequence, "Q3 LatAm founders", "active", false, 11)
            .WithContact(Person, Address, FirstName);

        var signals = Path.Combine(Path.GetTempPath(), "jason-signals", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(signals);
        var started = Path.Combine(signals, "started");
        var release = Path.Combine(signals, "release");

        try
        {
            // Ten seconds is the smallest interval the validator accepts, so an attempt nobody is reporting for
            // is given up on twenty seconds after the new runtime's own start rather than after the ten-minute
            // lease a shipped runtime would wait out.
            await GoldenPath.WriteSettingsAsync(it, new SettingsShape(ProviderHeartbeatSeconds: 10));
            await GoldenPath.StartAsync(it);
            GoldenPath.InstallReplyPackage(it);
            await GoldenPath.ReloadAsync(it, "installed the reply plugin", routed: true);

            var campaign = (string)GoldenPath.Json(await GoldenPath.Ok(
                GoldenPath.JasonAsync(it, "campaign", "create", "--name", "Q3 LatAm founders")))["id"]!;
            var contact = await AddPersonAsync(it, campaign);

            // The write the enrolment ends at is held open, so the kill lands on a call that is demonstrably in
            // flight rather than on one the test hopes is running.
            it.Account.HoldsOn("POST", $"/v3/sequences/{Sequence}/contact-links/bulk", started, release);

            var item = await EnrolAsync(it, campaign, contact);
            GoldenPath.AssertSuccess(await GoldenPath.JasonAsync(it, "campaign", "start", campaign));
            await GoldenPath.PollAsync(it, item, read => (string?)read["status"] == "awaiting_approval");
            var approval = (string)Assert.Single(GoldenPath.Json(await GoldenPath.Ok(
                GoldenPath.JasonAsync(it, "approval", "list")))["items"]!.AsArray())!["id"]!;
            await GoldenPath.Ok(GoldenPath.JasonAsync(
                it, "approval", "approve", approval, "--actor", "human:ada", "--reason", "checked the list"));

            // The child says it has started; the runtime cannot say it for us, because a provider attempt's
            // launch — its command, its pid, its exit code — is written when the invocation ends.
            await WaitAsync(() => File.Exists(started), "The provider call never started.");
            await GoldenPath.ChildPidAsync(it, item);

            // 1. The crash. The runtime dies; what it started does not.
            GoldenPath.Kill(it);
            var unreachable = await GoldenPath.JasonAsync(it, "runtime", "status");
            Assert.Equal(ExitCodes.RuntimeUnavailable, unreachable.ExitCode);
            Assert.Contains("runtime_unreachable", unreachable.Output, StringComparison.Ordinal);

            // 2. And the provider acts anyway, because the orphaned child finishes the call it had already
            //    sent. This is the state the whole proof is about: the effect happened and nobody recorded it.
            await File.WriteAllTextAsync(release, string.Empty, Ct);
            await WaitAsync(
                () => it.Account.EnrolledIn(Sequence).Contains(Person),
                "The orphaned child never finished the call it had already sent.");
            var beforeTheRestart = it.Account.Mark();

            // 3. The restart. Nothing is written for an attempt that was past its start: the item is left
            //    processing, and the lease enforcer is what ends it — with the honest class, not with a guess.
            await GoldenPath.StartAsync(it);
            var ended = await GoldenPath.PollAsync(it, item, read => (string?)read["status"] == "succeeded");

            // By their number rather than by their place in the list: which attempt is which is a fact about
            // the work, not about how a read happens to order them.
            var attempts = ended["attempts"]!.AsArray();
            Assert.Equal(2, attempts.Count);
            var lost = Assert.Single(attempts, read => (int?)read!["number"] == 1)!;
            Assert.Equal("heartbeat_missed", (string?)lost["error"]!["code"]);
            Assert.Equal("ambiguous", (string?)lost["error"]!["class"]);
            Assert.True((bool)lost["error"]!["retriable"]!, "An enrolment declares a recovery read, so an unanswered end is worth another attempt.");

            // 4. The repeat runs under the decision that was already given — the subject has not changed, so
            //    nobody is asked twice — and it asks the provider what happened before it writes anything.
            var repeat = Assert.Single(attempts, read => (int?)read!["number"] == 2)!;
            Assert.Equal("succeeded", (string?)repeat["status"]);
            Assert.Equal(approval, (string?)repeat["provenance"]!["approval_id"]);
            Assert.Equal(approval, (string?)lost["provenance"]!["approval_id"]);

            // The repeat asks the provider what happened and writes nothing: the participation read is there,
            // the enrolment write is not, and the answer is the contract's neutral word for "it was already
            // done". The contact is ensured again first, because the identifier the lost attempt learned was
            // never recorded — and ensuring is a read by address, so it costs a call and creates nobody.
            var afterwards = it.Account.CallsSince(beforeTheRestart);
            Assert.Contains(afterwards, call => call.Path == $"/v3/sequences/{Sequence}/contacts/{Person}");
            Assert.DoesNotContain(afterwards, call => call.Path.EndsWith("/contact-links/bulk", StringComparison.Ordinal));
            Assert.Equal("already_enrolled", (string?)ended["result"]!["items"]!.AsArray()[0]!["status"]);
            Assert.Single(it.Account.Contacts);

            // 5. And the point of all of it: the person was enrolled once. A crash between the act and the
            //    record costs a recovery read, not a second enrolment.
            Assert.Equal([Person], it.Account.EnrolledIn(Sequence));
            Assert.Single(it.Account.Calls, call => call.Path.EndsWith("/contact-links/bulk", StringComparison.Ordinal));

            // ---------------------------------------------------------------------------------------------
            // The other case: an attempt that was claimed and never launched.
            //
            // The live window for it is the few instructions between the claim and the commit that says the
            // work has started, which cannot be hit reliably from outside a process. So the state a crash in
            // that window leaves is constructed here, with the runtime down, and what is proven is that a
            // restart makes the decision at all and that the work really does run again.
            // `StartupRecoveryTests` is where the decision itself is proven.
            // ---------------------------------------------------------------------------------------------
            GoldenPath.AssertSuccess(await GoldenPath.JasonAsync(it, "campaign", "pause", campaign));
            var read = (string)GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(
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

            await GoldenPath.StopAsync(it);
            ClaimWithoutLaunching(it, read);
            await GoldenPath.StartAsync(it);

            var given = await GoldenPath.PollAsync(it, read, item => (string?)item["status"] == "created");
            var interrupted = Assert.Single(given["attempts"]!.AsArray())!;
            Assert.Equal("interrupted", (string?)interrupted["status"]);
            Assert.Null(interrupted["started_at"]);

            // Nothing was spent: an attempt that ran nothing and asked nobody is not one of the tries the work
            // is allowed, which is what keeps a restart from eating a work item's budget.
            Assert.Equal(0, (int)given["attempt_count"]!);

            // And the work is work again.
            GoldenPath.AssertSuccess(await GoldenPath.JasonAsync(it, "campaign", "start", campaign));
            GoldenPath.AssertSucceeded(await GoldenPath.PollAsync(it, read, item => GoldenPath.Finished((string?)item["status"])));

            await GoldenPath.StopAsync(it);
        }
        finally
        {
            // The child outlives the runtime by design, so it is let go, waited for, and only then ended.
            if (!File.Exists(release))
            {
                await File.WriteAllTextAsync(release, string.Empty, CancellationToken.None);
            }

            await GoldenPath.SettleAsync(it, TimeSpan.FromSeconds(30));
            GoldenPath.Delete(signals);
        }
    }

    /// <summary>
    /// The state a crash between the claim and the launch leaves behind: the item is scheduled, and its live
    /// attempt has no start on it. Written with the runtime down, because nothing may write a work item's
    /// status but the transition table while one is running.
    /// </summary>
    private static void ClaimWithoutLaunching(Installation it, string workItemId)
    {
        using var connection = new SqliteConnection($"Data Source={it.Paths.DatabaseFile}");
        connection.Open();

        using var scheduled = connection.CreateCommand();
        scheduled.CommandText = "UPDATE work_items SET status = 'scheduled' WHERE public_id = $item;";
        scheduled.Parameters.AddWithValue("$item", workItemId);
        Assert.Equal(1, scheduled.ExecuteNonQuery());

        using var claimed = connection.CreateCommand();
        claimed.CommandText = """
            INSERT INTO attempts (
                public_id, work_item_id, number, command, status, context_snapshot_json, claimed_at, lock_until)
            SELECT
                $attempt, id, 1, 'provider_op', 'running', '{}',
                strftime('%Y-%m-%d %H:%M:%S', 'now'), strftime('%Y-%m-%d %H:%M:%S', 'now', '+10 minutes')
            FROM work_items WHERE public_id = $item;
            """;
        claimed.Parameters.AddWithValue("$item", workItemId);
        claimed.Parameters.AddWithValue("$attempt", "att_01JB6K8TQ2W9V4MZ0C3Y7H5NRD");
        Assert.Equal(1, claimed.ExecuteNonQuery());
    }

    /// <summary>Waits for something a child process does, because a test may observe it but never hurry it.</summary>
    private static async Task WaitAsync(Func<bool> until, string never)
    {
        var deadline = DateTime.UtcNow.Add(GoldenPath.Patience);
        while (DateTime.UtcNow < deadline)
        {
            if (until())
            {
                return;
            }

            await Task.Delay(100, Ct);
        }

        Assert.Fail(never);
    }

    /// <summary>The person this campaign is about, added the way an agent would add them.</summary>
    private static async Task<string> AddPersonAsync(Installation it, string campaign)
    {
        var contactsFile = Path.Combine(it.Root, Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(
            contactsFile,
            new JsonArray(new JsonObject
            {
                ["first_name"] = FirstName,
                ["channels"] = new JsonArray(new JsonObject { ["channel"] = "email", ["value"] = Address }),
            }).ToJsonString(JasonJson.Options),
            Ct);

        await GoldenPath.Ok(GoldenPath.JasonAsync(
            it, "campaign", "add-contacts", campaign, "--file", contactsFile, "--match-by", "email"));
        return (string)Assert.Single(GoldenPath.Json(await GoldenPath.Ok(
            GoldenPath.JasonAsync(it, "campaign", "list-contacts", campaign)))["items"]!.AsArray())!["contact"]!["id"]!;
    }

    /// <summary>One enrolment, as a work item naming the canonical operation and carrying its arguments.</summary>
    private static async Task<string> EnrolAsync(Installation it, string campaign, string contact) =>
        (string)GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(
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
