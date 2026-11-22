using Jason.Contracts.Api;
using Jason.Runtime.Journal;

namespace Jason.Runtime.Dispatch;

/// <summary>
/// One line of the chronicle as the summon is allowed to see it: a kind, some identifiers, and who wrote it.
/// </summary>
/// <remarks>
/// A type rather than a discipline. The dispatcher must not read meaning, and the surest way to keep that rule
/// is a shape that cannot carry any: there is nowhere here to put <c>old</c>, <c>new</c>, <c>key</c> or
/// <c>reason</c>. It is also what the query projects, so those columns — one of them a JSON document — are
/// never read from the database at all.
/// </remarks>
public readonly record struct ChronicleLine(
    int Id,
    string PublicId,
    string Kind,
    string? WorkItemId,
    string? AttemptId,
    ActorType ActorType,
    string? ActorId);

/// <summary>
/// Why a manager is being summoned, for the record: which kind of line, which line, about which work — and how
/// many lines qualified in the same read. Identifiers and a number only. What happened is in the chronicle, and
/// the manager reads the chronicle; nothing of it is copied here, where the dispatcher would have had to read it.
/// </summary>
/// <remarks>
/// <see cref="DecisionId"/> is filled by the caller and never by the rule below, because it is not on the line:
/// a decision remembers the chronicle line its answer wrote, so the summon resolves it with one lookup by an
/// identifier it already holds rather than by reading what any line says.
/// </remarks>
public readonly record struct ManagerCause(
    string Kind, string JournalEntryId, string? WorkItemId, string? AttemptId, string? DecisionId, int QualifyingCount);

/// <summary>What one read of a campaign's chronicle decided: a cause, or none, and where the watermark stands now.</summary>
public readonly record struct TriggerRead(ManagerCause? Cause, int Watermark);

/// <summary>
/// Whether a campaign's chronicle, read above its watermark, summons a manager. Pure — the caller reads the
/// entries and says which of them are a check-in's own; this decides, so the rule can be read in one place.
/// <para>
/// The dispatcher must not read meaning, and this is dispatcher code: it reads an entry's kind, its identifiers
/// and its actor, and never <c>Old</c>, <c>New</c>, <c>Key</c> or <c>Reason</c>. A kind is enough to know that
/// something happened; what happened is the manager's to read.
/// </para>
/// </summary>
public static class ManagerTriggers
{
    /// <summary>
    /// How strongly a kind claims to be the cause of a review its read summons, lowest first. A campaign going live is
    /// what the review that follows is for, so <c>campaign_started</c> ranks first; a thread that moved is what a
    /// person is waiting on, so <c>inbound_reply_observed</c> ranks next; every other kind ranks the same. It reads
    /// the kind and nothing else, and it changes only which line is named: what is counted, and how far the read
    /// accounts for, stay as they were.
    /// </summary>
    public static int Rank(string kind) => kind switch
    {
        JournalKinds.CampaignStarted => 0,
        JournalKinds.InboundReplyObserved => 1,
        _ => 2,
    };

    /// <param name="entries">The chronicle above the watermark, oldest first, already capped by the caller.</param>
    /// <param name="triggers">The kinds that summon a review. Empty is a deliberate configuration: cadence only.</param>
    /// <param name="fromCheckIn">
    /// Whether an entry is a check-in's own — about a check-in work item, or written by a check-in's attempt.
    /// Such an entry is neither a cause nor counted, because a manager whose host is missing fails at pre-flight
    /// like any other item, and a failure that could summon a manager would summon its own successor for ever.
    /// </param>
    /// <param name="watermark">How far the chronicle had been accounted for before this read.</param>
    public static TriggerRead Read(
        IReadOnlyList<ChronicleLine> entries,
        IReadOnlySet<string> triggers,
        Func<ChronicleLine, bool> fromCheckIn,
        int watermark)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(triggers);
        ArgumentNullException.ThrowIfNull(fromCheckIn);

        if (entries.Count == 0)
        {
            return new TriggerRead(null, watermark);
        }

        // The cause is the first line of the highest-ranked kind the read holds: a strictly better rank replaces it,
        // an equal one never does, so within a rank the first line wins as it always has.
        ChronicleLine? first = null;
        var rank = int.MaxValue;
        var qualifying = 0;
        foreach (var entry in entries)
        {
            if (!triggers.Contains(entry.Kind) || fromCheckIn(entry))
            {
                continue;
            }

            var ranked = Rank(entry.Kind);
            if (ranked < rank)
            {
                first = entry;
                rank = ranked;
            }

            qualifying++;
        }

        // The watermark is the last entry read, not the cause. The manager about to be launched reads the whole
        // chronicle anyway, so everything in this read is accounted for by the check-in this read creates: ten
        // failures in a burst are one launch, not ten, and only what arrives after this read waits for the next.
        // It also passes over what was excluded, or the same check-in lines would be read again on every scan.
        var advanced = Math.Max(watermark, entries[^1].Id);

        return first is not { } cause
            ? new TriggerRead(null, advanced)
            : new TriggerRead(new ManagerCause(cause.Kind, cause.PublicId, cause.WorkItemId, cause.AttemptId, DecisionId: null, qualifying), advanced);
    }
}
