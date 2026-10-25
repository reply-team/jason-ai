using System.Globalization;
using Jason.Contracts.Api;

namespace Jason.Cli.Human;

/// <summary>
/// <c>--human</c> rendering for the block verbs. Every renderer returns null when the body is not what it expects, and
/// the runner falls back to printing the response as it came.
/// </summary>
public static class BlockRenderers
{
    private const int Label = 15;

    /// <summary>The blocks as a table: when each opened, what it blocks, why, and how much it holds.</summary>
    public static string? BlockList(string json)
    {
        var page = RenderText.Read<Page<ProviderBlockDto>>(json);
        if (page?.Items is null)
        {
            return null;
        }

        var table = new HumanTable("OPENED", "BLOCKED", "REASON", "STATUS", "HELD", "PLUGIN", "ID");
        foreach (var block in page.Items)
        {
            table.Row(
                RenderText.Moment(block.OpenedAt),
                Blocked(block),
                block.Reason,
                RenderText.Snake(block.Status),
                block.HeldCount.ToString(CultureInfo.InvariantCulture),
                block.PluginId,
                block.Id);
        }

        return RenderText.WithCursor(table.Render(), page.NextCursor);
    }

    /// <summary>
    /// One block in the order a person clears it: what is blocked, why, and where it is cleared first; then whether it
    /// clears by itself, how long and how often it has held, what it holds and its next trial; identifiers last. The
    /// clear's lines appear only once there is a clear to speak of.
    /// </summary>
    public static string? Block(string json)
    {
        var block = RenderText.Read<ProviderBlockDto>(json);
        if (block?.Id is null || block.PluginId is null)
        {
            return null;
        }

        var lines = new List<string>
        {
            Line("Blocked:", Blocked(block)),
            Line("Reason:", block.Reason),
            Line("Link:", block.Link),
            Line("Clears at:", RenderText.Moment(block.ClearsAt)),
            Line("Status:", RenderText.Snake(block.Status)),
            Line("Opened:", RenderText.Moment(block.OpenedAt)),
            Line("Last seen:", RenderText.Moment(block.LastSeenAt)),
            Line("Seen:", Times(block.SeenCount)),
            Line("Holds:", Items(block.HeldCount)),
        };

        if (block.Status == ProviderBlockStatus.Open)
        {
            lines.Add(Line("Next trial:", RenderText.Moment(block.NextTrialAt)));
            lines.Add(Line("Trial item:", block.TrialWorkItemId));
        }
        else
        {
            lines.Add(Line("Cleared:", RenderText.Moment(block.ClearedAt)));
            lines.Add(Line("Cleared by:", ClearedBy(block)));
        }

        lines.Add(Line("Plugin:", block.PluginId));
        lines.Add(Line("Binding:", block.BindingIdentity));
        lines.Add(Line("Opened by:", block.OpenedByAttemptId));
        lines.Add(Line("Id:", block.Id));
        return RenderText.Lines(lines);
    }

    private static string Blocked(ProviderBlockDto block) =>
        block.Scope == ProviderBlockScope.Sender ? $"sender {block.SenderExternalId}" : "the account";

    private static string? ClearedBy(ProviderBlockDto block) =>
        block.ClearedByActor is { } actor ? RenderText.Actor(actor)
        : block.ClearedByAttemptId is { } attempt ? $"attempt {attempt}"
        : null;

    private static string Times(int count) => count == 1 ? "once" : $"{count.ToString(CultureInfo.InvariantCulture)} times";

    private static string Items(int count) => count == 1 ? "1 item" : $"{count.ToString(CultureInfo.InvariantCulture)} items";

    private static string Line(string label, string? value) => label.PadRight(Label) + (string.IsNullOrEmpty(value) ? "-" : value);
}
