using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

// Deliberately in the global namespace with the rest of this program.

/// <summary>
/// The two commands on sequences by name, as reply-cli 0.13.0 documents them: <c>sequence list --name -</c>, every
/// sequence whose whole name is this one ignoring case, oldest first, read to its end; and <c>sequence create --name
/// -</c>, a draft of that name holding nobody, with the one task step a sequence needs to exist. The name is read from
/// stdin only, and no message repeats it.
/// </summary>
internal static partial class Account
{
    /// <summary>When a sequence planted without a moment of its own was created: long before any test's work item.</summary>
    private const string PlantedLongAgo = "2026-01-05T09:15:42.17+00:00";

    /// <summary>The account's default schedule, which Reply attaches to a sequence created without one.</summary>
    private const int DefaultSchedule = 501;

    /// <summary>Where the identifiers of the sequences this stand-in creates start: clear of every planted one.</summary>
    private const int FirstCreatedSequenceId = 2001;

    private const int FirstCreatedStepId = 9001;

    /// <summary>How many sequences whose names contain the name the list reads before it refuses to decide: 20 pages of 1000.</summary>
    private const int ListCap = 20_000;

    /// <summary>The one step reply-cli gives a sequence it creates: a task for its owner, which reaches nobody.</summary>
    private static JsonObject Placeholder() => new()
    {
        ["type"] = "task",
        ["actionType"] = "toDo",
        ["delayInMinutes"] = 0,
        ["description"] = "Added because a sequence needs one step to exist; replace it before this sequence is ever started.",
    };

    private static Ending SequenceListByName(string root, Parsed line, string? stdin)
    {
        var (name, refusal) = NameOf(line, stdin, "list");
        if (refusal is not null)
        {
            return refusal;
        }

        var sequences = Records(root, SequencesFile).ToList();
        if (sequences.Count(sequence => Text(sequence, "name").Contains(name!, StringComparison.OrdinalIgnoreCase)) > ListCap)
        {
            return Refused(
                "sequence.list_incomplete",
                "Read 20000 sequences whose names contain this one and Reply said there were more, so it could not be decided which carry it.",
                status: 200);
        }

        var wanted = name!.ToLowerInvariant();
        var items = sequences
            .Select((sequence, position) => (Sequence: sequence, Position: position))
            .Where(row => Text(row.Sequence, "name").ToLowerInvariant() == wanted)
            .OrderBy(row => Created(row.Sequence))
            .ThenBy(row => row.Position)
            .Select(row => (JsonNode)new JsonObject
            {
                ["id"] = row.Sequence["id"]!.DeepClone(),
                ["name"] = row.Sequence["name"]!.DeepClone(),
                ["status"] = row.Sequence["status"] is JsonValue status && status.TryGetValue<string>(out var word) ? word : null,
                ["is_archived"] = row.Sequence["isArchived"] is JsonValue archived && archived.TryGetValue<bool>(out var flag) ? flag : null,
                ["created_at"] = row.Sequence["created"]?.DeepClone() ?? JsonValue.Create(PlantedLongAgo),
            })
            .ToList();

        return Done(new JsonObject { ["items"] = new JsonArray([.. items]) });
    }

    /// <summary>
    /// A new sequence, in Reply's <c>new</c> state and not archived, with the one task step. A name the account holds
    /// already is accepted, as Reply accepts it: the command does not look first. Reply attaches the default schedule and
    /// the default mailbox on its own, and writes <c>created</c> with two fractional digits and an offset.
    /// </summary>
    private static Ending SequenceCreateByName(string root, Parsed line, string? stdin)
    {
        var (name, refusal) = NameOf(line, stdin, "create");
        if (refusal is not null)
        {
            return refusal;
        }

        var sequences = Read(root, SequencesFile);
        var id = Math.Max(
            FirstCreatedSequenceId - 1,
            sequences.OfType<JsonObject>().Select(sequence => sequence["id"]!.GetValue<int>()).DefaultIfEmpty(0).Max()) + 1;

        var step = Placeholder();
        step["id"] = FirstCreatedStepId + (id - FirstCreatedSequenceId) * 100;
        step["parentId"] = null;

        var sequence = new JsonObject
        {
            ["id"] = id,
            ["ownerUserId"] = 42,
            ["name"] = name,
            ["status"] = "new",
            ["isArchived"] = false,
            ["health"] = "healthy",
            ["created"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.ff'+00:00'", CultureInfo.InvariantCulture),
            ["scheduleId"] = DefaultSchedule,
            ["emailAccounts"] = new JsonArray(SendingAccount),
            ["steps"] = new JsonArray(step),
        };

        sequences.Add(sequence);
        Write(root, SequencesFile, sequences);
        return Done(new JsonObject { ["sequence_id"] = id, ["steps_added"] = 1, ["status"] = "new", ["is_archived"] = false });
    }

    /// <summary>
    /// The name as reply-cli reads it: <c>--name -</c> and nothing else, then <c>{name}</c> as JSON on stdin, a string
    /// that is not blank, with no other field. A refusal never repeats the text.
    /// </summary>
    private static (string? Name, Ending? Refusal) NameOf(Parsed line, string? stdin, string verb)
    {
        var hint = $"Pipe it in: echo '{{\"name\":\"…\"}}' | reply sequence {verb} --name -";
        if (line.Option("--name") != "-")
        {
            return (null, Refused("usage.name", "--name takes only -: the name is read from stdin.", exit: 2, detail: hint));
        }

        JsonObject given;
        try
        {
            given = JsonNode.Parse(stdin ?? string.Empty) as JsonObject ?? throw new JsonException("not an object");
        }
        catch (JsonException)
        {
            return (null, Refused("usage.name", "stdin must be one JSON object.", exit: 2, detail: hint));
        }

        if (given.Any(member => member.Key != "name"))
        {
            return (null, Refused("usage.name", "stdin carries a field other than name.", exit: 2, detail: hint));
        }

        return given["name"] is JsonValue value && value.TryGetValue<string>(out var name) && name.Trim().Length > 0
            ? (name, null)
            : (null, Refused("usage.name", "The name must be a string that is not blank.", exit: 2));
    }

    private static DateTimeOffset Created(JsonObject sequence) =>
        DateTimeOffset.TryParse(
            sequence["created"] is JsonValue created && created.TryGetValue<string>(out var text) ? text : PlantedLongAgo,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var at)
            ? at
            : DateTimeOffset.MinValue;
}
