using Jason.Contracts.Api;

namespace Jason.Cli.Human;

/// <summary>
/// <c>--human</c> rendering for the suppression verbs. A renderer returns null when the body is not what it
/// expects, and the runner falls back to printing the response as it came.
/// </summary>
public static class SuppressionRenderers
{
    private const int Label = 10;

    /// <summary>One do-not-contact entry.</summary>
    public static string? Suppression(string json)
    {
        var suppression = RenderText.Read<SuppressionDto>(json);
        if (suppression?.Id is null || suppression.Channel is null || suppression.Value is null)
        {
            return null;
        }

        return RenderText.Lines(
        [
            Line("Id:", suppression.Id),
            Line("Channel:", suppression.Channel),
            Line("Value:", suppression.Value),
            Line("Reason:", suppression.Reason),
            Line("Created:", RenderText.Moment(suppression.CreatedAt)),
        ]);
    }

    /// <summary>Whether the removal found anything: removing an entry that is not there is not an error.</summary>
    public static string? Removed(string json)
    {
        var removal = RenderText.Read<SuppressionRemovedDto>(json);
        if (removal?.Channel is null || removal.Value is null)
        {
            return null;
        }

        return $"{removal.Channel} {removal.Value}: {(removal.Removed ? "removed" : "not suppressed")}";
    }

    /// <summary>A page of do-not-contact entries.</summary>
    public static string? SuppressionList(string json)
    {
        var page = RenderText.Read<Page<SuppressionDto>>(json);
        if (page?.Items is null)
        {
            return null;
        }

        var table = new HumanTable("ID", "CHANNEL", "VALUE", "REASON", "CREATED");
        foreach (var suppression in page.Items)
        {
            table.Row(suppression.Id, suppression.Channel, suppression.Value, suppression.Reason, RenderText.Moment(suppression.CreatedAt));
        }

        return RenderText.WithCursor(table.Render(), page.NextCursor);
    }

    private static string Line(string label, string? value) => label.PadRight(Label) + (string.IsNullOrEmpty(value) ? "-" : value);
}
