using System.Text;
using Jason.Contracts.Api;

namespace Jason.Cli.Human;

/// <summary>
/// <c>--human</c> rendering for the report verbs. Every renderer returns null when the body is not what it
/// expects, and the runner falls back to printing the response as it came.
/// </summary>
public static class ReportRenderers
{
    /// <summary>
    /// The one sentence that has to survive every rendering of a report. A reader glancing at a campaign must
    /// never take one of these for something Jason did: it is somebody's account of something that happened
    /// where Jason could not see it, and the runtime has checked none of it.
    /// </summary>
    public const string Heading = "Reported from outside Jason — the reporter's word, not verified.";

    private const int Label = 15;

    public static string? ReportList(string json)
    {
        var page = RenderText.Read<Page<ReportSummaryDto>>(json);
        if (page?.Items is null)
        {
            return null;
        }

        var table = new HumanTable("RECEIVED", "EFFECT", "REPORTER", "TOOL", "OPERATION", "ID");
        foreach (var report in page.Items)
        {
            table.Row(
                RenderText.Moment(report.ReceivedAt),
                report.Effect,
                RenderText.Actor(report.Reporter),
                report.Tool,
                Operation(report.Correlation),
                report.Id);
        }

        return Heading + Environment.NewLine + Environment.NewLine + RenderText.WithCursor(table.Render(), page.NextCursor);
    }

    /// <summary>
    /// One report, with what was claimed first and the identifiers afterwards — the order somebody reads it in
    /// when they are trying to work out what happened to a person.
    /// </summary>
    public static string? Report(string json)
    {
        var report = RenderText.Read<ReportDto>(json);
        if (report?.Id is null || report.Correlation is null)
        {
            return null;
        }

        var text = new StringBuilder();
        text.AppendLine(Heading);
        text.AppendLine();
        text.AppendLine(Line("Effect:", report.Effect()));
        text.AppendLine(Line("Summary:", report.Summary()));
        text.AppendLine(Line("Reporter:", RenderText.Actor(report.Reporter)));
        text.AppendLine(Line("Tool:", report.Tool()));
        text.AppendLine(Line("Account:", report.Account()));
        text.AppendLine(Line("Happened:", report.Text("occurred_at")));
        text.AppendLine(Line("Observed:", report.Text("observed_at")));
        text.AppendLine(Line("Received:", RenderText.Moment(report.ReceivedAt)));
        text.AppendLine(Line("Campaign:", report.Correlation.CampaignId));
        text.AppendLine(Line("Contact:", Contact(report.Correlation)));
        text.AppendLine(Line("Work item:", report.Correlation.WorkItemId));
        text.AppendLine(Line("Operation:", Operation(report.Correlation)));
        text.AppendLine(Line("Not known:", report.List("unknown_fields")));
        text.AppendLine(Line("Unsure of:", report.Text("uncertainty")));
        text.AppendLine(Line("Evidence:", RenderText.Compact(report.Assertion?["evidence"])));
        text.AppendLine(Line("Admitted:", RenderText.Snake(report.Dedup?.Outcome ?? ReportDedupOutcome.Admitted)));
        text.Append(Line("Id:", report.Id));
        return text.ToString();
    }

    /// <summary>An operation the catalog does not publish is still shown — it is what the reporter said — with
    /// the fact that nobody here recognises it said beside it rather than left to be discovered.</summary>
    private static string? Operation(ReportCorrelationDto correlation) =>
        correlation.Operation is null
            ? null
            : correlation.Operation + (correlation.OperationKnown ? string.Empty : " (unknown here)");

    private static string? Contact(ReportCorrelationDto correlation) =>
        correlation.ContactId is null
            ? null
            : correlation.ContactId + (correlation.ContactInCampaign == false ? " (not a member of that campaign)" : string.Empty);

    private static string? Effect(this ReportDto report) => report.Text("effect");

    private static string? Tool(this ReportDto report) => report.Text("tool");

    private static string? Account(this ReportDto report) => report.Text("account");

    private static string? Summary(this ReportDto report) => report.Text("summary");

    /// <summary>
    /// Read out of the assertion rather than off a column: what is rendered is what the reporter said, and the
    /// document they said it in is the thing the runtime kept.
    /// </summary>
    private static string? Text(this ReportDto report, string field) => (string?)report.Assertion?[field];

    private static string? List(this ReportDto report, string field) =>
        report.Assertion?[field] is not System.Text.Json.Nodes.JsonArray entries || entries.Count == 0
            ? null
            : string.Join(", ", entries.Select(entry => (string?)entry));

    private static string Line(string label, string? value) => label.PadRight(Label) + (string.IsNullOrEmpty(value) ? "-" : value);
}
