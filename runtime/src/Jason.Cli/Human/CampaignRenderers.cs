using Jason.Contracts.Api;

namespace Jason.Cli.Human;

/// <summary>
/// <c>--human</c> rendering for the campaign verbs. Every renderer returns null when the body is not what it
/// expects, and the runner falls back to printing the response as it came.
/// </summary>
public static class CampaignRenderers
{
    private const int Label = 14;

    /// <summary>One campaign: its attributes as lines, and the context by key — the values themselves are in the JSON output.</summary>
    public static string? Campaign(string json)
    {
        var campaign = RenderText.Read<CampaignDto>(json);
        if (campaign?.Id is null || campaign.Name is null)
        {
            return null;
        }

        var lines = new List<string>
        {
            Line("Id:", campaign.Id),
            Line("Name:", campaign.Name),
            Line("Status:", RenderText.Snake(campaign.Status)),
            Line("Created:", RenderText.Moment(campaign.CreatedAt)),
            Line("Updated:", RenderText.Moment(campaign.UpdatedAt)),
        };

        if (campaign.ArchivedAt is not null)
        {
            lines.Add(Line("Archived:", RenderText.Moment(campaign.ArchivedAt.Value)));
        }

        // "none" rather than a blank, the same word this renderer uses for a campaign that carries no context.
        lines.Add(Line("Profile:", campaign.ExecutionProfile ?? "none"));
        lines.Add(Line("Context keys:", RenderText.Keys(campaign.Context)));
        lines.AddRange(RenderText.ExternalIds(campaign.ExternalIds));
        return RenderText.Lines(lines);
    }

    /// <summary>A page of campaigns, and the cursor that continues it.</summary>
    public static string? CampaignList(string json)
    {
        var page = RenderText.Read<Page<CampaignSummaryDto>>(json);
        if (page?.Items is null)
        {
            return null;
        }

        var table = new HumanTable("ID", "NAME", "STATUS", "CREATED");
        foreach (var campaign in page.Items)
        {
            table.Row(campaign.Id, campaign.Name, RenderText.Snake(campaign.Status), RenderText.Moment(campaign.CreatedAt));
        }

        return RenderText.WithCursor(table.Render(), page.NextCursor);
    }

    /// <summary>A page of memberships: who is in the campaign, where they stand, and how to recognize them.</summary>
    public static string? Members(string json)
    {
        var page = RenderText.Read<Page<MembershipItemDto>>(json);
        if (page?.Items is null)
        {
            return null;
        }

        var table = new HumanTable("CONTACT", "NAME", "STATE", "EMAIL");
        foreach (var member in page.Items)
        {
            table.Row(
                member.Contact?.Id,
                RenderText.Name(member.Contact?.FirstName, member.Contact?.LastName),
                RenderText.Snake(member.State),
                RenderText.Email(member.Contact?.Channels));
        }

        return RenderText.WithCursor(table.Render(), page.NextCursor);
    }

    /// <summary>The outcome of an import: the counts, then every item the runtime refused and why.</summary>
    public static string? Batch(string json)
    {
        var result = RenderText.Read<AddContactsResult>(json);
        if (result?.Summary is null || result.Items is null)
        {
            return null;
        }

        var lines = new List<string>
        {
            $"added {result.Summary.Added}, already members {result.Summary.AlreadyMember}, rejected {result.Summary.Rejected}",
        };
        lines.AddRange(result.Items
            .Where(item => item.Status == AddContactsItemStatus.Rejected)
            .Select(item => Rejected(item.Index, item.Error)));

        return RenderText.Lines(lines);
    }

    /// <summary>The outcome of a removal, counted the way the API counts it.</summary>
    public static string? RemoveBatch(string json)
    {
        var result = RenderText.Read<RemoveContactsResult>(json);
        if (result?.Summary is null || result.Items is null)
        {
            return null;
        }

        var lines = new List<string>
        {
            $"removed {result.Summary.Removed}, not members {result.Summary.NotMember}, already excluded {result.Summary.AlreadyExcluded}, rejected {result.Summary.Rejected}",
        };
        lines.AddRange(result.Items
            .Where(item => item.Status == RemoveContactsItemStatus.Rejected)
            .Select(item => Rejected(item.Index, item.Error)));

        return RenderText.Lines(lines);
    }

    private static string Rejected(int index, ErrorBody? error) =>
        $"#{index}: {error?.Code ?? "rejected"}{(error?.Message is null ? string.Empty : " — " + error.Message)}";

    private static string Line(string label, string value) => label.PadRight(Label) + value;
}
