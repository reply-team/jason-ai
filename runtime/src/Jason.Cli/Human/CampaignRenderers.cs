using System.Globalization;
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
            Line("Project:", $"{campaign.ProjectSlug} ({campaign.ProjectId})"),
            Line("Created:", RenderText.Moment(campaign.CreatedAt)),
            Line("Updated:", RenderText.Moment(campaign.UpdatedAt)),
        };

        if (campaign.ArchivedAt is not null)
        {
            lines.Add(Line("Archived:", RenderText.Moment(campaign.ArchivedAt.Value)));
        }

        // "none" rather than a blank, the same word this renderer uses for a campaign that carries no context.
        lines.Add(Line("Profile:", campaign.ExecutionProfile ?? "none"));

        // Only when this campaign has asked for its own pace. "Every 18000 s" printed under every campaign
        // would be the installation's setting wearing the campaign's clothes.
        if (campaign.ReviewSeconds is { } seconds)
        {
            lines.Add(Line("Review:", string.Create(CultureInfo.InvariantCulture, $"every {seconds} s")));
        }

        // Only for a campaign created under a key: the key its creator chose, which a repeat of that create sends again.
        if (campaign.IdempotencyKey is { } key)
        {
            lines.Add(Line("Key:", key));
        }
        lines.Add(Line("Context keys:", RenderText.Keys(campaign.Context)));
        lines.AddRange(RenderText.ExternalIds(campaign.ExternalIds));
        return RenderText.Lines(lines);
    }

    /// <summary>
    /// A page of campaigns, and the cursor that continues it. Each campaign's project is shown by the slug the listing
    /// carries, so a listing is one request however many projects it spans.
    /// </summary>
    public static string? CampaignList(string json)
    {
        var page = RenderText.Read<Page<CampaignSummaryDto>>(json);
        if (page?.Items is null)
        {
            return null;
        }

        var table = new HumanTable("ID", "NAME", "STATUS", "PROJECT", "CREATED");
        foreach (var campaign in page.Items)
        {
            table.Row(campaign.Id, campaign.Name, RenderText.Snake(campaign.Status), campaign.ProjectSlug, RenderText.Moment(campaign.CreatedAt));
        }

        return RenderText.WithCursor(table.Render(), page.NextCursor);
    }

    /// <summary>
    /// A listing the CLI scoped to the project of the folder it runs in, said above its rows, so a person reading it knows
    /// why it holds one project's campaigns.
    /// </summary>
    public static string ScopedByFolder(string project, string rows) =>
        RenderText.Lines([$"Project: {RenderText.Shown(project)}, from the working directory (--all-projects lists every project's)", string.Empty, rows]);

    /// <summary>
    /// A page of the campaigns' people: who they are, where they stand, how to recognize them, the campaigns that list them
    /// and, under a count, what it counts them for.
    /// </summary>
    public static string? Members(string json)
    {
        var page = RenderText.Read<Page<MembershipItemDto>>(json);
        if (page?.Items is null)
        {
            return null;
        }

        var table = new HumanTable("CONTACT", "NAME", "STATE", "EMAIL", "CAMPAIGNS", "WHY");
        foreach (var member in page.Items)
        {
            var names = (member.Contact?.Campaigns ?? []).ToDictionary(campaign => campaign.CampaignId, campaign => campaign.CampaignName, StringComparer.Ordinal);
            table.Row(
                member.Contact?.Id,
                RenderText.Name(member.Contact?.FirstName, member.Contact?.LastName),
                RenderText.Snake(member.State),
                RenderText.Email(member.Contact?.Channels),
                string.Join(", ", (member.Counted ?? []).Select(counted => names.GetValueOrDefault(counted.CampaignId, counted.CampaignId))),
                member.Why is { } why ? string.Join(" ", new[] { why.Text ?? why.Code, RenderText.Moment(why.At) }.Where(part => part is not null)) : null);
        }

        return RenderText.WithCursor(table.Render(), page.NextCursor);
    }

    /// <summary>
    /// Where each campaign's people stand now, and the total across them; the provider's two counts are a dash while no
    /// sequence is linked. Each campaign is named by its name, from <paramref name="names"/>, with its id beside it.
    /// </summary>
    public static string? Summary(string json, IReadOnlyDictionary<string, string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        var summary = RenderText.Read<CampaignSummaryResponse>(json);
        if (summary?.Campaigns is null)
        {
            return null;
        }

        var table = new HumanTable("CAMPAIGN", "ID", "PEOPLE", "WAITING", "FAILED", "WORKING", "NEW", "IDLE", "SEQUENCE", "OUT", "REMOVED");
        foreach (var row in summary.Campaigns)
        {
            table.Row(
                names.GetValueOrDefault(row.CampaignId),
                row.CampaignId,
                Count(row.People),
                Count(row.Waiting),
                Count(row.Failed),
                Count(row.Working),
                Count(row.New),
                Count(row.Idle),
                row.SequenceLinked ? Count(row.Sequence) : "-",
                row.SequenceLinked ? Count(row.Out) : "-",
                Count(row.Removed));
        }

        if (summary.Total is { } total)
        {
            // With no campaign linked to a sequence, the total has no sequence to count either, as each row says.
            var linked = summary.Campaigns.Any(row => row.SequenceLinked);
            table.Row(
                "TOTAL", null, Count(total.People), Count(total.Waiting), Count(total.Failed), Count(total.Working), Count(total.New), Count(total.Idle),
                linked ? Count(total.Sequence) : "-", linked ? Count(total.Out) : "-", Count(total.Removed));
        }

        return table.Render();
    }

    /// <summary>
    /// What happened to each campaign's people in the period asked about, and the total across them, each campaign named
    /// as <see cref="Summary"/> names it.
    /// </summary>
    public static string? Activity(string json, IReadOnlyDictionary<string, string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        var activity = RenderText.Read<CampaignActivityResponse>(json);
        if (activity?.Campaigns is null)
        {
            return null;
        }

        var table = new HumanTable("CAMPAIGN", "ID", "PEOPLE", "ADDED", "WORKED ON", "CONTACTED", "REPLIED", "OPTED OUT", "REMOVED");
        foreach (var row in activity.Campaigns)
        {
            table.Row(
                names.GetValueOrDefault(row.CampaignId), row.CampaignId, Count(row.People), Count(row.Added), Count(row.WorkedOn), Count(row.Contacted), Count(row.Replied), Count(row.OptedOut),
                Count(row.Removed));
        }

        if (activity.Total is { } total)
        {
            table.Row(
                "TOTAL", null, Count(total.People), Count(total.Added), Count(total.WorkedOn), Count(total.Contacted), Count(total.Replied), Count(total.OptedOut),
                Count(total.Removed));
        }

        return table.Render();
    }

    /// <summary>One count over time: each bucket's span and the people it counts.</summary>
    public static string? ActivityByPeriod(string json)
    {
        var series = RenderText.Read<CampaignActivityByPeriodResponse>(json);
        if (series?.Buckets is null)
        {
            return null;
        }

        var table = new HumanTable("FROM", "TO", "PEOPLE");
        foreach (var bucket in series.Buckets)
        {
            table.Row(RenderText.Moment(bucket.Start), RenderText.Moment(bucket.End), Count(bucket.Value));
        }

        return table.Render();
    }

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

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
        $"#{index}: {error?.Code ?? "rejected"}{(error?.Message is null ? string.Empty : " — " + RenderText.Shown(error.Message))}";

    private static string Line(string label, string value) => label.PadRight(Label) + RenderText.Shown(value);
}
