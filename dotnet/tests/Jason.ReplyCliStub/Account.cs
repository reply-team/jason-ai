using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

// Deliberately in the global namespace, as the top-level program in this project is: a namespace named after the
// assembly would be found ahead of the test helper classes of the same name in the project that references it.

/// <summary>
/// One Reply account, held as a directory of JSON files: its contacts and their custom fields, lists, sequences,
/// enrollments, opt-outs, statistics and threads. The work commands read and change those files (<c>Account.Commands.cs</c>,
/// the sequences by name in <c>Account.Sequences.cs</c>, the inbox in <c>Account.Inbox.cs</c>, one contact by its id in
/// <c>Account.Contacts.cs</c>); no Reply endpoint is imitated.
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
    private const string CustomFieldsFile = "customfields.json";
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
    /// Every vendor-named variable this process was given, by name and value. Only the ones named after this
    /// vendor, because that is the whole of the question: an operator's own <c>REPLY_API_KEY</c> or
    /// <c>REPLY_TEAM_ID</c> must not reach a child, and the only names that legitimately do are the two the
    /// package itself sets on the call, <c>REPLY_NO_UPDATE_CHECK</c> and <c>REPLY_CLIENT</c>. Copying the rest of a
    /// machine's environment into a file would answer nothing and would put whatever a developer exported into it.
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

    private static string Text(JsonObject? parent, string name) =>
        parent?[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : string.Empty;

    private static bool Flag(JsonObject parent, string name) =>
        parent[name] is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;

    private static int Number(JsonObject parent, string name) =>
        parent[name] is JsonValue value && value.TryGetValue<int>(out var number) ? number : 0;

    private static int? Number(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;

    /// <summary>
    /// A LinkedIn profile link the way reply-cli 0.13.0 matches and writes one: its lowercased slug — on linkedin.com and
    /// its subdomains the path's case does not count, nor the scheme's or the host's, a default port, one trailing slash
    /// or a fragment, while <c>www</c> and the query do. Null when the text is not an absolute http or https link.
    /// </summary>
    private static string? LinkedInKey(string text)
    {
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || uri.Host.Length == 0)
        {
            return null;
        }

        var host = uri.Host.ToLowerInvariant();
        var path = uri.AbsolutePath.EndsWith('/') ? uri.AbsolutePath[..^1] : uri.AbsolutePath;
        if (host == "linkedin.com" || host.EndsWith(".linkedin.com", StringComparison.Ordinal))
        {
            path = path.ToLowerInvariant();
        }

        var port = uri.IsDefaultPort ? string.Empty : ":" + uri.Port.ToString(CultureInfo.InvariantCulture);
        return uri.Scheme + "://" + host + port + path + uri.Query;
    }
}
