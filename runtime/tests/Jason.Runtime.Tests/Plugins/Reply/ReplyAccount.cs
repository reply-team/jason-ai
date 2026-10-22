using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>One call the stand-in was asked to make, as it recorded it before answering.</summary>
/// <param name="Command">The command's two words, such as <c>sequence add-contact</c>.</param>
/// <param name="Operands">The positional arguments after the command: the ids.</param>
/// <param name="Body">What the call read from stdin: the person, the name, or the message.</param>
/// <param name="Args">The whole argument vector, which is what makes "this flag never appears" assertable.</param>
/// <param name="Environment">
/// Every vendor-named variable the stand-in was started with. No <c>REPLY_*</c> name enters the runtime, so what
/// an operator exported in their own shell must not reach a child; the only one that may be here is the one the
/// package sets on the call itself, and this is where that is read rather than assumed.
/// </param>
public sealed record ReplyCall(
    string Command,
    IReadOnlyList<string> Operands,
    string? Body,
    IReadOnlyList<string> Args,
    IReadOnlyDictionary<string, string> Environment)
{
    /// <summary>The call as one line: <c>sequence get 7</c>, or <c>contact find</c> for a command that takes no id.</summary>
    public string Line => Operands.Count == 0 ? Command : Command + " " + string.Join(' ', Operands);

    /// <summary>The value the call gave an option, or null when it gave none.</summary>
    public string? Option(string flag)
    {
        for (var at = 0; at < Args.Count - 1; at++)
        {
            if (string.Equals(Args[at], flag, StringComparison.Ordinal))
            {
                return Args[at + 1];
            }
        }

        return null;
    }
}

/// <summary>
/// One Reply account, as a directory of JSON files: the state the stand-in vendor CLI reads and writes, and the
/// ordered log of every call it was asked to make. It is what a test plants a provider's world in, and what the
/// test reads back afterwards to say what really happened — who exists, who is on which list, who takes part in
/// which sequence, and in what order the plugin asked.
/// </summary>
/// <remarks>
/// The account is found the way the real CLI finds its store — <c>%APPDATA%\reply</c> on Windows,
/// <c>$XDG_CONFIG_HOME/reply</c> elsewhere, then the profile name — so no argument the plugin would have to know
/// about carries it. A test sets <see cref="ConfigHomeVariable"/> to <see cref="ConfigHome"/> on whichever
/// process will start the CLI: for a plugin invocation that is the runtime, and the base environment carries the
/// name to the child. Everything stays inside a temporary directory the test owns: no network, nothing installed
/// on the machine, and nothing that could reach a real account.
/// </remarks>
public sealed class ReplyAccount : IDisposable
{
    public const string ContactsFile = "contacts.json";
    public const string ListsFile = "lists.json";
    public const string SequencesFile = "sequences.json";
    public const string EnrollmentsFile = "enrollments.json";
    public const string OptOutsFile = "optouts.json";
    public const string StatsFile = "stats.json";
    public const string ThreadsFile = "threads.json";
    public const string CategoriesFile = "categories.json";
    public const string CustomFieldsFile = "customfields.json";
    public const string InstructionsFile = "instructions.json";
    public const string CredentialFile = "credential.json";
    public const string SendersFile = "senders.json";
    public const string IdentityFile = "identity.json";
    public const string ConnectionsFile = "connections.json";
    public const string FeaturesFile = "features.json";
    public const string SentFile = "sent.json";

    /// <summary>The Reply user the stand-in's credential signs in as, unless a test plants another.</summary>
    public const int Caller = 501;

    /// <summary>The call log, one JSON object per line: appended to, never rewritten, so a lost line is a bug.</summary>
    public const string CallsFile = "calls.jsonl";

    /// <summary>The store directory inside the config home, which is the one name the real CLI also uses.</summary>
    public const string StoreDirectory = "reply";

    private const string DefaultToken = "stand-in-credential";

    private readonly string _temporary;

