using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Contracts.Operations;
using Jason.Runtime.Domain;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

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

    /// <summary>The preview's member that carries the doubts a person reads before deciding.</summary>
    public const string PreviewKey = "doubts";

    /// <summary>
    /// The doubts that stand about <paramref name="item"/>, as the approval's preview carries them, or null where none does:
    /// the creator's <c>ask_person</c> first, then the review that stands when it doubts. Each entry is <c>source</c>,
    /// <c>by</c>, <c>role</c>, <c>code</c>, <c>note</c> and <c>line</c>. The sentences come from the item's row; a send
    /// parked before, whose row the first park cleared, takes its creator's sentence from its newest earlier approval that
    /// carried one, since <c>ask_person</c> is never taken back. A review's sentence is never carried over: the text it was
    /// about no longer stands, and only a doubt that stands now brings its sentence at all.
    /// </summary>
    public static async Task<JsonArray?> PreviewAsync(JasonDbContext db, WorkItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(item);
        var doubts = new JsonArray();
        if (AskPersonCode(item) is { } code)
        {
            doubts.Add(Entry(
                AskPersonKey,
                new ActorRef(item.CreatedByType, item.CreatedById),
                await CreatorsRoleAsync(db, item, cancellationToken).ConfigureAwait(false),
                code,
                item.AskPersonNote ?? await EarlierAskPersonNoteAsync(db, item, cancellationToken).ConfigureAwait(false),
                line: null));
        }

        if (await Reviews.StandingAsync(db, item, cancellationToken).ConfigureAwait(false) is { Result: ReviewResult.Doubts } review)
        {
            doubts.Add(Entry("review", review.By, review.Role, review.Code!, item.ReviewNote, review.Line));
        }

        return doubts.Count == 0 ? null : doubts;
    }

    /// <summary>
    /// Both sentences leave the row: the park moves what stands onto the preview, and nothing is kept at rest after it,
    /// whether or not a doubt stood. Called by the one park routine, in the park's own save.
    /// </summary>
    internal static void ClearAtPark(WorkItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.AskPersonNote = null;
        item.ReviewNote = null;
    }

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

    private static JsonObject Entry(string source, ActorRef by, string? role, string code, string? note, string? line) => new()
    {
        ["source"] = source,
        ["by"] = JsonSerializer.SerializeToNode(by, JasonJson.Options),
        ["role"] = role,
        ["code"] = code,
        ["note"] = note,
        ["line"] = line,
    };

    /// <summary>The role of the run that created the send, where an attempt did; null for anybody else.</summary>
    private static async Task<string?> CreatorsRoleAsync(JasonDbContext db, WorkItem item, CancellationToken cancellationToken)
    {
        if (item.CreatedByType != ActorType.Attempt || item.CreatedById is not { } attempt)
        {
            return null;
        }

        return await db.Attempts.AsNoTracking()
            .Where(a => a.PublicId == attempt)
            .Select(a => a.WorkItem!.Role)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The creator's sentence as an earlier approval of the item's carried it, the newest first; null where none did. Read
    /// through the campaign's approvals, so it is bounded by them.
    /// </summary>
    private static async Task<string?> EarlierAskPersonNoteAsync(JasonDbContext db, WorkItem item, CancellationToken cancellationToken)
    {
        var earlier = await db.Approvals.FromSqlInterpolated($"""
                SELECT * FROM approvals INDEXED BY ix_approvals_campaign_id
                WHERE campaign_id = {item.CampaignId} AND work_item_id = {item.Id}
                ORDER BY id DESC
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var approval in earlier)
        {
            if (approval.Preview[PreviewKey] is JsonArray entries
                && entries.FirstOrDefault(entry => (string?)entry?["source"] == AskPersonKey) is JsonObject asked
                && (string?)asked["note"] is { } note)
            {
                return note;
            }
        }

        return null;
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
