using System.Text.Json.Nodes;
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
        if (contact.Campaigns is { Count: > 0 } campaigns)
        {
            var table = new HumanTable("CAMPAIGN", "NAME", "STATE", "ADDED");
            foreach (var campaign in campaigns)
            {
                table.Row(campaign.CampaignId, campaign.CampaignName, RenderText.Snake(campaign.State), RenderText.Moment(campaign.AddedAt));
            }

            lines.Add(string.Empty);
            lines.Add(table.Render());
        }

        lines.AddRange(RenderText.ExternalIds(contact.ExternalIds));
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

        var table = new HumanTable("ID", "NAME", "COMPANY", "EMAIL", "TIME ZONE", "LAST");
        foreach (var contact in page.Items)
        {
            table.Row(
                contact.Id,
                RenderText.Name(contact.FirstName, contact.LastName),
                contact.Company,
                RenderText.Email(contact.Channels),
                contact.TimeZone,
                contact.LastActivity is { } last ? $"{last.Kind} {RenderText.Moment(last.Ts)}" : null);
        }

        return RenderText.WithCursor(table.Render(), page.NextCursor);
    }

    /// <summary>A page of a person's threads as the provider listed them.</summary>
    public static string? Threads(string json)
    {
        var page = RenderText.Read<ContactThreadsResponse>(json);
        if (page?.Items is null)
        {
            return null;
        }

        var table = new HumanTable("THREAD", "CHANNEL", "SUBJECT", "LAST ACTIVITY", "UNREAD");
        foreach (var thread in page.Items.OfType<JsonObject>())
        {
            table.Row(
                Shown(thread["external_id"]),
                Shown(thread["channel"]),
                Shown(thread["subject"]) ?? Shown(thread["preview"]),
                Shown(thread["last_activity_at"]),
                thread["unread"]?.GetValue<bool>() == true ? "yes" : null);
        }

        return page.HasMore ? table.Render() + Environment.NewLine + "more threads: ask again with --offset" : table.Render();
    }

    /// <summary>
    /// One thread: what it is, then each message with who wrote it to whom and when, and its body as the provider holds
    /// it, less the control characters that would drive the terminal instead of being printed on it.
    /// </summary>
    public static string? Thread(string json)
    {
        var read = RenderText.Read<ContactThreadResponse>(json);
        if (read?.Thread is null || read.Messages is null)
        {
            return null;
        }

        var lines = new List<string>
        {
            Line("Thread:", Shown(read.Thread["external_id"])),
            Line("Channel:", Shown(read.Thread["channel"])),
            Line("Subject:", Shown(read.Thread["subject"])),
        };
        foreach (var message in read.Messages.OfType<JsonObject>())
        {
            var to = message["to"] is JsonArray recipients ? string.Join(", ", recipients.Select(Shown)) : null;
            lines.Add(string.Empty);
            lines.Add($"{Shown(message["sent_at"])}  {Shown(message["from"])} -> {to}");
            lines.Add(Shown(message["body"]) ?? string.Empty);
        }

        if (read.HasMore)
        {
            lines.Add(string.Empty);
            lines.Add("more messages: ask again with --offset");
        }

        return RenderText.Lines(lines);
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

    /// <summary>
    /// Text a provider wrote, as it may be printed: without the C0 and C1 control characters but the line feed and the
    /// tab, so an escape sequence in a subject or a body is shown as the rest of its characters and never runs.
    /// </summary>
    private static string? Shown(JsonNode? node) =>
        (string?)node is { } text ? string.Concat(text.Where(c => c is '\n' or '\t' || !char.IsControl(c))) : null;

    private static string Line(string label, string? value) => label.PadRight(Label) + (string.IsNullOrEmpty(value) ? "-" : value);
}
