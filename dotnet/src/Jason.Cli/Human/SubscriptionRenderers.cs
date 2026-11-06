using System.Globalization;
using Jason.Contracts.Api;

namespace Jason.Cli.Human;

/// <summary>
/// <c>--human</c> rendering for the subscription verbs. Every renderer returns null when the body is not what it expects,
/// and the runner falls back to printing the response as it came. An envelope is a plugin's own text: a human view lists
/// its keys and never prints it, and the JSON output carries it whole.
/// </summary>
public static class SubscriptionRenderers
{
    private const int Label = 12;

    /// <summary>The subscribed kinds in one table, the id of a kind's chain in flight under IN FLIGHT.</summary>
    public static string? Kinds(string json)
    {
        var page = RenderText.Read<Page<SubscriptionKindDto>>(json);
        if (page?.Items is null)
        {
            return null;
        }

        if (page.Items.Count == 0)
        {
            return RenderText.WithCursor("no subscribed kinds: no plugin of the active load subscribes to a journal kind", page.NextCursor);
        }

        var table = new HumanTable("KIND", "CHAIN", "CURSOR", "HEAD", "LAG", "WAITING ON", "IN FLIGHT", "RESTS UNTIL");
        foreach (var kind in page.Items)
        {
            table.Row(
                kind.Kind,
                string.Join(", ", kind.Chain),
                kind.Cursor,
                kind.Head,
                kind.Lag.ToString(CultureInfo.InvariantCulture),
                kind.WaitingOn.Count == 0 ? null : string.Join(", ", kind.WaitingOn),
                kind.InFlight?.Run,
                RenderText.Moment(kind.RestUntil));
        }

        return RenderText.WithCursor(table.Render(), page.NextCursor);
    }

    /// <summary>One kind, as a rewind leaves it.</summary>
    public static string? Kind(string json)
    {
        var kind = RenderText.Read<SubscriptionKindDto>(json);
        if (kind?.Kind is null || kind.Chain is null)
        {
            return null;
        }

        return RenderText.Lines(
        [
            Line("Kind:", kind.Kind),
            Line("Chain:", string.Join(", ", kind.Chain)),
            Line("Cursor:", kind.Cursor),
            Line("Head:", kind.Head),
            Line("Lag:", kind.Lag.ToString(CultureInfo.InvariantCulture)),
            Line("Waiting on:", kind.WaitingOn is { Count: > 0 } waiting ? string.Join(", ", waiting) : null),
            Line("In flight:", kind.InFlight is { } run ? $"{run.Run} for {run.Line}, since {RenderText.Moment(run.StartedAt)}" : null),
            Line("Rests until:", RenderText.Moment(kind.RestUntil)),
        ]);
    }

    /// <summary>The records of one kind's chains, the newest first.</summary>
    public static string? Runs(string json)
    {
        var page = RenderText.Read<Page<SubscriptionRunDto>>(json);
        if (page?.Items is null)
        {
            return null;
        }

        if (page.Items.Count == 0)
        {
            return RenderText.WithCursor("no chains have run for this kind", page.NextCursor);
        }

        var table = new HumanTable("ID", "LINE", "STATUS", "STARTED", "ENDED", "HANDLERS", "FAILURE", "ENVELOPE");
        foreach (var run in page.Items)
        {
            table.Row(
                run.Id,
                run.Line,
                RenderText.Snake(run.Status),
                RenderText.Moment(run.StartedAt),
                RenderText.Moment(run.EndedAt),
                (run.Handlers?.Count ?? 0).ToString(CultureInfo.InvariantCulture),
                run.Failure is { } failure ? $"{failure.PluginId}: {failure.Code} ({failure.Class})" : null,
                run.Envelope is null ? null : RenderText.Keys(run.Envelope));
        }

        return RenderText.WithCursor(table.Render(), page.NextCursor);
    }

    private static string Line(string label, string? value) => label.PadRight(Label) + (string.IsNullOrEmpty(value) ? "-" : RenderText.Shown(value));
}
