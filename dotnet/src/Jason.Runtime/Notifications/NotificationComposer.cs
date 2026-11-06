using System.Globalization;
using Jason.Contracts.Ids;
using Jason.Runtime.Approvals;
using Jason.Runtime.Decisions;

namespace Jason.Runtime.Notifications;

/// <summary>
/// What a notification says, made from six counts and, for failed protective work, at most three campaign public ids
/// — opaque ids that name nothing by themselves — and nothing else. That is half of why nothing about the work — a
/// campaign's name, a contact, a subject, a question's words, an error — can reach a channel: this signature has
/// nowhere to take it from.
/// </summary>
public static class NotificationComposer
{
    public const string Title = "Jason";

    /// <summary>How many campaigns a pointer names where protective work failed; the rest are counted.</summary>
    public const int MaxCampaignsNamed = 3;

    /// <summary>The largest count a pointer says as a figure.</summary>
    public const int MaxSaid = 9_999;

    /// <summary>What a pointer says for a count past <see cref="MaxSaid"/>: four characters, as the longest figure is.</summary>
    public const string PastFourDigits = "10k+";

    /// <summary>Where to look. Plain text: a toast shows backticks as backticks.</summary>
    public const string Look = "Ask your agent what Jason is waiting for, or run jason approval list and jason decision list.";

    /// <summary>Where to look when protective work failed and nothing waits.</summary>
    public const string LookAtFailures = "Ask your agent what failed, or run jason workitem list --status failed --status expired.";

    /// <summary>Where to look when protective work failed and something waits as well.</summary>
    public const string LookAtBoth =
        "Ask your agent what failed and what Jason is waiting for, or run jason workitem list --status failed --status expired, jason approval list and jason decision list.";

    /// <summary>Where to look when an account's events were not read and nothing else is said.</summary>
    public const string LookAtGaps = "Ask your agent what Jason did not read, or run " + LookAtGapsCommand + ".";

    /// <summary>The command that shows the accounts whose events were not read, and the spans of them nobody will read.</summary>
    public const string LookAtGapsCommand = "jason events sources";

    /// <summary>Where to look when a provider block holds work and nothing else is said.</summary>
    public const string LookAtBlocks = "Ask your agent what is blocked, or run " + LookAtBlocksCommand + ".";

    /// <summary>The command that shows the provider blocks, the work each holds and where a person clears it.</summary>
    public const string LookAtBlocksCommand = "jason block list";

    /// <summary>Where to look when items wait on a daily limit and nothing else is said.</summary>
    public const string LookAtHeld = "Ask your agent what is held, or run " + LookAtHeldCommand + ".";

    /// <summary>The command that shows the items that wait, the held ones among them.</summary>
    public const string LookAtHeldCommand = "jason workitem list --status waiting";

    /// <summary>Where to look when protective work failed, items wait on a daily limit and nothing else is said.</summary>
    public const string LookAtFailedAndHeld = "Ask your agent what failed and what is held, or run " + LookAtFailedAndHeldCommand + ".";

    /// <summary>The one command that shows failed protective work and held items together.</summary>
    public const string LookAtFailedAndHeldCommand = "jason workitem list --status failed --status expired --status waiting";

    /// <param name="item">
    /// The public id of the one approval or question that waits, read beside the counts; carried only when it is the
    /// one thing that waits for a person, and only by the Windows toast.
    /// </param>
    /// <param name="eventGaps">How many accounts have a span of events nobody will read, said since the last pointer that reached anybody.</param>
    /// <param name="blocks">The provider blocks opened since the last pointer that reached anybody and open still, and the work items they hold.</param>
    /// <param name="held">How many items wait, as this is sent, on a daily limit of launches.</param>
    /// <exception cref="ArgumentOutOfRangeException">A count is negative, or every count is zero: nothing to say is never composed.</exception>
    public static NotificationMessage Compose(int approvals, int decisions, FailedProtectiveWork? failed = null, int eventGaps = 0, HeldByBlocks? blocks = null, string? item = null, int held = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(approvals);
        ArgumentOutOfRangeException.ThrowIfNegative(decisions);
        var failures = failed?.Count ?? 0;
        ArgumentOutOfRangeException.ThrowIfNegative(failures);
        ArgumentOutOfRangeException.ThrowIfNegative(eventGaps);
        var blocked = blocks?.Blocks ?? 0;
        ArgumentOutOfRangeException.ThrowIfNegative(blocked);
        ArgumentOutOfRangeException.ThrowIfNegative(blocks?.WorkItems ?? 0);
        ArgumentOutOfRangeException.ThrowIfNegative(held);
        if (approvals == 0 && decisions == 0 && failures == 0 && eventGaps == 0 && blocked == 0 && held == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(approvals), "Nothing is waiting, so there is nothing to say.");
        }

        var sentences = new List<string>(5);
        if (approvals > 0 || decisions > 0)
        {
            var parts = new List<string>(2);
            if (approvals > 0)
            {
                parts.Add(Count(approvals, "approval", "approvals"));
            }

            if (decisions > 0)
            {
                parts.Add(Count(decisions, "question", "questions"));
            }

            var verb = (long)approvals + decisions == 1 ? "is" : "are";
            sentences.Add($"{string.Join(" and ", parts)} {verb} waiting in Jason.");
        }

        // Beside a block or a hold, failed protective work names one campaign and counts the rest, and beside both it names
        // none: every thing said, with three campaigns named, would not fit the message the delivery contract takes.
        if (failures > 0)
        {
            sentences.Add(Failed(failed!, held == 0 ? (blocked > 0 ? 1 : MaxCampaignsNamed) : (blocked > 0 ? 0 : 1)));
        }

