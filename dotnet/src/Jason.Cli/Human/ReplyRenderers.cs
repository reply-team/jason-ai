using System.Globalization;
using Jason.Contracts.Api;

namespace Jason.Cli.Human;

/// <summary>
/// <c>--human</c> rendering for the reply verbs that read. Every renderer returns null when the body is not what it
/// expects, and the runner falls back to printing the response as it came.
/// </summary>
public static class ReplyRenderers
{
    /// <summary>What a row says in place of a category word where the verdicts name none.</summary>
    public const string NoCategory = "(no category)";

    /// <summary>What a row says in place of a category word where it counts every judged reply of the campaign.</summary>
    public const string AllCategories = "(all)";

    /// <summary>
    /// The judged replies of each campaign, a row per category and one for all of them, each campaign named by its name
    /// from <paramref name="names"/> with its id beside it, and the total across them last.
    /// </summary>
    public static string? Count(string json, IReadOnlyDictionary<string, string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        var counts = RenderText.Read<ReplyCountResponse>(json);
        if (counts?.Campaigns is null || counts.Total?.Categories is null)
        {
            return null;
        }

        var table = new HumanTable("CAMPAIGN", "ID", "CATEGORY", "REPLIES", "THREADS");
        foreach (var row in counts.Campaigns)
        {
            Rows(table, names.GetValueOrDefault(row.CampaignId), row.CampaignId, row.Categories ?? [], row.Replies, row.Threads);
        }

        Rows(table, "TOTAL", null, counts.Total.Categories, counts.Total.Replies, counts.Total.Threads);
        return table.Render();
    }

    private static void Rows(HumanTable table, string? name, string? id, IReadOnlyList<ReplyCategoryCount> categories, int replies, int threads)
    {
        foreach (var category in categories)
        {
            table.Row(name, id, category.Category ?? NoCategory, Number(category.Replies), Number(category.Threads));
        }

        table.Row(name, id, AllCategories, Number(replies), Number(threads));
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
