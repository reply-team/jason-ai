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
    /// A list that names no kind is refused; <see cref="Fill"/> says how a file comes to write one.
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

        // A thread in one of the campaign's conversations moved: somebody may have replied, and reading what they said
        // is the manager's. An installation that narrowed this list before the kind existed is not woken by replies
        // until it adds it.
        JournalKinds.InboundReplyObserved,
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
    /// The defaults apply only where the file does not write the key. An empty array, a <c>null</c>, an empty
    /// object or an empty variable each leave the key in the configuration with nothing under it, and a list
    /// written that way stays empty, for the validator to refuse: whoever wrote it said something about the list
    /// that is not a list, and taking it for the defaults gave them the opposite of a manager woken for nothing,
    /// without a word. The key is matched without regard to case, as the binder matches it. A value of the wrong
    /// type is recorded for the validator to refuse, as it is in every bound section, rather than thrown.
    /// </remarks>
    public static void Fill(IConfigurationSection section, ManagerOptions options)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(options);

        options.Triggers.Clear();

        // No key of this section is one a person chooses, so no plugin's id can be in a path of it.
        SafeBinding.Bind(section, options, SafeBinding.NoInstalledPlugins);
        var written = section.GetChildren().Any(child => string.Equals(child.Key, nameof(Triggers), StringComparison.OrdinalIgnoreCase));
        if (!written)
        {
            options.Triggers = [.. Default];
        }
    }
}
