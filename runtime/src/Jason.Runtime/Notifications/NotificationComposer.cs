using System.Globalization;

namespace Jason.Runtime.Notifications;

/// <summary>
/// What a notification says, made from two integers and nothing else. That is half of why nothing about the work —
/// a campaign, a contact, a subject, a question's words — can reach a channel: this signature has nowhere to take
/// it from.
/// </summary>
public static class NotificationComposer
{
    public const string Title = "Jason";

    /// <summary>Where to look. Plain text: a toast shows backticks as backticks.</summary>
    public const string Look = "Ask your agent what Jason is waiting for, or run jason approval list and jason decision list.";

    /// <exception cref="ArgumentOutOfRangeException">A count is negative, or both are zero: nothing waiting is never composed.</exception>
    public static NotificationMessage Compose(int approvals, int decisions)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(approvals);
        ArgumentOutOfRangeException.ThrowIfNegative(decisions);
        if (approvals == 0 && decisions == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(approvals), "Nothing is waiting, so there is nothing to say.");
        }

        var parts = new List<string>(2);
        if (approvals > 0)
        {
            parts.Add(Count(approvals, "approval", "approvals"));
        }

        if (decisions > 0)
        {
            parts.Add(Count(decisions, "question", "questions"));
        }

        var verb = approvals + decisions == 1 ? "is" : "are";
        var summary = $"{string.Join(" and ", parts)} {verb} waiting in Jason.";
        return new NotificationMessage(Title, summary, Look, approvals, decisions);
    }

    private static string Count(int count, string one, string many) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} {(count == 1 ? one : many)}");
}

/// <summary>
/// One pointer: a title, a summary of the counts, and where to look. A toast shows the title, then the summary and
/// where to look as two lines; a channel that carries one line sends <see cref="Text"/>.
/// </summary>
public sealed record NotificationMessage(string Title, string Summary, string Look, int Approvals, int Decisions)
{
    public string Text => $"{Summary} {Look}";
}
