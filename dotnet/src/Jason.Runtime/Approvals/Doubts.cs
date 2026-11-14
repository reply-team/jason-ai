using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Runtime.Domain;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Approvals;

/// <summary>
/// A doubt about a send, from either of its two doors: the creator's <c>ask_person</c>, set in the context at create and
/// never taken back, and a doubting review (<c>workitem.review</c>). Each carries a code from <see cref="DoubtCodes"/> and
/// may carry one sentence, which is a person's or an agent's text: it is kept on the item outside the context, never
/// journaled and never logged, and moved onto the approval's preview at the park, where the person reads it.
/// </summary>
public static class Doubts
{
    /// <summary>The reserved context key a creator escalates its own send with: <c>{"code": …}</c> and nothing else.</summary>
    public const string AskPersonKey = "ask_person";

    /// <summary>The one member of <see cref="AskPersonKey"/>.</summary>
    public const string CodeMember = "code";

    /// <summary>The request field that carries <see cref="AskPersonKey"/>'s sentence, beside the context.</summary>
    public const string AskPersonNoteField = "ask_person_note";

    /// <summary>The code a context's <see cref="AskPersonKey"/> carries, or null where it carries none.</summary>
    public static string? AskPersonCode(WorkItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Context[AskPersonKey] is JsonObject asked && asked[CodeMember] is JsonValue code && code.TryGetValue(out string? text) ? text : null;
    }

    /// <summary>
    /// The creator's <c>ask_person</c> and its sentence, as a create asks for them: the key only on a send a person decides
    /// each one of, holding exactly a code of the list; the sentence only beside the key, one line of at most
    /// <see cref="DoubtCodes.MaxNoteLength"/> characters.
    /// </summary>
    internal static void ValidateCreate(WorkItemCreateRequest request, ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(errors);
        var field = "context." + AskPersonKey;
        JsonNode? asked = null;
        var named = request.Context?.TryGetPropertyValue(AskPersonKey, out asked) == true;
        if (named)
        {
            var operation = request.Operation?.Trim();
            if (request.Kind != WorkItemKind.ProviderOp || !ReviewSubject.IsReviewable(operation))
            {
                errors.Add(field, "not_applicable", "ask_person is for a send a person decides each one of: provider_op work whose operation's approval is confirm_each.");
            }
            else
            {
                ValidateAskPerson(asked, field, errors);
            }
        }

        if (request.AskPersonNote is not null)
        {
            if (!named)
            {
                errors.Add(AskPersonNoteField, "not_allowed", "ask_person_note is the sentence beside the context key ask_person, and is refused without it.");
            }
            else
            {
                Note(request.AskPersonNote, AskPersonNoteField, errors);
            }
        }
    }

    /// <summary>A patch never names <see cref="AskPersonKey"/>: it is set at create and never moved or taken back, by anybody.</summary>
    internal static void RefuseInPatch(JsonObject? set, IReadOnlyList<string>? unset, ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (set?.ContainsKey(AskPersonKey) == true)
        {
            errors.Add("set." + AskPersonKey, "not_allowed", "ask_person is set when the work is created and never changed or taken back.");
        }

        for (var index = 0; index < (unset?.Count ?? 0); index++)
        {
            if (string.Equals(unset![index], AskPersonKey, StringComparison.Ordinal))
            {
                errors.Add(string.Create(CultureInfo.InvariantCulture, $"unset[{index}]"), "not_allowed", "ask_person is set when the work is created and never changed or taken back.");
            }
        }
    }

    /// <summary>
    /// A doubt's sentence: trimmed, one line, something in it, at most <see cref="DoubtCodes.MaxNoteLength"/> characters. The
    /// message never repeats it.
    /// </summary>
    internal static string? Note(string? value, string field, ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (value is null)
        {
            return null;
        }

        var note = value.Trim();
        if (note.Length == 0)
        {
            errors.Add(field, "invalid", $"{field} is one sentence with something in it, or is left out.");
        }
        else if (note.Length > DoubtCodes.MaxNoteLength)
        {
            errors.Add(field, "too_long", string.Create(CultureInfo.InvariantCulture, $"{field} must be at most {DoubtCodes.MaxNoteLength} characters."));
        }
        else if (note.EnumerateRunes().Any(PreviewTemplate.ChangesTheLine))
        {
            errors.Add(field, "invalid", $"{field} is one line of text, without control, format or line-break characters.");
        }

        return note;
    }

    /// <summary>A code of the closed list, refused at <paramref name="field"/> with the list spelled out.</summary>
    internal static void Code(string? code, string field, ValidationErrors errors)
    {
        if (!DoubtCodes.Knows(code))
        {
            errors.Add(field, "invalid", $"{field} is one of: {string.Join(", ", DoubtCodes.All)}.");
        }
    }

    private static void ValidateAskPerson(JsonNode? asked, string field, ValidationErrors errors)
    {
        if (asked is not JsonObject members)
        {
            errors.Add(field, "invalid", "ask_person is an object holding one member, code.");
            return;
        }

        foreach (var (name, _) in members)
        {
            if (string.Equals(name, CodeMember, StringComparison.Ordinal))
            {
                continue;
            }

            errors.Add(
                $"{field}.{name}",
                "not_allowed",
                string.Equals(name, "note", StringComparison.Ordinal)
                    ? "ask_person holds the code alone: its sentence goes in ask_person_note, beside the context, which no line carries."
                    : "ask_person holds one member, code.");
        }

        var code = members[CodeMember] is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;
        Code(code, $"{field}.{CodeMember}", errors);
    }
}
