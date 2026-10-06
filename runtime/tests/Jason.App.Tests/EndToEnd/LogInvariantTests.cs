using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Runtime.Tests.Logging;

namespace Jason.App.Tests.EndToEnd;

/// <summary>
/// The log the real executable writes, read back line by line and held to the one list of properties a line may
/// carry. Values a person types into this runtime go in carrying a sentinel, and these are the ones planted: a
/// campaign's context, the reason a campaign was updated, a contact's first name, email address and custom field, a
/// work item's brief, a provider operation's argument that the provider's refusal quotes, a role the claim cannot
/// launch, which its refusal quotes, a question a running role asks and its reason for asking, a person's answer and
/// the reason for it, the reason a running work item was
/// cancelled, an unloadable package's manifest, a refused setting, a setting of the wrong type, a reload's reason and
/// a timestamp the request door cannot read, and the keys a campaign and a work item were created with — made, repeated,
/// sent again with a different request and once too long to be a key. None of them may appear anywhere in the log,
/// whole or in part.
/// </summary>
/// <remarks>
/// <para>
/// The scan of the source (<c>LogTemplateInventoryTests</c>) holds every template the runtime writes. This holds what
/// no template shows: the properties a log scope adds, the framework's own lines, an exception written whole, and a
/// sentence composed at run time from what the validator or the binder was handed. Every property of every event is
/// checked, framework events included, and the log level is the loudest the settings accept, so nothing the run
/// wrote was filtered out before it was read.
/// </para>
/// <para>
/// The checks that matter are made before the stop is judged: a regression that makes the runtime fail at stopping
/// is still read for what it logged on the way. Each request that plants a sentinel has its answer checked when it is
/// made — the question raised, the answer recorded, the cancellation finished — and afterwards the run proves it
/// reached the rest of what it set out to — each refusal, the reload, the attempts — so the absence of a sentinel is
/// the absence of a leak and not of the line that would have carried it.
/// </para>
/// </remarks>
public class LogInvariantTests
{
    private const string PluginId = "fake-provider";

    /// <summary>A package directory beside the real one whose manifest no load accepts.</summary>
    private const string Unloadable = "unloadable";

    /// <summary>The line the runtime writes once for an edit of a section it refuses.</summary>
    private const string RefusedTemplate = "Edited {Section} settings were refused; the runtime keeps the last ones that validated: {Failure}";

    /// <summary>
    /// What every sentinel begins with. A log that carries any of it carries part of a sentinel, so a value cut short
    /// before the run's mark is found as surely as a whole one.
    /// </summary>
    private const string SentinelPrefix = "SENTINEL_";

    /// <summary>How long an edit of the settings file may take to reach the runtime and be refused.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(30);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_line_the_runtime_logs_carries_only_reviewed_properties_and_nothing_a_person_typed()
    {
        // One prefix and one mark for the run, in every sentinel: a log that carried any of them, whole or in part,
        // carries the prefix, the mark or both.
        var mark = Guid.NewGuid().ToString("N");
        var sentinels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["a campaign's context"] = SentinelPrefix + "CTX_" + mark,
            ["a campaign update's reason"] = SentinelPrefix + "UPDATE_" + mark,
            ["a contact's first name"] = SentinelPrefix + "NAME_" + mark,
            ["a contact's email address"] = SentinelPrefix + "EMAIL_" + mark,
            ["a contact's custom field"] = SentinelPrefix + "FIELD_" + mark,
            ["a work item's brief"] = SentinelPrefix + "BRIEF_" + mark,
            ["a provider operation's argument"] = SentinelPrefix + "INPUT_" + mark,
            ["a role's question"] = SentinelPrefix + "QUESTION_" + mark,
            ["a role's reason for asking"] = SentinelPrefix + "ASKED_" + mark,
            ["a person's answer"] = SentinelPrefix + "ANSWER_" + mark,
            ["a person's reason for the answer"] = SentinelPrefix + "ANSWERED_" + mark,
            ["a cancellation's reason"] = SentinelPrefix + "CANCEL_" + mark,
            ["an unloadable package's manifest"] = SentinelPrefix + "MANIFEST_" + mark,
            ["a refused trigger"] = SentinelPrefix + "TRIGGER_" + mark,
            ["a setting of the wrong type"] = SentinelPrefix + "TYPE_" + mark,
            ["a reload's reason"] = SentinelPrefix + "REASON_" + mark,
            ["a timestamp the door cannot read"] = SentinelPrefix + "SINCE_" + mark,
            ["a campaign's idempotency key"] = SentinelPrefix + "CKEY_" + mark,
            ["a work item's idempotency key"] = SentinelPrefix + "WKEY_" + mark,

            // A role name is lowercase with hyphens, and is what the message of an attempt the claim refuses quotes.
            ["a role nothing can launch"] = "sentinel-role-" + mark,
        };

