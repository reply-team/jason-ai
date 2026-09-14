using Jason.Contracts.Api;

namespace Jason.Cli.Human;

/// <summary>
/// <c>--human</c> rendering for the journal verbs. The chronicle is meant to be read, so a listing is one row
/// per entry and a single entry shows what was recorded, values included.
/// </summary>
public static class JournalRenderers
{
    private const int Label = 11;

    /// <summary>One entry, with the values it carries: a caller who just appended one wants to see it back.</summary>
    public static string? Entry(string json)
    {
        var entry = RenderText.Read<JournalEntryDto>(json);
        if (entry?.Id is null || entry.Kind is null)
        {
            return null;
        }

        var lines = new List<string>
        {
            Line("Id:", entry.Id),
            Line("When:", RenderText.Moment(entry.Ts)),
            Line("Kind:", entry.Kind),
            Line("Actor:", RenderText.Actor(entry.Actor)),
            Line("Campaign:", entry.CampaignId),
            Line("Key:", entry.Key),
            Line("Reason:", entry.Reason),
        };

        if (entry.Old is not null)
        {
            lines.Add(Line("Old:", RenderText.Compact(entry.Old)));
        }

        if (entry.New is not null)
        {
            lines.Add(Line("New:", RenderText.Compact(entry.New)));
        }

        return RenderText.Lines(lines);
    }

    /// <summary>A page of the chronicle, newest first as the runtime returns it.</summary>
    public static string? EntryList(string json)
    {
        var page = RenderText.Read<Page<JournalEntryDto>>(json);
        if (page?.Items is null)
        {
            return null;
        }

        var table = new HumanTable("WHEN", "KIND", "ACTOR", "KEY", "REASON");
        foreach (var entry in page.Items)
        {
            table.Row(RenderText.Moment(entry.Ts), entry.Kind, RenderText.Actor(entry.Actor), entry.Key, entry.Reason);
        }

        return RenderText.WithCursor(table.Render(), page.NextCursor);
    }

    private static string Line(string label, string? value) => label.PadRight(Label) + (string.IsNullOrEmpty(value) ? "-" : value);
}
