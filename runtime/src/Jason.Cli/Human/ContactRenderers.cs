using Jason.Contracts.Api;

namespace Jason.Cli.Human;

/// <summary>
/// <c>--human</c> rendering for the contact verbs. A renderer returns null when the body is not what it
/// expects, and the runner falls back to printing the response as it came.
/// </summary>
public static class ContactRenderers
{
    private const int Label = 14;

    /// <summary>One contact: who they are as lines, then the ways to reach them as a table.</summary>
    public static string? Contact(string json)
    {
        var contact = RenderText.Read<ContactDto>(json);
        if (contact?.Id is null)
        {
            return null;
        }

        var lines = new List<string>
        {
            Line("Id:", contact.Id),
            Line("Name:", RenderText.Name(contact.FirstName, contact.LastName)),
            Line("Company:", contact.Company),
            Line("Title:", contact.Title),
            Line("Time zone:", contact.TimeZone),
            Line("Created:", RenderText.Moment(contact.CreatedAt)),
            Line("Updated:", RenderText.Moment(contact.UpdatedAt)),
        };

        if (contact.ArchivedAt is not null)
        {
            lines.Add(Line("Archived:", RenderText.Moment(contact.ArchivedAt.Value)));
        }

        lines.Add(Line("Custom keys:", RenderText.Keys(contact.Custom)));
        lines.Add(string.Empty);
        lines.Add(Channels(contact.Channels));
        return RenderText.Lines(lines);
    }

    /// <summary>A page of contacts, recognizable by name, company and address.</summary>
    public static string? ContactList(string json)
    {
        var page = RenderText.Read<Page<ContactDto>>(json);
        if (page?.Items is null)
        {
            return null;
        }

        var table = new HumanTable("ID", "NAME", "COMPANY", "EMAIL", "TIME ZONE");
        foreach (var contact in page.Items)
        {
            table.Row(
                contact.Id,
                RenderText.Name(contact.FirstName, contact.LastName),
                contact.Company,
                RenderText.Email(contact.Channels),
                contact.TimeZone);
        }

        return RenderText.WithCursor(table.Render(), page.NextCursor);
    }

    private static string Channels(IReadOnlyList<ChannelDto>? channels)
    {
        var table = new HumanTable("CHANNEL", "VALUE", "LABEL", "PRIMARY");
        if (channels is not null)
        {
            foreach (var channel in channels)
            {
                table.Row(channel.Channel, channel.Value, channel.Label, channel.Primary ? "yes" : null);
            }
        }

        return table.Render();
    }

    private static string Line(string label, string? value) => label.PadRight(Label) + (string.IsNullOrEmpty(value) ? "-" : value);
}
