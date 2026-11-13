using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Events;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Plugins.Manifest;
using Jason.Runtime.Tests.Documentation;
using Jason.Runtime.Tests.Plugins.Invocation;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Plugins.Demo;

/// <summary>
/// The demo provider, <c>plugins/demo</c>, through the real plugin host: the package the demo installation routes every
/// operation to. It requests nothing of the plugin fence, so nothing it does can leave its process; it answers each
/// operation it performs in that operation's own document; and it reports the sample story's written replies as events,
/// by its own cursor, counted from the moment the installation names under <c>Plugins:Config:demo:story_start</c>.
/// </summary>
public sealed class DemoPackageTests
{
    private const string Id = "demo";
    private const string DigestList = "plugins/demo.digests";

    private const string ContactId = "cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD";
    private const string CampaignId = "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD";
    private const string WorkItemId = "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRH";

    private static readonly string[] Performed =
        ["message.send", "social_invitation.send", "conversation.reply", "sender.list", "conversation.get", "conversation.list"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Shipped => Path.Combine(AppContext.BaseDirectory, "Fixtures", "plugins", Id);

    private static string StoryModule => Path.Combine(Shipped, "modules", "story.js");

    private static EventsContract Events => EventsCatalog.Find("events.read")!;

    /// <summary>The story as the desktop reads it: the literal between the export and its closing semicolon, as JSON.</summary>
    private static JsonObject Story()
    {
        var text = File.ReadAllText(StoryModule);
        const string Prefix = "export const story = ";
        Assert.StartsWith(Prefix, text, StringComparison.Ordinal);
        Assert.EndsWith(";\n", text, StringComparison.Ordinal);
        return JsonNode.Parse(text[Prefix.Length..^2])!.AsObject();
    }

    private static IEnumerable<JsonObject> People() => Story()["people"]!.AsArray().Select(person => person!.AsObject());

    private static string Text(JsonNode? node) => node!.GetValue<string>();

    /// <summary>A moment as the settings carry it, to the millisecond.</summary>
    private static DateTimeOffset ToTheMillisecond(DateTimeOffset moment) => DateTimeOffset.FromUnixTimeMilliseconds(moment.ToUnixTimeMilliseconds());

    private static string Pin(JsonObject? identifiers, string kind) => Text(identifiers![kind]);

    private static JsonObject PersonWithReplies() => People().First(person => person["replies"]!.AsArray().Count > 0);

    private static string Email(JsonObject person) =>
        Text(person["channels"]!.AsArray().Single(channel => Text(channel!["channel"]) == "email")!["value"]);

    // -----------------------------------------------------------------------------------------------------------------
    // The package and the fence.
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_manifest_is_valid_and_lists_exactly_the_operations_the_story_needs()
    {
        var read = ManifestReader.Read(File.ReadAllText(Path.Combine(Shipped, "plugin.yaml")), Id, Shipped, new ManifestBounds(3_600_000, 512));

        Assert.True(read.IsValid, string.Join("; ", read.Problems.Select(problem => problem.Code + " at " + problem.Path)));
        var manifest = read.Manifest!;
        Assert.Equal(PluginKind.Provider, manifest.Kind);
        Assert.Equal(Performed, manifest.Operations);
        Assert.True(manifest.ReadsEvents);
        Assert.All(manifest.Operations, operation => Assert.NotNull(OperationCatalog.Find(operation)));

        // Nothing is ever taken for later, so no send_after is taken and nothing reads or cancels such a send.
        Assert.Same(ManifestArguments.None, manifest.Arguments);
        Assert.DoesNotContain("message.status", manifest.Operations);
        Assert.DoesNotContain("message.cancel", manifest.Operations);
    }

    /// <summary>
    /// The proof that the demo sends to nobody: the package requests no program, no host, no variable, no file and no
    /// Runtime API family, so the plugin host hands it none, whatever a person's settings grant, and a send made to it
    /// cannot leave its process. Granting it everything changes nothing, because a grant only narrows a request.
    /// </summary>
    [Fact]
    public async Task It_requests_no_host_and_no_program_so_the_fence_gives_it_nothing_whatever_is_granted()
    {
        var manifest = ManifestReader.Read(File.ReadAllText(Path.Combine(Shipped, "plugin.yaml")), Id, Shipped, new ManifestBounds(3_600_000, 512)).Manifest!;
        Assert.Equal(new CapabilityRequests(null, null, null, null, null), manifest.Capabilities);

        await using var api = await StartAsync(storyStart: null, grantEverything: true);
        var registry = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginList, null, Ct);
        var plugin = Assert.Single(registry.Plugins, listed => listed.Id == Id);

        Assert.Equal(PluginStatus.Valid, plugin.Status);
        Assert.Null(plugin.Capabilities.Exec);
        Assert.Null(plugin.Capabilities.Http);
        Assert.Null(plugin.Capabilities.Env);
        Assert.Null(plugin.Capabilities.Api);
        Assert.Null(plugin.Capabilities.Files);
        Assert.Null(plugin.Capabilities.Subscriptions);

        // And no line of the package names a host function that reaches out.
        foreach (var file in Directory.EnumerateFiles(Shipped, "*.js", SearchOption.AllDirectories))
        {
            var code = File.ReadAllText(file);
            foreach (var reach in new[] { "host.exec", "host.http", "host.env", "host.appendLine", "host.call" })
            {
                Assert.DoesNotContain(reach, code, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>A package's version is its content: the shipped package is the last line of its digest list.</summary>
    [Fact]
    public void The_package_is_the_digest_its_version_has_on_the_list()
    {
        var root = RepositoryTree.Root();
        var package = Path.Combine(root, "plugins", Id);
        var version = YamlToJson.Convert(File.ReadAllText(Path.Combine(package, "plugin.yaml")))!["version"]!.GetValue<string>();
        var line = $"{version} {PackageDigest.Compute(package).Digest}";

        var path = Path.Combine(root, DigestList);
        Assert.True(File.Exists(path), $"{DigestList} is missing. Create it with a header comment and this line:{Environment.NewLine}{line}");
        var listed = File.ReadAllLines(path).Where(entry => entry.Length > 0 && !entry.StartsWith('#')).ToList();
        Assert.All(listed, entry => Assert.Matches(new Regex("^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*) sha256:[0-9a-f]{64}$"), entry));
        Assert.Equal(listed.Count, listed.Select(entry => entry.Split(' ')[0]).Distinct(StringComparer.Ordinal).Count());
        Assert.True(
            listed.Count > 0 && listed[^1] == line,
            $"The last line of {DigestList} is the shipped package's, version {version}. Append this line, after the last edit to the package:{Environment.NewLine}{line}");
    }

    // -----------------------------------------------------------------------------------------------------------------
    // The story's data: one module, data alone, one set.
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The story is one literal and nothing else, so the desktop's seed reads the same file the plugin imports, by the
    /// literal alone: no import, no function, no code to run, and the literal is strict JSON in ASCII.
    /// </summary>
    [Fact]
    public void The_story_module_is_one_exported_literal_of_strict_json_and_nothing_else()
    {
        var bytes = File.ReadAllBytes(StoryModule);
        Assert.All(bytes, value => Assert.True(value is 0x0A or (>= 0x20 and < 0x7F), $"byte {value} is not printable ASCII"));
        var text = File.ReadAllText(StoryModule);
        Assert.Matches(new Regex("\\Aexport const story = \\{.*\\};\\n\\z", RegexOptions.Singleline), text);
        Assert.Equal(1, Regex.Count(text, "^export ", RegexOptions.Multiline));

        // The literal parses as JSON, so it holds no expression, no function and no comment.
        using var parsed = JsonDocument.Parse(text["export const story = ".Length..^2]);
        Assert.Equal(JsonValueKind.Object, parsed.RootElement.ValueKind);
        Assert.Equal(
            ["seller", "operator", "project", "senders", "campaigns", "companies", "people"],
            parsed.RootElement.EnumerateObject().Select(member => member.Name));

        // Every other module of the package reads it through one module of lookups.
        var importers = Directory.EnumerateFiles(Shipped, "*.js", SearchOption.AllDirectories)
            .Where(file => File.ReadAllText(file).Contains("./story.js", StringComparison.Ordinal))
            .Select(Path.GetFileName);
        Assert.Equal(["people.js"], importers);
    }

    [Fact]
    public void The_story_is_invented_email_only_and_holds_the_shape_the_seed_reads()
    {
        var story = Story();
        Assert.Equal("dana.whitfield", Text(story["operator"]!["actor"]));
        Assert.EndsWith(".example", Text(story["seller"]!["domain"]), StringComparison.Ordinal);

        var campaigns = story["campaigns"]!.AsArray().Select(campaign => (Text(campaign!["key"]), Text(campaign["status"]))).ToList();
        Assert.Equal(["active", "active", "paused", "draft"], campaigns.Select(campaign => campaign.Item2));

        var companies = story["companies"]!.AsArray().Select(company => company!.AsObject()).ToList();
        Assert.Equal(18, companies.Count);
        Assert.All(companies, company => Assert.EndsWith(".example", Text(company["domain"]), StringComparison.Ordinal));

        var people = People().ToList();
        Assert.Equal(54, people.Count);
        Assert.Equal(people.Count, people.Select(person => Text(person["contact_id"])).Distinct(StringComparer.Ordinal).Count());
        Assert.All(people, person =>
        {
            Assert.Equal("demo-contact-" + Text(person["key"]), Text(person["contact_id"]));
            Assert.Contains(Text(person["company"]), companies.Select(company => Text(company["key"])));
            Assert.Contains(Text(person["campaign"]), campaigns.Select(campaign => campaign.Item1));

            var channels = person["channels"]!.AsArray().Select(channel => (Text(channel!["channel"]), Text(channel["value"]))).ToList();
            Assert.Equal(["email", "phone"], channels.Select(channel => channel.Item1));
            Assert.Matches(new Regex("^[a-z]+\\.[a-z]+@[a-z0-9-]+\\.example$"), channels[0].Item2);
            Assert.Matches(new Regex("^\\+1[2-9][0-9]{2}55501[0-9]{2}$"), channels[1].Item2);

            var sends = person["sends"]!.AsArray().Select(send => send!.AsObject()).ToList();
            Assert.All(sends, send =>
            {
                Assert.Equal("message.send", Text(send["operation"]));
                Assert.Equal("email", Text(send["channel"]));
                Assert.Contains(Text(send["approval"]), new[] { "given", "waiting" });
                Assert.False(string.IsNullOrWhiteSpace(Text(send["subject"])));
                Assert.False(string.IsNullOrWhiteSpace(Text(send["body"])));
            });

            // A reply answers a send that went, after the story's start, and nobody whose message still waits answers.
            Assert.All(person["replies"]!.AsArray(), reply =>
            {
                Assert.True(reply!["offset_seconds"]!.GetValue<int>() > 0);
                var answered = Assert.Single(sends, send => Text(send["key"]) == Text(reply["send"]));
                Assert.Equal("given", Text(answered["approval"]));
            });
        });

        // The numbers the story was given.
        int Count(string campaign, Func<JsonObject, int> what) => people.Where(person => Text(person["campaign"]) == campaign).Sum(what);
        int Sends(JsonObject person, string approval) => person["sends"]!.AsArray().Count(send => Text(send!["approval"]) == approval);
        Assert.Equal(24, Count("regional-fleets-q4", _ => 1));
        Assert.Equal(24, Count("regional-fleets-q4", person => Sends(person, "given")));
        Assert.Equal(10, Count("last-mile-pilot-invite", person => Sends(person, "waiting")));
        Assert.Equal(0, Count("last-mile-pilot-invite", person => person["replies"]!.AsArray().Count));
        Assert.Equal(0, Count("cold-chain-carriers", person => person["sends"]!.AsArray().Count));
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Each operation, held to its own document.
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_send_to_a_person_of_the_story_finds_them_and_pins_the_contact_the_events_name()
    {
        await using var api = await StartAsync(storyStart: null);
        var person = PersonWithReplies();

        var result = await InvokeAsync(api, "message.send", EmailSend(Email(person)));

        var succeeded = Succeeded("message.send", result);
        var item = succeeded.Result!["items"]![0]!;
        Assert.Equal("sent", Text(item["status"]));
        Assert.Equal(Text(person["contact_id"]), Pin(succeeded.ExternalIds, "contact"));
        Assert.Equal("found", Pin(succeeded.ExternalIdOrigins, "contact"));
        Assert.Equal(Text(item["reference"]), Pin(succeeded.ExternalIds, "send"));
        Assert.Equal("demo-send-" + WorkItemId[3..], Text(item["reference"]));
    }

    [Fact]
    public async Task A_send_to_somebody_the_story_does_not_hold_is_taken_under_a_contact_made_for_them_and_the_same_one_again()
    {
        await using var api = await StartAsync(storyStart: null);

        var first = Succeeded("message.send", await InvokeAsync(api, "message.send", EmailSend("someone.else@elsewhere.example")));
        var again = Succeeded("message.send", await InvokeAsync(api, "message.send", EmailSend("someone.else@elsewhere.example"), attempt: 2));

        Assert.Matches(new Regex("^demo-contact-x[0-9a-f]{8}$"), Pin(first.ExternalIds, "contact"));
        Assert.Equal("created", Pin(first.ExternalIdOrigins, "contact"));
        Assert.Equal(Pin(first.ExternalIds, "contact"), Pin(again.ExternalIds, "contact"));
        Assert.Equal(Pin(first.ExternalIds, "send"), Pin(again.ExternalIds, "send"));
        Assert.DoesNotContain(People(), person => Text(person["contact_id"]) == Pin(first.ExternalIds, "contact"));
    }

    [Fact]
    public async Task A_send_from_an_account_the_demo_does_not_hold_or_at_another_address_is_refused_in_the_document_s_codes()
    {
        await using var api = await StartAsync(storyStart: null);

        var unknown = EmailSend("someone.else@elsewhere.example");
        unknown["args"]!["sender"]!["external_id"] = "demo-sender-other";
        Failed("message.send", await InvokeAsync(api, "message.send", unknown), "sender_not_found");

        var moved = EmailSend("someone.else@elsewhere.example");
        moved["args"]!["sender"]!["address"] = "nobody@tillerwave.example";
        Failed("message.send", await InvokeAsync(api, "message.send", moved), "sender_mismatch");
    }

    [Fact]
    public async Task An_invitation_is_taken_from_the_linkedin_account_and_goes_to_nobody()
    {
        await using var api = await StartAsync(storyStart: null);
        var input = new JsonObject
        {
            ["args"] = new JsonObject
            {
                ["channel"] = "linkedin",
                ["sender"] = new JsonObject { ["external_id"] = "demo-sender-linkedin", ["address"] = null },
                ["note"] = "Glad to connect.",
            },
            ["contacts"] = new JsonArray(Contact("linkedin", "https://www.linkedin.com/in/someone-example")),
            ["campaign"] = Campaign(),
            ["idempotency_key"] = WorkItemId,
        };

        var succeeded = Succeeded("social_invitation.send", await InvokeAsync(api, "social_invitation.send", input));

        Assert.Equal("sent", Text(succeeded.Result!["items"]![0]!["status"]));
        Assert.Equal("created", Pin(succeeded.ExternalIdOrigins, "contact"));
    }

    [Fact]
    public async Task Sender_list_answers_the_two_invented_accounts_and_one_channel_when_asked_for_one()
    {
        await using var api = await StartAsync(storyStart: null);

        var all = Succeeded("sender.list", await InvokeAsync(api, "sender.list", SenderList(null))).Result!;
        Assert.Equal(["demo-sender-email", "demo-sender-linkedin"], all["items"]!.AsArray().Select(item => Text(item!["external_id"])));
        Assert.Equal("""{"email":"listed","linkedin":"listed"}""", all["channels"]!.ToJsonString());

        var email = Succeeded("sender.list", await InvokeAsync(api, "sender.list", SenderList("email"))).Result!;
        Assert.Equal("dana@tillerwave.example", Text(Assert.Single(email["items"]!.AsArray())!["address"]));
        Assert.Equal("""{"email":"listed","linkedin":"not_asked"}""", email["channels"]!.ToJsonString());
    }

    /// <summary>
    /// The written threads, as a person's read sees them: before the story starts there are none; after it, the person's
    /// thread holds the message sent at the start and, once its moment has come, the reply written to it, and the
    /// thread's last activity is that reply's moment to the millisecond — the moment its event says.
    /// </summary>
    [Fact]
    public async Task A_person_s_thread_shows_the_send_and_the_reply_whose_moment_has_come()
    {
        var person = PersonWithReplies();
        var reply = person["replies"]![0]!;
        var start = ToTheMillisecond(DateTimeOffset.UtcNow.AddSeconds(-reply["offset_seconds"]!.GetValue<int>() - 5));
        var thread = "demo-thread-" + Text(reply["send"]);

        await using (var before = await StartAsync(storyStart: null))
        {
            var none = Succeeded("conversation.list", await InvokeAsync(before, "conversation.list", ThreadsOf(person))).Result!;
            Assert.Empty(none["items"]!.AsArray());
            Failed("conversation.get", await InvokeAsync(before, "conversation.get", Thread(thread)), "conversation_not_found");
        }

        await using var api = await StartAsync(storyStart: start);

        var listed = Succeeded("conversation.list", await InvokeAsync(api, "conversation.list", ThreadsOf(person))).Result!;
        var item = Assert.Single(listed["items"]!.AsArray())!;
        Assert.Equal(thread, Text(item["external_id"]));
        Assert.True(item["unread"]!.GetValue<bool>());
        var moment = start.AddSeconds(reply["offset_seconds"]!.GetValue<int>());
        Assert.Equal(moment.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture), Text(item["last_activity_at"]));

        var got = Succeeded("conversation.get", await InvokeAsync(api, "conversation.get", Thread(thread))).Result!;
        var messages = got["messages"]!.AsArray();
        Assert.Equal(["outbound", "inbound"], messages.Select(message => Text(message!["direction"])));
        Assert.Equal(Text(reply["body"]), Text(messages[1]!["body"]));
        Assert.Equal(Email(person), Text(messages[1]!["from"]));
    }

    [Fact]
    public async Task A_reply_into_a_written_thread_is_taken_and_one_into_another_person_s_thread_is_refused()
    {
        var person = PersonWithReplies();
        var other = People().First(candidate => Text(candidate["key"]) != Text(person["key"]) && candidate["sends"]!.AsArray().Count > 0);
        var thread = "demo-thread-" + Text(person["replies"]![0]!["send"]);
        await using var api = await StartAsync(storyStart: DateTimeOffset.UtcNow);

        var taken = Succeeded("conversation.reply", await InvokeAsync(api, "conversation.reply", Reply(thread, Text(person["contact_id"]), Email(person))));
        Assert.Equal(thread, Text(taken.Result!["items"]![0]!["thread_external_id"]));
        Assert.Equal(Text(person["contact_id"]), Pin(taken.ExternalIds, "contact"));

        Failed("conversation.reply", await InvokeAsync(api, "conversation.reply", Reply(thread, Text(other["contact_id"]), Email(other))), "thread_contact_mismatch");
        Failed("conversation.reply", await InvokeAsync(api, "conversation.reply", Reply("demo-thread-none", Text(person["contact_id"]), Email(person))), "conversation_not_found");
    }

    // -----------------------------------------------------------------------------------------------------------------
    // The events: written replies, by the package's own cursor.
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Before_the_story_starts_a_read_answers_an_empty_page_with_a_cursor()
    {
        await using var api = await StartAsync(storyStart: null);

        var page = Page(await ReadAsync(api, ReadInput(since: DateTimeOffset.UtcNow.AddDays(-30))));

        Assert.Empty(page["events"]!.AsArray());
        Assert.False(page["has_more"]!.GetValue<bool>());
        var again = Page(await ReadAsync(api, ReadInput(cursor: Text(page["cursor"]))));
        Assert.Empty(again["events"]!.AsArray());
    }

    /// <summary>
    /// The read returns the replies whose moment has come, in the order they landed, each a thread the other side moved,
    /// naming the person by the contact the sends pin and the thread the person's read lists; a reply still ahead is not
    /// returned. Read two at a time, the cursor moves on with no reply lost and none repeated, and a read from the last
    /// cursor finds nothing more until another reply lands.
    /// </summary>
    [Fact]
    public async Task The_read_moves_by_its_cursor_and_returns_the_written_replies_that_have_landed()
    {
        var start = ToTheMillisecond(DateTimeOffset.UtcNow.AddSeconds(-300));
        await using var api = await StartAsync(storyStart: start);
        var written = People()
            .SelectMany(person => person["replies"]!.AsArray().Select(reply => (Person: person, Reply: reply!.AsObject())))
            .Select(entry => (entry.Person, entry.Reply, At: start.AddSeconds(entry.Reply["offset_seconds"]!.GetValue<int>())))
            .OrderBy(entry => entry.At)
            .ToList();

        var before = DateTimeOffset.UtcNow;
        var seen = new List<JsonObject>();
        var page = Page(await ReadAsync(api, ReadInput(since: start.AddDays(-30), limit: 2)));
        seen.AddRange(page["events"]!.AsArray().Select(found => found!.AsObject()));
        var reads = 1;
        while (page["has_more"]!.GetValue<bool>())
        {
            Assert.Equal(2, page["events"]!.AsArray().Count);
            page = Page(await ReadAsync(api, ReadInput(cursor: Text(page["cursor"]), limit: 2)));
            seen.AddRange(page["events"]!.AsArray().Select(found => found!.AsObject()));
            Assert.True(++reads < 20, "the cursor never reached the end");
        }

        var after = DateTimeOffset.UtcNow;

        // Every reply whose moment had come before the reads began, none whose moment was still ahead after them.
        var landed = written.Where(entry => entry.At <= before).ToList();
        Assert.True(landed.Count >= 2, "the story landed too few replies in five minutes to read a page past the first");
        Assert.True(seen.Count >= landed.Count);
        Assert.DoesNotContain(written.Skip(seen.Count), entry => entry.At <= before);
        Assert.All(written.Take(seen.Count), entry => Assert.True(entry.At <= after));
        Assert.Contains(written, entry => entry.At > after);

        for (var i = 0; i < seen.Count; i++)
        {
            var (person, reply, at) = written[i];
            var found = seen[i];
            Assert.Equal("thread_moved", Text(found["kind"]));
            Assert.Equal("other_side", Text(found["moved_by"]));
            Assert.Equal(Text(person["contact_id"]), Text(found["contact"]));
            Assert.Equal("demo-thread-" + Text(reply["send"]), Text(found["thread"]));
            Assert.Null(found["campaign"]);
            Assert.Equal(at, DateTimeOffset.Parse(Text(found["occurred_at"]), System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(reply["meeting_intent"]!.GetValue<bool>(), found["has_meeting_intent"]!.GetValue<bool>());
        }

        Assert.Equal(seen.Count, seen.Select(found => Text(found["id"])).Distinct(StringComparer.Ordinal).Count());

        // The page after the last holds nothing that was read already.
        var rest = Page(await ReadAsync(api, ReadInput(cursor: Text(page["cursor"]))));
        Assert.DoesNotContain(rest["events"]!.AsArray(), found => seen.Any(read => Text(read["id"]) == Text(found!["id"])));
    }

    /// <summary>
    /// A reply exists only for a person the package knows, and only in the thread of a send the story says went: every
    /// event names a contact of the story and that person's own thread, and nobody the package made a contact for is
    /// ever named, so a reply to somebody never written to cannot reach a campaign.
    /// </summary>
    [Fact]
    public async Task A_reply_exists_only_for_a_person_the_package_knows_in_the_thread_of_a_send_that_went()
    {
        var start = DateTimeOffset.UtcNow.AddHours(-1);
        await using var api = await StartAsync(storyStart: start);
        var people = People().ToDictionary(person => Text(person["contact_id"]), StringComparer.Ordinal);

        var page = Page(await ReadAsync(api, ReadInput(since: start.AddDays(-1), limit: 100)));
        var events = page["events"]!.AsArray().Select(found => found!.AsObject()).ToList();

        Assert.Equal(People().Sum(person => person["replies"]!.AsArray().Count), events.Count);
        Assert.All(events, found =>
        {
            var person = people[Text(found["contact"])];
            var given = person["sends"]!.AsArray().Where(send => Text(send!["approval"]) == "given").Select(send => "demo-thread-" + Text(send!["key"]));
            Assert.Contains(Text(found["thread"]), given);
        });
        Assert.DoesNotContain(events, found => Text(found["contact"]).StartsWith("demo-contact-x", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_cursor_the_package_did_not_give_is_unknown()
    {
        await using var api = await StartAsync(storyStart: DateTimeOffset.UtcNow);

        var failed = Assert.IsType<InvocationOutcome.Failed>((await ReadAsync(api, ReadInput(cursor: "not-a-demo-cursor"))).Outcome).Error;

        Assert.Equal(("events_cursor_unknown", FailureClass.Permanent), (failed.Code, failed.Class));
        Assert.Contains(Events.FailureCodes, declared => declared.Code == failed.Code && declared.Class == failed.Class);
    }

    // -----------------------------------------------------------------------------------------------------------------
    // The harness.
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A runtime with the shipped package copied in, its story started at the moment given or not at all, and, where
    /// asked, every capability granted that a person could grant: the fence still gives the package nothing it did not
    /// request.
    /// </summary>
    private static Task<RuntimeApiFixture> StartAsync(DateTimeOffset? storyStart, bool grantEverything = false) =>
        RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths =>
            {
                File.WriteAllText(paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
                TestPlugins.Copy(Shipped, paths.PluginPackageDirectory(Id));
                if (grantEverything)
                {
                    TestPlugins.Grant(paths, Id, exec: ["*"], http: ["*"], env: ["*"], api: ["read"]);
                }

                if (storyStart is { } start)
                {
                    var settings = JsonNode.Parse(File.ReadAllText(paths.UserSettingsFile))!.AsObject();
                    settings["Plugins"] ??= new JsonObject();
                    settings["Plugins"]!["Config"] = new JsonObject
                    {
                        [Id] = new JsonObject { ["story_start"] = start.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture) },
                    };
                    File.WriteAllText(paths.UserSettingsFile, settings.ToJsonString());
                }
            },
            configureServices: services => services.AddSingleton<IPluginHostLocator>(new JasonDllLocator()));

    private static async Task<PluginInvocationResult> InvokeAsync(RuntimeApiFixture api, string operation, JsonObject input, int attempt = 1)
    {
        // The input is what the operation's document says it is before anything runs.
        Assert.Empty(OperationCatalog.Find(operation)!.CheckInput(input, PatternTiming.Default));
        return await PluginInvokerTests.InvokeAsync(
            api,
            new PluginInvocationRequest(Id, operation, input, null, "att_01K0DEMOPROVIDER", AttemptId: "att_01K0DEMOPROVIDER",
                AttemptNumber: attempt, WorkItemId: WorkItemId, CampaignId: CampaignId),
            Ct);
    }

    private static async Task<PluginInvocationResult> ReadAsync(RuntimeApiFixture api, JsonObject input)
    {
        Assert.Empty(SchemaValidator.Validate(input, Events.InputSchema));
        return await PluginInvokerTests.InvokeAsync(
            api,
            new PluginInvocationRequest(Id, "events.read", input, null, "evr_01K0DEMOEVENTSREAD", Purpose: InvocationPurpose.Events),
            Ct);
    }

    /// <summary>A success held to the operation's own output schema and to the identifiers it declares.</summary>
    private static InvocationOutcome.Succeeded Succeeded(string operation, PluginInvocationResult result)
    {
        if (result.Outcome is not InvocationOutcome.Succeeded succeeded)
        {
            var detail = result.Outcome is InvocationOutcome.Failed failed ? failed.Error.Code + ": " + failed.Error.Message : result.Outcome.ToString();
            Assert.Fail($"{operation} did not succeed: {detail}");
            throw new InvalidOperationException();
        }

        var contract = OperationCatalog.Find(operation)!;
        Assert.Empty(OutcomeContract.CheckResult(contract, succeeded.Result));
        Assert.Empty(OutcomeContract.CheckExternalIds(contract, succeeded.ExternalIds));
        return succeeded;
    }

    /// <summary>A failure in a code the operation's document declares, under the class it gives it.</summary>
    private static void Failed(string operation, PluginInvocationResult result, string code)
    {
        var failed = Assert.IsType<InvocationOutcome.Failed>(result.Outcome).Error;
        Assert.Equal(code, failed.Code);
        Assert.Contains(OperationCatalog.Find(operation)!.FailureCodes, declared => declared.Code == failed.Code && declared.Class == failed.Class);
    }

    /// <summary>A page held to the events contract, carrying no pins.</summary>
    private static JsonObject Page(PluginInvocationResult result)
    {
        if (result.Outcome is not InvocationOutcome.Succeeded succeeded)
        {
            var detail = result.Outcome is InvocationOutcome.Failed failed ? failed.Error.Code + ": " + failed.Error.Message : result.Outcome.ToString();
            Assert.Fail("events.read did not succeed: " + detail);
            throw new InvalidOperationException();
        }

        Assert.Empty(Events.CheckResult(succeeded.Result));
        Assert.Null(succeeded.ExternalIds);
        return succeeded.Result!.AsObject();
    }

    private static JsonObject Contact(string channel, string value, string? pin = null) => new()
    {
        ["id"] = ContactId,
        ["first_name"] = "Sample",
        ["last_name"] = "Person",
        ["company"] = null,
        ["title"] = null,
        ["time_zone"] = null,
        ["channels"] = new JsonArray(new JsonObject { ["channel"] = channel, ["value"] = value, ["primary"] = true }),
        ["external_ids"] = pin is null ? new JsonObject() : new JsonObject { ["contact"] = pin },
    };

    private static JsonObject Campaign() => new()
    {
        ["id"] = CampaignId,
        ["name"] = "Regional Fleets Q4",
        ["status"] = "active",
        ["external_ids"] = new JsonObject(),
    };

    private static JsonObject EmailSend(string address) => new()
    {
        ["args"] = new JsonObject
        {
            ["channel"] = "email",
            ["sender"] = new JsonObject { ["external_id"] = "demo-sender-email", ["address"] = "dana@tillerwave.example" },
            ["message"] = new JsonObject { ["format"] = "email", ["subject"] = "A question about dispatch", ["body"] = "Hello." },
        },
        ["contacts"] = new JsonArray(Contact("email", address)),
        ["campaign"] = Campaign(),
        ["idempotency_key"] = WorkItemId,
    };

    private static JsonObject SenderList(string? channel)
    {
        var args = new JsonObject { ["owners"] = "own", ["limit"] = 20, ["offset"] = 0 };
        if (channel is not null)
        {
            args["sender_channel"] = channel;
        }

        return new JsonObject { ["args"] = args, ["campaign"] = Campaign() };
    }

    private static JsonObject ThreadsOf(JsonObject person) => new()
    {
        ["args"] = new JsonObject { ["limit"] = 20, ["offset"] = 0, ["source"] = "all" },
        ["contacts"] = new JsonArray(new JsonObject { ["id"] = ContactId, ["external_ids"] = new JsonObject { ["contact"] = Text(person["contact_id"]) } }),
    };

    private static JsonObject Thread(string id) => new()
    {
        ["args"] = new JsonObject { ["thread"] = new JsonObject { ["external_id"] = id }, ["limit"] = 20, ["offset"] = 0 },
    };

    private static JsonObject Reply(string thread, string pin, string address)
    {
        var contact = new JsonObject
        {
            ["id"] = ContactId,
            ["channels"] = new JsonArray(new JsonObject { ["channel"] = "email", ["value"] = address, ["primary"] = true }),
            ["external_ids"] = new JsonObject { ["contact"] = pin },
        };
        return new JsonObject
        {
            ["args"] = new JsonObject { ["channel"] = "email", ["thread"] = new JsonObject { ["external_id"] = thread }, ["body"] = "Thanks, Thursday works." },
            ["contacts"] = new JsonArray(contact),
            ["campaign"] = Campaign(),
            ["idempotency_key"] = WorkItemId,
        };
    }

    private static JsonObject ReadInput(DateTimeOffset? since = null, string? cursor = null, int limit = 100) => new()
    {
        ["cursor"] = cursor,
        ["since"] = since?.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture),
        ["until"] = null,
        ["limit"] = limit,
    };
}
