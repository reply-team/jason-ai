using System.Text;
using Jason.Contracts.Api;

namespace Jason.Cli.Human;

/// <summary>
/// <c>--human</c> rendering for the note verbs. Every renderer returns null when the body is not what it
/// expects, and the runner falls back to printing the response as it came.
/// </summary>
public static class RoleNoteRenderers
{
    /// <summary>
    /// The one sentence that has to survive every rendering of a note. Somebody reading a campaign must never
    /// take one of these for what the runtime knows: it is what a role wrote down for itself, and where it
    /// disagrees with the campaign the campaign is right.
    /// </summary>
    public const string Heading = "A role's own note — its working memory, not what the runtime knows.";

    private const int Label = 12;

    public static string? Note(string json)
    {
        var note = RenderText.Read<RoleNoteDto>(json);
        if (note?.Role is null || note.Note is null)
        {
            return null;
        }

        var text = new StringBuilder();
        text.AppendLine(Heading);
        text.AppendLine();
        text.AppendLine(Line("Campaign:", note.CampaignId));
        text.AppendLine(Line("Role:", note.Role));
        text.AppendLine(Line("Written:", RenderText.Moment(note.UpdatedAt) ?? "never"));
        text.AppendLine(Line("By:", RenderText.Actor(note.UpdatedBy)));
        text.AppendLine(Line("Size:", note.NoteBytes + " bytes"));
        text.AppendLine(Line("Hash:", RenderText.Digest(note.NoteHash)));
        text.AppendLine();
        text.Append(note.Note.ToJsonString(Indented));
        return text.ToString();
    }

    /// <summary>
    /// A campaign's memory at a glance. The listing carries no note, so this table cannot show one: what it
    /// shows is which roles have written and how much, which is what somebody scanning a campaign is asking.
    /// </summary>
    public static string? NoteList(string json)
    {
        var page = RenderText.Read<Page<RoleNoteSummaryDto>>(json);
        if (page?.Items is null)
        {
            return null;
        }

        var table = new HumanTable("ROLE", "BYTES", "WRITTEN", "BY", "HASH");
        foreach (var note in page.Items)
        {
            table.Row(
                note.Role,
                note.NoteBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
                RenderText.Moment(note.UpdatedAt),
                RenderText.Actor(note.UpdatedBy),
                RenderText.Digest(note.NoteHash));
        }

        return Heading + Environment.NewLine + Environment.NewLine + RenderText.WithCursor(table.Render(), page.NextCursor);
    }

    private static System.Text.Json.JsonSerializerOptions Indented { get; } = new(Contracts.Json.JasonJson.Options) { WriteIndented = true };

    private static string Line(string label, string? value) => label.PadRight(Label) + (string.IsNullOrEmpty(value) ? "-" : value);
}
