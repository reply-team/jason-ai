using Jason.Runtime.Journal;
using Microsoft.Extensions.Configuration;

namespace Jason.Runtime.Configuration;

/// <summary>
/// The campaign-manager loop: which happenings are worth waking a manager for, how often one is woken anyway,
/// and what the check-in it is woken by is allowed to cost.
/// </summary>
/// <remarks>
/// There is no <c>Manager:Enabled</c>. <c>Dispatcher:Enabled</c> already decides whether this runtime has a
/// loop at all, and a second switch would be a second answer to one question — an installation that turned the
/// dispatcher off and the manager on would be describing something that cannot happen.
/// </remarks>
public sealed class ManagerOptions
{
    public const string Section = "Manager";

    /// <summary>
    /// The narrowest and widest cadence an installation may ask for, and the same two numbers a campaign's own
    /// pace is held to. One pair, named once: a campaign that could be reviewed more often than the runtime
    /// allows would be buying launches the operator did not agree to, and two copies of a bound are two numbers
    /// that can drift.
    /// </summary>
    public const int MinimumReviewSeconds = 300;

    public const int MaximumReviewSeconds = 604_800;

    /// <summary>
    /// The chronicle kinds that summon a review. Each must be one the runtime writes itself: a kind a caller
    /// can append is a kind anything could forge, and a rule keyed on one would be a rule anybody could fire.
    /// An empty list is a deliberate configuration in code — review on the cadence and on nothing else —
    /// though the file cannot express one; <see cref="Fill"/> says why.
    /// </summary>
    public List<string> Triggers { get; set; } = [.. Default];

    /// <summary>What an installation reacts to when it has not said otherwise.</summary>
    public static IReadOnlyList<string> Default { get; } =
    [
        JournalKinds.WorkItemFailed,
        JournalKinds.ApprovalRejected,
        JournalKinds.ExternalEffectReported,

        // The one kind whose absence breaks something rather than merely narrowing the loop: a question a
        // person has answered releases nothing unless this is in force, and the role that asked has already
        // ended. An installation that narrows this list has to keep it.
        JournalKinds.DecisionAnswered,
    ];

    /// <summary>
    /// How often an active campaign is reviewed when nothing has happened to it. Five hours by default: a
    /// manager is a person running dailies, not a process watching a queue. A campaign may ask for its own pace
    /// within the same bounds.
    /// </summary>
    public int ReviewSeconds { get; set; } = 18_000;

    /// <summary>
    /// The budget of one check-in. A review reads a campaign and says what it thinks; the hour an
    /// <c>ai_role</c> item gets by default is a budget for doing research, and a review that wanders for an
    /// hour has gone wrong rather than gone deep.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 900;

    /// <summary>
    /// How many attempts a check-in is worth. One: a semantic review that failed has no better second run
    /// waiting, and the cadence comes round anyway. Retrying it would spend an operator's plan on the same
    /// failure three times before anybody was told.
    /// </summary>
    public int MaxAttempts { get; set; } = 1;

    /// <summary>Where a check-in sits in the queue. Ordinary work by default: a review is not an emergency.</summary>
    public int Priority { get; set; }

    /// <summary>
    /// How much chronicle one campaign's summon reads in one scan. A burst — an import that failed two thousand
    /// items — must not hold the scan while it is counted, and what is not read this time is read next time.
    /// </summary>
    public int MaxEntriesPerScan { get; set; } = 500;

    /// <summary>
    /// Read rather than bound, for one reason: the standard binder <em>appends</em> to a list that already has
    /// values, so an operator narrowing the triggers to one kind would have got that kind plus the three
    /// defaults — the opposite of what they wrote, and silently. The list is emptied first, so what is in the
    /// file is what is in force.
    /// </summary>
    /// <remarks>
    /// An empty array in the file cannot be told from an absent key — configuration represents neither — so it
    /// reads as "unset" and the defaults apply. Reviewing on the cadence alone is therefore not something the
    /// file can say; nothing in this version needs it to.
    /// </remarks>
    public static void Fill(IConfiguration section, ManagerOptions options)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(options);

        options.Triggers.Clear();
        section.Bind(options);
        if (options.Triggers.Count == 0)
        {
            options.Triggers = [.. Default];
        }
    }
}
