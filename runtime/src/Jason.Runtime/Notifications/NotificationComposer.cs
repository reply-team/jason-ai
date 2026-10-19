using System.Globalization;

namespace Jason.Runtime.Notifications;

/// <summary>
/// What a notification says, made from four counts and, for failed protective work, at most three campaign public ids
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

    /// <param name="eventGaps">How many accounts have a span of events nobody will read, said since the last pointer that reached anybody.</param>
    /// <exception cref="ArgumentOutOfRangeException">A count is negative, or every count is zero: nothing to say is never composed.</exception>
    public static NotificationMessage Compose(int approvals, int decisions, FailedProtectiveWork? failed = null, int eventGaps = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(approvals);
        ArgumentOutOfRangeException.ThrowIfNegative(decisions);
        var failures = failed?.Count ?? 0;
        ArgumentOutOfRangeException.ThrowIfNegative(failures);
        ArgumentOutOfRangeException.ThrowIfNegative(eventGaps);
        if (approvals == 0 && decisions == 0 && failures == 0 && eventGaps == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(approvals), "Nothing is waiting, so there is nothing to say.");
        }

        var sentences = new List<string>(3);
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

        if (failures > 0)
        {
            sentences.Add(Failed(failed!));
        }

        if (eventGaps > 0)
        {
            sentences.Add($"{Count(eventGaps, "account has", "accounts have")} events Jason did not read.");
        }

        // Either alone, never their sum: two counts near the largest an int holds would add up to less than nothing.
        var waiting = approvals > 0 || decisions > 0;
        var look = eventGaps > 0 ? Pointing(waiting, failures > 0)
            : failures == 0 ? Look
            : waiting ? LookAtBoth
            : LookAtFailures;
        return new NotificationMessage(Title, string.Join(" ", sentences), look, approvals, decisions, failures, eventGaps);
    }

    /// <summary>
    /// Where to look when an account's events were not read: alone, the agent and the record of the accounts; beside other
    /// things, the agent and the command of each thing, the record last, without the questions each would ask — four
    /// things said in full would not fit the message the delivery contract takes.
    /// </summary>
    private static string Pointing(bool waiting, bool failed)
    {
        var commands = new List<string>(4);
        if (waiting)
        {
            commands.Add("jason approval list");
            commands.Add("jason decision list");
        }

        if (failed)
        {
            commands.Add("jason workitem list --status failed --status expired");
        }

        if (commands.Count == 0)
        {
            return LookAtGaps;
        }

        commands.Add(LookAtGapsCommand);

        return $"Ask your agent, or run {string.Join(", ", commands.Take(commands.Count - 1))} and {commands[^1]}.";
    }

    /// <summary>"2 protective actions failed in Jason, in cmp_… and cmp_…." — the campaigns by public id, three at most.</summary>
    private static string Failed(FailedProtectiveWork failed)
    {
        var named = failed.Campaigns.Take(MaxCampaignsNamed).ToList();
        var places = failed.MoreCampaigns > 0 ? $"{string.Join(", ", named)} and {Figure(failed.MoreCampaigns)} more"
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
    private static string Figure(int count) =>
        count > MaxSaid ? PastFourDigits : count.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// One pointer: a title, a summary of the counts, and where to look. A toast shows the title, then the summary and
/// where to look as two lines; a channel that carries one line sends <see cref="Text"/>.
/// </summary>
public sealed record NotificationMessage(string Title, string Summary, string Look, int Approvals, int Decisions, int ProtectiveFailures = 0, int EventGaps = 0)
{
    public string Text => $"{Summary} {Look}";
}

/// <summary>
/// Protective work that failed since the last pointer that reached anybody: how many items, the campaigns they were in
/// by public id in the order they first failed, and how many more campaigns there were past those named.
/// </summary>
public sealed record FailedProtectiveWork(int Count, IReadOnlyList<string> Campaigns, int MoreCampaigns);
