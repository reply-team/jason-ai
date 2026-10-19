using System.Globalization;
using Jason.Contracts.Api;

namespace Jason.Cli.Human;

/// <summary>
/// <c>--human</c> rendering for the events verbs. Every renderer returns null when the body is not what it expects, and
/// the runner falls back to printing the response as it came.
/// </summary>
public static class EventsRenderers
{
    private const int Label = 12;

    /// <summary>
    /// The accounts in one table — whether each is read, how its last read went and when the next is due, and the latest
    /// span nobody will read — then the spans asked for again of each account that has any, newest first.
    /// </summary>
    public static string? Sources(string json)
    {
        var page = RenderText.Read<Page<EventSourceDto>>(json);
        if (page?.Items is null)
        {
            return null;
        }

        if (page.Items.Count == 0)
        {
            return RenderText.WithCursor("no accounts: no route binds a plugin that reads events", page.NextCursor);
        }

        var table = new HumanTable("ID", "PLUGIN", "BOUND", "HEALTHY", "LAST READ", "FAILURES", "CODE", "NEXT READ", "NOT READ");
        foreach (var source in page.Items)
        {
            table.Row(
                source.Id,
                source.Plugin,
                source.Bound ? "yes" : "no",
                source.Healthy ? "yes" : "no",
                RenderText.Moment(source.ReadStartedAt),
                source.Failures.ToString(CultureInfo.InvariantCulture),
                source.LastCode,
                RenderText.Moment(source.NextReadAt),
                source.LatestGap is { } gap ? $"{RenderText.Moment(gap.From)} to {RenderText.Moment(gap.To)}" : null);
        }

        var lines = new List<string> { table.Render() };
        foreach (var source in page.Items.Where(source => source.Backfills is { Count: > 0 }))
        {
            lines.Add(string.Empty);
            lines.Add($"read again, {source.Id}:");
            lines.AddRange(source.Backfills.Select(Span));
        }

        return RenderText.WithCursor(RenderText.Lines(lines), page.NextCursor);
    }

    /// <summary>One span asked for again.</summary>
    public static string? Backfill(string json)
    {
        var backfill = RenderText.Read<EventBackfillDto>(json);
        if (backfill?.Id is null || backfill.Source is null)
        {
            return null;
        }

        return RenderText.Lines(
        [
            Line("Id:", backfill.Id),
            Line("Account:", backfill.Source),
            Line("From:", RenderText.Moment(backfill.From)),
            Line("To:", RenderText.Moment(backfill.To)),
            Line("Status:", RenderText.Snake(backfill.Status)),
            Line("Asked:", $"{RenderText.Moment(backfill.RequestedAt)} by {RenderText.Actor(backfill.Actor)}"),
            Line("Reason:", backfill.Reason),
            Line("Stored:", string.Create(CultureInfo.InvariantCulture, $"{backfill.Stored} new, {backfill.Duplicates} already held")),
            Line("Ended:", RenderText.Moment(backfill.EndedAt)),
            Line("Code:", backfill.Code is null ? null : $"{backfill.Code} ({backfill.Class})"),
        ]);
    }

    /// <summary>The receipt of a signal: when it came, and when the account is read next.</summary>
    public static string? Signal(string json)
    {
        var receipt = RenderText.Read<EventsSignalResponse>(json);
        if (receipt is null || receipt.SignaledAt == default)
        {
            return null;
        }

        return RenderText.Lines(
        [
            Line("Signalled:", RenderText.Moment(receipt.SignaledAt)),
            Line("Next read:", RenderText.Moment(receipt.NextReadAt)),
        ]);
    }

    private static string Span(EventBackfillDto backfill)
    {
        var how = backfill.Code is null ? string.Empty : $", {backfill.Code} ({backfill.Class})";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"  {backfill.Id}  {RenderText.Moment(backfill.From)} to {RenderText.Moment(backfill.To)}  {RenderText.Snake(backfill.Status)}, {backfill.Stored} new, {backfill.Duplicates} already held{how}");
    }

    private static string Line(string label, string? value) => label.PadRight(Label) + (string.IsNullOrEmpty(value) ? "-" : value);
}
