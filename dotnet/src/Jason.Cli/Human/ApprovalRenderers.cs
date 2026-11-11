using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Operations;

namespace Jason.Cli.Human;

/// <summary>
/// <c>--human</c> rendering for the approval verbs. Every renderer returns null when the body is not what it
/// expects, and the runner falls back to printing the response as it came.
/// </summary>
public static class ApprovalRenderers
{
    private const int Label = 15;

    /// <summary>What is waiting, as a table: the work, what it would do, and how long it has been asked.</summary>
    public static string? ApprovalList(string json)
    {
        var page = RenderText.Read<Page<ApprovalSummaryDto>>(json);
        if (page?.Items is null)
        {
            return null;
        }

        var table = new HumanTable("REQUESTED", "OPERATION", "STATUS", "WORK ITEM", "ID");
        foreach (var approval in page.Items)
        {
            table.Row(
                RenderText.Moment(approval.RequestedAt),
                approval.Operation,
                RenderText.Snake(approval.Status),
                approval.WorkItemId,
                approval.Id);
        }

        return RenderText.WithCursor(table.Render(), page.NextCursor);
    }

    /// <summary>
    /// One decision, with what it would do first: a person reads the effect, the person it reaches and the
    /// account it acts through before anything about identifiers, because that is the order in which the
    /// decision is actually made.
    /// </summary>
    public static string? Approval(string json)
    {
        var approval = RenderText.Read<ApprovalDto>(json);
        if (approval?.Id is null || approval.Operation is null)
        {
            return null;
        }

        var preview = approval.Preview;
        var text = new StringBuilder();

        // The one line the operation's own template renders, where it declares one: what would be done, to whom
        // and how, read before the properties it is drawn from. A preview without one prints as it always did.
        if ((string?)preview?["summary"] is { Length: > 0 } summary)
        {
            text.AppendLine(Line("Summary:", summary));
        }

        text.AppendLine(Line("Operation:", approval.Operation));
        text.AppendLine(Line("Intent:", (string?)preview?["intent"]));
        text.AppendLine(Line("Reach:", Property(preview?["reach"])));
        text.AppendLine(Line("Undo:", Property(preview?["reversibility"])));
        text.AppendLine(Line("Cost:", Property(preview?["cost"])));
        text.AppendLine(Line("Campaign:", Named(preview?["campaign"])));
        text.AppendLine(Line("Contact:", Person(preview?["contact"])));
        text.AppendLine(Line("Plugin:", approval.PluginId));
        text.AppendLine(Line("Account:", approval.BindingIdentity));
        AppendExample(text, preview?["example"]);
        text.AppendLine(Line("Status:", RenderText.Snake(approval.Status)));
        text.AppendLine(Line("Parked:", approval.Reason));
        text.AppendLine(Line("Requested:", RenderText.Moment(approval.RequestedAt)));
        text.AppendLine(Line("Decided:", RenderText.Moment(approval.DecidedAt)));
        text.AppendLine(Line("Decided by:", RenderText.Actor(approval.DecidedBy)));
        text.AppendLine(Line("Note:", approval.DecisionReason));
        text.AppendLine(Line("Work item:", approval.WorkItemId));
        text.AppendLine(Line("Content hash:", approval.SubjectHash));
        text.Append(Line("Id:", approval.Id));
        return text.ToString();
    }

    /// <summary>
    /// The one reading of a template that the approval holds, printed as the runtime rendered it. It is somebody's
    /// message, so a character that changes how the line around it reads (see
    /// <see cref="PreviewTemplate.ChangesTheLine"/>) is shown as U+FFFD, a mark that can be seen, rather than written;
    /// a tab, the joiners, the soft hyphen and the direction marks stay (see <see cref="RenderText.Shown"/>, which also says what the rule
    /// leaves to the writer). The subject is one line and its line breaks are such characters; the body keeps its own,
    /// each of its lines indented under the label so that none reads as a labelled line of the approval. An empty
    /// subject says so. A preview without an example adds nothing.
    /// </summary>
    private static void AppendExample(StringBuilder text, JsonNode? example)
    {
        if (example is not JsonObject)
        {
            return;
        }

        var subject = (string?)example["subject"];
        text.AppendLine(Line("Example:", "one reading of the template, as the runtime renders it"));
        text.AppendLine(Indented("Subject:", subject is { Length: 0 } ? "(empty)" : RenderText.Shown(subject)));
        text.AppendLine("  Body:");
        foreach (var line in RenderText.ShownLines((string?)example["body"] ?? string.Empty))
        {
            text.AppendLine(line.Length == 0 ? string.Empty : "    " + line);
        }

        if (example["unfilled"] is JsonArray { Count: > 0 } unfilled)
        {
            text.AppendLine(Indented("Unfilled:", string.Join(", ", unfilled.Select(name => (string?)name))));
        }

        text.AppendLine("  You approve the template as written; the example is the runtime's rendering, not the provider's, which may differ.");
    }