        if (held > 0)
        {
            sentences.Add($"{Count(held, "item", "items")} {(held == 1 ? "waits" : "wait")} on a budget.");
        }

        if (blocked > 0)
        {
            var alone = sentences.Count == 0 && eventGaps == 0;
            var verb = blocked == 1 ? "holds" : "hold";
            sentences.Add($"{Count(blocked, "provider block", "provider blocks")} {verb} {Count(blocks!.WorkItems, "work item", "work items")}{(alone ? " in Jason" : string.Empty)}.");
        }

        if (eventGaps > 0)
        {
            sentences.Add($"{Count(eventGaps, "account has", "accounts have")} events Jason did not read.");
        }

        // Either alone, never their sum: two counts near the largest an int holds would add up to less than nothing.
        var waiting = approvals > 0 || decisions > 0;
        var look = eventGaps > 0 || blocked > 0 || held > 0 ? Pointing(waiting, failures > 0, held > 0, blocked > 0, eventGaps > 0)
            : failures == 0 ? Look
            : waiting ? LookAtBoth
            : LookAtFailures;
        return new NotificationMessage(Title, string.Join(" ", sentences), look, approvals, decisions, failures, eventGaps, blocked, One(approvals, decisions, item), held);
    }

    /// <summary>
    /// The one item that waits, by its opaque public id: only when exactly one approval or one question waits, and only
    /// an id of that kind's prefix. Anything else is no item, never an error: a click then opens the list.
    /// </summary>
    private static string? One(int approvals, int decisions, string? item) =>
        (approvals, decisions) switch
        {
            (1, 0) when PublicId.IsWellFormed(ApprovalGate.IdPrefix, item) => item,
            (0, 1) when PublicId.IsWellFormed(DecisionService.IdPrefix, item) => item,
            _ => null,
        };

    /// <summary>
    /// Where to look when items are held, a provider block holds work or an account's events were not read: alone, the agent and the one
    /// command that shows it; beside other things, the agent and the command of each thing, in the order they were said,
    /// without the questions each would ask — six things said in full would not fit the message the delivery contract
    /// takes.
    /// </summary>
    private static string Pointing(bool waiting, bool failed, bool held, bool blocked, bool gaps)
    {
        var commands = new List<string>(5);
        if (waiting)
        {
            commands.Add("jason approval list");
            commands.Add("jason decision list");
        }

        if (failed)
        {
            commands.Add(held ? LookAtFailedAndHeldCommand : "jason workitem list --status failed --status expired");
        }
        else if (held)
        {
            commands.Add(LookAtHeldCommand);
        }

        if (blocked)
        {
            commands.Add(LookAtBlocksCommand);
        }

        if (gaps)
        {
            commands.Add(LookAtGapsCommand);
        }

        if (commands.Count == 1)
        {
            // Failed protective work comes here only beside a hold: alone it has its own words.
            return failed ? LookAtFailedAndHeld : held ? LookAtHeld : blocked ? LookAtBlocks : LookAtGaps;
        }

        return $"Ask your agent, or run {string.Join(", ", commands.Take(commands.Count - 1))} and {commands[^1]}.";
    }

    /// <summary>"2 protective actions failed in Jason, in cmp_… and cmp_…." — the campaigns by public id, <paramref name="most"/> at most, and with none to name, none counted.</summary>
    private static string Failed(FailedProtectiveWork failed, int most)
    {
        var named = failed.Campaigns.Take(most).ToList();
        // As a long: the campaigns counted past those named, near the largest count an int holds, plus the ones left
        // unnamed beside a block, would add up to less than nothing.
        var more = (long)failed.MoreCampaigns + (failed.Campaigns.Count - named.Count);
        var places = most == 0 ? null
            : more > 0 ? $"{string.Join(", ", named)} and {Figure(more)} more"
            : named.Count == 0 ? null
            : named.Count == 1 ? named[0]
            : $"{string.Join(", ", named.Take(named.Count - 1))} and {named[^1]}";
        var count = Count(failed.Count, "protective action", "protective actions");
        return places is null ? $"{count} failed in Jason." : $"{count} failed in Jason, in {places}.";
    }

    private static string Count(int count, string one, string many) => $"{Figure(count)} {(count == 1 ? one : many)}";

    /// <summary>
    /// A count as a pointer says it: as it is up to four digits, and past them as <see cref="PastFourDigits"/>, so that no
    /// count is ever said in more than four characters and the longest pointer fits the delivery contract whatever the
    /// counts are. A toast needs the size of it; the figure is where the pointer sends the person.
    /// </summary>
    private static string Figure(long count) =>
        count > MaxSaid ? PastFourDigits : count.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// One pointer: a title, a summary of the counts, and where to look. A toast shows the title, then the summary and
/// where to look as two lines; a channel that carries one line sends <see cref="Text"/>.
/// </summary>
public sealed record NotificationMessage(string Title, string Summary, string Look, int Approvals, int Decisions, int ProtectiveFailures = 0, int EventGaps = 0, int Blocks = 0, string? Item = null, int Held = 0)
{
    public string Text => $"{Summary} {Look}";
}

/// <summary>
/// Protective work that failed since the last pointer that reached anybody: how many items, the campaigns they were in
/// by public id in the order they first failed, and how many more campaigns there were past those named.
/// </summary>
public sealed record FailedProtectiveWork(int Count, IReadOnlyList<string> Campaigns, int MoreCampaigns);

/// <summary>
/// Provider blocks opened since the last pointer that reached anybody and open still: how many, and how many work items
/// their open waits hold. Nothing of a block travels but the two counts — no link, reason, sender, plugin or campaign.
/// </summary>
public sealed record HeldByBlocks(int Blocks, int WorkItems);
