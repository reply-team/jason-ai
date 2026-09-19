using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Execution;

namespace Jason.FakeAgentHost;

/// <summary>
/// The repertoire. Each behaviour is one way an agent host can end — the good one, and every bad one the
/// dispatcher has to survive: a host that dies mid-run, one that never reports, one that goes quiet, one that
/// reports twice, one that spills the token into its own output.
/// </summary>
internal static class Behaviours
{
    public const int Success = 0;
    public const int UnknownBehaviour = 2;
    public const int Crashed = 3;
    public const int UnexpectedAnswer = 2;

    /// <summary>How far a chain of <c>script</c> files may point at further <c>script</c> files before it is a loop.</summary>
    private const int MaxScriptDepth = 8;

    private static readonly TimeSpan HeartbeatSpacing = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Resolves what will actually run, following <c>script &lt;file&gt;</c> to the behaviour named in the file.
    /// Done before the host says anything, so its one diagnostic line names the behaviour that ran.
    /// </summary>
    public static (string Behaviour, IReadOnlyList<string> Options) Resolve(IReadOnlyList<string> arguments)
    {
        var current = arguments;
        for (var depth = 0; depth <= MaxScriptDepth; depth++)
        {
            if (current.Count == 0)
            {
                return (string.Empty, []);
            }

            if (!string.Equals(current[0], "script", StringComparison.Ordinal) || current.Count < 2)
            {
                return (current[0], [.. current.Skip(1)]);
            }

            string[] named;
            try
            {
                named = File.ReadAllText(current[1]).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                return ("script", [.. current.Skip(1)]);
            }

            current = named;
        }

        return ("script", []);
    }

    /// <summary>The names this host answers to, so a caller can tell whether argv named one of them.</summary>
    public static bool Known(string behaviour) =>
        behaviour is "succeed" or "hang" or "mute" or "crash" or "silent" or "stale" or "leak" or "echo-envelope"
            or "abandon" or "flood" or "researcher" or "manager";

    /// <summary>
    /// The behaviour a launched host is told to perform through its brief rather than its arguments. A host
    /// started through an execution profile does not choose what is on its command line — the runtime composes
    /// that from the profile and its own constants — so a test that needs a particular behaviour says so in the
    /// work item's context, which is where everything else about the job already travels.
    /// </summary>
    public static (string Behaviour, IReadOnlyList<string> Options) FromContext(LaunchEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.Context["behaviour"]?.GetValue<string>() is not { } named)
        {
            return (string.Empty, []);
        }

