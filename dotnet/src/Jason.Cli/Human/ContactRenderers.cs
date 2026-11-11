using System.Text;
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
            var table = new HumanTable("CAMPAIGN", "NAME", "STATE", "STANDING", "ADDED");
            foreach (var campaign in campaigns)
            {
                table.Row(campaign.CampaignId, campaign.CampaignName, RenderText.Snake(campaign.State), Standing(campaign), RenderText.Moment(campaign.AddedAt));
            }

            lines.Add(string.Empty);
            lines.Add(table.Render());
        }

        lines.AddRange(RenderText.ExternalIds(contact.ExternalIds));
        return RenderText.Lines(lines);
    }

    /// <summary>A page of contacts, recognizable by name, company and address, with where each stands in each campaign.</summary>
    public static string? ContactList(string json)
    {
        var page = RenderText.Read<Page<ContactDto>>(json);
        if (page?.Items is null)
        {
            return null;
        }

        var table = new HumanTable("ID", "NAME", "COMPANY", "EMAIL", "TIME ZONE", "CAMPAIGNS", "LAST");
        foreach (var contact in page.Items)
        {
            table.Row(
                contact.Id,
                RenderText.Name(contact.FirstName, contact.LastName),
                contact.Company,
                RenderText.Email(contact.Channels),
                contact.TimeZone,
                contact.Campaigns is { Count: > 0 } campaigns
                    ? string.Join("; ", campaigns.Select(campaign => campaign.Standing is { Count: > 0 } ? $"{campaign.CampaignName}: {Standing(campaign)}" : campaign.CampaignName))
                    : null,
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
                Shown(thread["subject"]) ?? OneLine(thread["preview"]),
                Shown(thread["last_activity_at"]),
                thread["unread"]?.GetValue<bool>() == true ? "yes" : null);
        }

        return page.HasMore ? table.Render() + Environment.NewLine + "more threads: ask again with --offset" : table.Render();
    }

    /// <summary>
    /// One thread: what it is, then each message with who wrote it to whom and when, and its body as the provider holds
    /// it, each line shown by <see cref="RenderText.ShownLines"/>.
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
            lines.AddRange(RenderText.ShownLines((string?)message["body"] ?? string.Empty));
        }

        if (read.HasMore)
        {
            lines.Add(string.Empty);
            lines.Add("more messages: ask again with --offset");
        }

        return RenderText.Lines(lines);
    }

    /// <summary>The counts of campaign summary the person stands in there, in the counts' order, or nothing.</summary>
    private static string? Standing(ContactCampaignDto campaign) =>
        campaign.Standing is { Count: > 0 } counts ? string.Join(", ", counts.Select(RenderText.Snake)) : null;

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
    /// The start of a message, which a thread with no subject is listed by: its line breaks are ordinary, so each run of
    /// them becomes one space in a one-line column, as a question's excerpt folds its white space, and the rest is shown by
    /// <see cref="Shown(JsonNode?)"/>.
    /// </summary>
    private static string? OneLine(JsonNode? node)
    {
        if ((string?)node is not { } text)
        {
            return null;
        }

        var folded = new StringBuilder(text.Length);
        var inBreak = false;
        foreach (var character in text)
        {
            if (character is (char)0x0A or (char)0x0D or (char)0x85 or (char)0x2028 or (char)0x2029)
            {
                if (!inBreak)
                {
                    folded.Append(' ');
                }

                inBreak = true;
            }
            else
            {
                folded.Append(character);
                inBreak = false;
            }
        }

        return RenderText.Shown(folded.ToString());
    }

    /// <summary>Text a provider wrote, printed by the rule the thread views hold for it (<see cref="RenderText.Shown"/>).</summary>
    private static string? Shown(JsonNode? node) => RenderText.Shown((string?)node);

    private static string Line(string label, string? value) => label.PadRight(Label) + (string.IsNullOrEmpty(value) ? "-" : RenderText.Shown(value));
}
