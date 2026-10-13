using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

// Deliberately in the global namespace, as the top-level program in this project is: a namespace named after the
// assembly would be found ahead of the test helper classes of the same name in the project that references it.

/// <summary>
/// One Reply account, held as a directory of JSON files: its contacts, lists, sequences, enrollments, opt-outs,
/// statistics and threads. The work commands read and change those files (<c>Account.Commands.cs</c>), and the one raw
/// call the official plugin still makes — the contact search — is answered here, in the shape a live account answered
/// it.
/// </summary>
/// <remarks>
/// Nothing here touches the network or a shell, and nothing is written outside the account directory, so a test says
/// what happened at the provider by reading the files back.
/// </remarks>
internal static partial class Account
{
    private const string ContactsFile = "contacts.json";
    private const string ListsFile = "lists.json";
    private const string SequencesFile = "sequences.json";
    private const string EnrollmentsFile = "enrollments.json";
    private const string OptOutsFile = "optouts.json";
    private const string StatsFile = "stats.json";
    private const string ThreadsFile = "threads.json";
    private const string InstructionsFile = "instructions.json";
    private const string CredentialFile = "credential.json";

    /// <summary>How many accounts a sequence sends from, kept by this stand-in alone and never shown in an answer.</summary>
    private const string SendingAccounts = "_sendingAccounts";

    /// <summary>
    /// The file that says a store belongs to this stand-in. It is looked for before anything is created, read
    /// or written, because the directory this program finds is decided by a variable a test sets — and a test
    /// that forgot to set it would otherwise find the operator's own Reply configuration and start writing call
    /// logs and planted accounts into it.
    /// </summary>
    private const string MarkerFile = ".jason-stand-in";
    private const string CallsFile = "calls.jsonl";

    /// <summary>Where this provider's identifiers start; the number is meaningless, its stability is not.</summary>
    private const int FirstContactId = 1001;

    public static async Task<int> RunAsync(string profile, string method, string path, string? body, string[] argv)
    {
        var store = Locate();
        if (!File.Exists(Path.Combine(store, MarkerFile)))
        {
            // Not a store this stand-in planted. Refused before the directory is even created, in the shape the
            // real CLI refuses a call it will not make: a usage error, and its own code on stderr.
            await Console.Error.WriteLineAsync(new JsonObject
            {
                ["error"] = new JsonObject
                {
                    ["code"] = "usage.store",
                    ["title"] = "This is not a stand-in store.",
                    ["hint"] = $"No '{MarkerFile}' under '{store}'. A test sets the configuration directory; this one did not.",
                },
            }.ToJsonString());

            return 2;
        }

        var root = Path.Combine(store, profile);
        Directory.CreateDirectory(root);

        // Recorded before anything is answered. What a plugin did is then a fact a test can read rather than
        // something inferred from the state left behind: an obligation to read before writing is invisible
        // against a provider that would have answered the same either way.
        Record(root, method, path, body, argv);

        if (!File.Exists(Path.Combine(root, CredentialFile)))
        {
            // The CLI's own refusal, before there is any HTTP status to report and before any request is built.
            // reply-cli 0.5.1 spells it exactly this way: a usage error, so exit 2, with nothing on stdout and
            // one line of its `--json` error envelope on stderr, whose `auth.` code is what says the refusal was
            // about who is calling rather than about what was asked.
            var refusal = new JsonObject
            {
                ["error"] = new JsonObject
                {
                    ["code"] = "auth.required",
                    ["title"] = "Not authenticated.",
                    ["hint"] = $"Run `reply auth login` or set REPLY_API_KEY (profile '{profile}').",
                },
            };

            await Console.Error.WriteLineAsync(refusal.ToJsonString());
            return 2;
        }

        var instruction = Take(root, method, path);
        switch (instruction is null ? string.Empty : Text(instruction, "kind"))
        {
            case "answer":
                return await AnswerAsync(Number(instruction!, "code"), Scripted(Text(instruction!, "body")));

            case "hang":
                await Task.Delay(Number(instruction!, "milliseconds"));
                break;

            case "hold":
                await HoldAsync(instruction!);
                break;

            case "prints":
                // Whatever the test built, written out exactly as it is: no envelope, no status, nothing this
                // program decides. It is the one answer `Answers` cannot express, because that one always
                // prints the CLI's own {code, data} around whatever it is given — and a caller has to survive
                // stdout that is not an envelope at all.
                await Console.Out.WriteAsync(Text(instruction!, "stdout"));
                await Console.Out.FlushAsync();

                // And whatever it said beside it. The real CLI says why it refused on stderr and nowhere else,
                // so an ending without that half is only half an ending.
                if (Text(instruction!, "stderr") is { Length: > 0 } said)
                {
                    await Console.Error.WriteLineAsync(said);
                }

                return Number(instruction!, "exit_code");
        }

        var (code, answer) = Answer(root, method, path, body);
        return await AnswerAsync(code, answer);
    }