        var parts = named.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? (string.Empty, []) : (parts[0], [.. parts.Skip(1)]);
    }

    public static async Task<int> RunAsync(string behaviour, IReadOnlyList<string> options, LaunchEnvelope envelope, string rawEnvelope, RuntimeApi api)
    {
        switch (behaviour)
        {
            case "succeed":
                return await SucceedAsync(options, envelope, api).ConfigureAwait(false);
            case "hang":
                return await HangAsync(envelope, api).ConfigureAwait(false);
            case "mute":
                return await MuteAsync().ConfigureAwait(false);
            case "crash":
                return await CrashAsync(envelope, api).ConfigureAwait(false);
            case "silent":
                return Success;
            case "stale":
                return await StaleAsync(envelope, api).ConfigureAwait(false);
            case "leak":
                return await LeakAsync(envelope, api).ConfigureAwait(false);
            case "abandon":
                return await AbandonAsync(envelope, api).ConfigureAwait(false);
            case "flood":
                return await FloodAsync(options).ConfigureAwait(false);
            case "manager":
                return await ManagerAsync(options, envelope, api).ConfigureAwait(false);
            case "researcher":
                return await ResearcherAsync(options, envelope, api).ConfigureAwait(false);
            case "echo-envelope":
                return await EchoEnvelopeAsync(rawEnvelope).ConfigureAwait(false);
            default:
                await Diagnostics.WriteAsync($"unknown behaviour '{behaviour}'").ConfigureAwait(false);
                return UnknownBehaviour;
        }
    }

    /// <summary>
    /// A host that finishes its work, leaves something behind still holding its output, and exits. A shell-capable
    /// agent does this whenever it starts a background command and does not wait for it: the child ends, the pipe
    /// does not, and a launcher that waits for the pipe waits for the helper instead. The attempt is completed
    /// first, so what is being measured afterwards is only the launcher.
    /// </summary>
    private static async Task<int> AbandonAsync(LaunchEnvelope envelope, RuntimeApi api)
    {
        await CompleteAsync(envelope, api, new JsonObject { ["summary"] = "done, and something is still running" })
            .ConfigureAwait(false);

        var start = new ProcessStartInfo(Environment.ProcessPath ?? "dotnet")
        {
            UseShellExecute = false,

            // Inherited, which is the whole point: this helper holds the pipe the launcher is reading.
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            RedirectStandardInput = false,
        };

        foreach (var argument in HelperArguments())
        {
            start.ArgumentList.Add(argument);
        }

        using var helper = Process.Start(start);
        await Diagnostics.WriteAsync($"left pid {helper?.Id.ToString(CultureInfo.InvariantCulture) ?? "none"} holding the pipes").ConfigureAwait(false);
        return Success;
    }

    /// <summary>
    /// The helper, which is this same program asked to sleep: started with no behaviour and no envelope, it reads
    /// an empty standard input, fails to parse it and would exit at once — so it is told to linger instead.
    /// </summary>
    private static IEnumerable<string> HelperArguments()
    {
        if (Environment.ProcessPath is null || Environment.ProcessPath.EndsWith("dotnet", StringComparison.OrdinalIgnoreCase)
            || Environment.ProcessPath.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            yield return Environment.GetCommandLineArgs()[0];
        }

        yield return "linger";
    }

    /// <summary>
    /// More output than any pipe will hold, so that a launcher which stopped reading would deadlock rather than
    /// merely lose the tail. That deadlock is the thing the pumps exist to prevent, and a test writing a few
    /// kilobytes never reaches it.
    /// </summary>
    private static async Task<int> FloodAsync(IReadOnlyList<string> options)
    {
        var megabytes = int.TryParse(Option(options, "--megabytes"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var asked)
            ? asked
            : 2;

        var line = new string('x', 1024);
        for (var written = 0; written < megabytes * 1024; written++)
        {
            await Console.Out.WriteLineAsync(line).ConfigureAwait(false);
        }

        await Console.Out.FlushAsync().ConfigureAwait(false);
        return Success;
    }

    /// <summary>The whole happy path: progress, proof of life, a result.</summary>
    private static async Task<int> SucceedAsync(IReadOnlyList<string> options, LaunchEnvelope envelope, RuntimeApi api)
    {
        var result = ParseResult(Option(options, "--result")) ?? new JsonObject { ["summary"] = "done", ["attempt"] = envelope.AttemptNumber };
        var heartbeats = int.TryParse(Option(options, "--heartbeats"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ? count : 1;

        await SetHalfDoneAsync(envelope, api).ConfigureAwait(false);

        for (var beat = 0; beat < heartbeats; beat++)
        {
            await Task.Delay(HeartbeatSpacing).ConfigureAwait(false);
            await api.CallAsync(Operations.WorkItemHeartbeat, new WorkItemHeartbeatRequest(envelope.WorkItemId, envelope.AttemptId)).ConfigureAwait(false);
        }

        await CompleteAsync(envelope, api, result).ConfigureAwait(false);
        return Success;
    }

    /// <summary>Alive and reporting it, forever: the lease holds, the work never ends. Only a kill stops this.</summary>
    private static async Task<int> HangAsync(LaunchEnvelope envelope, RuntimeApi api)
    {
        var spacing = TimeSpan.FromSeconds(Math.Max(1, envelope.HeartbeatSeconds / 4));
        while (true)
        {
            await api.CallAsync(Operations.WorkItemHeartbeat, new WorkItemHeartbeatRequest(envelope.WorkItemId, envelope.AttemptId)).ConfigureAwait(false);
            await Task.Delay(spacing).ConfigureAwait(false);
        }
    }

    /// <summary>Started and then nothing: no heartbeat, no result. This is what a missed heartbeat looks like.</summary>
    private static async Task<int> MuteAsync()
    {
        await Task.Delay(Timeout.Infinite).ConfigureAwait(false);
        return Success;
    }

    /// <summary>Half the work reported, then gone — the exit code is all the runtime gets.</summary>
    private static async Task<int> CrashAsync(LaunchEnvelope envelope, RuntimeApi api)
    {
        await SetHalfDoneAsync(envelope, api).ConfigureAwait(false);
        return Crashed;
    }

    /// <summary>Reports twice. The second report must be refused by the fencing, or the fencing is not there.</summary>
    private static async Task<int> StaleAsync(LaunchEnvelope envelope, RuntimeApi api)
    {
        await CompleteAsync(envelope, api, new JsonObject { ["summary"] = "done" }).ConfigureAwait(false);

        var (status, body) = await CompleteAsync(envelope, api, new JsonObject { ["summary"] = "done again" }).ConfigureAwait(false);
        if (status == 409 && body.Contains("stale_attempt", StringComparison.Ordinal))
        {
            await Diagnostics.WriteAsync("stale_attempt observed").ConfigureAwait(false);
            return Success;
        }

        await Diagnostics.WriteAsync($"expected 409 stale_attempt, got {status.ToString(CultureInfo.InvariantCulture)}").ConfigureAwait(false);
        return UnexpectedAnswer;
    }

    /// <summary>A badly written host that echoes its credentials. Nothing it writes may reach a stored artifact.</summary>
    private static async Task<int> LeakAsync(LaunchEnvelope envelope, RuntimeApi api)
    {
        var token = api.ReadDescriptor()?.Token ?? "n/a";
        await Console.Out.WriteLineAsync($"token={token}").ConfigureAwait(false);
        await Diagnostics.WriteAsync($"token={token}").ConfigureAwait(false);
        await CompleteAsync(envelope, api, new JsonObject { ["summary"] = "done" }).ConfigureAwait(false);
        return Success;
    }

    /// <summary>Writes back exactly what it was handed, so a test can assert on the envelope the launcher composed.</summary>
    private static async Task<int> EchoEnvelopeAsync(string rawEnvelope)
    {
        await Console.Out.WriteLineAsync(rawEnvelope.Trim()).ConfigureAwait(false);
        return Success;
    }

    /// <summary>
    /// A campaign manager doing the least a review can honestly do: read why it was woken, read the campaign and
    /// its own note, leave a line in the chronicle, and answer in the shape a check-in is held to. Whether it
    /// then acts is the test's choice — <c>manager --act</c> creates a work item, so that the difference between
    /// "looked and did something" and "looked and left it alone" is visible from outside.
    /// <para>
    /// Three more options, each for one thing a review can do that nothing else proves. <c>--escalate</c> asks
    /// a question and ends, which is the case the whole escalation round trip rests on. <c>--note-check</c>
    /// reads the note and the campaign and says which of the two it believed, because a note is what a role
    /// remembered and the runtime is what is true. <c>--prose</c> answers with a sentence where an object was
    /// asked for — the same role, taught the same way, refused only for the shape of its answer.
    /// </para>
    /// </summary>
    /// <remarks>
    /// It reads the intent out of the brief rather than being told on argv, because a launched role is never
    /// told anything on argv: the runtime composes that, and what the review is for travels in the context like
    /// everything else a role is given.
    /// </remarks>
    private static async Task<int> ManagerAsync(IReadOnlyList<string> options, LaunchEnvelope envelope, RuntimeApi api)
    {
        var intent = envelope.Context["review_intent"]?.GetValue<string>() ?? "unstated";
        var trigger = envelope.Context["trigger"]?.GetValue<string>();
        var remembered = await NoteAsync(envelope, api).ConfigureAwait(false);

        // Identifiers and counts. What the campaign says is the campaign's business, and this line ends up in a
        // work directory the next person to read is debugging something else.
        await Diagnostics.WriteAsync(
            $"intent={intent} trigger={trigger ?? "none"} recalled={(remembered?.Count ?? 0).ToString(CultureInfo.InvariantCulture)}")
            .ConfigureAwait(false);

        var (campaignStatus, campaignBody) = await api.CallAsync(
            Operations.CampaignGet, new CampaignGetRequest(envelope.CampaignId)).ConfigureAwait(false);

        // What the runtime says about this campaign, beside what the note claims about it. A note is what a
        // role remembered and the runtime is what is true, so a manager that finds them disagreeing reports
        // the state and says where the disagreement was.
        if (options.Contains("--note-check"))
        {
            var state = Status(campaignBody);
            var claimed = (string?)remembered?["campaign_status"];
            await Diagnostics.WriteAsync($"believed={claimed ?? "nothing"} state={state ?? "unreadable"}").ConfigureAwait(false);
        }

        var raised = new JsonArray();
        if (options.Contains("--escalate"))
        {
            var (status, body) = await api.CallAsync(
                Operations.DecisionRaise,
                new DecisionRaiseRequest(
                    envelope.WorkItemId,
                    envelope.AttemptId,
                    "the brief says ask before a fourth touch — do we keep calling this account?",
                    [new DecisionOption("keep going", null), new DecisionOption("stop", null)],
                    Referenced(envelope),
                    "the review could not decide this for itself"))
                .ConfigureAwait(false);

            if (status == 200 && JsonNode.Parse(body) is JsonObject asked && asked["id"] is { } id)
            {
                raised.Add(JsonValue.Create(id.GetValue<string>()));
            }
            else
            {
                await Diagnostics.WriteAsync($"{Operations.DecisionRaise} was refused: {status.ToString(CultureInfo.InvariantCulture)}").ConfigureAwait(false);
            }
        }

        var created = new JsonArray();
        if (options.Contains("--act"))
        {
            var (status, body) = await api.CallAsync(
                Operations.WorkItemCreate,
                new WorkItemCreateRequest(
                    envelope.CampaignId,
                    WorkItemKind.AiRole,
                    "researcher",
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    new JsonObject { ["brief"] = "look again at what the review found" },
                    null,
                    null,
                    "the review asked for it"))
                .ConfigureAwait(false);

            if (status == 200 && JsonNode.Parse(body) is JsonObject item && item["id"] is { } id)
            {
                created.Add(JsonValue.Create(id.GetValue<string>()));
            }
            else
            {
                await Diagnostics.WriteAsync($"the work item this review asked for was refused: {status.ToString(CultureInfo.InvariantCulture)}").ConfigureAwait(false);
            }
        }

        // The chronicle line every review leaves, whether or not it did anything. An empty tact is a finding:
        // somebody reading the campaign later should see that it was looked at and deliberately left alone.
        await api.CallAsync(
            Operations.JournalAppend,
            new JournalAppendRequest(
                envelope.CampaignId,
                "review",
                "review",
                new JsonObject { ["intent"] = intent, ["trigger"] = trigger },
                null,
                null))
            .ConfigureAwait(false);

        await RememberAsync(envelope, api, remembered).ConfigureAwait(false);

        var outcome = raised.Count > 0 ? "escalated" : created.Count > 0 ? "acted" : "nothing";
        var summary = options.Contains("--note-check")
            ? $"the note and the campaign disagreed; the campaign says {Status(campaignBody) ?? "nothing readable"}"
            : $"woken {intent}; the campaign answered {campaignStatus.ToString(CultureInfo.InvariantCulture)}";

        // A sentence where an object was asked for: the same role, taught the same way, refused only for the
        // shape of its answer. The researcher has this sibling and a manager did not.
        JsonNode result = options.Contains("--prose")
            ? JsonValue.Create("I had a look at the campaign and formed a view about where it stands.")!
            : new JsonObject
            {
                ["outcome"] = outcome,
                ["summary"] = summary,
                ["created_work_items"] = created,
                ["decisions_raised"] = raised,
            };

        if (options.Contains("--prose"))
        {
            await Diagnostics.WriteAsync("answering=prose").ConfigureAwait(false);
        }

        var answer = await CompleteAsync(envelope, api, result).ConfigureAwait(false);
        return answer.Status == 200 ? Success : UnexpectedAnswer;
    }

    /// <summary>
    /// The campaign's own status out of what <c>campaign.get</c> answered, or null where nothing readable came
    /// back. Read rather than assumed: the point of the note-check behaviour is which of the two a manager
    /// believed, so inventing either would prove nothing.
    /// </summary>
    private static string? Status(string body)
    {
        try
        {
            return (string?)JsonNode.Parse(body)?["status"];
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// What to read before answering, as identifiers. The work the question was woken by where a brief names
    /// one, and otherwise the run's own — a question is always about something, and a role asking one with
    /// nothing to read beside it is asking somebody to guess.
    /// </summary>
    /// <remarks>
    /// Every reference here is a row this campaign really has, which is the point: one that resolves to
    /// nothing is refused at the moment of asking, so a stand-in that made them up would be exercising the
    /// refusal rather than the round trip.
    /// </remarks>
    private static List<DecisionReference> Referenced(LaunchEnvelope envelope)
    {
        var cause = envelope.Context["cause"] as JsonObject;
        var item = (string?)cause?["work_item_id"] ?? envelope.WorkItemId;
        var attempt = (string?)cause?["attempt_id"] ?? envelope.AttemptId;

        var references = new List<DecisionReference>();
        if (item is { Length: > 0 })
        {
            references.Add(new DecisionReference(DecisionReferenceKind.WorkItem, item));
        }

        if (attempt is { Length: > 0 })
        {
            references.Add(new DecisionReference(DecisionReferenceKind.Attempt, attempt));
        }

        return references;
    }

    /// <summary>
    /// The one behaviour that does the job rather than a failure mode: a role that reads its skill out of the
    /// directory it was started in, reads its own note through the API, answers in the shape it was asked for
    /// and writes down what it learned before it finishes.
    /// <para>
    /// It is the smallest honest researcher. What it "finds" is what it can see — whether it was taught, and
    /// what it remembered — because everything else in this run is real: the process, the work directory, the
    /// callbacks, the note. <c>--prose</c> makes it answer with a sentence instead of the structure, which is
    /// the sibling case: the same role, taught the same way, refused only for the shape of its answer.
    /// </para>
    /// </summary>
    private static async Task<int> ResearcherAsync(IReadOnlyList<string> options, LaunchEnvelope envelope, RuntimeApi api)
    {
        var taught = SkillName(envelope);
        var remembered = await NoteAsync(envelope, api).ConfigureAwait(false);
        // How much was recalled, never which keys. A note's key names are its content as much as its values
        // are, and this line ends up in the trace of a failed attempt — which is exactly where the rest of
        // this wave takes care to put nothing.
        await Diagnostics.WriteAsync(
            $"taught-by={taught ?? "nothing"} recalled={(remembered?.Count ?? 0).ToString(CultureInfo.InvariantCulture)}").ConfigureAwait(false);

        var findings = new JsonArray(
            JsonValue.Create(taught is null ? "no skill reached this run" : $"the skill named '{taught}' was in the work directory"),
            JsonValue.Create(remembered is null || remembered.Count == 0 ? "nothing was remembered about this campaign" : "the note from an earlier pass was read"));

        await api.CallAsync(
            Operations.WorkItemSetResult,
            new WorkItemSetResultRequest(envelope.WorkItemId, envelope.AttemptId, new JsonObject { ["progress"] = "read the brief, the skill and the note" }))
            .ConfigureAwait(false);

        // Written before the answer, because the note is what survives the attempt: an answer refused for its
        // shape still leaves the next run better informed than this one was.
        await RememberAsync(envelope, api, remembered).ConfigureAwait(false);

        JsonNode result = options.Contains("--prose")
            ? JsonValue.Create("I had a thorough look at the question and formed a view.")!
            : new JsonObject
            {
                ["findings"] = findings,
                ["taught_by"] = taught,
                ["recalled"] = new JsonArray([.. (remembered?.Select(pair => (JsonNode?)JsonValue.Create(pair.Key)) ?? [])]),
            };

        var answer = await CompleteAsync(envelope, api, result).ConfigureAwait(false);
        return answer.Status == 200 ? Success : UnexpectedAnswer;
    }

    /// <summary>
    /// The name the skill in this attempt's own work directory gives itself, or null where none arrived. Read
    /// out of the file rather than assumed from the envelope: what is being proved is that the runtime really
    /// put the skill where a host looks for it.
    /// </summary>
    private static string? SkillName(LaunchEnvelope envelope)
    {
        if (envelope.Role is not { Length: > 0 } role)
        {
            return null;
        }

        var file = Path.Combine(envelope.WorkDir, ".claude", "skills", role, "SKILL.md");
        try
        {
            foreach (var line in File.ReadLines(file).Take(64))
            {
                if (line.StartsWith("name:", StringComparison.Ordinal))
                {
                    return line["name:".Length..].Trim().Trim('"', '\'');
                }
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// The role's note for this campaign, read through the API at the moment it is wanted. The envelope carries
    /// the address and never the document, so this is the only way to have it — which is the point: what comes
    /// back is plainly a document with an age rather than something that arrived looking like current truth.
    /// </summary>
    private static async Task<JsonObject?> NoteAsync(LaunchEnvelope envelope, RuntimeApi api)
    {
        if (envelope.RoleMemory is not { } memory)
        {
            return null;
        }

        var (status, body) = await api.CallAsync(
            Operations.RoleNoteGet, new RoleNoteGetRequest(memory.CampaignId, memory.Role)).ConfigureAwait(false);
        if (status != 200)
        {
            await Diagnostics.WriteAsync($"the note could not be read: {status.ToString(CultureInfo.InvariantCulture)} {body}").ConfigureAwait(false);
            return null;
        }

        try
        {
            return JsonNode.Parse(body)?["note"]?.AsObject();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// What this pass learned, written whole: a note is replaced rather than merged, so appending to one means
    /// reading it, adding to what was there and writing all of it back.
    /// </summary>
    private static async Task RememberAsync(LaunchEnvelope envelope, RuntimeApi api, JsonObject? remembered)
    {
        if (envelope.RoleMemory is not { } memory)
        {
            return;
        }

        var note = remembered is null ? new JsonObject() : remembered.DeepClone().AsObject();
        note["passes"] = (note["passes"]?.GetValue<int>() ?? 0) + 1;
        note["last_attempt"] = envelope.AttemptId;

        var (status, body) = await api.CallAsync(
            Operations.RoleNoteSet,
            new RoleNoteSetRequest(
                memory.CampaignId,
                memory.Role,
                note,

                // The run that wrote it, claimed the way a host calling the API directly has to claim it. An
                // agent reporting through the CLI gets this from its environment and never types it.
                new ActorRef(ActorType.Attempt, envelope.AttemptId),
                "What this pass learned."))
            .ConfigureAwait(false);
        if (status != 200)
        {
            await Diagnostics.WriteAsync($"the note could not be written: {status.ToString(CultureInfo.InvariantCulture)} {body}").ConfigureAwait(false);
        }
    }

    private static Task<(int Status, string Body)> SetHalfDoneAsync(LaunchEnvelope envelope, RuntimeApi api) =>
        api.CallAsync(Operations.WorkItemSetResult, new WorkItemSetResultRequest(envelope.WorkItemId, envelope.AttemptId, new JsonObject { ["progress"] = "half" }));

    /// <summary>
    /// Finishing, and saying so when the runtime refuses to let it finish. A host told its answer is not the
    /// shape that was asked for and then exiting silently would leave an attempt that ended for no visible
    /// reason; a real one would try again, and this one at least says what it was told.
    /// </summary>
    private static async Task<(int Status, string Body)> CompleteAsync(LaunchEnvelope envelope, RuntimeApi api, JsonNode? result)
    {
        var answer = await api.CallAsync(
            Operations.WorkItemComplete,
            new WorkItemCompleteRequest(envelope.WorkItemId, envelope.AttemptId, CompletionStatus.Succeeded, result, null, null))
            .ConfigureAwait(false);

        if (answer.Status != 200)
        {
            await Diagnostics.WriteAsync($"complete refused with {answer.Status.ToString(CultureInfo.InvariantCulture)}: {answer.Body}").ConfigureAwait(false);
        }

        return answer;
    }

    private static JsonNode? ParseResult(string? json)
    {
        try
        {
            return json is null ? null : JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The value after <paramref name="name"/> in an option list, or null. Public because the watchdog reads
    /// the same list: a host is configured by one set of arguments, whether the thing being configured is a
    /// behaviour or the rule by which the host gives up on its runtime.
    /// </summary>
    public static string? Option(IReadOnlyList<string> options, string name)
    {
        for (var index = 0; index < options.Count - 1; index++)
        {
            if (string.Equals(options[index], name, StringComparison.Ordinal))
            {
                return options[index + 1];
            }
        }

        return null;
    }
}