    public ReplyAccount(string profile = "default")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profile);
        Profile = profile;
        _temporary = Path.Combine(Path.GetTempPath(), "jason-tests", Guid.NewGuid().ToString("N"));
        ConfigHome = Path.Combine(_temporary, "config");
        Root = Path.Combine(ConfigHome, StoreDirectory, profile);
        Directory.CreateDirectory(Root);

        // The one file that says this store is a test's and not a person's. The stand-in refuses to touch a
        // store without it, so a test that forgot to point the configuration directory here cannot reach the
        // operator's own Reply account — it fails instead, which is the only safe way for it to fail.
        File.WriteAllText(Path.Combine(ConfigHome, StoreDirectory, ".jason-stand-in"), string.Empty);

        // Signed in from the start: an account with no credential is a state a test asks for by naming a profile
        // nobody signed into, which is exactly how the real thing fails.
        Write(CredentialFile, new JsonObject { ["token"] = DefaultToken });
        WithIdentity(Caller);
    }

    /// <summary>The account directory: everything this provider holds, and the log of what it was asked.</summary>
    public string Root { get; }

    /// <summary>What <see cref="ConfigHomeVariable"/> must be set to for the CLI to find this account.</summary>
    public string ConfigHome { get; }

    public string Profile { get; }

    /// <summary>
    /// The name of the variable that decides where the CLI looks: the platform's own, never a vendor-named one,
    /// because no <c>REPLY_*</c> name may enter the runtime.
    /// </summary>
    public static string ConfigHomeVariable => OperatingSystem.IsWindows() ? "APPDATA" : "XDG_CONFIG_HOME";

    /// <summary>
    /// The stand-in itself, as MSBuild copies it beside whatever test project references it. It lives here
    /// rather than beside the search path it is resolved through, because this file is the whole of what a
    /// process-boundary test needs to drive a Reply account, and a helper that dragged a dependency along would
    /// have to be copied instead of shared.
    /// </summary>
    public static string StandInPath =>
        Path.Combine(AppContext.BaseDirectory, "reply" + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));

    /// <summary>That name and value, for a test that has to put them on the process which will start the CLI.</summary>
    public IReadOnlyDictionary<string, string> Variables =>
        new Dictionary<string, string>(StringComparer.Ordinal) { [ConfigHomeVariable] = ConfigHome };

    /// <summary>
    /// The credential this account holds. It is a planted value precisely because nothing may ever carry it out:
    /// a token that turned up in an argument vector, a diagnostic or a log would be a real leak.
    /// </summary>
    public string Marker => Read(CredentialFile) is JsonObject credential && credential["token"] is JsonValue token
        ? token.GetValue<string>()
        : DefaultToken;

    // -------------------------------------------------------------------------------------------------------
    // What the provider holds
    // -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A person this account already holds. Identifiers are integers, as Reply's are. A LinkedIn URL is held as written,
    /// in whatever case somebody typed it at Reply.
    /// </summary>
    public ReplyAccount WithContact(
        int id,
        string? email,
        string? firstName = null,
        bool optedOut = false,
        string? lastName = null,
        string? company = null,
        string? title = null,
        string? linkedIn = null)
    {
        Replace(ContactsFile, id, new JsonObject
        {
            ["id"] = id,
            ["email"] = email,
            ["linkedInUrl"] = linkedIn,
            ["firstName"] = firstName,
            ["lastName"] = lastName,
            ["company"] = company,
            ["title"] = title,
            ["callStatus"] = "none",
            ["meetingStatus"] = "none",
        });

        if (optedOut)
        {
            // The opt-out register is the provider's own, kept apart from the contact: a suppression a test
            // plants has to be answerable without the record that happens to sit beside it.
            var register = ReadArray(OptOutsFile);
            if (!register.Any(entry => entry!.GetValue<int>() == id))
            {
                register.Add(id);
                Write(OptOutsFile, register);
            }
        }

        return this;
    }

    /// <summary>
    /// A second contact under the address of one already planted. Reply's create path publishes no word for an
    /// address the account already holds, so an account can come to hold the same person twice — and a lookup by
    /// address then finds two.
    /// </summary>
    public ReplyAccount WithDuplicate(int id, int of)
    {
        var original = Contacts.OfType<JsonObject>().SingleOrDefault(existing => existing["id"]!.GetValue<int>() == of)
            ?? throw new InvalidOperationException($"no contact {of} has been planted to duplicate.");
        return WithContact(id, (string?)original["email"], (string?)original["firstName"], linkedIn: (string?)original["linkedInUrl"]);
    }

    /// <summary>One of the contact custom fields the account defines, by the title Reply names it with.</summary>
    public ReplyAccount WithCustomField(int id, string title) =>
        Replace(CustomFieldsFile, id, new JsonObject { ["id"] = id, ["title"] = title, ["fieldType"] = "text" });

    /// <summary>A value a planted contact holds for a custom field, under the key Reply answers it by.</summary>
    public ReplyAccount WithCustomValue(int contact, string key, string value)
    {
        var contacts = ReadArray(ContactsFile);
        var planted = contacts.OfType<JsonObject>().SingleOrDefault(existing => existing["id"]!.GetValue<int>() == contact)
            ?? throw new InvalidOperationException($"no contact {contact} has been planted to give a custom value to.");
        var values = planted["customFields"] as JsonArray ?? [];
        values.Add(new JsonObject { ["key"] = key, ["value"] = value });
        planted["customFields"] = values.DeepClone();
        Write(ContactsFile, contacts);
        return this;
    }

    /// <summary>A contact list and the contacts it holds; not shared unless a test says so.</summary>
    public ReplyAccount WithList(int id, string name, params int[] members) => WithList(id, name, shared: false, members);

    public ReplyAccount WithList(int id, string name, bool shared, params int[] members)
    {
        ArgumentNullException.ThrowIfNull(members);
        return Replace(ListsFile, id, new JsonObject
        {
            ["id"] = id,
            ["name"] = name,
            ["isShared"] = shared,
            ["members"] = new JsonArray([.. members.Select(member => (JsonNode)JsonValue.Create(member))]),
        });
    }

    /// <summary>
    /// A sequence and its steps. The steps are a chain — each one's parent is the one before it — because that is
    /// the only shape in which a Reply sequence has an order at all: there is no ordering field on a step.
    /// </summary>
    public ReplyAccount WithSequence(int id, string name, string status, bool archived = false, params int[] steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var chain = new JsonArray();
        for (var position = 0; position < steps.Length; position++)
        {
            chain.Add(new JsonObject
            {
                ["id"] = steps[position],
                ["parentId"] = position == 0 ? null : JsonValue.Create(steps[position - 1]),
                ["type"] = "email",
                ["delayInMinutes"] = position == 0 ? 0 : 1440,
            });
        }

        return Replace(SequencesFile, id, new JsonObject
        {
            ["id"] = id,
            ["name"] = name,
            ["status"] = status,
            ["isArchived"] = archived,
            ["health"] = "good",
            ["steps"] = chain,
        });
    }

    /// <summary>
    /// When a planted sequence was created, written as a live account writes it: two fractional digits and an offset. A
    /// sequence planted without one is answered as created long before any test's work item.
    /// </summary>
    public ReplyAccount CreatedAt(int sequence, DateTimeOffset at) =>
        CreatedAt(sequence, at.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.ff'+00:00'", System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>The same, written exactly as given: another offset, another number of fractional digits.</summary>
    public ReplyAccount CreatedAt(int sequence, string written)
    {
        var record = ReadArray(SequencesFile).OfType<JsonObject>().Single(existing => existing["id"]!.GetValue<int>() == sequence);
        var changed = (JsonObject)record.DeepClone();
        changed["created"] = written;
        return Replace(SequencesFile, sequence, changed);
    }

    /// <summary>
    /// A planted sequence with no connected account to send from, so a start is refused. The stand-in keeps this
    /// under a key of its own and never shows it in an answer.
    /// </summary>
    public ReplyAccount WithoutSendingAccount(int sequence)
    {
        var record = ReadArray(SequencesFile).OfType<JsonObject>().Single(existing => existing["id"]!.GetValue<int>() == sequence);
        var changed = (JsonObject)record.DeepClone();
        changed["_sendingAccounts"] = 0;
        return Replace(SequencesFile, sequence, changed);
    }

    /// <summary>
    /// The steps of a planted sequence, shaped by hand: a condition in the middle of the chain, a branch, or a
    /// chain shorter than somebody asked for. Reply gives a step no ordering field whatever — the
    /// <c>parentId</c> graph is the only order there is — so a shape that is not a single chain is exactly the
    /// one that has no Nth step, and these tests need to plant one.
    /// </summary>
    public ReplyAccount WithSteps(int sequence, params (int Id, int? ParentId, string Type)[] steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var planted = Sequences.FirstOrDefault(entry => entry!["id"]!.GetValue<int>() == sequence)
            ?? throw new InvalidOperationException($"no sequence {sequence} has been planted to give steps to.");

        var shaped = new JsonArray();
        foreach (var (id, parent, type) in steps)
        {
            shaped.Add(new JsonObject
            {
                ["id"] = id,
                ["parentId"] = parent is null ? null : JsonValue.Create(parent.Value),
                ["type"] = type,
                ["delayInMinutes"] = parent is null ? 0 : 1440,
            });
        }

        return Replace(SequencesFile, sequence, new JsonObject
        {
            ["id"] = sequence,
            ["name"] = planted["name"]!.DeepClone(),
            ["status"] = planted["status"]!.DeepClone(),
            ["isArchived"] = planted["isArchived"]!.DeepClone(),
            ["health"] = planted["health"]!.DeepClone(),
            ["steps"] = shaped,
        });
    }

    /// <summary>
    /// A person taking part in a sequence. The date added, the step and what the last email met are optional, as a
    /// test plants them: Reply always answers a date and a step, and the stand-in fills in the ones left out.
    /// </summary>
    public ReplyAccount WithEnrollment(
        int sequence,
        int contact,
        string statusInSequence = "active",
        string? addedAt = null,
        int? stepId = null,
        string? stepName = null,
        (bool Replied, bool Bounced)? disposition = null)
    {
        var enrollments = ReadArray(EnrollmentsFile);
        foreach (var existing in enrollments.ToList())
        {
            if (existing!["sequenceId"]!.GetValue<int>() == sequence && existing["contactId"]!.GetValue<int>() == contact)
            {
                enrollments.Remove(existing);
            }
        }

        var enrollment = new JsonObject
        {
            ["sequenceId"] = sequence,
            ["contactId"] = contact,
            ["statusInSequence"] = statusInSequence,
            ["currentStep"] = stepId is null && stepName is null
                ? 1
                : new JsonObject { ["stepId"] = stepId, ["displayName"] = stepName },
        };

        if (addedAt is not null)
        {
            enrollment["addingDate"] = addedAt;
        }

        if (disposition is { } met)
        {
            enrollment["emailDisposition"] = new JsonObject { ["isReplied"] = met.Replied, ["isBounced"] = met.Bounced };
        }

        enrollments.Add(enrollment);

        Write(EnrollmentsFile, enrollments);
        return this;
    }

    /// <summary>
    /// What the account answers a sequence's statistics with, overview by overview, whatever the window. A null
    /// overview is left out of the answer, which is how a provider that reports one channel only is planted.
    /// </summary>
    public ReplyAccount WithStats(int sequence, JsonObject? emailOverview, JsonObject? linkedInOverview)
    {
        var stats = ReadArray(StatsFile);
        foreach (var existing in stats.ToList())
        {
            if (existing!["sequenceId"]!.GetValue<int>() == sequence)
            {
                stats.Remove(existing);
            }
        }

        var entry = new JsonObject { ["sequenceId"] = sequence };
        if (emailOverview is not null)
        {
            entry["emailOverview"] = emailOverview;
        }

        if (linkedInOverview is not null)
        {
            entry["linkedInOverview"] = linkedInOverview;
        }

        stats.Add(entry);
        Write(StatsFile, stats);
        return this;
    }

    /// <summary>
    /// An inbox thread, written as Reply holds it: the list's fields, the party with everything Reply knows of them,
    /// and the whole message history inline. The test builds the object, because the shapes are the point.
    /// </summary>
    public ReplyAccount WithThread(JsonObject thread)
    {
        ArgumentNullException.ThrowIfNull(thread);
        return Replace(ThreadsFile, thread["id"]!.GetValue<int>(), thread);
    }

    /// <summary>One of the inbox categories a thread can be filed under, as Reply lists them.</summary>
    public ReplyAccount WithCategory(int id, string name, bool reserved = false) =>
        Replace(CategoriesFile, id, new JsonObject
        {
            ["id"] = id,
            ["name"] = name,
            ["color"] = "green",
            ["isReserved"] = reserved,
            ["unreadThreadsCount"] = 0,
        });

    /// <summary>A category entry written exactly as given — one this package could not file a thread under, say.</summary>
    public ReplyAccount WithCategoryEntry(JsonObject entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var categories = ReadArray(CategoriesFile);
        categories.Add(entry);
        Write(CategoriesFile, categories);
        return this;
    }

    /// <summary>
    /// The thread's next reply is one Reply takes and cannot deliver, for this reason of its own — or for none it gives,
    /// when the reason is null. The mark is the stand-in's and never shown in an answer.
    /// </summary>
    public ReplyAccount FailsToDeliverIn(int thread, string? reason)
    {
        var threads = ReadArray(ThreadsFile);
        var planted = threads.OfType<JsonObject>().Single(existing => existing["id"]!.GetValue<int>() == thread);
        planted["_sendFails"] = new JsonObject { ["reason"] = reason };
        Write(ThreadsFile, threads);
        return this;
    }

    /// <summary>
    /// Who the credential signs in as, as <c>reply auth whoami</c> answers it: a Reply user, the login name and the team.
    /// The login name is the operator's own, so a test can look for it where it must never travel.
    /// </summary>
    public ReplyAccount WithIdentity(int userId, string username = "owner@example.com", int teamId = 900)
    {
        Write(IdentityFile, new JsonObject { ["userId"] = userId, ["username"] = username, ["teamId"] = teamId });
        return this;
    }

    /// <summary>
    /// An account a send can go from, as Reply lists it: a mailbox (<c>email</c>) or a LinkedIn account, with its name, its
    /// address — the mailbox's email or the LinkedIn profile — the Reply user whose it is, and Reply's own word for its
    /// state (<c>connected</c>, <c>disconnected</c>, <c>unknown</c>; <c>enabled</c>, <c>disabled</c>, <c>cookieInvalid</c>,
    /// <c>dailyLimitReached</c>). <paramref name="limit"/> plants a LinkedIn account whose daily or weekly allowance is spent.
    /// </summary>
    public ReplyAccount WithSender(
        long id,
        string channel,
        string? name,
        string? address,
        int? owner = Caller,
        string? status = null,
        string? limit = null)
    {
        var senders = ReadArray(SendersFile);
        foreach (var existing in senders.ToList())
        {
            if (existing!["id"]!.GetValue<long>() == id && (string?)existing["channel"] == channel)
            {
                senders.Remove(existing);
            }
        }

        var sender = new JsonObject
        {
            ["id"] = id,
            ["channel"] = channel,
            ["name"] = name,
            ["address"] = address,
            ["ownerUserId"] = owner,
            ["status"] = status ?? (channel == "email" ? "connected" : "enabled"),
        };
        if (limit is not null)
        {
            sender["_limit"] = limit;
        }

        senders.Add(sender);
        Write(SendersFile, senders);
        return this;
    }

    /// <summary>
    /// A contact on the account's blacklist, which Reply consults on a direct email and on no LinkedIn send. It is kept on
    /// the planted record and never answered by <c>contact get</c>, as Reply's contact read does not show it.
    /// </summary>
    public ReplyAccount WithBlacklisted(int contact)
    {
        var contacts = ReadArray(ContactsFile);
        var planted = contacts.OfType<JsonObject>().SingleOrDefault(existing => existing["id"]!.GetValue<int>() == contact)
            ?? throw new InvalidOperationException($"no contact {contact} has been planted to blacklist.");
        planted["_blacklisted"] = true;
        Write(ContactsFile, contacts);
        return this;
    }

    /// <summary>
    /// A contact Reply's exact lookup does not return, though its import matches it — by the email, or by the LinkedIn URL
    /// where the person has no email. Reply documents neither how the lookup nor how the import compares a value, so the
    /// two may disagree; this is the contact they disagree about. <c>contact get</c> reads it as any other.
    /// </summary>
    public ReplyAccount Unlisted(int contact)
    {
        var contacts = ReadArray(ContactsFile);
        var planted = contacts.OfType<JsonObject>().SingleOrDefault(existing => existing["id"]!.GetValue<int>() == contact)
            ?? throw new InvalidOperationException($"no contact {contact} has been planted to keep from the lookup.");
        planted["_unlisted"] = true;
        Write(ContactsFile, contacts);
        return this;
    }

    /// <summary>A contact the LinkedIn account is already connected with: an invitation to them sends nothing, and a message or a voice message may go.</summary>
    public ReplyAccount WithConnection(int contact, long account)
    {
        var connections = ReadArray(ConnectionsFile);
        connections.Add(new JsonObject { ["contactId"] = contact, ["accountId"] = account });
        Write(ConnectionsFile, connections);
        return this;
    }

    /// <summary>
    /// A feature the account's plan or role does not include: <c>emailAccounts</c> or <c>linkedInAccounts</c>, the two
    /// lists <c>account list</c> reads, or <c>directLinkedIn</c>, the LinkedIn sends outside a sequence.
    /// </summary>
    public ReplyAccount Without(string feature)
    {
        var features = Read(FeaturesFile) as JsonObject ?? [];
        features[feature] = false;
        Write(FeaturesFile, features);
        return this;
    }

    // -------------------------------------------------------------------------------------------------------
    // What it can be told to do instead, through the directory and never through a flag
    // -------------------------------------------------------------------------------------------------------

    /// <summary>The next run of this command prints this result and exits 0, whatever the account holds.</summary>
    public ReplyAccount Returns(string command, JsonObject result, string? match = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        return Prints(command, 0, result.ToJsonString(), null, match);
    }

    /// <summary>
    /// The next run of this command fails the way reply-cli fails: nothing on stdout, one <c>{"error":…}</c> line on
    /// stderr, and this exit code. The members left null are left out, as reply-cli leaves them out.
    /// </summary>
    public ReplyAccount Refuses(
        string command,
        string code,
        int exitCode = 1,
        int? status = null,
        string? replyCode = null,
        string? detail = null,
        JsonObject? ids = null,
        JsonObject? details = null,
        int? retryAfter = null,
        string? match = null)
    {
        var error = new JsonObject { ["code"] = code, ["title"] = "The stand-in was told to refuse this." };
        if (status is { } answered)
        {
            error["status"] = answered;
        }

        if (replyCode is not null)
        {
            error["reply_code"] = replyCode;
        }

        if (detail is not null)
        {
            error["detail"] = detail;
        }

        if (ids is not null)
        {
            error["ids"] = ids.DeepClone();
        }

        if (retryAfter is { } wait)
        {
            error["retry_after"] = wait;
        }

        if (details is not null)
        {
            error["details"] = details.DeepClone();
        }

        return Prints(command, exitCode, string.Empty, new JsonObject { ["error"] = error }.ToJsonString(), match);
    }

    /// <summary>The next run of this command writes exactly this and exits with exactly this code.</summary>
    public ReplyAccount Prints(string command, int exitCode, string stdout, string? stderr = null, string? match = null) =>
        Script(new JsonObject
        {
            ["kind"] = "prints",
            ["command"] = command,
            ["match"] = match,
            ["exit_code"] = exitCode,
            ["stdout"] = stdout,
            ["stderr"] = stderr ?? string.Empty,
        });

    /// <summary>
    /// The next run of this command does everything it would have done, and then answers as reply-cli answers a write
    /// whose answer never came back: <c>outcome.unknown</c>, exit 3, with the ids it had learned.
    /// </summary>
    public ReplyAccount LosesTheOutcome(string command, string? match = null) =>
        Script(new JsonObject { ["kind"] = "lost", ["command"] = command, ["match"] = match });

    /// <summary>The next run of this command takes this long before it answers.</summary>
    public ReplyAccount Hangs(string command, int milliseconds, string? match = null) =>
        Script(new JsonObject { ["kind"] = "hang", ["command"] = command, ["match"] = match, ["milliseconds"] = milliseconds });

    /// <summary>The next run of this command says it started, waits to be let go, then answers as it otherwise would.</summary>
    public ReplyAccount HoldsOn(string command, string marker, string until, int capMs = 120_000, string? match = null) =>
        Script(new JsonObject
        {
            ["kind"] = "hold",
            ["command"] = command,
            ["match"] = match,
            ["marker"] = marker,
            ["until"] = until,
            ["timeout_ms"] = capMs,
        });

    /// <summary>
    /// Plants the value this account's credential is, so that a test can look for it everywhere afterwards. It
    /// is held by the CLI and by nothing else; finding it in an outcome, an argument, a diagnostic or a log is
    /// the definition of the leak these tests exist to rule out.
    /// </summary>
    public ReplyAccount WithMarker(string value)
    {
        Write(CredentialFile, new JsonObject { ["token"] = value });
        return this;
    }

    // -------------------------------------------------------------------------------------------------------
    // What the test reads back
    // -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Every call this account was asked to make, in order, with the body and the whole argument vector. An
    /// obligation to read before writing is invisible against a provider that would have answered the same
    /// either way; this is what makes it a fact.
    /// </summary>
    public IReadOnlyList<ReplyCall> Calls
    {
        get
        {
            var file = Path.Combine(Root, CallsFile);
            if (!File.Exists(file))
            {
                return [];
            }

            var calls = new List<ReplyCall>();
            foreach (var line in ReadAllLines(file))
            {
                if (line.Trim().Length == 0 || JsonNode.Parse(line) is not JsonObject call)
                {
                    continue;
                }

                calls.Add(new ReplyCall(
                    call["command"]!.GetValue<string>(),
                    [.. call["operands"]!.AsArray().Select(operand => operand!.GetValue<string>())],
                    call["body"] is JsonValue body ? body.GetValue<string>() : null,
                    [.. call["args"]!.AsArray().Select(argument => argument!.GetValue<string>())],
                    (call["env"] as JsonObject ?? []).ToDictionary(
                        variable => variable.Key,
                        variable => variable.Value?.GetValue<string>() ?? string.Empty,
                        StringComparer.Ordinal)));
            }

            return calls;
        }
    }

    /// <summary>Where the log stands now, so a later assertion can be about one attempt rather than a lifetime.</summary>
    public int Mark() => Calls.Count;

    public IReadOnlyList<ReplyCall> CallsSince(int mark) => [.. Calls.Skip(mark)];

    public JsonArray Contacts => ReadArray(ContactsFile);

    /// <summary>One contact as the account holds it now, or null once it holds them no longer.</summary>
    public JsonObject? ContactOf(int id) =>
        Contacts.OfType<JsonObject>().SingleOrDefault(contact => contact["id"]!.GetValue<int>() == id);

    public JsonArray Lists => ReadArray(ListsFile);

    public JsonArray Sequences => ReadArray(SequencesFile);

    public JsonArray Enrollments => ReadArray(EnrollmentsFile);

    /// <summary>The account's opt-out register: the contact identifiers Reply holds as opted out.</summary>
    public JsonArray OptOuts => ReadArray(OptOutsFile);

    /// <summary>The inbox threads as the account holds them now: what a reply, a category or a read mark changed.</summary>
    public JsonArray Threads => ReadArray(ThreadsFile);

    /// <summary>One thread as the account holds it now.</summary>
    public JsonObject ThreadOf(int id) =>
        Threads.OfType<JsonObject>().Single(thread => thread["id"]!.GetValue<int>() == id);

    /// <summary>A thread's messages this side wrote, in the order the account holds them.</summary>
    public IReadOnlyList<JsonObject> OutboundIn(int thread) =>
        [.. (ThreadOf(thread)["messages"] as JsonArray ?? []).OfType<JsonObject>()
            .Where(message => message["isOutbound"] is JsonValue outbound && outbound.GetValue<bool>())];

    /// <summary>
    /// Every send that went out, in order: the command, the contact, the account it went from and the text on stdin. The
    /// only honest answer to "was anything sent".
    /// </summary>
    public JsonArray Sent => ReadArray(SentFile);

    /// <summary>Who is on a list right now, which is the only honest answer to "did the add happen at all".</summary>
    public IReadOnlyList<int> MembersOf(int listId) =>
        [.. (Lists.FirstOrDefault(list => list!["id"]!.GetValue<int>() == listId)?["members"]?.AsArray() ?? [])
            .Select(member => member!.GetValue<int>())];

    public IReadOnlyList<int> EnrolledIn(int sequenceId) =>
        [.. Enrollments.Where(entry => entry!["sequenceId"]!.GetValue<int>() == sequenceId)
            .Select(entry => entry!["contactId"]!.GetValue<int>())];

    // -------------------------------------------------------------------------------------------------------
    // Running it
    // -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Starts the stand-in the way the runtime will: the program a resolver finds on the search path, with the
    /// account named only by the environment.
    /// </summary>
    public Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(
        IReadOnlyList<string> args,
        CancellationToken cancellationToken) =>
        RunAsync(args, null, cancellationToken);

    /// <summary>The same, with a person, a name or a message on stdin — which is where every one of them travels.</summary>
    public Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(
        IReadOnlyList<string> args,
        string? stdin,
        CancellationToken cancellationToken) =>
        RunAsync(args, stdin, static (_, _) => Task.CompletedTask, cancellationToken);

    /// <summary>
    /// The same, with <paramref name="started"/> run between the start and the write of stdin, so a row can hold the
    /// stand-in to the moment it is about: gone before the text reaches it, for one.
    /// </summary>
    public async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(
        IReadOnlyList<string> args,
        string? stdin,
        Func<Process, CancellationToken, Task> started,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(started);

        // Spoken to as the runtime's own plugin host speaks to it, UTF-8 both ways through the product's one door, so
        // what a row sends and reads back is not left to this console's code page.
        var info = Jason.Contracts.Discovery.ChildProcess.StartInfo(StandInPath, Jason.Contracts.Discovery.ChildKind.SpeaksToJason);

        foreach (var argument in args)
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment[ConfigHomeVariable] = ConfigHome;

        using var process = Process.Start(info)!;
        await started(process, cancellationToken);
        try
        {
            if (stdin is not null)
            {
                await process.StandardInput.WriteAsync(stdin.AsMemory(), cancellationToken);
            }

            process.StandardInput.Close();
        }
        catch (IOException) when (Gone(process))
        {
            // A call refused before stdin is read can end the stand-in before the text reaches it, and the pipe then
            // refuses the write. What it answered is still on its stdout and stderr, which is what the row is about.
            Release(process.StandardInput);
        }

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, stdout, stderr);
    }

    /// <summary>
    /// Whether the stand-in has ended. A pipe refuses the write as soon as the process has closed its end, which can be
    /// a moment before the process is reported as exited, so the answer is waited for, a bounded while.
    /// </summary>
    private static bool Gone(Process process) => process.WaitForExit(TimeSpan.FromSeconds(10));

    /// <summary>
    /// Lets go of a stdin whose write was refused. Disposing it flushes what the refused write left buffered, which the
    /// pipe refuses once more; the handle is closed all the same.
    /// </summary>
    private static void Release(StreamWriter stdin)
    {
        try
        {
            stdin.Dispose();
        }
        catch (IOException)
        {
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_temporary, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // -------------------------------------------------------------------------------------------------------
    // The files
    // -------------------------------------------------------------------------------------------------------

    private ReplyAccount Script(JsonObject instruction)
    {
        var instructions = Read(InstructionsFile) as JsonObject ?? [];
        if (instructions["scripted"] is not JsonArray scripted)
        {
            scripted = [];
            instructions["scripted"] = scripted;
        }

        scripted.Add(instruction);
        Write(InstructionsFile, instructions);
        return this;
    }

    private ReplyAccount Replace(string fileName, int id, JsonObject record)
    {
        var records = ReadArray(fileName);
        foreach (var existing in records.ToList())
        {
            if (existing!["id"]!.GetValue<int>() == id)
            {
                records.Remove(existing);
            }
        }

        records.Add(record);
        Write(fileName, records);
        return this;
    }

    /// <summary>
    /// One of the account's files, read while the stand-in — another process — may be rewriting it. A plain read
    /// refuses a file another process holds open for writing, and a read that lands between the rewrite's truncation
    /// and its last byte parses nothing whole. Both are the write in progress, so the file is read again.
    /// </summary>
    private JsonNode? Read(string fileName)
    {
        var file = Path.Combine(Root, fileName);
        for (var attempt = 1; ; attempt++)
        {
            if (!File.Exists(file))
            {
                return null;
            }

            try
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return JsonNode.Parse(stream);
            }
            catch (Exception exception) when (attempt < 200 && exception is IOException or JsonException)
            {
                Thread.Sleep(25);
            }
        }
    }

    private JsonArray ReadArray(string fileName) => Read(fileName) as JsonArray ?? [];

    private void Write(string fileName, JsonNode content) =>
        File.WriteAllText(Path.Combine(Root, fileName), content.ToJsonString(), new UTF8Encoding(false));

    /// <summary>Reads a file another process may be appending to right now, which a plain read refuses.</summary>
    private static IEnumerable<string> ReadAllLines(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }
}
