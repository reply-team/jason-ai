using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Json;

namespace Jason.App.Tests.EndToEnd;

/// <summary>
/// A reply reaches a campaign's manager, end to end and out of process: the shipped program reads a live campaign's
/// conversations through the official package against the stand-in Reply account, says the thread that moved in one
/// line naming the thread and the person, and that line wakes the campaign's manager. The provider is only read, and
/// the person's words — the subject, the preview, the name the provider shows — reach neither the chronicle nor the
/// runtime's own log.
/// </summary>
public class InboundSyncE2ETests
{
    private const int Sequence = 7;

    private const int Person = 1001;

    private const int Conversation = 501;

    private const string Address = "marta@example.test";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_reply_in_a_live_campaign_s_conversations_is_said_once_and_wakes_its_manager()
    {
        // One mark for the run, in every word of the person's that must stay at the provider.
        var mark = "zq" + Guid.NewGuid().ToString("N");
        using var it = GoldenPath.Create("inbound");
        it.Account.WithSequence(Sequence, "Q3 LatAm founders", "active", false, 11)
            .WithContact(Person, Address, "Marta");

        // The read is off while the campaign is linked and its person pinned, so the first read the test waits for is
        // the one that finds the reply rather than one that found the campaign not linked yet.
        await WriteSettingsAsync(it, inbound: false);
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
                ["first_name"] = "Marta",
                ["channels"] = new JsonArray(new JsonObject { ["channel"] = "email", ["value"] = Address }),
            }).ToJsonString(JasonJson.Options),
            Ct);
        await GoldenPath.Ok(GoldenPath.JasonAsync(it, "campaign", "add-contacts", campaign, "--file", contactsFile, "--match-by", "email"));
        var contact = (string)Assert.Single(GoldenPath.Json(await GoldenPath.Ok(
            GoldenPath.JasonAsync(it, "campaign", "list-contacts", campaign)))["items"]!.AsArray())!["contact"]!["id"]!;

        // The enrolment links the campaign to the sequence and pins the person, once a person has approved it.
        var enrolment = (string)GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(
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
        await GoldenPath.Ok(GoldenPath.JasonAsync(it, "campaign", "start", campaign));
        await GoldenPath.PollAsync(it, enrolment, read => (string?)read["status"] == "awaiting_approval");
        var approval = (string)Assert.Single(GoldenPath.Json(await GoldenPath.Ok(
            GoldenPath.JasonAsync(it, "approval", "list")))["items"]!.AsArray())!["id"]!;
        await GoldenPath.Ok(GoldenPath.JasonAsync(it, "approval", "approve", approval, "--actor", "human:ada", "--reason", "checked"));
        GoldenPath.AssertSucceeded(await GoldenPath.PollAsync(it, enrolment, read => GoldenPath.Finished((string?)read["status"])));

        // Marta answers, after the campaign went live.
        var replied = DateTime.UtcNow.AddSeconds(5);
        it.Account.WithThread(Thread(Conversation, replied, mark));
        var beforeTheRead = it.Account.Mark();

        // 1. The read is switched on by a live edit, and the next scans read the conversations and say the reply.
        await WriteSettingsAsync(it, inbound: true);
        var line = await LineAsync(it, campaign, Patience);
        var said = line["new"]!;
        Assert.Equal("system", (string?)line["actor"]!["type"]);
        Assert.Equal("dispatcher", (string?)line["actor"]!["id"]);
        Assert.Equal(GoldenPath.PluginId, (string?)said["plugin"]);
        Assert.Equal(Conversation.ToString(CultureInfo.InvariantCulture), (string?)said["thread"]);
        Assert.Equal(contact, (string?)said["contact_id"]);
        Assert.Equal("email", (string?)said["channel"]);
        Assert.True((bool)said["unread"]!);
        Assert.True((bool)said["has_meeting_intent"]!);
        Assert.Equal(Moment(replied), (string?)said["last_activity_at"]);
        Assert.StartsWith("sha256:", (string?)line["key"], StringComparison.Ordinal);

        // The line names the read that found it: a conversation.list the dispatcher made, which succeeded.
        var read = GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(it, "workitem", "get", (string)line["work_item_id"]!)));
        Assert.Equal("conversation.list", (string?)read["operation"]);
        Assert.Equal("system", (string?)read["created_by"]!["type"]);
        GoldenPath.AssertSucceeded(read);
        Assert.Equal((string?)line["attempt_id"], (string?)Assert.Single(read["attempts"]!.AsArray())!["id"]);
        Assert.True(
            string.CompareOrdinal((string?)read["context"]!["input"]!["since"], Moment(replied)) < 0,
            "The read that found the reply started before it.");

        // The control for step 5: the reply's words did reach the runtime — the read keeps the page it read, as any read
        // does — so their absence from the chronicle and the log below is not the absence of anything to leak.
        Assert.Contains(mark, read["result"]!.ToJsonString(), StringComparison.Ordinal);

        // 2. It woke the manager: one check-in whose brief names the line and why it was summoned.
        var checkIn = await CheckInAsync(it, campaign, (string)line["id"]!);
        Assert.Contains("inbound_reply_observed", checkIn["context"]!.ToJsonString(), StringComparison.Ordinal);

        // 3. The next read lists the same thread and says nothing: the move was said once. The read is accounted for at
        //    the scan after it ends, so the line count is read a few scans later.
        var first = (string)line["work_item_id"]!;
        JsonObject? next = null;
        await WaitAsync(
            async () =>
            {
                next = (await ReadsAsync(it, campaign)).FirstOrDefault(item => (string?)item["id"] != first && (string?)item["status"] == "succeeded");
                return next is not null;
            },
            TimeSpan.FromSeconds(120),
            "A second read never succeeded.");
        var listed = GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(it, "workitem", "get", (string)next!["id"]!)));
        Assert.Contains(
            listed["result"]!["items"]!.AsArray(),
            thread => (string?)thread!["external_id"] == Conversation.ToString(CultureInfo.InvariantCulture));
        await Task.Delay(TimeSpan.FromSeconds(4), Ct);
        var lines = GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(
            it, "journal", "list", "--campaign", campaign, "--kind", "inbound_reply_observed")))["items"]!.AsArray();
        Assert.Single(lines);

        // 4. The provider was only read: every call since the reply is the inbox list for this sequence, from a moment.
        var calls = it.Account.CallsSince(beforeTheRead);
        Assert.NotEmpty(calls);
        Assert.All(calls, call =>
        {
            Assert.Equal("inbox list", call.Command);
            Assert.Equal("7", call.Option("--sequence-id"));
            Assert.Equal("inbox", call.Option("--source"));
            Assert.NotNull(call.Option("--since"));
            Assert.Equal("100", call.Option("--limit"));
            Assert.Equal("0", call.Option("--offset"));
            Assert.Null(call.Body);
        });

        // 5. The person's words stayed at the provider: not in the chronicle, and not in the runtime's own log, read
        //    once the runtime has stopped writing it.
        var chronicle = GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(it, "journal", "list", "--campaign", campaign, "--limit", "500")));
        Assert.DoesNotContain(mark, chronicle.ToJsonString(), StringComparison.Ordinal);
        await GoldenPath.StopAsync(it);
        Assert.NotEmpty(Directory.GetFiles(it.Paths.LogsDirectory));
        foreach (var log in Directory.GetFiles(it.Paths.LogsDirectory))
        {
            var text = await File.ReadAllTextAsync(log, Ct);
            Assert.DoesNotContain(mark, text, StringComparison.Ordinal);

            // And no read went wrong on the way: a line said twice would have been refused and logged as a failure.
            Assert.DoesNotContain("The inbound sync of campaign", text, StringComparison.Ordinal);
            Assert.DoesNotContain("The inbound read of campaign", text, StringComparison.Ordinal);
        }
    }

    /// <summary>A moment as the runtime writes every timestamp: ISO-8601 UTC to the millisecond.</summary>
    private static string Moment(DateTime value) =>
        new DateTime(value.Ticks - (value.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc)
            .ToString("yyyy-MM-dd'T'HH:mm:ss'.000Z'", CultureInfo.InvariantCulture);

    private static TimeSpan Patience => TimeSpan.FromSeconds(90);

    /// <summary>The shipped defaults, routed to the official package, with the inbound read switched on or off.</summary>
    private static async Task WriteSettingsAsync(Installation it, bool inbound)
    {
        var settings = JsonNode.Parse(GoldenPath.Settings(it, new SettingsShape()))!.AsObject();
        settings["Inbound"] = new JsonObject { ["Enabled"] = inbound, ["SyncSeconds"] = 60 };
        await File.WriteAllTextAsync(it.Paths.UserSettingsFile, settings.ToJsonString(JasonJson.Options), Ct);
    }

    /// <summary>The campaign's first inbound_reply_observed line, waited for.</summary>
    private static async Task<JsonObject> LineAsync(Installation it, string campaign, TimeSpan within)
    {
        JsonObject? line = null;
        await WaitAsync(
            async () =>
            {
                var items = GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(
                    it, "journal", "list", "--campaign", campaign, "--kind", "inbound_reply_observed")))["items"]!.AsArray();
                line = items.Count > 0 ? items[^1]!.AsObject() : null;
                return line is not null;
            },
            within,
            "No inbound_reply_observed line appeared: the live edit may not have reached the runtime, or no read listed the reply.");
        return line!;
    }

    /// <summary>The check-in the line summoned, waited for: the item whose brief names the line.</summary>
    private static async Task<JsonObject> CheckInAsync(Installation it, string campaign, string lineId)
    {
        JsonObject? found = null;
        await WaitAsync(
            async () =>
            {
                foreach (var listed in (await ItemsAsync(it, campaign)).Where(item => (string?)item["role"] == "manager"))
                {
                    var item = GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(it, "workitem", "get", (string)listed["id"]!)));
                    if (item["context"]!.ToJsonString().Contains(lineId, StringComparison.Ordinal))
                    {
                        found = item;
                    }
                }

                return found is not null;
            },
            Patience,
            "No manager check-in named the line.");
        return found!;
    }

    private static async Task<List<JsonObject>> ReadsAsync(Installation it, string campaign) =>
        [.. (await ItemsAsync(it, campaign)).Where(item => (string?)item["operation"] == "conversation.list")];

    private static async Task<List<JsonObject>> ItemsAsync(Installation it, string campaign) =>
        [.. GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(it, "workitem", "list", "--campaign", campaign, "--limit", "200")))["items"]!
            .AsArray()
            .Select(item => item!.AsObject())];

    private static async Task WaitAsync(Func<Task<bool>> condition, TimeSpan within, string never)
    {
        var deadline = DateTime.UtcNow.Add(within);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, never);
            await Task.Delay(500, Ct);
        }
    }

    /// <summary>The arguments the enrolment's own contract publishes.</summary>
    private static JsonObject Enrolment() => new()
    {
        ["campaign"] = new JsonObject { ["external_id"] = Sequence.ToString(CultureInfo.InvariantCulture) },
        ["channel"] = "email",
        ["collision"] = "skip",
        ["start"] = new JsonObject { ["position"] = "first_step" },
        ["first_touch"] = "authored_delay",
    };

    /// <summary>A thread as the provider holds it: Marta wrote back, and her words carry the run's mark.</summary>
    private static JsonObject Thread(int id, DateTime moved, string mark)
    {
        var at = moved.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        return new JsonObject
        {
            ["id"] = id,
            ["channel"] = "email",
            ["isRead"] = false,
            ["subject"] = "Re: Q3 pilot " + mark,
            ["bodyPreview"] = "Could we talk on Thursday? " + mark,
            ["lastActivityDate"] = at,
            ["isLastMessagePlanned"] = false,
            ["contact"] = new JsonObject
            {
                ["id"] = Person,
                ["ownerId"] = 42,
                ["fullName"] = "Marta " + mark,
                ["email"] = Address,
                ["linkedInProfileUrl"] = null,
                ["phone"] = null,
                ["companyName"] = null,
                ["title"] = null,
                ["isDeleted"] = false,
            },
            ["sequence"] = new JsonObject { ["id"] = Sequence, ["name"] = "Q3 LatAm founders" },
            ["category"] = null,
            ["hasMeetingIntent"] = true,
            ["status"] = new JsonObject { ["state"] = "ok" },
            ["emailAccountId"] = 77,
            ["linkedInAccountId"] = null,
            ["aiDraft"] = null,
            ["messages"] = new JsonArray(new JsonObject
            {
                ["channel"] = "email",
                ["messageId"] = $"<m{id}@mail.example.com>",
                ["isOutbound"] = false,
                ["date"] = at,
                ["fromAddress"] = Address,
                ["subject"] = "Re: Q3 pilot " + mark,
                ["body"] = "Could we talk on Thursday? " + mark,
            }),
        };
    }
}
