using System.Globalization;
using System.Text.RegularExpressions;
using Jason.Contracts.Api;

namespace Jason.Cli.Human;

/// <summary>
/// <c>--human</c> rendering for the decision verbs. Every renderer returns null when the body is not what it
/// expects, and the runner falls back to printing the response as it came.
/// </summary>
public static partial class DecisionRenderers
{
    private const int Label = 15;

    /// <summary>
    /// How much of a question a row shows. A question may run to two thousand characters and a table row cannot;
    /// the whole of it is in the JSON and in <c>decision get</c>, and a row is for telling questions apart.
    /// </summary>
    private const int QuestionShown = 60;

    /// <summary>What is waiting, as a table: when it was asked, of which campaign's work, and the question itself last, cut to fit.</summary>
    public static string? DecisionList(string json)
    {
        var page = RenderText.Read<Page<DecisionSummaryDto>>(json);
        if (page?.Items is null)
        {
            return null;
        }

        var table = new HumanTable("RAISED", "STATUS", "CAMPAIGN", "WORK ITEM", "ID", "QUESTION");
        foreach (var decision in page.Items)
        {
            table.Row(
                RenderText.Moment(decision.RaisedAt),
                RenderText.Snake(decision.Status),
                decision.CampaignId,
                decision.WorkItemId,
                decision.Id,
                Excerpt(decision.Question));
        }

        return RenderText.WithCursor(table.Render(), page.NextCursor);
    }

    /// <summary>
    /// One question, in the order a person arrives at an answer: the question whole, then the answers the asker
    /// named, then what to read first, then where the question stands — and the identifiers last, as everywhere
    /// else. The two tables sit between the question and the rest because that is where a reader wants them,
    /// and a table does not fit in a label's column.
    /// </summary>
    public static string? Decision(string json)
    {
        var decision = RenderText.Read<DecisionDto>(json);
        if (decision?.Id is null || decision.Question is null)
        {
            return null;
        }

        var lines = new List<string> { Line("Question:", decision.Question) };
        lines.AddRange(Options(decision.Options));
        lines.AddRange(References(decision.References));
        if (lines.Count > 1)
        {
            lines.Add(string.Empty);
        }

        lines.AddRange(
        [
            Line("Status:", RenderText.Snake(decision.Status)),
            Line("Answer:", decision.Answer),
            Line("Chosen:", decision.ChosenOption),
            Line("Answered by:", RenderText.Actor(decision.AnsweredBy)),
            Line("Answered:", RenderText.Moment(decision.AnsweredAt)),
            Line("Campaign:", decision.CampaignId),
            Line("Work item:", decision.WorkItemId),
            Line("Asked by:", decision.AttemptId),
            Line("Raised:", RenderText.Moment(decision.RaisedAt)),
            Line("Id:", decision.Id),
        ]);

        return RenderText.Lines(lines);
    }

    /// <summary>The answers the asker named, as a block only when there are any: most questions are answered in words.</summary>
    private static IReadOnlyList<string> Options(IReadOnlyList<DecisionOption>? options)
    {
        if (options is not { Count: > 0 })
        {
            return [];
        }

        var table = new HumanTable("LABEL", "DETAIL");
        foreach (var option in options)
        {
            table.Row(option.Label, option.Detail);
        }

        return [string.Empty, "OPTIONS", table.Render()];
    }

    /// <summary>What to read before deciding, by kind and id — the rows themselves are read through their own verbs.</summary>
    private static IReadOnlyList<string> References(IReadOnlyList<DecisionReference>? references)
    {
        if (references is not { Count: > 0 })
        {
            return [];
        }

        var table = new HumanTable("KIND", "ID");
        foreach (var reference in references)
        {
            // A stored reference always says what it points at — the runtime refuses one that does not — so a
            // kind missing here is a row written by something older than that rule, not a thing to guess at.
            table.Row(reference.Kind is { } kind ? RenderText.Snake(kind) : "unknown", reference.Id);
        }

        return [string.Empty, "REFERENCES", table.Render()];
    }

    /// <summary>
    /// The head of a question on one line: line breaks folded into spaces so a row stays a row, and an ellipsis
    /// where the rest was, so nobody mistakes the excerpt for the question.
    /// </summary>
    private static string? Excerpt(string? question)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return null;
        }

        var folded = Whitespace().Replace(question, " ").Trim();
        return folded.Length <= QuestionShown ? folded : Head(folded, QuestionShown).TrimEnd() + " …";
    }

    /// <summary>
    /// The head of the text, at most <paramref name="units"/> UTF-16 units long and cut only where one text
    /// element ends and the next begins.
    /// </summary>
    /// <remarks>
    /// Cutting at an index would take half of anything that is not one unit wide: an astral character is two,
    /// and the half left behind is a lone surrogate — not text, and drawn as a replacement box by whatever
    /// prints it. Text elements rather than runes, because an emoji with a modifier is several runes and one
    /// thing on a terminal row.
    /// </remarks>
    private static string Head(string text, int units)
    {
        var elements = StringInfo.GetTextElementEnumerator(text);
        var taken = 0;
        while (elements.MoveNext())
        {
            var element = (string)elements.Current;
            if (taken + element.Length > units)
            {
                break;
            }

            taken += element.Length;
        }

        return text[..taken];
    }

    private static string Line(string label, string? value) => label.PadRight(Label) + (string.IsNullOrEmpty(value) ? "-" : value);

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
