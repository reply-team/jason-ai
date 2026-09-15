using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

// Deliberately in the global namespace, as the top-level program in this project is: a namespace named after
// the assembly would be found ahead of the test helper class of the same name in the projects that reference it.

/// <summary>
/// The stand-in vendor's account, held as a directory of JSON files, and the handful of subcommands a plugin
/// calls to work in it. It stands for the thing a canonical operation always ends at: a provider with its own
/// identifiers, its own list and campaign state, its own suppression register, and its own record of what each
/// idempotency key already did.
/// </summary>
/// <remarks>
/// Every subcommand reads one JSON request on stdin and writes exactly one JSON answer on stdout. Nothing here
/// touches the network or a shell, and nothing is written outside the directory it was given, so a test can say
/// what happened at the provider by reading the files back. A provider's refusal is an answer with an
/// <c>error</c>, not a crash; the one non-zero exit is the forced crash a test asks for, which is how "the effect
/// happened and the answer was lost" is reproduced without changing a line of canonical input.
/// </remarks>
internal static class Workspace
{
    private const int Usage = 2;

    /// <summary>The exit code of the one failure a test can force: the write landed, the answer never came.</summary>
    private const int LostAnswer = 3;

    private const string ContactsFile = "contacts.json";
    private const string ListsFile = "lists.json";
    private const string CampaignsFile = "campaigns.json";
    private const string LedgerFile = "ledger.json";
    private const string SuppressionFile = "suppression.json";
    private const string InstructionsFile = "instructions.json";
    private const string CallsFile = "calls.json";

    /// <summary>Where this provider's identifiers start; the number is meaningless, its stability is not.</summary>
    private const int FirstContactNumber = 1001;

    public static async Task<int> RunAsync(string root, IReadOnlyList<string> subcommand)
    {
        if (!Directory.Exists(root))
        {
            await Console.Error.WriteLineAsync("fake-cli: no such workspace");
            return Usage;
        }

        var request = await ReadRequestAsync();
        if (request is null)
        {
            await Console.Error.WriteLineAsync("fake-cli: the request on stdin is not a JSON object");
            return Usage;
        }

        var verb = string.Join(' ', subcommand);
        if (!Known(verb))
        {
            await Console.Error.WriteLineAsync($"fake-cli: '{verb}' is not a workspace subcommand");
            return Usage;
        }

        // Every call is recorded before it runs. What a plugin did is then a fact a test can read rather than
        // something inferred from the state that happened to be left behind: an obligation to read before
        // writing is invisible against a provider that would have answered the same either way.
        RecordCall(root, verb, Text(request, "key"));

        switch (verb)
        {
            case "contact ensure":
                return await AnswerAsync(EnsureContact(root, request));

            case "list add":
                return await EffectAsync(root, request, AddToList(root, request));

            case "campaign get":
                return await AnswerAsync(GetCampaign(root, request));

            case "campaign enroll":
                return await EffectAsync(root, request, Enroll(root, request));

            default:
                return await AnswerAsync(GetLedgerEntry(root, request));
        }
    }

    /// <summary>The five subcommands this account answers; anything else is the caller's usage error.</summary>
    private static bool Known(string verb) =>
        verb is "contact ensure" or "list add" or "campaign get" or "campaign enroll" or "ledger get";

    // -------------------------------------------------------------------------------------------------------
    // The subcommands
    // -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Finds or creates the provider's own contact. A caller that already holds an identifier is answered by it
    /// and the address in the request is never matched against — the record of how each call asked is kept, so a
    /// test can prove the difference rather than take it on trust.
    /// </summary>
    private static JsonObject EnsureContact(string root, JsonObject request)
    {
        var contacts = ReadObject(root, ContactsFile);
        var channel = Text(request, "channel");
        var value = Text(request, "value");
        var pinned = Text(request, "external_id");

        if (pinned.Length > 0)
        {
            if (contacts[pinned] is not JsonObject held)
            {
                return Error("contact_not_found", $"This account holds no contact {pinned}.");
            }

            RecordAsk(held, "by_id");
            Write(root, ContactsFile, contacts);
            return Suppressed(root, Text(held, "value"))
                ?? new JsonObject { ["id"] = pinned, ["created"] = false, ["asked"] = "by_id" };
        }

        if (Suppressed(root, value) is { } refusal)
        {
            return refusal;
        }

        foreach (var (id, node) in contacts)
        {
            if (node is JsonObject candidate
                && string.Equals(Text(candidate, "channel"), channel, StringComparison.Ordinal)
                && string.Equals(Text(candidate, "value"), value, StringComparison.Ordinal))
            {
                RecordAsk(candidate, "by_channel");
                Write(root, ContactsFile, contacts);
                return new JsonObject { ["id"] = id, ["created"] = false, ["asked"] = "by_channel" };
            }
        }

        var created = "p_" + (FirstContactNumber + contacts.Count).ToString(CultureInfo.InvariantCulture);
        var record = new JsonObject
        {
            ["id"] = created,
            ["channel"] = channel,
            ["value"] = value,
            ["asks"] = new JsonArray(),
        };
        RecordAsk(record, "by_channel");
        contacts[created] = record;
        Write(root, ContactsFile, contacts);
        return new JsonObject { ["id"] = created, ["created"] = true, ["asked"] = "by_channel" };
    }