        using var it = GoldenPath.Create("log-invariant");
        var workspace = Path.Combine(it.Root, "provider-account");
        Directory.CreateDirectory(workspace);
        await File.WriteAllTextAsync(it.Paths.UserSettingsFile, Settings(workspace), Ct);

        // Installing a plugin is copying its directory. The second copy is the candidate that does not load: its
        // manifest names a kind there is no such thing as, so the load at startup is rejected whole.
        var package = Path.Combine(GoldenPath.TestTree, "Fixtures", "plugins", PluginId);
        GoldenPath.Copy(package, it.Paths.PluginPackageDirectory(PluginId));
        var unloadable = it.Paths.PluginPackageDirectory(Unloadable);
        GoldenPath.Copy(package, unloadable);
        var manifest = Path.Combine(unloadable, "plugin.yaml");
        await File.WriteAllTextAsync(
            manifest,
            (await File.ReadAllTextAsync(manifest, Ct))
                .Replace("id: " + PluginId, "id: " + Unloadable, StringComparison.Ordinal)
                .Replace("kind: provider", "kind: " + sentinels["an unloadable package's manifest"], StringComparison.Ordinal),
            Ct);

        await GoldenPath.StartAsync(it);
        GoldenPath.AssertSuccess(await GoldenPath.JasonAsync(it, "runtime", "status"));

