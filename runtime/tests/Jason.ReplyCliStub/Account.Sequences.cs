using System.Globalization;
using System.Text.Json.Nodes;

// Deliberately in the global namespace with the rest of this program.

/// <summary>
/// The two raw calls on <c>/v3/sequences</c>: the list and the create, which the official package makes because
/// reply-cli has no command that creates a sequence. Both answer in the shape a live account answered them.
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

    /// <summary>
    /// Every sequence, newest first, paged with <c>top</c> (1 to 1000, 25 when absent) and <c>skip</c>. A <c>name</c>
    /// matches part of a name whatever the case, as Reply's own filter does. An item carries what Reply's list answers
    /// and nothing more: no steps, and nothing this stand-in keeps for its own refusals.
    /// </summary>
    private static (int, JsonNode?) SequenceList(string root, string query)
    {
        var top = Count(query, "top") ?? 25;
        var skip = Count(query, "skip") ?? 0;
        if (top is < 1 or > 1000 || skip < 0)
        {
            return (400, Problem(400, "Bad Request", "sequence.invalidPagination", "Invalid requested page view."));
        }

        IEnumerable<JsonObject> found = Records(root, SequencesFile);
        if (Parameter(query, "name") is { Length: > 0 } name)
        {
            found = found.Where(sequence => Text(sequence, "name").Contains(name, StringComparison.OrdinalIgnoreCase));
        }

        var ordered = found
            .Select((sequence, position) => (Sequence: sequence, Position: position))
            .OrderByDescending(row => Created(row.Sequence))
            .ThenByDescending(row => row.Position)
            .Select(row => row.Sequence)
            .ToList();
        var page = ordered.Skip(skip).Take(top).Select(sequence => (JsonNode)ListItem(sequence)).ToList();
        return (200, new JsonObject { ["items"] = new JsonArray([.. page]), ["hasMore"] = ordered.Count > skip + page.Count });
    }

    /// <summary>
    /// A new sequence, in Reply's <c>new</c> state and not archived, with the steps it was given chained in order. A
    /// name and at least one step are required, as Reply requires them; a name the account holds already is accepted,
    /// as Reply accepted it live. Reply attaches the default schedule and the default mailbox on its own, whether or not
    /// the request named either, and writes <c>created</c> with two fractional digits and an offset.
    /// </summary>
    private static (int, JsonNode?) SequenceCreate(string root, string? body)
    {
        var request = Request(body);
        if (request?["name"] is not JsonValue named || !named.TryGetValue<string>(out var name) || name.Trim().Length == 0)
        {
            return (400, Invalid("/name", "Name is required."));
        }

        if (request["steps"] is not JsonArray steps || steps.Count == 0)
        {
            return (400, Invalid("/steps", "At least one step is required."));
        }

        var sequences = Read(root, SequencesFile);
        var id = Math.Max(
            FirstCreatedSequenceId - 1,
            sequences.OfType<JsonObject>().Select(sequence => sequence["id"]!.GetValue<int>()).DefaultIfEmpty(0).Max()) + 1;

        var chain = new JsonArray();
        var stepId = FirstCreatedStepId + (id - FirstCreatedSequenceId) * 100;
        for (var position = 0; position < steps.Count; position++)
        {
            if (steps[position] is not JsonObject given || Text(given, "type").Length == 0 || given["delayInMinutes"] is not JsonValue)
            {
                return (400, Invalid($"/steps/{position}", "A step needs a type and a delay."));
            }

            var step = (JsonObject)given.DeepClone();
            step["id"] = stepId + position;
            step["parentId"] = position == 0 ? null : JsonValue.Create(stepId + position - 1);
            chain.Add(step);
        }

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
            ["steps"] = chain,
        };

        sequences.Add(sequence);
        Write(root, SequencesFile, sequences);
        return (201, Shown(sequence));
    }

    /// <summary>A sequence as the list answers one.</summary>
    private static JsonObject ListItem(JsonObject sequence) => new()
    {
        ["id"] = sequence["id"]!.DeepClone(),
        ["ownerUserId"] = sequence["ownerUserId"]?.DeepClone() ?? JsonValue.Create(42),
        ["name"] = sequence["name"]!.DeepClone(),
        ["status"] = sequence["status"]!.DeepClone(),
        ["created"] = sequence["created"]?.DeepClone() ?? JsonValue.Create(PlantedLongAgo),
        ["isArchived"] = sequence["isArchived"]!.DeepClone(),
        ["health"] = sequence["health"]?.DeepClone(),
    };

    private static DateTimeOffset Created(JsonObject sequence) =>
        DateTimeOffset.TryParse(
            sequence["created"] is JsonValue created && created.TryGetValue<string>(out var text) ? text : PlantedLongAgo,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var at)
            ? at
            : DateTimeOffset.MinValue;

    /// <summary>Reply's validation refusal, as it answered a create with no steps live.</summary>
    private static JsonObject Invalid(string pointer, string detail) => new()
    {
        ["errors"] = new JsonArray(new JsonObject { ["pointer"] = pointer, ["detail"] = detail }),
        ["title"] = "Validation failed",
    };

    /// <summary>One query parameter, by name and unescaped, or nothing where the call did not carry it.</summary>
    private static string? Parameter(string query, string name)
    {
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (separator > 0 && string.Equals(pair[..separator], name, StringComparison.Ordinal))
            {
                return Uri.UnescapeDataString(pair[(separator + 1)..].Replace('+', ' '));
            }
        }

        return null;
    }
}
