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

    /// <summary>The address the wiring check enrols: the person's own, standing in for whoever follows the page.</summary>
    private const string YourAddress = "ada@example.test";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_objective_becomes_approved_provider_work_and_survives_the_runtime_restarting()
    {
        using var it = GoldenPath.Create("golden");
        it.Account.WithSequence(Sequence, "Q3 LatAm founders", "active", false, 11)
            .WithContact(Person, Address, FirstName);

        await GoldenPath.WriteSettingsAsync(it, new SettingsShape());

        // The documented first step, held to the verb's own wait: the one start in this suite that is not patient.
        await GoldenPath.StartAsDocumentedAsync(it);
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
        Assert.Single(it.Account.Calls, call => call.Command == "sequence add-contact");

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
            var loaded = await GoldenPath.ReloadAsync(it, "installed the reply plugin", routed: true);
            GoldenPath.AssertTheStandInAnswered(Assert.Single(loaded["plugins"]!.AsArray())!.AsObject());

            var campaign = (string)GoldenPath.Json(await GoldenPath.Ok(
                GoldenPath.JasonAsync(it, "campaign", "create", "--name", "Q3 LatAm founders")))["id"]!;
            var contact = await AddPersonAsync(it, campaign);

            // The write the enrolment ends at is held open, so the kill lands on a call that is demonstrably in
            // flight rather than on one the test hopes is running.
            it.Account.HoldsOn("sequence add-contact", started, release);

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

            // The repeat runs the enrolment again, and the CLI reads the person's part in the sequence before it writes
            // anything: the answer is the contract's neutral word for "it was already done". The person is named by
            // their fields again, because the identifier the lost attempt learned was never recorded — and the import
            // matches them by address, so it creates nobody.
            var afterwards = it.Account.CallsSince(beforeTheRestart);
            Assert.Single(afterwards, call => call.Command == "sequence add-contact");
            Assert.Equal("already_enrolled", (string?)ended["result"]!["items"]!.AsArray()[0]!["status"]);
            Assert.Single(it.Account.Contacts);

            // 5. And the point of all of it: the command ran twice and the person was enrolled once. A crash between
            //    the act and the record costs a second run that writes nothing, not a second enrolment.
            Assert.Equal([Person], it.Account.EnrolledIn(Sequence));
            Assert.Equal(2, it.Account.Calls.Count(call => call.Command == "sequence add-contact"));

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
            // The child outlives the runtime by design. Letting it go is what actually ends it — it answers the
            // call it was holding and exits — and the settle below is best-effort: a child that is still
            // running has no pid the API can name, so what keeps the cleanup honest is that removing the
            // directory retries while anything still holds a file in it.
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
    /// attempt is <c>scheduled</c> too, with no start on it — the handler is what moves an attempt to
    /// <c>running</c>, and it does that in the same breath as the start this crash happened before. Written with
    /// the runtime down, because nothing may write a work item's status but the transition table while one is
    /// running. The provenance the claim pins is deliberately left out: nothing in what a restart decides reads
    /// it, and inventing a plausible-looking one would be inventing evidence.
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
                $attempt, id, 1, 'provider_op', 'scheduled', '{}',
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

    /// <summary>
    /// The wiring check a stranger follows, with the page's own arguments. Jason's campaign is started; the provider's
    /// campaign for it is made by <c>campaign.create</c>, which runs without a park because its contract asks nobody —
    /// the question before it is the page's own — and is pinned on Jason's campaign. The two reads work by that pin and
    /// find a draft that holds nobody; only then is the person's own address enrolled, refusing rather than deciding if
    /// they already take part. The enrolment waits for the person's word, the read after it finds exactly them, and the
    /// way back takes them out on its own word and leaves the provider's campaign where it is. Nothing in the run starts
    /// it, so nothing in it can send.
    /// </summary>
    /// <remarks>
    /// The arguments are read from the page, not written here, so the page and what this proves cannot drift apart. The
    /// account at the far end is the stand-in vendor CLI, and it holds no sequence until the run makes one; the same run
    /// against a real account is a person's own.
    /// </remarks>
    [Fact]
    public async Task The_wiring_check_makes_the_provider_s_campaign_reads_it_and_enrols_only_you()
    {
        var page = WiringCheckCommands();
        Assert.Equal("refuse", (string?)page.Inputs["campaign.enroll"]["collision"]);
        var people = page.Keys["enrollment.list"];
        Assert.True(people.Count == 2, $"docs/golden-path.md reads the provider's campaign's people {people.Count} times; it reads them before the enrolment and after it.");

        using var it = GoldenPath.Create("wiring");

        await GoldenPath.WriteSettingsAsync(it, new SettingsShape());
        await GoldenPath.StartAsync(it);
        GoldenPath.InstallReplyPackage(it);
        await GoldenPath.ReloadAsync(it, "installed the reply plugin", routed: true);

        var campaign = (string)GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(
            it, "campaign", "create", "--name", page.CampaignName, "--idempotency-key", page.CampaignKey)))["id"]!;
        var contact = await AddPeopleAsync(it, campaign, WiringCheckContacts().Replace("<your address>", YourAddress, StringComparison.Ordinal));
        GoldenPath.AssertSuccess(await GoldenPath.JasonAsync(it, "campaign", "start", campaign));

        // 1. The provider's campaign for Jason's campaign. Nobody is asked by the runtime: the operation reaches nobody.
        //    It is a draft that holds nobody, with the one task step the provider needs a sequence to have, and nothing
        //    else in the account is touched.
        var made = await RunAsync(it, campaign, null, "campaign.create", page.Inputs["campaign.create"], page.Keys["campaign.create"][0]);
        Assert.Null(Assert.Single(made["attempts"]!.AsArray())!["provenance"]!["approval_id"]);
        Assert.Empty(GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(it, "approval", "list")))["items"]!.AsArray());
        var created = made["result"]!["items"]!.AsArray()[0]!;
        Assert.Equal("draft", (string?)created["state"]);
        Assert.Equal(0, (int)created["enrolled"]!);
        Assert.Equal(1, (int)created["steps_added"]!);

        var external = (string)created["campaign_external_id"]!;
        var sequence = int.Parse(external, CultureInfo.InvariantCulture);
        var held = Assert.Single(it.Account.Sequences)!;
        Assert.Equal(sequence, (int)held["id"]!);
        Assert.Equal((string?)page.Inputs["campaign.create"]["name"], (string?)held["name"]);
        Assert.Equal("new", (string?)held["status"]);
        Assert.Equal("task", (string?)Assert.Single(held["steps"]!.AsArray())!["type"]);

        // And Jason's campaign carries it as its pin, which is what every later command works by.
        var pins = GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(it, "campaign", "get", campaign)))["external_ids"]!.AsArray();
        var pin = Assert.Single(pins, one => (string?)one!["kind"] == "campaign")!;
        Assert.Equal(external, (string?)pin["value"]);
        Assert.Equal(GoldenPath.PluginId, (string?)pin["plugin_id"]);

        // 2. Two reads before anything is written to it, neither naming it and neither waiting for anybody: reading
        //    changes nothing at the provider. It is a draft — Reply's "new" in the contract's own word — holding nobody.
        var draft = await RunAsync(it, campaign, null, "campaign.get", page.Inputs["campaign.get"], page.Keys["campaign.get"][0]);
        Assert.Equal("draft", (string?)draft["result"]!["campaign"]!["status"]);
        var nobody = await RunAsync(it, campaign, null, "enrollment.list", page.Inputs["enrollment.list"], people[0]);
        Assert.Equal(0, (int)nobody["result"]!["total"]!);
        Assert.DoesNotContain(it.Account.Calls, call => call.Command == "sequence add-contact");

        // 3. The enrolment parks for the person's word, is given it, and runs.
        var enrolled = await DecidedAsync(it, campaign, contact, "campaign.enroll", page.Inputs["campaign.enroll"], page.Keys["campaign.enroll"][0]);
        Assert.Equal("enrolled", (string?)enrolled["result"]!["items"]!.AsArray()[0]!["status"]);
        Assert.False((bool)enrolled["result"]!["campaign_live"]!);

        // 4. The read after it finds exactly the one person this run is about.
        var you = await RunAsync(it, campaign, null, "enrollment.list", page.Inputs["enrollment.list"], people[1]);
        Assert.Equal(1, (int)you["result"]!["total"]!);
        Assert.Single(it.Account.EnrolledIn(sequence));

        // 5. The way back waits for its own word, takes the person out, and leaves the provider's campaign where it is:
        //    still there, still a draft, holding nobody.
        await DecidedAsync(it, campaign, contact, "enrollment.stop", page.Inputs["enrollment.stop"], page.Keys["enrollment.stop"][0]);
        Assert.Empty(it.Account.EnrolledIn(sequence));
        Assert.Equal("new", (string?)Assert.Single(it.Account.Sequences)!["status"]);

        // 6. And it ends, once the stop has finished, by pausing Jason's campaign: Jason stops reading the provider's
        //    campaign, which stays where it is.
        GoldenPath.AssertSuccess(await GoldenPath.JasonAsync(it, "campaign", "pause", campaign));
        var paused = GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(it, "campaign", "get", campaign)));
        Assert.Equal("paused", (string?)paused["status"]);
        Assert.Equal("new", (string?)Assert.Single(it.Account.Sequences)!["status"]);

        // And nothing in the run started it, which is what keeps a wiring check from sending.
        Assert.DoesNotContain(it.Account.Calls, call => call.Command == "sequence start");

        await GoldenPath.StopAsync(it);
    }

    /// <summary>
    /// What the wiring check's commands carry, read from the page rather than copied here: the name and key Jason's
    /// campaign is created with; the <c>--input</c> of each provider operation, by operation — the page's own arguments,
    /// single-quoted JSON, which a shell hands over as written, the same each time an operation is created twice; and
    /// each work item's <c>--idempotency-key</c>, by operation in the order the page gives them.
    /// </summary>
    /// <remarks>
    /// Single quotes because PowerShell takes no backslash escape: a double-quoted argument with its quotes escaped for a
    /// POSIX shell reaches the CLI split in two there, and an agent's first read failed on exactly that.
    /// </remarks>
    private static WiringCheck WiringCheckCommands()
    {
        var lines = WiringCheckPage().Split('\n').Select(line => line.Trim()).ToList();

        var create = Assert.Single(lines, line => line.StartsWith("jason campaign create ", StringComparison.Ordinal));
        var named = System.Text.RegularExpressions.Regex.Match(create, "^jason campaign create --name \"([^\"]+)\" --idempotency-key (\\S+)$");
        Assert.True(named.Success, $"docs/golden-path.md creates Jason's campaign with something other than a name and a key: {create}");

        var inputs = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        var keys = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var line in lines.Where(line => line.StartsWith("jason workitem create ", StringComparison.Ordinal)))
        {
            var operation = System.Text.RegularExpressions.Regex.Match(line, @"--operation (\S+)").Groups[1].Value;
            var key = System.Text.RegularExpressions.Regex.Match(line, @"--idempotency-key (\S+)");
            Assert.True(key.Success, $"docs/golden-path.md creates {operation} without a key of its own: {line}");
            keys.TryAdd(operation, []);
            keys[operation].Add(key.Groups[1].Value);

            var quoted = System.Text.RegularExpressions.Regex.Match(line, "--input '([^']*)'");
            Assert.True(quoted.Success, $"docs/golden-path.md gives {operation} its --input as something other than single-quoted JSON: {line}");
            var parsed = JsonNode.Parse(quoted.Groups[1].Value)!.AsObject();
            if (inputs.TryGetValue(operation, out var seen))
            {
                Assert.Equal(seen.ToJsonString(JasonJson.Options), parsed.ToJsonString(JasonJson.Options));
                continue;
            }

            inputs[operation] = parsed;
        }

        foreach (var operation in new[] { "campaign.create", "campaign.get", "enrollment.list", "campaign.enroll", "enrollment.stop" })
        {
            Assert.True(inputs.ContainsKey(operation), $"docs/golden-path.md creates no {operation} work item.");
        }

        // The way back ends by pausing Jason's campaign, after the stop: the run below does the same.
        var stop = lines.FindIndex(line => line.Contains("--operation enrollment.stop ", StringComparison.Ordinal));
        var pause = lines.FindIndex(line => line.StartsWith("jason campaign pause ", StringComparison.Ordinal));
        Assert.True(stop >= 0 && pause > stop, $"docs/golden-path.md pauses Jason's campaign at its line {pause} and stops the enrolment at its line {stop}.");

        return new WiringCheck(named.Groups[1].Value, named.Groups[2].Value, inputs, keys);
    }

    /// <summary>The wiring check's commands as the page prints them; see <see cref="WiringCheckCommands"/>.</summary>
    private sealed record WiringCheck(
        string CampaignName,
        string CampaignKey,
        Dictionary<string, JsonObject> Inputs,
        Dictionary<string, List<string>> Keys);

    /// <summary>
    /// The one line <c>contacts.json</c> holds on the wiring check, as the page prints it: the person's own address and
    /// nothing else, not even a first name, which the package derives from the address.
    /// </summary>
    private static string WiringCheckContacts()
    {
        var file = Assert.Single(WiringCheckPage().Split('\n'), line => line.StartsWith("[{\"channels\":", StringComparison.Ordinal));
        Assert.Contains("<your address>", file, StringComparison.Ordinal);
        return file;
    }

    private static string WiringCheckPage() =>
        File.ReadAllText(Path.Combine(Skills.SkillPack.RepositoryRoot(), "docs", "golden-path.md")).Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>One provider operation as a work item, run to its end without anybody's word: a read.</summary>
    private static async Task<JsonObject> RunAsync(Installation it, string campaign, string? contact, string operation, JsonObject input, string key)
    {
        var item = await CreateAsync(it, campaign, contact, operation, input, key);
        var ran = await GoldenPath.PollAsync(it, item, read => GoldenPath.Finished((string?)read["status"]));
        GoldenPath.AssertSucceeded(ran);
        return ran;
    }

    /// <summary>One provider operation that parks for a person's word, given that word, and run to its end.</summary>
    private static async Task<JsonObject> DecidedAsync(Installation it, string campaign, string contact, string operation, JsonObject input, string key)
    {
        var item = await CreateAsync(it, campaign, contact, operation, input, key);
        await GoldenPath.PollAsync(it, item, read => (string?)read["status"] == "awaiting_approval");

        var waiting = Assert.Single(
            GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(it, "approval", "list")))["items"]!.AsArray(),
            approval => (string?)approval!["work_item_id"] == item)!;
        GoldenPath.AssertSuccess(await GoldenPath.JasonAsync(
            it, "approval", "approve", (string)waiting["id"]!, "--actor", "human:ada", "--reason", "my own wiring check"));

        var ran = await GoldenPath.PollAsync(it, item, read => GoldenPath.Finished((string?)read["status"]));
        GoldenPath.AssertSucceeded(ran);
        return ran;
    }

    private static async Task<string> CreateAsync(Installation it, string campaign, string? contact, string operation, JsonObject input, string key)
    {
        string[] about = contact is null ? [] : ["--contact", contact];
        string[] arguments = ["workitem", "create", campaign, "--kind", "provider_op", "--operation", operation, .. about, "--input", input.ToJsonString(JasonJson.Options), "--idempotency-key", key];
        return (string)GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(it, arguments)))["id"]!;
    }

    /// <summary>The person this campaign is about, added the way an agent would add them.</summary>
    private static Task<string> AddPersonAsync(Installation it, string campaign, string address = Address, string firstName = FirstName) =>
        AddPeopleAsync(
            it,
            campaign,
            new JsonArray(new JsonObject
            {
                ["first_name"] = firstName,
                ["channels"] = new JsonArray(new JsonObject { ["channel"] = "email", ["value"] = address }),
            }).ToJsonString(JasonJson.Options));

    /// <summary>The one person a <c>contacts.json</c> holds, added from that file, and their contact id.</summary>
    private static async Task<string> AddPeopleAsync(Installation it, string campaign, string contacts)
    {
        var contactsFile = Path.Combine(it.Root, Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(contactsFile, contacts, Ct);

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