        // The candidate goes, and the reload that activates what is left is given a reason somebody typed.
        Directory.Delete(unloadable, recursive: true);
        var reloaded = GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(
            it, "plugin", "reload", "--reason", sentinels["a reload's reason"])));
        Assert.True((bool)reloaded["activated"]!);
        GoldenPath.AssertTheStandInAnswered(Assert.Single(reloaded["plugins"]!.AsArray())!.AsObject());

        var campaign = (string)GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(
            it, "campaign", "create", "--name", "Log guard", "--context", Object("brief", sentinels["a campaign's context"]))))["id"]!;
        GoldenPath.AssertSuccess(await GoldenPath.JasonAsync(
            it, "campaign", "update", campaign, "--name", "Log guard, renamed", "--reason", sentinels["a campaign update's reason"]));
        GoldenPath.AssertSuccess(await GoldenPath.JasonAsync(
            it,
            "contact",
            "create",
            "--first-name",
            sentinels["a contact's first name"],
            "--email",
            sentinels["a contact's email address"] + "@example.test",
            "--custom",
            Object("note", sentinels["a contact's custom field"])));

        // A key a caller chose, on each creating verb: made, repeated, sent again with a different request, and once
        // too long to be a key. The work item's goes into a campaign nobody starts, so nothing about it is ever run.
        var keyedCampaign = await KeysAsync(
            it,
            sentinels["a campaign's idempotency key"],
            key => ["campaign", "create", "--name", "Keyed", "--idempotency-key", key],
            key => ["campaign", "create", "--name", "Keyed, but another", "--idempotency-key", key]);
        await KeysAsync(
            it,
            sentinels["a work item's idempotency key"],
            key => ["workitem", "create", keyedCampaign, "--kind", "ai_role", "--role", "researcher", "--idempotency-key", key],
            key => ["workitem", "create", keyedCampaign, "--kind", "ai_role", "--role", "researcher", "--priority", "5", "--idempotency-key", key]);

        // A word where a moment belongs: the CLI hands it on as it was typed, so the runtime's own reading refuses it.
        var unreadable = await GoldenPath.JasonAsync(it, "journal", "list", "--since", sentinels["a timestamp the door cannot read"]);

        // Three attempts: an agent that does its job, a provider operation the provider refuses for good — with a
        // message that quotes the argument it could not find — and a role no command can start, which the claim
        // refuses before any handler exists, with a message that quotes the role.
        GoldenPath.AssertSuccess(await GoldenPath.JasonAsync(
            it, "role", "add", "fake", "--entry-command", "dotnet", "--entry-command", FakeAgentHostLocator.Dll, "--entry-command", "succeed"));
        GoldenPath.AssertSuccess(await GoldenPath.JasonAsync(it, "role", "add", sentinels["a role nothing can launch"]));
        var agent = (string)GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(
            it,
            "workitem",
            "create",
            campaign,
            "--kind",
            "ai_role",
            "--role",
            "fake",
            "--context",
            Object("brief", sentinels["a work item's brief"]),
            "--result-format",
            "{\"type\":\"object\",\"description\":\"a summary\"}")))["id"]!;
        var provider = (string)GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(
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
                ["campaign"] = new JsonObject { ["external_id"] = sentinels["a provider operation's argument"] },
            }.ToJsonString(JasonJson.Options))))["id"]!;
        var unlaunchable = (string)GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(
            it, "workitem", "create", campaign, "--kind", "ai_role", "--role", sentinels["a role nothing can launch"])))["id"]!;
        GoldenPath.AssertSuccess(await GoldenPath.JasonAsync(it, "campaign", "start", campaign));

        var refused =await GoldenPath.PollAsync(it, provider, item => GoldenPath.Finished((string?)item["status"]));
        Assert.Equal("failed", (string?)refused["status"]);
        var failure = Assert.Single(refused["attempts"]!.AsArray())!["error"]!;
        Assert.Equal("campaign_not_found", (string?)failure["code"]);
        Assert.Equal("permanent", (string?)failure["class"]);

        // The claim's own refusal. Its message quotes the sentinel, which is what makes the absence of it from the log
        // worth something: the line that says how the attempt ended must say the code and not the message.
        var unstarted = await GoldenPath.PollAsync(it, unlaunchable, item => GoldenPath.Finished((string?)item["status"]));
        Assert.Equal("failed", (string?)unstarted["status"]);
        var claimRefusal = Assert.Single(unstarted["attempts"]!.AsArray())!;
        Assert.Equal("role_not_launchable", (string?)claimRefusal["error"]!["code"]);
        Assert.Contains(sentinels["a role nothing can launch"], (string?)claimRefusal["error"]!["message"], StringComparison.Ordinal);
        var claimRefusalAttempt = (string)claimRefusal["id"]!;

        // The launch is written when the agent's process has ended, which is after the line that says so.
        GoldenPath.AssertSucceeded(await GoldenPath.PollAsync(
            it,
            agent,
            item => (string?)item["status"] == "succeeded" && item["attempts"]?.AsArray().FirstOrDefault()?["launch"]?["pid"] is not null));

        // A question asked from a running attempt and a person's answer to it, each with its reason. The role keeps
        // its attempt alive until it is cancelled, so the question is asked by the attempt that holds the work, as a
        // role would ask it; the cancellation that ends it carries a reason of its own.
        GoldenPath.AssertSuccess(await GoldenPath.JasonAsync(
            it, "role", "add", "asker", "--entry-command", "dotnet", "--entry-command", FakeAgentHostLocator.Dll, "--entry-command", "hang"));
        var asking = (string)GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(
            it, "workitem", "create", campaign, "--kind", "ai_role", "--role", "asker", "--context", Object("brief", "ask a person"))))["id"]!;
        var holding = await GoldenPath.PollAsync(
            it,
            asking,
            item => item["attempts"]?.AsArray().FirstOrDefault() is { } attempt
                && (string?)attempt["status"] == "running"
                && attempt["last_heartbeat_at"] is not null);
        var attemptId = (string)holding["attempts"]![0]!["id"]!;
        var decision = (string)GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(
            it,
            "decision",
            "raise",
            asking,
            "--attempt",
            attemptId,
            "--question",
            sentinels["a role's question"],
            "--option",
            "keep going",
            "--option",
            "stop",
            "--reason",
            sentinels["a role's reason for asking"])))["id"]!;
        var answered = GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(
            it,
            "--actor",
            "human:ada",
            "decision",
            "answer",
            decision,
            "--answer",
            sentinels["a person's answer"],
            "--option",
            "stop",
            "--reason",
            sentinels["a person's reason for the answer"])));
        Assert.Equal("answered", (string?)answered["status"]);
        GoldenPath.AssertSuccess(await GoldenPath.JasonAsync(it, "workitem", "cancel", asking, "--reason", sentinels["a cancellation's reason"]));
        Assert.Equal("cancelled", (string?)(await GoldenPath.PollAsync(it, asking, item => GoldenPath.Finished((string?)item["status"])))["status"]);

        // A setting the validator refuses, and one the binder cannot read, each waited for until the runtime has said
        // so — or until the wait runs out, which the end of the test reports with what the log did say.
        await File.WriteAllTextAsync(
            it.Paths.UserSettingsFile,
            Settings(workspace, settings => settings["Manager"] = new JsonObject { ["Triggers"] = new JsonArray(sentinels["a refused trigger"]) }),
            Ct);
        await LoggedAsync(it, Refusal("Manager"));

        await File.WriteAllTextAsync(
            it.Paths.UserSettingsFile,
            Settings(workspace, settings => settings["Dispatcher"]!["Enabled"] = sentinels["a setting of the wrong type"]),
            Ct);
        await LoggedAsync(it, Refusal("Dispatcher"));

        await File.WriteAllTextAsync(it.Paths.UserSettingsFile, Settings(workspace), Ct);

        var stopped = await GoldenPath.JasonAsync(it, "runtime", "stop");
        var events = Events(it.Paths);

        AssertEveryPropertyIsReviewed(events);
        AssertNoSentinel(it.Paths, events, mark, sentinels);

        GoldenPath.AssertSuccess(stopped);
        Assert.False(GoldenPath.IsRunning(it.Pid!.Value), "The runtime process was still alive after runtime stop returned.");
        it.Pid = null;

        // What the run set out to reach, it reached: otherwise a missing sentinel would only be a missing line.
        Assert.Equal(ExitCodes.ApiError, unreadable.ExitCode);
        Assert.Equal("invalid_request", (string?)GoldenPath.Json(unreadable)["error"]?["code"]);
        AssertSeen(events, "a refusal of the edited Manager section", Refusal("Manager"));
        AssertSeen(events, "a refusal of the edited Dispatcher section", Refusal("Dispatcher"));
        AssertSeen(events, "the candidate that did not load", Template("Plugin candidate {Directory} is not loadable: {Codes}"));
        AssertSeen(events, "the reload that activated", Template("Plugin reload activated snapshot {SnapshotId} with {Plugins} plugins"));
        AssertSeen(events, "the provider invocation's end", Template("Plugin invocation {InvocationId} (correlation {CorrelationId}) ended: exit {ExitCode}, {Verdict} in {DurationMs} ms"));
        AssertSeen(events, "the agent's end", Template("Attempt {AttemptId} of work item {WorkItemId} ended with exit code {ExitCode}"));
        AssertSeen(
            events,
            "an attempt ended at the claim",
            line => (string?)line["@mt"] == "Attempt {AttemptId} of work item {WorkItemId} ended at the claim: {Code}"
                && (string?)line["Code"] == "role_not_launchable"
                && (string?)line["AttemptId"] == claimRefusalAttempt
                && (string?)line["WorkItemId"] == unlaunchable
                && (string?)line["CampaignId"] == campaign);
        AssertSeen(events, "a line of the attempt's scope", line => (string?)line["CampaignId"] == campaign && line["@mt"]?.GetValue<string>().StartsWith("Plugin", StringComparison.Ordinal) == true);
        AssertSeen(events, "a request line", line => line["RequestPath"] is not null && line["StatusCode"] is not null);
        AssertSeen(events, "a framework line", line => line["address"] is not null);
    }

    /// <summary>
    /// The settings of this run: the loudest log level the validator accepts, a dispatcher that ticks every second,
    /// one attempt per item, the package's grant, and the route that sends provider work to it.
    /// </summary>
    private static string Settings(string workspace, Action<JsonObject>? edit = null)
    {
        var settings = new JsonObject
        {
            ["Logging"] = new JsonObject { ["MinimumLevel"] = "Verbose" },
            ["Dispatcher"] = new JsonObject
            {
                ["TickSeconds"] = 1,
                ["RetryDelaySeconds"] = 0,
                ["ProviderOp"] = new JsonObject { ["MaxAttempts"] = 1 },
            },
            ["Plugins"] = new JsonObject
            {
                ["Grants"] = new JsonObject { [PluginId] = new JsonObject { ["Exec"] = new JsonArray("*") } },
            },
            ["Routes"] = new JsonObject
            {
                ["Default"] = new JsonObject
                {
                    ["Plugin"] = PluginId,
                    ["Binding"] = new JsonObject { ["workspace"] = workspace },
                },
            },
        };

        edit?.Invoke(settings);
        return settings.ToJsonString(JasonJson.Options);
    }

    private static string Object(string key, string value) => new JsonObject { [key] = value }.ToJsonString(JasonJson.Options);

    private static Func<JsonObject, bool> Template(string template) => line => (string?)line["@mt"] == template;

    private static Func<JsonObject, bool> Refusal(string section) =>
        line => (string?)line["@mt"] == RefusedTemplate && (string?)line["Section"] == section;

    /// <summary>Waits for a line the runtime is about to write. Whether it came is asserted at the end, after the leaks.</summary>
    private static async Task LoggedAsync(Installation it, Func<JsonObject, bool> wanted)
    {
        var deadline = DateTime.UtcNow.Add(Settle);
        while (DateTime.UtcNow < deadline && !Events(it.Paths, whole: false).Any(wanted))
        {
            await Task.Delay(200, Ct);
        }
    }

    /// <summary>
    /// Every event in every log file, in order. Read while the runtime runs, a last line may still be on its way and
    /// is passed over; read after it stopped, every line is a whole event or the test says which one is not.
    /// </summary>
    private static List<JsonObject> Events(JasonPaths paths, bool whole = true)
    {
        var events = new List<JsonObject>();
        if (!Directory.Exists(paths.LogsDirectory))
        {
            return events;
        }

        foreach (var file in Directory.GetFiles(paths.LogsDirectory, "runtime-*.jsonl").Order(StringComparer.Ordinal))
        {
            foreach (var line in RuntimeLogs.ReadAllText(file).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                JsonObject? parsed;
                try
                {
                    parsed = JsonNode.Parse(line) as JsonObject;
                }
                catch (JsonException)
                {
                    parsed = null;
                }

                if (parsed is not null)
                {
                    events.Add(parsed);
                }
                else
                {
                    Assert.False(whole, $"A line of {Path.GetFileName(file)} is not a JSON object: {line}");
                }
            }
        }

        return events;
    }

    private static void AssertEveryPropertyIsReviewed(List<JsonObject> events)
    {
        Assert.NotEmpty(events);
        var offences = events
            .SelectMany(line => line
                .Where(property => !ReviewedLogProperties.Names.Contains(property.Key))
                .Select(property => $"{property.Key} on \"{(string?)line["@mt"]}\" ({(string?)line["SourceContext"]})"))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.True(
            offences.Count == 0,
            $"{offences.Count} propert(ies) of the runtime's log are not on the reviewed list:{Environment.NewLine}{string.Join(Environment.NewLine, offences)}");
    }

    /// <summary>
    /// No sentinel anywhere in any file under <c>logs/</c>, whole or in part: the files are read as text, so a value
    /// inside an exception, a nested property or a line that is not even an event is found as surely as one in a
    /// property of its own. Where an event carries a whole sentinel, the event and the property are named. A part of
    /// one — the prefix every sentinel begins with, or the run's mark — found anywhere a whole sentinel does not
    /// account for it is named with the text around it, so a value cut short on its way into the log is a leak too.
    /// </summary>
    /// <summary>
    /// One creating verb under a key: made, then repeated and answered with what it made, then a different request under
    /// the same key refused, then a key too long to be one refused. Each answer is checked as it comes, so a sentinel
    /// missing from the log is one that went through the door and not one that never arrived.
    /// </summary>
    /// <returns>The id of what the key made.</returns>
    private static async Task<string> KeysAsync(Installation it, string key, Func<string, string[]> make, Func<string, string[]> different)
    {
        var made = (string)GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(it, make(key))))["id"]!;
        Assert.Equal(made, (string?)GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(it, make(key))))["id"]);

        var reused = await GoldenPath.JasonAsync(it, different(key));
        Assert.Equal(ExitCodes.ApiError, reused.ExitCode);
        Assert.Equal("idempotency_key_reused", (string?)GoldenPath.Json(reused)["error"]?["code"]);

        var tooLong = await GoldenPath.JasonAsync(it, make(key + new string('k', 128)));
        Assert.Equal(ExitCodes.ApiError, tooLong.ExitCode);
        Assert.Equal("validation_failed", (string?)GoldenPath.Json(tooLong)["error"]?["code"]);
        return made;
    }

    private static void AssertNoSentinel(JasonPaths paths, List<JsonObject> events, string mark, Dictionary<string, string> sentinels)
    {
        var written = Directory.GetFiles(paths.LogsDirectory, "*", SearchOption.AllDirectories)
            .Select(file => (Name: Path.GetFileName(file), Text: RuntimeLogs.ReadAllText(file)))
            .ToList();
        Assert.NotEmpty(written);

        var leaks = new List<string>();
        foreach (var (what, sentinel) in sentinels)
        {
            if (!written.Any(file => file.Text.Contains(sentinel, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var carriers = events
                .SelectMany(line => line
                    .Where(property => property.Value?.ToJsonString().Contains(sentinel, StringComparison.OrdinalIgnoreCase) == true)
                    .Select(property => $"{property.Key} on \"{(string?)line["@mt"]}\""))
                .Distinct(StringComparer.Ordinal);
            leaks.Add($"{sentinel} ({what}) is in the log, in: {string.Join("; ", carriers)}");
        }

        foreach (var (name, text) in written)
        {
            foreach (var part in new[] { SentinelPrefix, mark })
            {
                for (var at = text.IndexOf(part, StringComparison.OrdinalIgnoreCase); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.OrdinalIgnoreCase))
                {
                    if (!sentinels.Values.Any(sentinel => Within(text, at, part, sentinel)))
                    {
                        leaks.Add($"Part of a sentinel, {part}, is in {name}: …{Around(text, at)}…");
                    }
                }
            }
        }

        Assert.True(
            leaks.Count == 0,
            $"The runtime's log carries what a person typed:{Environment.NewLine}{string.Join(Environment.NewLine, leaks.Distinct(StringComparer.Ordinal))}");
    }

    /// <summary>Whether the part found at the index is that place in a whole sentinel, which is named on its own.</summary>
    private static bool Within(string text, int at, string part, string sentinel)
    {
        var offset = sentinel.IndexOf(part, StringComparison.OrdinalIgnoreCase);
        return offset >= 0 && at >= offset && text.AsSpan(at - offset).StartsWith(sentinel, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The text on either side of an index, on one line, for a failure that has to show where it looked.</summary>
    private static string Around(string text, int at)
    {
        var start = Math.Max(0, at - 60);
        return text[start..Math.Min(text.Length, at + 60)].ReplaceLineEndings(" ");
    }

    private static void AssertSeen(List<JsonObject> events, string what, Func<JsonObject, bool> wanted) =>
        Assert.True(events.Any(wanted), $"The run never logged {what}, so the absence of a leak there proves nothing.");
}