    /// <summary>
    /// Puts a contact on a list, once per idempotency key. A key this account has already seen is answered from
    /// the ledger without touching the list again, which is what makes a repeat safe.
    /// </summary>
    private static JsonObject AddToList(string root, JsonObject request)
    {
        var key = Text(request, "key");
        if (ReadObject(root, LedgerFile)[key] is JsonObject already)
        {
            return new JsonObject
            {
                ["status"] = "already_member",
                ["from_ledger"] = true,
                ["recorded"] = Text(already, "status"),
            };
        }

        var lists = ReadObject(root, ListsFile);
        var listId = Text(request, "list_id");
        var contactId = Text(request, "contact_id");
        if (lists[listId] is not JsonObject list)
        {
            return Error("list_not_found", $"This account holds no list {listId}.");
        }

        var members = list["members"]!.AsArray();
        var present = members.Any(member => string.Equals(member!.GetValue<string>(), contactId, StringComparison.Ordinal));
        if (!present)
        {
            members.Add(contactId);
            Write(root, ListsFile, lists);
        }

        var status = present ? "already_member" : "added";
        Record(root, key, new JsonObject
        {
            ["key"] = key,
            ["operation"] = "list_membership.add",
            ["status"] = status,
            ["list_id"] = listId,
            ["contact_id"] = contactId,
        });

        return new JsonObject { ["status"] = status, ["from_ledger"] = false };
    }

    private static JsonObject GetCampaign(string root, JsonObject request)
    {
        var campaigns = ReadObject(root, CampaignsFile);
        var id = Text(request, "external_id");
        if (campaigns[id] is not JsonObject campaign)
        {
            return Error("campaign_not_found", $"This account holds no campaign {id}.");
        }

        var status = Text(campaign, "status");
        return new JsonObject
        {
            ["id"] = id,
            ["name"] = Text(campaign, "name"),
            ["status"] = status,
            ["live"] = IsLive(status),
            ["counts"] = new JsonObject { ["enrolled"] = campaign["enrollments"]!.AsArray().Count },
            ["vendor"] = campaign["vendor"]?.DeepClone() ?? new JsonObject(),
        };
    }

    /// <summary>
    /// Enrolls a contact into a campaign, once per idempotency key, and says whether the campaign was live at the
    /// moment it happened — because that is what decides whether the enrollment was bookkeeping or a send.
    /// </summary>
    private static JsonObject Enroll(string root, JsonObject request)
    {
        var campaigns = ReadObject(root, CampaignsFile);
        var campaignId = Text(request, "campaign_id");
        if (campaigns[campaignId] is not JsonObject campaign)
        {
            return Error("campaign_not_found", $"This account holds no campaign {campaignId}.");
        }

        var status = Text(campaign, "status");
        var live = IsLive(status);
        var key = Text(request, "key");
        if (ReadObject(root, LedgerFile)[key] is JsonObject already)
        {
            return new JsonObject
            {
                ["status"] = "already_enrolled",
                ["live"] = live,
                ["from_ledger"] = true,
                ["recorded"] = Text(already, "status"),
            };
        }

        if (string.Equals(status, "Archived", StringComparison.Ordinal) || string.Equals(status, "Finished", StringComparison.Ordinal))
        {
            return Error("campaign_not_enrollable", $"Campaign {campaignId} is {status} and takes no enrollment.");
        }

        var contactId = Text(request, "contact_id");
        var enrollments = campaign["enrollments"]!.AsArray();
        var present = enrollments.Any(contact => string.Equals(contact!.GetValue<string>(), contactId, StringComparison.Ordinal));
        if (present && string.Equals(Text(request, "collision"), "refuse", StringComparison.Ordinal))
        {
            return Error("collision_refused", $"Contact {contactId} already takes part in campaign {campaignId}.");
        }

        if (!present)
        {
            enrollments.Add(contactId);
            Write(root, CampaignsFile, campaigns);
        }

        var outcome = present ? "already_enrolled" : "enrolled";
        Record(root, key, new JsonObject
        {
            ["key"] = key,
            ["operation"] = "campaign.enroll",
            ["status"] = outcome,
            ["campaign_id"] = campaignId,
            ["contact_id"] = contactId,
            ["live"] = live,
            ["start"] = Text(request, "start"),
            ["first_touch"] = Text(request, "first_touch"),
        });

        return new JsonObject { ["status"] = outcome, ["live"] = live, ["from_ledger"] = false };
    }