    /// <summary>A published property as a person reads it: the dangerous value, and the condition beside it.</summary>
    private static string? Property(JsonNode? property) =>
        property is null
            ? null
            : (string?)property["value"] + ((bool?)property["conditional"] == true ? " (conditional)" : string.Empty);

    // A campaign's and a contact's names are refused a control character when they are written, but a name may still
    // hold a format character such as a direction override, and one written before that rule may hold anything, so every
    // part of these two lines goes through the rule the example uses.
    private static string? Named(JsonNode? entity) =>
        entity is null ? null : $"{RenderText.Shown((string?)entity["name"])} ({RenderText.Shown((string?)entity["id"])})";

    private static string? Person(JsonNode? contact)
    {
        if (contact is null)
        {
            return null;
        }

        var where = RenderText.Shown((string?)contact["value"]);
        var channel = RenderText.Shown((string?)contact["channel"]);
        var name = RenderText.Shown((string?)contact["name"]);
        var id = RenderText.Shown((string?)contact["id"]);

        // A contact known only by an address has no name to set its id apart from: the id stands where the name would.
        var who = string.IsNullOrEmpty(name) ? id : $"{name} ({id})";
        return who + (where is null ? string.Empty : $" | {channel} {where}");
    }

    /// <summary>One standing approval: what it covers, its limits, and whether it still lets drafts go.</summary>
    public static string? StandingApproval(string json)
    {
        var standing = RenderText.Read<StandingApprovalDto>(json);
        if (standing?.CampaignId is null || (standing.Category is null && standing.Operations is null))
        {
            return null;
        }

        var text = new StringBuilder();
        if (standing.Category is not null)
        {
            text.AppendLine(Line("Category:", standing.Category));
        }

        text.AppendLine(Line("Campaign:", standing.CampaignId));
        text.AppendLine(Line("Covers:", Covers(standing)));
        if (standing.Category is null)
        {
            text.AppendLine(Line("Audience:", Audience(standing, withId: true)));
            text.AppendLine(Line("People:", People(standing)));
            text.AppendLine(Line("Per search:", standing.MaxPerSearch?.ToString(CultureInfo.InvariantCulture)));
        }

        text.AppendLine(Line("In force:", standing.InForce ? "yes" : "no"));
        text.AppendLine(Line("Expires:", RenderText.Moment(standing.ExpiresAt)));
        text.AppendLine(Line("Let go:", Released(standing)));
        text.AppendLine(Line("Set by:", RenderText.Actor(standing.SetBy)));
        text.AppendLine(Line("Set:", RenderText.Moment(standing.SetAt)));
        text.Append(Line("Line:", standing.LineId));
        return text.ToString();
    }

    /// <summary>A campaign's standing approvals, as a table, one category to a row.</summary>
    public static string? StandingApprovalList(string json)
    {
        var page = RenderText.Read<Page<StandingApprovalDto>>(json);
        if (page?.Items is null)
        {
            return null;
        }

        var table = new HumanTable("CATEGORY", "COVERS", "AUDIENCE", "PEOPLE", "IN FORCE", "EXPIRES", "LET GO", "SET BY");
        foreach (var standing in page.Items)
        {
            table.Row(
                standing.Category,
                Covers(standing),
                Audience(standing, withId: false),
                People(standing),
                standing.InForce ? "yes" : "no",
                RenderText.Moment(standing.ExpiresAt),
                Released(standing),
                RenderText.Actor(standing.SetBy));
        }

        return RenderText.WithCursor(table.Render(), page.NextCursor);
    }

    private static string? Covers(StandingApprovalDto standing) =>
        standing.Operations is null ? null : string.Join(", ", standing.Operations);

    /// <summary>The allowance's audience by its number, and its id where there is room; nothing for a category.</summary>
    private static string? Audience(StandingApprovalDto standing, bool withId) => standing.Audience is not { } audience
        ? null
        : withId
            ? string.Create(CultureInfo.InvariantCulture, $"{audience.Number} ({RenderText.Shown(audience.Id)})")
            : audience.Number.ToString(CultureInfo.InvariantCulture);

    /// <summary>The people the allowance's searches asked for, against its ceiling; nothing for a category.</summary>
    private static string? People(StandingApprovalDto standing) => standing.MaxPeople is { } ceiling
        ? string.Create(CultureInfo.InvariantCulture, $"{standing.People ?? 0} of {ceiling}")
        : null;

    private static string Released(StandingApprovalDto standing) => standing.MaxReleases is { } ceiling
        ? string.Create(CultureInfo.InvariantCulture, $"{standing.Released} of {ceiling}")
        : standing.Released.ToString(CultureInfo.InvariantCulture);

    private static string Line(string label, string? value) => label.PadRight(Label) + (string.IsNullOrEmpty(value) ? "-" : RenderText.Shown(value));

    private static string Indented(string label, string? value) => "  " + label.PadRight(Label - 2) + (string.IsNullOrEmpty(value) ? "-" : value);
}
