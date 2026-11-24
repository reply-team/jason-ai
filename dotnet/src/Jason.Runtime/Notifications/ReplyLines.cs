using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Notifications;

/// <summary>
/// The lines that say a person wrote back: a <c>reply_classified</c> verdict a role recorded inside a run — the line names an
/// attempt — whose nature is <c>person</c>, and which is not the desktop chat's. The eighth thing a pointer says, counted by
/// the sender in the window since the last pointer that reached anybody, and by reply, not by line or campaign: every
/// verdict on one reply carries the reply's own key, and the key names no campaign. A person's own verdict, recorded outside a
/// run, is one they already know of; an automatic answer, a bounce, an opt-out and a verdict that does not say are no
/// person's reply; none of them is counted.
/// </summary>
/// <remarks>
/// It wakes a pointer at the end of the run that recorded it — a <c>workitem_succeeded</c>, <c>workitem_failed</c> or
/// <c>workitem_expired</c> line of an item one of whose attempts recorded such a verdict first — so the approval the answer
/// needs, parked at the claim after the run, is said in the same pointer. A verdict recorded after its item already ended
/// wakes one by its own line. A reply said before, by an earlier verdict of the same marks, wakes nothing again.
/// </remarks>
public static class ReplyLines
{
    public const string Kind = JournalKinds.ReplyClassified;

    /// <summary>The nature a verdict gives a reply a person wrote themselves, in the runtime's closed words.</summary>
    public const string PersonNature = "person";

    /// <summary>The ends of a run that wake a pointer for the replies it judged. A cancellation ends no judging run.</summary>
    public static IReadOnlyList<string> RunEnds { get; } = [JournalKinds.WorkItemSucceeded, JournalKinds.WorkItemFailed, JournalKinds.WorkItemExpired];

    /// <summary>Whether a line of this kind may wake a pointer, which only a read of the journal around it decides.</summary>
    public static bool MayWake(string kind) => string.Equals(kind, Kind, StringComparison.Ordinal) || RunEnds.Contains(kind, StringComparer.Ordinal);

    /// <summary>
    /// Whether a verdict counts: it names an attempt, it says a person wrote the reply, and it is not marked as the chat's. The
    /// same marks <see cref="Count"/> and <see cref="Waking"/> read in SQL: the nature compared as text, as SQLite's
    /// <c>json_extract</c> and <c>=</c> compare it, and only a JSON <c>true</c> as the chat's mark.
    /// </summary>
    public static bool Counts(JsonNode? payload, string? attemptId) =>
        attemptId is not null
        && payload is JsonObject line
        && line["nature"] is JsonValue nature && nature.GetValueKind() == JsonValueKind.String
        && string.Equals(nature.GetValue<string>(), PersonNature, StringComparison.Ordinal)
        && line["confined"]?.GetValueKind() != JsonValueKind.True;

    /// <summary>
    /// How many replies were said in <c>(after, upTo]</c>: the keys of the verdicts that count, less the keys a verdict of the
    /// same marks already said at or before the window's start. Counted by the database, through the kind index, the window
    /// bounded on both sides and what came before it bounded by its start, so a window that grows while no pointer reaches
    /// anybody is never read into memory.
    /// </summary>
    public static IQueryable<int> Count(JasonDbContext db, int after, int upTo)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Database.SqlQuery<int>($"""
            SELECT count(*) AS "Value" FROM (
                SELECT key FROM journal INDEXED BY ix_journal_kind
                WHERE kind = {Kind} AND id > {after} AND id <= {upTo} AND key IS NOT NULL AND attempt_id IS NOT NULL
                    AND json_extract(new_json, '$.nature') = {PersonNature} AND json_type(new_json, '$.confined') IS NOT 'true'
                EXCEPT
                SELECT key FROM journal INDEXED BY ix_journal_kind
                WHERE kind = {Kind} AND id <= {after} AND key IS NOT NULL AND attempt_id IS NOT NULL
                    AND json_extract(new_json, '$.nature') = {PersonNature} AND json_type(new_json, '$.confined') IS NOT 'true')
            """);
    }

    /// <summary>
    /// The lines in <c>[from, to]</c> that wake a pointer for a reply: the end of a run one of whose verdicts before it counts
    /// and is the first of its reply's key to count, and such a verdict recorded after its item had already ended. The ends
    /// and the verdicts are read through the kind index bounded by the batch, a run's verdicts and ends through its work item,
    /// and the verdicts before one through the kind index again.
    /// </summary>
    public static IQueryable<int> Waking(JasonDbContext db, int from, int to)
    {
        ArgumentNullException.ThrowIfNull(db);
        string[] ends = [.. RunEnds];
        return db.Database.SqlQuery<int>($"""
            SELECT ended.id AS "Value" FROM journal AS ended INDEXED BY ix_journal_kind
            WHERE ended.kind IN ({ends[0]}, {ends[1]}, {ends[2]}) AND ended.id >= {from} AND ended.id <= {to}
                AND ended.work_item_id IS NOT NULL
                AND EXISTS (
                    SELECT 1 FROM journal AS verdict
                    WHERE verdict.work_item_id = ended.work_item_id AND verdict.kind = {Kind} AND verdict.id < ended.id
                        AND verdict.key IS NOT NULL AND verdict.attempt_id IS NOT NULL
                        AND json_extract(verdict.new_json, '$.nature') = {PersonNature} AND json_type(verdict.new_json, '$.confined') IS NOT 'true'
                        AND NOT EXISTS (
                            SELECT 1 FROM journal AS earlier INDEXED BY ix_journal_kind
                            WHERE earlier.kind = {Kind} AND earlier.id < verdict.id AND earlier.key = verdict.key AND earlier.attempt_id IS NOT NULL
                                AND json_extract(earlier.new_json, '$.nature') = {PersonNature} AND json_type(earlier.new_json, '$.confined') IS NOT 'true'))
            UNION
            SELECT verdict.id AS "Value" FROM journal AS verdict INDEXED BY ix_journal_kind
            WHERE verdict.kind = {Kind} AND verdict.id >= {from} AND verdict.id <= {to}
                AND verdict.key IS NOT NULL AND verdict.attempt_id IS NOT NULL AND verdict.work_item_id IS NOT NULL
                AND json_extract(verdict.new_json, '$.nature') = {PersonNature} AND json_type(verdict.new_json, '$.confined') IS NOT 'true'
                AND EXISTS (
                    SELECT 1 FROM journal AS ended
                    WHERE ended.work_item_id = verdict.work_item_id AND ended.kind IN ({ends[0]}, {ends[1]}, {ends[2]}) AND ended.id < verdict.id)
                AND NOT EXISTS (
                    SELECT 1 FROM journal AS earlier INDEXED BY ix_journal_kind
                    WHERE earlier.kind = {Kind} AND earlier.id < verdict.id AND earlier.key = verdict.key AND earlier.attempt_id IS NOT NULL
                        AND json_extract(earlier.new_json, '$.nature') = {PersonNature} AND json_type(earlier.new_json, '$.confined') IS NOT 'true')
            """);
    }
}