    private static JsonObject GetLedgerEntry(string root, JsonObject request)
    {
        var entry = ReadObject(root, LedgerFile)[Text(request, "key")];
        return new JsonObject
        {
            ["found"] = entry is not null,
            ["entry"] = entry?.DeepClone(),
        };
    }

    // -------------------------------------------------------------------------------------------------------
    // Answering, and the one way to fail after having acted
    // -------------------------------------------------------------------------------------------------------

    private static async Task<int> AnswerAsync(JsonObject answer)
    {
        await Console.Out.WriteAsync(answer.ToJsonString());
        await Console.Out.FlushAsync();
        return 0;
    }

    /// <summary>
    /// Answers a subcommand that may have changed the account — unless a test has asked this key to die after the
    /// effect, in which case the effect stands, nothing is written to stdout, and the program exits non-zero. A
    /// caller then knows only that it called; whether anything happened is what the recovery read is for.
    /// </summary>
    private static async Task<int> EffectAsync(string root, JsonObject request, JsonObject answer)
    {
        if (answer["error"] is not null || !ShouldFailAfterEffect(root, Text(request, "key")))
        {
            return await AnswerAsync(answer);
        }

        await Console.Error.WriteLineAsync("fake-cli: the effect was written and the answer was lost");
        return LostAnswer;
    }

    private static bool ShouldFailAfterEffect(string root, string key)
    {
        var instructions = ReadObject(root, InstructionsFile);
        if (instructions["fail_after_effect"] is not JsonObject instruction
            || !string.Equals(Text(instruction, "key"), key, StringComparison.Ordinal))
        {
            return false;
        }

        if (instruction["once"] is null || instruction["once"]!.GetValue<bool>())
        {
            instructions.Remove("fail_after_effect");
            Write(root, InstructionsFile, instructions);
        }

        return true;
    }

    // -------------------------------------------------------------------------------------------------------
    // The files
    // -------------------------------------------------------------------------------------------------------

    private static async Task<JsonObject?> ReadRequestAsync()
    {
        var text = await Console.In.ReadToEndAsync();
        if (text.Trim().Length == 0)
        {
            return [];
        }

        try
        {
            return JsonNode.Parse(text) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>The call log: one entry per subcommand, in the order the account was asked.</summary>
    private static void RecordCall(string root, string verb, string key)
    {
        var file = Path.Combine(root, CallsFile);
        var calls = File.Exists(file) ? JsonNode.Parse(File.ReadAllText(file))!.AsArray() : [];
        calls.Add(new JsonObject { ["subcommand"] = verb, ["key"] = key });
        File.WriteAllText(file, calls.ToJsonString(), new UTF8Encoding(false));
    }

    private static void Record(string root, string key, JsonObject entry)
    {
        var ledger = ReadObject(root, LedgerFile);
        ledger[key] = entry;
        Write(root, LedgerFile, ledger);
    }

    /// <summary>The provider's suppression register, re-read on every call: it is the provider's answer, not ours.</summary>
    private static JsonObject? Suppressed(string root, string value)
    {
        var file = Path.Combine(root, SuppressionFile);
        if (!File.Exists(file))
        {
            return null;
        }

        var suppressed = JsonNode.Parse(File.ReadAllText(file))!.AsArray()
            .Any(entry => string.Equals(entry!.GetValue<string>(), value, StringComparison.OrdinalIgnoreCase));

        return suppressed ? Error("suppressed", $"This account suppresses {value}.") : null;
    }

    private static void RecordAsk(JsonObject contact, string how)
    {
        if (contact["asks"] is not JsonArray asks)
        {
            asks = [];
            contact["asks"] = asks;
        }

        asks.Add(how);
    }

    private static bool IsLive(string providerStatus) => string.Equals(providerStatus, "Active", StringComparison.Ordinal);

    private static JsonObject Error(string code, string message) =>
        new() { ["error"] = new JsonObject { ["code"] = code, ["message"] = message } };

    private static JsonObject ReadObject(string root, string fileName)
    {
        var file = Path.Combine(root, fileName);
        return File.Exists(file) ? JsonNode.Parse(File.ReadAllText(file))!.AsObject() : [];
    }

    private static void Write(string root, string fileName, JsonNode content) =>
        File.WriteAllText(Path.Combine(root, fileName), content.ToJsonString(), new UTF8Encoding(false));

    private static string Text(JsonObject parent, string name) =>
        parent[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : string.Empty;
}