    /// <summary>
    /// Where the real CLI keeps its store, and therefore where this one looks: the platform's own configuration
    /// directory. No flag carries it, because the child's environment is built from nothing and the package
    /// declares no environment capability — so this is the only way an account can be chosen, and choosing one
    /// exercises the base environment for real.
    /// </summary>
    /// <remarks>
    /// A variable that is missing or empty gives a directory of this program's own under the temporary tree,
    /// never a relative path: a relative one would resolve against whatever the caller's working directory
    /// happened to be. Nothing is written there either, because no store of this program's making carries the
    /// marker — the two rules together are what keep a forgotten variable from reaching anybody's real account.
    /// </remarks>
    private static string Locate()
    {
        string configured;
        if (OperatingSystem.IsWindows())
        {
            configured = Environment.GetEnvironmentVariable("APPDATA") ?? string.Empty;
        }
        else if (Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } home)
        {
            configured = home;
        }
        else if (Environment.GetEnvironmentVariable("HOME") is { Length: > 0 } user)
        {
            configured = Path.Combine(user, ".config");
        }
        else
        {
            configured = string.Empty;
        }

        return configured.Length == 0
            ? Path.Combine(Path.GetTempPath(), "jason-reply-stand-in", "reply")
            : Path.Combine(configured, "reply");
    }

    // -------------------------------------------------------------------------------------------------------
    // The one raw call the official package still makes
    // -------------------------------------------------------------------------------------------------------

    private static (int Code, JsonNode? Body) Answer(string root, string method, string path, string? body)
    {
        var mark = path.IndexOf('?', StringComparison.Ordinal);
        var query = mark >= 0 ? path[(mark + 1)..] : string.Empty;
        var bare = mark >= 0 ? path[..mark] : path;

        return (bare, method) switch
        {
            ("/v3/contacts/filter", "POST") => Filter(root, query, body),
            _ => (404, Problem(404, "Not Found", "route.notFound", $"This account answers no {method} {bare}.")),
        };
    }

    /// <summary>A sequence as Reply would answer it: without the keys this stand-in keeps for its own refusals.</summary>
    private static JsonObject Shown(JsonObject sequence)
    {
        var shown = (JsonObject)sequence.DeepClone();
        foreach (var key in shown.Select(member => member.Key).Where(key => key.StartsWith('_')).ToList())
        {
            shown.Remove(key);
        }

        return shown;
    }

    private static readonly string[] EmailCounts =
    [
        "contacted", "delivered", "opened", "replied", "interested", "notReached", "optedOut", "outOfOffice", "bounced",
        "autoReplied", "meetingsBooked", "deliveredPercentage", "openedPercentage", "repliedPercentage",
        "interestedPercentage", "notReachedPercentage", "optedOutPercentage", "outOfOfficePercentage",
        "bouncedPercentage", "autoRepliedPercentage", "meetingsBookedPercentage",
    ];

    private static readonly string[] LinkedInCounts =
    [
        "connectionsSent", "connectionsAccepted", "messagesSent", "replied", "inMailsSent", "inMailsReplied",
        "connectionNotesSent", "connectionNotesReplied", "profileViews", "likes", "follows", "endorses", "comments",
        "regularMessagesSent", "regularMessagesReplied", "connectionsAcceptedPercentage", "repliedPercentage",
        "inMailsRepliedPercentage", "connectionNotesRepliedPercentage", "regularMessagesRepliedPercentage",
    ];

    /// <summary>Whether a thread holds at least one message the other side wrote.</summary>
    private static bool Inbound(JsonObject thread) =>
        (thread["messages"] as JsonArray ?? []).Any(message => message?["isOutbound"] is JsonValue outbound && !outbound.GetValue<bool>());

    /// <summary>Whether a thread's latest message is one this side wrote.</summary>
    private static bool LatestOutbound(JsonObject thread) =>
        (thread["messages"] as JsonArray ?? []).OfType<JsonObject>()
            .OrderBy(message => DateTimeOffset.TryParse(Text(message, "date"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? at : DateTimeOffset.MinValue)
            .LastOrDefault()?["isOutbound"] is JsonValue outbound && outbound.GetValue<bool>();

    private static DateTimeOffset LastActivity(JsonObject thread) =>
        DateTimeOffset.TryParse(Text(thread, "lastActivityDate"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
            ? at
            : DateTimeOffset.MinValue;

    /// <summary>A thread as the list and the filter answer it: everything but the history, the draft and the accounts.</summary>
    private static JsonObject Listed(JsonObject thread)
    {
        var listed = new JsonObject();
        foreach (var name in new[] { "id", "channel", "isRead", "subject", "bodyPreview", "lastActivityDate", "isLastMessagePlanned", "contact", "sequence", "category", "hasMeetingIntent", "status" })
        {
            if (thread.TryGetPropertyValue(name, out var value))
            {
                listed[name] = value?.DeepClone();
            }
        }

        return listed;
    }

    private static JsonObject Overview(params string[] names)
    {
        var overview = new JsonObject();
        foreach (var name in names)
        {
            overview[name] = 0;
        }

        return overview;
    }

    /// <summary>When a person was added, for the order the list answers in; one planted without a date sorts last.</summary>
    private static DateTimeOffset Added(JsonObject enrollment) =>
        DateTimeOffset.TryParse(Text(enrollment, "addingDate"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var added)
            ? added
            : DateTimeOffset.MinValue;

    private static JsonObject Member(JsonObject enrollment, JsonObject? contact, HashSet<int> suppressed)
    {
        var contactId = enrollment["contactId"]!.GetValue<int>();
        JsonNode step = enrollment["currentStep"] switch
        {
            JsonObject planted => planted.DeepClone(),
            JsonValue number when number.TryGetValue<int>(out var position) =>
                new JsonObject { ["stepId"] = 9900 + position, ["displayName"] = position.ToString(CultureInfo.InvariantCulture) },
            _ => new JsonObject { ["stepId"] = null, ["displayName"] = "Finished" },
        };

        return new JsonObject
        {
            ["contactId"] = contactId,
            ["sequenceId"] = enrollment["sequenceId"]!.GetValue<int>(),
            ["email"] = contact?["email"]?.DeepClone(),
            ["firstName"] = contact?["firstName"]?.DeepClone(),
            ["lastName"] = contact?["lastName"]?.DeepClone() ?? JsonValue.Create(string.Empty),
            ["company"] = contact?["company"]?.DeepClone(),
            ["title"] = contact?["title"]?.DeepClone(),
            ["statusInSequence"] = Text(enrollment, "statusInSequence"),
            ["currentStep"] = step,
            ["addingDate"] = enrollment["addingDate"]?.DeepClone() ?? JsonValue.Create("2026-10-02T09:15:42.17+03:00"),
            ["isOptedOut"] = suppressed.Contains(contactId),
            ["callStatus"] = contact?["callStatus"]?.DeepClone() ?? JsonValue.Create("none"),
            ["meetingStatus"] = contact?["meetingStatus"]?.DeepClone() ?? JsonValue.Create("none"),
            ["emailDisposition"] = enrollment["emailDisposition"]?.DeepClone(),
            ["emailAccountId"] = SendingAccount,
            ["linkedInAccountId"] = null,
        };
    }

    /// <summary>The stand-in's one mailbox, which it answers as a participation's sending account.</summary>
    private const int SendingAccount = 77;

    /// <summary>
    /// The contacts search, paged with <c>top</c> and <c>skip</c>, as a live account answered it: every contact whose
    /// address contains the <c>searchTerm</c>, whatever the case of either. Reply's own search reaches further than the
    /// address, which only adds near matches a caller has to tell apart anyway. A rule set is ignored, as the live
    /// account ignored it, and so is anything else the body carries.
    /// </summary>
    private static (int, JsonNode?) Filter(string root, string query, string? body)
    {
        var request = Request(body);
        var contacts = Read(root, ContactsFile);
        IEnumerable<JsonObject> found = contacts.OfType<JsonObject>();

        if (request?["searchTerm"] is JsonValue searched && searched.TryGetValue<string>(out var term) && term.Length > 0)
        {
            found = found.Where(contact =>
                contact["email"] is JsonValue email && email.TryGetValue<string>(out var address)
                && address.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        var matched = found.ToList();

        var skip = Count(query, "skip") ?? 0;
        var top = Count(query, "top") ?? 25;
        var page = new JsonArray([.. matched.Skip(skip).Take(top).Select(contact => (JsonNode)contact.DeepClone())]);
        return (200, new JsonObject { ["items"] = page, ["hasMore"] = matched.Count > skip + page.Count });
    }

    // -------------------------------------------------------------------------------------------------------
    // Answering, and the instructions a test leaves in the directory
    // -------------------------------------------------------------------------------------------------------

    private static async Task<int> AnswerAsync(int code, JsonNode? body)
    {
        await Console.Out.WriteLineAsync(new JsonObject { ["code"] = code, ["data"] = body }.ToJsonString());
        await Console.Out.FlushAsync();

        // The exit code says only which side of 400 the status fell on; the status itself is on stdout.
        return code >= 400 ? 1 : 0;
    }

    /// <summary>
    /// The first instruction that names this call, spent as it is taken, so the attempt after it sees the
    /// account again. A test scripts a failing attempt and a recovering one by scripting the first alone.
    /// </summary>
    private static JsonObject? Take(string root, string method, string path)
    {
        var file = Path.Combine(root, InstructionsFile);
        if (!File.Exists(file)
            || JsonNode.Parse(File.ReadAllText(file)) is not JsonObject instructions
            || instructions["scripted"] is not JsonArray scripted)
        {
            return null;
        }

        for (var position = 0; position < scripted.Count; position++)
        {
            if (scripted[position] is not JsonObject instruction
                || !string.Equals(Text(instruction, "method"), method, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Text(instruction, "path"), path, StringComparison.Ordinal))
            {
                continue;
            }

            scripted.RemoveAt(position);
            File.WriteAllText(file, instructions.ToJsonString(), new UTF8Encoding(false));
            return instruction;
        }

        return null;
    }

    /// <summary>
    /// A body a test wrote out by hand. It is answered as it stands — as JSON when it is JSON, as the string it
    /// is when it is not — because an answer no provider would send is exactly what some tests are about.
    /// </summary>
    private static JsonNode? Scripted(string body)
    {
        if (body.Trim().Length == 0)
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return JsonValue.Create(body);
        }
    }

    private static void Record(string root, string method, string path, string? body, string[] argv)
    {
        var entry = new JsonObject
        {
            ["command"] = "api",
            ["operands"] = new JsonArray(JsonValue.Create(path)),
            ["method"] = method,
            ["path"] = path,
            ["body"] = body,
            ["args"] = new JsonArray([.. argv.Select(argument => (JsonNode)JsonValue.Create(argument))]),
            ["env"] = VendorVariables(),
        };

        Append(Path.Combine(root, CallsFile), entry.ToJsonString() + "\n");
    }

    /// <summary>
    /// Every vendor-named variable this process was given, by name and value. Only the ones named after this
    /// vendor, because that is the whole of the question: an operator's own <c>REPLY_API_KEY</c> or
    /// <c>REPLY_TEAM_ID</c> must not reach a child, and the one name that legitimately does is the one the
    /// package itself sets on the call. Copying the rest of a machine's environment into a file would answer
    /// nothing and would put whatever a developer exported into it.
    /// </summary>
    private static JsonObject VendorVariables()
    {
        var seen = new JsonObject();
        foreach (DictionaryEntry variable in Environment.GetEnvironmentVariables())
        {
            var name = variable.Key.ToString() ?? string.Empty;
            if (name.StartsWith("REPLY_", StringComparison.OrdinalIgnoreCase))
            {
                seen[name] = variable.Value?.ToString();
            }
        }

        return seen;
    }

    /// <summary>
    /// Appends one line to the log. Appended rather than rewritten, because two invocations of this program can
    /// be in flight at once and a read-modify-write would quietly lose one of them.
    /// </summary>
    private static void Append(string file, string line)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.Write(line);
                return;
            }
            catch (IOException) when (attempt < 100)
            {
                Thread.Sleep(20);
            }
        }
    }

    // -------------------------------------------------------------------------------------------------------
    // The files, and the small readings of them
    // -------------------------------------------------------------------------------------------------------

    private static JsonArray Read(string root, string fileName)
    {
        var file = Path.Combine(root, fileName);
        return (File.Exists(file) ? JsonNode.Parse(File.ReadAllText(file)) as JsonArray : null) ?? [];
    }

    private static void Write(string root, string fileName, JsonNode content) =>
        File.WriteAllText(Path.Combine(root, fileName), content.ToJsonString(), new UTF8Encoding(false));

    private static IEnumerable<JsonObject> Records(string root, string fileName) => Read(root, fileName).OfType<JsonObject>();

    private static HashSet<int> OptOuts(string root) =>
        [.. Read(root, OptOutsFile).Select(entry => entry!.GetValue<int>())];

    private static JsonObject? Find(JsonArray records, int? id) =>
        id is null ? null : records.OfType<JsonObject>().FirstOrDefault(record => record["id"]!.GetValue<int>() == id);

    private static bool Matches(JsonObject record, string member, string id) =>
        Number(id) is { } number && record[member]!.GetValue<int>() == number;

    private static int NextId(JsonArray contacts) =>
        contacts.Count == 0 ? FirstContactId : contacts.OfType<JsonObject>().Max(contact => contact["id"]!.GetValue<int>()) + 1;

    private static JsonObject? Request(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(body) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>One numeric query parameter, by name, or nothing where the call did not carry it.</summary>
    private static int? Count(string query, string name)
    {
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (separator > 0
                && string.Equals(pair[..separator], name, StringComparison.Ordinal)
                && int.TryParse(pair[(separator + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static JsonObject Problem(int status, string title, string? code, string detail)
    {
        var problem = new JsonObject
        {
            ["type"] = "about:blank",
            ["title"] = title,
            ["status"] = status,
            ["detail"] = detail,
        };

        if (code is not null)
        {
            problem["code"] = code;
        }

        return problem;
    }

    private static string Text(JsonObject? parent, string name) =>
        parent?[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : string.Empty;

    private static bool Flag(JsonObject parent, string name) =>
        parent[name] is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;

    private static int Number(JsonObject parent, string name) =>
        parent[name] is JsonValue value && value.TryGetValue<int>(out var number) ? number : 0;

    private static int? Number(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;
}
