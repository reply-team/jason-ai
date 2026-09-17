using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>One call the stand-in was asked to make, as it recorded it before answering.</summary>
/// <param name="Args">The whole argument vector, which is what makes "this flag never appears" assertable.</param>
/// <param name="Environment">
/// Every vendor-named variable the stand-in was started with. No <c>REPLY_*</c> name enters the runtime, so what
/// an operator exported in their own shell must not reach a child; the only one that may be here is the one the
/// package sets on the call itself, and this is where that is read rather than assumed.
/// </param>
public sealed record ReplyCall(
    string Method,
    string Path,
    string? Body,
    IReadOnlyList<string> Args,
    IReadOnlyDictionary<string, string> Environment);

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
    public const string InstructionsFile = "instructions.json";
    public const string CredentialFile = "credential.json";

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

    /// <summary>A person this account already holds. Identifiers are integers, as Reply's are.</summary>
    public ReplyAccount WithContact(int id, string email, string? firstName = null, bool optedOut = false)
    {
        Replace(ContactsFile, id, new JsonObject
        {
            ["id"] = id,
            ["email"] = email,
            ["firstName"] = firstName,
            ["lastName"] = null,
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
    /// A contact list. It is not shared unless a test says so, which is how the lists a person makes for
    /// themselves arrive — and a list that is not shared is one Reply's own <c>GET /v3/contacts/{id}/lists</c>
    /// does not report, as a live account showed.
    /// </summary>
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

    public ReplyAccount WithEnrollment(int sequence, int contact, string statusInSequence = "active")
    {
        var enrollments = ReadArray(EnrollmentsFile);
        foreach (var existing in enrollments.ToList())
        {
            if (existing!["sequenceId"]!.GetValue<int>() == sequence && existing["contactId"]!.GetValue<int>() == contact)
            {
                enrollments.Remove(existing);
            }
        }

        enrollments.Add(new JsonObject
        {
            ["sequenceId"] = sequence,
            ["contactId"] = contact,
            ["statusInSequence"] = statusInSequence,
            ["currentStep"] = 1,
        });

        Write(EnrollmentsFile, enrollments);
        return this;
    }

    // -------------------------------------------------------------------------------------------------------
    // What it can be told to do instead, through the directory and never through a flag
    // -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The next call to this method and path answers with this status and body, whatever the account holds. The
    /// body is written out as it is given, so a test can build an answer no provider would send.
    /// </summary>
    public ReplyAccount Answers(string method, string path, int code, string bodyJson) =>
        Script(new JsonObject
        {
            ["kind"] = "answer",
            ["method"] = method,
            ["path"] = path,
            ["code"] = code,
            ["body"] = bodyJson,
        });

    /// <summary>
    /// The next call to this method and path does everything it would have done and then says nothing useful
    /// about it: the write lands, the answer is a gateway failure. That is the ending a recovery read exists for.
    /// </summary>
    public ReplyAccount LosesTheAnswerAfterWriting(string method, string path) =>
        Script(new JsonObject { ["kind"] = "lost", ["method"] = method, ["path"] = path });

    /// <summary>
    /// The next call to this method and path writes exactly this on stdout and on stderr and ends with exactly
    /// this code, whatever it would otherwise have answered. It is the one ending <see cref="Answers"/> cannot
    /// express — that one always prints the CLI's own <c>{code, data}</c> around whatever it is given — and a
    /// caller has to survive stdout that is not an envelope at all: a stack trace, half a line, or nothing.
    /// </summary>
    public ReplyAccount Prints(string method, string path, int exitCode, string stdout, string? stderr = null) =>
        Script(new JsonObject
        {
            ["kind"] = "prints",
            ["method"] = method,
            ["path"] = path,
            ["exit_code"] = exitCode,
            ["stdout"] = stdout,
            ["stderr"] = stderr ?? string.Empty,
        });

    /// <summary>
    /// The next call to this method and path says it has started — by creating <paramref name="marker"/> — and
    /// then waits for <paramref name="until"/> to appear before answering as it otherwise would. It is what
    /// <see cref="Hangs"/> cannot do: a delay says how long to wait, never that the waiting has begun, and a
    /// test that has to interrupt a call in flight can only be deterministic if it observes the call rather
    /// than sleeping for it. The cap stops a test that never releases the call from holding a run open.
    /// </summary>
    public ReplyAccount HoldsOn(string method, string path, string marker, string until, int capMs = 120_000) =>
        Script(new JsonObject
        {
            ["kind"] = "hold",
            ["method"] = method,
            ["path"] = path,
            ["marker"] = marker,
            ["until"] = until,
            ["timeout_ms"] = capMs,
        });

    /// <summary>The next call to this method and path takes this long before it answers.</summary>
    public ReplyAccount Hangs(string method, string path, int milliseconds) =>
        Script(new JsonObject
        {
            ["kind"] = "hang",
            ["method"] = method,
            ["path"] = path,
            ["milliseconds"] = milliseconds,
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
                    call["method"]!.GetValue<string>(),
                    call["path"]!.GetValue<string>(),
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

    public JsonArray Lists => ReadArray(ListsFile);

    public JsonArray Sequences => ReadArray(SequencesFile);

    public JsonArray Enrollments => ReadArray(EnrollmentsFile);

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

    /// <summary>The same, with a request body on stdin — which is where every body travels.</summary>
    public async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(
        IReadOnlyList<string> args,
        string? stdin,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);

        var info = new ProcessStartInfo(StandInPath)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in args)
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment[ConfigHomeVariable] = ConfigHome;

        using var process = Process.Start(info)!;
        if (stdin is not null)
        {
            await process.StandardInput.WriteAsync(stdin.AsMemory(), cancellationToken);
        }

        process.StandardInput.Close();

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, stdout, stderr);
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

    private JsonNode? Read(string fileName)
    {
        var file = Path.Combine(Root, fileName);
        return File.Exists(file) ? JsonNode.Parse(File.ReadAllText(file)) : null;
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
