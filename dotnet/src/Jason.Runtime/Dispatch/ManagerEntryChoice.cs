using Jason.Runtime.Journal;

namespace Jason.Runtime.Dispatch;

/// <summary>
/// What the summon knows when it chooses a check-in's entry: the kind of the line that caused the review, whether that
/// line is the campaign's first <c>campaign_started</c> line, and whether the scheduled review is due. A kind, the
/// answer to one comparison of identifiers, and a clock's answer — nowhere to put what any line says.
/// </summary>
/// <param name="CauseKind">The kind of the cause line, or null where nothing in the chronicle caused the review.</param>
/// <param name="FirstStart">
/// Whether the cause line is the campaign's first <c>campaign_started</c> line by journal id. It means nothing for a
/// cause of any other kind.
/// </param>
/// <param name="ReviewDue">Whether the campaign's scheduled review is due.</param>
public readonly record struct ManagerEntryInputs(string? CauseKind, bool FirstStart, bool ReviewDue);

/// <summary>
/// Which of the manager's entries a check-in is created with, from what caused it. Pure: a table over a kind and two
/// booleans, so the dispatcher still reads no meaning — it knows a campaign went live, not what it went live for.
/// <c>docs/campaign-manager.md</c> §5 prints the table, and a test holds the page to this function row for row.
/// </summary>
/// <remarks>
/// A cause wins over the cadence: a review with a cause is a review of that cause, whether or not the clock was due
/// as well. A cause of a kind the table does not name is the general text, which is no entry at all.
/// </remarks>
public static class ManagerEntryChoice
{
    /// <summary>The campaign's first going live.</summary>
    public const string Start = "start";

    /// <summary>The campaign going live again, after a pause.</summary>
    public const string Resume = "resume";

    /// <summary>A thread in one of the campaign's conversations moved.</summary>
    public const string InboundReply = "inbound_reply";

    /// <summary>The cadence came round with nothing in the chronicle to cause a review.</summary>
    public const string QuickLook = "quick_look";

    /// <returns>The entry's name, one of the manager's in <c>RoleEntries</c>, or null for the general text.</returns>
    public static string? Choose(ManagerEntryInputs inputs) => inputs.CauseKind switch
    {
        JournalKinds.CampaignStarted => inputs.FirstStart ? Start : Resume,
        JournalKinds.InboundReplyObserved => InboundReply,
        null => inputs.ReviewDue ? QuickLook : null,
        _ => null,
    };
}
